namespace DeskBox.Services;

internal static class SystemMonitorAspectLayout
{
    internal readonly record struct Bounds(int X, int Y, int Width, int Height);
    internal static Bounds Resize(Bounds initial, Bounds proposed, Bounds work, string direction,
        double ratio, int chromeWidth = 0, int chromeHeight = 0, int minimumContentWidth = 180)
    {
        if (!(ratio > 0) || !double.IsFinite(ratio)) return proposed;
        bool left = direction.Contains("Left"), top = direction.Contains("Top");
        bool horizontal = left || direction.Contains("Right"), vertical = top || direction.Contains("Bottom");
        double w = Math.Max(1, proposed.Width - chromeWidth), h = Math.Max(1, proposed.Height - chromeHeight);
        double initialW = Math.Max(1, initial.Width - chromeWidth), initialH = Math.Max(1, initial.Height - chromeHeight);
        double targetW = horizontal && vertical ? (Math.Abs(w / initialW - 1) >= Math.Abs(h / initialH - 1) ? w : h * ratio)
            : horizontal ? w : h * ratio;
        int right = initial.X + initial.Width, bottom = initial.Y + initial.Height;
        double maxW = horizontal ? (left ? right - work.X : work.X + work.Width - initial.X) : work.Width;
        double maxH = vertical ? (top ? bottom - work.Y : work.Y + work.Height - initial.Y) : work.Height;
        double limit = Math.Max(1, Math.Min(maxW - chromeWidth, (maxH - chromeHeight) * ratio));
        targetW = Math.Clamp(targetW, Math.Min(minimumContentWidth, limit), limit);
        int width = (int)Math.Round(targetW) + chromeWidth;
        int height = (int)Math.Round(targetW / ratio) + chromeHeight;
        int x = horizontal ? left ? right - width : initial.X : initial.X + (initial.Width - width) / 2;
        int y = vertical ? top ? bottom - height : initial.Y : initial.Y + (initial.Height - height) / 2;
        return new(Math.Clamp(x, work.X, work.X + Math.Max(0, work.Width - width)),
            Math.Clamp(y, work.Y, work.Y + Math.Max(0, work.Height - height)), width, height);
    }
}
