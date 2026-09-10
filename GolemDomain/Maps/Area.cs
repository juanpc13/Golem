namespace GolemDomain.Maps;

/// <summary>
/// An area — Juan's ESPACIO: a named part of the map (a room, an aisle, a hall, a bay). It is information: it
/// has a name, it belongs to a map, and it tells the map which passages leave it — found once and told in one
/// train: <c>map.Area('kitchen').DoorTo('north').DoorTo('west')</c>. That it is a rectangle standing at (0, 8)
/// is not the area's business but its <see cref="Layouts.Zone"/>'s, the area in the perspective of positions.
/// </summary>
internal class Area
{
    internal string Name { get; }
    internal Map Map { get; }

    internal Area(string name, Map map)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("an area needs a name");
        if (map == null) throw new DomainException($"area '{name}' belongs to a map");
        Name = name;
        Map = map;
    }

    // ---- telling the map what leaves this area (chainable) ----

    /// <summary>A door from this area into another (declared from either side, it is one door). Chainable.</summary>
    internal virtual Area DoorTo(string area)
    {
        Map.Door(Name, area);
        return this;
    }

    /// <summary>The whole boundary with another area is open — no wall, no door. Chainable.</summary>
    internal virtual Area OpenTo(string area)
    {
        Map.Open(Name, area);
        return this;
    }

    // ---- what the map disposes about it ----

    internal IReadOnlyList<Passage> Passages() => Map.PassagesOf(Name).ToList();
    internal IReadOnlyList<Door> Doors() => Map.DoorsOf(Name).ToList();
    internal IReadOnlyList<Opening> Openings() => Map.OpeningsOf(Name).ToList();
    /// <summary>The names of the areas one can pass into from here.</summary>
    internal IReadOnlyList<string> Neighbours() => Map.Neighbours(Name).ToList();
}
