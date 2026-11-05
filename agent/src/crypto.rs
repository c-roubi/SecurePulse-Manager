use aes_gcm::{
    aead::{Aead, KeyInit},
    Aes256Gcm, Nonce,
};
use rand::Rng;
use hmac::Mac;
use sha2::Sha256;

pub struct CryptoManager {
    cipher: Aes256Gcm,
    hmac_key: Vec<u8>,
}

impl CryptoManager {
    pub fn new(key: &[u8; 32]) -> Result<Self, Box<dyn std::error::Error>> {
        let cipher = Aes256Gcm::new(key.into());
        let hmac_key = key.to_vec();

        Ok(Self { cipher, hmac_key })
    }

    pub fn encrypt(&self, plaintext: &[u8]) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        let nonce_bytes: [u8; 12] = rand::thread_rng().gen();
        let nonce = Nonce::from_slice(&nonce_bytes);

        let ciphertext = self.cipher.encrypt(nonce, plaintext)
            .map_err(|e| format!("Encryption failed: {}", e))?;

        // Combine nonce + ciphertext
        let mut result = nonce_bytes.to_vec();
        result.extend_from_slice(&ciphertext);

        // Add HMAC
        let hmac = self.compute_hmac(&result);
        result.extend_from_slice(&hmac);

        Ok(result)
    }

    pub fn decrypt(&self, data: &[u8]) -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        if data.len() < 12 + 16 + 32 {
            return Err("Encrypted data too short".into());
        }

        // Verify HMAC
        let hmac_offset = data.len() - 32;
        let payload = &data[..hmac_offset];
        let provided_hmac = &data[hmac_offset..];

        let computed_hmac = self.compute_hmac(payload);
        if computed_hmac != provided_hmac {
            return Err("HMAC verification failed".into());
        }

        // Extract nonce and ciphertext
        let nonce = Nonce::from_slice(&data[..12]);
        let ciphertext = &data[12..hmac_offset];

        let plaintext = self.cipher.decrypt(nonce, ciphertext)
            .map_err(|e| format!("Decryption failed: {}", e))?;

        Ok(plaintext)
    }

    fn compute_hmac(&self, data: &[u8]) -> Vec<u8> {
        use hmac::Hmac;
        type HmacSha256 = Hmac<Sha256>;

        let mut mac = <HmacSha256 as Mac>::new_from_slice(&self.hmac_key)
            .expect("HMAC can take key of any size");
        mac.update(data);
        mac.finalize().into_bytes().to_vec()
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[test]
    fn test_encrypt_decrypt() {
        let key = [42u8; 32];
        let crypto = CryptoManager::new(&key).unwrap();

        let plaintext = b"Hello, World!";
        let encrypted = crypto.encrypt(plaintext).unwrap();
        let decrypted = crypto.decrypt(&encrypted).unwrap();

        assert_eq!(plaintext, &decrypted[..]);
    }
}
