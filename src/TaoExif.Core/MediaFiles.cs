namespace TaoExif.Core;

/// <summary>File types TaoExif lists by default (images, RAW, video, audio, documents exiftool reads).</summary>
public static class MediaFiles
{
    public static readonly HashSet<string> Extensions = new(StringComparer.OrdinalIgnoreCase)
    {
        ".jpg", ".jpeg", ".jpe", ".png", ".tif", ".tiff", ".heic", ".heif", ".hif", ".avif", ".webp", ".gif", ".bmp", ".jxl",
        ".dng", ".cr2", ".cr3", ".crw", ".nef", ".nrw", ".arw", ".srf", ".sr2", ".orf", ".rw2", ".raf", ".pef", ".srw",
        ".x3f", ".3fr", ".iiq", ".erf", ".mef", ".mos", ".rwl", ".kdc", ".dcr", ".psd", ".psb", ".xmp", ".ico",
        ".mp4", ".mov", ".m4v", ".avi", ".mts", ".m2ts", ".3gp", ".mkv", ".webm", ".insv", ".lrv",
        ".mp3", ".flac", ".m4a", ".wav", ".ogg", ".opus", ".pdf", ".svg", ".eps", ".ai",
    };

    public static bool IsMedia(string path) => Extensions.Contains(Path.GetExtension(path));
}
