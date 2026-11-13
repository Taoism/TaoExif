using System.Diagnostics;
using System.Text;
using Avalonia;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.Media.Imaging;
using Avalonia.Platform;
using TaoExif.Core;
using TaoExif.Dialogs;

namespace TaoExif;

public sealed partial class MainWindow : Window
{
    private static WindowIcon? _icon;

    public static WindowIcon AppIcon => _icon ??= new WindowIcon(AssetLoader.Open(new Uri("avares://TaoExif/Assets/taoexif.png")));

    private readonly AppSettings _settings;
    private readonly string? _startPath;
    private ExifToolService? _exif;
    private int _busyCount;
    private readonly StringBuilder _log = new();

    public MainWindow() : this(null)
    {
    }

    public MainWindow(string? startPath)
    {
        _startPath = startPath;
        _settings = AppSettings.Load();
        InitializeComponent();
        Icon = AppIcon;
        RestoreWindowState();

        Map.MapClicked += OnMapClicked;
        Opened += OnOpened;
        Closing += OnClosing;
        // SIGTERM / logoff: stop the exiftool child processes even when the window never closes
        AppDomain.CurrentDomain.ProcessExit += (_, _) => _exif?.Dispose();
    }

    private async void OnOpened(object? sender, EventArgs e)
    {
        BuildFolderTree();
        BuildColumns();
        BuildWorkspace();
        BuildPresets();
        BuildRecentMenu();
        BuildFileContextMenus();
        SetViewMode(_settings.ThumbnailView);
        SubfoldersMenu.IsChecked = _settings.IncludeSubfolders;
        Map.CenterOn(_settings.MapLatitude, _settings.MapLongitude, _settings.MapZoom);

        await StartExifToolAsync();

        string? folder = _startPath ?? _settings.LastFolder;
        if (folder != null && File.Exists(folder))
            folder = Path.GetDirectoryName(Path.GetFullPath(folder));
        if (folder != null && Directory.Exists(folder))
            await OpenFolderAsync(folder);
    }

    private async Task StartExifToolAsync()
    {
        _exif?.Dispose();
        _exif = null;
        // the configured/installed exiftool first; the bundled copy if that is missing or will not start
        var candidates = new List<ExifToolCommand>();
        if (ExifToolLocator.Locate(_settings.ExifToolPath) is { } first)
            candidates.Add(first);
        if (ExifToolLocator.FindBundled(AppContext.BaseDirectory) is { } bundled && !candidates.Contains(bundled))
            candidates.Add(bundled);

        string? lastError = null;
        foreach (var cmd in candidates)
        {
            var svc = new ExifToolService(cmd);
            try
            {
                string ver = await svc.GetVersionAsync(CancellationToken.None);
                _exif = svc;
                VersionText.Text = $"exiftool {ver} ({cmd.SourceText})";
                ToolTip.SetTip(VersionText, cmd.ExifToolPath);
                Log($"exiftool {ver} ({cmd.SourceText}): {cmd.ExifToolPath}");
                return;
            }
            catch (Exception ex) when (ex is IOException or InvalidOperationException or System.ComponentModel.Win32Exception)
            {
                svc.Dispose();
                lastError = cmd.ExifToolPath + "\n" + ex.Message;
                Log("exiftool failed to start: " + lastError);
            }
        }

        VersionText.Text = candidates.Count == 0 ? "exiftool not found" : "exiftool failed";
        string text;
        if (lastError != null)
            text = "exiftool could not be started:\n" + lastError;
        else if (ExifToolLocator.BundleNeedsPerl())
            text = "The bundled exiftool needs Perl, which was not found (it comes with macOS and most Linux desktops, but not with minimal installs).\n\n" +
                   "Install Perl, or exiftool itself:\n" +
                   "  Fedora: sudo dnf install perl-Image-ExifTool\n" +
                   "  Debian/Ubuntu: sudo apt install libimage-exiftool-perl\n" +
                   "  Arch: sudo pacman -S perl-image-exiftool";
        else
            text = "exiftool was not found.\n\nInstall it (exiftool.org, or: winget install OliverBetz.ExifTool / brew install exiftool / apt install libimage-exiftool-perl), " +
                   "or set its location in Edit → Settings.";
        await MessageBox.ShowAsync(this, text + "\n\nFolders can be browsed, but no metadata can be read until then.", MessageKind.Warning);
    }

    /// <summary>The service, or a message telling the user it is missing.</summary>
    private async Task<ExifToolService?> RequireExifAsync()
    {
        if (_exif == null)
            await MessageBox.ShowAsync(this, "exiftool is not available. Set its location in Edit → Settings.", MessageKind.Warning);
        return _exif;
    }

    private WriteOptions CurrentWriteOptions => new(_settings.OverwriteOriginal, _settings.PreserveFileDates);

    // ---------- window state ----------

    private void RestoreWindowState()
    {
        Width = Math.Max(900, _settings.WindowWidth);
        Height = Math.Max(560, _settings.WindowHeight);
        if (!double.IsNaN(_settings.WindowX) && !double.IsNaN(_settings.WindowY))
        {
            var pos = new PixelPoint((int)_settings.WindowX, (int)_settings.WindowY);
            if (Screens.All.Any(s => s.WorkingArea.Contains(pos + new PixelPoint(40, 40))))
            {
                WindowStartupLocation = WindowStartupLocation.Manual;
                Position = pos;
            }
        }
        if (_settings.WindowMaximized)
            WindowState = WindowState.Maximized;
        MainGrid.ColumnDefinitions[0].Width = new GridLength(Math.Clamp(_settings.TreeWidth, 140, 600));
        MainGrid.ColumnDefinitions[4].Width = new GridLength(Math.Clamp(_settings.RightWidth, 320, 1400));
        RightGrid.RowDefinitions[0].Height = new GridLength(Math.Clamp(_settings.PreviewHeight, 80, 1200));
    }

    private void OnClosing(object? sender, WindowClosingEventArgs e)
    {
        _settings.WindowMaximized = WindowState == WindowState.Maximized;
        if (WindowState == WindowState.Normal)
        {
            _settings.WindowX = Position.X;
            _settings.WindowY = Position.Y;
            _settings.WindowWidth = Width;
            _settings.WindowHeight = Height;
        }
        _settings.TreeWidth = MainGrid.ColumnDefinitions[0].ActualWidth;
        _settings.RightWidth = MainGrid.ColumnDefinitions[4].ActualWidth;
        _settings.PreviewHeight = RightGrid.RowDefinitions[0].ActualHeight;
        _settings.MapLatitude = Map.CenterLatitude;
        _settings.MapLongitude = Map.CenterLongitude;
        _settings.MapZoom = Map.Zoom;
        SaveColumnWidths();
        SaveSettings();
        _folderCts?.Cancel();
        _exif?.Dispose();
    }

    private void SaveSettings()
    {
        try
        {
            _settings.Save();
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            Log("Could not save settings: " + ex.Message);
        }
    }

    // ---------- status / log ----------

    private void SetStatus(string text) => StatusText.Text = text;

    private IDisposable BeginBusy(string text)
    {
        _busyCount++;
        Busy.IsVisible = true;
        SetStatus(text);
        return new BusyScope(this);
    }

    private sealed class BusyScope(MainWindow owner) : IDisposable
    {
        private bool _done;

        public void Dispose()
        {
            if (_done)
                return;
            _done = true;
            owner._busyCount--;
            if (owner._busyCount <= 0)
            {
                owner._busyCount = 0;
                owner.Busy.IsVisible = false;
            }
        }
    }

    private void Log(string text)
    {
        _log.Append(DateTime.Now.ToString("HH:mm:ss")).Append("  ").AppendLine(text.TrimEnd());
        if (_log.Length > 200_000)
            _log.Remove(0, _log.Length - 150_000);
        LogBox.Text = _log.ToString();
        LogBox.CaretIndex = LogBox.Text.Length;
    }

    /// <summary>Logs an exiftool write result and puts a one-line summary in the status bar.</summary>
    private void Report(string action, ExifToolResult r)
    {
        string text = r.Text.Trim();
        var summary = text.Split('\n').Select(l => l.Trim()).LastOrDefault(l => l.Length > 0) ?? "done";
        Log(action + ": " + text.Replace("\n", " | ") + (r.Errors.Length > 0 ? "\n    " + r.Errors.Replace("\n", "\n    ") : ""));
        if (r.Errors.Length > 0)
        {
            SetStatus(action + ": " + summary + "  (warnings, see Log)");
            if (r.Errors.Contains("Error", StringComparison.Ordinal))
                LogExpander.IsExpanded = true;
        }
        else
        {
            SetStatus(action + ": " + summary);
        }
    }

    // ---------- keyboard ----------

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Handled)
            return;
        bool ctrl = e.KeyModifiers.HasFlag(KeyModifiers.Control) || e.KeyModifiers.HasFlag(KeyModifiers.Meta);
        switch (e.Key)
        {
            case Key.O when ctrl:
                OnOpenFolder(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.S when ctrl:
                OnWorkspaceSave(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.F5:
                OnRefresh(this, new RoutedEventArgs());
                e.Handled = true;
                break;
            case Key.Up when e.KeyModifiers.HasFlag(KeyModifiers.Alt):
                OnFolderUp(this, new RoutedEventArgs());
                e.Handled = true;
                break;
        }
    }

    // ---------- misc menu ----------

    private void OnExit(object? sender, RoutedEventArgs e) => Close();

    private async void OnSettings(object? sender, RoutedEventArgs e)
    {
        var dlg = new SettingsDialog(_settings);
        if (!await dlg.ShowDialog<bool>(this))
            return;
        string? oldExe = _settings.ExifToolPath;
        dlg.Apply(_settings);
        SaveSettings();
        if (_exif == null || oldExe != _settings.ExifToolPath)
            await StartExifToolAsync();
        await ReloadSelectionAsync();
    }

    private void OnTagNamesHelp(object? sender, RoutedEventArgs e) => OpenUrl("https://exiftool.org/TagNames/");

    private static void OpenUrl(string url)
    {
        try
        {
            Process.Start(new ProcessStartInfo(url) { UseShellExecute = true });
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    private async void OnAbout(object? sender, RoutedEventArgs e)
    {
        var v = typeof(MainWindow).Assembly.GetName().Version?.ToString(3) ?? "";
        await MessageBox.ShowAsync(this,
            $"TaoExif {v}\n\nA cross-platform dark GUI for Phil Harvey's ExifTool, inspired by ExifToolGui (Bogdan Hrastnik, Frank Bijnen).\n\n" +
            $"exiftool: {(_exif == null ? "not found" : _exif.ExePath + " (" + _exif.Command.SourceText + ")")}\nSettings: {AppSettings.DefaultPath}\n\nMap data © OpenStreetMap contributors.",
            MessageKind.Info, "About TaoExif");
    }

    internal static Bitmap? LoadAssetBitmap(string name)
    {
        try
        {
            return new Bitmap(AssetLoader.Open(new Uri("avares://TaoExif/Assets/" + name)));
        }
        catch (Exception ex) when (ex is IOException or ArgumentException)
        {
            return null;
        }
    }
}
