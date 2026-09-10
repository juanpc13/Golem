using GolemHost.Domain.Geometry;

namespace GolemHost.Domain.Plans;

/// <summary>
/// A mark: the location where a body touched something the plan does not hold, and the heading it had when
/// it touched — a FACT of the map of touches, as opposed to the <see cref="Thing"/> hypothesized from several.
/// The heading is the mark's normal: the thing lies beyond the point in that direction, and the side the body
/// came from is free at the body's radius. Along the surface whatever stands there is taken to reach at least
/// <see cref="Reach"/> around the point (the hypothesis is refined by the next touch).
/// <para>Origins: robotics would write this into an occupancy grid (Moravec &amp; Elfes, 1985) as a cell's
/// probability. We keep the touch itself — a located fact with a reach and a normal — because the journal
/// records what was lived, not a snapshot of belief; the grid is a projection someone else may draw from the marks.
/// The 8-sep lab showed the earlier disc (no normal) inflating a 0.7 crate so much that a 3 m hall closed to a
/// 0.5 body on both sides: the normal is what opens the side the body came from.</para>
/// </summary>
internal sealed class Mark : Location
{
    internal Mark(double x, double y, double heading) : base("mark", x, y)
    {
        if (double.IsNaN(heading) || double.IsInfinity(heading)) throw new DomainException("a mark needs the heading of the touch: its normal");
        Heading = heading;
    }

    /// <summary>The heading of the touch: the normal into the thing.</summary>
    internal double Heading { get; }
    internal double Reach => ObstacleMap.MarkReach;

    /// <summary>How far beyond the mark a position lies, along the normal (negative: on the side the body came from).</summary>
    internal double Ahead(Position p) => (p.X - X) * Math.Cos(Heading) + (p.Y - Y) * Math.Sin(Heading);

    /// <summary>Whether a body of this radius, centered at a position, would run into what the mark stands for:
    /// within the clearance (reach + radius + margin) of the point, unless it stands on the side the body came
    /// from, a radius and a margin clear of the surface.</summary>
    internal bool Blocks(Position center, double radius)
    {
        if (DistanceTo(center) >= Reach + radius + ObstacleMap.MarkMargin) return false;
        return Ahead(center) > -(radius + ObstacleMap.MarkMargin);
    }
}
