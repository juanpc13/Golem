using System.Windows;

namespace WardenCli.Views;

/// <summary>The golems to add to the console: one by its host, port and name — the name asked of the golem itself if the operator likes — or
/// the golems of the compose (six since 8-oct-2026), those not here yet. The window adds them and connects; the golems are untouched.</summary>
public partial class AddGolemDialog : Window
{
    private readonly IReadOnlyCollection<string> present;

    /// <summary>What to add: name, host, port of each.</summary>
    public List<(string Name, string Host, int Port)> Chosen { get; } = new();

    public AddGolemDialog(IReadOnlyCollection<string> present, int nextPort)
    {
        InitializeComponent();
        this.present = present;
        PortBox.Text = nextPort.ToString();
        Loaded += (_, _) => { NameBox.Focus(); };
    }

    // the golem says who it is: GET /body at that host and port
    private async void Ask_Click(object sender, RoutedEventArgs e)
    {
        if (!TryPort(out int port)) return;
        Problem.Text = "asking…";
        string? name = await GolemClient.PingAsync(new Golem { Host = HostBox.Text, Port = port });
        if (name == null) { Problem.Text = $"nobody answers at {HostBox.Text.Trim()}:{port}"; return; }
        NameBox.Text = name;
        Problem.Text = present.Contains(name) ? $"{name} is already here" : "";
    }

    private void Add_Click(object sender, RoutedEventArgs e)
    {
        if (!TryPort(out int port)) return;
        string name = NameBox.Text.Trim().ToLowerInvariant();
        if (name == "") { Problem.Text = "a golem needs its name — write it, or ask it"; return; }
        if (present.Contains(name)) { Problem.Text = $"{name} is already here"; return; }
        if (HostBox.Text.Trim() == "") { Problem.Text = "where does it answer? a host"; return; }
        Chosen.Add((name, HostBox.Text.Trim(), port));
        DialogResult = true;
    }

    private void Four_Click(object sender, RoutedEventArgs e)
    {
        var compose = new (string Name, int Port)[] { ("blue", 8081), ("red", 8082), ("green", 8083), ("yellow", 8084), ("orange", 8085), ("cyan", 8086) };   // six since 8-oct-2026
        foreach (var (name, port) in compose.Where(f => !present.Contains(f.Name))) Chosen.Add((name, "localhost", port));
        if (Chosen.Count == 0) { Problem.Text = "the golems of the compose are all here"; return; }
        DialogResult = true;
    }

    private bool TryPort(out int port)
    {
        if (int.TryParse(PortBox.Text.Trim(), out port) && port is > 0 and < 65536) return true;
        Problem.Text = "the port is a number from 1 to 65535";
        return false;
    }
}
