namespace TaoExif.Core;

/// <summary>One metadata value as reported by exiftool -G0:1.</summary>
public sealed record TagEntry(string Family0, string Family1, string Name, string Value)
{
    /// <summary>The tag as exiftool accepts it for writing, e.g. "IFD0:Make" or "XMP-dc:Title".</summary>
    public string WriteName => Family1.Length > 0 ? Family1 + ":" + Name : Name;

    /// <summary>"EXIF / IFD0" style label for display.</summary>
    public string GroupLabel => Family0 == Family1 || Family1.Length == 0 ? Family0 : Family0 + " / " + Family1;

    /// <summary>Groups exiftool cannot write (file system info, composite and so on).</summary>
    public bool IsReadOnly => Family0 switch
    {
        "ExifTool" or "Composite" => true,
        "File" => Name is not ("FileName" or "Directory" or "FileModifyDate" or "FileCreateDate"),
        _ => false,
    };
}

/// <summary>A tag to write; <see cref="Value"/> null or empty deletes the tag.</summary>
public sealed record TagWrite(string Tag, string? Value);
