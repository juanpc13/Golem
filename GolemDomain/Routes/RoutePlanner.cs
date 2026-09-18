using System.Globalization;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Maps;
using GolemDomain.Touches;

namespace GolemDomain.Routes;

/// <summary>
/// The route planner: the shortest road between two points of a layout, for a body of a given radius —
/// Dijkstra over a graph whose nodes are the doors (plus the start, the goal, the midpoints of the openings
/// and detour points around the marks) and whose edges join nodes that can see each other inside one zone or
/// across an opening, clear of every mark. The cost of an edge is generic (<see cref="EdgeCost"/>); today it is
/// the distance. The body is not a point: a door is crossed straight, an opening away from its corners, a mark
/// is skirted by a body's width, and a zone too narrow for the body around a mark is no road at all.
/// <para>It consults two modules and owns neither: the <see cref="MapLayout"/> says where the walls and the doors
/// stand, the <see cref="Collisions"/> what nobody charted; between the two it finds the shortest road.</para>
/// <para>Origins (noted, not copied — the puppet frames them as operations of a repertoire, see NOTEBOOK
/// *Estado del arte*): the shortest path is Dijkstra (1959). A graph whose nodes are the points a body can see
/// each other from (here: doors, opening midpoints, detour points) is the visibility graph — VGRAPH, Lozano-Pérez
/// &amp; Wesley, CACM 1979. Treating the body as a point and growing everything else by its radius (the clearance
/// around a mark, the inset off a wall) is the configuration-space reduction — Lozano-Pérez, IEEE TC 1983. Our
/// graph is sparse because the map is topological: areas joined by passages, in the spirit of the "distinctive
/// places and travel paths" of Kuipers &amp; Byun's Spatial Semantic Hierarchy (1991).</para>
/// </summary>
internal sealed class RoutePlanner
{
    private readonly MapLayout layout;
    private readonly Collisions collisions;
    private readonly double radius;
    private readonly EdgeCost cost;

    internal RoutePlanner(MapLayout layout, Collisions collisions, double radius) : this(layout, collisions, radius, new DistanceCost()) { }

    internal RoutePlanner(MapLayout layout, Collisions collisions, double radius, EdgeCost cost)
    {
        if (layout == null) throw new GolemDomainException("a planner needs a layout");
        if (collisions == null) throw new GolemDomainException("a planner needs to know what the bodies learned");
        if (cost == null) throw new GolemDomainException("a planner needs to know what an edge costs");
        this.layout = layout;
        this.collisions = collisions;
        if (radius < 0) throw new GolemDomainException("a body's radius cannot be negative");
        this.cost = cost;
        this.radius = radius;
    }

    private IReadOnlyList<Thing> things = Array.Empty<Thing>();            // the things the marks outline, for one road
    private IReadOnlyList<Rectangle> figures = Array.Empty<Rectangle>();   // their figures grown by the body: where its centre may not go

    // A point is clear when the walls leave room AND no figure of a thing holds it.
    private bool Fits(Position at) => layout.HasRoom(at, radius) && !figures.Any(f => f.Contains(at));

    /// <summary>The shortest road from one point to another through the passages: the legs to walk, the stop last.</summary>
    internal Trajectory Road(Position from, Position to)
    {
        if (from == null) throw new GolemDomainException("RoutePlanner.Road: 'from' was not given");
        if (to == null) throw new GolemDomainException("RoutePlanner.Road: 'to' was not given");
        return Road(from, new[] { to });
    }

    /// <summary>The road through several stops, in the order given: the shortest road from each stop to the next,
    /// walked as one — doors crossed straight, openings named where the walk meets them, marks skirted.</summary>
    internal Trajectory Road(Position from, IReadOnlyList<Position> stops)
    {
        if (from == null) throw new GolemDomainException("RoutePlanner.Road: 'from' was not given");
        if (stops == null) throw new GolemDomainException("RoutePlanner.Road: 'stops' was not given");
        var raw = new List<Leg>();
        Position here = from;
        foreach (var stop in stops)
        {
            raw.AddRange(RawRoad(here, stop));
            here = stop;
        }
        return WithOpeningsNamed(from, layout.WithDoorCrossings(from, new Trajectory(raw)));
    }

    /// <summary>How long the shortest road from one point to another is, leg to leg.</summary>
    internal double RoadLength(Position from, Position to)
    {
        if (from == null) throw new GolemDomainException("RoutePlanner.RoadLength: 'from' was not given");
        if (to == null) throw new GolemDomainException("RoutePlanner.RoadLength: 'to' was not given");
        return Road(from, to).Length(from);
    }

    /// <summary>The order of stops that makes the whole road shortest, starting from a point. Every order is tried
    /// up to seven stops; beyond that the nearest stop is taken each time. (An open travelling-salesman tour over
    /// the road matrix: exhaustive while 7! is cheap, the classic nearest-neighbour heuristic beyond — the same
    /// two answers robotics gives to multi-goal coverage; better heuristics, 2-opt say, would slot in here.)</summary>
    internal IReadOnlyList<Position> BestOrder(Position from, IReadOnlyList<Position> stops)
    {
        if (from == null) throw new GolemDomainException("RoutePlanner.BestOrder: 'from' was not given");
        if (stops == null) throw new GolemDomainException("RoutePlanner.BestOrder: 'stops' was not given");
        if (stops.Count < 2) return stops;
        var points = new List<Position> { from };
        points.AddRange(stops);
        var road = new double[points.Count, points.Count];
        for (int i = 0; i < points.Count; i++)
            for (int j = 1; j < points.Count; j++)
                if (i != j) road[i, j] = RoadLength(points[i], points[j]);

        var order = new List<int>();
        if (stops.Count <= 7)
        {
            double best = double.PositiveInfinity;
            foreach (var candidate in Permutations(Enumerable.Range(1, stops.Count).ToList()))
            {
                double length = 0; int at = 0;
                foreach (int next in candidate) { length += road[at, next]; at = next; }
                if (length < best) { best = length; order = candidate; }
            }
        }
        else
        {
            var left = Enumerable.Range(1, stops.Count).ToList();
            int at = 0;
            while (left.Count > 0)
            {
                int nearest = left.OrderBy(j => road[at, j]).First();
                order.Add(nearest);
                left.Remove(nearest);
                at = nearest;
            }
        }
        return order.Select(i => points[i]).ToList();
    }

    private static IEnumerable<List<int>> Permutations(List<int> items)
    {
        if (items.Count <= 1) { yield return new List<int>(items); yield break; }
        foreach (int head in items)
        {
            var rest = items.Where(i => i != head).ToList();
            foreach (var tail in Permutations(rest))
            {
                var p = new List<int> { head };
                p.AddRange(tail);
                yield return p;
            }
        }
    }

    // ---- Dijkstra from one point to the next: the raw legs (doors, openings and detours met, the stop last) ----

    private List<Leg> RawRoad(Position from, Position to)
    {
        var start = new Node(from, layout.ZonesOf(from), NodeKind.Start);
        var goal = new Node(to, layout.ZonesOf(to), NodeKind.Goal);
        var nodes = new List<Node> { start };
        foreach (var d in layout.PlacedDoors)
            nodes.Add(new Node(d.At, new[] { layout.Of(d.A), layout.Of(d.B) }, d.Door));
        // an opening is a node too (its midpoint), so a road can chain two openings
        foreach (var o in layout.Openings)
            if (layout.Touches(o)) nodes.Add(new Node(layout.MidpointOf(o), new[] { layout.Of(o.AreaA), layout.Of(o.AreaB) }, o));
        // around every THING, the corners of its figure grown by the body — a hair further out, so the run from one
        // corner to the next stays clear — only where the body fits (the figure of a thing, not a ring per mark:
        // the second way around a crate already takes the crate's real width; Juan, 14-sep-2026)
        things = collisions.Things();
        figures = things.Select(t => t.Extent(Collisions.MarkMargin).Inflated(radius)).ToList();
        foreach (var figure in figures)
            foreach (var corner in figure.Inflated(0.08).Corners())
            {
                if (!Fits(corner)) continue;
                nodes.Add(new Node(corner, layout.Zones.Where(z => z.ContainsInset(corner, radius + Collisions.MarkMargin)).ToArray(), NodeKind.Detour));
            }
        nodes.Add(goal);

        var dist = nodes.ToDictionary(n => n, _ => double.PositiveInfinity);
        var prev = new Dictionary<Node, Node>();
        var done = new HashSet<Node>();
        dist[start] = 0;
        while (true)
        {
            Node u = null;
            foreach (var n in nodes)
                if (!done.Contains(n) && !double.IsPositiveInfinity(dist[n]) && (u == null || dist[n] < dist[u])) u = n;
            if (u == null) break;
            if (u == goal) break;
            done.Add(u);
            foreach (var v in nodes)
            {
                if (done.Contains(v) || v == u) continue;
                if (!Sees(u, v)) continue;
                double edge = cost.Between(u.At, v.At);
                if (dist[u] + edge < dist[v]) { dist[v] = dist[u] + edge; prev[v] = u; }
            }
        }
        if (double.IsPositiveInfinity(dist[goal]))
            throw new GolemDomainException(collisions.MarkCount == 0
                ? $"no road from ({Fmt(from.X)}, {Fmt(from.Y)}) to ({Fmt(to.X)}, {Fmt(to.Y)}) through the map"
                : $"no road from ({Fmt(from.X)}, {Fmt(from.Y)}) to ({Fmt(to.X)}, {Fmt(to.Y)}) that fits a body of radius {Fmt(radius)} past {collisions.MarkCount} marks");

        var legs = new List<Leg>();
        for (Node n = goal; n != start; n = prev[n])
        {
            if (n == goal) legs.Insert(0, new Leg(n.At, layout.ZoneAt(to).Name));   // a stop: named by its zone alone
            else if (n.Kind == NodeKind.Detour) legs.Insert(0, new Leg(n.At, Leg.Detour));
            else if (n.Kind == NodeKind.Passage) legs.Insert(0, new Leg(n.At, n.Via.Name));
            else legs.Insert(0, new Leg(n.At, OpeningCrossed(prev[n], n)?.Name ?? "?"));
        }
        if (legs.Count == 0) legs.Add(new Leg(to, layout.ZoneAt(to).Name));         // already there: the stop alone
        return legs;
    }

    // ---- the graph ----

    private enum NodeKind { Start, Passage, Detour, Goal }

    private sealed class Node
    {
        internal Position At { get; }
        internal IReadOnlyList<Zone> Zones { get; }
        internal Passage Via { get; }
        internal NodeKind Kind { get; }
        internal Node(Position at, IReadOnlyList<Zone> zones, NodeKind kind) {
            if (at == null) throw new GolemDomainException("Node.Node: 'at' was not given");
            if (zones == null) throw new GolemDomainException("Node.Node: 'zones' was not given");
            if (kind == null) throw new GolemDomainException("Node.Node: 'kind' was not given"); At = at; Zones = zones; Kind = kind; }
        internal Node(Position at, IReadOnlyList<Zone> zones, Passage via) {
            if (at == null) throw new GolemDomainException("Node.Node: 'at' was not given");
            if (zones == null) throw new GolemDomainException("Node.Node: 'zones' was not given");
            if (via == null) throw new GolemDomainException("Node.Node: 'via' was not given"); At = at; Zones = zones; Via = via; Kind = NodeKind.Passage; }
    }

    // Two nodes see each other when they share a zone (a straight line inside a rectangle), or when they stand
    // in two zones joined by an opening and the straight line between them crosses that opening — and, either
    // way, the run enters no figure of a thing grown by the body. The start may still stand inside such a figure
    // (the body backed off its retreat, but the thing showed itself wider since; or a touch was estimated short): the
    // first run out is judged mark by mark, on each mark's own terms — its reach along the surface, its normal toward
    // the free side — it may not run THROUGH a mark, but it may leave the way it came.
    private bool Sees(Node u, Node v)
    {
        bool related = u.Zones.Intersect(v.Zones).Any() || OpeningCrossed(u, v) != null;
        if (!related) return false;
        var run = new Segment(u.At, v.At);
        for (int i = 0; i < figures.Count; i++)
        {
            if (!figures[i].IsCrossedBy(run)) continue;
            if (u.Kind == NodeKind.Start && figures[i].Contains(u.At))
            {
                foreach (var m in things[i].Vertices())
                {
                    if (!m.Blocks(run.ClosestTo(m.At), radius)) continue;
                    if (u.At.DistanceTo(m.At) < radius && run.DistanceTo(m.At) >= u.At.DistanceTo(m.At) - 1e-9) continue;
                    if (run.DistanceTo(m.At) >= radius - 0.05) continue;
                    return false;
                }
                continue;
            }
            return false;
        }
        return true;
    }

    private Opening OpeningCrossed(Node u, Node v)
    {
        foreach (var zu in u.Zones)
            foreach (var zv in v.Zones)
            {
                if (!layout.HasOpeningBetween(zu, zv)) continue;
                var opening = layout.OpeningBetween(zu, zv);
                if (layout.Touches(opening) && layout.IsCrossed(opening, u.At, v.At)) return opening;
            }
        return null;
    }

    // Insert a named leg where the road crosses an opening, so `Cross` can tell it. The road is walked as the
    // body walks it: from one leg's exit to the next leg's approach.
    private Trajectory WithOpeningsNamed(Position from, Trajectory road)
    {
        var result = new List<Leg>();
        Position here = from;
        foreach (var leg in road.Legs())
        {
            var crossing = OpeningBetween(here, leg.Approach);
            if (crossing != null) result.Add(crossing);
            result.Add(leg);
            here = leg.Exit;
        }
        return new Trajectory(result);
    }

    private Leg OpeningBetween(Position u, Position v)
    {
        var zu = layout.ZonesOf(u);
        var zv = layout.ZonesOf(v);
        if (zu.Intersect(zv).Any()) return null;
        foreach (var a in zu)
            foreach (var b in zv)
            {
                if (!layout.HasOpeningBetween(a, b)) continue;
                var opening = layout.OpeningBetween(a, b);
                if (!layout.Touches(opening) || !layout.IsCrossed(opening, u, v)) continue;
                return new Leg(layout.CrossingPoint(opening, u, v), opening.Name);
            }
        return null;
    }

    private static string Fmt(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
}
