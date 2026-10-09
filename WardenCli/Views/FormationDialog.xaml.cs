using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace WardenCli.Views;

/// <summary>The operator says which figure, where, how big, how its places are shared and which golems take part; the console composes
/// the `form` and `take` lines on the SELECTED golem's tab (propuesta 104: the formation is the golem's, and it tells its fleet). A DOUBLE
/// RING asks the two radii and which golems ride each ring. Nothing is sent from here.</summary>
public partial class FormationDialog : Window
{
    public string Figure { get; private set; } = "square";
    public double CenterX { get; private set; }
    public double CenterY { get; private set; }
    /// <summary>The side of a polygon, the radius of a circle, the OUTER radius of a double ring.</summary>
    public double Measure { get; private set; }
    /// <summary>The inner radius of a double ring.</summary>
    public double InnerRadius { get; private set; }
    /// <summary>rank or distance.</summary>
    public string Policy { get; private set; } = "rank";
    /// <summary>The golems chosen — the outer ring's, for a double ring.</summary>
    public List<string> Chosen { get; private set; } = new();
    /// <summary>The golems of the inner ring, for a double ring.</summary>
    public List<string> InnerChosen { get; private set; } = new();

    private readonly IReadOnlyList<string> golems;

    public FormationDialog(IReadOnlyList<string> golems, string point)
    {
        this.golems = golems;
        InitializeComponent();
        GolemsBox.ItemsSource = golems;
        InnerGolemsBox.ItemsSource = golems;
        GolemsBox.SelectAll();
        if (point.Contains(',')) CenterBox.Text = point.Trim();
    }

    private string Picked => Figures.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Content as string ?? "square";

    // the figure changes what the dialog asks: a side, a radius, or the two radii and the two rings' golems
    private void Figure_Checked(object sender, RoutedEventArgs e)
    {
        if (MeasureBox == null) return;   // the first radio is checked while the window is still being built
        bool rings = Picked == "double ring";
        MeasureLabel.Text = Picked switch { "circle" => "radius", "double ring" => "outer radius", _ => "side" };
        InnerLabel.Visibility = InnerBox.Visibility = OuterHeader.Visibility = rings ? Visibility.Visible : Visibility.Collapsed;
        InnerColumn.Width = rings ? new GridLength(1, GridUnitType.Star) : new GridLength(0);
        GolemsLabel.Text = rings ? "golems of each ring" : "golems of the fleet";
        if (rings)
        {
            if (MeasureBox.Text == "2.0") MeasureBox.Text = "3.0";
            // the names sorted, the first half on the outer ring and the rest on the inner one — the operator changes it as it likes
            var sorted = golems.OrderBy(n => n, StringComparer.Ordinal).ToList();
            int outer = (sorted.Count + 1) / 2;
            GolemsBox.SelectedItems.Clear();
            InnerGolemsBox.SelectedItems.Clear();
            foreach (var n in sorted.Take(outer)) GolemsBox.SelectedItems.Add(n);
            foreach (var n in sorted.Skip(outer)) InnerGolemsBox.SelectedItems.Add(n);
        }
        else if (InnerGolemsBox.SelectedItems.Count > 0)
        {
            InnerGolemsBox.SelectedItems.Clear();
            GolemsBox.SelectAll();
        }
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Problem.Text = "";
        var center = CenterBox.Text.Split(',');
        if (center.Length != 2 || !double.TryParse(center[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double cx) || !double.TryParse(center[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double cy))
        { Problem.Text = "the centre is x,y"; return; }
        Figure = Picked;
        bool rings = Figure == "double ring";
        if (!double.TryParse(MeasureBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double measure) || measure <= 0)
        { Problem.Text = $"the {MeasureLabel.Text} is a length above zero"; return; }
        if (GolemsBox.SelectedItems.Count == 0) { Problem.Text = rings ? "choose at least one golem for the outer ring" : "choose at least one golem"; return; }
        if (rings)
        {
            if (!double.TryParse(InnerBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double inner) || inner <= 0)
            { Problem.Text = "the inner radius is a length above zero"; return; }
            if (inner >= measure) { Problem.Text = "the inner radius is smaller than the outer one"; return; }
            if (InnerGolemsBox.SelectedItems.Count == 0) { Problem.Text = "choose at least one golem for the inner ring"; return; }
            var both = GolemsBox.SelectedItems.Cast<string>().Intersect(InnerGolemsBox.SelectedItems.Cast<string>()).ToList();
            if (both.Count > 0) { Problem.Text = $"a golem rides one ring: {string.Join(", ", both)} on both"; return; }
            InnerRadius = inner;
            InnerChosen = InnerGolemsBox.SelectedItems.Cast<string>().ToList();
        }
        Policy = ByRankBox.IsChecked == true ? "rank" : "distance";
        CenterX = cx; CenterY = cy; Measure = measure;
        Chosen = GolemsBox.SelectedItems.Cast<string>().ToList();
        DialogResult = true;
    }
}
