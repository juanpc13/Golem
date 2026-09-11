using GolemDomain.Geometry;

namespace GolemDomain.Touches;

/// <summary>
/// A mark — Juan's MARCA (hecho): a body touched something the map does not hold. It HAS a position (where the
/// touch fell) and a heading (which way the body faced: the mark's normal, the thing lies beyond the point in
/// that direction and the side the body came from is free at the body's radius). A FACT of the collisions
/// module, as opposed to the <see cref="Thing"/> hypothesized from several. Along the surface whatever stands
/// there is taken to reach at least <see cref="Reach"/> around the point. It is not a position: the position is
/// where it fell (Juan, 10-sep-2026).
/// <para>Origins: robotics would write this into an occupancy grid (Moravec &amp; Elfes, 1985) as a cell's
/// probability. We keep the touch itself — a located fact with a reach and a normal — because the journal
/// records what was lived, not a snapshot of belief; the grid is a projection someone else may draw from the marks.
/// The 8-sep lab showed the earlier disc (no normal) inflating a 0.7 crate so much that a 3 m hall closed to a
/// 0.5 body on both sides: the normal is what opens the side the body came from.</para>
/// </summary>
internal sealed class Mark
{
    internal Position At { get; }
    /// <summary>The heading of the touch: the normal into the thing.</summary>
    internal double Heading { get; }

    internal Mark(Position at, double heading)
    {
        At = at ?? throw new GolemDomainException("a mark needs where the touch fell");
        if (double.IsNaN(heading) || double.IsInfinity(heading)) throw new GolemDomainException("a mark needs the heading of the touch: its normal");
        Heading = heading;
    }

    internal double Reach => Collisions.MarkReach;

    internal double DistanceTo(Position p) => At.DistanceTo(p);

    /// <summary>How far beyond the mark a position lies, along the normal (negative: on the side the body came from).</summary>
    internal double Ahead(Position p) => (p.X - At.X) * Math.Cos(Heading) + (p.Y - At.Y) * Math.Sin(Heading);

    /// <summary>Whether a body of this radius, centered at a position, would run into what the mark stands for:
    /// within the clearance (reach + radius + margin) of the point, unless it stands on the side the body came
    /// from, a radius and a margin clear of the surface.</summary>
    internal bool Blocks(Position center, double radius)
    {
        if (At.DistanceTo(center) >= Reach + radius + Collisions.MarkMargin) return false;
        return Ahead(center) > -(radius + Collisions.MarkMargin);
    }
}
