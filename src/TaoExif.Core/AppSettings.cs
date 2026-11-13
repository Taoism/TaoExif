using System.Text.Json;
using System.Text.Json.Serialization;

namespace TaoExif.Core;

/// <summary>A file list column: an exiftool tag spec and its header.</summary>
public sealed class ColumnDef
{
    public string Header { get; set; } = "";
    public string Tag { get; set; } = "";
    public double Width { get; set; } = 120;
}

/// <summary>A workspace row: a tag the user edits often.</summary>
public sealed class WorkspaceDef
{
    public string Label { get; set; } = "";
    public string Tag { get; set; } = "";
}

/// <summary>A saved exiftool command.</summary>
public sealed class CommandPreset
{
    public string Name { get; set; } = "";
    public string Arguments { get; set; } = "";
}

public sealed class AppSettings
{
    public string? ExifToolPath { get; set; }
    public string? LastFolder { get; set; }
    public bool ThumbnailView { get; set; }
    public bool OverwriteOriginal { get; set; } = true;
    public bool PreserveFileDates { get; set; }
    public bool ShowDuplicates { get; set; } = true;
    public bool ShowUnknown { get; set; }
    public bool NumericValues { get; set; }
    public bool IncludeSubfolders { get; set; }
    public double WindowX { get; set; } = double.NaN;
    public double WindowY { get; set; } = double.NaN;
    public double WindowWidth { get; set; } = 1400;
    public double WindowHeight { get; set; } = 860;
    public bool WindowMaximized { get; set; }
    public double TreeWidth { get; set; } = 240;
    public double RightWidth { get; set; } = 520;
    public double PreviewHeight { get; set; } = 300;
    public double MapLatitude { get; set; } = 48.2;
    public double MapLongitude { get; set; } = 16.37;
    public int MapZoom { get; set; } = 4;
    public string RenameFormat { get; set; } = "%Y%m%d_%H%M%S%%-c.%%e";
    public string RenameTag { get; set; } = "DateTimeOriginal";
    public List<string> RecentFolders { get; set; } = [];
    public bool RemoveMetadataAll { get; set; } = true;
    public List<string> RemoveMetadataUnchecked { get; set; } = ["ICC_Profile"];

    public List<ColumnDef> Columns { get; set; } = DefaultColumns();
    public List<WorkspaceDef> Workspace { get; set; } = DefaultWorkspace();
    public List<CommandPreset> Commands { get; set; } = DefaultCommands();

    public static List<ColumnDef> DefaultColumns() =>
    [
        new() { Header = "Date taken", Tag = "DateTimeOriginal", Width = 150 },
        new() { Header = "Camera", Tag = "Model", Width = 130 },
        new() { Header = "Dimensions", Tag = "ImageSize", Width = 90 },
        new() { Header = "Exposure", Tag = "ExposureTime", Width = 80 },
        new() { Header = "F", Tag = "FNumber", Width = 50 },
        new() { Header = "ISO", Tag = "ISO", Width = 55 },
        new() { Header = "Focal", Tag = "FocalLength", Width = 70 },
    ];

    public static List<WorkspaceDef> DefaultWorkspace() =>
    [
        new() { Label = "Title", Tag = "XMP-dc:Title" },
        new() { Label = "Description", Tag = "XMP-dc:Description" },
        new() { Label = "Keywords", Tag = "XMP-dc:Subject" },
        new() { Label = "Creator", Tag = "XMP-dc:Creator" },
        new() { Label = "Copyright", Tag = "XMP-dc:Rights" },
        new() { Label = "Rating", Tag = "XMP-xmp:Rating" },
        new() { Label = "Artist (EXIF)", Tag = "IFD0:Artist" },
        new() { Label = "Copyright (EXIF)", Tag = "IFD0:Copyright" },
        new() { Label = "Date taken", Tag = "ExifIFD:DateTimeOriginal" },
        new() { Label = "Camera make", Tag = "IFD0:Make" },
        new() { Label = "Camera model", Tag = "IFD0:Model" },
        new() { Label = "City", Tag = "XMP-photoshop:City" },
        new() { Label = "Country", Tag = "XMP-photoshop:Country" },
    ];

    public static List<CommandPreset> DefaultCommands() =>
    [
        new() { Name = "Show all dates", Arguments = "-a -G1 -s -time:all" },
        new() { Name = "Show GPS", Arguments = "-G1 -s -gps:all" },
        new() { Name = "Validate", Arguments = "-validate -warning -error -a" },
        new() { Name = "Reset orientation flag", Arguments = "-Orientation=1 -n -overwrite_original" },
        new() { Name = "File date from DateTimeOriginal", Arguments = "\"-FileModifyDate<DateTimeOriginal\" -overwrite_original" },
        new() { Name = "List makernotes", Arguments = "-G1 -s -makernotes:all" },
    ];

    private static readonly JsonSerializerOptions JsonOptions = new()
    {
        WriteIndented = true,
        NumberHandling = JsonNumberHandling.AllowNamedFloatingPointLiterals,
    };

    public static string ConfigDirectory =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "TaoExif");

    public static string DefaultPath => Path.Combine(ConfigDirectory, "settings.json");

    public static AppSettings Load(string? path = null)
    {
        path ??= DefaultPath;
        try
        {
            if (File.Exists(path))
                return JsonSerializer.Deserialize<AppSettings>(File.ReadAllText(path), JsonOptions) ?? new AppSettings();
        }
        catch (Exception ex) when (ex is IOException or JsonException or UnauthorizedAccessException)
        {
            // fall back to defaults; a broken settings file must not stop the app
        }
        return new AppSettings();
    }

    public void Save(string? path = null)
    {
        path ??= DefaultPath;
        Directory.CreateDirectory(Path.GetDirectoryName(path)!);
        string tmp = path + ".tmp";
        File.WriteAllText(tmp, JsonSerializer.Serialize(this, JsonOptions));
        File.Move(tmp, path, true);
    }
}
