using System.Globalization;

namespace GolemHost.Domain;

/// <summary>
/// The golem's map: places (rectangles), doors between them, open boundaries — and the marks
/// where a body touched something the plan does not hold. Answers where a point is and the
/// shortest road between two points through the passages — Dijkstra over a graph whose nodes
/// are the doors (plus the start, the goal, and detour points around the marks) and whose
/// edges join nodes that can see each other inside one place or across an open boundary,
/// clear of every mark. Rooms are rectangles, so a straight line inside a room never crosses
/// a wall. The body is not a point, though: a door is crossed straight (line up in front of
/// it, leave behind it), an open boundary is crossed away from its corners, a mark is skirted
/// by a body's width, and a place too narrow for the body around a mark is no road at all.
/// </summary>
internal sealed class Atlas
{
    // The body's size, as the map accounts for it: how far from a door the body lines up and is
    // clear of the wall, and how far from the corners of an open boundary it crosses.
    internal const double DoorClearance = 0.6;
    internal const double OpeningMargin = 0.5;
    // How far from a boundary line a touched point may fall and still be that wall (its thickness, the pose's error).
    internal const double WallTolerance = 0.3;
    // A mark is a point where a body touched something: whatever stands there is taken to reach at least
    // this far around the point; a body passes it at its own radius plus a margin.
    internal const double MarkReach = 0.25;
    internal const double MarkMargin = 0.1;

    private readonly List<Place> places = new();
    private readonly List<Passage> passages = new();
    private readonly List<Waypoint> marks = new();

    internal Place AddPlace(string name, double x, double y, double width, double height)
    {
        if (places.Any(p => p.Name == name)) throw new DomainException($"place '{name}' already exists");
        var place = new Place(name, x, y, width, height, this);
        places.Add(place);
        return place;
    }

    internal void AddDoor(string a, string b, Waypoint at)
    {
        if (a == b) throw new DomainException($"a door must join two different places, not '{a}' twice");
        if (passages.Any(p => p.IsDoor && p.Joins(a, b) && p.At.DistanceTo(at) < 1e-9)) return; // declared from the other side already
        passages.Add(new Passage(a, b, at));
    }

    internal void AddOpening(string a, string b)
    {
        if (a == b) throw new DomainException($"an opening must join two different places, not '{a}' twice");
        if (passages.Any(p => !p.IsDoor && p.Joins(a, b))) return;
        passages.Add(new Passage(a, b, null));
    }

    /// <summary>A point where a body touched something the plan does not hold. Two touches within a tenth of a
    /// unit are one mark. Returns how many marks the map holds.</summary>
    internal int AddMark(Waypoint at)
    {
        if (!marks.Any(m => m.DistanceTo(at) < 0.1)) marks.Add(at);
        return marks.Count;
    }

    internal int PlaceCount => places.Count;
    internal int PassageCount => passages.Count;
    internal int MarkCount => marks.Count;
    internal bool Knows(string place) => places.Any(p => p.Name == place);

    // ---- the map, read as objects: the places, and what each one holds ----

    internal IReadOnlyList<Place> Places => places;

    /// <summary>The doors of a place as that place sees them: the place across, and where the door stands.</summary>
    internal IReadOnlyList<Doorway> DoorsOf(Place place) =>
        passages.Where(p => p.IsDoor && p.Joins(place.Name)).Select(p => new Doorway(p.OtherSide(place.Name), p.At)).ToList();

    /// <summary>The open boundaries of a place: the place across each one.</summary>
    internal IReadOnlyList<Opening> OpeningsOf(Place place) =>
        passages.Where(p => !p.IsDoor && p.Joins(place.Name)).Select(p => new Opening(p.OtherSide(place.Name))).ToList();

    /// <summary>The marks standing in a place (a mark on a shared wall stands in both).</summary>
    internal IReadOnlyList<Mark> MarksIn(Place place) =>
        marks.Where(place.Contains).Select(m => new Mark(m.X, m.Y, MarkReach)).ToList();

    internal Place PlaceNamed(string name)
    {
        foreach (var p in places) if (p.Name == name) return p;
        throw new DomainException($"unknown place '{name}'");
    }

    internal bool IsOnMap(Waypoint at) => places.Any(p => p.Contains(at));

    /// <summary>Whether a body of this radius stands clear at a point: inside a place, off every wall by its
    /// radius plus a margin, and off every mark by the mark's reach plus its radius plus a margin.</summary>
    internal bool Fits(Waypoint at, double radius)
    {
        if (!HasRoom(at, radius)) return false;
        return !marks.Any(m => m.DistanceTo(at) < MarkReach + radius + MarkMargin);
    }

    /// <summary>Whether the walls alone leave room for a body of this radius at a point — marks not counted:
    /// the question a body asks while feeling its way around one.</summary>
    internal bool HasRoom(Waypoint at, double radius) => places.Any(p => ContainsInset(p, at, radius + MarkMargin));

    /// <summary>Whether a point lies on a wall the map knows: within tolerance of a place's boundary that is not
    /// declared open. A door is a gap in a known wall, so near a door the wall is still what stands there; the
    /// corner of a solid block is known through the perpendicular boundaries that meet at it.</summary>
    internal bool IsWallAt(Waypoint at, double tolerance)
    {
        foreach (var p in places)
            foreach (var edge in Edges(p))
                if (!IsOpenEdge(p, edge) && DistanceToSegment(at, edge.From, edge.To) <= tolerance) return true;
        return false;
    }

    private static IEnumerable<(Waypoint From, Waypoint To)> Edges(Place p)
    {
        double x0 = p.X, y0 = p.Y, x1 = p.X + p.Width, y1 = p.Y + p.Height;
        yield return (new Waypoint(x0, y0), new Waypoint(x1, y0));   // south
        yield return (new Waypoint(x0, y1), new Waypoint(x1, y1));   // north
        yield return (new Waypoint(x0, y0), new Waypoint(x0, y1));   // west
        yield return (new Waypoint(x1, y0), new Waypoint(x1, y1));   // east
    }

    // An edge is open when a neighbour joined by an opening shares that very edge.
    private bool IsOpenEdge(Place p, (Waypoint From, Waypoint To) edge)
    {
        bool vertical = Math.Abs(edge.From.X - edge.To.X) < 1e-9;
        foreach (var o in passages.Where(o => !o.IsDoor && o.Joins(p.Name)))
        {
            var q = PlaceNamed(o.OtherSide(p.Name));
            if (vertical)
            {
                if ((Math.Abs(q.X - edge.From.X) < 1e-6 || Math.Abs(q.X + q.Width - edge.From.X) < 1e-6)
                    && Math.Min(edge.To.Y, q.Y + q.Height) - Math.Max(edge.From.Y, q.Y) > 1e-6) return true;
            }
            else if ((Math.Abs(q.Y - edge.From.Y) < 1e-6 || Math.Abs(q.Y + q.Height - edge.From.Y) < 1e-6)
                     && Math.Min(edge.To.X, q.X + q.Width) - Math.Max(edge.From.X, q.X) > 1e-6) return true;
        }
        return false;
    }

    // A point inside a place with a margin off every wall — except where the boundary is open.
    private bool ContainsInset(Place p, Waypoint at, double inset)
    {
        if (!p.Contains(at)) return false;
        foreach (var edge in Edges(p))
            if (!IsOpenEdge(p, edge) && DistanceToSegment(at, edge.From, edge.To) < inset) return false;
        return true;
    }

    private static double DistanceToSegment(Waypoint p, Waypoint a, Waypoint b)
    {
        double dx = b.X - a.X, dy = b.Y - a.Y;
        double length2 = dx * dx + dy * dy;
        double t = length2 < 1e-12 ? 0 : Math.Clamp(((p.X - a.X) * dx + (p.Y - a.Y) * dy) / length2, 0, 1);
        double ox = a.X + t * dx - p.X, oy = a.Y + t * dy - p.Y;
        return Math.Sqrt(ox * ox + oy * oy);
    }

    /// <summary>The place a point stands in. A point on a shared wall belongs to the first declared.</summary>
    internal Place PlaceAt(Waypoint at)
    {
        foreach (var p in places) if (p.Contains(at)) return p;
        throw new DomainException($"the point ({Fmt(at.X)}, {Fmt(at.Y)}) is nowhere on the map");
    }

    /// <summary>The shortest road from one point to another through the passages, for a body of this radius: the legs to walk, the stop last.</summary>
    internal List<Leg> Road(Waypoint from, Waypoint to, double radius) => Road(from, new[] { to }, radius);

    /// <summary>The road through several stops, in the order given: the shortest road from each stop to the next,
    /// walked as one — doors crossed straight, open boundaries named where the walk meets them, marks skirted.</summary>
    internal List<Leg> Road(Waypoint from, IReadOnlyList<Waypoint> stops, double radius)
    {
        var raw = new List<Leg>();
        Waypoint here = from;
        foreach (var stop in stops)
        {
            raw.AddRange(RawRoad(here, stop, radius));
            here = stop;
        }
        return WithOpeningsNamed(from, WithDoorCrossings(raw));
    }

    /// <summary>The order of stops that makes the whole road shortest, starting from a point. Every order is tried
    /// up to seven stops; beyond that the nearest stop is taken each time.</summary>
    internal IReadOnlyList<Waypoint> BestOrder(Waypoint from, IReadOnlyList<Waypoint> stops, double radius)
    {
        if (stops.Count < 2) return stops;
        var points = new List<Waypoint> { from };
        points.AddRange(stops);
        var road = new double[points.Count, points.Count];
        for (int i = 0; i < points.Count; i++)
            for (int j = 1; j < points.Count; j++)
                if (i != j) road[i, j] = RoadLength(points[i], points[j], radius);

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

    // Dijkstra from one point to the next: the raw legs (doors, openings and detours met, the stop last).
    private List<Leg> RawRoad(Waypoint from, Waypoint to, double radius)
    {
        double clearance = MarkReach + radius + MarkMargin;
        var start = new Node(from, PlacesOf(from), null, NodeKind.Start);
        var goal = new Node(to, PlacesOf(to), null, NodeKind.Goal);
        var nodes = new List<Node> { start };
        foreach (var d in passages.Where(p => p.IsDoor))
            nodes.Add(new Node(d.At, new[] { d.A, d.B }, d, NodeKind.Passage));
        // an open boundary is a node too (its midpoint), so a road can chain two openings
        foreach (var o in passages.Where(p => !p.IsDoor))
        {
            var mid = SharedEdgeMidpoint(PlaceNamed(o.A), PlaceNamed(o.B));
            if (mid != null) nodes.Add(new Node(mid, new[] { o.A, o.B }, o, NodeKind.Passage));
        }
        // around every mark, the points a body of this radius could pass through — only where it fits
        // (a ring a little wider than the clearance, so the run between two neighbouring points stays clear)
        foreach (var m in marks)
            for (int k = 0; k < 8; k++)
            {
                double angle = k * Math.PI / 4;
                var p = new Waypoint(m.X + (clearance + 0.08) * Math.Cos(angle), m.Y + (clearance + 0.08) * Math.Sin(angle));
                if (!Fits(p, radius)) continue;
                nodes.Add(new Node(p, places.Where(q => ContainsInset(q, p, radius + MarkMargin)).Select(q => q.Name).ToArray(), null, NodeKind.Detour));
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
                if (!Sees(u, v, clearance, radius, out double cost)) continue;
                if (dist[u] + cost < dist[v]) { dist[v] = dist[u] + cost; prev[v] = u; }
            }
        }
        if (double.IsPositiveInfinity(dist[goal]))
            throw new DomainException(marks.Count == 0
                ? $"no road from ({Fmt(from.X)}, {Fmt(from.Y)}) to ({Fmt(to.X)}, {Fmt(to.Y)}) through the map"
                : $"no road from ({Fmt(from.X)}, {Fmt(from.Y)}) to ({Fmt(to.X)}, {Fmt(to.Y)}) that fits a body of radius {Fmt(radius)} past {marks.Count} marks");

        var legs = new List<Leg>();
        for (Node n = goal; n != start; n = prev[n])
        {
            if (n == goal) legs.Insert(0, new Leg(n.At, PlaceAt(to).Name));   // a stop: named by its place alone
            else if (n.Kind == NodeKind.Detour) legs.Insert(0, new Leg(n.At, Leg.Detour));
            else if (n.Via != null) legs.Insert(0, new Leg(n.At, n.Via.Name));
            else legs.Insert(0, new Leg(n.At, OpeningCrossed(prev[n], n)?.Name ?? "?"));
        }
        if (legs.Count == 0) legs.Add(new Leg(to, PlaceAt(to).Name));         // already there: the stop alone
        return legs;
    }

    /// <summary>
    /// Every door on a road is crossed straight: its leg gains an approach point in front of the door
    /// (DoorClearance into the place the body comes from) and an exit point behind it (into the place it
    /// goes to). Derived from the map alone, so a road parsed back from the journal gets the same crossings.
    /// </summary>
    internal List<Leg> WithDoorCrossings(List<Leg> legs)
    {
        var result = new List<Leg>();
        for (int i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            var door = passages.FirstOrDefault(p => p.IsDoor && p.Name == leg.Name && p.At.DistanceTo(leg.At) < 1e-6);
            string toSide = door == null || i + 1 >= legs.Count ? null : SideOf(door, legs[i + 1]);
            var step = toSide == null ? null : Normal(PlaceNamed(door.OtherSide(toSide)), PlaceNamed(toSide));
            if (step == null) { result.Add(leg); continue; }
            result.Add(new Leg(leg.At, leg.Name,
                new Waypoint(leg.At.X - step.X * DoorClearance, leg.At.Y - step.Y * DoorClearance),
                new Waypoint(leg.At.X + step.X * DoorClearance, leg.At.Y + step.Y * DoorClearance)));
        }
        return result;
    }

    // The side of a door the road continues on: the door's place that holds the next leg's point. When
    // both hold it (the next point sits on a wall they share, e.g. another door), the next leg's own
    // passage decides; when neither does, the crossing cannot be told.
    private string SideOf(Passage door, Leg next)
    {
        bool inA = PlaceNamed(door.A).Contains(next.At), inB = PlaceNamed(door.B).Contains(next.At);
        if (inA && !inB) return door.A;
        if (inB && !inA) return door.B;
        if (!inA) return null;
        var onward = passages.FirstOrDefault(p => p.Name == next.Name);
        if (onward == null) return null;
        if (onward.Joins(door.A) && !onward.Joins(door.B)) return door.A;
        if (onward.Joins(door.B) && !onward.Joins(door.A)) return door.B;
        return null;
    }

    // A unit step across the wall two touching places share, from one into the other.
    private static Waypoint Normal(Place from, Place to)
    {
        if (Math.Abs(from.X + from.Width - to.X) < 1e-6) return new Waypoint(1, 0);
        if (Math.Abs(to.X + to.Width - from.X) < 1e-6) return new Waypoint(-1, 0);
        if (Math.Abs(from.Y + from.Height - to.Y) < 1e-6) return new Waypoint(0, 1);
        if (Math.Abs(to.Y + to.Height - from.Y) < 1e-6) return new Waypoint(0, -1);
        return null;
    }

    internal double RoadLength(Waypoint from, Waypoint to, double radius)
    {
        double length = 0;
        Waypoint here = from;
        foreach (var leg in Road(from, to, radius)) { length += here.DistanceTo(leg.At); here = leg.At; }
        return length;
    }

    // ---- the graph ----

    private enum NodeKind { Start, Passage, Detour, Goal }

    private sealed class Node
    {
        internal Waypoint At { get; }
        internal string[] Places { get; }
        internal Passage Via { get; }
        internal NodeKind Kind { get; }
        internal Node(Waypoint at, string[] places, Passage via, NodeKind kind) { At = at; Places = places; Via = via; Kind = kind; }
    }

    private string[] PlacesOf(Waypoint at)
    {
        var names = places.Where(p => p.Contains(at)).Select(p => p.Name).ToArray();
        if (names.Length == 0) throw new DomainException($"the point ({Fmt(at.X)}, {Fmt(at.Y)}) is nowhere on the map");
        return names;
    }

    // Two nodes see each other when they share a place (a straight line inside a rectangle),
    // or when they stand in two places joined by an opening and the straight line between
    // them crosses that open boundary — and, either way, the line keeps clear of every mark.
    // The start may stand inside a mark's clearance (the body just backed off it, maybe from
    // several): the first run out is judged by the body's own radius — it may not run THROUGH a
    // mark, but it may brush past one at the distance it already stands from things.
    private bool Sees(Node u, Node v, double clearance, double radius, out double cost)
    {
        cost = u.At.DistanceTo(v.At);
        bool related = u.Places.Intersect(v.Places).Any() || OpeningCrossed(u, v) != null;
        if (!related) return false;
        foreach (var m in marks)
        {
            double along = DistanceToSegment(m, u.At, v.At);
            if (along >= clearance) continue;
            if (u.Kind == NodeKind.Start && along >= radius - 0.05) continue;
            return false;
        }
        return true;
    }

    private Passage OpeningCrossed(Node u, Node v)
    {
        foreach (var pu in u.Places)
            foreach (var pv in v.Places)
            {
                var opening = passages.FirstOrDefault(p => !p.IsDoor && p.Joins(pu, pv));
                if (opening != null && SegmentCrossesSharedEdge(PlaceNamed(pu), PlaceNamed(pv), u.At, v.At))
                    return opening;
            }
        return null;
    }

    // The shared edge of two touching rectangles, and whether the straight run u->v crosses it.
    private static bool SegmentCrossesSharedEdge(Place a, Place b, Waypoint u, Waypoint v)
    {
        // vertical shared edge (a's right = b's left or vice versa)
        foreach (var (left, right) in new[] { (a, b), (b, a) })
        {
            if (Math.Abs(left.X + left.Width - right.X) < 1e-6)
            {
                double x = right.X;
                double y0 = Math.Max(left.Y, right.Y), y1 = Math.Min(left.Y + left.Height, right.Y + right.Height);
                if (y1 <= y0) continue;
                if ((u.X - x) * (v.X - x) > 0) return false;               // both on the same side: no crossing
                if (Math.Abs(v.X - u.X) < 1e-9) return false;
                double t = (x - u.X) / (v.X - u.X);
                double y = u.Y + t * (v.Y - u.Y);
                return y >= y0 - 1e-9 && y <= y1 + 1e-9;
            }
        }
        // horizontal shared edge (a's top = b's bottom or vice versa)
        foreach (var (low, high) in new[] { (a, b), (b, a) })
        {
            if (Math.Abs(low.Y + low.Height - high.Y) < 1e-6)
            {
                double y = high.Y;
                double x0 = Math.Max(low.X, high.X), x1 = Math.Min(low.X + low.Width, high.X + high.Width);
                if (x1 <= x0) continue;
                if ((u.Y - y) * (v.Y - y) > 0) return false;
                if (Math.Abs(v.Y - u.Y) < 1e-9) return false;
                double t = (y - u.Y) / (v.Y - u.Y);
                double x = u.X + t * (v.X - u.X);
                return x >= x0 - 1e-9 && x <= x1 + 1e-9;
            }
        }
        return false;
    }

    // Insert a named leg where the road crosses an open boundary, so `Cross` can tell it. The
    // road is walked as the body walks it: from one leg's exit to the next leg's approach.
    private List<Leg> WithOpeningsNamed(Waypoint from, List<Leg> legs)
    {
        var result = new List<Leg>();
        Waypoint here = from;
        foreach (var leg in legs)
        {
            var crossing = OpeningBetween(here, leg.Approach);
            if (crossing != null) result.Add(crossing);
            result.Add(leg);
            here = leg.Exit;
        }
        return result;
    }

    private Leg OpeningBetween(Waypoint u, Waypoint v)
    {
        var pu = PlacesOf(u);
        var pv = PlacesOf(v);
        if (pu.Intersect(pv).Any()) return null;
        foreach (var a in pu)
            foreach (var b in pv)
            {
                var opening = passages.FirstOrDefault(p => !p.IsDoor && p.Joins(a, b));
                if (opening == null) continue;
                var A = PlaceNamed(a); var B = PlaceNamed(b);
                if (!SegmentCrossesSharedEdge(A, B, u, v)) continue;
                return new Leg(CrossingPoint(A, B, u, v), opening.Name);
            }
        return null;
    }

    // The middle of the edge two touching rectangles share; null when they do not touch.
    private static Waypoint SharedEdgeMidpoint(Place a, Place b)
    {
        foreach (var (left, right) in new[] { (a, b), (b, a) })
            if (Math.Abs(left.X + left.Width - right.X) < 1e-6)
            {
                double y0 = Math.Max(left.Y, right.Y), y1 = Math.Min(left.Y + left.Height, right.Y + right.Height);
                if (y1 > y0) return new Waypoint(right.X, (y0 + y1) / 2);
            }
        foreach (var (low, high) in new[] { (a, b), (b, a) })
            if (Math.Abs(low.Y + low.Height - high.Y) < 1e-6)
            {
                double x0 = Math.Max(low.X, high.X), x1 = Math.Min(low.X + low.Width, high.X + high.Width);
                if (x1 > x0) return new Waypoint((x0 + x1) / 2, high.Y);
            }
        return null;
    }

    // Where the straight run u->v meets the open boundary — kept OpeningMargin away from the
    // boundary's corners, where the walls of the solid blocks stand.
    private static Waypoint CrossingPoint(Place a, Place b, Waypoint u, Waypoint v)
    {
        foreach (var (left, right) in new[] { (a, b), (b, a) })
            if (Math.Abs(left.X + left.Width - right.X) < 1e-6)
            {
                double x = right.X, t = (x - u.X) / (v.X - u.X);
                double y0 = Math.Max(left.Y, right.Y), y1 = Math.Min(left.Y + left.Height, right.Y + right.Height);
                return new Waypoint(x, AwayFromCorners(u.Y + t * (v.Y - u.Y), y0, y1));
            }
        foreach (var (low, high) in new[] { (a, b), (b, a) })
            if (Math.Abs(low.Y + low.Height - high.Y) < 1e-6)
            {
                double y = high.Y, t = (y - u.Y) / (v.Y - u.Y);
                double x0 = Math.Max(low.X, high.X), x1 = Math.Min(low.X + low.Width, high.X + high.Width);
                return new Waypoint(AwayFromCorners(u.X + t * (v.X - u.X), x0, x1), y);
            }
        return v;
    }

    private static double AwayFromCorners(double along, double from, double to)
    {
        if (to - from <= 2 * OpeningMargin) return (from + to) / 2;   // a narrow opening: straight through the middle
        return Math.Clamp(along, from + OpeningMargin, to - OpeningMargin);
    }

    private static string Fmt(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>A door as one place sees it: the place across, and where the door stands. Read, never written.</summary>
internal sealed class Doorway
{
    internal string To { get; }
    internal Waypoint At { get; }
    internal Doorway(string to, Waypoint at) { To = to; At = at; }
}

/// <summary>An open boundary as one place sees it: the place across. Read, never written.</summary>
internal sealed class Opening
{
    internal string To { get; }
    internal Opening(string to) => To = to;
}

/// <summary>A mark as the map shows it: where a body touched something the plan does not hold, and how far that thing is taken to reach.</summary>
internal sealed class Mark
{
    internal double X { get; }
    internal double Y { get; }
    internal double Reach { get; }
    internal Mark(double x, double y, double reach) { X = x; Y = y; Reach = reach; }
}

/// <summary>
/// One stretch of a road: where to go next, and how the journal names it — a door (kitchen/north), an
/// opening (north~center), a detour around a mark (around), or a stop (the place alone: garage). A door's
/// leg also says how it is walked: line up at the approach (in front of the door, off the wall) and end at
/// the exit (behind it); for an opening, a detour or a stop both are the point.
/// </summary>
internal sealed class Leg
{
    internal const string Detour = "around";

    internal Waypoint At { get; }
    internal string Name { get; }
    internal Waypoint Approach { get; }
    internal Waypoint Exit { get; }
    /// <summary>A stop to reach, as opposed to a passage to cross or a mark to skirt.</summary>
    internal bool IsStop => Name != Detour && !Name.Contains('/') && !Name.Contains('~');

    internal Leg(Waypoint at, string name) : this(at, name, at, at) { }

    internal Leg(Waypoint at, string name, Waypoint approach, Waypoint exit)
    {
        At = at;
        Name = name;
        Approach = approach;
        Exit = exit;
    }
}
