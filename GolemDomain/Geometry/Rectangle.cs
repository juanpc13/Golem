using Envelope = NetTopologySuite.Geometries.Envelope;
using Shape2D = NetTopologySuite.Geometries.Geometry;
using GeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using LineString = NetTopologySuite.Geometries.LineString;

namespace GolemDomain.Geometry;

/// <summary>
/// An axis-aligned rectangle on the plane: the figure an area of the map is realized as today (a polygon
/// later). Pure geometry — it knows nothing of areas, doors or walls; the layout composes it into a zone.
/// Inside it is NetTopologySuite's envelope, and its answers are the library's (ajuste 89): where a position lies,
/// how far, whether a run enters it, which edge two rectangles share (the intersection of their boxes, when it is a line).
/// </summary>
internal sealed class Rectangle
{
    private const double Tolerance = 1e-9;
    private static readonly GeometryFactory Factory = GeometryFactory.Default;

    internal double X { get; }
    internal double Y { get; }
    internal double Width { get; }
    internal double Height { get; }
    private readonly Envelope box;
    private Shape2D shape;

    internal Rectangle(double x, double y, double width, double height)
    {
        if (width <= 0 || height <= 0) throw new GolemDomainException("a rectangle needs a positive width and height");
        X = x;
        Y = y;
        Width = width;
        Height = height;
        box = new Envelope(x, x + width, y, y + height);
    }

    /// <summary>The rectangle a library envelope covers.</summary>
    internal Rectangle(Envelope envelope) : this(Guarded(envelope).MinX, envelope.MinY, envelope.Width, envelope.Height) { }

    private static Envelope Guarded(Envelope envelope) =>
        envelope ?? throw new GolemDomainException("Rectangle.Rectangle: 'envelope' was not given");

    private Shape2D Shape => shape ??= Factory.ToGeometry(box);

    internal Position Center => Position.Of(box.Centre);

    /// <summary>Inclusive on the edges: a point on a shared edge belongs to both rectangles.</summary>
    internal bool Contains(Position at)
    {
        if (at == null) throw new GolemDomainException("Rectangle.Contains: 'at' was not given");
        var tolerant = box.Copy();
        tolerant.ExpandBy(Tolerance);
        return tolerant.Contains(at.AsCoordinate());
    }

    /// <summary>The same rectangle grown by a distance on every side.</summary>
    internal Rectangle Inflated(double by)
    {
        var grown = box.Copy();
        grown.ExpandBy(by);
        return new Rectangle(grown);
    }

    /// <summary>How far a position lies from the rectangle: zero inside or on its edges.</summary>
    internal double DistanceTo(Position at)
    {
        if (at == null) throw new GolemDomainException("Rectangle.DistanceTo: 'at' was not given");
        return box.Distance(new Envelope(at.AsCoordinate()));
    }

    /// <summary>Whether a run enters the rectangle: an end inside it, or the run crossing one of its edges.</summary>
    internal bool IsCrossedBy(Segment run)
    {
        if (run == null) throw new GolemDomainException("Rectangle.IsCrossedBy: 'run' was not given");
        return Shape.Intersects(Factory.CreateLineString(new[] { run.From.AsCoordinate(), run.To.AsCoordinate() }));
    }

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
    internal bool Touches(Rectangle other)
    {
        if (other == null) throw new GolemDomainException("Rectangle.Touches: 'other' was not given");
        return TrySharedEdge(other) != null;
    }

    /// <summary>The edge shared with another rectangle, or null when they do not touch: what the two boxes have in common,
    /// when that is a line and not a corner (a point) or an overlap (a polygon).</summary>
    internal Segment TrySharedEdge(Rectangle other)
    {
        if (other == null) throw new GolemDomainException("Rectangle.TrySharedEdge: 'other' was not given");
        if (Shape.Intersection(other.Shape) is not LineString shared || shared.IsEmpty || shared.Length <= 1e-6) return null;
        return new Segment(Position.Of(shared.Coordinates[0]), Position.Of(shared.Coordinates[shared.NumPoints - 1]));
    }

    /// <summary>A unit step across the shared edge, from this rectangle into the other.</summary>
    internal Position StepInto(Rectangle other)
    {
        if (other == null) throw new GolemDomainException("Rectangle.StepInto: 'other' was not given");
        var edge = TrySharedEdge(other) ?? throw new GolemDomainException("the rectangles share no edge to step across");
        return edge.IsVertical
            ? new Position(Math.Sign(other.Center.X - Center.X), 0)
            : new Position(0, Math.Sign(other.Center.Y - Center.Y));
    }
}
