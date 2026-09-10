namespace GolemDomain.Geometry;

/// <summary>
/// A segment: a straight run from one position to another — the robot's basic displacement from position i to
/// position j, the unit a trajectory is made of, and the shape of a wall (a line, no thickness, today).
/// Open, because a <see cref="Plans.Wall"/> IS a segment of a place's boundary.
/// </summary>
internal class Segment
{
    internal Position From { get; }
    internal Position To { get; }

    internal Segment(Position from, Position to)
    {
        if (from == null || to == null) throw new DomainException("a segment needs both of its ends");
        From = from;
        To = to;
    }

    internal double Length => From.DistanceTo(To);
    internal Position Midpoint => new((From.X + To.X) / 2, (From.Y + To.Y) / 2);
    internal bool IsVertical => Math.Abs(From.X - To.X) < 1e-9;
    internal bool IsHorizontal => Math.Abs(From.Y - To.Y) < 1e-9;
    /// <summary>The heading of the run, radians counter-clockwise from +x.</summary>
    internal double Heading => From.HeadingTo(To);

    /// <summary>The point of this segment closest to a position.</summary>
    internal Position ClosestTo(Position p)
    {
        double dx = To.X - From.X, dy = To.Y - From.Y;
        double length2 = dx * dx + dy * dy;
        double t = length2 < 1e-12 ? 0 : Math.Clamp(((p.X - From.X) * dx + (p.Y - From.Y) * dy) / length2, 0, 1);
        return new Position(From.X + t * dx, From.Y + t * dy);
    }

    /// <summary>How far a position lies from the closest point of this segment (zero when it lies on it).</summary>
    internal double DistanceTo(Position p) => ClosestTo(p).DistanceTo(p);
}
