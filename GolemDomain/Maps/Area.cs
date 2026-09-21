namespace GolemDomain.Maps;

/// <summary>
/// An area — Juan's ESPACIO: a named part of the map (a room, an aisle, a hall, a bay). It is information: it
/// has a name, it belongs to a map, and it tells the map which passages leave it — created once, and once its
/// neighbours exist too, told which of them it opens to, BY OBJECT (Juan, 21-sep-2026: "mejor pasar el objeto del
/// área"): <c>kitchen = map.Area('kitchen'); north = map.Area('north'); kitchen.DoorTo(north);</c>. It hands them out
/// as objects (<c>Neighbours()</c>, <c>Connects(other)</c>). That it is a rectangle standing at (0, 8) is not the
/// area's business but its <see cref="Layouts.Zone"/>'s, the area in the perspective of positions.
/// </summary>
internal class Area
{
    internal string Name { get; }
    internal Map Map { get; }

    internal Area(string name, Map map)
    {
        if (map == null) throw new GolemDomainException($"area '{name}' belongs to a map");
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("an area needs a name");
        Name = name;
        Map = map;
    }

    // ---- telling the map what leaves this area: the neighbour as an OBJECT, so both areas exist before the passage does ----

    /// <summary>A door from this area into another. Chainable.</summary>
    internal virtual Area DoorTo(Area area) {
        if (area == null) throw new GolemDomainException("Area.DoorTo: 'area' was not given"); Map.Door(this, area); return this; }

    /// <summary>The whole boundary with another area is open — no wall, no door. Chainable.</summary>
    internal virtual Area OpenTo(Area area) {
        if (area == null) throw new GolemDomainException("Area.OpenTo: 'area' was not given"); Map.Open(this, area); return this; }

    // ---- what the map disposes about it ----

    internal IReadOnlyList<Passage> Passages() => Map.PassagesOf(this).ToList();
    internal IReadOnlyList<Door> Doors() => Map.DoorsOf(this).ToList();
    internal IReadOnlyList<Opening> Openings() => Map.OpeningsOf(this).ToList();
    /// <summary>The areas one can pass into from here.</summary>
    internal IReadOnlyList<Area> Neighbours() => Map.Neighbours(this);
    /// <summary>Whether some passage joins this area to another.</summary>
    internal bool Connects(Area other)
    {
        if (other == null) throw new GolemDomainException("Area.Connects: 'other' was not given");
        return Map.Connects(this, other);
    }
}
