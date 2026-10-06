namespace DeskBox.Services;

internal static class GlanceTextContrastPolicy
{
    internal readonly record struct Rgb(byte R, byte G, byte B);
    internal readonly record struct Result(Rgb Foreground, double BlackScrim);
    internal static double Luminance(Rgb color)
    {
        static double Linear(byte v) { double x = v / 255d; return x <= .04045 ? x / 12.92 : Math.Pow((x + .055) / 1.055, 2.4); }
        return .2126 * Linear(color.R) + .7152 * Linear(color.G) + .0722 * Linear(color.B);
    }
    internal static double Contrast(Rgb a, Rgb b) => (Math.Max(Luminance(a), Luminance(b)) + .05) / (Math.Min(Luminance(a), Luminance(b)) + .05);
    internal static Rgb Blend(Rgb foreground, Rgb background, double opacity) => new(
        (byte)Math.Clamp(Math.Round(foreground.R * opacity + background.R * (1 - opacity)), 0, 255),
        (byte)Math.Clamp(Math.Round(foreground.G * opacity + background.G * (1 - opacity)), 0, 255),
        (byte)Math.Clamp(Math.Round(foreground.B * opacity + background.B * (1 - opacity)), 0, 255));
    internal static Result Resolve(IReadOnlyList<Rgb> pixels)
    {
        if (pixels.Count == 0) return new(new(255, 255, 255), 0);
        var complement = new Rgb((byte)(255 - pixels.Average(p => p.R)),
            (byte)(255 - pixels.Average(p => p.G)), (byte)(255 - pixels.Average(p => p.B)));
        bool Meets(Rgb candidate) => pixels.All(pixel => Contrast(candidate, pixel) >= 4.5);
        if (Meets(complement)) return new(complement, 0);
        Rgb light = new(255, 255, 255), dark = new(0, 0, 0);
        bool preferLight = pixels.Average(Luminance) < .18;
        for (int step = 1; step <= 20; step++)
        {
            var first = Blend(preferLight ? light : dark, complement, step / 20d);
            var second = Blend(preferLight ? dark : light, complement, step / 20d);
            if (Meets(first)) return new(first, 0);
            if (Meets(second)) return new(second, 0);
        }
        // A local scrim supplies contrast across mixed bright/dark image regions.
        for (int step = 1; step <= 14; step++)
        {
            double opacity = step * .05;
            if (pixels.All(pixel => Contrast(light, Blend(dark, pixel, opacity)) >= 4.5)) return new(light, opacity);
        }
        return new(light, .7);
    }
    internal static (double X, double Y) ImagePoint(double x, double y, double viewWidth, double viewHeight,
        double imageWidth, double imageHeight, bool fit, double alignX, double alignY)
    {
        double scale = fit ? Math.Min(viewWidth / imageWidth, viewHeight / imageHeight) : Math.Max(viewWidth / imageWidth, viewHeight / imageHeight);
        return ((x - (viewWidth - imageWidth * scale) * alignX) / (imageWidth * scale),
            (y - (viewHeight - imageHeight * scale) * alignY) / (imageHeight * scale));
    }
}
