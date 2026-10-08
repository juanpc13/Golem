using System.Globalization;
using System.Windows;

namespace WardenCli.Views;

/// <summary>The operator says which figure, where, how big, how its vertices are shared and which golems take part; the console puts each
/// golem on its first vertex, on its tab (<see cref="Formations"/>). No steps and no sense here (8-oct-2026): the formation is turned
/// afterwards with ↻ ↺, a step per click. Nothing is sent from here.</summary>
public partial class FormationDialog : Window
{
    public string Figure { get; private set; } = "square";
    public double CenterX { get; private set; }
    public double CenterY { get; private set; }
    public double Side { get; private set; }
    public string Assignment { get; private set; } = "rank";
    public List<string> Chosen { get; private set; } = new();

    public FormationDialog(IReadOnlyList<string> golems, string point)
    {
        InitializeComponent();
        GolemsBox.ItemsSource = golems;
        GolemsBox.SelectAll();
        if (point.Contains(',')) CenterBox.Text = point.Trim();
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        var center = CenterBox.Text.Split(',');
        if (center.Length != 2 || !double.TryParse(center[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double cx) || !double.TryParse(center[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double cy))
        { Problem.Text = "the centre is x,y"; return; }
        if (!double.TryParse(MeasureBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double measure) || measure <= 0)
        { Problem.Text = "the side is a length above zero"; return; }
        if (GolemsBox.SelectedItems.Count == 0) { Problem.Text = "choose at least one golem"; return; }
        Figure = Figures.Children.OfType<System.Windows.Controls.RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Content as string ?? "square";
        CenterX = cx; CenterY = cy; Side = measure;
        Assignment = ByRankBox.IsChecked == true ? "rank" : "distance";
        Chosen = GolemsBox.SelectedItems.Cast<string>().ToList();
        DialogResult = true;
    }
}
