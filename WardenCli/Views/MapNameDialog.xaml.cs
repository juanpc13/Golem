using System.Text.RegularExpressions;
using System.Windows;
using System.Windows.Controls;

namespace WardenCli.Views;

/// <summary>The name of the map the golem exports from its exploration (propuesta 120; Juan: "debe pedirme el nombre que quiero setearle y que se
/// validen no existan otros iguales"): a name the line accepts — a letter first, then letters, digits, hyphens — and one the golem does not know
/// yet, checked against the maps it listed; the golem itself refuses a repeat as well.</summary>
public partial class MapNameDialog : Window
{
    private static readonly Regex Valid = new(@"^[A-Za-z_][A-Za-z0-9_-]*$", RegexOptions.Compiled);
    private readonly HashSet<string> known;

    /// <summary>The name chosen, lower case, once the dialog was accepted.</summary>
    public string Chosen { get; private set; } = "";

    public MapNameDialog(string golem, string scenario, IReadOnlyCollection<string> knownMaps, string suggested)
    {
        InitializeComponent();
        known = new HashSet<string>(knownMaps, StringComparer.OrdinalIgnoreCase);
        Prompt.Text = $"The map {golem} deduced in {scenario} becomes a scenario of its own under this name — use map enters it, forget map drops it.";
        Known.Text = known.Count == 0 ? "" : "maps it knows already: " + string.Join(", ", knownMaps);
        NameBox.Text = suggested;
        Loaded += (_, _) => { NameBox.Focus(); NameBox.SelectAll(); };
        Validate();
    }

    private void Name_Changed(object sender, TextChangedEventArgs e) => Validate();

    // what is wrong with the name, if anything: said under the box, and the export button follows
    private bool Validate()
    {
        string name = NameBox.Text.Trim().ToLowerInvariant();
        string problem =
            name == "" ? "a map needs a name" :
            !Valid.IsMatch(name) ? "a name is letters, digits and hyphens, starting with a letter — like explored-blind-1" :
            known.Contains(name) ? $"{name} is a map the golem knows already: another name" : "";
        Problem.Text = problem;
        ExportButton.IsEnabled = problem == "";
        return problem == "";
    }

    private void Export_Click(object sender, RoutedEventArgs e)
    {
        if (!Validate()) return;
        Chosen = NameBox.Text.Trim().ToLowerInvariant();
        DialogResult = true;
    }
}
