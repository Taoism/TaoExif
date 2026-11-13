using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;
using TaoExif.Core;

namespace TaoExif.Dialogs;

/// <summary>
/// Remove metadata, with the same choices as ExifToolGui: a "remove ALL" switch plus EXIF / XMP sub-groups,
/// IPTC, Photoshop, JFIF and the ICC profile. With "remove ALL" checked, unchecked groups are kept;
/// otherwise only the checked groups are removed.
/// </summary>
public sealed class RemoveMetadataDialog : DialogWindow
{
    /// <summary>A checkbox for one exiftool group, optionally with sub-groups.</summary>
    private sealed class GroupNode(string label, string group, params GroupNode[] children)
    {
        public string Group { get; } = group;
        public GroupNode[] Children { get; } = children;
        public CheckBox Box { get; } = new() { Content = label };
    }

    private readonly CheckBox _all = new() { Content = "Remove ALL metadata", FontWeight = FontWeight.SemiBold };
    private readonly TextBlock _mode = Label("", true);
    private readonly SelectableTextBlock _args = new() { TextWrapping = TextWrapping.Wrap, FontSize = 12 };
    private readonly List<GroupNode> _groups;
    private bool _syncing;

    public RemoveMetadataDialog(int fileCount, bool removeAll, IReadOnlyCollection<string> uncheckedGroups) : base("Remove metadata", 600)
    {
        _groups =
        [
            new("EXIF — all", "EXIF",
                new("Maker notes", "MakerNotes"),
                new("GPS", "GPS"),
                new("IFD1 (thumbnail)", "IFD1")),
            new("XMP — all", "XMP",
                new("XMP-exif", "XMP-exif"),
                new("XMP-acdsee", "XMP-acdsee"),
                new("XMP-mediapro", "XMP-mediapro"),
                new("XMP-photoshop", "XMP-photoshop"),
                new("XMP-crs (Camera Raw)", "XMP-crs"),
                new("XMP-microsoft", "XMP-microsoft"),
                new("XMP-tiff", "XMP-tiff"),
                new("XMP-pdf", "XMP-pdf")),
            new("IPTC — all", "IPTC"),
            new("Photoshop — all", "Photoshop"),
            new("JFIF — all", "JFIF"),
            new("ICC colour profile", "ICC_Profile"),
        ];

        var unset = new HashSet<string>(uncheckedGroups, StringComparer.OrdinalIgnoreCase);
        foreach (var g in _groups)
        {
            if (g.Children.Length == 0)
            {
                g.Box.IsChecked = !unset.Contains(g.Group);
            }
            else
            {
                foreach (var c in g.Children)
                    c.Box.IsChecked = !unset.Contains(c.Group) && !unset.Contains(g.Group);
                g.Box.IsChecked = unset.Contains(g.Group) ? false : ParentState(g);
            }
        }
        _all.IsChecked = removeAll;

        var list = new StackPanel { Spacing = 6 };
        foreach (var g in _groups)
        {
            list.Children.Add(g.Box);
            g.Box.IsCheckedChanged += (_, _) => OnParentChanged(g);
            if (g.Children.Length == 0)
                continue;
            var sub = new WrapPanel { Margin = new Thickness(26, 0, 0, 4) };
            foreach (var c in g.Children)
            {
                c.Box.MinWidth = 170;
                c.Box.Margin = new Thickness(0, 0, 8, 0);
                c.Box.IsCheckedChanged += (_, _) => OnChildChanged(g);
                sub.Children.Add(c.Box);
            }
            list.Children.Add(sub);
        }
        _all.IsCheckedChanged += (_, _) => OnAllChanged();

        var box = new Border
        {
            Classes = { "panel" },
            Padding = new Thickness(14, 10),
            Margin = new Thickness(24, 0, 0, 0),
            Child = list,
        };
        _args.Classes.Add("muted");
        _args.FontFamily = new FontFamily("Cascadia Mono,Consolas,Menlo,DejaVu Sans Mono,monospace");

        SetBody(Column(10,
            Label($"Remove metadata from {fileCount} selected file(s). This cannot be undone unless backups are kept (Edit → Settings)."),
            _all,
            _mode,
            box,
            _args), "Remove");
        OkButton.Classes.Remove("accent");
        UpdateSummary();
    }

    /// <summary>True: "-all=" minus <see cref="Groups"/>. False: remove only <see cref="Groups"/>.</summary>
    public bool RemoveAll => _all.IsChecked == true;

    /// <summary>The groups to keep (RemoveAll) or to remove (not RemoveAll).</summary>
    public List<string> Groups => RemoveAll ? KeptGroups() : RemovedGroups();

    /// <summary>Unchecked groups, saved so the dialog opens the same way next time.</summary>
    public List<string> UncheckedGroups =>
        _groups.SelectMany(g => g.Box.IsChecked == false ? [g.Group] : g.Children.Where(c => c.Box.IsChecked == false).Select(c => c.Group)).ToList();

    private List<string> KeptGroups()
    {
        var keep = new List<string>();
        foreach (var g in _groups)
        {
            if (g.Box.IsChecked == false)
                keep.Add(g.Group);
            else if (g.Box.IsChecked == null)
                keep.AddRange(g.Children.Where(c => c.Box.IsChecked != true).Select(c => c.Group));
        }
        return keep;
    }

    private List<string> RemovedGroups()
    {
        var remove = new List<string>();
        foreach (var g in _groups)
        {
            if (g.Box.IsChecked == true)
                remove.Add(g.Group);
            else if (g.Box.IsChecked == null)
                remove.AddRange(g.Children.Where(c => c.Box.IsChecked == true).Select(c => c.Group));
        }
        return remove;
    }

    private static bool? ParentState(GroupNode g)
    {
        int on = g.Children.Count(c => c.Box.IsChecked == true);
        if (on == g.Children.Length)
            return true;
        return on == 0 ? false : null;
    }

    private void OnAllChanged()
    {
        if (_syncing)
            return;
        _syncing = true;
        bool value = _all.IsChecked == true;
        foreach (var g in _groups)
        {
            g.Box.IsChecked = value;
            foreach (var c in g.Children)
                c.Box.IsChecked = value;
        }
        _syncing = false;
        UpdateSummary();
    }

    private void OnParentChanged(GroupNode g)
    {
        if (_syncing)
            return;
        _syncing = true;
        // a partial parent toggles to unchecked: follow the parent's new state
        bool value = g.Box.IsChecked != false;
        g.Box.IsChecked = value;
        foreach (var c in g.Children)
            c.Box.IsChecked = value;
        _syncing = false;
        UpdateSummary();
    }

    private void OnChildChanged(GroupNode g)
    {
        if (_syncing)
            return;
        _syncing = true;
        g.Box.IsChecked = ParentState(g);
        _syncing = false;
        UpdateSummary();
    }

    private void UpdateSummary()
    {
        _mode.Text = RemoveAll
            ? "Everything is removed (also comments and other groups not listed); unchecked groups are kept."
            : "Only the checked groups are removed.";
        var args = ExifToolService.RemoveArgs(RemoveAll, Groups);
        _args.Text = args.Count == 0 ? "Nothing selected." : "exiftool " + string.Join(" ", args);
        OkButton.IsEnabled = args.Count > 0;
    }

    protected override async Task<bool> ValidateAsync()
    {
        if (!RemoveAll && Groups.Count == 0)
        {
            await MessageBox.ShowAsync(this, "Choose what to remove.", MessageKind.Warning);
            return false;
        }
        return true;
    }
}
