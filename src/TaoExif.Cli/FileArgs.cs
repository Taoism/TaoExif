using TaoExif.Core;

namespace TaoExif.Cli;

/// <summary>Turns file, folder and wildcard arguments into a list of full paths.</summary>
public static class FileArgs
{
    /// <param name="args">Files, folders, or patterns like *.jpg (expanded here, since cmd.exe does not).</param>
    /// <param name="recursive">Include subfolders of folder arguments.</param>
    /// <param name="extensions">Extensions to take from folders ("jpg,cr2"); empty means the usual media types.</param>
    public static List<string> Expand(IEnumerable<string> args, bool recursive, IReadOnlyCollection<string>? extensions)
    {
        var exts = extensions is { Count: > 0 }
            ? new HashSet<string>(extensions.Select(e => "." + e.TrimStart('*', '.')), StringComparer.OrdinalIgnoreCase)
            : null;
        bool Wanted(string f) => exts?.Contains(Path.GetExtension(f)) ?? MediaFiles.IsMedia(f);

        var seen = new HashSet<string>(ExifToolService.PathComparer);
        var result = new List<string>();
        void Add(string f)
        {
            string full = Path.GetFullPath(f);
            if (seen.Add(full))
                result.Add(full);
        }

        var dirOptions = new EnumerationOptions
        {
            RecurseSubdirectories = recursive,
            IgnoreInaccessible = true,
            AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
            MatchCasing = MatchCasing.CaseInsensitive,
        };

        foreach (var arg in args)
        {
            if (File.Exists(arg))
            {
                Add(arg);
            }
            else if (Directory.Exists(arg))
            {
                var files = Directory.EnumerateFiles(arg, "*", dirOptions)
                    .Where(f => Wanted(f) && !f.EndsWith("_original", StringComparison.OrdinalIgnoreCase))
                    .OrderBy(f => f, StringComparer.OrdinalIgnoreCase);
                foreach (var f in files)
                    Add(f);
            }
            else if (arg.IndexOfAny(['*', '?']) >= 0)
            {
                string dir = Path.GetDirectoryName(arg) is { Length: > 0 } d ? d : ".";
                string pattern = Path.GetFileName(arg);
                if (Directory.Exists(dir))
                {
                    foreach (var f in Directory.EnumerateFiles(dir, pattern, dirOptions).OrderBy(f => f, StringComparer.OrdinalIgnoreCase))
                        Add(f);
                }
            }
            else
            {
                Console.Error.WriteLine("taoexif-cli: not found: " + arg);
            }
        }
        return result;
    }
}
