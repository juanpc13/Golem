using WardenCli.Formations;
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
    /// <summary>The inner radius of a double ring, a propeller's hub.</summary>
    public double InnerRadius { get; private set; }
    /// <summary>A propeller's blades (propuesta 114).</summary>
    public int Blades { get; private set; } = 3;
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

    /// <summary>THE DIALOG SEEDED with a formation told already (13-oct-2026; Juan: "¿podríamos reeditar la formación para que tome otros valores de
    /// la creación?"): its figure, centre, size, inner radius, policy and fleet as the row says them, so the operator changes what it likes and the
    /// lines are rewritten on the tabs under the same name.</summary>
    public void Seed(FormationView formation)
    {
        Title = $"Formation — {formation.Name} edited: its form (and take) lines rewritten on the tabs";
        foreach (var r in Figures.Children.OfType<RadioButton>()) r.IsChecked = (r.Content as string) == formation.Shape;
        CenterBox.Text = formation.CenterText;
        MeasureBox.Text = formation.Measure.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        if (formation.IsDoubleRing && formation.AsFigure() is DoubleRing rings) InnerBox.Text = rings.InnerRadius.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        if (formation.IsPropeller && formation.AsFigure() is Propeller propeller)
        {
            InnerBox.Text = propeller.Hub.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            BladesBox.Text = propeller.Blades.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        ByRankBox.IsChecked = formation.Policy != "distance";
        var names = formation.CrewNames;
        var outer = formation.IsDoubleRing && formation.Division > 0 ? names.Take(formation.Division).ToList() : names.ToList();
        var inner = formation.IsDoubleRing && formation.Division > 0 ? names.Skip(formation.Division).ToList() : new List<string>();
        GolemsBox.SelectedItems.Clear();
        InnerGolemsBox.SelectedItems.Clear();
        foreach (var n in golems) { if (outer.Contains(n)) GolemsBox.SelectedItems.Add(n); if (inner.Contains(n)) InnerGolemsBox.SelectedItems.Add(n); }
    }

    // the figure changes what the dialog asks: a side, a radius, or the two radii and the two rings' golems
    private void Figure_Checked(object sender, RoutedEventArgs e)
    {
        if (MeasureBox == null) return;   // the first radio is checked while the window is still being built
        bool rings = Picked == "double ring";
        bool propeller = Picked == "propeller";
        MeasureLabel.Text = Picked switch { "circle" => "radius", "double ring" => "outer radius", "propeller" => "radius (the tips)", _ => "side" };
        InnerLabel.Text = propeller ? "hub radius" : "inner radius";
        InnerLabel.Visibility = InnerBox.Visibility = rings || propeller ? Visibility.Visible : Visibility.Collapsed;
        OuterHeader.Visibility = rings ? Visibility.Visible : Visibility.Collapsed;
        BladesPanel.Visibility = propeller ? Visibility.Visible : Visibility.Collapsed;
        if (propeller && MeasureBox.Text == "2.0") MeasureBox.Text = "3.0";
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
        if (Figure == "propeller")
        {
            if (!double.TryParse(InnerBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double hub) || hub <= 0 || hub >= measure)
            { Problem.Text = "the hub radius is above zero and smaller than the radius"; return; }
            if (!int.TryParse(BladesBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int blades) || blades < 2 || blades > 12)
            { Problem.Text = "a propeller has from 2 to 12 blades"; return; }
            InnerRadius = hub;
            Blades = blades;
        }
        Policy = ByRankBox.IsChecked == true ? "rank" : "distance";
        CenterX = cx; CenterY = cy; Measure = measure;
        Chosen = GolemsBox.SelectedItems.Cast<string>().ToList();
        DialogResult = true;
    }
}
