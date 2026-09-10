namespace GolemHost.Domain.Plans;

/// <summary>
/// The catalog of floor plans with a name, hardcoded with the same classes the golem charts with. A named plan
/// reaches a journal as the release text it renders (<see cref="FloorPlan.AsRelease"/>): the journal keeps the
/// map, the code keeps the recipe. All on an 11 × 11 floor, like the arena the world builds.
/// </summary>
internal static class FloorPlans
{
    internal const string ArenaName = "arena";
    internal const string CrossCorridorsName = "cross-corridors";
    internal const string RingCorridorName = "ring-corridor";

    internal static string[] Names() => new[] { ArenaName, CrossCorridorsName, RingCorridorName };

    internal static FloorPlan Named(string name)
    {
        switch (name)
        {
            case ArenaName: return Arena();
            case CrossCorridorsName: return CrossCorridors();
            case RingCorridorName: return RingCorridor();
        }
        throw new DomainException($"no floor plan named '{name}': {string.Join(", ", Names())}");
    }

    /// <summary>The arena the world builds (sim/world/plan.json) and the golems carry as map_v1: a ring of nine
    /// places around two solid blocks, a wide shortcut through the middle (open boundaries) and two long narrow
    /// corridors, so the shortest road shows.</summary>
    internal static FloorPlan Arena()
    {
        var plan = new FloorPlan();
        plan.AddPlace("kitchen", 0, 8, 4, 3).DoorTo("north", 4, 9.5).DoorTo("west", 0.75, 8);
        plan.AddPlace("north", 4, 8, 3, 3).DoorTo("storage", 7, 9.5).OpenTo("center");
        plan.AddPlace("storage", 7, 8, 4, 3).DoorTo("east", 10.25, 8);
        plan.AddPlace("west", 0, 3, 1.5, 5).DoorTo("living", 0.75, 3);
        plan.AddPlace("center", 4, 3, 3, 5).OpenTo("south");
        plan.AddPlace("east", 9.5, 3, 1.5, 5).DoorTo("garage", 10.25, 3);
        plan.AddPlace("living", 0, 0, 4, 3).DoorTo("south", 4, 1.5);
        plan.AddPlace("south", 4, 0, 3, 3).DoorTo("garage", 7, 1.5);
        plan.AddPlace("garage", 7, 0, 4, 3);
        return plan;
    }

    /// <summary>Four rooms in the corners, separated by two aisles that cross in the middle: each room opens to
    /// its two aisles, and the aisles meet at an open crossing.</summary>
    internal static FloorPlan CrossCorridors()
    {
        var plan = new FloorPlan();
        plan.AddPlace("northwest", 0, 6.25, 4.75, 4.75).DoorTo("north-aisle", 4.75, 8.625).DoorTo("west-aisle", 2.375, 6.25);
        plan.AddPlace("northeast", 6.25, 6.25, 4.75, 4.75).DoorTo("north-aisle", 6.25, 8.625).DoorTo("east-aisle", 8.625, 6.25);
        plan.AddPlace("southwest", 0, 0, 4.75, 4.75).DoorTo("south-aisle", 4.75, 2.375).DoorTo("west-aisle", 2.375, 4.75);
        plan.AddPlace("southeast", 6.25, 0, 4.75, 4.75).DoorTo("south-aisle", 6.25, 2.375).DoorTo("east-aisle", 8.625, 4.75);
        plan.AddPlace("north-aisle", 4.75, 6.25, 1.5, 4.75);
        plan.AddPlace("south-aisle", 4.75, 0, 1.5, 4.75);
        plan.AddPlace("west-aisle", 0, 4.75, 4.75, 1.5);
        plan.AddPlace("east-aisle", 6.25, 4.75, 4.75, 1.5);
        plan.AddPlace("crossing", 4.75, 4.75, 1.5, 1.5).OpenTo("north-aisle").OpenTo("south-aisle").OpenTo("west-aisle").OpenTo("east-aisle");
        return plan;
    }

    /// <summary>Four rooms together in the middle, each with a door to its two neighbours, surrounded by one
    /// corridor that runs all the way round (four open stretches) with a door from every room into it.</summary>
    internal static FloorPlan RingCorridor()
    {
        var plan = new FloorPlan();
        plan.AddPlace("northwest", 1.5, 5.5, 4, 4).DoorTo("northeast", 5.5, 7.5).DoorTo("southwest", 3.5, 5.5).DoorTo("west-corridor", 1.5, 7.5).DoorTo("north-corridor", 3.5, 9.5);
        plan.AddPlace("northeast", 5.5, 5.5, 4, 4).DoorTo("southeast", 7.5, 5.5).DoorTo("east-corridor", 9.5, 7.5).DoorTo("north-corridor", 7.5, 9.5);
        plan.AddPlace("southwest", 1.5, 1.5, 4, 4).DoorTo("southeast", 5.5, 3.5).DoorTo("west-corridor", 1.5, 3.5).DoorTo("south-corridor", 3.5, 1.5);
        plan.AddPlace("southeast", 5.5, 1.5, 4, 4).DoorTo("east-corridor", 9.5, 3.5).DoorTo("south-corridor", 7.5, 1.5);
        plan.AddPlace("west-corridor", 0, 0, 1.5, 11).OpenTo("north-corridor").OpenTo("south-corridor");
        plan.AddPlace("east-corridor", 9.5, 0, 1.5, 11).OpenTo("north-corridor").OpenTo("south-corridor");
        plan.AddPlace("north-corridor", 1.5, 9.5, 8, 1.5);
        plan.AddPlace("south-corridor", 1.5, 0, 8, 1.5);
        return plan;
    }
}
