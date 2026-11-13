using System.Globalization;
using System.Text.Json;

namespace TaoExif.Core;

/// <summary>Options that change how metadata is read.</summary>
public sealed record ReadOptions(bool Duplicates = true, bool Unknown = false, bool Numeric = false);

/// <summary>Options that change how files are written.</summary>
public sealed record WriteOptions(bool OverwriteOriginal = true, bool PreserveFileDates = false);

/// <summary>Which metadata families a copy/remove operation touches.</summary>
[Flags]
public enum MetaGroups
{
    None = 0,
    Exif = 1,
    Xmp = 2,
    Iptc = 4,
    Gps = 8,
    MakerNotes = 16,
    Icc = 32,
    All = Exif | Xmp | Iptc | Gps | MakerNotes | Icc,
}

/// <summary>A GPS position read from a file.</summary>
public readonly record struct GeoPoint(double Latitude, double Longitude, double? Altitude = null);

/// <summary>High level exiftool operations used by the GUI.</summary>
public sealed class ExifToolService : IDisposable
{
    private readonly ExifToolProcess _main;
    private readonly ExifToolProcess _thumbs;

    public ExifToolService(ExifToolCommand command)
    {
        Command = command;
        _main = new ExifToolProcess(command);
        // A second process so thumbnail extraction does not queue behind metadata reads and writes.
        _thumbs = new ExifToolProcess(command);
    }

    public ExifToolCommand Command { get; }

    public string ExePath => Command.ExifToolPath;

    public async Task<string> GetVersionAsync(CancellationToken ct = default)
    {
        var r = await _main.ExecuteAsync(["-ver"], ct).ConfigureAwait(false);
        return r.Text.Trim();
    }

    /// <summary>All tags of one file, in exiftool's order.</summary>
    public async Task<List<TagEntry>> ReadMetadataAsync(string file, ReadOptions options, CancellationToken ct = default)
    {
        var args = new List<string> { "-json", "-G0:1", "-s", "-api", "largefilesupport=1" };
        if (options.Duplicates)
            args.Add("-a");
        if (options.Unknown)
            args.Add("-u");
        if (options.Numeric)
            args.Add("-n");
        args.Add(file);

        var r = await _main.ExecuteAsync(args, ct).ConfigureAwait(false);
        return ParseMetadataJson(r.Text);
    }

    internal static List<TagEntry> ParseMetadataJson(string json)
    {
        var list = new List<TagEntry>();
        if (string.IsNullOrWhiteSpace(json))
            return list;

        using var doc = JsonDocument.Parse(json);
        if (doc.RootElement.ValueKind != JsonValueKind.Array || doc.RootElement.GetArrayLength() == 0)
            return list;

        foreach (var prop in doc.RootElement[0].EnumerateObject())
        {
            if (prop.Name == "SourceFile")
                continue;
            var parts = prop.Name.Split(':');
            string g0 = "", g1 = "", name;
            switch (parts.Length)
            {
                case 1:
                    name = parts[0];
                    break;
                case 2:
                    g0 = g1 = parts[0];
                    name = parts[1];
                    break;
                default:
                    g0 = parts[0];
                    g1 = parts[1];
                    name = parts[^1];
                    break;
            }
            list.Add(new TagEntry(g0, g1, name, JsonToText(prop.Value)));
        }
        return list;
    }

    internal static string JsonToText(JsonElement e) => e.ValueKind switch
    {
        JsonValueKind.String => e.GetString() ?? "",
        JsonValueKind.Number => e.GetRawText(),
        JsonValueKind.True => "true",
        JsonValueKind.False => "false",
        JsonValueKind.Null => "",
        JsonValueKind.Array => string.Join(", ", e.EnumerateArray().Select(JsonToText)),
        _ => e.GetRawText(),
    };

    /// <summary>Reads the given tags for many files at once (for file list columns).</summary>
    public async Task<Dictionary<string, Dictionary<string, string>>> ReadTagsAsync(
        IReadOnlyList<string> files, IReadOnlyList<string> tags, CancellationToken ct = default, bool numeric = false)
    {
        var result = new Dictionary<string, Dictionary<string, string>>(PathComparer);
        if (files.Count == 0 || tags.Count == 0)
            return result;

        var args = new List<string> { "-json", "-fast", "-s", "-G1" };
        if (numeric)
            args.Add("-n");
        args.AddRange(tags.Select(t => "-" + t));
        args.AddRange(files);
        var r = await _main.ExecuteAsync(args, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(r.Text))
            return result;

        using var doc = JsonDocument.Parse(r.Text);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (!item.TryGetProperty("SourceFile", out var src))
                continue;
            var values = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
            foreach (var prop in item.EnumerateObject())
            {
                if (prop.Name != "SourceFile")
                    values[prop.Name] = JsonToText(prop.Value);
            }
            result[NormalizePath(src.GetString() ?? "")] = values;
        }
        return result;
    }

    /// <summary>
    /// Finds the value for a tag spec ("G0:G1:Tag", "G1:Tag", "G0:Tag" or "Tag") in a <see cref="ReadTagsAsync"/> row,
    /// whose keys are "G1:Tag": an exact group match first, then the first tag with that name.
    /// </summary>
    public static string? Lookup(IReadOnlyDictionary<string, string> values, string spec)
    {
        var parts = spec.Split(':');
        string name = parts[^1];
        if (parts.Length >= 2 && values.TryGetValue(parts[^2] + ":" + name, out var exact))
            return exact;
        foreach (var kv in values)
        {
            if (BareTagName(kv.Key).Equals(name, StringComparison.OrdinalIgnoreCase))
                return kv.Value;
        }
        return null;
    }

    /// <summary>The bare tag name of "G0:G1:Tag" / "G1:Tag" / "Tag".</summary>
    public static string BareTagName(string spec)
    {
        int i = spec.LastIndexOf(':');
        return i >= 0 ? spec[(i + 1)..] : spec;
    }

    public static StringComparer PathComparer => OperatingSystem.IsWindows() ? StringComparer.OrdinalIgnoreCase : StringComparer.Ordinal;

    /// <summary>exiftool reports paths with forward slashes; normalise so they match <see cref="Path.GetFullPath(string)"/>.</summary>
    public static string NormalizePath(string path) => Path.GetFullPath(path);

    /// <summary>Extracts a binary tag (e.g. PreviewImage); null when the file has none.</summary>
    public async Task<byte[]?> ExtractBinaryAsync(string file, string tag, CancellationToken ct = default)
    {
        var r = await _thumbs.ExecuteAsync(["-b", "-" + tag, file], ct).ConfigureAwait(false);
        return r.Output.Length > 0 ? r.Output : null;
    }

    /// <summary>The largest embedded JPEG: PreviewImage, JpgFromRaw, then ThumbnailImage.</summary>
    public async Task<byte[]?> ExtractEmbeddedImageAsync(string file, bool smallFirst, CancellationToken ct = default)
    {
        string[] order = smallFirst
            ? ["ThumbnailImage", "PreviewImage", "JpgFromRaw"]
            : ["PreviewImage", "JpgFromRaw", "OtherImage", "ThumbnailImage"];
        foreach (var tag in order)
        {
            var bytes = await ExtractBinaryAsync(file, tag, ct).ConfigureAwait(false);
            if (bytes is { Length: > 100 })
                return bytes;
        }
        return null;
    }

    /// <summary>EXIF orientation 1-8 (1 when absent).</summary>
    public async Task<int> ReadOrientationAsync(string file, CancellationToken ct = default)
    {
        var r = await _thumbs.ExecuteAsync(["-n", "-s3", "-Orientation", file], ct).ConfigureAwait(false);
        return int.TryParse(r.Text.Trim(), NumberStyles.Integer, CultureInfo.InvariantCulture, out int o) && o is >= 1 and <= 8 ? o : 1;
    }

    public async Task<Dictionary<string, GeoPoint>> ReadGpsAsync(IReadOnlyList<string> files, CancellationToken ct = default)
    {
        var result = new Dictionary<string, GeoPoint>(PathComparer);
        if (files.Count == 0)
            return result;
        var args = new List<string> { "-json", "-n", "-fast", "-GPSLatitude", "-GPSLongitude", "-GPSAltitude" };
        args.AddRange(files);
        var r = await _main.ExecuteAsync(args, ct).ConfigureAwait(false);
        if (string.IsNullOrWhiteSpace(r.Text))
            return result;
        using var doc = JsonDocument.Parse(r.Text);
        foreach (var item in doc.RootElement.EnumerateArray())
        {
            if (item.TryGetProperty("SourceFile", out var src)
                && TryNumber(item, "GPSLatitude", out double lat)
                && TryNumber(item, "GPSLongitude", out double lon))
            {
                double? alt = TryNumber(item, "GPSAltitude", out double a) ? a : null;
                result[NormalizePath(src.GetString() ?? "")] = new GeoPoint(lat, lon, alt);
            }
        }
        return result;
    }

    private static bool TryNumber(JsonElement item, string name, out double value)
    {
        value = 0;
        if (!item.TryGetProperty(name, out var p))
            return false;
        if (p.ValueKind == JsonValueKind.Number)
            return p.TryGetDouble(out value);
        return p.ValueKind == JsonValueKind.String
            && double.TryParse(p.GetString(), NumberStyles.Float, CultureInfo.InvariantCulture, out value);
    }

    // ---------- writing ----------

    private static List<string> WriteArgs(WriteOptions o)
    {
        var args = new List<string>();
        if (o.OverwriteOriginal)
            args.Add("-overwrite_original");
        if (o.PreserveFileDates)
            args.Add("-P");
        return args;
    }

    public Task<ExifToolResult> WriteTagsAsync(IReadOnlyList<string> files, IEnumerable<TagWrite> writes, WriteOptions o, CancellationToken ct = default)
    {
        var args = WriteArgs(o);
        // list tags (keywords...) are shown joined with ", "; split them the same way when writing
        args.Add("-sep");
        args.Add(", ");
        args.AddRange(writes.Select(w => "-" + w.Tag + "=" + (w.Value ?? "")));
        args.AddRange(files);
        return _main.ExecuteAsync(args, ct);
    }

    internal static IEnumerable<string> GroupSpecs(MetaGroups g)
    {
        if (g.HasFlag(MetaGroups.Exif))
            yield return "EXIF";
        if (g.HasFlag(MetaGroups.Xmp))
            yield return "XMP";
        if (g.HasFlag(MetaGroups.Iptc))
            yield return "IPTC";
        if (g.HasFlag(MetaGroups.Gps))
            yield return "GPS";
        if (g.HasFlag(MetaGroups.MakerNotes))
            yield return "MakerNotes";
        if (g.HasFlag(MetaGroups.Icc))
            yield return "ICC_Profile";
    }

    /// <summary>
    /// Arguments for removing metadata by group name (EXIF, GPS, XMP-crs, ICC_Profile ...).
    /// With <paramref name="removeAll"/> everything goes ("-all=") except the <paramref name="groups"/>, which are kept;
    /// otherwise only the <paramref name="groups"/> are removed.
    /// </summary>
    public static List<string> RemoveArgs(bool removeAll, IEnumerable<string> groups)
    {
        var list = groups.Where(g => !string.IsNullOrWhiteSpace(g)).Distinct(StringComparer.OrdinalIgnoreCase).ToList();
        if (removeAll)
        {
            // "-all= --GPS:all" would also keep the IFD0 tags the GPS block hangs off (Make, Model ...),
            // so delete everything and copy the kept groups back, as the exiftool docs recommend.
            var args = new List<string> { "-all=" };
            if (list.Count > 0)
            {
                args.Add("-tagsFromFile");
                args.Add("@");
                args.AddRange(list.Select(KeepSpec));
            }
            return args;
        }
        return list.Select(g => "-" + g + ":all=").ToList();
    }

    /// <summary>The copy-back argument for a kept group; ICC profiles and maker notes go as whole blocks.</summary>
    private static string KeepSpec(string group) => group.ToUpperInvariant() switch
    {
        "ICC_PROFILE" => "-ICC_Profile",
        "MAKERNOTES" => "-MakerNotes",
        _ => "-" + group + ":all",
    };

    /// <summary>Removes metadata as described by <see cref="RemoveArgs"/>.</summary>
    public Task<ExifToolResult> RemoveGroupsAsync(IReadOnlyList<string> files, bool removeAll, IEnumerable<string> groups, WriteOptions o, CancellationToken ct = default)
    {
        var remove = RemoveArgs(removeAll, groups);
        if (remove.Count == 0)
            throw new ArgumentException("Nothing to remove.", nameof(groups));
        var args = WriteArgs(o);
        args.AddRange(remove);
        args.AddRange(files);
        return _main.ExecuteAsync(args, ct);
    }

    /// <summary>Copies metadata from <paramref name="source"/> into every target.</summary>
    public Task<ExifToolResult> CopyMetadataAsync(string source, IReadOnlyList<string> targets, MetaGroups groups, WriteOptions o, CancellationToken ct = default)
    {
        var args = WriteArgs(o);
        args.Add("-tagsFromFile");
        args.Add(source);
        if (groups == MetaGroups.All)
        {
            args.Add("-all:all");
        }
        else
        {
            args.AddRange(GroupSpecs(groups).Select(s => "-" + s + ":all"));
        }
        args.AddRange(targets);
        return _main.ExecuteAsync(args, ct);
    }

    /// <summary>Removes the chosen groups, or everything (optionally keeping the colour profile).</summary>
    public Task<ExifToolResult> RemoveMetadataAsync(IReadOnlyList<string> files, MetaGroups groups, bool keepIcc, WriteOptions o, CancellationToken ct = default)
    {
        var args = WriteArgs(o);
        if (groups == MetaGroups.All)
        {
            args.Add("-all=");
            if (keepIcc)
                args.Add("--ICC_Profile:all");
        }
        else
        {
            args.AddRange(GroupSpecs(groups).Select(s => "-" + s + ":all="));
        }
        args.AddRange(files);
        return _main.ExecuteAsync(args, ct);
    }

    /// <summary>Formats a shift for exiftool's date arithmetic ("Y:M:D h:m:s").</summary>
    public static string FormatShift(int years, int months, int days, int hours, int minutes, int seconds) =>
        string.Create(CultureInfo.InvariantCulture, $"{years}:{months}:{days} {hours}:{minutes}:{seconds}");

    /// <summary>Shifts the given date tags (e.g. "AllDates") forward or back.</summary>
    public Task<ExifToolResult> ShiftDatesAsync(IReadOnlyList<string> files, IEnumerable<string> tags, string shift, bool forward, WriteOptions o, CancellationToken ct = default)
    {
        var args = WriteArgs(o);
        string op = forward ? "+=" : "-=";
        args.AddRange(tags.Select(t => "-" + t + op + shift));
        args.AddRange(files);
        return _main.ExecuteAsync(args, ct);
    }

    /// <summary>
    /// Renames files from a date tag. <paramref name="dateFormat"/> is an exiftool -d format such as
    /// "%Y%m%d_%H%M%S%%-c.%%e". With <paramref name="dryRun"/> only TestName is reported.
    /// </summary>
    public Task<ExifToolResult> RenameAsync(IReadOnlyList<string> files, string sourceTag, string dateFormat, bool dryRun, CancellationToken ct = default)
    {
        var args = new List<string> { "-d", dateFormat, (dryRun ? "-TestName<" : "-FileName<") + sourceTag };
        args.AddRange(files);
        return _main.ExecuteAsync(args, ct);
    }

    /// <summary>Renames using a full exiftool name template, e.g. "${Model}_%f.%e".</summary>
    public Task<ExifToolResult> RenameTemplateAsync(IReadOnlyList<string> files, string template, bool dryRun, CancellationToken ct = default)
    {
        var args = new List<string> { (dryRun ? "-TestName<" : "-FileName<") + template };
        args.AddRange(files);
        return _main.ExecuteAsync(args, ct);
    }

    /// <summary>Geotags files from a GPX/KML/NMEA log. <paramref name="geosync"/> is e.g. "+1:00:00" or null.</summary>
    public Task<ExifToolResult> GeotagAsync(IReadOnlyList<string> files, string trackLog, string? geosync, WriteOptions o, CancellationToken ct = default)
    {
        var args = WriteArgs(o);
        args.Add("-geotag");
        args.Add(trackLog);
        if (!string.IsNullOrWhiteSpace(geosync))
            args.Add("-geosync=" + geosync.Trim());
        args.AddRange(files);
        return _main.ExecuteAsync(args, ct);
    }

    /// <summary>Writes EXIF GPS (and XMP when <paramref name="alsoXmp"/>) for a point; null clears GPS.</summary>
    public Task<ExifToolResult> SetGpsAsync(IReadOnlyList<string> files, GeoPoint? point, bool alsoXmp, WriteOptions o, CancellationToken ct = default)
    {
        var args = WriteArgs(o);
        args.AddRange(GpsWriteArgs(point, alsoXmp));
        args.AddRange(files);
        return _main.ExecuteAsync(args, ct);
    }

    internal static IEnumerable<string> GpsWriteArgs(GeoPoint? point, bool alsoXmp)
    {
        if (point is not { } p)
        {
            yield return "-GPS:all=";
            if (alsoXmp)
            {
                yield return "-XMP-exif:GPSLatitude=";
                yield return "-XMP-exif:GPSLongitude=";
                yield return "-XMP-exif:GPSAltitude=";
            }
            yield break;
        }

        var inv = CultureInfo.InvariantCulture;
        yield return "-GPS:GPSLatitude=" + Math.Abs(p.Latitude).ToString("0.########", inv);
        yield return "-GPS:GPSLatitudeRef=" + (p.Latitude < 0 ? "S" : "N");
        yield return "-GPS:GPSLongitude=" + Math.Abs(p.Longitude).ToString("0.########", inv);
        yield return "-GPS:GPSLongitudeRef=" + (p.Longitude < 0 ? "W" : "E");
        if (p.Altitude is { } alt)
        {
            yield return "-GPS:GPSAltitude=" + Math.Abs(alt).ToString("0.##", inv);
            yield return "-GPS:GPSAltitudeRef=" + (alt < 0 ? "1" : "0");
        }
        if (alsoXmp)
        {
            yield return "-XMP-exif:GPSLatitude=" + p.Latitude.ToString("0.########", inv);
            yield return "-XMP-exif:GPSLongitude=" + p.Longitude.ToString("0.########", inv);
            if (p.Altitude is { } a)
                yield return "-XMP-exif:GPSAltitude=" + a.ToString("0.##", inv);
        }
    }

    /// <summary>All metadata of the files as CSV or JSON text (for export).</summary>
    public async Task<string> ExportAsync(IReadOnlyList<string> files, bool csv, CancellationToken ct = default)
    {
        var args = new List<string> { csv ? "-csv" : "-json", "-G1", "-a", "-s", "-api", "largefilesupport=1" };
        if (!csv)
            args.Add("-struct");
        args.AddRange(files);
        var r = await _main.ExecuteAsync(args, ct).ConfigureAwait(false);
        return r.Text;
    }

    /// <summary>Deletes exiftool's *_original backup files in the folder.</summary>
    public Task<ExifToolResult> DeleteBackupsAsync(string folder, bool recursive, CancellationToken ct = default)
    {
        var args = new List<string> { "-delete_original!" };
        if (recursive)
            args.Add("-r");
        args.Add(folder);
        return _main.ExecuteAsync(args, ct);
    }

    /// <summary>Runs exiftool with these exact arguments, then the files.</summary>
    public Task<ExifToolResult> RunArgsAsync(IReadOnlyList<string> exifArgs, IReadOnlyList<string> files, CancellationToken ct = default)
    {
        var args = new List<string>(exifArgs);
        args.AddRange(files);
        return _main.ExecuteAsync(args, ct);
    }

    /// <summary>Runs a user-typed exiftool command line against the files.</summary>
    public Task<ExifToolResult> RunRawAsync(string commandLine, IReadOnlyList<string> files, CancellationToken ct = default)
    {
        var args = CommandLine.Split(commandLine);
        if (args.Count > 0 && Path.GetFileNameWithoutExtension(args[0]).StartsWith("exiftool", StringComparison.OrdinalIgnoreCase))
            args.RemoveAt(0);
        args.AddRange(files);
        return _main.ExecuteAsync(args, ct);
    }

    public void Dispose()
    {
        _main.Dispose();
        _thumbs.Dispose();
    }
}
