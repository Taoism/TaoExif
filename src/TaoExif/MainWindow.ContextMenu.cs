using System.Diagnostics;
using Avalonia.Controls;
using Avalonia.Input;
using Avalonia.Interactivity;
using Avalonia.VisualTree;
using TaoExif.ViewModels;

namespace TaoExif;

public sealed partial class MainWindow
{
    private static readonly string[] XdgOpenPaths = ["/usr/bin/xdg-open", "/usr/local/bin/xdg-open", "/bin/xdg-open"];

    /// <summary>Right-click menu for files: on the details list, the thumbnails and the preview.</summary>
    private void BuildFileContextMenus()
    {
        FileGrid.ContextMenu = CreateFileMenu();
        ThumbList.ContextMenu = CreateFileMenu();
        PreviewPanel.ContextMenu = CreateFileMenu();
        FileGrid.ContextRequested += OnFileContextRequested;
        ThumbList.ContextRequested += OnFileContextRequested;
    }

    private ContextMenu CreateFileMenu()
    {
        MenuItem Item(string header, EventHandler<RoutedEventArgs> click)
        {
            var mi = new MenuItem { Header = header };
            mi.Click += click;
            return mi;
        }

        var selectionItems = new List<MenuItem>
        {
            Item("Remove metadata…", OnRemoveMetadata),
            Item("Remove GPS", OnRemoveGps),
            Item("Copy metadata from file…", OnCopyMetadata),
            Item("Shift date/time…", OnShiftDates),
            Item("Rename…", OnRename),
            Item("Geotag from track log…", OnGeotag),
            Item("Export metadata (CSV)…", OnExportCsv),
        };
        var singleItems = new List<MenuItem>
        {
            Item("Open", OnOpenSelected),
            Item("Show in folder", OnShowInFolder),
        };
        var copyPath = Item("Copy path", OnCopyPath);

        var menu = new ContextMenu();
        foreach (var mi in singleItems)
            menu.Items.Add(mi);
        menu.Items.Add(copyPath);
        menu.Items.Add(new Separator());
        foreach (var mi in selectionItems)
            menu.Items.Add(mi);

        menu.Opening += (_, _) =>
        {
            bool any = _selected.Count > 0;
            foreach (var mi in selectionItems)
                mi.IsEnabled = any;
            foreach (var mi in singleItems)
                mi.IsEnabled = _selected.Count == 1;
            copyPath.IsEnabled = any;
        };
        return menu;
    }

    /// <summary>Right-clicking a file that is not selected selects it first (like Explorer/Finder).</summary>
    private void OnFileContextRequested(object? sender, ContextRequestedEventArgs e)
    {
        var item = (e.Source as Control)?.GetSelfAndVisualAncestors()
            .OfType<Control>()
            .Select(c => c.DataContext)
            .OfType<FileItem>()
            .FirstOrDefault();
        if (item != null && !_selected.Contains(item))
            SelectFiles([item]);
    }

    private void OnOpenSelected(object? sender, RoutedEventArgs e)
    {
        if (_selected.Count == 1)
            OpenUrl(_selected[0].FullPath);
    }

    private void OnShowInFolder(object? sender, RoutedEventArgs e)
    {
        if (_selected.Count != 1)
            return;
        string path = _selected[0].FullPath;
        try
        {
            if (OperatingSystem.IsWindows())
            {
                string explorer = Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe");
                Process.Start(new ProcessStartInfo(explorer, $"/select,\"{path}\"") { UseShellExecute = false });
            }
            else if (OperatingSystem.IsMacOS())
            {
                Process.Start("/usr/bin/open", ["-R", path]);
            }
            else
            {
                string xdg = XdgOpenPaths.FirstOrDefault(File.Exists) ?? "/usr/bin/xdg-open";
                Process.Start(xdg, [Path.GetDirectoryName(path) ?? "/"]);
            }
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Log("Show in folder failed: " + ex.Message);
        }
    }

    private async void OnCopyPath(object? sender, RoutedEventArgs e)
    {
        if (_selected.Count > 0 && Clipboard != null)
            await Avalonia.Input.Platform.ClipboardExtensions.SetTextAsync(Clipboard, string.Join(Environment.NewLine, SelectedPaths));
    }
}
