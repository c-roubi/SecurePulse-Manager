use aes_gcm::{
    aead::{Aead, KeyInit},
    Aes256Gcm, Nonce as AesNonce,
};
use chacha20poly1305::{ChaCha20Poly1305, Nonce as ChaNonce};
use hkdf::Hkdf;
use hmac::{Hmac, Mac};
use rand::Rng;
use sha2::Sha256;
use x25519_dalek::{EphemeralSecret, PublicKey};
use serde::{Deserialize, Serialize};
use log::{info, debug};

type HmacSha256 = Hmac<Sha256>;

const HKDF_INFO_ENC: &[u8] = b"securepulse-enc-key";
const HKDF_INFO_HMAC: &[u8] = b"securepulse-hmac-key";
const HKDF_INFO_NEXT: &[u8] = b"securepulse-chain-next";

// Cipher suite IDs embedded in wire format
const CIPHER_AES_256_GCM: u8 = 0x01;
const CIPHER_CHACHA20_POLY1305: u8 = 0x02;

// HMAC algorithm IDs
const HMAC_SHA256: u8 = 0x10;
const HMAC_BLAKE3: u8 = 0x11;

#[allow(dead_code)]
#[derive(Serialize, Deserialize, Clone, Debug)]
pub struct KeyExchangePayload {
    pub public_key: Vec<u8>,
    pub salt: Vec<u8>,
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum CipherAlgo {
    Aes256Gcm,
    ChaCha20Poly1305,
}

#[allow(dead_code)]
impl CipherAlgo {
    fn id(&self) -> u8 {
        match self {
            Self::Aes256Gcm => CIPHER_AES_256_GCM,
            Self::ChaCha20Poly1305 => CIPHER_CHACHA20_POLY1305,
        }
    }

    fn from_id(id: u8) -> Result<Self, Box<dyn std::error::Error>> {
        match id {
            CIPHER_AES_256_GCM => Ok(Self::Aes256Gcm),
            CIPHER_CHACHA20_POLY1305 => Ok(Self::ChaCha20Poly1305),
            _ => Err(format!("Unknown cipher ID: 0x{:02x}", id).into()),
        }
    }
}

#[derive(Clone, Copy, Debug, PartialEq)]
enum HmacAlgo {
    Sha256,
    Blake3,
}

#[allow(dead_code)]
impl HmacAlgo {
    fn id(&self) -> u8 {
        match self {
            Self::Sha256 => HMAC_SHA256,
            Self::Blake3 => HMAC_BLAKE3,
        }
    }

    fn from_id(id: u8) -> Result<Self, Box<dyn std::error::Error>> {
        match id {
            HMAC_SHA256 => Ok(Self::Sha256),
            HMAC_BLAKE3 => Ok(Self::Blake3),
            _ => Err(format!("Unknown HMAC ID: 0x{:02x}", id).into()),
        }
    }
}

#[allow(dead_code)]
pub struct KeyChain {
    chain_key: [u8; 32],
    enc_key: [u8; 32],
    hmac_key: [u8; 32],
    message_index: u64,
    prev_nonce: [u8; 12],
    cipher_algo: CipherAlgo,
    hmac_algo: HmacAlgo,
}

#[allow(dead_code)]
impl KeyChain {
    fn derive_from_chain(chain_key: &[u8; 32], salt: &[u8]) -> Result<Self, Box<dyn std::error::Error>> {
        let hk = Hkdf::<Sha256>::new(Some(salt), chain_key);

        let mut enc_key = [0u8; 32];
        hk.expand(HKDF_INFO_ENC, &mut enc_key)
            .map_err(|_| "HKDF expand failed for enc_key")?;

        let mut hmac_key = [0u8; 32];
        hk.expand(HKDF_INFO_HMAC, &mut hmac_key)
            .map_err(|_| "HKDF expand failed for hmac_key")?;

        Ok(Self {
            chain_key: *chain_key,
            enc_key,
            hmac_key,
            message_index: 0,
            prev_nonce: [0u8; 12],
            cipher_algo: CipherAlgo::Aes256Gcm,
            hmac_algo: HmacAlgo::Sha256,
        })
    }

    pub fn ratchet(&mut self) -> Result<(), Box<dyn std::error::Error>> {
        let hk = Hkdf::<Sha256>::new(Some(&self.prev_nonce), &self.chain_key);

        let mut next_chain = [0u8; 32];
        hk.expand(HKDF_INFO_NEXT, &mut next_chain)
            .map_err(|_| "HKDF ratchet failed")?;

        let hk2 = Hkdf::<Sha256>::new(Some(&self.message_index.to_be_bytes()), &next_chain);

        let mut new_enc = [0u8; 32];
        hk2.expand(HKDF_INFO_ENC, &mut new_enc)
            .map_err(|_| "HKDF expand failed for new enc_key")?;

        let mut new_hmac = [0u8; 32];
        hk2.expand(HKDF_INFO_HMAC, &mut new_hmac)
            .map_err(|_| "HKDF expand failed for new hmac_key")?;

        self.chain_key = next_chain;
        self.enc_key = new_enc;
        self.hmac_key = new_hmac;
        self.message_index += 1;

        // Rotate cipher suite deterministically from chain state
        self.cipher_algo = self.select_cipher();
        self.hmac_algo = self.select_hmac();

        debug!("Ratchet #{}: cipher={:?}, hmac={:?}",
            self.message_index, self.cipher_algo, self.hmac_algo);
        Ok(())
    }

    fn select_cipher(&self) -> CipherAlgo {
        let selector = self.chain_key[0];
        match selector % 2 {
            0 => CipherAlgo::Aes256Gcm,
            _ => CipherAlgo::ChaCha20Poly1305,
        }
    }

    fn select_hmac(&self) -> HmacAlgo {
        let selector = self.chain_key[1];
        match selector % 2 {
            0 => HmacAlgo::Sha256,
            _ => HmacAlgo::Blake3,
        }
    }

    fn derive_nonce(&self) -> [u8; 12] {
        let mut hasher = blake3::Hasher::new();
        hasher.update(&self.prev_nonce);
        hasher.update(&self.message_index.to_be_bytes());
        hasher.update(&self.chain_key[..12]);
        let hash = hasher.finalize();
        let mut nonce = [0u8; 12];
        nonce.copy_from_slice(&hash.as_bytes()[..12]);
        nonce
    }

    fn encrypt_data(&self, nonce: &[u8; 12], plaintext: &[u8]) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        match self.cipher_algo {
            CipherAlgo::Aes256Gcm => {
                let cipher = Aes256Gcm::new_from_slice(&self.enc_key)
                    .map_err(|e| format!("AES init failed: {}", e))?;
                cipher.encrypt(AesNonce::from_slice(nonce), plaintext)
                    .map_err(|e| format!("AES encrypt failed: {}", e).into())
            }
            CipherAlgo::ChaCha20Poly1305 => {
                let cipher = ChaCha20Poly1305::new_from_slice(&self.enc_key)
                    .map_err(|e| format!("ChaCha20 init failed: {}", e))?;
                cipher.encrypt(ChaNonce::from_slice(nonce), plaintext)
                    .map_err(|e| format!("ChaCha20 encrypt failed: {}", e).into())
            }
        }
    }

    fn decrypt_data(&self, nonce: &[u8; 12], ciphertext: &[u8]) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        match self.cipher_algo {
            CipherAlgo::Aes256Gcm => {
                let cipher = Aes256Gcm::new_from_slice(&self.enc_key)
                    .map_err(|e| format!("AES init failed: {}", e))?;
                cipher.decrypt(AesNonce::from_slice(nonce), ciphertext)
                    .map_err(|e| format!("AES decrypt failed: {}", e).into())
            }
            CipherAlgo::ChaCha20Poly1305 => {
                let cipher = ChaCha20Poly1305::new_from_slice(&self.enc_key)
                    .map_err(|e| format!("ChaCha20 init failed: {}", e))?;
                cipher.decrypt(ChaNonce::from_slice(nonce), ciphertext)
                    .map_err(|e| format!("ChaCha20 decrypt failed: {}", e).into())
            }
        }
    }

    fn compute_hmac(&self, data: &[u8]) -> Vec<u8> {
        match self.hmac_algo {
            HmacAlgo::Sha256 => {
                let mut mac = <HmacSha256 as Mac>::new_from_slice(&self.hmac_key)
                    .expect("HMAC can take key of any size");
                mac.update(data);
                mac.finalize().into_bytes().to_vec()
            }
            HmacAlgo::Blake3 => {
                let mut hasher = blake3::Hasher::new_keyed(&self.hmac_key);
                hasher.update(data);
                hasher.finalize().as_bytes().to_vec()
            }
        }
    }

    fn verify_hmac(&self, data: &[u8], provided: &[u8]) -> bool {
        let computed = self.compute_hmac(data);
        if computed.len() != provided.len() {
            return false;
        }
        // Constant-time comparison to prevent timing attacks
        let mut diff = 0u8;
        for (a, b) in computed.iter().zip(provided.iter()) {
            diff |= a ^ b;
        }
        diff == 0
    }
}

#[allow(dead_code)]
pub struct CryptoManager {
    psk: [u8; 32],
    key_chain: Option<KeyChain>,
}

#[allow(dead_code)]
impl CryptoManager {
    pub fn new(psk: &[u8; 32]) -> Result<Self, Box<dyn std::error::Error>> {
        Ok(Self {
            psk: *psk,
            key_chain: None,
        })
    }

    pub fn generate_key_exchange(&self) -> (EphemeralSecret, KeyExchangePayload) {
        let secret = EphemeralSecret::random_from_rng(rand::thread_rng());
        let public = PublicKey::from(&secret);
        let salt: [u8; 32] = rand::thread_rng().gen();

        let payload = KeyExchangePayload {
            public_key: public.as_bytes().to_vec(),
            salt: salt.to_vec(),
        };

        (secret, payload)
    }

    pub fn complete_key_exchange(
        &mut self,
        our_secret: EphemeralSecret,
        server_payload: &KeyExchangePayload,
    ) -> Result<(), Box<dyn std::error::Error>> {
        if server_payload.public_key.len() != 32 {
            return Err("Invalid server public key length".into());
        }

        let mut server_pub_bytes = [0u8; 32];
        server_pub_bytes.copy_from_slice(&server_payload.public_key);
        let server_public = PublicKey::from(server_pub_bytes);

        let shared_secret = our_secret.diffie_hellman(&server_public);

        let mut combined = [0u8; 64];
        combined[..32].copy_from_slice(shared_secret.as_bytes());
        combined[32..].copy_from_slice(&self.psk);

        let hk = Hkdf::<Sha256>::new(Some(&server_payload.salt), &combined);
        let mut session_key = [0u8; 32];
        hk.expand(b"securepulse-session-key", &mut session_key)
            .map_err(|_| "HKDF session key derivation failed")?;

        self.key_chain = Some(KeyChain::derive_from_chain(&session_key, &server_payload.salt)?);

        info!("Session key established via ECDH + PSK");
        Ok(())
    }

    pub fn init_with_psk(&mut self) -> Result<(), Box<dyn std::error::Error>> {
        let salt: [u8; 32] = rand::thread_rng().gen();
        self.key_chain = Some(KeyChain::derive_from_chain(&self.psk, &salt)?);
        info!("KeyChain initialized from PSK (fallback mode)");
        Ok(())
    }

    pub fn encrypt(&mut self, plaintext: &[u8]) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        let chain = self.key_chain.as_mut()
            .ok_or("KeyChain not initialized")?;

        let nonce_bytes = chain.derive_nonce();

        let ciphertext = chain.encrypt_data(&nonce_bytes, plaintext)?;

        // Wire format v3:
        // [version (1)] [cipher_id (1)] [hmac_id (1)] [msg_index (8)] [nonce (12)] [ciphertext] [MAC (32)]
        let mut result = Vec::with_capacity(3 + 8 + 12 + ciphertext.len() + 32);
        result.push(0x03); // version 3
        result.push(chain.cipher_algo.id());
        result.push(chain.hmac_algo.id());
        result.extend_from_slice(&chain.message_index.to_be_bytes());
        result.extend_from_slice(&nonce_bytes);
        result.extend_from_slice(&ciphertext);

        let mac = chain.compute_hmac(&result);
        result.extend_from_slice(&mac);

        chain.prev_nonce = nonce_bytes;
        chain.ratchet()?;

        Ok(result)
    }

    pub fn decrypt(&mut self, data: &[u8]) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        // Minimum: 3 (header) + 8 (index) + 12 (nonce) + 16 (AEAD tag) + 32 (MAC)
        if data.len() < 71 {
            return Err("Encrypted data too short".into());
        }

        let version = data[0];
        if version != 0x03 {
            return Err(format!("Unsupported wire version: 0x{:02x}", version).into());
        }

        let chain = self.key_chain.as_mut()
            .ok_or("KeyChain not initialized")?;

        let wire_cipher = CipherAlgo::from_id(data[1])?;
        let wire_hmac = HmacAlgo::from_id(data[2])?;

        if wire_cipher != chain.cipher_algo || wire_hmac != chain.hmac_algo {
            return Err("Cipher suite mismatch - sender and receiver out of sync".into());
        }

        // Verify MAC
        let mac_offset = data.len() - 32;
        let payload = &data[..mac_offset];
        let provided_mac = &data[mac_offset..];

        if !chain.verify_hmac(payload, provided_mac) {
            return Err("MAC verification failed".into());
        }

        let nonce: [u8; 12] = data[11..23].try_into()?;
        let ciphertext = &data[23..mac_offset];

        let plaintext = chain.decrypt_data(&nonce, ciphertext)?;

        chain.prev_nonce = nonce;
        chain.ratchet()?;

        Ok(plaintext)
    }

    pub fn message_index(&self) -> u64 {
        self.key_chain.as_ref().map_or(0, |c| c.message_index)
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    fn make_synced_pair(key: &[u8; 32], salt: &[u8; 32]) -> (CryptoManager, CryptoManager) {
        let mut sender = CryptoManager::new(key).unwrap();
        sender.key_chain = Some(KeyChain::derive_from_chain(key, salt).unwrap());

        let mut receiver = CryptoManager::new(key).unwrap();
        receiver.key_chain = Some(KeyChain::derive_from_chain(key, salt).unwrap());

        (sender, receiver)
    }

    #[test]
    fn test_synced_encrypt_decrypt() {
        let (mut sender, mut receiver) = make_synced_pair(&[42u8; 32], &[99u8; 32]);

        let plaintext = b"secret command payload";
        let encrypted = sender.encrypt(plaintext).unwrap();
        let decrypted = receiver.decrypt(&encrypted).unwrap();
        assert_eq!(plaintext.to_vec(), decrypted);

        let plaintext2 = b"another secret";
        let encrypted2 = sender.encrypt(plaintext2).unwrap();
        let decrypted2 = receiver.decrypt(&encrypted2).unwrap();
        assert_eq!(plaintext2.to_vec(), decrypted2);
    }

    #[test]
    fn test_cipher_rotation_happens() {
        let (mut sender, mut receiver) = make_synced_pair(&[42u8; 32], &[99u8; 32]);

        let mut cipher_ids_seen = std::collections::HashSet::new();
        let mut hmac_ids_seen = std::collections::HashSet::new();

        for i in 0..20 {
            let msg = format!("message {}", i);
            let encrypted = sender.encrypt(msg.as_bytes()).unwrap();

            // Extract cipher_id and hmac_id from wire format
            cipher_ids_seen.insert(encrypted[1]);
            hmac_ids_seen.insert(encrypted[2]);

            let decrypted = receiver.decrypt(&encrypted).unwrap();
            assert_eq!(msg.as_bytes(), &decrypted[..]);
        }

        assert!(cipher_ids_seen.len() > 1,
            "Expected cipher rotation, only saw: {:?}", cipher_ids_seen);
        assert!(hmac_ids_seen.len() > 1,
            "Expected HMAC rotation, only saw: {:?}", hmac_ids_seen);
    }

    #[test]
    fn test_wire_format_v3_header() {
        let (mut sender, _) = make_synced_pair(&[42u8; 32], &[99u8; 32]);

        let encrypted = sender.encrypt(b"test").unwrap();

        assert_eq!(encrypted[0], 0x03, "Version byte should be 0x03");
        assert!(encrypted[1] == CIPHER_AES_256_GCM || encrypted[1] == CIPHER_CHACHA20_POLY1305);
        assert!(encrypted[2] == HMAC_SHA256 || encrypted[2] == HMAC_BLAKE3);
    }

    #[test]
    fn test_replay_attack_fails() {
        let (mut sender, mut receiver) = make_synced_pair(&[42u8; 32], &[99u8; 32]);

        let encrypted = sender.encrypt(b"msg1").unwrap();
        let _ = receiver.decrypt(&encrypted).unwrap();

        let result = receiver.decrypt(&encrypted);
        assert!(result.is_err());
    }

    #[test]
    fn test_key_ratchet_produces_different_keys() {
        let mut chain = KeyChain::derive_from_chain(&[42u8; 32], &[1u8; 32]).unwrap();

        let enc1 = chain.enc_key;
        chain.ratchet().unwrap();
        let enc2 = chain.enc_key;
        chain.ratchet().unwrap();
        let enc3 = chain.enc_key;

        assert_ne!(enc1, enc2);
        assert_ne!(enc2, enc3);
        assert_ne!(enc1, enc3);
    }

    #[test]
    fn test_nonce_derivation_unique() {
        let mut chain = KeyChain::derive_from_chain(&[42u8; 32], &[1u8; 32]).unwrap();

        let n1 = chain.derive_nonce();
        chain.ratchet().unwrap();
        let n2 = chain.derive_nonce();

        assert_ne!(n1, n2);
    }

    #[test]
    fn test_ecdh_key_exchange() {
        let key = [42u8; 32];
        let mut agent = CryptoManager::new(&key).unwrap();
        let mut server = CryptoManager::new(&key).unwrap();

        let (agent_secret, agent_payload) = agent.generate_key_exchange();
        let (server_secret, server_payload) = server.generate_key_exchange();

        agent.complete_key_exchange(agent_secret, &server_payload).unwrap();
        server.complete_key_exchange(server_secret, &agent_payload).unwrap();
    }

    #[test]
    fn test_encrypt_decrypt_with_psk() {
        let key = [42u8; 32];
        let mut crypto = CryptoManager::new(&key).unwrap();
        crypto.init_with_psk().unwrap();

        let encrypted = crypto.encrypt(b"Hello, World!").unwrap();
        assert!(encrypted.len() > 71);
        assert_eq!(encrypted[0], 0x03);
    }

    #[test]
    fn test_many_messages_all_decrypt() {
        let (mut sender, mut receiver) = make_synced_pair(&[42u8; 32], &[99u8; 32]);

        for i in 0..50 {
            let msg = format!("message number {} with some data to encrypt", i);
            let encrypted = sender.encrypt(msg.as_bytes()).unwrap();
            let decrypted = receiver.decrypt(&encrypted).unwrap();
            assert_eq!(msg.as_bytes(), &decrypted[..], "Failed at message {}", i);
        }
    }

    #[test]
    fn test_tampered_message_rejected() {
        let (mut sender, mut receiver) = make_synced_pair(&[42u8; 32], &[99u8; 32]);

        let mut encrypted = sender.encrypt(b"important data").unwrap();

        // Flip a byte in the ciphertext area
        let mid = encrypted.len() / 2;
        encrypted[mid] ^= 0xFF;

        let result = receiver.decrypt(&encrypted);
        assert!(result.is_err());
    }
}
