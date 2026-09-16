namespace GolemDomain.Geometry;

/// <summary>
/// A segment: a straight run from one position to another — the robot's basic displacement from position i to
/// position j, the unit a trajectory is made of, and the line a wall stands on (no thickness, today). A wall
/// HAS a segment; it is not one (Juan, 10-sep-2026).
/// </summary>
internal sealed class Segment
{
    internal Position From { get; }
    internal Position To { get; }

    internal Segment(Position from, Position to)
    {
        if (from == null || to == null) throw new GolemDomainException("a segment needs both of its ends");
        if (ReferenceEquals(from, to)) throw new GolemDomainException("Segment.Segment: 'from' and 'to' are the same point");
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
        if (p == null) throw new GolemDomainException("Segment.ClosestTo: 'p' was not given");
        double dx = To.X - From.X, dy = To.Y - From.Y;
        double length2 = dx * dx + dy * dy;
        double t = length2 < 1e-12 ? 0 : Math.Clamp(((p.X - From.X) * dx + (p.Y - From.Y) * dy) / length2, 0, 1);
        return new Position(From.X + t * dx, From.Y + t * dy);
    }

    /// <summary>Whether this segment and another cross or touch (a shared point counts).</summary>
    internal bool Crosses(Segment other)
    {
        if (other == null) throw new GolemDomainException("Segment.Crosses: 'other' was not given");
        static double Turn(Position a, Position b, Position c) => (b.X - a.X) * (c.Y - a.Y) - (b.Y - a.Y) * (c.X - a.X);
        static bool Within(Position a, Position b, Position c) =>
            Math.Min(a.X, b.X) - 1e-9 <= c.X && c.X <= Math.Max(a.X, b.X) + 1e-9 && Math.Min(a.Y, b.Y) - 1e-9 <= c.Y && c.Y <= Math.Max(a.Y, b.Y) + 1e-9;
        double d1 = Turn(other.From, other.To, From), d2 = Turn(other.From, other.To, To);
        double d3 = Turn(From, To, other.From), d4 = Turn(From, To, other.To);
        if (((d1 > 0 && d2 < 0) || (d1 < 0 && d2 > 0)) && ((d3 > 0 && d4 < 0) || (d3 < 0 && d4 > 0))) return true;
        if (Math.Abs(d1) < 1e-12 && Within(other.From, other.To, From)) return true;
        if (Math.Abs(d2) < 1e-12 && Within(other.From, other.To, To)) return true;
        if (Math.Abs(d3) < 1e-12 && Within(From, To, other.From)) return true;
        if (Math.Abs(d4) < 1e-12 && Within(From, To, other.To)) return true;
        return false;
    }

    /// <summary>How far a position lies from the closest point of this segment (zero when it lies on it).</summary>
    internal double DistanceTo(Position p)
    {
        if (p == null) throw new GolemDomainException("Segment.DistanceTo: 'p' was not given");
        return ClosestTo(p).DistanceTo(p);
    }
}
