using System.Text.Json;

namespace WardenCli;

/// <summary>What the golem answered to its READS — where, state, route, obstacles — kept as INFORMATION beside the map, never as commands
/// on the script (Juan, 7-oct-2026: "estos reads deberían ir más del lado del mapa… no tanto como botones sino como información"). The console
/// asks every few seconds and shows what came back; it decides nothing.</summary>
public sealed record Readings(string Zone, string Scenario, string Navigation, double? Speed, double? Turn, bool Held, int? Pending, int? Routes,
                              int? RouteId, string RouteStatus, string RouteAction, string RoutePlan, string RouteWhy,
                              int Things, int Met, int Marks, IReadOnlyList<Obstacle> Obstacles, IReadOnlyList<string> Scenarios,
                              IReadOnlyList<FormationView> Formations)
{
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
