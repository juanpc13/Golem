namespace GolemDomain.Geometry;

/// <summary>
/// An axis-aligned rectangle on the plane: the figure an area of the map is realized as today (a polygon
/// later). Pure geometry — it knows nothing of areas, doors or walls; the layout composes it into a zone.
/// </summary>
internal sealed class Rectangle
{
    internal double X { get; }
    internal double Y { get; }
    internal double Width { get; }
    internal double Height { get; }

    internal Rectangle(double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0) throw new GolemDomainException("a rectangle needs a positive width and height");
        X = x;
        Y = y;
        Width = width;
        Height = height;
    }

    internal Position Center => new(X + Width / 2, Y + Height / 2);

    /// <summary>Inclusive on the edges: a point on a shared edge belongs to both rectangles.</summary>
    internal bool Contains(Position at) =>
        at.X >= X - 1e-9 && at.X <= X + Width + 1e-9 && at.Y >= Y - 1e-9 && at.Y <= Y + Height + 1e-9;

    /// <summary>The four corners, counter-clockwise from the south-west.</summary>
    internal IReadOnlyList<Position> Corners() => new[]
    {
        new Position(X, Y), new Position(X + Width, Y), new Position(X + Width, Y + Height), new Position(X, Y + Height),
    };

    /// <summary>The four edges: south, north, west, east.</summary>
    internal IReadOnlyList<Segment> Edges()
    {
        double x0 = X, y0 = Y, x1 = X + Width, y1 = Y + Height;
        return new[]
        {
            new Segment(new Position(x0, y0), new Position(x1, y0)),
            new Segment(new Position(x0, y1), new Position(x1, y1)),
            new Segment(new Position(x0, y0), new Position(x0, y1)),
            new Segment(new Position(x1, y0), new Position(x1, y1)),
        };
    }

    /// <summary>Whether this rectangle and another share an edge (touch along it).</summary>
    internal bool Touches(Rectangle other) => TrySharedEdge(other) != null;

    /// <summary>The edge shared with another rectangle, or null when they do not touch.</summary>
    internal Segment TrySharedEdge(Rectangle other)
    {
        if (Math.Abs(X + Width - other.X) < 1e-6) return VerticalOverlap(other.X, other);
        if (Math.Abs(other.X + other.Width - X) < 1e-6) return VerticalOverlap(X, other);
        if (Math.Abs(Y + Height - other.Y) < 1e-6) return HorizontalOverlap(other.Y, other);
        if (Math.Abs(other.Y + other.Height - Y) < 1e-6) return HorizontalOverlap(Y, other);
        return null;
    }

    /// <summary>A unit step across the shared edge, from this rectangle into the other.</summary>
    internal Position StepInto(Rectangle other)
    {
        if (Math.Abs(X + Width - other.X) < 1e-6) return new Position(1, 0);
        if (Math.Abs(other.X + other.Width - X) < 1e-6) return new Position(-1, 0);
        if (Math.Abs(Y + Height - other.Y) < 1e-6) return new Position(0, 1);
        if (Math.Abs(other.Y + other.Height - Y) < 1e-6) return new Position(0, -1);
        throw new GolemDomainException("the rectangles share no edge to step across");
    }

    private Segment VerticalOverlap(double x, Rectangle other)
    {
        double y0 = Math.Max(Y, other.Y), y1 = Math.Min(Y + Height, other.Y + other.Height);
        return y1 > y0 + 1e-6 ? new Segment(new Position(x, y0), new Position(x, y1)) : null;
    }

    private Segment HorizontalOverlap(double y, Rectangle other)
    {
        double x0 = Math.Max(X, other.X), x1 = Math.Min(X + Width, other.X + other.Width);
        return x1 > x0 + 1e-6 ? new Segment(new Position(x0, y), new Position(x1, y)) : null;
    }
}
