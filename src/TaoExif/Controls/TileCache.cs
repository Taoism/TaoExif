using System.Collections.Concurrent;
using System.Net.Http.Headers;
using Avalonia.Media.Imaging;
using TaoExif.Core;

namespace TaoExif.Controls;

/// <summary>
/// OpenStreetMap tiles with a disk cache. Follows the OSM tile usage policy:
/// identifying User-Agent, at most two parallel downloads, cached tiles reused for 14 days.
/// </summary>
public sealed class TileCache
{
    public static TileCache Shared { get; } = new();

    private const string UrlTemplate = "https://tile.openstreetmap.org/{0}/{1}/{2}.png";
    private static readonly TimeSpan MaxAge = TimeSpan.FromDays(14);

    private readonly HttpClient _http;
    private readonly SemaphoreSlim _downloads = new(2, 2);
    private readonly ConcurrentDictionary<string, Bitmap> _memory = new();
    private readonly ConcurrentDictionary<string, byte> _pending = new();
    private readonly ConcurrentDictionary<string, DateTime> _failed = new();
    private readonly ConcurrentQueue<string> _order = new();
    private readonly string _dir;

    private TileCache()
    {
        _dir = Path.Combine(AppSettings.ConfigDirectory, "tiles");
        _http = new HttpClient { Timeout = TimeSpan.FromSeconds(20) };
        var version = typeof(TileCache).Assembly.GetName().Version?.ToString(3) ?? "0.1";
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TaoExif", version));
        _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("(desktop metadata editor)"));
    }

    /// <summary>Raised on the thread pool when a requested tile becomes available.</summary>
    public event EventHandler? TileLoaded;

    /// <summary>Returns the tile if it is in memory; otherwise starts loading it and returns null.</summary>
    public Bitmap? Get(int z, int x, int y)
    {
        string key = z + "/" + x + "/" + y;
        if (_memory.TryGetValue(key, out var bmp))
            return bmp;
        if (_failed.TryGetValue(key, out var when) && DateTime.UtcNow - when < TimeSpan.FromSeconds(30))
            return null;
        if (_pending.TryAdd(key, 0))
            _ = LoadAsync(key, z, x, y);
        return null;
    }

    private async Task LoadAsync(string key, int z, int x, int y)
    {
        try
        {
            string file = Path.Combine(_dir, z.ToString(), x.ToString(), y + ".png");
            byte[]? data = null;
            if (File.Exists(file) && DateTime.UtcNow - File.GetLastWriteTimeUtc(file) < MaxAge)
                data = await File.ReadAllBytesAsync(file).ConfigureAwait(false);

            if (data == null)
            {
                await _downloads.WaitAsync().ConfigureAwait(false);
                try
                {
                    data = await _http.GetByteArrayAsync(string.Format(UrlTemplate, z, x, y)).ConfigureAwait(false);
                }
                finally
                {
                    _downloads.Release();
                }
                Directory.CreateDirectory(Path.GetDirectoryName(file)!);
                await File.WriteAllBytesAsync(file, data).ConfigureAwait(false);
            }

            using var ms = new MemoryStream(data);
            var bmp = new Bitmap(ms);
            _memory[key] = bmp;
            _order.Enqueue(key);
            Trim();
            TileLoaded?.Invoke(this, EventArgs.Empty);
        }
        catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or UnauthorizedAccessException or ArgumentException)
        {
            // offline or blocked: the tile stays blank and is retried 30 s later
            _failed[key] = DateTime.UtcNow;
        }
        finally
        {
            _pending.TryRemove(key, out _);
        }
    }

    private void Trim()
    {
        // keep the most recent ~400 tiles in memory (the old bitmaps are left to the GC, they may still be drawn)
        while (_memory.Count > 400 && _order.TryDequeue(out var old))
            _memory.TryRemove(old, out _);
    }
}
