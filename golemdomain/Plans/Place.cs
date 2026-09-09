using GolemHost.Domain.Geometry;

namespace GolemHost.Domain.Plans;

/// <summary>
/// A place — Juan's ESPACIO / HABITACIÓN: a named room of the floor plan, an axis-aligned rectangle today (a
/// polygon later), bounded by four corner locations and its walls, never overlapping another. Built fluently
/// from the release chain — <c>g.Chart('kitchen', 0, 6, 4, 5).DoorTo('hall', 4, 8.5).OpenTo('living')</c> —
/// so each place declares its own passages and the journal reads like a floor plan.
/// </summary>
internal sealed class Place
{
    internal string Name { get; }
    internal double X { get; }
    internal double Y { get; }
    internal double Width { get; }
    internal double Height { get; }

    private readonly FloorPlan plan;

    internal Place(string name, double x, double y, double width, double height, FloorPlan plan)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("a place needs a name");
        if (width <= 0 || height <= 0) throw new DomainException($"place '{name}' needs a positive width and height");
        if (plan == null) throw new DomainException($"place '{name}' belongs to a floor plan");
        Name = name;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        this.plan = plan;
    }

    internal Position Center => new(X + Width / 2, Y + Height / 2);

    /// <summary>Inclusive on the edges: a point on a shared wall belongs to both places.</summary>
    internal bool Contains(Position at) =>
        at.X >= X - 1e-9 && at.X <= X + Width + 1e-9 && at.Y >= Y - 1e-9 && at.Y <= Y + Height + 1e-9;

    /// <summary>A point inside the place and off every wall by at least the inset — an open edge does not count.</summary>
    internal bool ContainsInset(Position at, double inset) => Contains(at) && Walls().All(w => w.DistanceTo(at) >= inset);

    // ---- what the place holds, for whoever reads the map as objects ----
    //   foreach (places in g.Places()) { print places.Name 'name', …; foreach (doors in places.Doors()) { print doors.To 'to', doors.At.X 'x', …; } }

    /// <summary>The four corners, counter-clockwise from the south-west: the locations that bound the place.</summary>
    internal IReadOnlyList<Location> Corners() => new[]
    {
        new Location($"{Name} sw", X, Y),
        new Location($"{Name} se", X + Width, Y),
        new Location($"{Name} ne", X + Width, Y + Height),
        new Location($"{Name} nw", X, Y + Height),
    };

    /// <summary>The walls: every edge not declared open, each knowing the doors that pierce it.</summary>
    internal IReadOnlyList<Wall> Walls()
    {
        var walls = new List<Wall>();
        foreach (var edge in Edges())
        {
            if (IsOpen(edge)) continue;
            var doors = plan.DoorsJoining(Name).Where(d => edge.DistanceTo(d.At) < 1e-6).ToList();
            walls.Add(new Wall(this, edge.From, edge.To, doors));
        }
        return walls;
    }

    internal IReadOnlyList<Doorway> Doors() => plan.DoorsOf(this);
    internal IReadOnlyList<Opening> Openings() => plan.OpeningsOf(this);
    // What was touched here is not the plan's to answer: ask the golem for its obstacles (g.Obstacles()), which
    // name the zone they stand in.

    // ---- charting (the release chain) ----

    /// <summary>A door to a neighbouring place, at a point on the shared wall. Chainable.</summary>
    internal Place DoorTo(string place, double x, double y)
    {
        plan.AddDoor(Name, place, new Position(x, y));
        return this;
    }

    /// <summary>The whole shared boundary with a neighbour is open — no wall, no door. Chainable.</summary>
    internal Place OpenTo(string place)
    {
        plan.AddOpening(Name, place);
        return this;
    }

    // ---- neighbours: two rectangles that share an edge ----

    /// <summary>Whether this place and another share an edge (touch along a wall).</summary>
    internal bool Touches(Place other) => TrySharedEdge(other) != null;

    /// <summary>The edge shared with a neighbour. Consult Touches first.</summary>
    internal Segment SharedEdgeWith(Place other) =>
        TrySharedEdge(other) ?? throw new DomainException($"'{Name}' and '{other.Name}' share no wall");

    /// <summary>A unit step across the shared wall, from this place into the other.</summary>
    internal Position StepInto(Place other)
    {
        if (Math.Abs(X + Width - other.X) < 1e-6) return new Position(1, 0);
        if (Math.Abs(other.X + other.Width - X) < 1e-6) return new Position(-1, 0);
        if (Math.Abs(Y + Height - other.Y) < 1e-6) return new Position(0, 1);
        if (Math.Abs(other.Y + other.Height - Y) < 1e-6) return new Position(0, -1);
        throw new DomainException($"'{Name}' and '{other.Name}' share no wall to step across");
    }

    // The four edges: south, north, west, east.
    private IEnumerable<Segment> Edges()
    {
        double x0 = X, y0 = Y, x1 = X + Width, y1 = Y + Height;
        yield return new Segment(new Position(x0, y0), new Position(x1, y0));
        yield return new Segment(new Position(x0, y1), new Position(x1, y1));
        yield return new Segment(new Position(x0, y0), new Position(x0, y1));
        yield return new Segment(new Position(x1, y0), new Position(x1, y1));
    }

    // An edge is open when a neighbour joined by an opening shares that very edge.
    private bool IsOpen(Segment edge)
    {
        foreach (var opening in plan.OpeningsJoining(Name))
        {
            if (!plan.Knows(opening.OtherSide(Name))) continue;
            var neighbour = plan.PlaceNamed(opening.OtherSide(Name));
            if (!Touches(neighbour)) continue;
            var shared = SharedEdgeWith(neighbour);
            if (edge.IsVertical && shared.IsVertical && Math.Abs(shared.From.X - edge.From.X) < 1e-6) return true;
            if (edge.IsHorizontal && shared.IsHorizontal && Math.Abs(shared.From.Y - edge.From.Y) < 1e-6) return true;
        }
        return false;
    }

    private Segment TrySharedEdge(Place other)
    {
        if (Math.Abs(X + Width - other.X) < 1e-6) return VerticalOverlap(other.X, other);
        if (Math.Abs(other.X + other.Width - X) < 1e-6) return VerticalOverlap(X, other);
        if (Math.Abs(Y + Height - other.Y) < 1e-6) return HorizontalOverlap(other.Y, other);
        if (Math.Abs(other.Y + other.Height - Y) < 1e-6) return HorizontalOverlap(Y, other);
        return null;
    }

    private Segment VerticalOverlap(double x, Place other)
    {
        double y0 = Math.Max(Y, other.Y), y1 = Math.Min(Y + Height, other.Y + other.Height);
        return y1 > y0 + 1e-6 ? new Segment(new Position(x, y0), new Position(x, y1)) : null;
    }

    private Segment HorizontalOverlap(double y, Place other)
    {
        double x0 = Math.Max(X, other.X), x1 = Math.Min(X + Width, other.X + other.Width);
        return x1 > x0 + 1e-6 ? new Segment(new Position(x0, y), new Position(x1, y)) : null;
    }
}
