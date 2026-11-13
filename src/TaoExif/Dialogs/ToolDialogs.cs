using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Platform.Storage;
using TaoExif.Core;

namespace TaoExif.Dialogs;

/// <summary>Shift date/time tags by a fixed amount.</summary>
public sealed class ShiftDatesDialog : DialogWindow
{
    private readonly NumericUpDown _y = Num(), _mo = Num(), _d = Num(), _h = Num(), _mi = Num(), _s = Num(59);
    private readonly RadioButton _forward = new() { Content = "Later (+)", IsChecked = true, GroupName = "dir" };
    private readonly RadioButton _back = new() { Content = "Earlier (−)", GroupName = "dir" };
    private readonly CheckBox _exif = new() { Content = "EXIF dates (DateTimeOriginal, CreateDate, ModifyDate)", IsChecked = true };
    private readonly CheckBox _xmp = new() { Content = "XMP dates (XMP-xmp:CreateDate, ModifyDate, XMP-exif:DateTimeOriginal)" };
    private readonly CheckBox _qt = new() { Content = "QuickTime dates (videos)" };
    private readonly CheckBox _file = new() { Content = "File modified date" };

    public ShiftDatesDialog(int fileCount) : base("Shift date/time", 520)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,*,*,*,*,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 6, RowSpacing = 4 };
        string[] heads = ["Years", "Months", "Days", "Hours", "Minutes", "Seconds"];
        NumericUpDown[] boxes = [_y, _mo, _d, _h, _mi, _s];
        for (int i = 0; i < 6; i++)
        {
            var l = Label(heads[i], true);
            Grid.SetColumn(l, i);
            grid.Children.Add(l);
            Grid.SetColumn(boxes[i], i);
            Grid.SetRow(boxes[i], 1);
            grid.Children.Add(boxes[i]);
        }
        _h.Maximum = 23;
        _mi.Maximum = 59;

        SetBody(Column(10,
            Label($"Shift the dates of {fileCount} selected file(s)."),
            grid,
            Row(16, _forward, _back),
            Rule(),
            Column(4, _exif, _xmp, _qt, _file)), "Shift");
    }

    private static NumericUpDown Num(int max = 9999) => new() { Minimum = 0, Maximum = max, Value = 0, Increment = 1, FormatString = "0", HorizontalAlignment = HorizontalAlignment.Stretch, ShowButtonSpinner = false };

    private static int V(NumericUpDown n) => (int)(n.Value ?? 0);

    public string Shift => ExifToolService.FormatShift(V(_y), V(_mo), V(_d), V(_h), V(_mi), V(_s));
    public bool Forward => _forward.IsChecked == true;

    public List<string> Tags
    {
        get
        {
            var tags = new List<string>();
            if (_exif.IsChecked == true)
                tags.Add("AllDates");
            if (_xmp.IsChecked == true)
                tags.AddRange(["XMP-xmp:CreateDate", "XMP-xmp:ModifyDate", "XMP-exif:DateTimeOriginal"]);
            if (_qt.IsChecked == true)
                tags.AddRange(["QuickTime:CreateDate", "QuickTime:ModifyDate", "QuickTime:TrackCreateDate", "QuickTime:MediaCreateDate"]);
            if (_file.IsChecked == true)
                tags.Add("FileModifyDate");
            return tags;
        }
    }

    protected override async Task<bool> ValidateAsync()
    {
        if (Tags.Count == 0)
        {
            await MessageBox.ShowAsync(this, "Choose at least one kind of date.", MessageKind.Warning);
            return false;
        }
        if (V(_y) + V(_mo) + V(_d) + V(_h) + V(_mi) + V(_s) == 0)
        {
            await MessageBox.ShowAsync(this, "The shift is zero.", MessageKind.Warning);
            return false;
        }
        return true;
    }
}

/// <summary>Rename files from a date tag or an exiftool name template, with a dry run.</summary>
public sealed class RenameDialog : DialogWindow
{
    private readonly RadioButton _byDate = new() { Content = "From a date tag", IsChecked = true, GroupName = "mode" };
    private readonly RadioButton _byTemplate = new() { Content = "From a template", GroupName = "mode" };
    private readonly ComboBox _tag = new() { ItemsSource = new[] { "DateTimeOriginal", "CreateDate", "FileModifyDate", "MediaCreateDate" }, MinWidth = 200 };
    private readonly TextBox _format = new();
    private readonly TextBox _template = new() { Text = "${Model;tr/ /_/}_%f.%e" };
    private readonly TextBox _preview = new() { IsReadOnly = true, AcceptsReturn = true, Height = 170, TextWrapping = Avalonia.Media.TextWrapping.NoWrap };
    private readonly Func<RenameDialog, Task<string>> _dryRun;

    public RenameDialog(int fileCount, string tag, string format, Func<RenameDialog, Task<string>> dryRun) : base("Rename files", 620)
    {
        _dryRun = dryRun;
        _tag.SelectedItem = tag;
        if (_tag.SelectedItem == null)
            _tag.SelectedIndex = 0;
        _format.Text = format;
        _preview.Classes.Add("mono");

        var previewBtn = new Button { Content = "Preview names" };
        previewBtn.Click += async (_, _) =>
        {
            _preview.Text = "…";
            _preview.Text = await _dryRun(this);
        };

        var dateGrid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*"), RowDefinitions = new RowDefinitions("Auto,Auto"), ColumnSpacing = 8, RowSpacing = 6, Margin = new Avalonia.Thickness(24, 0, 0, 0) };
        AddCell(dateGrid, Label("Tag"), 0, 0);
        AddCell(dateGrid, _tag, 0, 1);
        AddCell(dateGrid, Label("Format"), 1, 0);
        AddCell(dateGrid, _format, 1, 1);

        SetBody(Column(8,
            Label($"Rename {fileCount} selected file(s). Existing names are never overwritten (%-c adds a counter)."),
            _byDate,
            dateGrid,
            Label("strftime codes: %Y year, %m month, %d day, %H hour, %M minute, %S second; %%e = extension, %%-c = counter on clashes.", true),
            _byTemplate,
            new Border { Margin = new Avalonia.Thickness(24, 0, 0, 0), Child = _template },
            Label("exiftool tags in ${…}, %f = old name, %e = extension, %c = counter.", true),
            Row(8, previewBtn),
            _preview), "Rename");
    }

    private static void AddCell(Grid g, Control c, int row, int col)
    {
        Grid.SetRow(c, row);
        Grid.SetColumn(c, col);
        g.Children.Add(c);
    }

    public bool UseTemplate => _byTemplate.IsChecked == true;
    public string SourceTag => _tag.SelectedItem as string ?? "DateTimeOriginal";
    public string DateFormat => _format.Text ?? "";
    public string NameTemplate => _template.Text ?? "";
}

/// <summary>Copy metadata from one file into the selected files.</summary>
public sealed class CopyMetadataDialog : DialogWindow
{
    private readonly TextBox _source = new() { PlaceholderText = "Source file" };
    private readonly CheckBox _all = new() { Content = "Everything writable", IsChecked = true };
    private readonly CheckBox _exif = new() { Content = "EXIF" }, _xmp = new() { Content = "XMP" }, _iptc = new() { Content = "IPTC" };
    private readonly CheckBox _gps = new() { Content = "GPS" }, _maker = new() { Content = "Maker notes" }, _icc = new() { Content = "ICC profile" };

    public CopyMetadataDialog(int fileCount, string? suggestedSource) : base("Copy metadata", 520)
    {
        _source.Text = suggestedSource;
        var groups = Row(14, _exif, _xmp, _iptc, _gps, _maker, _icc);
        _all.IsCheckedChanged += (_, _) => groups.IsEnabled = _all.IsChecked != true;
        groups.IsEnabled = false;
        SetBody(Column(10,
            Label($"Copy tags from the source into {fileCount} selected file(s)."),
            PathRow(_source, async () =>
            {
                var f = await PickFileAsync("Source file");
                if (f != null)
                    _source.Text = f;
            }),
            _all,
            groups), "Copy");
    }

    public string Source => _source.Text?.Trim() ?? "";

    public MetaGroups Groups => _all.IsChecked == true ? MetaGroups.All : CollectGroups(_exif, _xmp, _iptc, _gps, _maker, _icc);

    internal static MetaGroups CollectGroups(CheckBox exif, CheckBox xmp, CheckBox iptc, CheckBox gps, CheckBox maker, CheckBox icc)
    {
        var g = MetaGroups.None;
        if (exif.IsChecked == true) g |= MetaGroups.Exif;
        if (xmp.IsChecked == true) g |= MetaGroups.Xmp;
        if (iptc.IsChecked == true) g |= MetaGroups.Iptc;
        if (gps.IsChecked == true) g |= MetaGroups.Gps;
        if (maker.IsChecked == true) g |= MetaGroups.MakerNotes;
        if (icc.IsChecked == true) g |= MetaGroups.Icc;
        return g;
    }

    protected override async Task<bool> ValidateAsync()
    {
        if (!File.Exists(Source))
        {
            await MessageBox.ShowAsync(this, "The source file does not exist.", MessageKind.Warning);
            return false;
        }
        if (Groups == MetaGroups.None)
        {
            await MessageBox.ShowAsync(this, "Choose what to copy.", MessageKind.Warning);
            return false;
        }
        return true;
    }
}

/// <summary>Geotag from a GPS track log.</summary>
public sealed class GeotagDialog : DialogWindow
{
    private readonly TextBox _log = new() { PlaceholderText = "GPX, KML, NMEA, IGC … track log" };
    private readonly TextBox _sync = new() { PlaceholderText = "e.g. +1:00:00 (camera clock − GPS time)", Width = 260, HorizontalAlignment = HorizontalAlignment.Left };

    public GeotagDialog(int fileCount, string? lastLog) : base("Geotag from track log", 560)
    {
        _log.Text = lastLog;
        SetBody(Column(10,
            Label($"Write GPS positions into {fileCount} selected file(s) by matching their DateTimeOriginal against the track log."),
            PathRow(_log, async () =>
            {
                var f = await PickFileAsync("Track log", new FilePickerFileType("Track logs") { Patterns = ["*.gpx", "*.kml", "*.nmea", "*.log", "*.igc", "*.txt", "*.csv"] }, FilePickerFileTypes.All);
                if (f != null)
                    _log.Text = f;
            }),
            Label("Time correction (optional)"),
            _sync,
            Label("Use the correction when the camera clock was off or set to a different time zone. Leave empty when DateTimeOriginal has an offset or the clock was right.", true)), "Geotag");
    }

    public string TrackLog => _log.Text?.Trim() ?? "";
    public string? GeoSync => string.IsNullOrWhiteSpace(_sync.Text) ? null : _sync.Text.Trim();

    protected override async Task<bool> ValidateAsync()
    {
        if (!File.Exists(TrackLog))
        {
            await MessageBox.ShowAsync(this, "Choose an existing track log.", MessageKind.Warning);
            return false;
        }
        return true;
    }
}
