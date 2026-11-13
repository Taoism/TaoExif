using Avalonia.Interactivity;
using Avalonia.Platform.Storage;
using TaoExif.Core;
using TaoExif.Dialogs;

namespace TaoExif;

public sealed partial class MainWindow
{
    /// <summary>Common guard: exiftool present and files selected.</summary>
    private async Task<(ExifToolService Exif, List<string> Files)?> RequireSelectionAsync()
    {
        if (await RequireExifAsync() is not { } exif)
            return null;
        if (_selected.Count == 0)
        {
            await MessageBox.ShowAsync(this, "Select one or more files first.", MessageKind.Info);
            return null;
        }
        return (exif, SelectedPaths);
    }

    // ---------- list editors ----------

    private async void OnEditColumns(object? sender, RoutedEventArgs e)
    {
        SaveColumnWidths();
        var dlg = new ListEditorDialog("File list columns",
            "Extra columns in the details view. Tag: an exiftool tag name, optionally with a group (Model, ExifIFD:ISO, XMP-dc:Title, Composite:ImageSize).",
            "Header", "Tag",
            _settings.Columns.Select(c => (c.Header, c.Tag)),
            () => AppSettings.DefaultColumns().Select(c => (c.Header, c.Tag)));
        if (!await dlg.ShowDialog<bool>(this))
            return;
        var widths = _settings.Columns.ToDictionary(c => c.Tag, c => c.Width, StringComparer.OrdinalIgnoreCase);
        _settings.Columns = dlg.Result.Select(r => new ColumnDef { Header = r.First, Tag = r.Second, Width = widths.GetValueOrDefault(r.Second, 120) }).ToList();
        SaveSettings();
        await RebuildColumnsAsync();
    }

    private async void OnEditWorkspace(object? sender, RoutedEventArgs e)
    {
        var dlg = new ListEditorDialog("Workspace fields",
            "Fields shown on the Workspace tab. Use a group for writing: XMP-dc:Title, IFD0:Artist, IPTC:Keywords, XMP-photoshop:City.",
            "Label", "Tag",
            _settings.Workspace.Select(w => (w.Label, w.Tag)),
            () => AppSettings.DefaultWorkspace().Select(w => (w.Label, w.Tag)));
        if (!await dlg.ShowDialog<bool>(this))
            return;
        _settings.Workspace = dlg.Result.Select(r => new WorkspaceDef { Label = r.First, Tag = r.Second }).ToList();
        SaveSettings();
        BuildWorkspace();
        await LoadWorkspaceAsync(CancellationToken.None);
    }

    private async void OnEditPresets(object? sender, RoutedEventArgs e)
    {
        var dlg = new ListEditorDialog("Command presets",
            "Saved exiftool commands for the Command tab. The selected files are added after the arguments.",
            "Name", "Arguments",
            _settings.Commands.Select(c => (c.Name, c.Arguments)),
            () => AppSettings.DefaultCommands().Select(c => (c.Name, c.Arguments)));
        if (!await dlg.ShowDialog<bool>(this))
            return;
        _settings.Commands = dlg.Result.Select(r => new CommandPreset { Name = r.First, Arguments = r.Second }).ToList();
        SaveSettings();
        BuildPresets();
    }

    // ---------- tools ----------

    private async void OnShiftDates(object? sender, RoutedEventArgs e)
    {
        if (await RequireSelectionAsync() is not var (exif, files))
            return;
        var dlg = new ShiftDatesDialog(files.Count);
        if (!await dlg.ShowDialog<bool>(this))
            return;
        using (BeginBusy("Shifting dates…"))
        {
            var r = await exif.ShiftDatesAsync(files, dlg.Tags, dlg.Shift, dlg.Forward, CurrentWriteOptions, CancellationToken.None);
            Report($"Shift dates {(dlg.Forward ? "+" : "-")}{dlg.Shift}", r);
        }
        await AfterWriteAsync();
    }

    private async void OnRename(object? sender, RoutedEventArgs e)
    {
        if (await RequireSelectionAsync() is not var (exif, files))
            return;
        var dlg = new RenameDialog(files.Count, _settings.RenameTag, _settings.RenameFormat, async d =>
        {
            var preview = files.Take(200).ToList();
            var r = d.UseTemplate
                ? await exif.RenameTemplateAsync(preview, d.NameTemplate, true, CancellationToken.None)
                : await exif.RenameAsync(preview, d.SourceTag, d.DateFormat, true, CancellationToken.None);
            return FormatDryRun(r);
        });
        if (!await dlg.ShowDialog<bool>(this))
            return;
        if (!dlg.UseTemplate)
        {
            _settings.RenameTag = dlg.SourceTag;
            _settings.RenameFormat = dlg.DateFormat;
        }
        using (BeginBusy("Renaming…"))
        {
            var r = dlg.UseTemplate
                ? await exif.RenameTemplateAsync(files, dlg.NameTemplate, false, CancellationToken.None)
                : await exif.RenameAsync(files, dlg.SourceTag, dlg.DateFormat, false, CancellationToken.None);
            Report("Rename", r);
        }
        if (_currentFolder != null)
            await OpenFolderAsync(_currentFolder, reveal: false);
    }

    /// <summary>Turns "'a.jpg' --> 'b.jpg'" dry-run lines into a readable list.</summary>
    private static string FormatDryRun(ExifToolResult r)
    {
        var lines = r.Text.Split('\n')
            .Select(l => l.TrimEnd())
            .Where(l => l.Contains("-->", StringComparison.Ordinal))
            .Select(l =>
            {
                var parts = l.Split("-->", 2);
                return Path.GetFileName(parts[0].Trim().Trim('\'')) + "  →  " + Path.GetFileName(parts[1].Trim().Trim('\''));
            })
            .ToList();
        string text = lines.Count > 0 ? string.Join("\n", lines) : r.Text.Trim();
        return r.Errors.Length > 0 ? text + "\n\n" + r.Errors : text;
    }

    private async void OnCopyMetadata(object? sender, RoutedEventArgs e)
    {
        if (await RequireSelectionAsync() is not var (exif, files))
            return;
        var dlg = new CopyMetadataDialog(files.Count, null);
        if (!await dlg.ShowDialog<bool>(this))
            return;
        var targets = files.Where(f => !ExifToolService.PathComparer.Equals(Path.GetFullPath(f), Path.GetFullPath(dlg.Source))).ToList();
        using (BeginBusy("Copying metadata…"))
        {
            var r = await exif.CopyMetadataAsync(dlg.Source, targets, dlg.Groups, CurrentWriteOptions, CancellationToken.None);
            Report("Copy metadata from " + Path.GetFileName(dlg.Source), r);
        }
        await AfterWriteAsync();
    }

    private async void OnRemoveMetadata(object? sender, RoutedEventArgs e)
    {
        if (await RequireSelectionAsync() is not var (exif, files))
            return;
        var dlg = new RemoveMetadataDialog(files.Count, _settings.RemoveMetadataAll, _settings.RemoveMetadataUnchecked);
        if (!await dlg.ShowDialog<bool>(this))
            return;
        _settings.RemoveMetadataAll = dlg.RemoveAll;
        _settings.RemoveMetadataUnchecked = dlg.UncheckedGroups;
        SaveSettings();
        using (BeginBusy("Removing metadata…"))
        {
            var r = await exif.RemoveGroupsAsync(files, dlg.RemoveAll, dlg.Groups, CurrentWriteOptions, CancellationToken.None);
            Report("Remove metadata (" + string.Join(" ", ExifToolService.RemoveArgs(dlg.RemoveAll, dlg.Groups)) + ")", r);
        }
        await AfterWriteAsync();
    }

    private async void OnGeotag(object? sender, RoutedEventArgs e)
    {
        if (await RequireSelectionAsync() is not var (exif, files))
            return;
        var dlg = new GeotagDialog(files.Count, _lastTrackLog);
        if (!await dlg.ShowDialog<bool>(this))
            return;
        _lastTrackLog = dlg.TrackLog;
        using (BeginBusy("Geotagging…"))
        {
            var r = await exif.GeotagAsync(files, dlg.TrackLog, dlg.GeoSync, CurrentWriteOptions, CancellationToken.None);
            Report("Geotag", r);
        }
        if (dlg.TrackLog.EndsWith(".gpx", StringComparison.OrdinalIgnoreCase))
        {
            try
            {
                Map.SetTrack(GpxTrack.Load(dlg.TrackLog));
            }
            catch (Exception ex) when (ex is IOException or System.Xml.XmlException)
            {
                Log("Track not drawn: " + ex.Message);
            }
        }
        await AfterWriteAsync();
    }

    private async void OnRemoveGps(object? sender, RoutedEventArgs e)
    {
        if (await RequireSelectionAsync() is not var (exif, files))
            return;
        if (!await MessageBox.ConfirmAsync(this, $"Remove GPS data from {files.Count} file(s)?", "Remove"))
            return;
        using (BeginBusy("Removing GPS…"))
        {
            var r = await exif.SetGpsAsync(files, null, true, CurrentWriteOptions, CancellationToken.None);
            Report("Remove GPS", r);
        }
        await AfterWriteAsync();
    }

    private async void OnDeleteBackups(object? sender, RoutedEventArgs e)
    {
        if (_currentFolder == null || await RequireExifAsync() is not { } exif)
            return;
        bool recurse = _settings.IncludeSubfolders;
        var opts = new EnumerationOptions { RecurseSubdirectories = recurse, IgnoreInaccessible = true };
        int count = Directory.EnumerateFiles(_currentFolder, "*_original", opts).Count();
        if (count == 0)
        {
            SetStatus("No *_original backup files here");
            return;
        }
        if (!await MessageBox.ConfirmAsync(this, $"Delete {count} exiftool backup file(s) (*_original) in {_currentFolder}{(recurse ? " and its subfolders" : "")}?", "Delete"))
            return;
        using (BeginBusy("Deleting backups…"))
        {
            var r = await exif.DeleteBackupsAsync(_currentFolder, recurse, CancellationToken.None);
            Report("Delete backups", r);
        }
    }

    private async Task AfterWriteAsync()
    {
        var sel = _selected.ToList();
        await RefreshFilesAsync(sel);
        // writes may have changed sizes and dates on disk: refresh the file rows' basic info via a folder reload only when needed
        await OnSelectionChangedAsync();
    }

    // ---------- export ----------

    private async void OnExportCsv(object? sender, RoutedEventArgs e) => await ExportAsync(true);

    private async void OnExportJson(object? sender, RoutedEventArgs e) => await ExportAsync(false);

    private async Task ExportAsync(bool csv)
    {
        if (await RequireSelectionAsync() is not var (exif, files))
            return;
        var file = await StorageProvider.SaveFilePickerAsync(new FilePickerSaveOptions
        {
            Title = csv ? "Export CSV" : "Export JSON",
            SuggestedFileName = "metadata" + (csv ? ".csv" : ".json"),
            DefaultExtension = csv ? "csv" : "json",
            FileTypeChoices = [new FilePickerFileType(csv ? "CSV" : "JSON") { Patterns = [csv ? "*.csv" : "*.json"] }],
        });
        string? path = file?.TryGetLocalPath();
        if (path == null)
            return;
        using (BeginBusy("Exporting…"))
        {
            string text = await exif.ExportAsync(files, csv, CancellationToken.None);
            await File.WriteAllTextAsync(path, text, CancellationToken.None);
        }
        SetStatus($"Exported {files.Count} file(s) to {path}");
    }
}
