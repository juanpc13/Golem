using GolemDomain.Geometry;

namespace GolemDomain.Routes;

/// <summary>
/// One stretch of a trajectory: where to go next, and how the journal names it — a door (kitchen/north), an
/// opening (north~center), a detour around a mark (around), a step out of a peer's way (aside), or a
/// stop (the place alone: garage). A door's
/// leg also says how it is walked: line up at the approach (in front of the door, off the wall) and end at
/// the exit (behind it); for an opening, a detour or a stop both are the point. The legs of a maneuver
/// (back, aside, ahead) never enter a mission's road.
/// </summary>
internal sealed class Leg
{
    /// <summary>The name of a leg that skirts a mark: something uncharted stands there.</summary>
    internal const string Detour = "around";
    /// <summary>The name of a leg that steps out of a peer's way: a body stands there, and bodies move on.</summary>
    internal const string Courtesy = "aside";

    internal Position At { get; }
    internal string Name { get; }
    internal Position Approach { get; }
    internal Position Exit { get; }
    /// <summary>A stop to reach, as opposed to a passage to cross or a mark to skirt.</summary>
    internal bool IsStop => Name != Detour && Name != Courtesy && !Name.Contains('/') && !Name.Contains('~');

    internal Leg(Position at, string name) : this(at, name, at, at) { }

    internal Leg(Position at, string name, Position approach, Position exit)
    {
        if (at == null || approach == null || exit == null) throw new DomainException("a leg needs its point, its approach and its exit");
        if (name == null) throw new DomainException("a leg needs a name, even an empty one");
        At = at;
        Name = name;
        Approach = approach;
        Exit = exit;
    }
}
