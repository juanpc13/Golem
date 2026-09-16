using GolemDomain.Geometry;

namespace GolemDomain.Routes;

/// <summary>
/// One stretch of a trajectory: where to go next, and what it is — a door to cross (kitchen/north), an opening
/// to cross (north~center), a detour around a mark (around), a step out of a peer's way (aside), or a stop to
/// reach (named by its zone: garage). A door's leg also says how it is walked: line up at the approach (in front
/// of the door, off the wall) and end at the exit (behind it); for an opening, a detour or a stop both are the
/// point. The legs of a maneuver (back, aside, ahead) never enter a mission's road.
/// </summary>
internal sealed class Leg
{
    /// <summary>The name of a leg that skirts a mark: something uncharted stands there.</summary>
    internal const string Detour = "around";
    /// <summary>The name of a leg that steps out of a peer's way: a body stands there, and bodies move on.</summary>
    internal const string Courtesy = "aside";
    /// <summary>A point the way passes through that is no passage and no stop — what the route calls a point it was given
    /// bare (the planner's detours and courtesy steps arrive at the journal as points).</summary>
    internal const string Waypoint = "via";

    internal Position At { get; }
    internal string Name { get; }
    internal Position Approach { get; }
    internal Position Exit { get; }
    /// <summary>A stop to reach, as opposed to a passage to cross or a point to pass.</summary>
    internal bool IsStop => Kind == "stop";
    /// <summary>door, opening, around, aside or stop — what the journal's act for this leg is.</summary>
    internal string Kind =>
        Name == Detour ? Detour : Name == Courtesy ? Courtesy : Name == Waypoint ? Waypoint : Name.Contains('/') ? "door" : Name.Contains('~') ? "opening" : "stop";
    /// <summary>A passage's two areas (the first and the second of its name); "" for a point or a stop.</summary>
    internal string A => Kind == "door" ? Name[..Name.IndexOf('/')] : Kind == "opening" ? Name[..Name.IndexOf('~')] : "";
    internal string B => Kind == "door" ? Name[(Name.IndexOf('/') + 1)..] : Kind == "opening" ? Name[(Name.IndexOf('~') + 1)..] : "";

    /// <summary>Whether the way gave this leg its heading: false for the first leg of a way, walked from wherever the
    /// body stands (its pose is telemetry, never the journal's), true for every leg after it.</summary>
    internal bool HasHeading { get; }
    /// <summary>The heading the body travels into this leg's point — from the previous leg's point — radians,
    /// counter-clockwise from +x (Juan, 16-sep-2026: "los cálculos del ángulo al que debería girar" are the domain's).
    /// Zero when the way gave none (HasHeading false).</summary>
    internal double Heading { get; }

    internal Leg(Position at, string name) : this(at, name, at, at) { }

    internal Leg(Position at, string name, Position approach, Position exit) : this(at, name, approach, exit, false, 0.0) { }

    private Leg(Position at, string name, Position approach, Position exit, bool hasHeading, double heading)
    {
        if (at == null || approach == null || exit == null) throw new GolemDomainException("a leg needs its point, its approach and its exit");
        if (name == null) throw new GolemDomainException("a leg needs a name, even an empty one");
        At = at;
        Name = name;
        Approach = approach;
        Exit = exit;
        HasHeading = hasHeading;
        Heading = heading;
    }

    /// <summary>The same leg, told the point it is walked from: its heading is the bearing from there to its approach.</summary>
    internal Leg WalkedFrom(Position previous)
    {
        if (previous == null) throw new GolemDomainException("Leg.WalkedFrom: 'previous' was not given");
        return new Leg(At, Name, Approach, Exit, true, previous.HeadingTo(Approach));
    }
}
