using System.Diagnostics;
using System.Runtime.InteropServices;
using TaoExif.Core;

namespace TaoExif.Services;

/// <summary>Update policy for the GUI: when to check, and how this kind of install gets a new version.</summary>
public sealed class UpdateManager
{
    private static readonly TimeSpan CheckInterval = TimeSpan.FromHours(20);
    private readonly UpdateChecker _checker = new();

    public static InstallKind Kind { get; } = UpdateChecker.DetectInstallKind();

    /// <summary>Only an installer-based Windows copy replaces itself; other kinds get the verified download.</summary>
    public static bool CanInstallItself => Kind == InstallKind.WindowsInstaller;

    public static string DownloadFolder =>
        Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.UserProfile), "Downloads");

    private static string StagingFolder => Path.Combine(Path.GetTempPath(), "TaoExif-update");

    /// <summary>A verified installer waiting to run when TaoExif closes (Install mode).</summary>
    public string? PendingInstaller { get; private set; }

    public static bool IsDue(AppSettings s) =>
        s.UpdateMode != "Off" && (s.LastUpdateCheck is not { } last || DateTime.UtcNow - last > CheckInterval);

    /// <summary>The newest release if it is newer than this build (and not skipped, unless <paramref name="manual"/>).</summary>
    public async Task<ReleaseInfo?> CheckAsync(AppSettings s, bool manual, CancellationToken ct = default)
    {
        var latest = await _checker.GetLatestAsync(ct);
        s.LastUpdateCheck = DateTime.UtcNow;
        if (latest == null || !UpdateChecker.IsNewer(latest.Version, UpdateChecker.CurrentVersion))
            return null;
        if (!manual && s.SkippedVersion == latest.Version.ToString(3))
            return null;
        return latest;
    }

    public static ReleaseAsset? AssetFor(ReleaseInfo release) =>
        UpdateChecker.PickAsset(release, Kind, RuntimeInformation.OSArchitecture);

    /// <summary>Downloads the installer for later (Install mode).</summary>
    public async Task<bool> StageInstallerAsync(ReleaseInfo release, CancellationToken ct = default)
    {
        if (!CanInstallItself || AssetFor(release) is not { } asset)
            return false;
        PendingInstaller = await _checker.DownloadVerifiedAsync(release, asset, StagingFolder, null, ct);
        return true;
    }

    /// <summary>Downloads the matching package into the user's Downloads folder.</summary>
    public Task<string> DownloadAsync(ReleaseInfo release, ReleaseAsset asset, IProgress<double>? progress, CancellationToken ct = default) =>
        _checker.DownloadVerifiedAsync(release, asset, CanInstallItself ? StagingFolder : DownloadFolder, progress, ct);

    /// <summary>
    /// Starts the Windows installer over the current install. It closes TaoExif (Inno's CloseApplications),
    /// keeps the install folder and scope, and restarts TaoExif afterwards when <paramref name="relaunch"/> is set.
    /// </summary>
    public static void RunInstaller(string installer, bool relaunch, bool visible)
    {
        string dir = AppContext.BaseDirectory.TrimEnd(Path.DirectorySeparatorChar);
        bool allUsers = IsUnder(dir, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFiles))
                        || IsUnder(dir, Environment.GetFolderPath(Environment.SpecialFolder.ProgramFilesX86));
        var psi = new ProcessStartInfo(installer) { UseShellExecute = true };
        foreach (var a in new[]
                 {
                     visible ? "/SILENT" : "/VERYSILENT", "/SUPPRESSMSGBOXES", "/NORESTART", "/CLOSEAPPLICATIONS",
                     allUsers ? "/ALLUSERS" : "/CURRENTUSER", "/DIR=" + dir, relaunch ? "/RELAUNCH=1" : "/RELAUNCH=0",
                 })
        {
            psi.ArgumentList.Add(a);
        }
        Process.Start(psi);
    }

    private static bool IsUnder(string path, string folder) =>
        folder.Length > 0 && path.StartsWith(folder.TrimEnd(Path.DirectorySeparatorChar) + Path.DirectorySeparatorChar, StringComparison.OrdinalIgnoreCase);
}
