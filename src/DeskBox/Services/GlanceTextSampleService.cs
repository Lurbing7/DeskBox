using Windows.Graphics.Imaging;
using Windows.Storage;

namespace DeskBox.Services;

internal sealed class GlanceTextSampleService
{
    internal sealed record Sample(byte[] Pixels, int Width, int Height, double ImageWidth, double ImageHeight);
    private readonly Dictionary<string, Sample> _cache = new(StringComparer.OrdinalIgnoreCase);
    internal async Task<Sample?> ReadAsync(string? path, CancellationToken token)
    {
        if (string.IsNullOrEmpty(path) || !File.Exists(path)) return null;
        try
        {
            var fileInfo = new FileInfo(path);
            string key = $"{fileInfo.FullName}|{fileInfo.Length}|{fileInfo.LastWriteTimeUtc.Ticks}";
            if (_cache.TryGetValue(key, out var existing)) return existing;
            token.ThrowIfCancellationRequested();
            var file = await StorageFile.GetFileFromPathAsync(path);
            using var stream = await file.OpenReadAsync();
            var decoder = await BitmapDecoder.CreateAsync(stream);
            var pixels = await decoder.GetPixelDataAsync(BitmapPixelFormat.Bgra8, BitmapAlphaMode.Straight,
                new BitmapTransform { ScaledWidth = 48, ScaledHeight = 48, InterpolationMode = BitmapInterpolationMode.Fant },
                ExifOrientationMode.RespectExifOrientation, ColorManagementMode.ColorManageToSRgb);
            token.ThrowIfCancellationRequested();
            var sample = new Sample(pixels.DetachPixelData(), 48, 48, decoder.OrientedPixelWidth, decoder.OrientedPixelHeight);
            if (_cache.Count >= 32) _cache.Remove(_cache.Keys.First());
            _cache[key] = sample;
            return sample;
        }
        catch (OperationCanceledException) { throw; }
        catch (Exception ex) { App.LogVerbose($"[GlanceTextContrast] Sample failed: {ex.GetType().Name}"); return null; }
    }
}
