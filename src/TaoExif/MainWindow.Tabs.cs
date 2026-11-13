using System.Collections.ObjectModel;
using System.Globalization;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using TaoExif.Controls;
using TaoExif.Core;
using TaoExif.Dialogs;
using TaoExif.Services;
using TaoExif.ViewModels;

namespace TaoExif;

public sealed partial class MainWindow
{
    private const string AllGroups = "All groups";

    private List<MetaRow> _metaAll = [];
    private readonly ObservableCollection<MetaRow> _metaRows = [];
    private readonly ObservableCollection<WorkspaceRow> _workspace = [];
    private CancellationTokenSource? _selectionCts;
    private string? _lastTrackLog;

    private async Task ReloadSelectionAsync() => await OnSelectionChangedAsync();

    /// <summary>Loads everything that depends on the selection: preview, metadata, workspace, map markers.</summary>
    private async Task OnSelectionChangedAsync()
    {
        await (_selectionCts?.CancelAsync() ?? Task.CompletedTask);
        var cts = new CancellationTokenSource();
        _selectionCts = cts;
        var ct = cts.Token;

        SelectionText.Text = _selected.Count == 0 ? "" : $"{_selected.Count} selected";
        UpdateMapMarkers();
        var first = _selected.FirstOrDefault();
        try
        {
            var preview = LoadPreviewAsync(first, ct);
            await LoadMetadataAsync(first, ct);
            await LoadWorkspaceAsync(ct);
            await preview;
        }
        catch (OperationCanceledException)
        {
            // selection changed again
        }
        catch (Exception ex) when (ex is IOException or System.Text.Json.JsonException or InvalidOperationException)
        {
            Log("Reading metadata failed: " + ex.Message);
        }
    }

    // ---------- preview ----------

    private async Task LoadPreviewAsync(FileItem? item, CancellationToken ct)
    {
        if (item == null)
        {
            PreviewImage.Source = null;
            PreviewText.Text = "No preview";
            PreviewText.IsVisible = true;
            return;
        }
        PreviewText.Text = "Loading…";
        PreviewText.IsVisible = PreviewImage.Source == null;
        var bmp = await ImageLoader.LoadAsync(_exif, item.FullPath, 1600, false, ct);
        ct.ThrowIfCancellationRequested();
        PreviewTransform.LayoutTransform = new Avalonia.Media.RotateTransform(item.Rotation);
        PreviewImage.Source = bmp;
        PreviewText.Text = bmp == null ? "No preview for " + Path.GetExtension(item.FullPath).TrimStart('.').ToUpperInvariant() : "";
        PreviewText.IsVisible = bmp == null;
        PreviewImage.MaxWidth = double.PositiveInfinity;
    }

    // ---------- metadata tab ----------

    private async Task LoadMetadataAsync(FileItem? item, CancellationToken ct)
    {
        ClearMetaEditor();
        if (item == null || _exif == null)
        {
            _metaAll = [];
            ApplyMetaFilter();
            return;
        }
        var opts = new ReadOptions(_settings.ShowDuplicates, _settings.ShowUnknown, _settings.NumericValues);
        var tags = await _exif.ReadMetadataAsync(item.FullPath, opts, ct);
        ct.ThrowIfCancellationRequested();
        _metaAll = tags.Select(t => new MetaRow(t)).ToList();

        string? group = MetaGroup.SelectedItem as string;
        var groups = new List<string> { AllGroups };
        groups.AddRange(_metaAll.Select(r => r.Entry.Family0).Where(g => g.Length > 0).Distinct());
        _suppressGroupChange = true;
        MetaGroup.ItemsSource = groups;
        MetaGroup.SelectedItem = group != null && groups.Contains(group) ? group : AllGroups;
        _suppressGroupChange = false;
        ApplyMetaFilter();
    }

    private bool _suppressGroupChange;

    private void ApplyMetaFilter()
    {
        string f = (MetaFilter.Text ?? "").Trim();
        string? g = MetaGroup.SelectedItem as string;
        _metaRows.Clear();
        foreach (var r in _metaAll)
        {
            if (g != null && g != AllGroups && r.Entry.Family0 != g)
                continue;
            if (f.Length > 0
                && !r.Name.Contains(f, StringComparison.OrdinalIgnoreCase)
                && !r.Value.Contains(f, StringComparison.OrdinalIgnoreCase)
                && !r.Group.Contains(f, StringComparison.OrdinalIgnoreCase))
                continue;
            _metaRows.Add(r);
        }
        MetaGrid.ItemsSource = _metaRows;
    }

    private void OnMetaFilterChanged(object? sender, TextChangedEventArgs e) => ApplyMetaFilter();

    private void OnMetaGroupChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (!_suppressGroupChange)
            ApplyMetaFilter();
    }

    private void ClearMetaEditor()
    {
        MetaEditName.Text = "Select a tag";
        MetaEditValue.Text = "";
        MetaEditValue.IsEnabled = MetaSaveButton.IsEnabled = MetaDeleteButton.IsEnabled = false;
    }

    private void OnMetaSelectionChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (MetaGrid.SelectedItem is not MetaRow row)
        {
            ClearMetaEditor();
            return;
        }
        MetaEditName.Text = row.Entry.WriteName;
        ToolTip.SetTip(MetaEditName, row.Entry.WriteName);
        MetaEditValue.Text = row.Value;
        bool writable = !row.Entry.IsReadOnly && !row.Value.StartsWith("(Binary data", StringComparison.Ordinal);
        MetaEditValue.IsEnabled = MetaSaveButton.IsEnabled = MetaDeleteButton.IsEnabled = writable && _selected.Count > 0;
    }

    private string MetaWriteTag(MetaRow row) => _settings.NumericValues ? row.Entry.WriteName + "#" : row.Entry.WriteName;

    private async void OnMetaEditKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await WriteMetaAsync(false);
        }
    }

    private async void OnMetaWrite(object? sender, RoutedEventArgs e) => await WriteMetaAsync(false);

    private async void OnMetaDelete(object? sender, RoutedEventArgs e) => await WriteMetaAsync(true);

    private async Task WriteMetaAsync(bool delete)
    {
        if (MetaGrid.SelectedItem is not MetaRow row || await RequireExifAsync() is not { } exif || _selected.Count == 0)
            return;
        var files = _selected.ToList();
        string tag = MetaWriteTag(row);
        if (files.Count > 1 && !await MessageBox.ConfirmAsync(this,
                $"{(delete ? "Delete" : "Write")} {row.Entry.WriteName} in {files.Count} files?", delete ? "Delete" : "Write"))
            return;
        using (BeginBusy("Writing…"))
        {
            var r = await exif.WriteTagsAsync(files.Select(f => f.FullPath).ToList(), [new TagWrite(tag, delete ? null : MetaEditValue.Text)], CurrentWriteOptions, CancellationToken.None);
            Report(delete ? "Delete " + row.Entry.WriteName : "Write " + row.Entry.WriteName, r);
        }
        string keepName = row.Entry.WriteName;
        await RefreshFilesAsync(files);
        await OnSelectionChangedAsync();
        var again = _metaRows.FirstOrDefault(m => m.Entry.WriteName == keepName);
        if (again != null)
        {
            MetaGrid.SelectedItem = again;
            MetaGrid.ScrollIntoView(again, null);
        }
    }

    private async void OnMetaCopyValue(object? sender, RoutedEventArgs e)
    {
        if (MetaGrid.SelectedItem is MetaRow row && Clipboard != null)
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(Clipboard, row.Value);
    }

    private async void OnMetaCopyName(object? sender, RoutedEventArgs e)
    {
        if (MetaGrid.SelectedItem is MetaRow row && Clipboard != null)
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(Clipboard, row.Entry.WriteName);
    }

    private async void OnMetaAddColumn(object? sender, RoutedEventArgs e)
    {
        if (MetaGrid.SelectedItem is not MetaRow row)
            return;
        SaveColumnWidths();
        _settings.Columns.Add(new ColumnDef { Header = row.Name, Tag = row.Entry.WriteName, Width = 120 });
        SaveSettings();
        await RebuildColumnsAsync();
        SetStatus("Added column " + row.Entry.WriteName);
    }

    private async void OnMetaAddWorkspace(object? sender, RoutedEventArgs e)
    {
        if (MetaGrid.SelectedItem is not MetaRow row)
            return;
        _settings.Workspace.Add(new WorkspaceDef { Label = row.Name, Tag = row.Entry.WriteName });
        SaveSettings();
        BuildWorkspace();
        await LoadWorkspaceAsync(CancellationToken.None);
        SetStatus("Added workspace field " + row.Entry.WriteName);
    }

    private async Task RebuildColumnsAsync()
    {
        BuildColumns();
        int n = _settings.Columns.Count;
        foreach (var f in _allFiles)
            f.Values = new string[n];
        await LoadFileDetailsAsync(_allFiles.ToList(), _folderCts?.Token ?? CancellationToken.None);
    }

    // ---------- workspace tab ----------

    private void BuildWorkspace()
    {
        _workspace.Clear();
        foreach (var d in _settings.Workspace)
            _workspace.Add(new WorkspaceRow(d));
        WorkspaceList.ItemsSource = _workspace;
    }

    private async Task LoadWorkspaceAsync(CancellationToken ct)
    {
        var files = _selected.Take(500).Select(f => f.FullPath).ToList();
        if (files.Count == 0 || _exif == null)
        {
            foreach (var w in _workspace)
                w.Load("", "");
            WorkspaceInfo.Text = "No files selected";
            return;
        }
        var data = await _exif.ReadTagsAsync(files, _workspace.Select(w => w.Tag).ToList(), ct);
        ct.ThrowIfCancellationRequested();
        foreach (var w in _workspace)
        {
            var values = files.Select(f => data.TryGetValue(f, out var row) ? ExifToolService.Lookup(row, w.Tag) ?? "" : "").Distinct().ToList();
            if (values.Count == 1)
                w.Load(values[0], "");
            else
                w.Load("", "(different values — typing replaces all)");
        }
        WorkspaceInfo.Text = _selected.Count == 1 ? _selected[0].Name : $"{_selected.Count} files";
    }

    private async void OnWorkspaceRevert(object? sender, RoutedEventArgs e) => await LoadWorkspaceAsync(CancellationToken.None);

    private async void OnWorkspaceSave(object? sender, RoutedEventArgs e)
    {
        var changed = _workspace.Where(w => w.IsModified).ToList();
        if (changed.Count == 0)
        {
            SetStatus("No workspace changes to save");
            return;
        }
        if (await RequireExifAsync() is not { } exif || _selected.Count == 0)
            return;
        var files = _selected.ToList();
        if (files.Count > 1 && !await MessageBox.ConfirmAsync(this,
                $"Write {changed.Count} field(s) into {files.Count} files?\n\n" + string.Join("\n", changed.Select(c => $"{c.Label}: {(c.Value.Length == 0 ? "(delete)" : c.Value)}")), "Write"))
            return;
        using (BeginBusy("Saving workspace…"))
        {
            var r = await exif.WriteTagsAsync(files.Select(f => f.FullPath).ToList(), changed.Select(c => new TagWrite(c.Tag, c.Value)), CurrentWriteOptions, CancellationToken.None);
            Report("Workspace", r);
        }
        await RefreshFilesAsync(files);
        await OnSelectionChangedAsync();
    }

    // ---------- map tab ----------

    private void OnTabChanged(object? sender, SelectionChangedEventArgs e)
    {
        if (e.Source != Tabs)
            return;
        UpdateMapMarkers();
    }

    private bool MapVisible => Tabs.SelectedIndex == 2;

    private void UpdateMapMarkers()
    {
        var selected = _selected.ToHashSet();
        var markers = _allFiles
            .Where(f => f.Gps != null)
            .Select(f => new MapMarker(f.Gps!.Value.Latitude, f.Gps.Value.Longitude, f.Name, selected.Contains(f)))
            .ToList();
        Map.SetMarkers(markers);

        if (!MapVisible)
            return;
        var withGps = _selected.Where(f => f.Gps != null).ToList();
        if (withGps.Count == 1)
        {
            var p = withGps[0].Gps!.Value;
            Map.CenterOn(p.Latitude, p.Longitude, Math.Max(Map.Zoom, 12));
            SetLatLon(p.Latitude, p.Longitude);
        }
        else if (withGps.Count > 1)
        {
            Map.FitTo(withGps.Select(f => (f.Gps!.Value.Latitude, f.Gps!.Value.Longitude)));
        }
    }

    private void SetLatLon(double lat, double lon)
    {
        LatBox.Text = lat.ToString("0.######", CultureInfo.InvariantCulture);
        LonBox.Text = lon.ToString("0.######", CultureInfo.InvariantCulture);
    }

    private void OnMapClicked(object? sender, GeoPoint p)
    {
        Map.Pin = p;
        SetLatLon(p.Latitude, p.Longitude);
    }

    /// <summary>Parses the lat/lon boxes; accepts "lat, lon" pasted into the first box.</summary>
    private GeoPoint? ReadLatLon()
    {
        string a = (LatBox.Text ?? "").Trim(), b = (LonBox.Text ?? "").Trim();
        if (a.Contains(',') && b.Length == 0)
        {
            var parts = a.Split(',', 2);
            a = parts[0].Trim();
            b = parts[1].Trim();
        }
        if (double.TryParse(a, NumberStyles.Float, CultureInfo.InvariantCulture, out double lat)
            && double.TryParse(b, NumberStyles.Float, CultureInfo.InvariantCulture, out double lon)
            && lat is >= -90 and <= 90 && lon is >= -180 and <= 180)
            return new GeoPoint(lat, lon);
        return null;
    }

    private void OnMapGo(object? sender, RoutedEventArgs e)
    {
        if (ReadLatLon() is { } p)
        {
            Map.Pin = p;
            SetLatLon(p.Latitude, p.Longitude);
            Map.CenterOn(p.Latitude, p.Longitude, Math.Max(Map.Zoom, 13));
        }
        else
        {
            SetStatus("Enter latitude and longitude in decimal degrees, e.g. 48.8584, 2.2945");
        }
    }

    private void OnMapZoomIn(object? sender, RoutedEventArgs e) => Map.ZoomBy(1);

    private void OnMapZoomOut(object? sender, RoutedEventArgs e) => Map.ZoomBy(-1);

    private void OnMapShowSelected(object? sender, RoutedEventArgs e)
    {
        var pts = _selected.Where(f => f.Gps != null).Select(f => (f.Gps!.Value.Latitude, f.Gps!.Value.Longitude)).ToList();
        if (pts.Count == 0)
            pts = _allFiles.Where(f => f.Gps != null).Select(f => (f.Gps!.Value.Latitude, f.Gps!.Value.Longitude)).ToList();
        if (pts.Count == 0)
        {
            SetStatus("No files with GPS here");
            return;
        }
        Map.FitTo(pts);
    }

    private async void OnMapApply(object? sender, RoutedEventArgs e)
    {
        var p = ReadLatLon() ?? Map.Pin;
        if (p == null)
        {
            await MessageBox.ShowAsync(this, "Click the map or type coordinates first.", MessageKind.Info);
            return;
        }
        if (await RequireExifAsync() is not { } exif || _selected.Count == 0)
            return;
        var files = _selected.ToList();
        if (files.Count > 1 && !await MessageBox.ConfirmAsync(this,
                $"Set GPS {p.Value.Latitude:0.######}, {p.Value.Longitude:0.######} on {files.Count} files?", "Set"))
            return;
        using (BeginBusy("Writing GPS…"))
        {
            var r = await exif.SetGpsAsync(files.Select(f => f.FullPath).ToList(), p, GpsXmp.IsChecked == true, CurrentWriteOptions, CancellationToken.None);
            Report("Set GPS", r);
        }
        await RefreshFilesAsync(files);
        await OnSelectionChangedAsync();
    }

    private async void OnMapLoadTrack(object? sender, RoutedEventArgs e)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new Avalonia.Platform.Storage.FilePickerOpenOptions
        {
            Title = "GPX track",
            FileTypeFilter = [new Avalonia.Platform.Storage.FilePickerFileType("GPX") { Patterns = ["*.gpx"] }],
        });
        var path = files.Count > 0 ? Avalonia.Platform.Storage.StorageProviderExtensions.TryGetLocalPath(files[0]) : null;
        if (path == null)
            return;
        try
        {
            var track = GpxTrack.Load(path);
            Map.SetTrack(track);
            _lastTrackLog = path;
            if (track.Count > 0)
                Map.FitTo(track.Select(t => (t.Latitude, t.Longitude)));
            SetStatus($"Track: {track.Count} points from {Path.GetFileName(path)}");
        }
        catch (Exception ex) when (ex is IOException or System.Xml.XmlException or UnauthorizedAccessException)
        {
            await MessageBox.ShowAsync(this, "Could not read the track:\n" + ex.Message, MessageKind.Error);
        }
    }

    // ---------- command tab ----------

    private void BuildPresets()
    {
        PresetBox.ItemsSource = _settings.Commands.Select(c => c.Name).ToList();
    }

    private void OnPresetSelected(object? sender, SelectionChangedEventArgs e)
    {
        int i = PresetBox.SelectedIndex;
        if (i >= 0 && i < _settings.Commands.Count)
            CommandBox.Text = _settings.Commands[i].Arguments;
    }

    private async void OnPresetSave(object? sender, RoutedEventArgs e)
    {
        string args = (CommandBox.Text ?? "").Trim();
        if (args.Length == 0)
            return;
        var dlg = new ListEditorDialog("Save command", "Name the command; it is saved with the presets.", "Name", "Arguments",
            [("My command", args)]);
        if (!await dlg.ShowDialog<bool>(this))
            return;
        foreach (var (name, a) in dlg.Result)
            _settings.Commands.Add(new CommandPreset { Name = name, Arguments = a });
        SaveSettings();
        BuildPresets();
    }

    private async void OnCommandKeyDown(object? sender, KeyEventArgs e)
    {
        if (e.Key == Key.Enter)
        {
            e.Handled = true;
            await RunCommandAsync(SelectedPaths, "selected files");
        }
    }

    private async void OnRunSelected(object? sender, RoutedEventArgs e) => await RunCommandAsync(SelectedPaths, "selected files");

    private async void OnRunFolder(object? sender, RoutedEventArgs e)
    {
        if (_currentFolder != null)
            await RunCommandAsync([_currentFolder], _currentFolder);
    }

    private async void OnRunNoFiles(object? sender, RoutedEventArgs e) => await RunCommandAsync([], "no files");

    private void OnCommandClear(object? sender, RoutedEventArgs e) => CommandOutput.Text = "";

    private async Task RunCommandAsync(List<string> targets, string what)
    {
        string cmd = (CommandBox.Text ?? "").Trim();
        if (cmd.Length == 0 || await RequireExifAsync() is not { } exif)
            return;
        if (targets.Count == 0 && what == "selected files")
        {
            SetStatus("No files selected");
            return;
        }
        ExifToolResult r;
        using (BeginBusy("Running exiftool…"))
            r = await exif.RunRawAsync(cmd, targets, CancellationToken.None);
        string header = $"> exiftool {cmd}   [{(targets.Count == 1 ? Path.GetFileName(targets[0]) : what)}]";
        string body = r.Text.TrimEnd();
        if (r.Errors.Length > 0)
            body += (body.Length > 0 ? "\n" : "") + r.Errors;
        CommandOutput.Text = header + "\n" + body + "\n\n" + CommandOutput.Text;
        if (CommandOutput.Text.Length > 400_000)
            CommandOutput.Text = CommandOutput.Text[..300_000];
        Log("Command: " + cmd);
        SetStatus("Command finished");

        // the command may have written; refresh what we show
        var sel = _selected.ToList();
        if (sel.Count > 0 && (cmd.Contains('=') || cmd.Contains('<')))
        {
            await RefreshFilesAsync(sel);
            await OnSelectionChangedAsync();
        }
    }
}
