use log::info;
use sysinfo::{System, SystemExt, CpuExt, ProcessExt, DiskExt, PidExt};

pub struct SandboxDetector;

impl SandboxDetector {
    pub fn detect() -> (bool, Vec<String>) {
        let mut flags = Vec::new();
        let mut score = 0;

        let mut sys = System::new_all();
        sys.refresh_all();

        // 1. CPU count
        let cpus = sys.cpus().len();
        if cpus < 2 {
            flags.push(format!("Low CPU count: {}", cpus));
            score += 2;
        }

        // 2. RAM
        let ram_mb = sys.total_memory() / (1024 * 1024);
        if ram_mb < 2048 {
            flags.push(format!("Low RAM: {}MB", ram_mb));
            score += 2;
        }

        // 3. Disk size
        let total_disk_gb: u64 = sys.disks().iter().map(|d| d.total_space()).sum::<u64>() / (1024 * 1024 * 1024);
        if total_disk_gb < 60 {
            flags.push(format!("Small disk: {}GB", total_disk_gb));
            score += 1;
        }

        // 4. VM processes
        let vm_names: &[&str] = &[
            "vmtoolsd", "vmwaretray", "vmwareuser", "vmusrvc", "vboxtray",
            "vboxservice", "vboxclient", "qemu-ga", "xenservice", "prl_tools",
            "prl_cc", "virtio", "spice-vdagent",
        ];
        let mut vm_found = Vec::new();
        for (_pid, process) in sys.processes() {
            let name = process.name().to_lowercase();
            for &vm in vm_names {
                if name.contains(vm) && !vm_found.contains(&vm.to_string()) {
                    vm_found.push(vm.to_string());
                }
            }
        }
        if !vm_found.is_empty() {
            flags.push(format!("VM processes: {}", vm_found.join(", ")));
            score += 2;
        }

        // 5. Analysis tools
        let tool_names: &[&str] = &[
            "wireshark", "procmon", "procmon64", "procexp", "procexp64",
            "x64dbg", "x32dbg", "ollydbg", "ida", "ida64", "idaq", "idaq64",
            "fiddler", "processhacker", "pestudio", "autoruns", "autoruns64",
            "regmon", "filemon", "tcpview", "apimonitor", "dnspy",
        ];
        let mut tools_found = Vec::new();
        for (_pid, process) in sys.processes() {
            let name = process.name().to_lowercase();
            for &tool in tool_names {
                if name.contains(tool) && !tools_found.contains(&tool.to_string()) {
                    tools_found.push(tool.to_string());
                }
            }
        }
        if !tools_found.is_empty() {
            flags.push(format!("Analysis tools: {}", tools_found.join(", ")));
            score += 3;
        }

        // 6. Uptime
        let uptime_min = sys.uptime() / 60;
        if uptime_min < 10 {
            flags.push(format!("Low uptime: {}min", uptime_min));
            score += 1;
        }

        // 7. Recent files count
        let recent = recent_files_count();
        if recent < 20 {
            flags.push(format!("Few recent files: {}", recent));
            score += 1;
        }

        // 8. Debugger
        let debugger = is_debugger_present();
        if debugger {
            flags.push("Debugger detected".to_string());
            score += 3;
        }

        let is_sandbox = score >= 4;
        info!(
            "Sandbox detection: score={}, sandbox={}, flags={:?}",
            score, is_sandbox, flags
        );
        (is_sandbox, flags)
    }
}

fn recent_files_count() -> usize {
    let mut count = 0;
    let dirs_to_check: Vec<Option<std::path::PathBuf>> = vec![
        dirs::desktop_dir(),
        dirs::document_dir(),
    ];
    for dir in dirs_to_check.into_iter().flatten() {
        if let Ok(entries) = std::fs::read_dir(&dir) {
            count += entries.count();
        }
    }
    count
}

#[cfg(target_os = "windows")]
fn is_debugger_present() -> bool {
    unsafe { winapi::um::debugapi::IsDebuggerPresent() != 0 }
}

#[cfg(not(target_os = "windows"))]
fn is_debugger_present() -> bool {
    if let Ok(status) = std::fs::read_to_string("/proc/self/status") {
        for line in status.lines() {
            if line.starts_with("TracerPid:") {
                let pid = line.split(':').nth(1).unwrap_or("0").trim();
                return pid != "0";
            }
        }
    }
    false
}
