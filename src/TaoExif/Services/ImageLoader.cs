using Avalonia.Media.Imaging;
using TaoExif.Core;

namespace TaoExif.Services;

/// <summary>Decodes thumbnails and previews: directly for common formats, via exiftool's embedded JPEGs otherwise.</summary>
public static class ImageLoader
{
    private static readonly HashSet<string> Decodable = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".png", ".bmp", ".gif", ".webp", ".ico",
    };

    public static readonly HashSet<string> MediaExtensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".png", ".tif", ".tiff", ".heic", ".heif", ".hif", ".avif", ".webp", ".gif", ".bmp", ".jxl",
        ".dng", ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".orf", ".rw2", ".raf", ".pef", ".srw",
        ".x3f", ".3fr", ".iiq", ".erf", ".mef", ".mos", ".rwl", ".kdc", ".dcr", ".psd", ".psb", ".xmp", ".ico",
        ".mp4", ".mov", ".m4v", ".avi", ".mts", ".m2ts", ".3gp", ".mkv", ".webm", ".insv", ".lrv",
        ".mp3", ".flac", ".m4a", ".wav", ".ogg", ".opus", ".pdf", ".svg", ".eps", ".ai",
    };

    public static bool CanDecodeDirectly(string path) => Decodable.Contains(Path.GetExtension(path));

    public static double RotationFor(int orientation) => orientation switch
    {
        3 or 4 => 180,
        5 or 6 => 90,
        7 or 8 => 270,
        _ => 0,
    };

    /// <summary>Loads an image scaled to <paramref name="width"/>; null when nothing can be shown.</summary>
    public static async Task<Bitmap?> LoadAsync(ExifToolService? exif, string path, int width, bool thumbnail, CancellationToken ct)
    {
        if (CanDecodeDirectly(path))
        {
            // For thumbnails of big JPEGs the embedded EXIF thumbnail is much cheaper than decoding the file.
            if (thumbnail && exif != null && new FileInfo(path).Length > 3_000_000)
            {
                var embedded = await TryEmbedded(exif, path, width, true, ct).ConfigureAwait(false);
                if (embedded != null)
                    return embedded;
            }
            var direct = await Task.Run(() => DecodeFile(path, width), ct).ConfigureAwait(false);
            if (direct != null)
                return direct;
        }
        return exif == null ? null : await TryEmbedded(exif, path, width, thumbnail, ct).ConfigureAwait(false);
    }

    private static async Task<Bitmap?> TryEmbedded(ExifToolService exif, string path, int width, bool smallFirst, CancellationToken ct)
    {
        try
        {
            var bytes = await exif.ExtractEmbeddedImageAsync(path, smallFirst, ct).ConfigureAwait(false);
            if (bytes == null)
                return null;
            return await Task.Run(() => DecodeBytes(bytes, width), ct).ConfigureAwait(false);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException)
        {
            return null;
        }
    }

    private static Bitmap? DecodeFile(string path, int width)
    {
        try
        {
            using var fs = new FileStream(path, FileMode.Open, FileAccess.Read, FileShare.ReadWrite | FileShare.Delete, 1 << 16);
            return Bitmap.DecodeToWidth(fs, width, BitmapInterpolationMode.MediumQuality);
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException or ArgumentException or InvalidOperationException or NullReferenceException)
        {
            return null;
        }
    }

    private static Bitmap? DecodeBytes(byte[] bytes, int width)
    {
        try
        {
            using var ms = new MemoryStream(bytes);
            return Bitmap.DecodeToWidth(ms, width, BitmapInterpolationMode.MediumQuality);
        }
        catch (Exception ex) when (ex is IOException or ArgumentException or InvalidOperationException or NullReferenceException)
        {
            return null;
        }
    }
}
