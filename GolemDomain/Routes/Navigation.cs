using GolemDomain.Geometry;
using GolemDomain.Layouts;

namespace GolemDomain.Routes;

/// <summary>
/// HOW a route finds its way through the DOORS. Since ajuste 61 (29-sep-2026; Juan: "el dash se quitará, porque ahora es más una
/// política de estrategia para navegar… poder cambiarle al golem cuál deseamos que utilice") it is the GOLEM's STRATEGY. Since
/// ajuste 62 (the same night; Juan: "este route pregunte por esos if para ver las estrategias para optimizar la route con dash, si no
/// directo al print") every route is BORN door by door and the SCRIPT applies the strategy with the route in hand. Since ajuste 63
/// (30-sep-2026; Juan: "más bien es como un toggle y esos objetos ya existen dentro del golem") the strategies LIVE IN THE GOLEM from
/// birth — nothing is constructed from the journal, nothing found by a string: <c>g.Strategy.OnTheWay</c> and <c>g.Strategy.DoorByDoor</c>
/// are the golem's own (<see cref="Strategies"/>), <c>Activate()</c> switches one on (one active at a time), <c>Deactivate()</c> leaves the
/// direct one, and the script asks the strategy itself — <c>if (g.Strategy.OnTheWay.IsActive) { route = g.Dash(route); }</c> — so the
/// decision shows in the journal. Two ways, one
/// operation each — never a flag the planner switches on: <see cref="DoorByDoor"/> (every door on the way is a leg of its own: lined
/// up before it, crossed straight, out past it) and <see cref="OnTheWay"/> (a door is crossed on the way when the straight run passes
/// clean; the way bends only at the doors' points of clearance — the ajuste 57 way).
/// </summary>
internal abstract class Navigation
{
    private Action<Navigation> chosen;   // the golem this strategy belongs to, told when it is switched on: it switches the others off
    private Action<Navigation> dropped;  // …and when it is switched off: the direct way stays

    /// <summary>What the journal and the panel call this mode.</summary>
    internal abstract string Name { get; }
    /// <summary>Whether this strategy improves a route with a dash — what a route asks before improving itself twice (ajuste 62).</summary>
    internal abstract bool IsOnTheWay { get; }
    /// <summary>The direct way: what the golem goes by when nothing else is active; it cannot be switched off.</summary>
    internal abstract bool IsDefault { get; }

    /// <summary>Whether the golem that holds this strategy goes by it now — what the script asks with a route in hand (ajuste 63):
    /// <c>if (g.Strategy.OnTheWay.IsActive) { route = g.Dash(route); }</c>.</summary>
    internal bool IsActive { get; private set; }

    /// <summary>The strategy switched ON (ajuste 63; the team's notes: <c>g.StrategyCinetica['Dashed'].Active()</c>): the golem that holds
    /// it goes by it from here on and switches the others off — <c>strategy = g.Strategy.OnTheWay; strategy.Activate();</c>. Already
    /// active: nothing changes.</summary>
    internal Navigation Activate()
    {
        if (chosen == null) throw new GolemDomainException($"the strategy '{Name}' belongs to no golem: it is the golem's own (g.Strategy.OnTheWay), never built");
        if (!IsActive) chosen(this);
        return this;
    }

    /// <summary>The strategy switched OFF: the direct way stays. The direct way itself is refused — it is what stays when nothing
    /// else is active. Already off: nothing changes.</summary>
    internal Navigation Deactivate()
    {
        if (dropped == null) throw new GolemDomainException($"the strategy '{Name}' belongs to no golem: it is the golem's own (g.Strategy.OnTheWay), never built");
        if (IsDefault) throw new GolemDomainException($"'{Name}' is the golem's direct way: it stays when nothing else is active, and cannot be switched off — activate another");
        if (IsActive) dropped(this);
        return this;
    }

    // The golem that holds this strategy hands it the switches; the golem alone sets what is active, so one is active at a time.
    internal void HeldBy(Action<Navigation> chosen, Action<Navigation> dropped)
    {
        if (chosen == null) throw new GolemDomainException("Navigation.HeldBy: 'chosen' was not given");
        if (dropped == null) throw new GolemDomainException("Navigation.HeldBy: 'dropped' was not given");
        this.chosen = chosen;
        this.dropped = dropped;
    }
    internal void Active(bool on) => IsActive = on;

    /// <summary>Where the planner may stand at a door: the nodes a door offers the graph.</summary>
    internal abstract IReadOnlyList<Position> DoorPoints(MapLayout layout, PlacedDoor door);
    /// <summary>Whether a straight run may cross a door on its way, with no node at it.</summary>
    internal abstract bool ThroughDoors { get; }
    /// <summary>What a stop costs the way, in metres as if: a hair to prefer fewer stops, or nothing.</summary>
    internal abstract double HopCost { get; }
    /// <summary>The road as the body walks it, from the raw legs the planner found between the nodes.</summary>
    internal abstract Trajectory AsWalked(MapLayout layout, Position from, Trajectory road);

}

/// <summary>Every door on the way is a stop of the way: the node is the door's point, and the road as the body walks it lines up
/// before each door and stands clear past it (approach and exit) — the golem's DIRECT way, active at birth and how every route is
/// BORN (ajuste 62); what stays when the other strategy is switched off (ajuste 63): <c>g.Strategy.DoorByDoor</c>.</summary>
internal sealed class DoorByDoor : Navigation
{
    internal override string Name => "door by door";
    internal override bool IsOnTheWay => false;
    internal override bool IsDefault => true;
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

/// <summary>A door is crossed on the way when the straight run passes clean through its window; the way bends only at the doors'
/// points of clearance, never in a doorway, and at equal length prefers fewer stops (ajuste 57): <c>strategy = g.Strategy.OnTheWay;
/// strategy.Activate();</c> — and, with a route in hand, <c>if (g.Strategy.OnTheWay.IsActive) { route = g.Dash(route); }</c> (ajustes 62, 63).</summary>
internal sealed class OnTheWay : Navigation
{
    internal override string Name => "on the way";
    internal override bool IsOnTheWay => true;
    internal override bool IsDefault => false;
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

/// <summary>The golem's strategies of navigation, born with it (ajuste 63; Juan: "esos objetos ya existen dentro del golem… `if
/// (g.Strategy.OnTheWay.IsActive)`, así déjalo, no como string"): one property per strategy — <c>g.Strategy.DoorByDoor</c> (the direct way,
/// active at birth) and <c>g.Strategy.OnTheWay</c> — never found by a name, never built from the journal; <c>Active</c> is the one the
/// golem goes by. One is active at a time: switching one on switches the other off; switching the other off leaves the direct way.</summary>
internal sealed class Strategies
{
    internal DoorByDoor DoorByDoor { get; } = new();
    internal OnTheWay OnTheWay { get; } = new();

    internal Strategies()
    {
        foreach (var strategy in All()) strategy.HeldBy(Choose, Drop);
        DoorByDoor.Active(true);
    }

    /// <summary>The strategy the golem goes by now.</summary>
    internal Navigation Active => All().First(s => s.IsActive);
    /// <summary>Both, the direct one first.</summary>
    internal IReadOnlyList<Navigation> All() => new Navigation[] { DoorByDoor, OnTheWay };

    private void Choose(Navigation chosen) { foreach (var s in All()) s.Active(ReferenceEquals(s, chosen)); }
    private void Drop(Navigation dropped) { foreach (var s in All()) s.Active(s.IsDefault); }
}
