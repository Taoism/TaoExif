using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Media.Imaging;
using CommunityToolkit.Mvvm.ComponentModel;
using TaoExif.Core;

namespace TaoExif.ViewModels;

/// <summary>A file in the file list.</summary>
public sealed partial class FileItem : ObservableObject
{
    public FileItem(FileInfo info, int columnCount)
    {
        FullPath = info.FullName;
        Name = info.Name;
        Size = info.Length;
        Modified = info.LastWriteTime;
        _values = new string[columnCount];
    }

    public string FullPath { get; }
    public string Name { get; }
    public long Size { get; }
    public DateTime Modified { get; }
    public string SizeText => FormatSize(Size);
    public string ModifiedText => Modified.ToString("yyyy-MM-dd HH:mm", CultureInfo.CurrentCulture);
    public string Folder => Path.GetDirectoryName(FullPath) ?? "";

    [ObservableProperty]
    private string[] _values;

    [ObservableProperty]
    private Bitmap? _thumbnail;

    [ObservableProperty]
    private double _rotation;

    [ObservableProperty]
    private bool _thumbnailRequested;

    public GeoPoint? Gps { get; set; }

    public static string FormatSize(long bytes)
    {
        string[] units = ["B", "KB", "MB", "GB", "TB"];
        double v = bytes;
        int u = 0;
        while (v >= 1024 && u < units.Length - 1)
        {
            v /= 1024;
            u++;
        }
        return u == 0 ? bytes.ToString(CultureInfo.CurrentCulture) + " B" : v.ToString("0.#", CultureInfo.CurrentCulture) + " " + units[u];
    }
}

/// <summary>A folder in the folder tree; children load when it is expanded.</summary>
public sealed partial class FolderNode : ObservableObject
{
    private static readonly FolderNode Placeholder = new("…", "", false);
    private bool _loaded;

    public FolderNode(string name, string path, bool lazy = true)
    {
        Name = name;
        FullPath = path;
        if (lazy && HasSubfolders(path))
            Children.Add(Placeholder);
    }

    public string Name { get; }
    public string FullPath { get; }
    public ObservableCollection<FolderNode> Children { get; } = [];

    [ObservableProperty]
    private bool _isExpanded;

    partial void OnIsExpandedChanged(bool value)
    {
        if (value)
            EnsureLoaded();
    }

    public void EnsureLoaded()
    {
        if (_loaded)
            return;
        _loaded = true;
        Children.Clear();
        foreach (var dir in ListSubfolders(FullPath))
            Children.Add(new FolderNode(Path.GetFileName(dir), dir));
    }

    public void Reload()
    {
        _loaded = false;
        if (IsExpanded)
            EnsureLoaded();
    }

    private static readonly EnumerationOptions DirOptions = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
        RecurseSubdirectories = false,
    };

    internal static List<string> ListSubfolders(string path)
    {
        try
        {
            var dirs = Directory.EnumerateDirectories(path, "*", DirOptions)
                .Where(d => !Path.GetFileName(d).StartsWith('.'))
                .ToList();
            dirs.Sort(StringComparer.CurrentCultureIgnoreCase);
            return dirs;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private static bool HasSubfolders(string path)
    {
        try
        {
            return Directory.EnumerateDirectories(path, "*", DirOptions).Any();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return false;
        }
    }
}

/// <summary>One row of the Metadata tab.</summary>
public sealed class MetaRow(TagEntry entry)
{
    public TagEntry Entry { get; } = entry;
    public string Group => Entry.GroupLabel;
    public string Name => Entry.Name;
    public string Value => Entry.Value;
}

/// <summary>One editable row of the Workspace tab.</summary>
public sealed partial class WorkspaceRow : ObservableObject
{
    public WorkspaceRow(WorkspaceDef def)
    {
        Label = def.Label;
        Tag = def.Tag;
    }

    public string Label { get; }
    public string Tag { get; }

    public string Original { get; private set; } = "";

    [ObservableProperty]
    private string _value = "";

    [ObservableProperty]
    private bool _isModified;

    [ObservableProperty]
    private string _hint = "";

    partial void OnValueChanged(string value) => IsModified = value != Original;

    public void Load(string value, string hint)
    {
        Original = value;
        Value = value;
        Hint = hint;
        IsModified = false;
    }
}
