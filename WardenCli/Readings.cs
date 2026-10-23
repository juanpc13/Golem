using System.Text.Json;

namespace WardenCli;

/// <summary>What the golem answered to its READS — where, state, route, obstacles — kept as INFORMATION beside the map, never as commands
/// on the script (Juan, 7-oct-2026: "estos reads deberían ir más del lado del mapa… no tanto como botones sino como información"). The console
/// asks every few seconds and shows what came back; it decides nothing.</summary>
public sealed record Readings(string Zone, string Scenario, string Navigation, double? Speed, double? Turn, bool Held, int? Pending, int? Routes,
                              int? RouteId, string RouteStatus, string RouteAction, string RoutePlan, string RouteWhy,
                              int Things, int Met, int Marks, IReadOnlyList<Obstacle> Obstacles, IReadOnlyList<string> Scenarios,
                              IReadOnlyList<FormationView> Formations, PlayView? Play = null, IReadOnlyList<DisputeView>? Disputes = null,
                              IReadOnlyList<PlayView>? Plays = null, ExploredView? Explored = null, IReadOnlyList<ScenarioView>? Maps = null,
                              IReadOnlyList<UnionView>? Unions = null)
{
    /// <summary>THE UNIONS the golem was told (22-oct-2026; Juan: "un apartado… para ver las formaciones que tiene encima esa unionize"): its `unions` read; none before it answered.</summary>
    public IReadOnlyList<UnionView> UnionList => Unions ?? Array.Empty<UnionView>();
    /// <summary>THE GOLEM'S MAPS (propuesta 120): every scenario it knows, with what the `scenarios` read says of each; none before it answered.</summary>
    public IReadOnlyList<ScenarioView> MapList => Maps ?? Array.Empty<ScenarioView>();
    /// <summary>The disputes by distance (14-oct-2026), none before the golem answered.</summary>
    public IReadOnlyList<DisputeView> DisputeList => Disputes ?? Array.Empty<DisputeView>();
    /// <summary>Every play the golem was told (16-oct-2026), the ended ones among them; none before the golem answered.</summary>
    public IReadOnlyList<PlayView> PlayList => Plays ?? Array.Empty<PlayView>();

    /// <summary>Before anything was asked.</summary>
    public static readonly Readings Nothing = new("", "", "", null, null, false, null, null, null, "", "", "", "", 0, 0, 0, Array.Empty<Obstacle>(), Array.Empty<string>(), Array.Empty<FormationView>());

    /// <summary>The names the `scenarios` read lists under `known`.</summary>
    public static IReadOnlyList<string> ParseScenarios(JsonElement json)
    {
        var names = new List<string>();
        if (JsonWalk.Find(json, "known") is { ValueKind: JsonValueKind.Array } known)
            foreach (var k in known.EnumerateArray())
                if (k.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String) names.Add(n.GetString() ?? "");
        return names;
    }
}

/// <summary>One choice of the golem's environment, offered in a drop-down — a scenario it knows (Juan, 7-oct-2026: "por el mismo drop down que
/// él nos proporciona la lista de mapas que tiene"), a way of taking the doors: its name, and whether it is the one in force.</summary>
public sealed record Choice(string Name, bool Current)
{
    public string Label => Current ? $"{Name} · current" : Name;
    public override string ToString() => Label;
}

/// <summary>THE PLAY the golem is in (propuesta 108, fase 2): its title, this golem's part, the state, pacman's heading, the runs — the
/// `play` read, shown in the play tab. Pacman is autonomous (21-oct-2026): no keyboard, nothing of how it is driven.</summary>
public sealed record PlayView(string Name, string Part, string State, string Heading, int Runs, string Pacman, string Ghosts,
                              bool Released = false, (double X, double Y)? Seen = null, int RunsSeen = 0, int Chases = 0, bool Caught = false, string Catcher = "",
                              string Kind = "pacman")
{
    /// <summary>The state as the list's chip says it: staged, running, or over with who ended it.</summary>
    public string StateText => IsOver ? (Caught ? $"over — {Catcher} caught {Pacman}" : $"over — ended by the {Catcher}") : State.ToLowerInvariant();
    /// <summary>A row of the `plays` list (16-oct-2026): the play's name, kind, state, cast and who ended it.</summary>
    public string ListText => $"{Name} · {Kind} · {State.ToLowerInvariant()}{(IsOver ? (Caught ? $" — {Catcher} caught {Pacman}" : $" — ended by the {Catcher}") : "")} · pacman {Pacman}, ghosts {Ghosts}";

    /// <summary>The `plays` read parsed: every play the golem was told, in the order told.</summary>
    public static IReadOnlyList<PlayView> ParseList(JsonElement json)
    {
        var list = new List<PlayView>();
        if (JsonWalk.Find(json, "told") is not { ValueKind: JsonValueKind.Array } told) return list;
        foreach (var p in told.EnumerateArray())
            list.Add(new PlayView(JsonWalk.String(p, "called") ?? "", "", JsonWalk.String(p, "state") ?? "", "", 0,
                                  JsonWalk.String(p, "pacman") ?? "", JsonWalk.String(p, "ghosts") ?? "",
                                  Caught: JsonWalk.Find(p, "caught") is { ValueKind: JsonValueKind.True }, Catcher: JsonWalk.String(p, "catcher") ?? "",
                                  Kind: JsonWalk.String(p, "kind") ?? "pacman"));
        return list;
    }

    public bool IsPacman => Part.Equals("pacman", StringComparison.OrdinalIgnoreCase);
    public bool IsOver => State.Equals("over", StringComparison.OrdinalIgnoreCase);
    /// <summary>Pacman's line: its heading and runs; a ghost's (fase 3): whether it left the den, where pacman was last seen and on which run,
    /// its chases; over (fase 4): who caught pacman, or the warden.</summary>
    public string Text => IsOver
        ? $"{Name} · {Part.ToLowerInvariant()} · over — {(Caught ? $"{Catcher} caught {Pacman}" : $"ended by the {Catcher}")}"
        : IsPacman
        ? $"{Name} · pacman · {State.ToLowerInvariant()} · heading {Heading.ToLowerInvariant()} · {Runs} run(s)"
        : $"{Name} · ghost · {State.ToLowerInvariant()} · {(Released ? "out of the den" : "in the den")}{(Seen is { } s ? $" · pacman seen at ({N(s.X)}, {N(s.Y)}) on run {RunsSeen}" : " · pacman not seen yet")} · {Chases} chase(s)";
    public string CastText => $"pacman {Pacman} · ghosts {Ghosts}";
    private static string N(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);

    public static PlayView? Parse(JsonElement json)
    {
        if (JsonWalk.Find(json, "playing") is not { ValueKind: JsonValueKind.True }) return null;
        bool seen = JsonWalk.Find(json, "seen") is { ValueKind: JsonValueKind.True };
        return new PlayView(JsonWalk.String(json, "play") ?? "", JsonWalk.String(json, "part") ?? "", JsonWalk.String(json, "state") ?? "",
                            JsonWalk.String(json, "heading") ?? "", (int)(JsonWalk.Number(json, "runs") ?? 0),
                            JsonWalk.String(json, "pacman") ?? "", JsonWalk.String(json, "ghosts") ?? "",
                            JsonWalk.Find(json, "released") is { ValueKind: JsonValueKind.True },
                            seen ? (JsonWalk.Number(json, "seenX") ?? 0, JsonWalk.Number(json, "seenY") ?? 0) : null,
                            (int)(JsonWalk.Number(json, "runsSeen") ?? 0), (int)(JsonWalk.Number(json, "chases") ?? 0),
                            JsonWalk.Find(json, "caught") is { ValueKind: JsonValueKind.True }, JsonWalk.String(json, "catcher") ?? "");
    }
}

/// <summary>A PACE the environment offers (propuesta 111, 12-oct-2026; Juan: "en el panel del environment para subirle la velocidad"): a cruise
/// in m/s and a turn rate in rad/s under a name, and whether it is the one the golem has now (`where` says its speed and turn).</summary>
public sealed record PaceChoice(string Name, double Speed, double Turn, bool Current)
{
    private static string N(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    public string Label => $"{Name} · {N(Speed)} m/s, {N(Turn)} rad/s{(Current ? " · current" : "")}";
    /// <summary>The line that tells it.</summary>
    public string Line => $"speed {N(Speed)} --turn {N(Turn)}";
    public override string ToString() => Label;

    public static readonly (string Name, double Speed, double Turn)[] Presets =
    {
        ("slow", 1.0, 1.0), ("normal", 2.0, 1.5), ("fast", 3.0, 2.5), ("very fast", 4.0, 3.0),
    };
}

/// <summary>An obstacle the golem hypothesizes from what its body touched — a THING (its vertices joined: two make a line, three or more a
/// figure) or a PEER met — as the `obstacles` read prints it; the console draws the selected golem's on the map.</summary>
public sealed record Obstacle(string Kind, string Who, string Shape, string Zone, double X, double Y, IReadOnlyList<Vertex> Vertices)
{
    public bool IsThing => Kind == "thing";

    // ---- what the obstacles' tab shows (8-oct-2026), numbers with a decimal point whatever the machine's culture ----
    private static string N(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
    public string Title => IsThing ? $"thing · {Shape}" : $"peer met · {Who}";
    public string Where => $"at {N(X)},{N(Y)}{(Zone == "" ? "" : $" in {Zone}")}";
    public string Marks => IsThing ? $"{Vertices.Count} mark(s): {string.Join("  ", Vertices.Select(v => $"{N(v.X)},{N(v.Y)}"))}" : "a body met by touching, in the way while the route lasts";
    /// <summary>The point a <c>forget</c> is said at: the obstacle's centre.</summary>
    public string Point => $"{N(X)},{N(Y)}";

    public static IReadOnlyList<Obstacle> Parse(JsonElement json)
    {
        var list = new List<Obstacle>();
        if (JsonWalk.Find(json, "obstacles") is not { ValueKind: JsonValueKind.Array } items) return list;
        foreach (var o in items.EnumerateArray())
        {
            var vertices = new List<Vertex>();
            if (o.TryGetProperty("vertices", out var vs) && vs.ValueKind == JsonValueKind.Array)
                foreach (var v in vs.EnumerateArray()) vertices.Add(new Vertex(Num(v, "x"), Num(v, "y"), Num(v, "normal"), Num(v, "reach")));
            list.Add(new Obstacle(Str(o, "kind"), Str(o, "who"), Str(o, "shape"), Str(o, "zone"), Num(o, "cx"), Num(o, "cy"), vertices));
        }
        return list;
    }

    private static double Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";
}

/// <summary>A mark of a thing: where the body touched, the normal it pressed along, how far the mark reaches.</summary>
public sealed record Vertex(double X, double Y, double Normal, double Reach);

/// <summary>WHAT THE GOLEM EXPLORED (propuesta 120): the cells its exploration module deduced — free where its body went, wall where it touched —
/// and the frontier of the unknown, read from `explored`; the console draws them over the map and decides nothing.</summary>
public sealed record ExploredView(string Scenario, double Grain, int Strides, double Metres, int Covered, int Walls, int FrontierCount, bool Complete,
                                  IReadOnlyList<ExploredCell> Cells, IReadOnlyList<(double X, double Y)> Frontier, bool Exploring = false, bool Finished = false, int Probes = 0)
{
    public static ExploredView Parse(JsonElement e)
    {
        var cells = new List<ExploredCell>();
        if (e.TryGetProperty("cells", out var cs) && cs.ValueKind == JsonValueKind.Array)
            foreach (var c in cs.EnumerateArray())
                cells.Add(new ExploredCell(Int(c, "col"), Int(c, "row"), Str(c, "kind"), Num(c, "x"), Num(c, "y")));
        var frontier = new List<(double, double)>();
        if (e.TryGetProperty("unknown", out var fs) && fs.ValueKind == JsonValueKind.Array)
            foreach (var f in fs.EnumerateArray())
                frontier.Add((Num(f, "fx"), Num(f, "fy")));
        return new ExploredView(Str(e, "scenario"), Num(e, "grain"), Int(e, "strides"), Num(e, "metres"), Int(e, "covered"), Int(e, "walls"),
                                Int(e, "frontier"), Bool(e, "complete"), cells, frontier, Bool(e, "exploring"), Bool(e, "finished"), Int(e, "probes"));
    }

    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
    private static double Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0.0;
}

/// <summary>ONE OF THE GOLEM'S MAPS (propuesta 120; Juan: "otra pestaña con los mapas actuales y poder administrarlos"): a scenario the golem knows —
/// a release's, or one the explorer exported — as the `scenarios` read lists it: its zones, its marks, whether the golem is in it, whether it may be dropped.</summary>
public sealed record ScenarioView(string Name, int Zones, int Marks, bool Current, bool Exported, string Frame)
{
    public string Text => $"{Zones} zone(s), {Marks} mark(s)" + (Exported && Frame != "" && Frame != Name ? $" · read in {Frame}'s frame" : "");
    public string Kind => Exported ? "exported" : "release";
    public bool CanDrop => Exported && !Current;

    public static IReadOnlyList<ScenarioView> Parse(JsonElement e)
    {
        var list = new List<ScenarioView>();
        string current = e.TryGetProperty("current", out var c) && c.ValueKind == JsonValueKind.String ? c.GetString() ?? "" : "";
        if (e.TryGetProperty("known", out var known) && known.ValueKind == JsonValueKind.Array)
            foreach (var k in known.EnumerateArray())
            {
                string name = k.TryGetProperty("name", out var n) && n.ValueKind == JsonValueKind.String ? n.GetString() ?? "" : "";
                int zones = k.TryGetProperty("zones", out var z) && z.ValueKind == JsonValueKind.Number ? z.GetInt32() : 0;
                int marks = k.TryGetProperty("marks", out var m) && m.ValueKind == JsonValueKind.Number ? m.GetInt32() : 0;
                bool exported = k.TryGetProperty("exported", out var x) && x.ValueKind == JsonValueKind.True;
                string frame = k.TryGetProperty("frame", out var f) && f.ValueKind == JsonValueKind.String ? f.GetString() ?? "" : "";
                list.Add(new ScenarioView(name, zones, marks, name == current, exported, frame));
            }
        return list;
    }
}

/// <summary>One cell the golem knows: free or wall, its centre in the scenario's frame.</summary>
public sealed record ExploredCell(int Col, int Row, string Kind, double X, double Y)
{
    public bool IsFree => Kind == "free";
}
