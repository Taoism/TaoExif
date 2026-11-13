using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;
using Avalonia.Platform;
using Avalonia.Styling;
using Avalonia.Threading;

namespace TaoExif;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

    private static string? _themeMode;
    private static bool _watchingSystem;

    /// <summary>"Light", "Dark", or anything else to follow the operating system (live, when the OS theme changes).</summary>
    public static void ApplyTheme(string? theme)
    {
        if (Current == null)
            return;
        _themeMode = theme;
        var platform = Current.PlatformSettings;
        if (platform != null && !_watchingSystem)
        {
            _watchingSystem = true;
            platform.ColorValuesChanged += (_, _) => Dispatcher.UIThread.Post(() =>
            {
                if (_themeMode is not ("Light" or "Dark"))
                    ApplyTheme(_themeMode);
            });
        }
        // resolved explicitly: going back from Light/Dark to ThemeVariant.Default does not re-read the OS theme
        Current.RequestedThemeVariant = theme switch
        {
            "Light" => ThemeVariant.Light,
            "Dark" => ThemeVariant.Dark,
            _ => platform?.GetColorValues().ThemeVariant == PlatformThemeVariant.Light ? ThemeVariant.Light : ThemeVariant.Dark,
        };
    }

    public override void OnFrameworkInitializationCompleted()
    {
        if (ApplicationLifetime is IClassicDesktopStyleApplicationLifetime desktop)
        {
            string? startPath = desktop.Args is { Length: > 0 } a ? a[0] : null;
            desktop.MainWindow = new MainWindow(startPath);
        }
        base.OnFrameworkInitializationCompleted();
    }
}
