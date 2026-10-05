using LineSegment = NetTopologySuite.Geometries.LineSegment;

namespace GolemDomain.Geometry;

/// <summary>
/// A segment: a straight run from one position to another — the robot's basic displacement from position i to
/// position j, the unit a trajectory is made of, and the line a wall stands on (no thickness, today). A wall
/// HAS a segment; it is not one (Juan, 10-sep-2026). What it answers — its length, its closest point, whether two
/// cross — is NetTopologySuite's line segment answering (ajuste 89).
/// </summary>
internal sealed class Segment
{
    internal Position From { get; }
    internal Position To { get; }
    private readonly LineSegment line;

    internal Segment(Position from, Position to)
    {
        if (from == null || to == null) throw new GolemDomainException("a segment needs both of its ends");
        if (ReferenceEquals(from, to)) throw new GolemDomainException("Segment.Segment: 'from' and 'to' are the same point");
        From = from;
        To = to;
        line = new LineSegment(from.AsCoordinate(), to.AsCoordinate());
    }

    internal double Length => line.Length;
    internal Position Midpoint => Position.Of(line.MidPoint);
    internal bool IsVertical => Math.Abs(From.X - To.X) < 1e-9;
    internal bool IsHorizontal => Math.Abs(From.Y - To.Y) < 1e-9;
    /// <summary>The heading of the run, radians counter-clockwise from +x.</summary>
    internal double Heading => line.Angle;

    /// <summary>The point of this segment closest to a position.</summary>
    internal Position ClosestTo(Position p)
    {
        if (p == null) throw new GolemDomainException("Segment.ClosestTo: 'p' was not given");
        return Position.Of(line.ClosestPoint(p.AsCoordinate()));
    }

    /// <summary>Whether this segment and another cross or touch (a shared point counts).</summary>
    internal bool Crosses(Segment other)
    {
        if (other == null) throw new GolemDomainException("Segment.Crosses: 'other' was not given");
        return line.Intersection(other.line) != null;
    }

    /// <summary>How far a position lies from the closest point of this segment (zero when it lies on it).</summary>
    internal double DistanceTo(Position p)
    {
        if (p == null) throw new GolemDomainException("Segment.DistanceTo: 'p' was not given");
        return line.Distance(p.AsCoordinate());
    }
}
