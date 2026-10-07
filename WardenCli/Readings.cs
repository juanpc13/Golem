using System.Text.Json;

namespace WardenCli;

/// <summary>What the golem answered to its READS — where, state, route, obstacles — kept as INFORMATION beside the map, never as commands
/// on the script (Juan, 7-oct-2026: "estos reads deberían ir más del lado del mapa… no tanto como botones sino como información"). The console
/// asks every few seconds and shows what came back; it decides nothing.</summary>
public sealed record Readings(string Zone, string Scenario, string Navigation, bool Held, int? Pending, int? Routes,
                              int? RouteId, string RouteStatus, string RouteAction, string RoutePlan, string RouteWhy,
                              int Things, int Met, int Marks, IReadOnlyList<Obstacle> Obstacles, IReadOnlyList<string> Scenarios)
{
    /// <summary>Before anything was asked.</summary>
    public static readonly Readings Nothing = new("", "", "", false, null, null, null, "", "", "", "", 0, 0, 0, Array.Empty<Obstacle>(), Array.Empty<string>());

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

/// <summary>An obstacle the golem hypothesizes from what its body touched — a THING (its vertices joined: two make a line, three or more a
/// figure) or a PEER met — as the `obstacles` read prints it; the console draws the selected golem's on the map.</summary>
public sealed record Obstacle(string Kind, string Who, string Shape, string Zone, double X, double Y, IReadOnlyList<Vertex> Vertices)
{
    public bool IsThing => Kind == "thing";

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
