namespace GolemHost.Domain;

/// <summary>
/// A named room of the map: an axis-aligned rectangle. Built fluently from the release
/// chain — <c>g.Chart('kitchen', 0, 6, 4, 5).DoorTo('hall', 4, 8.5).OpenTo('living')</c> —
/// so each place declares its own passages and the journal reads like a floor plan.
/// </summary>
internal sealed class Place
{
    internal string Name { get; }
    internal double X { get; }
    internal double Y { get; }
    internal double Width { get; }
    internal double Height { get; }

    private readonly Atlas atlas;

    internal Place(string name, double x, double y, double width, double height, Atlas atlas)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("a place needs a name");
        if (width <= 0 || height <= 0) throw new DomainException($"place '{name}' needs a positive width and height");
        Name = name;
        X = x;
        Y = y;
        Width = width;
        Height = height;
        this.atlas = atlas;
    }

    internal Waypoint Center => new(X + Width / 2, Y + Height / 2);

    /// <summary>Inclusive on the edges: a point on a shared wall belongs to both places.</summary>
    internal bool Contains(Waypoint at) =>
        at.X >= X - 1e-9 && at.X <= X + Width + 1e-9 && at.Y >= Y - 1e-9 && at.Y <= Y + Height + 1e-9;

    // What the place holds, for whoever reads the map as objects:
    //   foreach (places in g.Places()) { print places.Name 'name', …; foreach (doors in places.Doors()) { print doors.To 'to', doors.At.X 'x', …; } }
    internal IReadOnlyList<Doorway> Doors() => atlas.DoorsOf(this);
    internal IReadOnlyList<Opening> Openings() => atlas.OpeningsOf(this);
    internal IReadOnlyList<Mark> Marks() => atlas.MarksIn(this);
    /// <summary>The obstacles the marks in this place outline: joined vertices, the figure of what stands here uncharted.</summary>
    internal IReadOnlyList<Obstacle> Obstacles() => atlas.ObstaclesIn(this);

    /// <summary>A door to a neighbouring place, at a point on the shared wall. Chainable.</summary>
    internal Place DoorTo(string place, double x, double y)
    {
        atlas.AddDoor(Name, place, new Waypoint(x, y));
        return this;
    }

    /// <summary>The whole shared boundary with a neighbour is open — no wall, no door. Chainable.</summary>
    internal Place OpenTo(string place)
    {
        atlas.AddOpening(Name, place);
        return this;
    }
}
