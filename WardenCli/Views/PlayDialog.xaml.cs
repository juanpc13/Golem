using System.Globalization;
using System.Windows;
using System.Windows.Controls;

namespace WardenCli.Views;

/// <summary>THE PLAY FROM THE RIBBON (15-oct-2026; Juan: "¿en la parte superior tenemos botón para crear uno? y poner a los involucrados para
/// enviarles el comando y el rol que tomarán, si serán el pacman o el ghost"): the operator picks who plays pacman and which golems the ghosts,
/// and the console writes the lines on every member's tab — the board (use map maze, pacman in the cradle, the ghosts in the den, slowed) and
/// `play pacman --pacman … --ghosts …` on pacman's tab, which spreads to the cast by tell. Nothing is sent from here.</summary>
public partial class PlayDialog : Window
{
    /// <summary>The cradle and the den's cells, in the maze's own frame (`Catalog.MazeDrawing`): the first ghost under the east door, the second
    /// under the west one, the rest behind — the placement the lab of 15-oct-2026 found clean (no ghost touches another on its way out).</summary>
    public const string Cradle = "14.25,8.25";
    public static readonly IReadOnlyList<string> Den = new[] { "15.75,18.75", "12.75,18.75", "14.25,17.25", "12.75,17.25", "15.75,17.25", "14.25,18.75" };

    public string Pacman { get; private set; } = "";
    public List<string> Ghosts { get; private set; } = new();
    public bool LayTheBoard { get; private set; } = true;
    /// <summary>The ghosts' cruise, or null to leave their pace alone.</summary>
    public double? GhostSpeed { get; private set; }

    private readonly IReadOnlyList<string> golems;

    public PlayDialog(IReadOnlyList<string> golems, string selected)
    {
        this.golems = golems;
        InitializeComponent();
        PacmanBox.ItemsSource = golems;
        PacmanBox.SelectedItem = golems.FirstOrDefault(g => g.Equals(selected, StringComparison.OrdinalIgnoreCase)) ?? golems.FirstOrDefault();
    }

    // the ghosts are everybody but pacman, all checked at first
    private void Pacman_Selected(object sender, SelectionChangedEventArgs e)
    {
        string star = PacmanBox.SelectedItem as string ?? "";
        GhostsBox.ItemsSource = golems.Where(g => !g.Equals(star, StringComparison.OrdinalIgnoreCase)).ToList();
        GhostsBox.SelectAll();
    }

    /// <summary>The lines for one member's tab, in order: the board (when laid), the ghost's pace, and the play on pacman's tab.</summary>
    public IReadOnlyList<string> LinesFor(string golem)
    {
        var lines = new List<string>();
        bool star = golem.Equals(Pacman, StringComparison.OrdinalIgnoreCase);
        int rank = Ghosts.FindIndex(g => g.Equals(golem, StringComparison.OrdinalIgnoreCase));
        if (!star && rank < 0) return lines;
        if (LayTheBoard)
        {
            lines.Add("use map maze");
            lines.Add($"place {(star ? Cradle : Den[Math.Min(rank, Den.Count - 1)])}");
        }
        if (!star && GhostSpeed is { } speed) lines.Add($"speed {speed.ToString("0.##", CultureInfo.InvariantCulture)} --turn 1.5");
        if (star) lines.Add($"play pacman --pacman {Pacman} --ghosts {string.Join(",", Ghosts)}");
        return lines;
    }

    private void Ok_Click(object sender, RoutedEventArgs e)
    {
        Pacman = PacmanBox.SelectedItem as string ?? "";
        Ghosts = GhostsBox.SelectedItems.Cast<string>().OrderBy(n => n, StringComparer.Ordinal).ToList();   // the names sorted: the order they leave the den in
        LayTheBoard = LayBox.IsChecked == true;
        if (Pacman == "") { Problem.Text = "pick who plays pacman"; return; }
        if (Ghosts.Count == 0) { Problem.Text = "pick at least one ghost"; return; }
        if (LayTheBoard && Ghosts.Count > Den.Count) { Problem.Text = $"the den has {Den.Count} cells: at most {Den.Count} ghosts on the board"; return; }
        if (SlowBox.IsChecked == true)
        {
            if (!double.TryParse(SpeedBox.Text, NumberStyles.Float, CultureInfo.InvariantCulture, out double speed) || speed <= 0) { Problem.Text = "the ghosts' speed is a number of m/s"; return; }
            GhostSpeed = speed;
        }
        DialogResult = true;
    }
}
