using System.Windows;
using System.Windows.Controls;

namespace WardenCli.Views;

/// <summary>THE TRAFFIC PLAY FROM THE RIBBON (propuesta 125, 23-oct-2026; Juan: "3 robots como en una carretera y el semáforo les habla por tell
/// para poder pasar uno recto y otro girando a su izquierda o derecha"): the operator picks the LIGHT and the CARS with their turns, and the
/// console writes the lines on every member's tab — the road (use map cross-corridors, a car at the far end of an arm each, the light in a
/// room), `play <name> traffic --light … --cars a:left,… --as light|car` and `start <name>`. Nothing is sent from here.</summary>
public partial class TrafficDialog : Window
{
    /// <summary>The light's spot in the road's own frame: the south-west corner, out of every car's way.</summary>
    public const string LightSpot = "3.0,3.0";
    public static readonly IReadOnlyList<string> ArmNames = new[] { "south", "north", "east", "west" };

    /// <summary>Where the k-th car of an arm's queue STARTS, in the road's frame: on the arm's inbound lane (the right of a car coming to the
    /// junction — the road's geometry, 15 × 15, the junction at 6–9, the lanes 0.75 m off the axis, the stop line 0.75 m from the junction's
    /// edge, a place every metre), a metre and a half behind its place, so it drives up to it.</summary>
    public static string Start(string arm, int slot) => arm switch
    {
        "south" => $"8.25,{N(3.75 - slot)}",
        "north" => $"6.75,{N(11.25 + slot)}",
        "east" => $"{N(11.25 + slot)},8.25",
        _ => $"{N(3.75 - slot)},6.75",
    };
    private static string N(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    /// <summary>A golem of the workspace as the dialog lists it: a car or not, the arm it comes from and its turn.</summary>
    public sealed class CarRow
    {
        public string Name { get; init; } = "";
        public bool IsCar { get; set; }
        public string Arm { get; set; } = "south";
        public string Turn { get; set; } = "straight";
        public IReadOnlyList<string> Arms { get; } = ArmNames;
        public IReadOnlyList<string> Turns { get; } = new[] { "straight", "left", "right" };
    }

    /// <summary>The play's name (`road-<n>`), its identity across the fleet, given by the window.</summary>
    public string Name { get; }
    public string Light { get; private set; } = "";
    /// <summary>The cars sorted by name with the arm each comes from and its turn: on one arm they queue by name.</summary>
    public List<(string Car, string Arm, string Turn)> Cars { get; private set; } = new();
    public bool LayTheRoad { get; private set; } = true;

    private readonly IReadOnlyList<string> golems;
    private readonly List<CarRow> rows = new();

    public TrafficDialog(IReadOnlyList<string> golems, string selected, string name)
    {
        this.golems = golems;
        Name = name;
        InitializeComponent();
        Title = $"Traffic {name} — a light and its cars, written on every member's tab";
        LightBox.ItemsSource = golems;
        LightBox.SelectedItem = golems.FirstOrDefault(g => g.Equals(selected, StringComparison.OrdinalIgnoreCase)) ?? golems.FirstOrDefault();
    }

    // the cars are everybody but the light, all checked at first, the turns dealt out straight, left, right in turn
    private void Light_Selected(object sender, SelectionChangedEventArgs e)
    {
        string light = LightBox.SelectedItem as string ?? "";
        rows.Clear();
        int i = 0;
        foreach (var g in golems.Where(g => !g.Equals(light, StringComparison.OrdinalIgnoreCase)).OrderBy(n => n, StringComparer.Ordinal))
        {
            rows.Add(new CarRow { Name = g, IsCar = true, Arm = i < 2 ? "south" : i < 4 ? "north" : i < 6 ? "east" : "west", Turn = new[] { "straight", "left", "right" }[i % 3] });   // two per arm at first: the queue shows
            i++;
        }
        CarsBox.ItemsSource = null;
        CarsBox.ItemsSource = rows;
    }

    /// <summary>The cars, the arm each comes from and its turn, as the play line says them: <c>blue:south:left,green:north:right,red:south:straight</c>.</summary>
    public string Journeys => string.Join(",", Cars.Select(c => $"{c.Car}:{c.Arm}:{c.Turn}"));

    /// <summary>A car's place in its arm's queue: the cars of that arm sorted by name, as the golem reckons it.</summary>
    public int SlotOf(string car) => Cars.Where(c => c.Arm == Cars.First(x => x.Car == car).Arm).TakeWhile(c => c.Car != car).Count();

    /// <summary>The lines for one member's tab, in order: the road (when laid), the play with this golem's part, and its start.</summary>
    public IReadOnlyList<string> LinesFor(string golem)
    {
        var lines = new List<string>();
        bool light = golem.Equals(Light, StringComparison.OrdinalIgnoreCase);
        int rank = Cars.FindIndex(c => c.Car.Equals(golem, StringComparison.OrdinalIgnoreCase));
        if (!light && rank < 0) return lines;
        if (LayTheRoad)
        {
            lines.Add("use map road");
            lines.Add($"place {(light ? LightSpot : Start(Cars[rank].Arm, SlotOf(golem)))}");
            lines.Add("@sync");   // every body placed before any car sets out (lab 23-oct-2026: a tab that lagged placed its car onto one already moving)
        }
        lines.Add($"play {Name} traffic --light {Light} --cars {Journeys} --as {(light ? "light" : "car")}");
        lines.Add($"start {Name}");
        return lines;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Light = LightBox.SelectedItem as string ?? "";
        Cars = rows.Where(r => r.IsCar).OrderBy(r => r.Name, StringComparer.Ordinal).Select(r => (r.Name, r.Arm, r.Turn)).ToList();
        LayTheRoad = LayBox.IsChecked == true;
        if (Light == "") { Problem.Text = "pick the light"; return; }
        if (Cars.Count == 0) { Problem.Text = "pick at least one car"; return; }
        if (Cars.GroupBy(c => c.Arm).Any(g => g.Count() > 4)) { Problem.Text = "an arm queues four cars at most on the board"; return; }
        DialogResult = true;
    }
}
