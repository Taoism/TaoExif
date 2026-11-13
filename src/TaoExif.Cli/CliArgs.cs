namespace TaoExif.Cli;

/// <summary>Thrown for bad command lines; the message is shown with a hint to use --help.</summary>
public sealed class UsageException(string message) : Exception(message);

/// <summary>
/// Small command-line parser: a command name, options (with or without a value, some repeatable)
/// and positional arguments. Option values may start with '-' (negative coordinates, "-1:00:00").
/// </summary>
public sealed class CliArgs
{
    private static readonly Dictionary<string, string> Aliases = new(StringComparer.Ordinal)
    {
        ["-t"] = "--tag", ["-r"] = "--recursive", ["-o"] = "--output", ["-q"] = "--quiet", ["-h"] = "--help",
        ["-n"] = "--numeric", ["-P"] = "--preserve-dates", ["-g"] = "--group",
    };

    private static readonly HashSet<string> ValueOptions = new(StringComparer.Ordinal)
    {
        "--tag", "--tags", "--group", "--groups", "--keep", "--by", "--format", "--date-tag", "--template",
        "--lat", "--lon", "--alt", "--track", "--sync", "--from", "--output", "--exiftool", "--ext",
    };

    private static readonly HashSet<string> Flags = new(StringComparer.Ordinal)
    {
        "--recursive", "--json", "--csv", "--quiet", "--help", "--numeric", "--preserve-dates", "--keep-backup",
        "--all", "--dry-run", "--xmp", "--duplicates", "--check",
    };

    private readonly Dictionary<string, List<string>> _values = new(StringComparer.Ordinal);
    private readonly HashSet<string> _flags = new(StringComparer.Ordinal);

    public string Command { get; private set; } = "";
    public List<string> Positional { get; } = [];

    /// <summary>For "run": the raw exiftool arguments (everything before "--").</summary>
    public List<string> Raw { get; } = [];

    public static CliArgs Parse(IReadOnlyList<string> args)
    {
        var a = new CliArgs();
        int i = 0;
        // leading global options before the command, e.g. "taoexif-cli --json info x.jpg"
        while (i < args.Count && args[i].StartsWith('-') && args[i] != "--")
            i = a.TakeOption(args, i);
        if (i < args.Count)
            a.Command = args[i++].ToLowerInvariant();

        if (a.Command == "run")
        {
            // run ARGS... [-- FILES...]: exiftool arguments are passed through untouched
            int sep = -1;
            for (int j = i; j < args.Count; j++)
            {
                if (args[j] == "--")
                {
                    sep = j;
                    break;
                }
            }
            int end = sep < 0 ? args.Count : sep;
            for (; i < end; i++)
                a.Raw.Add(args[i]);
            if (sep >= 0)
                a.Positional.AddRange(args.Skip(sep + 1));
            return a;
        }

        bool onlyPositional = false;
        while (i < args.Count)
        {
            string t = args[i];
            if (!onlyPositional && t == "--")
            {
                onlyPositional = true;
                i++;
            }
            else if (!onlyPositional && t.Length > 1 && t.StartsWith('-') && !LooksLikeNumber(t))
            {
                i = a.TakeOption(args, i);
            }
            else
            {
                a.Positional.Add(t);
                i++;
            }
        }
        return a;
    }

    private static bool LooksLikeNumber(string t) => double.TryParse(t, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);

    private int TakeOption(IReadOnlyList<string> args, int i)
    {
        string t = args[i];
        string? inline = null;
        int eq = t.IndexOf('=');
        if (t.StartsWith("--", StringComparison.Ordinal) && eq > 0)
        {
            inline = t[(eq + 1)..];
            t = t[..eq];
        }
        if (Aliases.TryGetValue(t, out var full))
            t = full;

        if (Flags.Contains(t))
        {
            if (inline != null)
                throw new UsageException($"{t} does not take a value");
            _flags.Add(t);
            return i + 1;
        }
        if (!ValueOptions.Contains(t))
            throw new UsageException("unknown option " + args[i]);

        string value;
        if (inline != null)
        {
            value = inline;
        }
        else
        {
            if (i + 1 >= args.Count)
                throw new UsageException(t + " needs a value");
            value = args[++i];
        }
        if (!_values.TryGetValue(t, out var list))
            _values[t] = list = [];
        list.Add(value);
        return i + 1;
    }

    public bool Has(string flag) => _flags.Contains(flag);

    public string? Value(string option) => _values.TryGetValue(option, out var v) ? v[^1] : null;

    public List<string> Values(string option) => _values.TryGetValue(option, out var v) ? v : [];

    /// <summary>Comma-separated option values, from repeated or combined use ("--groups GPS,XMP --groups IPTC").</summary>
    public List<string> List(string option) =>
        Values(option).SelectMany(v => v.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)).ToList();

    public string Required(string option) => Value(option) ?? throw new UsageException(option + " is required");
}
