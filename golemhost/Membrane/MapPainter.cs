using System.Text.Json;

namespace GolemHost.Membrane;

// Paints the golem's map on turtlesim's canvas so the kiosk shows the floor plan: a
// throwaway "painter" turtle is spawned, teleported along every stroke with its pen down
// (turtlesim draws the line between the old and the new position), then killed — the
// strokes stay. Open boundaries get no wall; doors leave a gap with an orange threshold;
// whatever is not a place is hatched so it reads as solid. Turtlesim has no walls of its
// own: the plan is a picture, and the golems honour it by planning through the passages.
// The map comes from the golem (g.DescribeMap()), never from this class.
public sealed class MapPainter
{
    private const double DoorGap = 0.8;
    private const double Inset = 0.2;           // outer walls drawn clearly inside the canvas edge
    private const string Painter = "painter";

    private static readonly (byte r, byte g, byte b, byte w) Wall = (232, 236, 250, 4);
    private static readonly (byte r, byte g, byte b, byte w) Threshold = (223, 165, 99, 3);
    private static readonly (byte r, byte g, byte b, byte w) Hatch = (52, 62, 140, 2);

    private readonly Rosbridge ros;

    public MapPainter(Rosbridge ros)
    {
        this.ros = ros;
    }

    public async Task PaintAsync(string mapJson, CancellationToken ct)
    {
        var plan = Plan.From(mapJson);
        var strokes = new List<Stroke>();
        strokes.AddRange(plan.Hatching().Select(s => new Stroke(s, Hatch)));
        strokes.AddRange(plan.Walls().Select(s => new Stroke(s, Wall)));
        strokes.AddRange(plan.Thresholds().Select(s => new Stroke(s, Threshold)));
        if (strokes.Count == 0) return;

        var first = strokes[0].Line;
        await ros.CallServiceAsync("/spawn", new { x = first.X1, y = first.Y1, theta = 0.0, name = Painter }, ct);
        try
        {
            foreach (var s in strokes)
            {
                await Pen(false, s.Ink, ct);
                await ros.CallServiceAsync($"/{Painter}/teleport_absolute", new { x = s.Line.X1, y = s.Line.Y1, theta = 0.0 }, ct);
                await Pen(true, s.Ink, ct);
                await ros.CallServiceAsync($"/{Painter}/teleport_absolute", new { x = s.Line.X2, y = s.Line.Y2, theta = 0.0 }, ct);
            }
        }
        finally
        {
            // turtlesim applies teleports on its next frame: kill the painter too soon and the
            // queued last strokes (the thresholds) are never drawn — let a few frames pass.
            try { await Task.Delay(600, ct); } catch (OperationCanceledException) { }
            await ros.CallServiceAsync("/kill", new { name = Painter }, CancellationToken.None);
        }
    }

    private Task Pen(bool down, (byte r, byte g, byte b, byte w) ink, CancellationToken ct) =>
        ros.CallServiceAsync($"/{Painter}/set_pen", new { r = ink.r, g = ink.g, b = ink.b, width = ink.w, off = (byte)(down ? 0 : 1) }, ct);

    private readonly record struct Line(double X1, double Y1, double X2, double Y2);
    private readonly record struct Stroke(Line Line, (byte r, byte g, byte b, byte w) Ink);

    // The floor plan as geometry: places (rectangles), doors (points on shared walls), open boundaries.
    private sealed class Plan
    {
        private readonly List<(string Name, double X, double Y, double W, double H)> places;
        private readonly List<(double X, double Y)> doors;
        private readonly List<(string A, string B)> opens;

        private Plan(List<(string, double, double, double, double)> places, List<(double, double)> doors, List<(string, string)> opens)
        {
            this.places = places;
            this.doors = doors;
            this.opens = opens;
        }

        internal static Plan From(string json)
        {
            using var doc = JsonDocument.Parse(json);
            var root = doc.RootElement;
            return new Plan(
                root.GetProperty("places").EnumerateArray()
                    .Select(p => (p.GetProperty("name").GetString(), p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble(),
                                  p.GetProperty("w").GetDouble(), p.GetProperty("h").GetDouble())).ToList(),
                root.GetProperty("doors").EnumerateArray()
                    .Select(d => (d.GetProperty("x").GetDouble(), d.GetProperty("y").GetDouble())).ToList(),
                root.GetProperty("opens").EnumerateArray()
                    .Select(o => (o.GetProperty("a").GetString(), o.GetProperty("b").GetString())).ToList());
        }

        private double MaxX => places.Count == 0 ? 0 : places.Max(p => p.X + p.W);
        private double MaxY => places.Count == 0 ? 0 : places.Max(p => p.Y + p.H);
        private bool OnMap(double x, double y) => places.Any(p => x >= p.X && x <= p.X + p.W && y >= p.Y && y <= p.Y + p.H);

        // Every place's four edges (inset when they lie on the canvas edge), minus the boundaries
        // declared open, minus a gap at each door.
        internal IEnumerable<Line> Walls()
        {
            double maxX = MaxX, maxY = MaxY;
            foreach (var p in places)
            {
                double x0 = p.X <= 1e-9 ? Inset : p.X, y0 = p.Y <= 1e-9 ? Inset : p.Y;
                double x1 = p.X + p.W >= maxX - 1e-9 ? maxX - Inset : p.X + p.W;
                double y1 = p.Y + p.H >= maxY - 1e-9 ? maxY - Inset : p.Y + p.H;
                var edges = new[]
                {
                    (new Line(x0, y0, x1, y0), new Line(p.X, p.Y, p.X + p.W, p.Y)),                 // bottom (drawn, nominal)
                    (new Line(x0, y1, x1, y1), new Line(p.X, p.Y + p.H, p.X + p.W, p.Y + p.H)),     // top
                    (new Line(x0, y0, x0, y1), new Line(p.X, p.Y, p.X, p.Y + p.H)),                 // left
                    (new Line(x1, y0, x1, y1), new Line(p.X + p.W, p.Y, p.X + p.W, p.Y + p.H))      // right
                };
                foreach (var (drawn, nominal) in edges)
                {
                    if (IsOpenEdge(nominal, p.Name)) continue;
                    foreach (var piece in CutDoors(drawn, nominal)) yield return piece;
                }
            }
        }

        // The threshold of every door: a short stroke along the gap, in the door colour.
        internal IEnumerable<Line> Thresholds()
        {
            foreach (var d in doors)
            {
                bool vertical = places.Any(p => Math.Abs(p.X - d.X) < 1e-9 || Math.Abs(p.X + p.W - d.X) < 1e-9);
                yield return vertical
                    ? new Line(d.X, d.Y - DoorGap / 2, d.X, d.Y + DoorGap / 2)
                    : new Line(d.X - DoorGap / 2, d.Y, d.X + DoorGap / 2, d.Y);
            }
        }

        // Horizontal stripes over whatever is not a place: the solid blocks read as solid.
        internal IEnumerable<Line> Hatching()
        {
            double maxX = MaxX, maxY = MaxY;
            const double step = 0.5, probe = 0.1, margin = 0.15;
            for (double y = step / 2; y < maxY; y += step)
            {
                double? runStart = null;
                for (double x = 0; x <= maxX + probe / 2; x += probe)
                {
                    bool solid = x < maxX && !OnMap(x, y);
                    if (solid && runStart == null) runStart = x;
                    if (!solid && runStart != null)
                    {
                        if (x - runStart.Value > 0.6) yield return new Line(runStart.Value + margin, y, x - probe - margin, y);
                        runStart = null;
                    }
                }
            }
        }

        // An edge is open when a neighbour joined by an opening shares that very edge.
        private bool IsOpenEdge(Line e, string place)
        {
            foreach (var o in opens)
            {
                if (o.A != place && o.B != place) continue;
                string other = o.A == place ? o.B : o.A;
                var n = places.FirstOrDefault(q => q.Name == other);
                if (n.Name == null) continue;
                bool vertical = Math.Abs(e.X1 - e.X2) < 1e-9;
                if (vertical && (Math.Abs(n.X - e.X1) < 1e-9 || Math.Abs(n.X + n.W - e.X1) < 1e-9)
                    && Overlaps(e.Y1, e.Y2, n.Y, n.Y + n.H)) return true;
                if (!vertical && (Math.Abs(n.Y - e.Y1) < 1e-9 || Math.Abs(n.Y + n.H - e.Y1) < 1e-9)
                    && Overlaps(e.X1, e.X2, n.X, n.X + n.W)) return true;
            }
            return false;
        }

        private static bool Overlaps(double a0, double a1, double b0, double b1) =>
            Math.Min(a1, b1) - Math.Max(a0, b0) > 1e-6;

        // Split a drawn edge around every door standing on its nominal line.
        private IEnumerable<Line> CutDoors(Line drawn, Line nominal)
        {
            bool vertical = Math.Abs(nominal.X1 - nominal.X2) < 1e-9;
            double from = vertical ? drawn.Y1 : drawn.X1, to = vertical ? drawn.Y2 : drawn.X2;
            var gaps = doors
                .Where(d => vertical ? Math.Abs(d.X - nominal.X1) < 1e-9 && d.Y > nominal.Y1 && d.Y < nominal.Y2
                                     : Math.Abs(d.Y - nominal.Y1) < 1e-9 && d.X > nominal.X1 && d.X < nominal.X2)
                .Select(d => vertical ? d.Y : d.X)
                .OrderBy(v => v);
            double cursor = from;
            foreach (var g in gaps)
            {
                double a = Math.Max(cursor, g - DoorGap / 2);
                if (a > cursor) yield return vertical ? new Line(drawn.X1, cursor, drawn.X1, a) : new Line(cursor, drawn.Y1, a, drawn.Y1);
                cursor = Math.Min(to, g + DoorGap / 2);
            }
            if (to > cursor) yield return vertical ? new Line(drawn.X1, cursor, drawn.X1, to) : new Line(cursor, drawn.Y1, to, drawn.Y1);
        }
    }
}
