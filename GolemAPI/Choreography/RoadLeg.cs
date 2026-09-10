using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GolemAPI.Choreography;

// One leg of a road as the host carries it between reading the golem's decision and writing it down: the kind
// of act it becomes (door, opening, around, aside, stop), the passage's two areas when it is one, and the point.
// The host invents nothing here: the legs come from g.Road(...) / g.RoadPast(...) as objects, travel through the
// ops queue in this wire form, and are written back as the golem's own acts — Route, then Via / Around / Aside /
// Stop, all in one journal entry (Fase 0, P4: several acts in one command are one entry).
public sealed record RoadLeg(string Kind, string A, string B, double X, double Y)
{
    // ---- the wire (the host's own layout, decided in one place) ----

    public static string Encode(IReadOnlyList<RoadLeg> legs) =>
        string.Join(";", legs.Select(l => $"{l.Kind}|{l.A}|{l.B}|{R(l.X)}|{R(l.Y)}"));

    public static List<RoadLeg> Decode(string text)
    {
        var legs = new List<RoadLeg>();
        if (string.IsNullOrEmpty(text)) return legs;
        foreach (var item in text.Split(';', StringSplitOptions.RemoveEmptyEntries))
        {
            var f = item.Split('|');
            legs.Add(new RoadLeg(f[0], f[1], f[2], double.Parse(f[3], CultureInfo.InvariantCulture), double.Parse(f[4], CultureInfo.InvariantCulture)));
        }
        return legs;
    }

    // ---- from the golem's objects: the JSON a query prints when it walks the legs ----
    //   foreach (legs in g.Road(@id, @x, @y).Legs()) { print legs.Kind 'kind', legs.A 'a', legs.B 'b', legs.At.X 'x', legs.At.Y 'y'; }

    public static List<RoadLeg> FromQuery(string json)
    {
        var legs = new List<RoadLeg>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("legs", out var array)) return legs;
        foreach (var e in array.EnumerateArray())
            legs.Add(new RoadLeg(
                e.GetProperty("kind").GetString() ?? "",
                e.TryGetProperty("a", out var a) ? a.GetString() ?? "" : "",
                e.TryGetProperty("b", out var b) ? b.GetString() ?? "" : "",
                e.GetProperty("x").GetDouble(),
                e.GetProperty("y").GetDouble()));
        return legs;
    }

    // ---- for a human: one line, as g.Plan renders it ----

    public static string Describe(IReadOnlyList<RoadLeg> legs) =>
        string.Join(" > ", legs.Select(l => (l.Kind switch { "door" => $"{l.A}/{l.B}", "opening" => $"{l.A}~{l.B}", _ => l.Kind })
                                         + $"@{l.X.ToString("0.##", CultureInfo.InvariantCulture)},{l.Y.ToString("0.##", CultureInfo.InvariantCulture)}"));

    // ---- the acts the golem writes, one per leg, values as @params (x{i}, y{i}, a{i}, b{i}) ----

    public static string Script(IReadOnlyList<RoadLeg> legs)
    {
        // inside braces, step by step: each object is found or built from its @params, named after what it is,
        // then handed to the act — the names die with the block (Fase 0, P2b)
        var script = new StringBuilder("{\n    g.Route(@id);\n");
        for (int i = 0; i < legs.Count; i++)
        {
            int n = i + 1;   // legs read from 1, as a person counts them
            script.Append(legs[i].Kind switch
            {
                "door" => $"    door{n} = map.FindDoor(@a{n}, @b{n});\n    at{n} = Position(@x{n}, @y{n});\n    g.Via(@id, door{n}, at{n});\n",
                "opening" => $"    opening{n} = map.FindOpening(@a{n}, @b{n});\n    at{n} = Position(@x{n}, @y{n});\n    g.Via(@id, opening{n}, at{n});\n",
                "around" => $"    around{n} = Position(@x{n}, @y{n});\n    g.Around(@id, around{n});\n",
                "aside" => $"    aside{n} = Position(@x{n}, @y{n});\n    g.Aside(@id, aside{n});\n",
                _ => $"    stop{n} = Position(@x{n}, @y{n});\n    g.Stop(@id, stop{n});\n",
            });
        }
        script.Append("}\n");
        return script.ToString();
    }

    private static string R(double d) => d.ToString("R", CultureInfo.InvariantCulture);
}
