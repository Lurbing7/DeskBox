using System.Drawing;
using System.Drawing.Drawing2D;
using System.Drawing.Imaging;

namespace DeskBox.Helpers;

/// <summary>Dock 专用：统一透明图标的可见主体长边，保留原图比例和源文件。</summary>
internal static class DockIconNormalizer
{
    internal static byte[] Normalize(byte[] bytes)
    {
        try
        {
            using var input = new MemoryStream(bytes, writable: false);
            using var source = new Bitmap(input);
            if (source.Width > 1024 || source.Height > 1024) return bytes;
            using var pixels = new Bitmap(source.Width, source.Height, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(pixels))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.DrawImageUnscaled(source, 0, 0);
            }
            Rectangle bounds = ArtworkBounds(pixels);
            if (bounds.IsEmpty) return bytes;
            int side = Math.Clamp(Math.Max(source.Width, source.Height), 32, 256);
            double scale = side * .88 / Math.Max(bounds.Width, bounds.Height);
            float width = (float)(bounds.Width * scale), height = (float)(bounds.Height * scale);
            using var normalized = new Bitmap(side, side, PixelFormat.Format32bppArgb);
            using (var graphics = Graphics.FromImage(normalized))
            {
                graphics.CompositingMode = CompositingMode.SourceCopy;
                graphics.InterpolationMode = InterpolationMode.HighQualityBicubic;
                graphics.PixelOffsetMode = PixelOffsetMode.HighQuality;
                graphics.DrawImage(pixels, new RectangleF((side - width) / 2, (side - height) / 2, width, height), bounds, GraphicsUnit.Pixel);
            }
            using var output = new MemoryStream();
            normalized.Save(output, ImageFormat.Png);
            return output.ToArray();
        }
        catch { return bytes; }
    }

    private static unsafe Rectangle ArtworkBounds(Bitmap bitmap)
    {
        var data = bitmap.LockBits(new Rectangle(0, 0, bitmap.Width, bitmap.Height), ImageLockMode.ReadOnly, PixelFormat.Format32bppArgb);
        try
        {
            byte peak = 0;
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                    peak = Math.Max(peak, *((byte*)data.Scan0 + y * data.Stride + x * 4 + 3));
            if (peak == 0) return Rectangle.Empty;
            byte threshold = IconBitmapQuality.SignificantAlphaThreshold(peak);
            int minX = bitmap.Width, minY = bitmap.Height, maxX = -1, maxY = -1;
            for (int y = 0; y < bitmap.Height; y++)
                for (int x = 0; x < bitmap.Width; x++)
                    if (*((byte*)data.Scan0 + y * data.Stride + x * 4 + 3) >= threshold)
                    { minX = Math.Min(minX, x); minY = Math.Min(minY, y); maxX = Math.Max(maxX, x); maxY = Math.Max(maxY, y); }
            return maxX < minX ? Rectangle.Empty : Rectangle.FromLTRB(minX, minY, maxX + 1, maxY + 1);
        }
        finally { bitmap.UnlockBits(data); }
    }
}
