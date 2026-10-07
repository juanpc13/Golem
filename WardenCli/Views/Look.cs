using System.Windows;
using System.Windows.Media;

namespace WardenCli.Views;

/// <summary>The look of a ribbon button, declared in XAML: the glyph drawn above its label and the tint of that glyph. The button's
/// <c>Tag</c> stays what it composes; the look is attached apart, so the two never mix.</summary>
public static class Look
{
    public static readonly DependencyProperty GlyphProperty =
        DependencyProperty.RegisterAttached("Glyph", typeof(string), typeof(Look), new PropertyMetadata("•"));
    public static readonly DependencyProperty TintProperty =
        DependencyProperty.RegisterAttached("Tint", typeof(Brush), typeof(Look), new PropertyMetadata(Brushes.Gainsboro));

    public static string GetGlyph(DependencyObject o) => (string)o.GetValue(GlyphProperty);
    public static void SetGlyph(DependencyObject o, string value) => o.SetValue(GlyphProperty, value);
    public static Brush GetTint(DependencyObject o) => (Brush)o.GetValue(TintProperty);
    public static void SetTint(DependencyObject o, Brush value) => o.SetValue(TintProperty, value);
}
