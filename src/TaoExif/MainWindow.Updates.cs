using System.Diagnostics;
using Avalonia.Interactivity;
using TaoExif.Core;
using TaoExif.Dialogs;
using TaoExif.Services;

namespace TaoExif;

public sealed partial class MainWindow
{
    private readonly UpdateManager _updates = new();
    private ReleaseInfo? _availableRelease;
    private bool _installing;

    /// <summary>Background check at startup, at most about once a day, unless updates are off.</summary>
    private async Task AutoCheckUpdatesAsync()
    {
        UpdateManager.CleanStaging();
        if (!UpdateManager.IsDue(_settings))
            return;
        ReleaseInfo? release;
        try
        {
            release = await _updates.CheckAsync(_settings, manual: false, CancellationToken.None);
            SaveSettings();
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
        {
            Log("Update check failed: " + ex.Message);
            return;
        }
        if (release == null)
            return;

        _availableRelease = release;
        Log($"TaoExif {release.Version.ToString(3)} is available.");
        if (_settings.UpdateMode == "Install" && UpdateManager.CanInstallItself)
        {
            try
            {
                if (await _updates.StageInstallerAsync(release, CancellationToken.None))
                {
                    ShowUpdateButton($"Update {release.Version.ToString(3)}: restart now", $"TaoExif {release.Version.ToString(3)} is downloaded and verified; it installs when you close TaoExif.");
                    return;
                }
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException)
            {
                Log("Update download failed: " + ex.Message);
            }
        }
        ShowUpdateButton($"Update {release.Version.ToString(3)} available", "Click for details.");
    }

    private void ShowUpdateButton(string text, string tip)
    {
        UpdateButton.Content = text;
        Avalonia.Controls.ToolTip.SetTip(UpdateButton, tip);
        UpdateButton.IsVisible = true;
    }

    private async void OnUpdateButton(object? sender, RoutedEventArgs e)
    {
        if (_updates.PendingInstaller is { } installer)
        {
            _installing = true;
            UpdateManager.RunInstaller(installer, relaunch: true, visible: true);
            Close();
            return;
        }
        if (_availableRelease != null)
            await OfferUpdateAsync(_availableRelease);
    }

    private async void OnCheckUpdates(object? sender, RoutedEventArgs e)
    {
        ReleaseInfo? release;
        using (BeginBusy("Checking for updates…"))
        {
            try
            {
                release = await _updates.CheckAsync(_settings, manual: true, CancellationToken.None);
                SaveSettings();
            }
            catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or System.Text.Json.JsonException or InvalidOperationException)
            {
                await MessageBox.ShowAsync(this, "Could not check for updates:\n" + ex.Message, MessageKind.Warning);
                return;
            }
        }
        SetStatus("Ready");
        if (release == null)
        {
            await MessageBox.ShowAsync(this, $"You have the latest version (TaoExif {UpdateChecker.CurrentVersion.ToString(3)}).", MessageKind.Info, "Updates");
            return;
        }
        _availableRelease = release;
        await OfferUpdateAsync(release);
    }

    private async Task OfferUpdateAsync(ReleaseInfo release)
    {
        var asset = UpdateManager.AssetFor(release);
        var dlg = new UpdateDialog(release, asset);
        await dlg.ShowDialog<bool>(this);
        switch (dlg.Choice)
        {
            case UpdateChoice.Skip:
                _settings.SkippedVersion = release.Version.ToString(3);
                SaveSettings();
                UpdateButton.IsVisible = false;
                break;
            case UpdateChoice.Install or UpdateChoice.Download when asset != null:
                await DownloadAndApplyAsync(release, asset, dlg.Choice == UpdateChoice.Install);
                break;
        }
    }

    private async Task DownloadAndApplyAsync(ReleaseInfo release, ReleaseAsset asset, bool install)
    {
        string path;
        using (BeginBusy($"Downloading {asset.Name}…"))
        {
            var progress = new Progress<double>(p => SetStatus($"Downloading {asset.Name}… {p:P0}"));
            try
            {
                path = await _updates.DownloadAsync(release, asset, progress, CancellationToken.None);
            }
            catch (Exception ex) when (ex is HttpRequestException or IOException or TaskCanceledException or InvalidOperationException or UnauthorizedAccessException)
            {
                SetStatus("Update download failed");
                await MessageBox.ShowAsync(this, "The update could not be downloaded:\n" + ex.Message, MessageKind.Error);
                return;
            }
        }
        Log($"Downloaded and verified {path}");

        if (install)
        {
            _installing = true;
            UpdateManager.RunInstaller(path, relaunch: true, visible: true);
            Close();
            return;
        }

        SetStatus("Downloaded " + path);
        string how = UpdateManager.Kind switch
        {
            InstallKind.LinuxDeb => $"Install it with:\n  sudo apt install \"{path}\"",
            InstallKind.MacApp => "Unzip it and replace TaoExif.app in Applications.",
            _ => "Unpack it over this copy of TaoExif (close TaoExif first).",
        };
        await MessageBox.ShowAsync(this, $"TaoExif {release.Version.ToString(3)} was downloaded and its checksum verified:\n{path}\n\n{how}", MessageKind.Info, "Update downloaded");
        RevealFile(path);
    }

    private static void RevealFile(string path)
    {
        try
        {
            if (OperatingSystem.IsWindows())
                Process.Start(new ProcessStartInfo(Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.Windows), "explorer.exe"), $"/select,\"{path}\"") { UseShellExecute = false });
            else if (OperatingSystem.IsMacOS())
                Process.Start("/usr/bin/open", ["-R", path]);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine(ex.Message);
        }
    }

    /// <summary>Install mode: a staged installer runs quietly after TaoExif has closed.</summary>
    private void InstallPendingUpdateOnExit()
    {
        if (_installing || _updates.PendingInstaller is not { } installer || _settings.UpdateMode != "Install")
            return;
        try
        {
            UpdateManager.RunInstaller(installer, relaunch: false, visible: false);
        }
        catch (Exception ex) when (ex is System.ComponentModel.Win32Exception or InvalidOperationException)
        {
            Debug.WriteLine(ex.Message);
        }
    }
}
