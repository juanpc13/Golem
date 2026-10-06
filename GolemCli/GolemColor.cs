using System.Globalization;
using System.Windows.Data;
using System.Windows.Media;

namespace GolemCli;

/// <summary>Each golem's colour, the one its body wears in the world: the boxes of the list, the tabs and the map share it.</summary>
public sealed class GolemColor : IValueConverter
{
    public static readonly GolemColor Brush = new();

    public static Color Of(string name) => name switch
    {
        "blue" => Color.FromRgb(0x59, 0xc2, 0xff),
        "red" => Color.FromRgb(0xf0, 0x71, 0x78),
        "green" => Color.FromRgb(0xaa, 0xd9, 0x4c),
        "yellow" => Color.FromRgb(0xff, 0xd1, 0x73),
        "purple" => Color.FromRgb(0xd2, 0xa6, 0xff),
        _ => Color.FromRgb(0xb0, 0xb0, 0xb0),
    };

    public object Convert(object value, Type targetType, object parameter, CultureInfo culture) =>
        new SolidColorBrush(Of(value as string ?? ""));

    public object ConvertBack(object value, Type targetType, object parameter, CultureInfo culture) => throw new NotSupportedException();
}
