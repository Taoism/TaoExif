using Avalonia.Data.Converters;
using Avalonia.Media;

namespace TaoExif;

public static class Converters
{
    private static readonly IBrush Modified = new SolidColorBrush(Color.Parse("#FF7CC47C"));
    private static readonly IBrush Normal = new SolidColorBrush(Color.Parse("#FFE8E8EA"));

    public static readonly IValueConverter ModifiedBrush = new FuncValueConverter<bool, IBrush>(m => m ? Modified : Normal);
}
