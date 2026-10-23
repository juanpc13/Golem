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
    /// <summary>The FORMATION the last of this fleet cues when everybody finished (propuesta 117; its name since propuesta 123), lower case; "" none.</summary>
    public string CueTo { get; private set; } = "";
    /// <summary>Whether the formation starts on a cue: no take is written for it.</summary>
    public bool OnCue { get; private set; }
    /// <summary>THE PULL (propuesta 118): the formation this one starts after — the one asked (propuesta 123); "" none.</summary>
    public string After { get; private set; } = "";
    /// <summary>How often the asker asks, in seconds (propuesta 121): --every on the line; 5 unless changed.</summary>
    public double Every { get; private set; } = 5;
    /// <summary>THE OPEN FORMATION (propuesta 123): of a union, no fleet of its own — every golem of the workspace a candidate; and the union's name.</summary>
    public bool Open { get; private set; }
    public string UnionName { get; private set; } = "";
    private const string None = "— none —";
    /// <summary>The formations the console knows (told, or on the tabs), to pick the one waited for — and, since propuesta 123, the one CUED (the cue
    /// names a formation, never a golem).</summary>
    public void KnowsFormations(IEnumerable<string> names)
    {
        known = names.Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        var kept = AfterBox.SelectedItem as string;
        AfterBox.ItemsSource = new[] { None }.Concat(known).ToList();
        AfterBox.SelectedItem = kept != null && kept != None && AfterBox.Items.Contains(kept) ? kept : None;
        RefreshCue();
    }
    private List<string> known = new();
    private string ownName = "";

    private readonly IReadOnlyList<string> golems;

    public FormationDialog(IReadOnlyList<string> golems, string point)
    {
        this.golems = golems;
        InitializeComponent();
        GolemsBox.ItemsSource = golems;
        GolemsBox.SelectAll();
        RefreshCue();
        if (point.Contains(',')) CenterBox.Text = point.Trim();
    }

    private string Picked => Figures.Children.OfType<RadioButton>().FirstOrDefault(r => r.IsChecked == true)?.Content as string ?? "square";
    private const string Nobody = "— nobody —";

    // the formations a cue may go to (propuesta 123): the ones the console knows, this one left out; the choice is kept while it is still one of them
    private void RefreshCue()
    {
        if (CueBox == null) return;
        var kept = CueBox.SelectedItem as string;
        CueBox.ItemsSource = new[] { Nobody }.Concat(known.Where(n => n != ownName)).ToList();
        CueBox.SelectedItem = kept != null && kept != Nobody && CueBox.Items.Contains(kept) ? kept : Nobody;
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
        ownName = formation.Name;
        if (formation.CueTo != "" && !known.Contains(formation.CueTo)) known.Add(formation.CueTo);
        if (formation.After != "" && !known.Contains(formation.After)) known.Add(formation.After);
        KnowsFormations(known);
        CueBox.SelectedItem = formation.CueTo != "" && formation.CueTo != formation.Name ? formation.CueTo : Nobody;
        if (formation.After != "") AfterBox.SelectedItem = formation.After;
        if (formation.Every > 0) EveryBox.Text = formation.Every.ToString("0.##", CultureInfo.InvariantCulture);
        OpenBox.IsChecked = formation.Open;
        if (formation.UnionName != "") UnionBox.Text = formation.UnionName;
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
        CueTo = cue;
        string after = AfterBox.SelectedItem as string is { } pickedAfter && pickedAfter != None ? pickedAfter.Trim().ToLowerInvariant() : "";
        if (after != "" && cue == after) { Problem.Text = $"{after} is waited for: it cannot be cued by this formation too"; return; }
        if (!double.TryParse(EveryBox.Text.Trim().TrimEnd('s', 'S'), NumberStyles.Float, CultureInfo.InvariantCulture, out double every) || every <= 0) { Problem.Text = "every: seconds above zero, like 5"; return; }
        After = after; Every = every;
        OnCue = OnCueBox.IsChecked == true || after != "";
        CenterX = cx; CenterY = cy; Measure = measure;
        Chosen = GolemsBox.SelectedItems.Cast<string>().ToList();
        // the open formation (propuesta 123): a polygon shared by distance or rps, of a named union; every golem of the workspace is a candidate
        Open = OpenBox.IsChecked == true;
        if (Open)
        {
            if (Policy == Policy.Rank) { Problem.Text = "an open formation shares its places by distance or rps: by rank nobody chooses"; return; }
            if (Figure is "circle" or "propeller") { Problem.Text = $"a {Figure} cannot open: an open formation is a polygon — its vertices are its places"; return; }
            UnionName = UnionBox.Text.Trim().ToLowerInvariant();
            if (!System.Text.RegularExpressions.Regex.IsMatch(UnionName, @"^[A-Za-z_][A-Za-z0-9_-]*$")) { Problem.Text = "the union needs a name like show-1"; return; }
            Chosen = golems.ToList();
        }
        DialogResult = true;
    }
}
