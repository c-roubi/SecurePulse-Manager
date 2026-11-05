using System;
using System.IO;
using System.Text;

namespace S4M1A.Builder
{
    /// <summary>أدوات لصق/قراءة الـ config overlay من نهاية ملف الـ implant.</summary>
    internal static class StubOverlay
    {
        public static readonly byte[] Magic = Encoding.ASCII.GetBytes("S4M1AOVL");

        /// <summary>يلصق blob في نهاية الملف (config) ويثبّت صحة الطول.</summary>
        public static void AppendOverlay(string exePath, byte[] blob)
        {
            using (var fs = new FileStream(exePath, FileMode.Append, FileAccess.Write))
                fs.Write(blob, 0, blob.Length);
            // يُحفظ الطول في آخر 8 بايت كـ LE int64 للقراءة لاحقاً
            using (var fs = new FileStream(exePath, FileMode.Append, FileAccess.Write))
            {
                byte[] len = BitConverter.GetBytes((long)blob.Length);
                fs.Write(len, 0, 8);
            }
        }

        /// <summary>يقرأ آخر overlay ملصوق (يرجع null إن لم يوجد).</summary>
        public static byte[] ReadOverlay(string exePath)
        {
            byte[] tail = new byte[8];
            using (var fs = new FileStream(exePath, FileMode.Open, FileAccess.Read))
            {
                if (fs.Length < 8) return null;
                fs.Seek(-8, SeekOrigin.End);
                fs.Read(tail, 0, 8);
                long len = BitConverter.ToInt64(tail, 0);
                if (len <= 0 || len > fs.Length - 8) return null;
                fs.Seek(-8 - len, SeekOrigin.End);
                byte[] buf = new byte[len];
                fs.Read(buf, 0, buf.Length);
                return buf;
            }
        }
    }
}