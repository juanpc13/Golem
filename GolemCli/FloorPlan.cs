using System.Text.Json;

namespace GolemCli;

/// <summary>What a golem's map disposes, as the golem tells it on <c>GET /map</c> (Juan, 6-oct-2026: "que el mapa del CLI muestre lo que
/// tiene actualmente el golem seleccionado en su mapa"): the scenario's name, its zones as rectangles with their doors and open sides. The
/// console only DRAWS it; nothing of it enters the console's choreography, which stays a precalculated script.</summary>
public sealed record FloorPlan(string Name, IReadOnlyList<Zone> Zones)
{
    /// <summary>How far the floor reaches: the farthest zone's edge, at least the open floor's 11 m.</summary>
    public double Extent => Math.Max(11.0, Zones.Count == 0 ? 0 : Math.Max(Zones.Max(z => z.X + z.W), Zones.Max(z => z.Y + z.H)));

    public static FloorPlan? Parse(JsonElement json)
    {
        string name = JsonWalk.String(json, "map") ?? "";
        var zones = new List<Zone>();
        var places = JsonWalk.Find(json, "places");
        if (places is { ValueKind: JsonValueKind.Array } array)
            foreach (var z in array.EnumerateArray())
            {
                var doors = new List<Door>();
                if (z.TryGetProperty("doors", out var ds) && ds.ValueKind == JsonValueKind.Array)
                    foreach (var d in ds.EnumerateArray()) doors.Add(new Door(d.GetProperty("to").GetString() ?? "", d.GetProperty("x").GetDouble(), d.GetProperty("y").GetDouble(), d.TryGetProperty("w", out var w) && w.ValueKind == JsonValueKind.Number ? w.GetDouble() : 1.4));
                var opens = new List<string>();
                if (z.TryGetProperty("opens", out var os) && os.ValueKind == JsonValueKind.Array)
                    foreach (var o in os.EnumerateArray()) opens.Add(o.GetProperty("to").GetString() ?? "");
                zones.Add(new Zone(z.GetProperty("name").GetString() ?? "", z.GetProperty("x").GetDouble(), z.GetProperty("y").GetDouble(), z.GetProperty("w").GetDouble(), z.GetProperty("h").GetDouble(), doors, opens));
            }
        return name == "" && zones.Count == 0 ? null : new FloorPlan(name, zones);
    }

    /// <summary>The edge two zones share, when they touch along one: (x0, y0, x1, y1); null when they do not.</summary>
    public static (double X0, double Y0, double X1, double Y1)? SharedEdge(Zone a, Zone b)
    {
        const double eps = 1e-6;
        if (Math.Abs(a.X + a.W - b.X) < eps || Math.Abs(b.X + b.W - a.X) < eps)
        {
            double x = Math.Abs(a.X + a.W - b.X) < eps ? b.X : a.X;
            double y0 = Math.Max(a.Y, b.Y), y1 = Math.Min(a.Y + a.H, b.Y + b.H);
            return y1 > y0 + eps ? (x, y0, x, y1) : null;
        }
        if (Math.Abs(a.Y + a.H - b.Y) < eps || Math.Abs(b.Y + b.H - a.Y) < eps)
        {
            double y = Math.Abs(a.Y + a.H - b.Y) < eps ? b.Y : a.Y;
            double x0 = Math.Max(a.X, b.X), x1 = Math.Min(a.X + a.W, b.X + b.W);
            return x1 > x0 + eps ? (x0, y, x1, y) : null;
        }
        return null;
    }
}

public sealed record Zone(string Name, double X, double Y, double W, double H, IReadOnlyList<Door> Doors, IReadOnlyList<string> Opens);
/// <summary>A door: a GAP in the wall two zones share, at its point, as wide as the map says (the domain's `Door.Width`, 1.4 m in the catalog).</summary>
public sealed record Door(string To, double X, double Y, double Width);
