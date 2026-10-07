using System.ComponentModel;
using System.Runtime.CompilerServices;

namespace WardenCli;

/// <summary>A golem in operation, as the console knows it: its name, where to reach it (host and port), the SCRIPT being built for it
/// — one command per line, the queue the console sends line by line — and the last things it answered: where it stood when last asked,
/// its status, how many commands went. Nothing here is the golem's truth: that lives in its journal.</summary>
public sealed class Golem : INotifyPropertyChanged
{
    private string name = "", host = "localhost", status = "not connected", script = "";
    private int port = 8081, sent;
    private double? lastX, lastY, lastHeading;
    private bool running, selected = true;

    /// <summary>Checked in the list: 'send to selected', 'to selected' and 'load in selected' act on the checked golems.</summary>
    public bool Selected { get => selected; set => Set(ref selected, value); }

    public string Name { get => name; set => Set(ref name, value.Trim().ToLowerInvariant()); }
    public string Host { get => host; set => Set(ref host, value.Trim()); }
    public int Port { get => port; set => Set(ref port, value); }
    public Uri Url => new($"http://{Host}:{Port}/");

    /// <summary>The commands built for this golem, one per line: its queue.</summary>
    public string Script { get => script; set => Set(ref script, value); }
    public string Status { get => status; set { Set(ref status, value); Raise(nameof(StatusLine)); } }
    /// <summary>Whether its queue is being sent now; the box says so.</summary>
    public bool Running { get => running; set { Set(ref running, value); Raise(nameof(StatusLine)); } }
    public int Sent { get => sent; set => Set(ref sent, value); }
    public double? LastX { get => lastX; set { Set(ref lastX, value); Raise(nameof(LastSeen)); Raise(nameof(WhereLine)); } }
    public double? LastY { get => lastY; set { Set(ref lastY, value); Raise(nameof(LastSeen)); Raise(nameof(WhereLine)); } }
    public double? LastHeading { get => lastHeading; set { Set(ref lastHeading, value); Raise(nameof(LastSeen)); Raise(nameof(WhereLine)); } }

    private Readings knowledge = Readings.Nothing;
    /// <summary>What it answered to the reads last — where, state, route, obstacles: INFORMATION beside the map, drawn on it (Juan, 7-oct-2026).</summary>
    public Readings Knowledge
    {
        get => knowledge;
        set { Set(ref knowledge, value); Raise(nameof(WhereLine)); Raise(nameof(StateLine)); Raise(nameof(RouteLine)); Raise(nameof(ObstaclesLine)); Raise(nameof(ScenarioOptions)); }
    }

    /// <summary>The scenarios it knows, for the drop-down that composes `enter <scenario>`; the one it is in says so.</summary>
    public IReadOnlyList<ScenarioOption> ScenarioOptions => Knowledge.Scenarios.Select(n => new ScenarioOption(n, n == Knowledge.Scenario)).ToList();

    public string WhereLine => LastX == null || LastY == null
        ? "where · not known yet"
        : $"where · ({LastX:0.00}, {LastY:0.00}) facing {LastHeading:0.00} rad, in {Knowledge.Zone} · {Knowledge.Scenario} · {Knowledge.Navigation}{(Knowledge.Held ? " · HELD" : "")}";
    public string StateLine => Knowledge.Pending == null ? "state · not asked yet" : $"state · {Knowledge.Pending} pending of {Knowledge.Routes} route(s)";
    public string RouteLine => Knowledge.RouteId == null
        ? "route · none yet"
        : $"route {Knowledge.RouteId} · {Knowledge.RouteStatus}{(Knowledge.RouteAction == "" ? "" : $", now {Knowledge.RouteAction}")}{(Knowledge.RouteWhy == "" ? "" : $" — {Knowledge.RouteWhy}")}{Environment.NewLine}plan · {Knowledge.RoutePlan}";
    public string ObstaclesLine => Knowledge.Pending == null
        ? "obstacles · not asked yet"
        : Knowledge.Marks == 0 && Knowledge.Met == 0 ? "obstacles · nothing touched yet" : $"obstacles · {Knowledge.Things} thing(s), {Knowledge.Met} peer(s) met, {Knowledge.Marks} mark(s)";

    private FloorPlan? plan;
    /// <summary>What its current scenario disposes, as it told it last (GET /map); the console's map draws it.</summary>
    public FloorPlan? Plan { get => plan; set => Set(ref plan, value); }

    public string LastSeen => LastX == null || LastY == null ? "position not asked yet" : $"({LastX:0.00}, {LastY:0.00}) facing {LastHeading:0.00} rad";
    /// <summary>What the box shows under the name: the status, and "sending…" while its queue goes.</summary>
    public string StatusLine => Running ? $"{Status} · sending…" : Status;
    public string Address => $"{Host}:{Port}";

    public override string ToString() => Name;

    /// <summary>The next command in the queue, taken out of the script; null when the queue is empty.</summary>
    public string? Dequeue()
    {
        var lines = Script.Replace("\r\n", "\n").Split('\n').ToList();
        int i = lines.FindIndex(l => l.Trim() != "" && !l.TrimStart().StartsWith('#'));
        if (i < 0) return null;
        string line = lines[i].Trim();
        lines.RemoveAt(i);
        Script = string.Join(Environment.NewLine, lines).TrimStart('\r', '\n');
        return line;
    }

    /// <summary>A command appended to the queue, on its own line.</summary>
    public void Enqueue(string line)
    {
        string s = Script.TrimEnd('\r', '\n');
        Script = s == "" ? line : s + Environment.NewLine + line;
    }

    public event PropertyChangedEventHandler? PropertyChanged;
    private void Set<T>(ref T field, T value, [CallerMemberName] string? property = null)
    {
        if (Equals(field, value)) return;
        field = value;
        Raise(property);
    }
    private void Raise(string? property) => PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(property));
}
