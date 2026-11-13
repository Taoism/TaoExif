using Avalonia.Controls;
using Avalonia.Platform.Storage;
using TaoExif.Core;

namespace TaoExif.Dialogs;

public sealed class SettingsDialog : DialogWindow
{
    private readonly TextBox _exe = new() { PlaceholderText = "Detected automatically when empty" };
    private readonly CheckBox _overwrite = new() { Content = "Overwrite originals (no *_original backup files)" };
    private readonly CheckBox _preserve = new() { Content = "Keep the file modified date when writing (-P)" };
    private readonly CheckBox _dups = new() { Content = "Show duplicate tags (-a)" };
    private readonly CheckBox _unknown = new() { Content = "Show unknown tags (-u)" };
    private readonly CheckBox _numeric = new() { Content = "Show raw numeric values (-n)" };
    private readonly ComboBox _theme = new() { Width = 200 };
    private readonly ComboBox _updates = new() { Width = 360 };
    private readonly string _originalTheme;

    private static readonly (string Key, string Text)[] Themes =
        [("System", "Follow system"), ("Light", "Light"), ("Dark", "Dark")];

    private static readonly (string Key, string Text)[] UpdateModes =
        [("Off", "Off"), ("Notify", "Tell me when an update is available"), ("Install", "Install updates automatically")];

    public SettingsDialog(AppSettings s) : base("Settings", 560)
    {
        _exe.Text = s.ExifToolPath;
        _overwrite.IsChecked = s.OverwriteOriginal;
        _preserve.IsChecked = s.PreserveFileDates;
        _dups.IsChecked = s.ShowDuplicates;
        _unknown.IsChecked = s.ShowUnknown;
        _numeric.IsChecked = s.NumericValues;
        _originalTheme = s.Theme;
        _theme.ItemsSource = Themes.Select(t => t.Text).ToList();
        _theme.SelectedIndex = Math.Max(0, Array.FindIndex(Themes, t => t.Key == s.Theme));
        // preview the theme right away; Cancel puts the old one back
        _theme.SelectionChanged += (_, _) => App.ApplyTheme(SelectedTheme);
        _updates.ItemsSource = UpdateModes.Select(t => t.Text).ToList();
        _updates.SelectedIndex = Math.Max(0, Array.FindIndex(UpdateModes, t => t.Key == s.UpdateMode));
        Closed += (_, _) =>
        {
            if (!_saved)
                App.ApplyTheme(_originalTheme);
        };

        var detect = new Button { Content = "Detect" };
        detect.Click += (_, _) => _exe.Text = ExifToolLocator.FindInstalled() ?? "";

        SetBody(Column(10,
            Label("exiftool executable"),
            PathRow(_exe, async () =>
            {
                var f = await PickFileAsync("exiftool", FilePickerFileTypes.All);
                if (f != null)
                    _exe.Text = f;
            }),
            Row(8, detect, Label("Leave empty to use an installed exiftool, or the copy bundled with TaoExif.", true)),
            Rule(),
            Label("Writing"),
            Column(4, _overwrite, _preserve),
            Rule(),
            Label("Metadata view"),
            Column(4, _dups, _unknown, _numeric),
            Rule(),
            Row(12, Label("Theme"), _theme),
            Row(12, Label("Updates"), _updates),
            Label(UpdateHint, true)), "Save");
    }

    private bool _saved;

    private string SelectedTheme => Themes[Math.Max(0, _theme.SelectedIndex)].Key;

    private static string UpdateHint => Services.UpdateManager.CanInstallItself
        ? "Updates are checked once a day on GitHub and verified against the release checksums before installing."
        : "Updates are checked once a day on GitHub. This copy can't replace itself (portable or package-managed), so TaoExif offers the verified download instead.";

    public void Apply(AppSettings s)
    {
        _saved = true;
        s.Theme = SelectedTheme;
        s.UpdateMode = UpdateModes[Math.Max(0, _updates.SelectedIndex)].Key;
        s.ExifToolPath = string.IsNullOrWhiteSpace(_exe.Text) ? null : _exe.Text.Trim();
        s.OverwriteOriginal = _overwrite.IsChecked == true;
        s.PreserveFileDates = _preserve.IsChecked == true;
        s.ShowDuplicates = _dups.IsChecked == true;
        s.ShowUnknown = _unknown.IsChecked == true;
        s.NumericValues = _numeric.IsChecked == true;
    }

    protected override async Task<bool> ValidateAsync()
    {
        if (!string.IsNullOrWhiteSpace(_exe.Text) && !File.Exists(_exe.Text.Trim()))
        {
            await MessageBox.ShowAsync(this, "That exiftool path does not exist.", MessageKind.Warning);
            return false;
        }
        return true;
    }
}
