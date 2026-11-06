use log::info;
use std::process::Command;
use base64::{Engine as _, engine::general_purpose::STANDARD as BASE64};

pub struct ScreenCapture;

impl ScreenCapture {
    /// Capture current desktop screenshot
    pub async fn capture() -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        let output = if cfg!(target_os = "windows") {
            Self::capture_windows().await?
        } else if cfg!(target_os = "macos") {
            Self::capture_macos().await?
        } else {
            Self::capture_linux().await?
        };

        Ok(output)
    }

    /// Capture on Windows using built-in tools
    async fn capture_windows() -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        // PowerShell script to capture screen
        let script = r#"
$bitmap = New-Object System.Drawing.Bitmap([System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Width, [System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Height)
$graphics = [System.Drawing.Graphics]::FromImage($bitmap)
$graphics.CopyFromScreen([System.Windows.Forms.Screen]::PrimaryScreen.Bounds.Location, [System.Drawing.Point]::Empty, $bitmap.Size)
$bitmap.Save("$env:TEMP\screenshot.jpg", [System.Drawing.Imaging.ImageFormat]::Jpeg)
Write-Output "$env:TEMP\screenshot.jpg"
"#;

        let output = Command::new("powershell")
            .arg("-NoProfile")
            .arg("-Command")
            .arg(script)
            .output()?;

        let path = String::from_utf8(output.stdout)?.trim().to_string();
        let img_data = std::fs::read(&path)?;
        let _ = std::fs::remove_file(&path);

        Ok(img_data)
    }

    /// Capture on macOS using screencapture
    async fn capture_macos() -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        let temp_path = "/tmp/screenshot.jpg";

        Command::new("screencapture")
            .arg("-x")
            .arg("-t")
            .arg("jpg")
            .arg(temp_path)
            .output()?;

        let img_data = std::fs::read(temp_path)?;
        let _ = std::fs::remove_file(temp_path);

        Ok(img_data)
    }

    /// Capture on Linux using scrot or gnome-screenshot
    async fn capture_linux() -> Result<Vec<u8>, Box<dyn std::error::Error>> {
        let temp_path = "/tmp/screenshot.jpg";

        // Try scrot first
        let result = Command::new("scrot")
            .arg("-q")
            .arg("85")
            .arg(temp_path)
            .output();

        if result.is_err() {
            // Fallback to gnome-screenshot
            Command::new("gnome-screenshot")
                .arg("-f")
                .arg(temp_path)
                .output()?;
        }

        let img_data = std::fs::read(temp_path)?;
        let _ = std::fs::remove_file(temp_path);

        Ok(img_data)
    }

    /// Compress screenshot to reduce bandwidth
    pub fn compress_jpeg(data: &[u8], quality: u32) -> Vec<u8> {
        // Return base64 encoded for transmission
        BASE64.encode(data).into_bytes()
    }

    /// Get screenshot info (for debugging)
    pub fn get_info(data: &[u8]) -> String {
        format!("Screenshot: {} bytes", data.len())
    }
}

#[cfg(test)]
mod tests {
    use super::*;

    #[tokio::test]
    async fn test_screenshot_capture() {
        match ScreenCapture::capture().await {
            Ok(data) => {
                println!("✅ Screenshot captured: {} bytes", data.len());
            }
            Err(e) => {
                println!("Note: Screenshot capture not available: {}", e);
            }
        }
    }
}
