using GolemDomain.Geometry;
using GolemDomain.Maps;

namespace GolemDomain.Layouts;

/// <summary>
/// A zone: an area of the map IN THE PERSPECTIVE OF POSITIONS — it is an <see cref="Area"/> (it inherits the
/// name, the map, the passages) and it also occupies a rectangle on the plane, never overlapping another. It is
/// created once and told what it is in one train: <c>map.Area('kitchen').At(Position(0.0, 8.0)).Size(4.0, 3.0)
/// .DoorAt('north', Position(4.0, 9.5))</c>, and found again with <c>map.Find('kitchen')</c>. From the rectangle
/// and the map it derives its corners, its walls (every edge not freed by an opening, each knowing the doors that
/// pierce it) and how it touches its neighbours.
/// </summary>
internal sealed class Zone : Area
{
    private Position corner;      // the south-west corner, once told
    private double width, height; // the size, once told

    internal Zone(string name, MapLayout layout) : base(name, layout) { }

    /// <summary>The concrete map this zone belongs to: the one with positions.</summary>
    internal MapLayout Layout => (MapLayout)Map;

    // ---- telling the zone where it stands (chainable) ----

    /// <summary>Where the zone stands: its south-west corner. Chainable.</summary>
    internal Zone At(Position southWest)
    {
        corner = southWest ?? throw new GolemDomainException($"zone '{Name}' needs the corner it stands at");
        return this;
    }

    /// <summary>How big the zone is. Chainable.</summary>
    internal Zone Size(double w, double h)
    {
        if (w <= 0 || h <= 0) throw new GolemDomainException($"zone '{Name}' needs a positive width and height");
        width = w;
        height = h;
        return this;
    }

    /// <summary>Where the door into another area stands: a point on the shared wall. Chainable.</summary>
    internal Zone DoorAt(Area area, Position at) { Layout.DoorAt(this, area, at); return this; }

    /// <summary>Where the door into another area, named (it may not be created yet), stands. Chainable.</summary>
    internal Zone DoorAt(string area, Position at) { Layout.DoorAt(Name, area, at); return this; }

    /// <summary>A door into another area, its point still to be told with DoorAt. The train stays a zone's.</summary>
    internal override Zone DoorTo(Area area) { base.DoorTo(area); return this; }
    internal override Zone DoorTo(string area) { base.DoorTo(area); return this; }

    /// <summary>The whole boundary with another area is open. The train stays a zone's.</summary>
    internal override Zone OpenTo(Area area) { base.OpenTo(area); return this; }
    internal override Zone OpenTo(string area) { base.OpenTo(area); return this; }

    // ---- the geometry, once told ----

    /// <summary>Whether the zone has been told where it stands and how big it is.</summary>
    internal bool IsLaidOut => corner != null && width > 0 && height > 0;

    /// <summary>The rectangle the zone occupies. Consult IsLaidOut first.</summary>
    internal Geometry.Rectangle Rect =>
        IsLaidOut ? new Geometry.Rectangle(corner.X, corner.Y, width, height) : throw new GolemDomainException($"area '{Name}' is not laid out");

    internal double X => Rect.X;
    internal double Y => Rect.Y;
    internal double Width => Rect.Width;
    internal double Height => Rect.Height;
    internal Position Center => Rect.Center;

    /// <summary>Inclusive on the edges: a point on a shared wall belongs to both zones.</summary>
    internal bool Contains(Position at) => IsLaidOut && Rect.Contains(at);

    /// <summary>A point inside the zone and off every wall by at least the inset — an open edge does not count.</summary>
    internal bool ContainsInset(Position at, double inset) => Contains(at) && Walls().All(w => w.DistanceTo(at) >= inset);

    // ---- what the zone holds, for whoever reads the map as objects ----
    //   foreach (zones in map.Zones) { print zones.Name 'name', …; foreach (doors in zones.Doorways()) { print doors.To 'to', doors.At.X 'x', …; } }

    /// <summary>The four corners, counter-clockwise from the south-west: the locations that bound the zone.</summary>
    internal IReadOnlyList<Location> Corners()
    {
        var c = Rect.Corners();
        return new[]
        {
            new Location($"{Name} sw", c[0].X, c[0].Y),
            new Location($"{Name} se", c[1].X, c[1].Y),
            new Location($"{Name} ne", c[2].X, c[2].Y),
            new Location($"{Name} nw", c[3].X, c[3].Y),
        };
    }

    /// <summary>The walls: every edge not freed by an opening, each knowing the doors that pierce it.</summary>
    internal IReadOnlyList<Wall> Walls()
    {
        var walls = new List<Wall>();
        foreach (var edge in Rect.Edges())
        {
            if (IsOpen(edge)) continue;
            var doors = Layout.PlacedDoors.Where(d => d.Door.Joins(this) && edge.DistanceTo(d.At) < 1e-6).ToList();
            walls.Add(new Wall(this, edge, doors));
        }
        return walls;
    }

    /// <summary>The doors of this zone as it sees them: the area across, where the door stands, how wide it is —
    /// the placed ones (the map's plain list of doors is <c>Doors()</c>, inherited from the area).</summary>
    internal IReadOnlyList<Doorway> Doorways() =>
        Layout.PlacedDoors.Where(d => d.Door.Joins(this)).Select(d => new Doorway(d.Door.OtherSide(this), d.At, d.Width)).ToList();

    /// <summary>The open stretches of this zone: the area across each one.</summary>
    internal IReadOnlyList<OpenSide> OpenSides() =>
        Map.OpeningsOf(this).Where(o => Map.Knows(o.A) && Map.Knows(o.B)).Select(o => new OpenSide(o.OtherSide(this))).ToList();

    // ---- neighbours: two rectangles that share an edge ----

    internal bool Touches(Zone other) => other != null && IsLaidOut && other.IsLaidOut && Rect.Touches(other.Rect);

    /// <summary>The edge shared with a neighbour. Consult Touches first.</summary>
    internal Segment SharedEdgeWith(Zone other) =>
        Rect.TrySharedEdge(other.Rect) ?? throw new GolemDomainException($"'{Name}' and '{other.Name}' share no wall");

    /// <summary>A unit step across the shared wall, from this zone into the other.</summary>
    internal Position StepInto(Zone other)
    {
        try { return Rect.StepInto(other.Rect); }
        catch (GolemDomainException) { throw new GolemDomainException($"'{Name}' and '{other.Name}' share no wall to step across"); }
    }

    // An edge is open when a neighbour joined by an opening shares that very edge.
    private bool IsOpen(Segment edge)
    {
        foreach (var opening in Map.OpeningsOf(this))
        {
            string across = opening.A == Name ? opening.B : opening.A;
            if (!Map.Knows(across)) continue;
            var neighbour = Layout.Find(across);
            if (!Touches(neighbour)) continue;
            var shared = SharedEdgeWith(neighbour);
            if (edge.IsVertical && shared.IsVertical && Math.Abs(shared.From.X - edge.From.X) < 1e-6) return true;
            if (edge.IsHorizontal && shared.IsHorizontal && Math.Abs(shared.From.Y - edge.From.Y) < 1e-6) return true;
        }
        return false;
    }
}
