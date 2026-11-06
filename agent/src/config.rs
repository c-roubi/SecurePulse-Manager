use std::env;

#[derive(Clone, Debug)]
pub struct Config {
    pub c2_url: String,
    pub c2_port: u16,
    pub encryption_key: [u8; 32],
    pub beacon_interval: u64,
    #[allow(dead_code)]
    pub agent_id: String,
    pub debug: bool,
}

impl Config {
    pub fn from_env() -> Result<Self, Box<dyn std::error::Error>> {
        // Load from .env file if it exists
        let _ = dotenv::dotenv();

        let c2_url = env::var("C2_URL").unwrap_or_else(|_| "localhost".to_string());
        let c2_port = env::var("C2_PORT")
            .unwrap_or_else(|_| "8000".to_string())
            .parse::<u16>()?;

        let beacon_interval = env::var("BEACON_INTERVAL")
            .unwrap_or_else(|_| "30".to_string())
            .parse::<u64>()?;

        let debug = env::var("DEBUG")
            .unwrap_or_else(|_| "false".to_string())
            .to_lowercase() == "true";

        // Load or generate agent ID
        let agent_id = env::var("AGENT_ID")
            .unwrap_or_else(|_| uuid::Uuid::new_v4().to_string());

        // Load encryption key from environment (required)
        let key_str = env::var("ENCRYPTION_KEY")
            .map_err(|_| "ENCRYPTION_KEY environment variable must be set")?;
        let encryption_key = Self::parse_key(&key_str)?;

        Ok(Self {
            c2_url,
            c2_port,
            encryption_key,
            beacon_interval,
            agent_id,
            debug,
        })
    }

    pub fn validate(&self) -> Result<(), Box<dyn std::error::Error>> {
        if self.c2_url.is_empty() {
            return Err("C2_URL not set".into());
        }
        if self.beacon_interval == 0 {
            return Err("BEACON_INTERVAL must be > 0".into());
        }
        Ok(())
    }

    fn parse_key(key_str: &str) -> Result<[u8; 32], Box<dyn std::error::Error>> {
        if key_str.len() != 64 {
            return Err("Encryption key must be 64 hex characters".into());
        }

        let mut key = [0u8; 32];
        for (i, chunk) in key_str.as_bytes().chunks(2).enumerate() {
            if i >= 32 {
                break;
            }
            let hex_str = std::str::from_utf8(chunk)?;
            key[i] = u8::from_str_radix(hex_str, 16)?;
        }

        Ok(key)
    }
}
