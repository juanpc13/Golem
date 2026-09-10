using GolemDomain.Geometry;

namespace GolemDomain.Layouts;

/// <summary>
/// A wall — Juan's PARED: a boundary of a zone that is not freed by an opening. It HAS a line on the plane (a
/// segment of no thickness today), it is as tall as the map says, and it is pierced only by its doors. A body
/// cannot go through it; it may touch it, and a touch on a known wall is the body's own execution error, not a
/// discovery. It is not a segment: the segment is how the layout realizes it (Juan, 10-sep-2026).
/// </summary>
internal sealed class Wall
{
    private readonly IReadOnlyList<PlacedDoor> doors;

    /// <summary>The zone this wall bounds (a shared wall is a wall of both zones, each seeing its own).</summary>
    internal Zone Zone { get; }
    /// <summary>The line the wall stands on.</summary>
    internal Segment Line { get; }
    internal double Thickness => Maps.Map.WallThickness;
    internal double Height => Maps.Map.Height;

    internal Wall(Zone zone, Segment line, IReadOnlyList<PlacedDoor> doors)
    {
        Zone = zone ?? throw new DomainException("a wall bounds a zone");
        Line = line ?? throw new DomainException("a wall stands on a line");
        this.doors = doors ?? throw new DomainException("a wall knows its doors, even none");
    }

    internal Position From => Line.From;
    internal Position To => Line.To;
    internal double Length => Line.Length;

    /// <summary>The doors that pierce this wall, each where it stands.</summary>
    internal IReadOnlyList<PlacedDoor> Doors() => doors;

    /// <summary>How far a position lies from the wall's line.</summary>
    internal double DistanceTo(Position at) => Line.DistanceTo(at);

    /// <summary>Whether a touched point lies on this wall: within tolerance of its line (the wall's real thickness,
    /// the pose's error) and not in one of its doorways — within a door's gap there is no wall, whatever was
    /// touched there is something else.</summary>
    internal bool Holds(Position at, double tolerance) =>
        Line.DistanceTo(at) <= tolerance && !doors.Any(d => d.At.DistanceTo(at) <= MapLayout.DoorGap);
}
