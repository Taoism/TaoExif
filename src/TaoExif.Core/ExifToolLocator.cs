namespace TaoExif.Core;

/// <summary>Where an exiftool came from.</summary>
public enum ExifToolSource
{
    Configured,
    System,
    Bundled,
}

/// <summary>How to start exiftool: a program plus leading arguments (the Perl bundle runs as "perl exiftool").</summary>
public sealed record ExifToolCommand(string FileName, IReadOnlyList<string> PrefixArgs, ExifToolSource Source)
{
    /// <summary>The exiftool itself (the script for the Perl bundle), for display.</summary>
    public string ExifToolPath => PrefixArgs.Count > 0 ? PrefixArgs[^1] : FileName;

    public string SourceText => Source switch
    {
        ExifToolSource.Bundled => "bundled",
        ExifToolSource.Configured => "configured",
        _ => "installed",
    };

    public static ExifToolCommand Direct(string path, ExifToolSource source) => new(path, [], source);
}

/// <summary>
/// Finds exiftool. Order: the path set in Settings, an exiftool installed on the system (PATH and the usual
/// install folders), then the copy bundled with TaoExif in the "exiftool" folder next to the app.
/// </summary>
public static class ExifToolLocator
{
    public const string BundleFolder = "exiftool";

    public static ExifToolCommand? Locate(string? configured, string? appDir = null)
    {
        appDir ??= AppContext.BaseDirectory;
        if (!string.IsNullOrWhiteSpace(configured) && File.Exists(configured))
            return ExifToolCommand.Direct(configured, ExifToolSource.Configured);
        return FindInstalled() is { } installed
            ? ExifToolCommand.Direct(installed, ExifToolSource.System)
            : FindBundled(appDir);
    }

    /// <summary>An exiftool on PATH or in the usual install folders.</summary>
    public static string? FindInstalled()
    {
        bool windows = OperatingSystem.IsWindows();
        string[] names = windows ? ["exiftool.exe"] : ["exiftool"];
        var dirs = PathDirectories();
        if (windows)
        {
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles), "ExifTool"));
            dirs.Add(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.LocalApplicationData), "Programs", "ExifTool"));
        }
        else
        {
            dirs.AddRange(["/opt/homebrew/bin", "/usr/local/bin", "/usr/bin"]);
        }
        return FindFile(dirs, names);
    }

    /// <summary>The copy shipped next to the app: exiftool.exe on Windows, the Perl distribution elsewhere.</summary>
    public static ExifToolCommand? FindBundled(string appDir)
    {
        string dir = Path.Combine(appDir, BundleFolder);
        if (OperatingSystem.IsWindows())
        {
            string exe = Path.Combine(dir, "exiftool.exe");
            return File.Exists(exe) ? ExifToolCommand.Direct(exe, ExifToolSource.Bundled) : null;
        }

        string script = Path.Combine(dir, "exiftool");
        if (!File.Exists(script))
            return null;
        // run through perl: the script may have lost its executable bit when the app was copied or unzipped
        string? perl = FindPerl();
        return perl == null ? null : new ExifToolCommand(perl, [script], ExifToolSource.Bundled);
    }

    /// <summary>True when a bundle folder exists but cannot be used because Perl is missing (Linux/macOS).</summary>
    public static bool BundleNeedsPerl(string? appDir = null) =>
        !OperatingSystem.IsWindows()
        && File.Exists(Path.Combine(appDir ?? AppContext.BaseDirectory, BundleFolder, "exiftool"))
        && FindPerl() == null;

    public static string? FindPerl()
    {
        var dirs = PathDirectories();
        dirs.AddRange(["/usr/bin", "/usr/local/bin", "/opt/homebrew/bin"]);
        return FindFile(dirs, ["perl"]);
    }

    private static List<string> PathDirectories() =>
        (Environment.GetEnvironmentVariable("PATH") ?? "")
        .Split(Path.PathSeparator, StringSplitOptions.RemoveEmptyEntries)
        .Select(d => d.Trim('"'))
        .ToList();

    private static string? FindFile(IEnumerable<string> dirs, string[] names)
    {
        foreach (var dir in dirs)
        {
            foreach (var name in names)
            {
                string candidate;
                try
                {
                    candidate = Path.Combine(dir, name);
                }
                catch (ArgumentException)
                {
                    continue;
                }
                if (File.Exists(candidate))
                    return candidate;
            }
        }
        return null;
    }
}
