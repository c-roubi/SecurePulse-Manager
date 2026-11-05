use serde::{Deserialize, Serialize};
use log::info;
use std::path::PathBuf;

#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct SystemInfo {
    pub hostname: String,
    pub os: String,
    pub os_version: String,
    pub architecture: String,
    pub agent_id: String,
}

fn get_or_create_agent_id() -> String {
    let config_dir = dirs::config_dir()
        .unwrap_or_else(|| PathBuf::from(".config"));
    let agent_id_file = config_dir.join("secure_agent_id");

    if let Ok(id) = std::fs::read_to_string(&agent_id_file) {
        return id.trim().to_string();
    }

    let id = uuid::Uuid::new_v4().to_string();
    let _ = std::fs::create_dir_all(&config_dir);
    let _ = std::fs::write(&agent_id_file, &id);
    id
}

pub fn gather_system_info() -> Result<SystemInfo, Box<dyn std::error::Error>> {
    info!("Gathering system information");

    let hostname = gethostname::gethostname()
        .to_string_lossy()
        .to_string();

    let os = std::env::consts::OS.to_string();
    let os_version = "1.0".to_string(); // Placeholder
    let architecture = std::env::consts::ARCH.to_string();
    let agent_id = get_or_create_agent_id();

    Ok(SystemInfo {
        hostname,
        os,
        os_version,
        architecture,
        agent_id,
    })
}
