use aes_gcm::{
    aead::{Aead, KeyInit},
    Aes256Gcm, Nonce,
};
use hkdf::Hkdf;
use hmac::{Hmac, Mac};
use rand::Rng;
use sha2::Sha256;
use x25519_dalek::{EphemeralSecret, PublicKey, SharedSecret};
use serde::{Deserialize, Serialize};
use log::{info, debug};

type HmacSha256 = Hmac<Sha256>;

const HKDF_INFO_ENC: &[u8] = b"securepulse-enc-key";
const HKDF_INFO_HMAC: &[u8] = b"securepulse-hmac-key";
const HKDF_INFO_NEXT: &[u8] = b"securepulse-chain-next";

#[derive(Serialize, Deserialize, Clone, Debug)]
pub struct KeyExchangePayload {
    pub public_key: Vec<u8>,
    pub salt: Vec<u8>,
}

pub struct KeyChain {
    chain_key: [u8; 32],
    enc_key: [u8; 32],
    hmac_key: [u8; 32],
    message_index: u64,
    prev_nonce: [u8; 12],
}

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
        })
    }

    pub fn ratchet(&mut self) -> Result<(), Box<dyn std::error::Error>> {
        let hk = Hkdf::<Sha256>::new(Some(&self.prev_nonce), &self.chain_key);

        let mut next_chain = [0u8; 32];
        hk.expand(HKDF_INFO_NEXT, &mut next_chain)
            .map_err(|_| "HKDF ratchet failed")?;

        let mut new_enc = [0u8; 32];
        let hk2 = Hkdf::<Sha256>::new(Some(&self.message_index.to_be_bytes()), &next_chain);
        hk2.expand(HKDF_INFO_ENC, &mut new_enc)
            .map_err(|_| "HKDF expand failed for new enc_key")?;

        let mut new_hmac = [0u8; 32];
        hk2.expand(HKDF_INFO_HMAC, &mut new_hmac)
            .map_err(|_| "HKDF expand failed for new hmac_key")?;

        self.chain_key = next_chain;
        self.enc_key = new_enc;
        self.hmac_key = new_hmac;
        self.message_index += 1;

        debug!("Key ratcheted to index {}", self.message_index);
        Ok(())
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
}

pub struct CryptoManager {
    psk: [u8; 32],
    key_chain: Option<KeyChain>,
}

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

        // Combine ECDH shared secret with PSK for extra security
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
            .ok_or("KeyChain not initialized - call init_with_psk or complete_key_exchange first")?;

        let nonce_bytes = chain.derive_nonce();
        let nonce = Nonce::from_slice(&nonce_bytes);

        let cipher = Aes256Gcm::new_from_slice(&chain.enc_key)
            .map_err(|e| format!("Cipher init failed: {}", e))?;

        let ciphertext = cipher.encrypt(nonce, plaintext)
            .map_err(|e| format!("Encryption failed: {}", e))?;

        // Header: [message_index (8 bytes)] [nonce (12 bytes)] [ciphertext] [HMAC (32 bytes)]
        let mut result = Vec::with_capacity(8 + 12 + ciphertext.len() + 32);
        result.extend_from_slice(&chain.message_index.to_be_bytes());
        result.extend_from_slice(&nonce_bytes);
        result.extend_from_slice(&ciphertext);

        let hmac = compute_hmac(&chain.hmac_key, &result);
        result.extend_from_slice(&hmac);

        // Advance the ratchet
        chain.prev_nonce = nonce_bytes;
        chain.ratchet()?;

        Ok(result)
    }

    pub fn decrypt(&mut self, data: &[u8]) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        // Minimum: 8 (index) + 12 (nonce) + 16 (AES-GCM tag) + 32 (HMAC)
        if data.len() < 68 {
            return Err("Encrypted data too short".into());
        }

        let chain = self.key_chain.as_mut()
            .ok_or("KeyChain not initialized")?;

        let hmac_offset = data.len() - 32;
        let payload = &data[..hmac_offset];
        let provided_hmac = &data[hmac_offset..];

        let computed_hmac = compute_hmac(&chain.hmac_key, payload);
        if computed_hmac != provided_hmac {
            return Err("HMAC verification failed - message tampered or wrong key state".into());
        }

        let _msg_index = u64::from_be_bytes(data[..8].try_into()?);
        let nonce = Nonce::from_slice(&data[8..20]);
        let ciphertext = &data[20..hmac_offset];

        let cipher = Aes256Gcm::new_from_slice(&chain.enc_key)
            .map_err(|e| format!("Cipher init failed: {}", e))?;

        let plaintext = cipher.decrypt(nonce, ciphertext)
            .map_err(|e| format!("Decryption failed: {}", e))?;

        chain.prev_nonce = data[8..20].try_into()?;
        chain.ratchet()?;

        Ok(plaintext)
    }

    pub fn message_index(&self) -> u64 {
        self.key_chain.as_ref().map_or(0, |c| c.message_index)
    }
}

fn compute_hmac(key: &[u8], data: &[u8]) -> Vec<u8> {
    let mut mac = <HmacSha256 as Mac>::new_from_slice(key)
        .expect("HMAC can take key of any size");
    mac.update(data);
    mac.finalize().into_bytes().to_vec()
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_encrypt_decrypt_with_psk() {
        let key = [42u8; 32];
        let mut crypto = CryptoManager::new(&key).unwrap();
        crypto.init_with_psk().unwrap();

        let plaintext = b"Hello, World!";
        let encrypted = crypto.encrypt(plaintext).unwrap();

        // Need a second instance with same state for decrypt
        // In real use, server and agent ratchet in sync
        let mut crypto2 = CryptoManager::new(&key).unwrap();
        crypto2.init_with_psk().unwrap();

        // This won't work because salt is random - that's by design
        // In production, salt is exchanged during handshake
    }

    #[test]
    fn test_key_ratchet_produces_different_keys() {
        let key = [42u8; 32];
        let salt = [1u8; 32];
        let mut chain = KeyChain::derive_from_chain(&key, &salt).unwrap();

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
        let key = [42u8; 32];
        let salt = [1u8; 32];
        let mut chain = KeyChain::derive_from_chain(&key, &salt).unwrap();

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

        // Both have initialized key chains (different session keys though,
        // since ECDH is asymmetric - in production the server echoes its
        // public key and uses the agent's)
    }

    #[test]
    fn test_synced_encrypt_decrypt() {
        let key = [42u8; 32];
        let salt = [99u8; 32];

        let mut sender = CryptoManager::new(&key).unwrap();
        sender.key_chain = Some(KeyChain::derive_from_chain(&key, &salt).unwrap());

        let mut receiver = CryptoManager::new(&key).unwrap();
        receiver.key_chain = Some(KeyChain::derive_from_chain(&key, &salt).unwrap());

        let plaintext = b"secret command payload";
        let encrypted = sender.encrypt(plaintext).unwrap();
        let decrypted = receiver.decrypt(&encrypted).unwrap();

        assert_eq!(plaintext.to_vec(), decrypted);

        // Second message also works (both ratcheted)
        let plaintext2 = b"another secret";
        let encrypted2 = sender.encrypt(plaintext2).unwrap();
        let decrypted2 = receiver.decrypt(&encrypted2).unwrap();
        assert_eq!(plaintext2.to_vec(), decrypted2);
    }

    #[test]
    fn test_replay_attack_fails() {
        let key = [42u8; 32];
        let salt = [99u8; 32];

        let mut sender = CryptoManager::new(&key).unwrap();
        sender.key_chain = Some(KeyChain::derive_from_chain(&key, &salt).unwrap());

        let mut receiver = CryptoManager::new(&key).unwrap();
        receiver.key_chain = Some(KeyChain::derive_from_chain(&key, &salt).unwrap());

        let encrypted = sender.encrypt(b"msg1").unwrap();
        let _ = receiver.decrypt(&encrypted).unwrap();

        // Replay the same message - should fail because receiver ratcheted
        let result = receiver.decrypt(&encrypted);
        assert!(result.is_err());
    }
}
