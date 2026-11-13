using System.Globalization;
using System.Text;
using System.Text.Json;
using TaoExif.Core;

namespace TaoExif.Cli;

/// <summary>taoexif-cli: the TaoExif operations for scripts, using the same engine and bundled ExifTool as the app.</summary>
public static class Program
{
    public const int Ok = 0;
    public const int ExifToolErrors = 1;
    public const int UsageError = 2;
    public const int NoExifTool = 3;

    private static readonly string[] Commands =
        ["info", "get", "set", "remove", "shift", "rename", "gps", "geotag", "copy", "export", "run", "version", "update", "help"];

    public static async Task<int> Main(string[] argv)
    {
        Console.OutputEncoding = new UTF8Encoding(false);
        CliArgs args;
        try
        {
            args = CliArgs.Parse(argv);
        }
        catch (UsageException ex)
        {
            return Usage(ex.Message);
        }

        if (args.Command is "" or "help" || args.Has("--help"))
        {
            await Console.Out.WriteAsync(Help.Text(args.Command is "" or "help" ? args.Positional.FirstOrDefault() : args.Command));
            return args.Command == "" && !args.Has("--help") ? UsageError : Ok;
        }
        if (!Commands.Contains(args.Command))
            return Usage($"unknown command '{args.Command}'");
        if (args.Command == "update")
            return await UpdateCommand(args);

        var cmd = ExifToolLocator.Locate(args.Value("--exiftool") ?? AppSettings.Load().ExifToolPath)
                  ?? ExifToolLocator.FindBundled(AppContext.BaseDirectory);
        if (cmd == null)
        {
            await Console.Error.WriteLineAsync(ExifToolLocator.BundleNeedsPerl()
                ? "taoexif-cli: the bundled exiftool needs perl, which was not found. Install perl or exiftool."
                : "taoexif-cli: exiftool not found. Install it or pass --exiftool PATH.");
            return NoExifTool;
        }

        using var exif = new ExifToolService(cmd);
        try
        {
            return await Run(args, exif);
        }
        catch (UsageException ex)
        {
            return Usage(ex.Message);
        }
        catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
        {
            await Console.Error.WriteLineAsync("taoexif-cli: " + ex.Message);
            return ExifToolErrors;
        }
    }

    private static int Usage(string message)
    {
        Console.Error.WriteLine("taoexif-cli: " + message);
        Console.Error.WriteLine("Run 'taoexif-cli help' for usage.");
        return UsageError;
    }

    private static async Task<int> Run(CliArgs a, ExifToolService exif)
    {
        if (a.Command == "version")
        {
            string ver = await exif.GetVersionAsync();
            if (a.Has("--json"))
            {
                WriteJson(w =>
                {
                    w.WriteStartObject();
                    w.WriteString("taoexif", UpdateChecker.CurrentVersion.ToString(3));
                    w.WriteString("exiftool", ver);
                    w.WriteString("exiftoolPath", exif.ExePath);
                    w.WriteString("exiftoolSource", exif.Command.SourceText);
                    w.WriteEndObject();
                });
            }
            else
            {
                Console.WriteLine($"taoexif-cli {UpdateChecker.CurrentVersion.ToString(3)}");
                Console.WriteLine($"exiftool {ver} ({exif.Command.SourceText}) {exif.ExePath}");
            }
            return Ok;
        }

        if (a.Command == "run")
        {
            var files = a.Positional.Count > 0 ? FileArgs.Expand(a.Positional, a.Has("--recursive"), null) : [];
            if (a.Raw.Count == 0)
                throw new UsageException("run needs exiftool arguments, e.g. run -G1 -s -time:all -- photo.jpg");
            return Emit(await exif.RunArgsAsync(a.Raw, files), a);
        }

        string gpsAction = "";
        var positional = a.Positional.ToList();
        if (a.Command == "gps")
        {
            gpsAction = positional.Count > 0 ? positional[0].ToLowerInvariant() : "";
            if (positional.Count > 0)
                positional.RemoveAt(0);
        }
        var paths = FileArgs.Expand(positional, a.Has("--recursive"), a.List("--ext"));
        if (paths.Count == 0)
            throw new UsageException("no files given (or none matched)");
        var wo = new WriteOptions(!a.Has("--keep-backup"), a.Has("--preserve-dates"));

        return a.Command switch
        {
            "info" => await Info(a, exif, paths),
            "get" => await Get(a, exif, paths),
            "set" => Emit(await exif.WriteTagsAsync(paths, ParseWrites(a), wo), a),
            "remove" => await Remove(a, exif, paths, wo),
            "shift" => await Shift(a, exif, paths, wo),
            "rename" => await Rename(a, exif, paths),
            "gps" => await Gps(a, gpsAction, exif, paths, wo),
            "geotag" => Emit(await exif.GeotagAsync(paths, a.Required("--track"), a.Value("--sync"), wo), a),
            "copy" => await Copy(a, exif, paths, wo),
            "export" => await Export(a, exif, paths),
            _ => throw new UsageException("unknown command " + a.Command),
        };
    }

    // ---------- commands ----------

    private static async Task<int> Info(CliArgs a, ExifToolService exif, List<string> paths)
    {
        var opts = new ReadOptions(a.Has("--duplicates"), false, a.Has("--numeric"));
        string? group = a.Value("--group");
        var all = new List<(string File, List<TagEntry> Tags)>();
        foreach (var p in paths)
        {
            var tags = await exif.ReadMetadataAsync(p, opts);
            if (group != null)
                tags = tags.Where(t => t.Family0.Equals(group, StringComparison.OrdinalIgnoreCase) || t.Family1.Equals(group, StringComparison.OrdinalIgnoreCase)).ToList();
            all.Add((p, tags));
        }

        if (a.Has("--json"))
        {
            WriteJson(w =>
            {
                w.WriteStartArray();
                foreach (var (file, tags) in all)
                {
                    w.WriteStartObject();
                    w.WriteString("file", file);
                    w.WriteStartArray("tags");
                    foreach (var t in tags)
                    {
                        w.WriteStartObject();
                        w.WriteString("group0", t.Family0);
                        w.WriteString("group1", t.Family1);
                        w.WriteString("name", t.Name);
                        w.WriteString("value", t.Value);
                        w.WriteEndObject();
                    }
                    w.WriteEndArray();
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            });
            return Ok;
        }

        foreach (var (file, tags) in all)
        {
            if (paths.Count > 1)
                Console.WriteLine("======== " + file);
            int width = tags.Count == 0 ? 0 : tags.Max(t => t.WriteName.Length);
            foreach (var t in tags)
                Console.WriteLine($"{t.WriteName.PadRight(width)} : {t.Value}");
        }
        return Ok;
    }

    private static async Task<int> Get(CliArgs a, ExifToolService exif, List<string> paths)
    {
        var tags = a.Values("--tag").Concat(a.List("--tags")).ToList();
        if (tags.Count == 0)
            throw new UsageException("get needs -t TAG (repeatable) or --tags A,B");
        var data = await exif.ReadTagsAsync(paths, tags);

        if (a.Has("--json"))
        {
            WriteJson(w =>
            {
                w.WriteStartArray();
                foreach (var p in paths)
                {
                    w.WriteStartObject();
                    w.WriteString("file", p);
                    foreach (var t in tags)
                    {
                        string? v = data.TryGetValue(p, out var row) ? ExifToolService.Lookup(row, t) : null;
                        if (v == null)
                            w.WriteNull(t);
                        else
                            w.WriteString(t, v);
                    }
                    w.WriteEndObject();
                }
                w.WriteEndArray();
            });
            return Ok;
        }

        foreach (var p in paths)
        {
            var row = data.GetValueOrDefault(p);
            var values = tags.Select(t => row == null ? "" : ExifToolService.Lookup(row, t) ?? "");
            // tab-separated: file, then one column per tag (easy to cut/awk/import)
            Console.WriteLine(string.Join('\t', new[] { p }.Concat(values)));
        }
        return Ok;
    }

    internal static List<TagWrite> ParseWrites(CliArgs a)
    {
        var writes = new List<TagWrite>();
        foreach (var t in a.Values("--tag"))
        {
            int eq = t.IndexOf('=');
            if (eq <= 0)
                throw new UsageException($"set needs -t TAG=VALUE (got '{t}'); an empty VALUE deletes the tag");
            writes.Add(new TagWrite(t[..eq].Trim(), t[(eq + 1)..]));
        }
        if (writes.Count == 0)
            throw new UsageException("set needs at least one -t TAG=VALUE");
        return writes;
    }

    private static async Task<int> Remove(CliArgs a, ExifToolService exif, List<string> paths, WriteOptions wo)
    {
        bool all = a.Has("--all");
        var groups = all ? a.List("--keep") : a.List("--groups");
        if (!all && groups.Count == 0)
            throw new UsageException("remove needs --all [--keep GROUPS] or --groups GROUPS");
        if (!a.Has("--quiet"))
            await Console.Error.WriteLineAsync("exiftool " + string.Join(" ", ExifToolService.RemoveArgs(all, groups)));
        return Emit(await exif.RemoveGroupsAsync(paths, all, groups, wo), a);
    }

    private static async Task<int> Shift(CliArgs a, ExifToolService exif, List<string> paths, WriteOptions wo)
    {
        string by = a.Required("--by").Trim();
        bool forward = !by.StartsWith('-');
        by = by.TrimStart('+', '-');
        var tags = a.List("--tags");
        if (tags.Count == 0)
            tags = ["AllDates"];
        return Emit(await exif.ShiftDatesAsync(paths, tags, by, forward, wo), a);
    }

    private static async Task<int> Rename(CliArgs a, ExifToolService exif, List<string> paths)
    {
        bool dry = a.Has("--dry-run");
        ExifToolResult r;
        if (a.Value("--template") is { } template)
            r = await exif.RenameTemplateAsync(paths, template, dry);
        else
            r = await exif.RenameAsync(paths, a.Value("--date-tag") ?? "DateTimeOriginal", a.Value("--format") ?? "%Y%m%d_%H%M%S%%-c.%%e", dry);
        return Emit(r, a);
    }

    private static async Task<int> Gps(CliArgs a, string action, ExifToolService exif, List<string> paths, WriteOptions wo)
    {
        switch (action)
        {
            case "set":
                double lat = ParseCoord(a.Required("--lat"), 90, "--lat");
                double lon = ParseCoord(a.Required("--lon"), 180, "--lon");
                double? alt = a.Value("--alt") is { } s ? ParseCoord(s, 100000, "--alt") : null;
                return Emit(await exif.SetGpsAsync(paths, new GeoPoint(lat, lon, alt), a.Has("--xmp"), wo), a);
            case "clear":
                return Emit(await exif.SetGpsAsync(paths, null, true, wo), a);
            case "show":
                var gps = await exif.ReadGpsAsync(paths);
                if (a.Has("--json"))
                {
                    WriteJson(w =>
                    {
                        w.WriteStartArray();
                        foreach (var p in paths)
                        {
                            w.WriteStartObject();
                            w.WriteString("file", p);
                            if (gps.TryGetValue(p, out var g))
                            {
                                w.WriteNumber("latitude", g.Latitude);
                                w.WriteNumber("longitude", g.Longitude);
                                if (g.Altitude is { } al)
                                    w.WriteNumber("altitude", al);
                            }
                            else
                            {
                                w.WriteNull("latitude");
                                w.WriteNull("longitude");
                            }
                            w.WriteEndObject();
                        }
                        w.WriteEndArray();
                    });
                }
                else
                {
                    foreach (var p in paths)
                        Console.WriteLine(gps.TryGetValue(p, out var g) ? $"{p}	{Invariant(g.Latitude)}	{Invariant(g.Longitude)}" : $"{p}		");
                }
                return Ok;
            default:
                throw new UsageException("gps needs an action: gps set --lat LAT --lon LON FILES | gps clear FILES | gps show FILES");
        }
    }

    private static double ParseCoord(string text, double limit, string option) =>
        double.TryParse(text, NumberStyles.Float, CultureInfo.InvariantCulture, out double v) && Math.Abs(v) <= limit
            ? v
            : throw new UsageException($"{option} must be a decimal number (got '{text}')");

    private static int Emit(ExifToolResult r, CliArgs a)
    {
        string text = r.Text.TrimEnd();
        if (text.Length > 0 && !a.Has("--quiet"))
            Console.WriteLine(text);
        if (r.Errors.Length > 0)
            Console.Error.WriteLine(r.Errors);
        return HasErrors(r) ? ExifToolErrors : Ok;
    }

    internal static bool HasErrors(ExifToolResult r) =>
        r.Errors.Split('\n').Any(l => l.TrimStart().StartsWith("Error", StringComparison.Ordinal))
        || r.Text.Contains("files weren't updated due to errors", StringComparison.Ordinal)
        || r.Errors.Contains("Nothing to do.", StringComparison.Ordinal)
        || r.Text.Contains("Nothing to do.", StringComparison.Ordinal);

    private static async Task<int> Copy(CliArgs a, ExifToolService exif, List<string> paths, WriteOptions wo)
    {
        string source = a.Required("--from");
        var groups = a.List("--groups");
        MetaGroups g = MetaGroups.None;
        foreach (var name in groups)
        {
            g |= name.ToUpperInvariant() switch
            {
                "EXIF" => MetaGroups.Exif,
                "XMP" => MetaGroups.Xmp,
                "IPTC" => MetaGroups.Iptc,
                "GPS" => MetaGroups.Gps,
                "MAKERNOTES" => MetaGroups.MakerNotes,
                "ICC" or "ICC_PROFILE" => MetaGroups.Icc,
                _ => throw new UsageException($"unknown group '{name}' (EXIF, XMP, IPTC, GPS, MakerNotes, ICC)"),
            };
        }
        if (g == MetaGroups.None)
            g = MetaGroups.All;
        var targets = paths.Where(p => !ExifToolService.PathComparer.Equals(p, Path.GetFullPath(source))).ToList();
        return Emit(await exif.CopyMetadataAsync(source, targets, g, wo), a);
    }

    private static async Task<int> Export(CliArgs a, ExifToolService exif, List<string> paths)
    {
        bool csv = a.Has("--csv");
        string text = await exif.ExportAsync(paths, csv);
        if (a.Value("--output") is { } output)
        {
            await File.WriteAllTextAsync(output, text, new UTF8Encoding(false));
            if (!a.Has("--quiet"))
                await Console.Error.WriteLineAsync($"wrote {paths.Count} file(s) to {output}");
        }
        else
        {
            Console.Write(text);
        }
        return Ok;
    }

    private static async Task<int> UpdateCommand(CliArgs a)
    {
        var checker = new UpdateChecker();
        ReleaseInfo? latest;
        try
        {
            latest = await checker.GetLatestAsync();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException)
        {
            await Console.Error.WriteLineAsync("taoexif-cli: update check failed: " + ex.Message);
            return ExifToolErrors;
        }
        var current = UpdateChecker.CurrentVersion;
        bool newer = latest != null && UpdateChecker.IsNewer(latest.Version, current);
        if (a.Has("--json"))
        {
            WriteJson(w =>
            {
                w.WriteStartObject();
                w.WriteString("current", current.ToString(3));
                w.WriteString("latest", latest?.Version.ToString(3));
                w.WriteBoolean("updateAvailable", newer);
                w.WriteString("url", latest?.Page.ToString());
                w.WriteEndObject();
            });
        }
        else if (newer)
        {
            Console.WriteLine($"TaoExif {latest!.Version.ToString(3)} is available (you have {current.ToString(3)}): {latest.Page}");
        }
        else
        {
            Console.WriteLine($"TaoExif {current.ToString(3)} is the latest version.");
        }
        // exit code 10 lets scripts react: "taoexif-cli update --check || ..."
        return newer && a.Has("--check") ? 10 : Ok;
    }

    // ---------- helpers ----------

    private static void WriteJson(Action<Utf8JsonWriter> write)
    {
        using var stdout = Console.OpenStandardOutput();
        using (var w = new Utf8JsonWriter(stdout, new JsonWriterOptions { Indented = true, Encoder = System.Text.Encodings.Web.JavaScriptEncoder.UnsafeRelaxedJsonEscaping }))
        {
            write(w);
        }
        stdout.Write("\n"u8);
    }

    internal static string Invariant(double v) => v.ToString("0.########", CultureInfo.InvariantCulture);
}
