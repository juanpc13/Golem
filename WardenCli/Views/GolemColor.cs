using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace WardenCli.Views;

/// <summary>Each golem's colour, the one its body wears in the world: the boxes of the list, the tabs and the map share it.</summary>
public sealed class GolemColor : IValueConverter
{
    public static readonly GolemColor Brush = new();
    /// <summary>The same colour as a translucent FILL: the selected box is painted in its golem's colour (Juan, 7-oct-2026).</summary>
    public static readonly GolemColor Fill = new() { alpha = 0x2c };

    private byte alpha = 0xff;

    public static Color Of(string name) => name switch
    {
        "blue" => Color.FromRgb(0x59, 0xc2, 0xff),
        "red" => Color.FromRgb(0xf0, 0x71, 0x78),
        "green" => Color.FromRgb(0xaa, 0xd9, 0x4c),
        "yellow" => Color.FromRgb(0xff, 0xd1, 0x73),
        "purple" => Color.FromRgb(0xd2, 0xa6, 0xff),
        "orange" => Color.FromRgb(0xff, 0x9e, 0x45),   // the fleet of six (8-oct-2026)
        "cyan" => Color.FromRgb(0x4c, 0xd9, 0xd9),
        _ => Color.FromRgb(0xb0, 0xb0, 0xb0),
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture)
    {
        var c = Of(value as string ?? "");
        return new SolidColorBrush(Color.FromArgb(alpha, c.R, c.G, c.B));
    }

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
