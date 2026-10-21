using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Scenarios;
using GolemDomain.Routes;

namespace GolemDomain.Trails;

/// <summary>THE GOLEM'S EXPLORATION (propuesta 120, 20-oct-2026; Juan: "un módulo similar al de colisiones o dinámicas, una propiedad del
/// golem… que vaya construyendo un mapa en memoria dinámicamente y, cuando termine de explorar, nos devuelva un objeto de mapa para
/// cargarlo en el golem… con la ayuda del chasis: la distancia que recorre y, cuando toca algo, unir puntos para crear el mapa entero…
/// como si un ciego estuviera tocando todo"): born with the golem — <c>g.Exploration</c> — it keeps ONE TRAIL PER SCENARIO (every
/// stride the body walked, as the acts brought the poses) and reads the scenario's marks; from those FACTS it derives a HYPOTHESIS
/// (paper 08: never kept, always recomputed): the cells of its GRAIN the trail crossed are FREE and open to each other where it
/// crossed between them, the cell a touch's normal points into is a WALL, the rest is unknown — <c>Deduced()</c> is that hypothesis
/// as a <c>MapLayout</c> the golem may stage and enter (ajuste 60), the planner unchanged. And THE MODE (fase B; Juan: "uno inicia el
/// modo exploración y él solo se queda ahí un tiempo indefinido hasta lograr explorar todo lo que encuentre"): <c>Start()</c>, and
/// every time the golem stands free the host asks for the next PROBE — <c>route = g.Exploration.Probe(me)</c>: the way through
/// the cells it knows to the nearest one that borders the unknown, and ONE STEP INTO IT — a route the golem lays itself, no planner,
/// that a touch ENDS (the mark learned, the body back where the step began) instead of correcting; nothing unknown left next to the
/// known, the exploration is complete and <c>Finish()</c> ends the mode. The three things it joins, apart on purpose: the facts (the
/// trail, the marks), the hypothesis (the cells, the deduced map, the frontier) and the policy (the nearest frontier, for now).</summary>
internal sealed class Explorations
{
    /// <summary>The cell of the explorer at birth (metres): the maze's cell, and the breadth a body of 0.25 needs to turn in place (propuesta 110).</summary>
    internal const double GrainAtBirth = 1.5;
    /// <summary>How far past the touch the thing touched is looked for (metres): a hair, so a mark on a boundary names the cell beyond it.</summary>
    internal const double IntoTheThing = 0.05;

    private readonly Golem golem;
    private readonly Dictionary<string, Trail> trails = new();
    private int probes;

    internal double Grain { get; } = GrainAtBirth;

    internal Explorations(Golem golem)
    {
        if (golem == null) throw new GolemDomainException("Explorations: 'golem' was not given");
        this.golem = golem;
    }

    // ---- the facts: the trail of the scenario the golem is in ----

    /// <summary>The trail of the scenario the golem is in — born empty the first time it is asked.</summary>
    internal Trail Trail => TrailOf(golem.Current.Name);

    private Trail TrailOf(string scenario)
    {
        if (!trails.TryGetValue(scenario, out var trail)) trails[scenario] = trail = new Trail(scenario);
        return trail;
    }

    /// <summary>The body WALKED to here in that scenario (the golem says it from `Stood`): a stride of the trail.</summary>
    internal void Walked(string scenario, Pose me)
    {
        if (scenario == null) return;   // no scenario yet: nothing to walk in
        if (me == null) throw new GolemDomainException("Explorations.Walked: 'me' was not given");
        TrailOf(scenario).Walked(me);
    }

    /// <summary>The body was CARRIED to here in that scenario (the golem says it when it wakes): the trail goes on from here, no stride.</summary>
    internal void Anchored(string scenario, Pose me)
    {
        if (scenario == null) return;
        if (me == null) throw new GolemDomainException("Explorations.Anchored: 'me' was not given");
        TrailOf(scenario).Anchored(me);
    }

    internal int StrideCount => Trail.Count;
    /// <summary>The metres the body walked in this scenario, stride by stride.</summary>
    internal double Distance => Trail.Length;

    // ---- the hypothesis: the cells, the deduced map, the frontier ----

    private sealed record Knowledge(HashSet<(int Col, int Row)> Free, HashSet<((int Col, int Row) A, (int Col, int Row) B)> Open, HashSet<(int Col, int Row)> Walls);

    private (int Col, int Row) CellOf(Position at) => ((int)Math.Floor(at.X / Grain), (int)Math.Floor(at.Y / Grain));

    private Position CentreOf((int Col, int Row) cell) => new((cell.Col + 0.5) * Grain, (cell.Row + 0.5) * Grain);

    private static bool Adjacent((int Col, int Row) a, (int Col, int Row) b) => Math.Abs(a.Col - b.Col) + Math.Abs(a.Row - b.Row) == 1;

    private static ((int Col, int Row), (int Col, int Row)) Ordered((int Col, int Row) a, (int Col, int Row) b) =>
        a.Col < b.Col || (a.Col == b.Col && a.Row < b.Row) ? (a, b) : (b, a);

    // The derivation, from the facts alone: every stride sampled finer than the grain so no cell it crossed is missed; a jump
    // through a corner (two cells diagonal to each other) goes through the one that shares the column; the cell the body STANDS in
    // is free (it is there); every mark's normal names the cell beyond the touch as a wall, unless the trail crossed that cell (a
    // thing inside a walked cell is no wall of it).
    private Knowledge Derive()
    {
        var free = new HashSet<(int, int)>();
        var open = new HashSet<((int, int), (int, int))>();
        var trail = Trail;
        if (trail.Last != null) free.Add(CellOf(trail.Last));
        foreach (var stride in trail.Strides)
        {
            int samples = Math.Max(1, (int)Math.Ceiling(stride.Length / (Grain / 8)));
            var prev = CellOf(stride.From);
            free.Add(prev);
            for (int i = 1; i <= samples; i++)
            {
                double t = (double)i / samples;
                var at = new Position(stride.From.X + t * (stride.To.X - stride.From.X), stride.From.Y + t * (stride.To.Y - stride.From.Y));
                var cell = CellOf(at);
                if (cell == prev) continue;
                if (Adjacent(prev, cell)) open.Add(Ordered(prev, cell));
                else
                {
                    var via = (cell.Col, prev.Row);
                    free.Add(via);
                    open.Add(Ordered(prev, via));
                    open.Add(Ordered(via, cell));
                }
                free.Add(cell);
                prev = cell;
            }
        }
        var walls = new HashSet<(int, int)>();
        foreach (var mark in golem.Current.Collisions.Marks)
        {
            // the thing touched lies along the mark's normal: the first cell that way that the body did not walk, up to half a grain in (a mark
            // reported a few centimetres short of the boundary — lab 20-oct-2026: 14.95 for a wall at 15.0 — still names the cell beyond it;
            // a thing deeper inside a walked cell is no wall of any cell)
            for (double depth = IntoTheThing; depth <= Grain / 2 + 1e-9; depth += IntoTheThing)
            {
                var into = new Position(mark.At.X + depth * Math.Cos(mark.Heading), mark.At.Y + depth * Math.Sin(mark.Heading));
                var cell = CellOf(into);
                if (free.Contains(cell)) continue;
                walls.Add(cell);
                break;
            }
        }
        return new Knowledge(free, open, walls);
    }

    /// <summary>Every cell the explorer knows — free, then wall — by column then row.</summary>
    internal IReadOnlyList<Cell> Cells()
    {
        var k = Derive();
        return k.Free.OrderBy(c => c.Col).ThenBy(c => c.Row).Select(c => new Cell(c.Col, c.Row, Cell.Free, Grain))
            .Concat(k.Walls.OrderBy(c => c.Col).ThenBy(c => c.Row).Select(c => new Cell(c.Col, c.Row, Cell.Wall, Grain)))
            .ToList();
    }

    internal int Covered => Derive().Free.Count;
    internal int WallCount => Derive().Walls.Count;

    /// <summary>THE DEDUCED MAP: a <c>MapLayout</c> named after the scenario — one zone per free cell, open to its neighbour where the
    /// trail crossed between them; every other boundary stays closed. Staged and entered like any map: <c>explored = g.Exploration.Deduced();
    /// exploredScenario = g.Stage(explored); g.Enter(exploredScenario);</c>.</summary>
    internal MapLayout Deduced() => Deduced($"explored-{golem.Current.Name}");

    /// <summary>The deduced map under the name the warden gives it — the one it exports (propuesta 120): <c>explored = g.Exploration.Deduced(@mapName);</c>.</summary>
    internal MapLayout Deduced(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("Explorations.Deduced: 'name' was not given");
        var k = Derive();
        var layout = new MapLayout(name.Trim().ToLowerInvariant());
        var zones = new Dictionary<(int, int), Zone>();
        foreach (var (col, row) in k.Free.OrderBy(c => c.Col).ThenBy(c => c.Row))
            zones[(col, row)] = layout.Area($"cell-{col}-{row}").At(new Position(col * Grain, row * Grain)).Size(Grain, Grain);
        foreach (var (a, b) in k.Open) zones[a].OpenTo(zones[b]);
        return layout;
    }

    // The scenario's map bounds, in cells: the floor's edge is no frontier, and the drawing covers the whole map — NULL when the map has no zone
    // (a blindfold): then nothing bounds the unknown but the walls touched, and the drawing covers what is known
    private (int MinCol, int MinRow, int MaxCol, int MaxRow)? Bounds()
    {
        var zones = golem.Current.Map.Zones.ToList();
        if (zones.Count == 0) return null;
        double minX = zones.Min(z => z.Rect.X), minY = zones.Min(z => z.Rect.Y);
        double maxX = zones.Max(z => z.Rect.X + z.Rect.Width), maxY = zones.Max(z => z.Rect.Y + z.Rect.Height);
        return ((int)Math.Floor(minX / Grain), (int)Math.Floor(minY / Grain), (int)Math.Ceiling(maxX / Grain) - 1, (int)Math.Ceiling(maxY / Grain) - 1);
    }

    // The extent of what is known — the cells free and wall — for a drawing with no map under it
    private static (int MinCol, int MinRow, int MaxCol, int MaxRow) Extent(Knowledge k)
    {
        var all = k.Free.Concat(k.Walls).ToList();
        if (all.Count == 0) return (0, 0, -1, -1);
        return (all.Min(c => c.Col), all.Min(c => c.Row), all.Max(c => c.Col), all.Max(c => c.Row));
    }

    private static readonly (int, int)[] Sides = { (1, 0), (0, 1), (-1, 0), (0, -1) };   // east, north, west, south: the order a decision tries them

    private static bool Within((int Col, int Row) c, (int MinCol, int MinRow, int MaxCol, int MaxRow)? b) => b is not { } bounds || (c.Col >= bounds.MinCol && c.Col <= bounds.MaxCol && c.Row >= bounds.MinRow && c.Row <= bounds.MaxRow);

    // The unknown next to a cell: its neighbours within the map that are neither free nor wall, east, north, west, south
    private static IEnumerable<(int Col, int Row)> UnknownNextTo((int Col, int Row) cell, Knowledge k, (int MinCol, int MinRow, int MaxCol, int MaxRow)? bounds)
    {
        foreach (var (dc, dr) in Sides)
        {
            var n = (cell.Col + dc, cell.Row + dr);
            if (!Within(n, bounds) || k.Free.Contains(n) || k.Walls.Contains(n)) continue;
            yield return n;
        }
    }

    /// <summary>THE FRONTIER: the centres of the cells next to a free one that are neither free nor wall — where the unknown begins —
    /// within the scenario's map; each once.</summary>
    internal IReadOnlyList<Position> Frontier()
    {
        var k = Derive();
        var bounds = Bounds();
        var found = new HashSet<(int, int)>();
        foreach (var cell in k.Free)
            foreach (var n in UnknownNextTo(cell, k, bounds)) found.Add(n);
        return found.OrderBy(c => c.Item1).ThenBy(c => c.Item2).Select(c => CentreOf(c)).ToList();
    }

    internal int FrontierCount => Frontier().Count;
    /// <summary>Explored whole: something walked and nothing unknown left next to it.</summary>
    internal bool IsComplete => Trail.Count > 0 && FrontierCount == 0;

    /// <summary>The hypothesis DRAWN as the catalog draws a maze (`Catalog.MazeDrawing`): one character per cell of the grain over the
    /// scenario's map, the first line the north edge — `.` free, `#` wall, `?` unknown. For the eye, and for the tests to score it
    /// against the real drawing cell by cell.</summary>
    internal IReadOnlyList<string> Drawing()
    {
        var k = Derive();
        var (minCol, minRow, maxCol, maxRow) = Bounds() ?? Extent(k);
        var lines = new List<string>();
        for (int row = maxRow; row >= minRow; row--)
        {
            var chars = new char[maxCol - minCol + 1];
            for (int col = minCol; col <= maxCol; col++)
                chars[col - minCol] = k.Free.Contains((col, row)) ? '.' : k.Walls.Contains((col, row)) ? '#' : '?';
            lines.Add(new string(chars));
        }
        return lines;
    }

    // ---- the mode and the policy (fase B): the golem probes until nothing unknown is left next to what it knows ----

    /// <summary>Whether the golem is exploring: it probes whenever it stands free, until the exploration is complete or it is stopped.</summary>
    internal bool Exploring { get; private set; }
    /// <summary>Whether the last exploration ended by itself, complete.</summary>
    internal bool Finished { get; private set; }
    /// <summary>How many probes this exploration opened.</summary>
    internal int Probes => probes;
    /// <summary>The policy that decides the next probe: the nearest cell that borders the unknown, for now the only one.</summary>
    internal string Policy => "frontier";

    /// <summary>THE BLINDFOLD (propuesta 120; Juan, 20-oct-2026: "quisiera poder lanzarlo sin mapa… ¿hay forma de desasignarle el mapa?"): a scenario
    /// of the golem's own with NO MAP AT ALL — no zone, no wall, no edge — named `blind-&lt;scenario&gt;` after the one the golem is in, read in that
    /// one's frame, exported (droppable) and never a release's; the golem enters it and explores: every touch is a mark, and the floor ends where
    /// it touches. Refused when a blindfold of that scenario is known already (enter it, or drop it). <c>blind = g.Exploration.Blindfold(); g.Enter(blind);</c></summary>
    internal Scenario Blindfold()
    {
        string name = $"blind-{golem.Current.Name}";
        var known = golem.Scenarios().FirstOrDefault(k => k.Name == name && k.Exported);
        return known ?? golem.Export(new MapLayout(name));   // the blindfold of this map, known already: taken up again, its trail kept
    }

    /// <summary>Whether the golem SEES: the scenario it is in has a map (zones); false under a blindfold. Exploring is done blind, so the host blindfolds
    /// a sighted golem first: <c>if (g.Exploration.Sighted) { blind = g.Exploration.Blindfold(); g.Enter(blind); } g.Exploration.Start();</c></summary>
    internal bool Sighted => golem.Current.Map.ZoneCount > 0;

    /// <summary>THE MODE ON — <c>g.Exploration.Start()</c>: from now on, standing free, the golem probes; already exploring, nothing changes.</summary>
    internal Explorations Start()
    {
        Exploring = true;
        Finished = false;
        probes = 0;
        return this;
    }

    /// <summary>THE MODE OFF by the warden — <c>g.Exploration.Stop()</c>: no probe opens any more; the one underway ends on its own.</summary>
    internal Explorations Stop()
    {
        Exploring = false;
        return this;
    }

    /// <summary>WHAT WAS EXPLORED HERE, CLEARED (propuesta 120; Juan: "un botón para hacer clear de lo actualmente explorado, como un reset"): the trail
    /// of this scenario starts afresh from where the body stands and everything learned by touching here is forgotten; refused while exploring.</summary>
    internal Explorations Clear()
    {
        if (Exploring) throw new GolemDomainException("the golem is exploring: stop first");
        var last = Trail.Last;
        trails[golem.Current.Name] = new Trail(golem.Current.Name);
        if (last != null) trails[golem.Current.Name].Anchored(last);
        golem.Current.Collisions.ForgetAll();
        Finished = false;
        probes = 0;
        return this;
    }

    /// <summary>A scenario dropped by the golem: its trail goes with it.</summary>
    internal void Dropped(string scenario)
    {
        if (scenario != null) trails.Remove(scenario);
    }

    /// <summary>THE MODE OFF BY ITSELF — <c>g.Exploration.Finish()</c>, written by the host when the exploration is complete (`Concluded`).</summary>
    internal Explorations Finish()
    {
        if (!Exploring) throw new GolemDomainException("the golem is not exploring: nothing to finish");
        if (!IsComplete) throw new GolemDomainException($"the exploration is not complete: {FrontierCount} cell(s) at the frontier");
        Exploring = false;
        Finished = true;
        return this;
    }

    /// <summary>Whether a probe opens now: exploring, the golem standing free, and somewhere unknown reachable through what it knows —
    /// <c>Check(g.Exploration.Probing)</c> before the host writes the probe.</summary>
    internal bool Probing => Exploring && !golem.HasPendingMission() && Trail.Last != null && PathToTheUnknown(Trail.Last) != null;

    /// <summary>Whether the exploration is complete while the mode is still on — the host writes <c>Finish()</c> then.</summary>
    internal bool Concluded => Exploring && !golem.HasPendingMission() && Trail.Last != null && PathToTheUnknown(Trail.Last) == null;

    /// <summary>THE PROBE — <c>route = g.Exploration.Probe(me)</c>, the pose the body's own report of where it stands: the way through the cells
    /// the golem knows to the nearest one that borders the unknown, and one step into the unknown cell — a route the golem lays itself,
    /// no planner, that a touch ends (the mark learned, the body back where the step began). Refused while not exploring, with a route
    /// pending, or with nothing unknown left next to what it knows (the exploration is complete).</summary>
    internal Route Probe(Pose me)
    {
        if (me == null) throw new GolemDomainException("Explorations.Probe: 'me' was not given");
        if (!Exploring) throw new GolemDomainException("the golem is not exploring: explore first");
        if (golem.HasPendingMission()) throw new GolemDomainException("a route is pending: the golem probes when it stands free");
        var path = PathToTheUnknown(me) ?? throw new GolemDomainException("nothing unknown is left next to what the golem knows: the exploration is complete");
        var route = golem.Probe(me, path);
        probes++;
        return route;
    }

    // THE POLICY, the nearest frontier: from the cell the body stands in, through the cells it knows and the openings it crossed
    // (breadth first, so the nearest first; east, north, west, south at each), the first cell with something unknown next to it —
    // the path to it, cell centre by cell centre, and the unknown cell's centre last. Null when nothing unknown is reachable.
    private IReadOnlyList<Position> PathToTheUnknown(Pose me)
    {
        var k = Derive();
        var bounds = Bounds();
        var start = CellOf(me);
        k.Free.Add(start);
        var before = new Dictionary<(int, int), (int, int)>();
        var queue = new Queue<(int Col, int Row)>();
        queue.Enqueue(start);
        before[start] = start;
        while (queue.Count > 0)
        {
            var cell = queue.Dequeue();
            var unknown = UnknownNextTo(cell, k, bounds).FirstOrDefault();
            if (unknown != default || UnknownNextTo(cell, k, bounds).Any())
            {
                var path = new List<Position>();
                for (var step = cell; step != start; step = before[step]) path.Insert(0, CentreOf(step));
                path.Add(CentreOf(unknown));
                return path;
            }
            foreach (var (dc, dr) in Sides)
            {
                var n = (cell.Col + dc, cell.Row + dr);
                if (!k.Free.Contains(n) || before.ContainsKey(n) || !k.Open.Contains(Ordered(cell, n))) continue;
                before[n] = cell;
                queue.Enqueue(n);
            }
        }
        return null;
    }
}
