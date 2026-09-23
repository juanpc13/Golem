using System.Text.Json;

namespace GolemTest.World;

/// <summary>A solid box on the floor, axis-aligned: a wall piece or a crate. Its name is the world's name for it, the one a
/// contact reports (like Gazebo's model name).</summary>
public sealed record Box(string Name, double CenterX, double CenterY, double SizeX, double SizeY)
{
    public double MinX => CenterX - SizeX / 2;
    public double MaxX => CenterX + SizeX / 2;
    public double MinY => CenterY - SizeY / 2;
    public double MaxY => CenterY + SizeY / 2;

    /// <summary>The point of the box closest to (x, y) — the point a disc centred there would touch first.</summary>
    public (double X, double Y) Closest(double x, double y) => (Math.Clamp(x, MinX, MaxX), Math.Clamp(y, MinY, MaxY));
}

/// <summary>Where a body starts: its name and its mark, as the plan declares it.</summary>
public sealed record BodyMark(string Name, double X, double Y, double Yaw);

// REALITY AS THE SIMULATOR BUILDS IT (propuesta 52, fase 1, 23-sep-2026): read from the same `sim/world/plan.json` that
// `sim/world/build_world.py` turns into Gazebo's world, with the same rules — every edge of a place is a wall unless a
// neighbour joined by an opening shares it, doors are gaps cut out of it, walls are boxes of the same thickness. Built
// WITHOUT the domain's map on purpose: the world in memory must be able to disagree with what the golem believes, the way
// Gazebo can. The crates are the kiosk's spots (`sim/bridge/crates.py`), with the same sizes and places.
public sealed class FloorPlan
{
    public const double WallThickness = 0.15;    // build_world.py: WALL_T
    public const double DoorGap = 1.4;           // build_world.py: DOOR_GAP — ~1.25 clear, the walls overlap half a thickness
    public const double BodyRadius = 0.25;       // build_world.py: BODY_R
    private const double Eps = 1e-6;

    /// <summary>The kiosk's crate spots (crates.py SPOTS): where each stands, how wide and deep.</summary>
    public static readonly IReadOnlyDictionary<string, Box> Crates = new Dictionary<string, Box>
    {
        ["west"] = new("crate_west", 0.75, 5.5, 0.7, 0.7),       // the west corridor: closed to a body
        ["center"] = new("crate_center", 5.5, 5.5, 0.7, 0.7),    // the middle of the hall: passable on either side
        ["east"] = new("crate_east", 10.25, 5.5, 0.7, 0.7),      // the east corridor: closed to a body
        ["big"] = new("crate_big", 5.5, 5.5, 2.8, 0.7),          // the hall wall to wall: nothing gets through
    };

    public IReadOnlyList<Box> Walls { get; }
    public IReadOnlyList<BodyMark> Bodies { get; }

    private FloorPlan(IReadOnlyList<Box> walls, IReadOnlyList<BodyMark> bodies)
    {
        Walls = walls;
        Bodies = bodies;
    }

    /// <summary>The plan copied beside the test binary (GolemTest.csproj links sim/world/plan.json).</summary>
    public static FloorPlan Load() => Load(Path.Combine(AppContext.BaseDirectory, "World", "plan.json"));

    public static FloorPlan Load(string path)
    {
        using var doc = JsonDocument.Parse(File.ReadAllText(path));
        var root = doc.RootElement;
        var places = root.GetProperty("places").EnumerateArray()
            .Select(p => (Name: p.GetProperty("name").GetString(), X: p.GetProperty("x").GetDouble(), Y: p.GetProperty("y").GetDouble(),
                          W: p.GetProperty("w").GetDouble(), H: p.GetProperty("h").GetDouble()))
            .ToList();
        var doors = root.GetProperty("doors").EnumerateArray()
            .Select(d => (X: d.GetProperty("x").GetDouble(), Y: d.GetProperty("y").GetDouble())).ToList();
        var opens = root.GetProperty("opens").EnumerateArray()
            .Select(o => (A: o.GetProperty("a").GetString(), B: o.GetProperty("b").GetString())).ToList();
        var bodies = root.GetProperty("bodies").EnumerateArray()
            .Select(b => new BodyMark(b.GetProperty("name").GetString(), b.GetProperty("x").GetDouble(), b.GetProperty("y").GetDouble(),
                                      b.TryGetProperty("yaw", out var yaw) ? yaw.GetDouble() : 0.0))
            .ToList();

        var walls = new List<Box>();
        var seen = new HashSet<string>();
        foreach (var p in places)
        {
            var edges = new Dictionary<string, (double X0, double Y0, double X1, double Y1)>
            {
                ["s"] = (p.X, p.Y, p.X + p.W, p.Y), ["n"] = (p.X, p.Y + p.H, p.X + p.W, p.Y + p.H),
                ["w"] = (p.X, p.Y, p.X, p.Y + p.H), ["e"] = (p.X + p.W, p.Y, p.X + p.W, p.Y + p.H),
            };
            foreach (var (side, edge) in edges)
            {
                if (IsOpen(edge, p.Name, places, opens)) continue;
                var pieces = CutDoors(edge, doors).ToList();
                for (int k = 0; k < pieces.Count; k++)
                {
                    var s = pieces[k];
                    string key = string.Join(",", new[] { s.X0, s.Y0, s.X1, s.Y1 }.Select(v => Math.Round(v, 3)));
                    if (!seen.Add(key)) continue;
                    string name = $"wall_{p.Name}_{side}" + (pieces.Count > 1 ? $"_{k + 1}" : "");
                    walls.Add(IsVertical(s)
                        ? new Box(name, s.X0, (s.Y0 + s.Y1) / 2, WallThickness, Math.Abs(s.Y1 - s.Y0) + WallThickness)
                        : new Box(name, (s.X0 + s.X1) / 2, s.Y0, Math.Abs(s.X1 - s.X0) + WallThickness, WallThickness));
                }
            }
        }
        return new FloorPlan(walls, bodies);
    }

    private static bool IsVertical((double X0, double Y0, double X1, double Y1) e) => Math.Abs(e.X0 - e.X1) < Eps;
    private static bool Overlaps(double a0, double a1, double b0, double b1) => Math.Min(a1, b1) - Math.Max(a0, b0) > Eps;

    // An edge is open when a neighbour joined by an opening shares that very edge (build_world.py: is_open).
    private static bool IsOpen((double X0, double Y0, double X1, double Y1) e, string place,
                               List<(string Name, double X, double Y, double W, double H)> places, List<(string A, string B)> opens)
    {
        bool vertical = IsVertical(e);
        foreach (var o in opens)
        {
            if (o.A != place && o.B != place) continue;
            string other = o.A == place ? o.B : o.A;
            var q = places.FirstOrDefault(pl => pl.Name == other);
            if (q.Name == null) continue;
            if (vertical && (Math.Abs(q.X - e.X0) < Eps || Math.Abs(q.X + q.W - e.X0) < Eps) && Overlaps(e.Y0, e.Y1, q.Y, q.Y + q.H)) return true;
            if (!vertical && (Math.Abs(q.Y - e.Y0) < Eps || Math.Abs(q.Y + q.H - e.Y0) < Eps) && Overlaps(e.X0, e.X1, q.X, q.X + q.W)) return true;
        }
        return false;
    }

    // The pieces of an edge that remain wall once every door on it is cut out (build_world.py: cut_doors).
    private static IEnumerable<(double X0, double Y0, double X1, double Y1)> CutDoors((double X0, double Y0, double X1, double Y1) e,
                                                                                      List<(double X, double Y)> doors)
    {
        bool vertical = IsVertical(e);
        double lo = vertical ? e.Y0 : e.X0, hi = vertical ? e.Y1 : e.X1;
        var gaps = doors
            .Where(d => vertical ? Math.Abs(d.X - e.X0) < Eps && e.Y0 + Eps < d.Y && d.Y < e.Y1 - Eps
                                 : Math.Abs(d.Y - e.Y0) < Eps && e.X0 + Eps < d.X && d.X < e.X1 - Eps)
            .Select(d => vertical ? d.Y : d.X).OrderBy(v => v).ToList();
        double cursor = lo;
        foreach (var g in gaps)
        {
            double a = Math.Max(cursor, g - DoorGap / 2);
            if (a > cursor + Eps) yield return vertical ? (e.X0, cursor, e.X0, a) : (cursor, e.Y0, a, e.Y0);
            cursor = Math.Min(hi, g + DoorGap / 2);
        }
        if (hi > cursor + Eps) yield return vertical ? (e.X0, cursor, e.X0, hi) : (cursor, e.Y0, hi, e.Y0);
    }
}
