using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Touches;

namespace GolemDomain.Routes;

/// <summary>
/// The courtesy step: how a body gets out of another's way. A body's width to one side of where it faces — its own
/// right first (so two bodies facing each other separate), then its left. The step must fit the body and must not walk
/// INTO the peer (whose place is what it told when it bumped). What a route decides first when it met a peer
/// (<see cref="Route.DecidePast"/>) and what a standing golem does when something touches it (<c>g.Aside</c>).
/// </summary>
internal static class Courtesy
{
    /// <summary>Two radii of the body to one side of where it faces.</summary>
    internal const double Step = 0.5;

    /// <summary>Where the body steps to, or null when neither side fits.</summary>
    internal static Position StepOutOfTheWayOf(MapLayout layout, Collisions collisions, double radius, string who, Pose me) =>
        StepOutOfTheWayOf(layout, collisions, radius, who, me, Step);

    /// <summary>The same step, as long as told: passing a body wants more than the courtesy's two radii (22-sep-2026 live: two
    /// radii left the centres exactly a body apart, and the run past the peer grazed it).</summary>
    internal static Position StepOutOfTheWayOf(MapLayout layout, Collisions collisions, double radius, string who, Pose me, double length)
    {
        if (layout == null) throw new GolemDomainException("Courtesy.StepOutOfTheWayOf: 'layout' was not given");
        if (collisions == null) throw new GolemDomainException("Courtesy.StepOutOfTheWayOf: 'collisions' was not given");
        if (me == null) throw new GolemDomainException("Courtesy.StepOutOfTheWayOf: 'me' was not given");
        if (length <= 0) throw new GolemDomainException("Courtesy.StepOutOfTheWayOf: the step must be a length above zero");
        var peer = collisions.LastKnownPositionOf(who);
        foreach (var turn in new[] { -Math.PI / 2, Math.PI / 2 })   // right, then left
        {
            var step = me.Along(me.Heading + turn, length);
            if (!(layout.HasRoom(step, radius) && !collisions.Blocks(step, radius))) continue;
            if (peer != null && step.DistanceTo(peer) <= me.DistanceTo(peer)) continue;
            return step;
        }
        return null;
    }
}
