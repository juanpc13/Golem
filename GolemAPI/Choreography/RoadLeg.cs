using System.Globalization;
using System.Text;
using System.Text.Json;

namespace GolemAPI.Choreography;

// One leg of a road as the host carries it: the kind of act it is (door, opening, around, aside, stop), the
// passage's two areas when it is one, the point — and, when the golem hands the plan out to be walked, how the
// leg is walked (line up at the approach, end at the exit; both are the point for anything but a door). The host
// invents nothing here: the legs come from g.Preview / g.Road / g.RoadPast / g.RoadAhead as objects, travel
// through the ops queue in the wire form below, and are written back as the golem's own acts — Route, then a
// Via / Around / Aside / Stop per leg, all in one journal entry — or walked one by one in the host's memory.
public sealed record RoadLeg(string Kind, string A, string B, double X, double Y, double AX, double AY, double EX, double EY)
{
    public RoadLeg(string kind, string a, string b, double x, double y) : this(kind, a, b, x, y, x, y, x, y) { }

    /// <summary>Whether this leg is walked in two runs: line up at the approach, then run through to the exit (a door).</summary>
    public bool IsCrossedStraight => AX != EX || AY != EY;

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
    //   foreach (legs in g.RoadAhead(@id).Legs()) { print legs.Kind 'kind', legs.A 'a', legs.B 'b', legs.At.X 'x', legs.At.Y 'y',
    //                                               legs.Approach.X 'ax', legs.Approach.Y 'ay', legs.Exit.X 'ex', legs.Exit.Y 'ey'; }

    public static List<RoadLeg> FromQuery(string json)
    {
        var legs = new List<RoadLeg>();
        using var doc = JsonDocument.Parse(json);
        if (!doc.RootElement.TryGetProperty("legs", out var array)) return legs;
        foreach (var e in array.EnumerateArray())
        {
            double x = e.GetProperty("x").GetDouble(), y = e.GetProperty("y").GetDouble();
            legs.Add(new RoadLeg(
                e.GetProperty("kind").GetString() ?? "",
                e.TryGetProperty("a", out var a) ? a.GetString() ?? "" : "",
                e.TryGetProperty("b", out var b) ? b.GetString() ?? "" : "",
                x, y,
                e.TryGetProperty("ax", out var ax) ? ax.GetDouble() : x,
                e.TryGetProperty("ay", out var ay) ? ay.GetDouble() : y,
                e.TryGetProperty("ex", out var ex) ? ex.GetDouble() : x,
                e.TryGetProperty("ey", out var ey) ? ey.GetDouble() : y));
        }
        return legs;
    }

    /// <summary>The query that hands a trajectory out leg by leg, with how each is walked.</summary>
    public const string PrintLegs =
        "print legs.Kind 'kind', legs.A 'a', legs.B 'b', legs.At.X 'x', legs.At.Y 'y', legs.Approach.X 'ax', legs.Approach.Y 'ay', legs.Exit.X 'ex', legs.Exit.Y 'ey';";

    // ---- for a human: one line, as g.Plan renders it ----

    public static string Describe(IReadOnlyList<RoadLeg> legs) =>
        string.Join(" > ", legs.Select(l => (l.Kind switch { "door" => $"{l.A}/{l.B}", "opening" => $"{l.A}~{l.B}", _ => l.Kind })
                                         + $"@{l.X.ToString("0.##", CultureInfo.InvariantCulture)},{l.Y.ToString("0.##", CultureInfo.InvariantCulture)}"));

    // ---- the acts the golem writes, one per leg, inside braces, step by step; values as @params (la{n}, lb{n}, lx{n}, ly{n}) ----

    /// <summary>The lines that decide a road: the road opened (`route = g.Route(@id)`), then each leg found or built from
    /// its @params, named after what it is, and handed to the road's own act. Legs count from 1. Meant to sit inside a braced block — alone (Script) or after
    /// the errand's own acts, in one entry.</summary>
    public static string Acts(IReadOnlyList<RoadLeg> legs)
    {
        var acts = new StringBuilder("    route = g.Route(@id);\n");
        for (int i = 0; i < legs.Count; i++)
        {
            int n = i + 1;
            acts.Append(legs[i].Kind switch
            {
                "door" => $"    door{n} = map.FindDoor(@la{n}, @lb{n});\n    at{n} = Position(@lx{n}, @ly{n});\n    route.Via(door{n}, at{n});\n",
                "opening" => $"    opening{n} = map.FindOpening(@la{n}, @lb{n});\n    at{n} = Position(@lx{n}, @ly{n});\n    route.Via(opening{n}, at{n});\n",
                "around" => $"    around{n} = Position(@lx{n}, @ly{n});\n    route.Around(around{n});\n",
                "aside" => $"    aside{n} = Position(@lx{n}, @ly{n});\n    route.Aside(aside{n});\n",
                _ => $"    stop{n} = Position(@lx{n}, @ly{n});\n    route.Stop(stop{n});\n",
            });
        }
        return acts.ToString();
    }

    /// <summary>The road alone, as one braced command.</summary>
    public static string Script(IReadOnlyList<RoadLeg> legs) => "{\n" + Acts(legs) + "}\n";

    /// <summary>Binds the legs' values to their @params (la, lb, lx, ly, counted from 1) — the same names Acts writes.</summary>
    public static void Bind(dynamic p, IReadOnlyList<RoadLeg> legs)
    {
        for (int i = 0; i < legs.Count; i++)
        {
            int n = i + 1;
            p[$"lx{n}", typeof(double)] = legs[i].X;
            p[$"ly{n}", typeof(double)] = legs[i].Y;
            if (legs[i].Kind == "door" || legs[i].Kind == "opening")
            {
                p[$"la{n}", typeof(string)] = legs[i].A;
                p[$"lb{n}", typeof(string)] = legs[i].B;
            }
        }
    }

    private static string R(double d) => d.ToString("R", CultureInfo.InvariantCulture);
}
