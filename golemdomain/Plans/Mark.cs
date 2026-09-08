using GolemHost.Domain.Geometry;

namespace GolemHost.Domain.Plans;

/// <summary>
/// A mark: the location where a body touched something the plan does not hold — a FACT of the map of
/// touches, as opposed to the <see cref="Obstacle"/> hypothesized from several. Whatever stands there is
/// taken to reach at least <see cref="Reach"/> around the point; a body passes it at its radius plus a margin.
/// <para>Origins: robotics would write this into an occupancy grid (Moravec &amp; Elfes, 1985) as a cell's
/// probability. We keep the touch itself — a located fact with a reach — because the journal records what was
/// lived, not a snapshot of belief; the grid is a projection someone else may draw from the marks.</para>
/// </summary>
internal sealed class Mark : Location
{
    internal Mark(double x, double y) : base("mark", x, y) { }

    internal double Reach => FloorPlan.MarkReach;
}
