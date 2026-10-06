using System.Globalization;
using System.Windows;

namespace GolemCli;

/// <summary>The operator says which figure, where, how big, how many steps around and which golems take part; the console lays it out
/// as queues of visits on their tabs (<see cref="Formations"/>). Nothing is sent from here.</summary>
public partial class FormationDialog : Window
{
    public string Figure { get; private set; } = "square";
    public double CenterX { get; private set; }
    public double CenterY { get; private set; }
    public double Side { get; private set; }
    public int Steps { get; private set; }
    public bool Clockwise { get; private set; }
    public string Assignment { get; private set; } = "rank";
    public bool OneErrand { get; private set; } = true;
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
        if (!int.TryParse(StepsBox.Text, out int steps) || steps < 0 || steps > 200)
        { Problem.Text = "the steps are 0 to 200"; return; }
        if (GolemsBox.SelectedItems.Count == 0) { Problem.Text = "choose at least one golem"; return; }
        Figure = Figures.Children.OfType<System.Windows.Controls.RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Content as string ?? "square";
        CenterX = cx; CenterY = cy; Side = measure; Steps = steps;
        Clockwise = ClockwiseBox.IsChecked == true;
        Assignment = ByRankBox.IsChecked == true ? "rank" : "distance";
        OneErrand = OneErrandBox.IsChecked == true;
        Chosen = GolemsBox.SelectedItems.Cast<string>().ToList();
        DialogResult = true;
    }
}
