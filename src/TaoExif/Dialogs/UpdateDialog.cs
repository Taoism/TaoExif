using Avalonia.Controls;
using Avalonia.Media;
using TaoExif.Core;
using TaoExif.Services;

namespace TaoExif.Dialogs;

public enum UpdateChoice
{
    Later,
    Install,
    Download,
    Skip,
}

/// <summary>"A new version is available" with the release notes.</summary>
public sealed class UpdateDialog : DialogWindow
{
    public UpdateDialog(ReleaseInfo release, ReleaseAsset? asset) : base("Update available", 600)
    {
        bool install = UpdateManager.CanInstallItself && asset != null;
        string current = UpdateChecker.CurrentVersion.ToString(3);
        string how = install
            ? "Install now closes TaoExif, installs the update and starts it again."
            : asset != null
                ? $"Download saves {asset.Name} to your Downloads folder after checking its SHA-256."
                : "There is no package for this system in the release; open the release page.";

        var notes = new SelectableTextBlock
        {
            Text = TrimNotes(release.Notes),
            TextWrapping = TextWrapping.Wrap,
            FontSize = 12.5,
        };
        var page = new Button { Content = "Release page" };
        page.Click += (_, _) => MainWindow.OpenUrl(release.Page.ToString());
        var skip = new Button { Content = "Skip this version" };
        skip.Click += (_, _) =>
        {
            Choice = UpdateChoice.Skip;
            Close(false);
        };

        SetBody(Column(10,
            Label($"TaoExif {release.Version.ToString(3)} is available (you have {current})."),
            new Border { Classes = { "panel" }, Padding = new Avalonia.Thickness(12, 8), Child = Scroll(notes, 300) },
            Label(how, true),
            Row(8, page, skip)), install ? "Install now" : "Download", "Later");
        OkButton.IsEnabled = asset != null;
        Choice = UpdateChoice.Later;
        Closing += (_, _) =>
        {
            if (Choice == UpdateChoice.Later && _accepted)
                Choice = install ? UpdateChoice.Install : UpdateChoice.Download;
        };
    }

    private bool _accepted;

    public UpdateChoice Choice { get; private set; }

    protected override Task<bool> ValidateAsync()
    {
        _accepted = true;
        return Task.FromResult(true);
    }

    private static string TrimNotes(string markdown) => UpdateChecker.PlainNotes(markdown);
}
