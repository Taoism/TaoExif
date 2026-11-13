using System.Net.Http.Headers;
using System.Runtime.InteropServices;
using System.Security.Cryptography;
using System.Text.Json;

namespace TaoExif.Core;

/// <summary>How this copy of TaoExif was installed; decides which release file fits and whether it can update itself.</summary>
public enum InstallKind
{
    WindowsInstaller,
    WindowsPortable,
    LinuxDeb,
    LinuxPortable,
    MacApp,
    Unknown,
}

public sealed record ReleaseAsset(string Name, Uri Url, long Size);

public sealed record ReleaseInfo(Version Version, string Tag, string Name, string Notes, Uri Page, IReadOnlyList<ReleaseAsset> Assets);

/// <summary>Checks GitHub releases and downloads verified packages.</summary>
public sealed class UpdateChecker
{
    public const string Repository = "Taoism/TaoExif";
    public const string SumsFile = "SHA256SUMS.txt";

    private static readonly string[] AllowedHosts = ["github.com", "api.github.com", "objects.githubusercontent.com", "release-assets.githubusercontent.com"];

    private readonly HttpClient _http;

    public UpdateChecker(HttpClient? http = null)
    {
        _http = http ?? new HttpClient { Timeout = TimeSpan.FromMinutes(10) };
        if (_http.DefaultRequestHeaders.UserAgent.Count == 0)
            _http.DefaultRequestHeaders.UserAgent.Add(new ProductInfoHeaderValue("TaoExif", CurrentVersion.ToString(3)));
    }

    public static Version CurrentVersion => typeof(UpdateChecker).Assembly.GetName().Version ?? new Version(0, 0, 0);

    /// <summary>The latest published (non-draft, non-prerelease) release.</summary>
    public async Task<ReleaseInfo?> GetLatestAsync(CancellationToken ct = default)
    {
        using var req = new HttpRequestMessage(HttpMethod.Get, $"https://api.github.com/repos/{Repository}/releases/latest");
        req.Headers.Accept.Add(new MediaTypeWithQualityHeaderValue("application/vnd.github+json"));
        using var resp = await _http.SendAsync(req, ct).ConfigureAwait(false);
        if (!resp.IsSuccessStatusCode)
            return null;
        string json = await resp.Content.ReadAsStringAsync(ct).ConfigureAwait(false);
        return ParseRelease(json);
    }

    internal static ReleaseInfo? ParseRelease(string json)
    {
        using var doc = JsonDocument.Parse(json);
        var root = doc.RootElement;
        string tag = root.GetProperty("tag_name").GetString() ?? "";
        if (!TryParseVersion(tag, out var version))
            return null;
        var assets = new List<ReleaseAsset>();
        if (root.TryGetProperty("assets", out var arr))
        {
            foreach (var a in arr.EnumerateArray())
            {
                string? name = a.GetProperty("name").GetString();
                string? url = a.GetProperty("browser_download_url").GetString();
                if (name != null && Uri.TryCreate(url, UriKind.Absolute, out var u))
                    assets.Add(new ReleaseAsset(name, u, a.TryGetProperty("size", out var sz) ? sz.GetInt64() : 0));
            }
        }
        string page = root.TryGetProperty("html_url", out var h) ? h.GetString() ?? "" : "";
        return new ReleaseInfo(version, tag,
            root.TryGetProperty("name", out var n) ? n.GetString() ?? tag : tag,
            root.TryGetProperty("body", out var b) ? b.GetString() ?? "" : "",
            Uri.TryCreate(page, UriKind.Absolute, out var p) ? p : new Uri($"https://github.com/{Repository}/releases"),
            assets);
    }

    public static bool TryParseVersion(string tag, out Version version)
    {
        string t = tag.Trim().TrimStart('v', 'V');
        int dash = t.IndexOf('-');
        if (dash >= 0)
            t = t[..dash];
        if (Version.TryParse(t, out var v))
        {
            version = new Version(v.Major, v.Minor, Math.Max(0, v.Build));
            return true;
        }
        version = new Version(0, 0, 0);
        return false;
    }

    private static readonly System.Text.RegularExpressions.Regex Italics =
        new(@"(?<![\w*])\*([^*]+)\*(?![\w*])", System.Text.RegularExpressions.RegexOptions.None, TimeSpan.FromSeconds(1));

    /// <summary>Release notes are Markdown; strip headings, emphasis, code marks and table rules for a plain read.</summary>
    public static string PlainNotes(string markdown)
    {
        var lines = markdown.Replace("\r", "").Split('\n')
            .Where(l => !l.TrimStart().StartsWith("|---", StringComparison.Ordinal))
            .Select(l => Italics.Replace(l.TrimStart('#', ' ').Replace("**", "").Replace("`", ""), "$1"))
            .Select(l => l.StartsWith('|') ? string.Join("  ·  ", l.Trim('|').Split('|').Select(c => c.Trim()).Where(c => c.Length > 0)) : l);
        string text = string.Join("\n", lines).Trim();
        return text.Length > 4000 ? text[..4000] + "…" : text;
    }

    public static bool IsNewer(Version candidate, Version current) =>
        candidate > new Version(current.Major, current.Minor, Math.Max(0, current.Build));

    /// <summary>Detects how the app in <paramref name="appDir"/> was installed.</summary>
    public static InstallKind DetectInstallKind(string? appDir = null)
    {
        appDir ??= AppContext.BaseDirectory;
        if (OperatingSystem.IsWindows())
            return File.Exists(Path.Combine(appDir, "unins000.exe")) ? InstallKind.WindowsInstaller : InstallKind.WindowsPortable;
        if (OperatingSystem.IsMacOS())
            return appDir.Contains(".app/Contents/MacOS", StringComparison.Ordinal) ? InstallKind.MacApp : InstallKind.Unknown;
        if (OperatingSystem.IsLinux())
            return appDir.TrimEnd('/') == "/opt/taoexif" ? InstallKind.LinuxDeb : InstallKind.LinuxPortable;
        return InstallKind.Unknown;
    }

    /// <summary>The release file that matches this install, e.g. the setup .exe for an installed Windows copy.</summary>
    public static ReleaseAsset? PickAsset(ReleaseInfo release, InstallKind kind, Architecture arch)
    {
        string suffix = kind switch
        {
            InstallKind.WindowsInstaller => "-setup-win-x64.exe",
            InstallKind.WindowsPortable => "-win-x64.zip",
            InstallKind.LinuxDeb => "_amd64.deb",
            InstallKind.LinuxPortable => "-linux-x64.tar.gz",
            InstallKind.MacApp => arch == Architecture.Arm64 ? "-macos-arm64.zip" : "-macos-x64.zip",
            _ => "",
        };
        return suffix.Length == 0 ? null : release.Assets.FirstOrDefault(a => a.Name.EndsWith(suffix, StringComparison.OrdinalIgnoreCase));
    }

    /// <summary>Parses "hash  name" (or "hash *name") lines.</summary>
    internal static Dictionary<string, string> ParseSums(string text)
    {
        var map = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (var raw in text.Split('\n'))
        {
            var line = raw.Trim();
            int sp = line.IndexOf(' ');
            if (sp != 64)
                continue;
            string hash = line[..64];
            string name = line[sp..].Trim().TrimStart('*');
            if (hash.All(Uri.IsHexDigit))
                map[name] = hash.ToLowerInvariant();
        }
        return map;
    }

    /// <summary>
    /// Downloads <paramref name="asset"/> into <paramref name="folder"/> and checks it against the release's SHA256SUMS.txt.
    /// The file is deleted and an exception thrown when the checksum is missing or wrong.
    /// </summary>
    public async Task<string> DownloadVerifiedAsync(ReleaseInfo release, ReleaseAsset asset, string folder, IProgress<double>? progress = null, CancellationToken ct = default)
    {
        var sumsAsset = release.Assets.FirstOrDefault(a => a.Name.Equals(SumsFile, StringComparison.OrdinalIgnoreCase))
            ?? throw new InvalidOperationException("The release has no " + SumsFile + "; the download cannot be verified.");
        CheckHost(asset.Url);
        CheckHost(sumsAsset.Url);

        string sums = await _http.GetStringAsync(sumsAsset.Url, ct).ConfigureAwait(false);
        if (!ParseSums(sums).TryGetValue(asset.Name, out var expected))
            throw new InvalidOperationException(asset.Name + " is not listed in " + SumsFile + ".");

        Directory.CreateDirectory(folder);
        string path = Path.Combine(folder, Path.GetFileName(asset.Name));
        using (var resp = await _http.GetAsync(asset.Url, HttpCompletionOption.ResponseHeadersRead, ct).ConfigureAwait(false))
        {
            resp.EnsureSuccessStatusCode();
            CheckHost(resp.RequestMessage?.RequestUri ?? asset.Url);
            long total = resp.Content.Headers.ContentLength ?? asset.Size;
            await using var src = await resp.Content.ReadAsStreamAsync(ct).ConfigureAwait(false);
            await using var dst = new FileStream(path, FileMode.Create, FileAccess.Write, FileShare.None, 1 << 16, true);
            var buffer = new byte[1 << 16];
            long done = 0;
            int n;
            while ((n = await src.ReadAsync(buffer, ct).ConfigureAwait(false)) > 0)
            {
                await dst.WriteAsync(buffer.AsMemory(0, n), ct).ConfigureAwait(false);
                done += n;
                if (total > 0)
                    progress?.Report((double)done / total);
            }
        }

        string actual = await HashFileAsync(path, ct).ConfigureAwait(false);
        if (!string.Equals(actual, expected, StringComparison.OrdinalIgnoreCase))
        {
            File.Delete(path);
            throw new InvalidOperationException($"Checksum mismatch for {asset.Name}; the download was deleted.");
        }
        return path;
    }

    public static async Task<string> HashFileAsync(string path, CancellationToken ct = default)
    {
        await using var fs = File.OpenRead(path);
        var hash = await SHA256.HashDataAsync(fs, ct).ConfigureAwait(false);
        return Convert.ToHexStringLower(hash);
    }

    private static void CheckHost(Uri url)
    {
        if (url.Scheme != Uri.UriSchemeHttps || !AllowedHosts.Contains(url.Host, StringComparer.OrdinalIgnoreCase))
            throw new InvalidOperationException("Refusing to download from " + url.Host + ".");
    }
}
