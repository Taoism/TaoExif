using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Data;
using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using Avalonia.Threading;
using TaoExif.Core;
using TaoExif.Services;
using TaoExif.ViewModels;

namespace TaoExif;

public sealed partial class MainWindow
{
    private const int FixedFileColumns = 3;

    private readonly ObservableCollection<FolderNode> _roots = [];
    private readonly List<FileItem> _allFiles = [];
    private readonly ObservableCollection<FileItem> _files = [];
    private List<FileItem> _selected = [];
    private string? _currentFolder;
    private CancellationTokenSource? _folderCts;
    private CancellationTokenSource? _thumbCts;
    private bool _showAllFiles;
    private bool _syncingSelection;
    private bool _revealing;
    private DispatcherTimer? _filterTimer;

    // ---------- folder tree ----------

    private void BuildFolderTree()
    {
        _roots.Clear();
        string home = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile);
        string pictures = Environment.GetFolderPath(Environment.SpecialFolder.MyPictures);
        if (Directory.Exists(pictures))
            _roots.Add(new FolderNode("Pictures", pictures));
        if (Directory.Exists(home))
            _roots.Add(new FolderNode("Home", home));

        if (OperatingSystem.IsWindows())
        {
            foreach (var d in DriveInfo.GetDrives())
            {
                try
                {
                    if (!d.IsReady)
                        continue;
                    string label = string.IsNullOrEmpty(d.VolumeLabel) ? d.Name.TrimEnd('\\') : $"{d.VolumeLabel} ({d.Name.TrimEnd('\\')})";
                    _roots.Add(new FolderNode(label, d.RootDirectory.FullName));
                }
                catch (IOException)
                {
                    // drive vanished
                }
            }
        }
        else
        {
            _roots.Add(new FolderNode("/", "/"));
            foreach (var mountRoot in new[] { "/Volumes", "/media/" + Environment.UserName, "/mnt" })
            {
                foreach (var dir in FolderNode.ListSubfolders(mountRoot))
                    _roots.Add(new FolderNode(Path.GetFileName(dir), dir));
            }
        }
        FolderTree.ItemsSource = _roots;
    }

    private async void OnFolderSelected(object? sender, SelectionChangedEventArgs e)
    {
        if (_revealing || FolderTree.SelectedItem is not FolderNode node)
            return;
        if (!string.Equals(node.FullPath, _currentFolder, StringComparison.Ordinal))
            await OpenFolderAsync(node.FullPath, reveal: false);
    }

    /// <summary>Expands the tree down to <paramref name="path"/> and selects it.</summary>
    private void RevealInTree(string path)
    {
        var comparer = OperatingSystem.IsWindows() ? StringComparison.OrdinalIgnoreCase : StringComparison.Ordinal;
        string full = Path.TrimEndingDirectorySeparator(Path.GetFullPath(path));

        // pick the deepest root containing the path (Pictures before C:\)
        var root = _roots
            .Where(r => IsUnder(full, r.FullPath, comparer))
            .OrderByDescending(r => r.FullPath.Length)
            .FirstOrDefault();
        if (root == null)
            return;

        _revealing = true;
        try
        {
            var node = root;
            while (!string.Equals(Path.TrimEndingDirectorySeparator(node.FullPath), full, comparer))
            {
                node.IsExpanded = true;
                node.EnsureLoaded();
                var next = node.Children.FirstOrDefault(c => IsUnder(full, c.FullPath, comparer));
                if (next == null)
                    break;
                node = next;
            }
            FolderTree.SelectedItem = node;
            Dispatcher.UIThread.Post(() =>
            {
                if (FolderTree.TreeContainerFromItem(node) is TreeViewItem tvi)
                    tvi.BringIntoView();
            }, DispatcherPriority.Background);
        }
        finally
        {
            _revealing = false;
        }
    }

    private static bool IsUnder(string path, string folder, StringComparison cmp)
    {
        string f = Path.TrimEndingDirectorySeparator(folder);
        if (string.Equals(path, f, cmp))
            return true;
        string withSep = f.EndsWith(Path.DirectorySeparatorChar) ? f : f + Path.DirectorySeparatorChar;
        return path.StartsWith(withSep, cmp);
    }

    // ---------- opening folders ----------

    private async void OnOpenFolder(object? sender, RoutedEventArgs e)
    {
        IStorageFolder? start = null;
        if (_currentFolder != null)
            start = await StorageProvider.TryGetFolderFromPathAsync(_currentFolder);
        var folders = await StorageProvider.OpenFolderPickerAsync(new FolderPickerOpenOptions { Title = "Open folder", SuggestedStartLocation = start });
        var path = folders.Count > 0 ? folders[0].TryGetLocalPath() : null;
        if (path != null)
            await OpenFolderAsync(path);
    }

    private async void OnFolderUp(object? sender, RoutedEventArgs e)
    {
        if (_currentFolder == null)
            return;
        var parent = Directory.GetParent(Path.TrimEndingDirectorySeparator(_currentFolder));
        if (parent != null)
            await OpenFolderAsync(parent.FullName);
    }

    private async void OnRefresh(object? sender, RoutedEventArgs e)
    {
        if (_currentFolder != null)
            await OpenFolderAsync(_currentFolder, keepSelection: true);
    }

    private async void OnPathKeyDown(object? sender, Avalonia.Input.KeyEventArgs e)
    {
        if (e.Key != Avalonia.Input.Key.Enter)
            return;
        e.Handled = true;
        string text = (PathBox.Text ?? "").Trim().Trim('"');
        if (text.StartsWith('~'))
            text = Environment.GetFolderPath(Environment.SpecialFolder.UserProfile) + text[1..];
        if (File.Exists(text))
            text = Path.GetDirectoryName(Path.GetFullPath(text)) ?? text;
        if (Directory.Exists(text))
            await OpenFolderAsync(text);
        else
            SetStatus("Folder not found: " + text);
    }

    private async void OnToggleSubfolders(object? sender, RoutedEventArgs e)
    {
        _settings.IncludeSubfolders = SubfoldersMenu.IsChecked;
        if (_currentFolder != null)
            await OpenFolderAsync(_currentFolder);
    }

    private async void OnToggleAllFiles(object? sender, RoutedEventArgs e)
    {
        _showAllFiles = AllFilesMenu.IsChecked;
        if (_currentFolder != null)
            await OpenFolderAsync(_currentFolder);
    }

    private void BuildRecentMenu()
    {
        var items = new List<MenuItem>();
        foreach (var f in _settings.RecentFolders)
        {
            var mi = new MenuItem { Header = f.Replace("_", "__") };
            string folder = f;
            mi.Click += async (_, _) =>
            {
                if (Directory.Exists(folder))
                    await OpenFolderAsync(folder);
                else
                    SetStatus("Folder not found: " + folder);
            };
            items.Add(mi);
        }
        RecentMenu.ItemsSource = items;
        RecentMenu.IsEnabled = items.Count > 0;
    }

    private void AddRecent(string folder)
    {
        var cmp = ExifToolService.PathComparer;
        _settings.RecentFolders.RemoveAll(f => cmp.Equals(f, folder));
        _settings.RecentFolders.Insert(0, folder);
        if (_settings.RecentFolders.Count > 12)
            _settings.RecentFolders.RemoveRange(12, _settings.RecentFolders.Count - 12);
        BuildRecentMenu();
    }

    private async Task OpenFolderAsync(string folder, bool reveal = true, bool keepSelection = false)
    {
        folder = Path.GetFullPath(folder);
        var keep = keepSelection ? _selected.Select(f => f.FullPath).ToHashSet(ExifToolService.PathComparer) : null;

        await (_folderCts?.CancelAsync() ?? Task.CompletedTask);
        await (_thumbCts?.CancelAsync() ?? Task.CompletedTask);
        var cts = new CancellationTokenSource();
        _folderCts = cts;
        var ct = cts.Token;

        _currentFolder = folder;
        _settings.LastFolder = folder;
        AddRecent(folder);
        PathBox.Text = folder;
        Title = "TaoExif — " + folder;
        if (reveal)
            RevealInTree(folder);

        List<FileInfo> infos;
        using (BeginBusy("Reading folder…"))
        {
            bool recurse = _settings.IncludeSubfolders, all = _showAllFiles;
            infos = await Task.Run(() => ListFiles(folder, recurse, all), ct);
        }
        if (ct.IsCancellationRequested)
            return;

        int cols = _settings.Columns.Count;
        _allFiles.Clear();
        _allFiles.AddRange(infos.Select(i => new FileItem(i, cols)));
        ApplyFileFilter();
        EmptyText.IsVisible = _allFiles.Count == 0;
        EmptyText.Text = _allFiles.Count == 0 ? "No supported files in this folder." : "";
        SetStatus($"{_allFiles.Count} file(s) in {folder}");

        if (keep is { Count: > 0 })
            SelectFiles(_files.Where(f => keep.Contains(f.FullPath)));
        else if (_files.Count > 0)
            SelectFiles([_files[0]]);
        else
            await OnSelectionChangedAsync();

        await LoadFileDetailsAsync(_allFiles.ToList(), ct);
    }

    private static readonly EnumerationOptions FileEnum = new()
    {
        IgnoreInaccessible = true,
        AttributesToSkip = FileAttributes.Hidden | FileAttributes.System,
    };

    private static List<FileInfo> ListFiles(string folder, bool recurse, bool all)
    {
        var opts = new EnumerationOptions
        {
            IgnoreInaccessible = true,
            AttributesToSkip = FileEnum.AttributesToSkip,
            RecurseSubdirectories = recurse,
            MaxRecursionDepth = 12,
        };
        try
        {
            var list = new DirectoryInfo(folder).EnumerateFiles("*", opts)
                .Where(f => all || ImageLoader.MediaExtensions.Contains(f.Extension))
                .Where(f => !f.Name.EndsWith("_original", StringComparison.OrdinalIgnoreCase))
                .Take(20000)
                .ToList();
            list.Sort((a, b) => string.Compare(a.FullName, b.FullName, StringComparison.CurrentCultureIgnoreCase));
            return list;
        }
        catch (Exception ex) when (ex is IOException or UnauthorizedAccessException)
        {
            return [];
        }
    }

    private void OnFilterChanged(object? sender, TextChangedEventArgs e)
    {
        _filterTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(200), DispatcherPriority.Normal, (_, _) =>
        {
            _filterTimer!.Stop();
            var keep = _selected.ToList();
            ApplyFileFilter();
            SelectFiles(keep.Where(_files.Contains));
        });
        _filterTimer.Stop();
        _filterTimer.Start();
    }

    private void ApplyFileFilter()
    {
        string f = (FilterBox.Text ?? "").Trim();
        _files.Clear();
        foreach (var item in _allFiles)
        {
            if (f.Length == 0 || item.Name.Contains(f, StringComparison.CurrentCultureIgnoreCase))
                _files.Add(item);
        }
        FileGrid.ItemsSource = _files;
        ThumbList.ItemsSource = _files;
        if (ThumbList.IsVisible)
            StartThumbnails();
    }

    // ---------- columns ----------

    private void BuildColumns()
    {
        while (FileGrid.Columns.Count > FixedFileColumns)
            FileGrid.Columns.RemoveAt(FixedFileColumns);
        for (int i = 0; i < _settings.Columns.Count; i++)
        {
            var c = _settings.Columns[i];
            FileGrid.Columns.Add(new DataGridTextColumn
            {
                Header = c.Header,
                Binding = new Binding($"Values[{i}]"),
                Width = new DataGridLength(Math.Clamp(c.Width, 40, 600)),
                SortMemberPath = $"Values[{i}]",
            });
        }
    }

    private void SaveColumnWidths()
    {
        for (int i = 0; i < _settings.Columns.Count && FixedFileColumns + i < FileGrid.Columns.Count; i++)
        {
            double w = FileGrid.Columns[FixedFileColumns + i].ActualWidth;
            if (w > 10)
                _settings.Columns[i].Width = w;
        }
    }

    /// <summary>Reads column values, orientation and GPS for the files in chunks, in the background.</summary>
    private async Task LoadFileDetailsAsync(List<FileItem> files, CancellationToken ct)
    {
        if (_exif == null || files.Count == 0)
            return;
        var exif = _exif;
        var columns = _settings.Columns.Select(c => c.Tag).ToList();
        using var busy = BeginBusy($"Reading metadata of {files.Count} file(s)…");
        try
        {
            for (int start = 0; start < files.Count; start += 200)
            {
                ct.ThrowIfCancellationRequested();
                var chunk = files.Skip(start).Take(200).ToList();
                var paths = chunk.Select(f => f.FullPath).ToList();
                var values = await exif.ReadTagsAsync(paths, columns, ct);
                var numeric = await exif.ReadTagsAsync(paths, ["Orientation", "Composite:GPSLatitude", "Composite:GPSLongitude"], ct, numeric: true);
                foreach (var item in chunk)
                    ApplyDetails(item, columns, values, numeric);
                SetStatus($"Read {Math.Min(start + chunk.Count, files.Count)} of {files.Count}…");
            }
            SetStatus($"{_allFiles.Count} file(s) in {_currentFolder}");
            UpdateMapMarkers();
        }
        catch (OperationCanceledException)
        {
            // folder changed
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException)
        {
            Log("Reading file details failed: " + ex.Message);
        }
    }

    private static void ApplyDetails(FileItem item, List<string> columns,
        Dictionary<string, Dictionary<string, string>> values, Dictionary<string, Dictionary<string, string>> numeric)
    {
        if (values.TryGetValue(item.FullPath, out var row))
            item.Values = columns.Select(c => ExifToolService.Lookup(row, c) ?? "").ToArray();
        if (!numeric.TryGetValue(item.FullPath, out var num))
            return;
        if (int.TryParse(ExifToolService.Lookup(num, "Orientation"), NumberStyles.Integer, CultureInfo.InvariantCulture, out int o))
            item.Rotation = ImageLoader.RotationFor(o);
        if (double.TryParse(ExifToolService.Lookup(num, "GPSLatitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
            && double.TryParse(ExifToolService.Lookup(num, "GPSLongitude"), NumberStyles.Float, CultureInfo.InvariantCulture, out double lon))
            item.Gps = new GeoPoint(lat, lon);
        else
            item.Gps = null;
    }

    /// <summary>Re-reads list details of files after a write.</summary>
    private Task RefreshFilesAsync(IEnumerable<FileItem> files)
    {
        var list = files.ToList();
        return LoadFileDetailsAsync(list, _folderCts?.Token ?? CancellationToken.None);
    }

    // ---------- view mode / thumbnails ----------

    private void OnDetailsView(object? sender, RoutedEventArgs e) => SetViewMode(false);

    private void OnThumbView(object? sender, RoutedEventArgs e) => SetViewMode(true);

    private void SetViewMode(bool thumbs)
    {
        var keep = _selected.ToList();
        _settings.ThumbnailView = thumbs;
        FileGrid.IsVisible = !thumbs;
        ThumbList.IsVisible = thumbs;
        DetailsToggle.IsChecked = !thumbs;
        ThumbsToggle.IsChecked = thumbs;
        DetailsMenu.IsChecked = !thumbs;
        ThumbsMenu.IsChecked = thumbs;
        SelectFiles(keep);
        if (thumbs)
            StartThumbnails();
        else
            _thumbCts?.Cancel();
    }

    private void StartThumbnails()
    {
        _thumbCts?.Cancel();
        var cts = new CancellationTokenSource();
        _thumbCts = cts;
        var todo = _files.Where(f => !f.ThumbnailRequested).ToList();
        if (todo.Count > 0)
            _ = LoadThumbnailsAsync(todo, cts.Token);
    }

    private async Task LoadThumbnailsAsync(List<FileItem> items, CancellationToken ct)
    {
        var exif = _exif;
        using var gate = new SemaphoreSlim(3);
        var tasks = items.Select(async item =>
        {
            await gate.WaitAsync(ct);
            try
            {
                if (ct.IsCancellationRequested || item.ThumbnailRequested)
                    return;
                item.ThumbnailRequested = true;
                var bmp = await ImageLoader.LoadAsync(exif, item.FullPath, 200, true, ct);
                Dispatcher.UIThread.Post(() => item.Thumbnail = bmp);
            }
            catch (OperationCanceledException)
            {
                item.ThumbnailRequested = false;
            }
            finally
            {
                gate.Release();
            }
        });
        try
        {
            await Task.WhenAll(tasks);
        }
        catch (OperationCanceledException)
        {
            // view or folder changed
        }
    }

    // ---------- selection ----------

    private void SelectFiles(IEnumerable<FileItem> items)
    {
        var list = items.ToList();
        _syncingSelection = true;
        try
        {
            FileGrid.SelectedItems.Clear();
            ThumbList.SelectedItems?.Clear();
            foreach (var i in list)
            {
                FileGrid.SelectedItems.Add(i);
                ThumbList.SelectedItems?.Add(i);
            }
            if (list.Count > 0)
            {
                FileGrid.ScrollIntoView(list[0], null);
                ThumbList.ScrollIntoView(list[0]);
            }
        }
        finally
        {
            _syncingSelection = false;
        }
        _selected = list;
        _ = OnSelectionChangedAsync();
    }

    private DispatcherTimer? _selectionTimer;

    private void OnFileSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (_syncingSelection)
            return;
        System.Collections.IList? source = sender == ThumbList ? ThumbList.SelectedItems : FileGrid.SelectedItems;
        _selected = source?.OfType<FileItem>().ToList() ?? [];
        SelectionText.Text = _selected.Count == 0 ? "" : $"{_selected.Count} selected";

        // debounce: arrow-key scrolling through a folder should not start a read per file
        _selectionTimer ??= new DispatcherTimer(TimeSpan.FromMilliseconds(120), DispatcherPriority.Normal, async (_, _) =>
        {
            _selectionTimer!.Stop();
            await OnSelectionChangedAsync();
        });
        _selectionTimer.Stop();
        _selectionTimer.Start();
    }

    private void OnSelectAll(object? sender, RoutedEventArgs e) => SelectFiles(_files);

    private async void OnFileDoubleTapped(object? sender, Avalonia.Input.TappedEventArgs e)
    {
        if (_selected.Count == 1)
        {
            OpenUrl(_selected[0].FullPath);
            await Task.CompletedTask;
        }
    }

    private List<string> SelectedPaths => _selected.Select(f => f.FullPath).ToList();
}
