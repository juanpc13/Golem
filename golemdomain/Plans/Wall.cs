using GolemHost.Domain.Geometry;

namespace GolemHost.Domain.Plans;

/// <summary>
/// A wall: a boundary of a place that is not declared open — a straight segment of (today) no thickness on
/// the plane, as tall as the plan, pierced only by its doors. A body cannot go through it; it may touch it,
/// and a touch on a known wall is the body's own execution error, not a discovery.
/// </summary>
internal sealed class Wall : Segment
{
    private readonly IReadOnlyList<Door> doors;

    /// <summary>The place this wall bounds (a shared wall is a wall of both places, each seeing its own).</summary>
    internal Place Place { get; }
    internal double Thickness => FloorPlan.WallThickness;
    internal double Height => FloorPlan.Height;

    internal Wall(Place place, Position from, Position to, IReadOnlyList<Door> doors) : base(from, to)
    {
        if (place == null) throw new DomainException("a wall bounds a place");
        if (doors == null) throw new DomainException("a wall knows its doors, even none");
        Place = place;
        this.doors = doors;
    }

    /// <summary>The doors that pierce this wall.</summary>
    internal IReadOnlyList<Door> Doors() => doors;

    /// <summary>Whether a touched point lies on this wall: within tolerance of its line (the wall's real thickness,
    /// the pose's error) and not in one of its doorways — within a door's gap there is no wall, whatever was
    /// touched there is something else.</summary>
    internal bool Holds(Position at, double tolerance) =>
        DistanceTo(at) <= tolerance && !doors.Any(d => d.At.DistanceTo(at) <= FloorPlan.DoorGap);
}
