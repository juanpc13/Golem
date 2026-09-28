using GolemDomain.Geometry;
using GolemDomain.Layouts;

namespace GolemDomain.Routes;

/// <summary>
/// HOW a route finds its way through the DOORS — the mode of travel the golem's verb chose (ajuste 58, 28-sep-2026; Juan: "mantener
/// la lógica anterior que tomaba hasta 5 legs con el visit, y este nuevo verbo que lo haga como lo solucionamos con 2 legs…
/// manejaríamos 3 modos: visit, cover y este nuevo"). Two ways, one operation each — never a flag the planner switches on:
/// <see cref="DoorByDoor"/> (every door on the way is a leg of its own: lined up before it, crossed straight, out past it — Visit,
/// Cover, Follow) and <see cref="OnTheWay"/> (a door is crossed on the way when the straight run passes clean; the way bends only at
/// the doors' points of clearance — Dash, the ajuste 57 way). A route is born with its mode and decides every way again with it.
/// </summary>
internal abstract class Navigation
{
    /// <summary>Every door on the way is a stop of the way: the node is the door's point, and the road as the body walks it lines up
    /// before each door and stands clear past it (approach and exit) — the way of Visit and Cover since 17-sep-2026.</summary>
    internal static readonly Navigation DoorByDoor = new DoorByDoorNavigation();
    /// <summary>A door is crossed on the way when the straight run passes clean through its window; the way bends only at the doors'
    /// points of clearance, never in a doorway, and at equal length prefers fewer stops — the way of Dash (ajuste 57, 28-sep-2026).</summary>
    internal static readonly Navigation OnTheWay = new OnTheWayNavigation();

    /// <summary>What the journal and the panel call this mode.</summary>
    internal abstract string Name { get; }
    /// <summary>Where the planner may stand at a door: the nodes a door offers the graph.</summary>
    internal abstract IReadOnlyList<Position> DoorPoints(MapLayout layout, PlacedDoor door);
    /// <summary>Whether a straight run may cross a door on its way, with no node at it.</summary>
    internal abstract bool ThroughDoors { get; }
    /// <summary>What a stop costs the way, in metres as if: a hair to prefer fewer stops, or nothing.</summary>
    internal abstract double HopCost { get; }
    /// <summary>The road as the body walks it, from the raw legs the planner found between the nodes.</summary>
    internal abstract Trajectory AsWalked(MapLayout layout, Position from, Trajectory road);

    private sealed class DoorByDoorNavigation : Navigation
    {
        internal override string Name => "door by door";
        internal override IReadOnlyList<Position> DoorPoints(MapLayout layout, PlacedDoor door)
        {
            if (layout == null) throw new GolemDomainException("Navigation.DoorPoints: 'layout' was not given");
            if (door == null) throw new GolemDomainException("Navigation.DoorPoints: 'door' was not given");
            return new[] { door.At };
        }
        internal override bool ThroughDoors => false;
        internal override double HopCost => 0.0;
        internal override Trajectory AsWalked(MapLayout layout, Position from, Trajectory road)
        {
            if (layout == null) throw new GolemDomainException("Navigation.AsWalked: 'layout' was not given");
            if (from == null) throw new GolemDomainException("Navigation.AsWalked: 'from' was not given");
            if (road == null) throw new GolemDomainException("Navigation.AsWalked: 'road' was not given");
            return layout.WithDoorCrossings(from, road);
        }
    }

    private sealed class OnTheWayNavigation : Navigation
    {
        internal override string Name => "on the way";
        // a door offers its two points of clearance — DoorClearance into each of its areas, square to the wall — where a body lines
        // up before it and stands clear past it: the way bends THERE, never in the doorway
        internal override IReadOnlyList<Position> DoorPoints(MapLayout layout, PlacedDoor door)
        {
            if (layout == null) throw new GolemDomainException("Navigation.DoorPoints: 'layout' was not given");
            if (door == null) throw new GolemDomainException("Navigation.DoorPoints: 'door' was not given");
            var step = layout.StepInto(door.Door, door.Door.AreaA);   // a unit step through the door into its first area
            return new[]
            {
                new Position(door.At.X + step.X * MapLayout.DoorClearance, door.At.Y + step.Y * MapLayout.DoorClearance),
                new Position(door.At.X - step.X * MapLayout.DoorClearance, door.At.Y - step.Y * MapLayout.DoorClearance),
            };
        }
        internal override bool ThroughDoors => true;
        internal override double HopCost => 0.001;   // metres, as if: what a stop costs the way — at equal length, the way with fewer stops
        internal override Trajectory AsWalked(MapLayout layout, Position from, Trajectory road)
        {
            if (layout == null) throw new GolemDomainException("Navigation.AsWalked: 'layout' was not given");
            if (from == null) throw new GolemDomainException("Navigation.AsWalked: 'from' was not given");
            if (road == null) throw new GolemDomainException("Navigation.AsWalked: 'road' was not given");
            return road;   // the doors the way bends at already carry their approach and exit; the rest were crossed on the way
        }
    }
}
