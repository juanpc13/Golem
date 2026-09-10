using GolemDomain.Geometry;

namespace GolemDomain.Layouts;

/// <summary>
/// The catalog of maps with a name, hardcoded with the same class the golem's release builds — each area found
/// once and told what it is, in one train. A named map reaches a journal as the release it renders
/// (<see cref="MapLayout.AsRelease"/>): the journal keeps the map, the code keeps the recipe. All on an 11 × 11
/// floor, like the warehouse the world builds.
/// </summary>
internal static class Catalog
{
    internal const string WarehouseName = "warehouse";
    internal const string CrossCorridorsName = "cross-corridors";
    internal const string RingCorridorName = "ring-corridor";

    internal static string[] Names() => new[] { WarehouseName, CrossCorridorsName, RingCorridorName };

    internal static MapLayout Named(string name)
    {
        switch (name)
        {
            case WarehouseName: return Warehouse();
            case CrossCorridorsName: return CrossCorridors();
            case RingCorridorName: return RingCorridor();
        }
        throw new DomainException($"no map named '{name}': {string.Join(", ", Names())}");
    }

    /// <summary>The warehouse the world builds (sim/world/plan.json) and the golems carry: a ring of nine areas
    /// around two solid blocks, a wide shortcut through the middle (openings) and two long narrow corridors, so
    /// the shortest road shows. (Juan, 10-sep-2026: "el mapa de bodega con todas esas distribuciones".)</summary>
    internal static MapLayout Warehouse()
    {
        var l = new MapLayout(WarehouseName);
        l.Area("kitchen").At(P(0, 8)).Size(4, 3).DoorAt("north", P(4, 9.5)).DoorAt("west", P(0.75, 8));
        l.Area("north").At(P(4, 8)).Size(3, 3).DoorAt("storage", P(7, 9.5)).OpenTo("center");
        l.Area("storage").At(P(7, 8)).Size(4, 3).DoorAt("east", P(10.25, 8));
        l.Area("west").At(P(0, 3)).Size(1.5, 5).DoorAt("living", P(0.75, 3));
        l.Area("center").At(P(4, 3)).Size(3, 5).OpenTo("south");
        l.Area("east").At(P(9.5, 3)).Size(1.5, 5).DoorAt("garage", P(10.25, 3));
        l.Area("living").At(P(0, 0)).Size(4, 3).DoorAt("south", P(4, 1.5));
        l.Area("south").At(P(4, 0)).Size(3, 3).DoorAt("garage", P(7, 1.5));
        l.Area("garage").At(P(7, 0)).Size(4, 3);
        return l;
    }

    /// <summary>Four rooms in the corners, separated by two aisles that cross in the middle: each room opens to
    /// its two aisles, and the aisles meet at an open crossing.</summary>
    internal static MapLayout CrossCorridors()
    {
        var l = new MapLayout(CrossCorridorsName);
        l.Area("northwest").At(P(0, 6.25)).Size(4.75, 4.75).DoorAt("north-aisle", P(4.75, 8.625)).DoorAt("west-aisle", P(2.375, 6.25));
        l.Area("northeast").At(P(6.25, 6.25)).Size(4.75, 4.75).DoorAt("north-aisle", P(6.25, 8.625)).DoorAt("east-aisle", P(8.625, 6.25));
        l.Area("southwest").At(P(0, 0)).Size(4.75, 4.75).DoorAt("south-aisle", P(4.75, 2.375)).DoorAt("west-aisle", P(2.375, 4.75));
        l.Area("southeast").At(P(6.25, 0)).Size(4.75, 4.75).DoorAt("south-aisle", P(6.25, 2.375)).DoorAt("east-aisle", P(8.625, 4.75));
        l.Area("north-aisle").At(P(4.75, 6.25)).Size(1.5, 4.75);
        l.Area("south-aisle").At(P(4.75, 0)).Size(1.5, 4.75);
        l.Area("west-aisle").At(P(0, 4.75)).Size(4.75, 1.5);
        l.Area("east-aisle").At(P(6.25, 4.75)).Size(4.75, 1.5);
        l.Area("crossing").At(P(4.75, 4.75)).Size(1.5, 1.5).OpenTo("north-aisle").OpenTo("south-aisle").OpenTo("west-aisle").OpenTo("east-aisle");
        return l;
    }

    /// <summary>Four rooms together in the middle, each with a door to its two neighbours, surrounded by one
    /// corridor that runs all the way round (four open stretches) with a door from every room into it.</summary>
    internal static MapLayout RingCorridor()
    {
        var l = new MapLayout(RingCorridorName);
        l.Area("northwest").At(P(1.5, 5.5)).Size(4, 4).DoorAt("northeast", P(5.5, 7.5)).DoorAt("southwest", P(3.5, 5.5)).DoorAt("west-corridor", P(1.5, 7.5)).DoorAt("north-corridor", P(3.5, 9.5));
        l.Area("northeast").At(P(5.5, 5.5)).Size(4, 4).DoorAt("southeast", P(7.5, 5.5)).DoorAt("east-corridor", P(9.5, 7.5)).DoorAt("north-corridor", P(7.5, 9.5));
        l.Area("southwest").At(P(1.5, 1.5)).Size(4, 4).DoorAt("southeast", P(5.5, 3.5)).DoorAt("west-corridor", P(1.5, 3.5)).DoorAt("south-corridor", P(3.5, 1.5));
        l.Area("southeast").At(P(5.5, 1.5)).Size(4, 4).DoorAt("east-corridor", P(9.5, 3.5)).DoorAt("south-corridor", P(7.5, 1.5));
        l.Area("west-corridor").At(P(0, 0)).Size(1.5, 11).OpenTo("north-corridor").OpenTo("south-corridor");
        l.Area("east-corridor").At(P(9.5, 0)).Size(1.5, 11).OpenTo("north-corridor").OpenTo("south-corridor");
        l.Area("north-corridor").At(P(1.5, 9.5)).Size(8, 1.5);
        l.Area("south-corridor").At(P(1.5, 0)).Size(8, 1.5);
        return l;
    }

    private static Position P(double x, double y) => new(x, y);
}
