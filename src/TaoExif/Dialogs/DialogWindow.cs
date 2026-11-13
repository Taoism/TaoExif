using Avalonia;
using Avalonia.Controls;
using Avalonia.Controls.Primitives;
using Avalonia.Input;
using Avalonia.Layout;
using Avalonia.Media;
using Avalonia.Platform.Storage;

namespace TaoExif.Dialogs;

/// <summary>Base for the small modal dialogs: dark, centred on the owner, OK/Cancel row at the bottom.</summary>
public class DialogWindow : Window
{
    private Button? _ok;

    protected DialogWindow(string title, double width)
    {
        Title = title;
        Width = width;
        SizeToContent = SizeToContent.Height;
        CanResize = false;
        ShowInTaskbar = false;
        WindowStartupLocation = WindowStartupLocation.CenterOwner;
        Icon = MainWindow.AppIcon;
    }

    protected Button OkButton => _ok!;

    /// <summary>Puts <paramref name="body"/> above the button row.</summary>
    protected void SetBody(Control body, string okText = "OK", string? cancelText = "Cancel")
    {
        _ok = new Button { Content = okText, IsDefault = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
        _ok.Classes.Add("accent");
        _ok.Click += async (_, _) =>
        {
            if (await ValidateAsync())
                Close(true);
        };

        var buttons = new StackPanel { Orientation = Orientation.Horizontal, Spacing = 8, HorizontalAlignment = HorizontalAlignment.Right };
        buttons.Children.Add(_ok);
        if (cancelText != null)
        {
            var cancel = new Button { Content = cancelText, IsCancel = true, MinWidth = 90, HorizontalContentAlignment = HorizontalAlignment.Center };
            cancel.Click += (_, _) => Close(false);
            buttons.Children.Add(cancel);
        }

        var footer = new Border
        {
            Classes = { "footer" },
            Padding = new Thickness(16, 12),
            Child = buttons,
        };
        DockPanel.SetDock(footer, Dock.Bottom);

        var dock = new DockPanel();
        dock.Children.Add(footer);
        dock.Children.Add(new Border { Padding = new Thickness(18, 16), Child = body });
        Content = dock;
    }

    protected override void OnKeyDown(KeyEventArgs e)
    {
        base.OnKeyDown(e);
        if (e.Key == Key.Escape)
        {
            Close(false);
            e.Handled = true;
        }
    }

    /// <summary>Return false to keep the dialog open (e.g. after showing a validation message).</summary>
    protected virtual Task<bool> ValidateAsync() => Task.FromResult(true);

    // ---------- small builders ----------

    public static TextBlock Label(string text, bool muted = false)
    {
        var tb = new TextBlock { Text = text, TextWrapping = TextWrapping.Wrap, VerticalAlignment = VerticalAlignment.Center };
        if (muted)
            tb.Classes.Add("muted");
        return tb;
    }

    public static StackPanel Column(double spacing, params Control[] children)
    {
        var sp = new StackPanel { Spacing = spacing };
        foreach (var c in children)
            sp.Children.Add(c);
        return sp;
    }

    public static StackPanel Row(double spacing, params Control[] children)
    {
        var sp = new StackPanel { Orientation = Orientation.Horizontal, Spacing = spacing };
        foreach (var c in children)
            sp.Children.Add(c);
        return sp;
    }

    /// <summary>A text box with a "…" browse button next to it.</summary>
    public static Grid PathRow(TextBox box, Func<Task> browse)
    {
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("*,Auto"), ColumnSpacing = 6 };
        grid.Children.Add(box);
        var btn = new Button { Content = "Browse…" };
        btn.Click += async (_, _) => await browse();
        Grid.SetColumn(btn, 1);
        grid.Children.Add(btn);
        return grid;
    }

    public async Task<string?> PickFileAsync(string title, params FilePickerFileType[] types)
    {
        var files = await StorageProvider.OpenFilePickerAsync(new FilePickerOpenOptions
        {
            Title = title,
            AllowMultiple = false,
            FileTypeFilter = types.Length > 0 ? types : null,
        });
        return files.Count > 0 ? files[0].TryGetLocalPath() : null;
    }

    public static Separator Rule() => new() { Margin = new Thickness(0, 4), Classes = { "rule" } };

    public static ScrollViewer Scroll(Control content, double maxHeight) => new()
    {
        Content = content,
        MaxHeight = maxHeight,
        HorizontalScrollBarVisibility = ScrollBarVisibility.Disabled,
    };
}
