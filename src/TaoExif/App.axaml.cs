using Avalonia;
using Avalonia.Controls.ApplicationLifetimes;
using Avalonia.Markup.Xaml;

namespace TaoExif;

public sealed class App : Application
{
    public override void Initialize() => AvaloniaXamlLoader.Load(this);

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
