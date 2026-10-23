using System.Collections.ObjectModel;
using System.ComponentModel;
using System.Globalization;
using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;
using WardenCli.Formations;

namespace WardenCli.Views;

/// <summary>One formation of a union as the operator writes it: its name, figure, centre, size, orientation and how many golems take it. Its name is
/// SUGGESTED from the figure (22-oct-2026; Juan: "que tenga las iniciales de la figura, tipo tri-XXXX, para medio distinguir"): `tri-1`, `sq-2`, `pent-1`,
/// `ring-1`, numbered among the rows and the formations the console knows; a name the operator typed stays.</summary>
public sealed class UnionRow : INotifyPropertyChanged
{
    private string name = "", figure = "triangle", center = "5.5,5.5", size = "3", angle = "0", golems = "3";
    private readonly List<string> steps = new();
    public string Name { get => name; set { if (name == value) return; name = value ?? ""; Changed(nameof(Name)); } }
    public string Figure { get => figure; set { if (figure == value) return; figure = value ?? ""; Changed(nameof(Figure)); } }
    public string Center { get => center; set { center = value ?? ""; Changed(nameof(Center)); } }
    public string Size { get => size; set { size = value ?? ""; Changed(nameof(Size)); } }
    public string Angle { get => angle; set { angle = value ?? ""; Changed(nameof(Angle)); } }
    public string Golems { get => golems; set { golems = value ?? ""; Changed(nameof(Golems)); } }
    /// <summary>THE STEPS IT DANCES once placed, IN ORDER — a list of operations, each `clockwise` or `counterclockwise` (22-oct-2026; Juan: "más bien
    /// debería ser como una lista de operaciones, no sólo una opción del dropdown"): one `rotate` line per entry.</summary>
    public IReadOnlyList<string> Steps => steps;
    public string StepsText => steps.Count == 0 ? "—" : string.Join(" ", steps.Select(Glyph));
    public string StepsTip => steps.Count == 0 ? "no step: placed, it finishes" : string.Join(", ", steps);
    public void AddStep(string sense) { steps.Add(sense); StepsChanged(); }
    public void DropLastStep() { if (steps.Count == 0) return; steps.RemoveAt(steps.Count - 1); StepsChanged(); }
    public UnionRow WithSteps(params string[] senses) { steps.AddRange(senses); return this; }
    private void StepsChanged() { Changed(nameof(Steps)); Changed(nameof(StepsText)); Changed(nameof(StepsTip)); }
    private static string Glyph(string sense) => sense == "counterclockwise" ? "↺" : sense == "rest" ? "—" : "↻";   // a REST is a visible gap (23-oct-2026; Juan: "pausas para esperar… el espacio vacío")

    /// <summary>The prefix a figure's name is suggested with.</summary>
    public static string PrefixOf(string figure) => (figure ?? "").Trim().ToLowerInvariant() switch { "square" => "sq", "pentagon" => "pent", "circle" => "ring", _ => "tri" };
    /// <summary>Whether a name is one the console suggested (so a new figure may suggest it again); a name the operator typed is never touched.</summary>
    public static bool IsSuggested(string name) => Regex.IsMatch(name ?? "", @"^(tri|sq|pent|ring)-\d+$");

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Changed(string property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}

/// <summary>A formation of the union, validated: what the lines carry.</summary>
public sealed record UnionFormation(string Name, string Figure, double CenterX, double CenterY, double Size, double Angle, int Golems, IReadOnlyList<string> Steps);

/// <summary>
/// THE PROGRAMME DIALOG (22-oct-2026; Juan: "otra opción para programar coreografías… que sólo pregunte una lista de formaciones para preparar los
/// comandos en los tabs para que los golems sepan qué van a coordinarse entre ellos… y cuántos van destinados a cada una, y ellos lo terminan
/// haciendo"): the operator names no golem — a list of formations in order, each with how many golems it takes — and the console writes on EVERY
/// tab the union, each formation `--open --places n`, `take <union>` and each formation's `finish`; the golems share themselves by the
/// union's law (propuesta 123). Nothing is sent from here.
/// </summary>
public partial class UnionDialog : Window
{
    public ObservableCollection<UnionRow> Rows { get; } = new();
    public string UnionName { get; private set; } = "";
    public Policy Policy { get; private set; } = Policy.Distance;
    /// <summary>Whether the formations follow each other by asking (the pull) rather than by a cue (the push).</summary>
    public bool Pull { get; private set; }
    public double Every { get; private set; } = 5;
    public IReadOnlyList<UnionFormation> Formations { get; private set; } = Array.Empty<UnionFormation>();

    private readonly HashSet<string> known;

    public UnionDialog(IEnumerable<string> knownFormations)
    {
        InitializeComponent();
        known = knownFormations.ToHashSet(StringComparer.Ordinal);
        int n = 1;
        while (known.Contains($"show-{n}")) n++;
        NameBox.Text = $"show-{n}";
        // two triangles to start from, the way the shows do it: the operator edits, adds or removes rows
        Rows.Add(new UnionRow { Name = "tri-left", Figure = "triangle", Center = "3,5.5", Size = "3", Angle = "0", Golems = "3" }.WithSteps("clockwise", "clockwise", "clockwise"));
        Rows.Add(new UnionRow { Name = "tri-right", Figure = "triangle", Center = "8,5.5", Size = "3", Angle = "0", Golems = "3" }.WithSteps("clockwise", "clockwise", "clockwise"));
        RowsGrid.ItemsSource = Rows;
    }

    // a name for a figure: its prefix and the first number no row and no known formation has
    private string Suggest(string figure, UnionRow? except = null)
    {
        string prefix = UnionRow.PrefixOf(figure);
        int n = 1;
        while (known.Contains($"{prefix}-{n}") || Rows.Any(r => !ReferenceEquals(r, except) && string.Equals(r.Name, $"{prefix}-{n}", StringComparison.OrdinalIgnoreCase))) n++;
        return $"{prefix}-{n}";
    }

    // ONE MORE FORMATION (22-oct-2026; Juan: "un botón para agregar una row, no que se autogenere vacío"): added after the last, a triangle named after it,
    // its row selected to edit
    private void AddRow_Click(object sender, RoutedEventArgs e)
    {
        try { RowsGrid.CommitEdit(DataGridEditingUnit.Row, true); } catch (InvalidOperationException) { }
        var row = new UnionRow { Figure = "triangle" };
        row.Name = Suggest(row.Figure, row);
        Rows.Add(row);
        RowsGrid.SelectedItem = row;
        RowsGrid.ScrollIntoView(row);
        Problem.Text = "";
    }

    // the figure picked: a suggested name follows it (tri-1 → sq-1); a name the operator typed stays
    private void RowsGrid_CellEditEnding(object? sender, DataGridCellEditEndingEventArgs e)
    {
        if (e.EditAction != DataGridEditAction.Commit || e.Row.Item is not UnionRow row) return;
        if (e.Column.Header as string != "figure" || e.EditingElement is not ComboBox box || box.SelectedItem is not string picked) return;
        if (row.Name == "" || UnionRow.IsSuggested(row.Name)) row.Name = Suggest(picked, row);
    }

    // a row taken off (22-oct-2026; Juan: "un botón para eliminar los rows vacíos"): the ✕ of its row, or every empty row at once
    // the steps of a row (22-oct-2026): a glyph appends one in its sense, ⌫ takes the last off — the list the row shows is the order danced
    private void StepClockwise_Click(object sender, RoutedEventArgs e) => StepOf(sender, e)?.AddStep("clockwise");
    private void StepCounterclockwise_Click(object sender, RoutedEventArgs e) => StepOf(sender, e)?.AddStep("counterclockwise");
    private void StepRest_Click(object sender, RoutedEventArgs e) => StepOf(sender, e)?.AddStep("rest");   // a beat the formation stays (23-oct-2026)
    private void StepBack_Click(object sender, RoutedEventArgs e) => StepOf(sender, e)?.DropLastStep();
    private static UnionRow? StepOf(object sender, RoutedEventArgs e)
    {
        e.Handled = true;
        return (sender as FrameworkElement)?.DataContext as UnionRow;
    }

    private void RemoveRow_Click(object sender, RoutedEventArgs e)
    {
        if ((sender as FrameworkElement)?.DataContext is not UnionRow row) return;   // the grid's placeholder for a new row: nothing to remove
        e.Handled = true;
        try { RowsGrid.CancelEdit(DataGridEditingUnit.Row); } catch (InvalidOperationException) { }
        Rows.Remove(row);
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Problem.Text = "";
        RowsGrid.CommitEdit(System.Windows.Controls.DataGridEditingUnit.Row, true);
        string name = NameBox.Text.Trim().ToLowerInvariant();
        if (!Regex.IsMatch(name, @"^[A-Za-z_][A-Za-z0-9_-]*$")) { Problem.Text = "the union needs a name like show-1"; return; }
        var formations = new List<UnionFormation>();
        foreach (var row in Rows)
        {
            string called = (row.Name ?? "").Trim().ToLowerInvariant();
            if (called == "" && string.IsNullOrWhiteSpace(row.Center)) continue;   // the grid's empty row
            if (!Regex.IsMatch(called, @"^[A-Za-z_][A-Za-z0-9_-]*$")) { Problem.Text = $"a formation needs a name like tri-left (row {formations.Count + 1})"; return; }
            if (called == name) { Problem.Text = $"{called}: a formation cannot be named like the union"; return; }
            if (formations.Any(f => f.Name == called)) { Problem.Text = $"{called} twice: each formation once"; return; }
            string figure = (row.Figure ?? "").Trim().ToLowerInvariant();
            if (figure is not ("triangle" or "square" or "pentagon" or "circle")) { Problem.Text = $"{called}: the figure is triangle, square, pentagon or circle"; return; }
            var xy = (row.Center ?? "").Split(',');
            if (xy.Length != 2 || !double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double cx) || !double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double cy))
            { Problem.Text = $"{called}: the centre is x,y"; return; }
            if (!double.TryParse(row.Size, NumberStyles.Float, CultureInfo.InvariantCulture, out double size) || size <= 0) { Problem.Text = $"{called}: the {(figure == "circle" ? "radius" : "side")} is a length above zero"; return; }
            if (!double.TryParse(row.Angle, NumberStyles.Float, CultureInfo.InvariantCulture, out double angle)) { Problem.Text = $"{called}: the angle is degrees"; return; }
            if (!int.TryParse(row.Golems, NumberStyles.Integer, CultureInfo.InvariantCulture, out int golems) || golems < 1) { Problem.Text = $"{called}: how many golems — a whole number above zero"; return; }
            formations.Add(new UnionFormation(called, figure, cx, cy, size, angle, golems, row.Steps.ToList()));
        }
        if (formations.Count == 0) { Problem.Text = "a union names at least one formation"; return; }
        if (!double.TryParse(EveryBox.Text.Trim().TrimEnd('s', 'S'), NumberStyles.Float, CultureInfo.InvariantCulture, out double every) || every <= 0) { Problem.Text = "every: seconds above zero, like 5"; return; }
        UnionName = name;
        Policy = ByGameBox.IsChecked == true ? Policy.RockPaperScissors : Policy.Distance;
        Pull = PullBox.IsChecked == true;
        Every = every;
        Formations = formations;
        DialogResult = true;
    }
}
