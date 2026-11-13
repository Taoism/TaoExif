using Avalonia;
using Avalonia.Controls;
using Avalonia.Layout;
using Avalonia.Media;

namespace TaoExif.Dialogs;

public enum MessageKind
{
    Info,
    Warning,
    Error,
    Question,
}

/// <summary>A dark message box (the OS ones are light on some platforms).</summary>
public sealed class MessageBox : DialogWindow
{
    private MessageBox(string title, string text, MessageKind kind, string ok, string? cancel) : base(title, 460)
    {
        var (glyph, color) = kind switch
        {
            MessageKind.Error => ("M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20Zm-1 5h2v7h-2Zm0 9h2v2h-2Z", "#FFE06060"),
            MessageKind.Warning => ("M12 2 1 21h22Zm-1 7h2v6h-2Zm0 8h2v2h-2Z", "#FFE0A040"),
            MessageKind.Question => ("M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20Zm0 15.5a1.25 1.25 0 1 1 0-2.5a1.25 1.25 0 0 1 0 2.5Zm1.6-6.3c-.8.5-1 .9-1 1.8h-2c0-1.6.6-2.4 1.7-3.1c.8-.5 1.2-.9 1.2-1.6c0-.8-.6-1.4-1.6-1.4c-1 0-1.6.6-1.7 1.5H8.3C8.4 6.5 9.9 5 12 5c2.2 0 3.7 1.3 3.7 3.1c0 1.4-.8 2.3-2.1 3.1Z", "#FF3D7FD6"),
            _ => ("M12 2a10 10 0 1 0 0 20a10 10 0 1 0 0-20Zm-1 5h2v2h-2Zm0 4h2v6h-2Z", "#FF3D7FD6"),
        };
        var icon = new Avalonia.Controls.Shapes.Path
        {
            Data = Geometry.Parse(glyph),
            Fill = new SolidColorBrush(Color.Parse(color)),
            Width = 30,
            Height = 30,
            Stretch = Stretch.Uniform,
            VerticalAlignment = VerticalAlignment.Top,
            Margin = new Thickness(0, 2, 14, 0),
        };
        var message = new SelectableTextBlock { Text = text, TextWrapping = TextWrapping.Wrap, MaxWidth = 380 };
        var grid = new Grid { ColumnDefinitions = new ColumnDefinitions("Auto,*") };
        grid.Children.Add(icon);
        Grid.SetColumn(message, 1);
        grid.Children.Add(message);
        SetBody(Scroll(grid, 420), ok, cancel);
    }

    public static Task ShowAsync(Window owner, string text, MessageKind kind = MessageKind.Info, string title = "TaoExif") =>
        new MessageBox(title, text, kind, "OK", null).ShowDialog<bool>(owner);

    public static Task<bool> ConfirmAsync(Window owner, string text, string okText = "OK", string title = "TaoExif") =>
        new MessageBox(title, text, MessageKind.Question, okText, "Cancel").ShowDialog<bool>(owner);
}
