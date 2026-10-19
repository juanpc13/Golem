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
    /// <summary>The side of a polygon, the radius of a circle, a propeller's tips.</summary>
    public double Measure { get; private set; }
    /// <summary>A propeller's hub.</summary>
    public double InnerRadius { get; private set; }
    /// <summary>A propeller's blades (propuesta 114).</summary>
    public int Blades { get; private set; } = 3;
    /// <summary>How the places are shared: rank, distance or the game.</summary>
    public Policy Policy { get; private set; } = Policy.Rank;
    /// <summary>The golems chosen for the fleet.</summary>
    public List<string> Chosen { get; private set; } = new();
    /// <summary>The golem the last of this fleet cues when everybody finished (propuesta 117), lower case; "" nobody.</summary>
    public string CueTo { get; private set; } = "";
    /// <summary>Whether the formation starts on a cue: no take is written for it.</summary>
    public bool OnCue { get; private set; }
    /// <summary>THE PULL (propuesta 118): the formation this one starts after and the golem asked; "" none.</summary>
    public string After { get; private set; } = "";
    public string AskTo { get; private set; } = "";
    private const string None = "— none —";
    /// <summary>The formations the console knows (told, or on the tabs), to pick the one waited for.</summary>
    public void KnowsFormations(IEnumerable<string> names)
    {
        var kept = AfterBox.SelectedItem as string;
        AfterBox.ItemsSource = new[] { None }.Concat(names.Distinct().OrderBy(n => n, StringComparer.Ordinal)).ToList();
        AfterBox.SelectedItem = kept != null && kept != None && AfterBox.Items.Contains(kept) ? kept : None;
    }

    private readonly IReadOnlyList<string> golems;

    public FormationDialog(IReadOnlyList<string> golems, string point)
    {
        this.golems = golems;
        InitializeComponent();
        GolemsBox.ItemsSource = golems;
        GolemsBox.SelectAll();
        GolemsBox.SelectionChanged += (_, _) => RefreshCue();   // the cue goes to the next group: never one of this fleet (Juan: "no deberían aparecer los que tengo seleccionados")
        RefreshCue();
        if (point.Contains(',')) CenterBox.Text = point.Trim();
    }

    private string Picked => Figures.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Content as string ?? "square";
    private const string Nobody = "— nobody —";

    // the golems a cue may go to: the ones of the workspace NOT in this fleet (propuesta 117); the choice is kept while it is still one of them
    private void RefreshCue()
    {
        if (CueBox == null) return;
        var chosen = GolemsBox.SelectedItems.Cast<string>().ToHashSet(StringComparer.Ordinal);
        var kept = CueBox.SelectedItem as string;
        CueBox.ItemsSource = new[] { Nobody }.Concat(golems.Where(g => !chosen.Contains(g))).ToList();
        CueBox.SelectedItem = kept != null && kept != Nobody && !chosen.Contains(kept) ? kept : Nobody;
        var keptAsk = AskBox.SelectedItem as string;
        AskBox.ItemsSource = new[] { Nobody }.Concat(golems.Where(g => !chosen.Contains(g))).ToList();
        AskBox.SelectedItem = keptAsk != null && keptAsk != Nobody && !chosen.Contains(keptAsk) ? keptAsk : Nobody;
        if (AfterBox.ItemsSource == null) AfterBox.ItemsSource = new[] { None };
        if (AfterBox.SelectedItem == null) AfterBox.SelectedItem = None;
    }

    /// <summary>THE DIALOG SEEDED with a formation told already (13-oct-2026; Juan: "¿podríamos reeditar la formación para que tome otros valores de
    /// la creación?"): its figure, centre, size, inner radius, policy and fleet as the row says them, so the operator changes what it likes and the
    /// lines are rewritten on the tabs under the same name.</summary>
    public void Seed(FormationView formation)
    {
        Title = $"Formation — {formation.Name} edited: its form (and take) lines rewritten on the tabs";
        foreach (var r in Figures.Children.OfType<RadioButton>()) r.IsChecked = (r.Content as string) == formation.Shape;
        CenterBox.Text = formation.CenterText;
        MeasureBox.Text = formation.Measure.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
        if (formation.IsPropeller && formation.AsFigure() is Propeller propeller)
        {
            InnerBox.Text = propeller.Hub.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
            BladesBox.Text = propeller.Blades.ToString(System.Globalization.CultureInfo.InvariantCulture);
        }
        ByRankBox.IsChecked = formation.Policy == Policy.Rank;
        ByDistanceBox.IsChecked = formation.Policy == Policy.Distance;
        ByGameBox.IsChecked = formation.Policy == Policy.RockPaperScissors;
        OnCueBox.IsChecked = formation.OnCue && formation.After == "";
        var names = formation.CrewNames;
        GolemsBox.SelectedItems.Clear();
        foreach (var n in golems) if (names.Contains(n)) GolemsBox.SelectedItems.Add(n);
        RefreshCue();
        CueBox.SelectedItem = formation.CueTo != "" && golems.Contains(formation.CueTo) && !names.Contains(formation.CueTo) ? formation.CueTo : Nobody;
        if (formation.After != "") { KnowsFormations(new[] { formation.After }); AfterBox.SelectedItem = formation.After; }
        AskBox.SelectedItem = formation.AskTo != "" && golems.Contains(formation.AskTo) && !names.Contains(formation.AskTo) ? formation.AskTo : Nobody;
    }

    // the figure changes what the dialog asks: a side, a radius, or a propeller's radius with its hub and its blades
    private void Figure_Checked(object sender, RoutedEventArgs e)
    {
        if (MeasureBox == null) return;   // the first radio is checked while the window is still being built
        bool propeller = Picked == "propeller";
        MeasureLabel.Text = Picked switch { "circle" => "radius", "propeller" => "radius (the tips)", _ => "side" };
        InnerLabel.Text = "hub radius";
        InnerLabel.Visibility = InnerBox.Visibility = propeller ? Visibility.Visible : Visibility.Collapsed;
        BladesPanel.Visibility = propeller ? Visibility.Visible : Visibility.Collapsed;
        if (propeller && MeasureBox.Text == "2.0") MeasureBox.Text = "3.0";
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Problem.Text = "";
        var center = CenterBox.Text.Split(',');
        if (center.Length != 2 || !double.TryParse(center[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double cx) || !double.TryParse(center[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double cy))
        { Problem.Text = "the centre is x,y"; return; }
        Figure = Picked;
        if (!double.TryParse(MeasureBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double measure) || measure <= 0)
        { Problem.Text = $"the {MeasureLabel.Text} is a length above zero"; return; }
        if (GolemsBox.SelectedItems.Count == 0) { Problem.Text = "choose at least one golem"; return; }
        if (Figure == "propeller")
        {
            if (!double.TryParse(InnerBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double hub) || hub <= 0 || hub >= measure)
            { Problem.Text = "the hub radius is above zero and smaller than the radius"; return; }
            if (!int.TryParse(BladesBox.Text, NumberStyles.Integer, CultureInfo.InvariantCulture, out int blades) || blades < 2 || blades > 12)
            { Problem.Text = "a propeller has from 2 to 12 blades"; return; }
            InnerRadius = hub;
            Blades = blades;
        }
        Policy = ByGameBox.IsChecked == true ? Policy.RockPaperScissors : ByDistanceBox.IsChecked == true ? Policy.Distance : Policy.Rank;
        string cue = CueBox.SelectedItem as string is { } picked && picked != Nobody ? picked.Trim().ToLowerInvariant() : "";
        if (cue != "" && GolemsBox.SelectedItems.Contains(cue)) { Problem.Text = $"the cue goes to the next group: {cue} is of this fleet"; return; }
        CueTo = cue;
        string after = AfterBox.SelectedItem as string is { } pickedAfter && pickedAfter != None ? pickedAfter.Trim().ToLowerInvariant() : "";
        string ask = AskBox.SelectedItem as string is { } pickedAsk && pickedAsk != Nobody ? pickedAsk.Trim().ToLowerInvariant() : "";
        if ((after == "") != (ask == "")) { Problem.Text = "the pull needs both: the formation waited for, and the golem asked"; return; }
        After = after; AskTo = ask;
        OnCue = OnCueBox.IsChecked == true || after != "";
        CenterX = cx; CenterY = cy; Measure = measure;
        Chosen = GolemsBox.SelectedItems.Cast<string>().ToList();
        DialogResult = true;
    }
}
