using System.Globalization;
using System.Text;

namespace GolemHost.Domain;

/// <summary>
/// The golem's map: places (rectangles), doors between them, and open boundaries. Answers
/// where a point is and the shortest road between two points through the passages —
/// Dijkstra over a graph whose nodes are the doors (plus the start and the goal) and whose
/// edges join nodes that can see each other inside one place or across an open boundary.
/// Rooms are rectangles, so a straight line inside a room never crosses a wall.
/// </summary>
internal sealed class Atlas
{
    private readonly List<Place> places = new();
    private readonly List<Passage> passages = new();

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

    internal int PlaceCount => places.Count;
    internal int PassageCount => passages.Count;
    internal bool Knows(string place) => places.Any(p => p.Name == place);

    internal Place PlaceNamed(string name)
    {
        foreach (var p in places) if (p.Name == name) return p;
        throw new DomainException($"unknown place '{name}'");
    }

    internal bool IsOnMap(Waypoint at) => places.Any(p => p.Contains(at));

    /// <summary>The place a point stands in. A point on a shared wall belongs to the first declared.</summary>
    internal Place PlaceAt(Waypoint at)
    {
        foreach (var p in places) if (p.Contains(at)) return p;
        throw new DomainException($"the point ({Fmt(at.X)}, {Fmt(at.Y)}) is nowhere on the map");
    }

    /// <summary>The shortest road from one point to another through the passages: the legs to walk, the goal last. Empty when both points share a place with a clear line.</summary>
    internal List<Leg> Road(Waypoint from, Waypoint to)
    {
        var start = new Node(from, PlacesOf(from), null);
        var goal = new Node(to, PlacesOf(to), null);
        var nodes = new List<Node> { start };
        foreach (var d in passages.Where(p => p.IsDoor))
            nodes.Add(new Node(d.At, new[] { d.A, d.B }, d));
        // an open boundary is a node too (its midpoint), so a road can chain two openings
        foreach (var o in passages.Where(p => !p.IsDoor))
        {
            var mid = SharedEdgeMidpoint(PlaceNamed(o.A), PlaceNamed(o.B));
            if (mid != null) nodes.Add(new Node(mid, new[] { o.A, o.B }, o));
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
                if (!Sees(u, v, out double cost)) continue;
                if (dist[u] + cost < dist[v]) { dist[v] = dist[u] + cost; prev[v] = u; }
            }
        }
        if (double.IsPositiveInfinity(dist[goal]))
            throw new DomainException($"no road from ({Fmt(from.X)}, {Fmt(from.Y)}) to ({Fmt(to.X)}, {Fmt(to.Y)}) through the map");

        var legs = new List<Leg>();
        for (Node n = goal; n != start; n = prev[n])
        {
            if (n == goal) legs.Insert(0, new Leg(n.At, PlaceAt(to).Name));
            else if (n.Via != null) legs.Insert(0, new Leg(n.At, n.Via.Name));
            else legs.Insert(0, new Leg(n.At, OpeningCrossed(prev[n], n)?.Name ?? "?"));
        }
        // an open boundary crossed on the way is a leg of its own, so the journal tells it
        return WithOpeningsNamed(from, legs);
    }

    internal double RoadLength(Waypoint from, Waypoint to)
    {
        double length = 0;
        Waypoint here = from;
        foreach (var leg in Road(from, to)) { length += here.DistanceTo(leg.At); here = leg.At; }
        return length;
    }

    /// <summary>The map as the panel and the painter read it.</summary>
    internal string Describe()
    {
        var sb = new StringBuilder("{\"places\":[");
        sb.Append(string.Join(",", places.Select(p =>
            $"{{\"name\":\"{p.Name}\",\"x\":{Fmt(p.X)},\"y\":{Fmt(p.Y)},\"w\":{Fmt(p.Width)},\"h\":{Fmt(p.Height)},\"cx\":{Fmt(p.Center.X)},\"cy\":{Fmt(p.Center.Y)}}}")));
        sb.Append("],\"doors\":[");
        sb.Append(string.Join(",", passages.Where(p => p.IsDoor).Select(p =>
            $"{{\"a\":\"{p.A}\",\"b\":\"{p.B}\",\"x\":{Fmt(p.At.X)},\"y\":{Fmt(p.At.Y)}}}")));
        sb.Append("],\"opens\":[");
        sb.Append(string.Join(",", passages.Where(p => !p.IsDoor).Select(p => $"{{\"a\":\"{p.A}\",\"b\":\"{p.B}\"}}")));
        sb.Append("]}");
        return sb.ToString();
    }

    // ---- the graph ----

    private sealed class Node
    {
        internal Waypoint At { get; }
        internal string[] Places { get; }
        internal Passage Via { get; }
        internal Node(Waypoint at, string[] places, Passage via) { At = at; Places = places; Via = via; }
    }

    private string[] PlacesOf(Waypoint at)
    {
        var names = places.Where(p => p.Contains(at)).Select(p => p.Name).ToArray();
        if (names.Length == 0) throw new DomainException($"the point ({Fmt(at.X)}, {Fmt(at.Y)}) is nowhere on the map");
        return names;
    }

    // Two nodes see each other when they share a place (a straight line inside a rectangle),
    // or when they stand in two places joined by an opening and the straight line between
    // them crosses that open boundary.
    private bool Sees(Node u, Node v, out double cost)
    {
        cost = u.At.DistanceTo(v.At);
        if (u.Places.Intersect(v.Places).Any()) return true;
        return OpeningCrossed(u, v) != null;
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

    // Insert a named leg where the road crosses an open boundary, so `Pass` can tell it.
    private List<Leg> WithOpeningsNamed(Waypoint from, List<Leg> legs)
    {
        var result = new List<Leg>();
        Waypoint here = from;
        foreach (var leg in legs)
        {
            var crossing = OpeningBetween(here, leg.At);
            if (crossing != null) result.Add(crossing);
            result.Add(leg);
            here = leg.At;
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

    private static Waypoint CrossingPoint(Place a, Place b, Waypoint u, Waypoint v)
    {
        foreach (var (left, right) in new[] { (a, b), (b, a) })
            if (Math.Abs(left.X + left.Width - right.X) < 1e-6)
            {
                double x = right.X, t = (x - u.X) / (v.X - u.X);
                return new Waypoint(x, u.Y + t * (v.Y - u.Y));
            }
        foreach (var (low, high) in new[] { (a, b), (b, a) })
            if (Math.Abs(low.Y + low.Height - high.Y) < 1e-6)
            {
                double y = high.Y, t = (y - u.Y) / (v.Y - u.Y);
                return new Waypoint(u.X + t * (v.X - u.X), y);
            }
        return v;
    }

    private static string Fmt(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>One stretch of a road: where to go next, and how the journal names it (a door, an opening, or the goal's place).</summary>
internal sealed class Leg
{
    internal Waypoint At { get; }
    internal string Name { get; }
    internal Leg(Waypoint at, string name) { At = at; Name = name; }
}
