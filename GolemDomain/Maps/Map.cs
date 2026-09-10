namespace GolemDomain.Maps;

/// <summary>
/// A map — Juan's MAPA, the MAQUETA: the ABSTRACT contract of what a map DISPOSES, and nothing else. Which
/// areas there are, which passages join them (a door through a wall, an open stretch with no wall), whether two
/// areas connect, and the attributes a map knows (how wide a door is, how tall the walls are). Not one
/// coordinate. The concrete map the golem uses is a <see cref="Layouts.MapLayout"/>: the same ways of creating
/// areas, extended with dimensions and positions (Juan, 10-sep-2026: "la abstracta es la de mapa y la clase
/// concreta ya sería la del MapLayout: las mismas formas para crear áreas, pero extiende las funciones").
/// <para>Built by finding an object once and telling it what it is: <c>map.Area('kitchen').DoorTo('north').DoorTo('west')</c>
/// creates the area and chains its passages; <c>map.Find('kitchen')</c> finds it again later.</para>
/// <para>Origins: a map of named places joined by passages is a topological map (Kuipers &amp; Byun, Robotics and
/// Autonomous Systems 1991), the metric living in another layer. Robotics keeps both as layers of one map; here
/// the layer with positions is a subclass (Juan, 10-sep-2026: "la de layout hereda de mapa").</para>
/// </summary>
internal abstract class Map
{
    // Attributes the map knows about its passages and walls — information, not geometry: a door is this wide,
    // a wall this tall and (today) this thin, wherever a layout puts them.
    internal const double DoorWidth = 1.4;
    internal const double Height = 0.5;
    internal const double WallThickness = 0;

    protected readonly List<Area> areas = new();
    protected readonly List<Passage> passages = new();

    internal string Name { get; }

    protected Map(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new DomainException("a map needs a name");
        Name = name;
    }

    // ---- disposing: find the object once, then tell it what it is ----

    /// <summary>A new area of the map, by name, ready to chain its passages: <c>map.Area('kitchen').DoorTo('north')</c>.
    /// Refuses a repeated name.</summary>
    internal virtual Area Area(string name)
    {
        if (areas.Any(a => a.Name == name)) throw new DomainException($"area '{name}' already exists on map '{Name}'");
        var area = NewArea(name);
        areas.Add(area);
        return area;
    }

    /// <summary>The area with this name, to read it or keep telling it what it is. Refuses an unknown name.</summary>
    internal virtual Area Find(string name)
    {
        foreach (var a in areas) if (a.Name == name) return a;
        throw new DomainException($"unknown area '{name}' on map '{Name}'");
    }

    // The kind of area this map holds: each concrete map says (a zone, an area with a rectangle, in a MapLayout).
    protected abstract Area NewArea(string name);

    /// <summary>A door between two areas: a gap in the wall they share. Declaring it twice (from either side) is one door.</summary>
    internal Door Door(string a, string b)
    {
        var existing = passages.OfType<Door>().FirstOrDefault(d => d.Joins(a, b));
        if (existing != null) return existing;
        var door = new Door(a, b);
        passages.Add(door);
        return door;
    }

    /// <summary>An open stretch between two areas: the whole boundary they share is free. Declared once from either side.</summary>
    internal Opening Open(string a, string b)
    {
        var existing = passages.OfType<Opening>().FirstOrDefault(o => o.Joins(a, b));
        if (existing != null) return existing;
        var opening = new Opening(a, b);
        passages.Add(opening);
        return opening;
    }

    // ---- what it disposes ----

    internal int AreaCount => areas.Count;
    internal int PassageCount => passages.Count;
    internal IReadOnlyList<Area> Areas => areas;
    internal IReadOnlyList<Passage> Passages => passages;
    internal IEnumerable<Door> Doors => passages.OfType<Door>();
    internal IEnumerable<Opening> Openings => passages.OfType<Opening>();

    internal bool Knows(string area) => areas.Any(a => a.Name == area);

    /// <summary>The passages out of an area: its doors and its open stretches.</summary>
    internal IEnumerable<Passage> PassagesOf(string area) => passages.Where(p => p.Joins(area));
    internal IEnumerable<Door> DoorsOf(string area) => Doors.Where(d => d.Joins(area));
    internal IEnumerable<Opening> OpeningsOf(string area) => Openings.Where(o => o.Joins(area));

    /// <summary>The areas one can pass into from an area, through any passage.</summary>
    internal IEnumerable<string> Neighbours(string area) => PassagesOf(area).Select(p => p.OtherSide(area)).Distinct();

    /// <summary>Whether two areas connect through some passage — a door or an open stretch — as the map disposes it,
    /// whatever their shapes and however they lie on a plane.</summary>
    internal bool Connects(string a, string b) => passages.Any(p => p.Joins(a, b));

    internal bool HasDoorBetween(string a, string b) => Doors.Any(d => d.Joins(a, b));
    internal bool HasOpeningBetween(string a, string b) => Openings.Any(o => o.Joins(a, b));

    internal Door DoorBetween(string a, string b) =>
        Doors.FirstOrDefault(d => d.Joins(a, b)) ?? throw new DomainException($"no door between '{a}' and '{b}' on map '{Name}'");

    internal Opening OpeningBetween(string a, string b) =>
        Openings.FirstOrDefault(o => o.Joins(a, b)) ?? throw new DomainException($"no open stretch between '{a}' and '{b}' on map '{Name}'");

    /// <summary>The passage the journal names: kitchen/north for a door, north~center for an opening.</summary>
    internal Passage PassageNamed(string name) =>
        passages.FirstOrDefault(p => p.Name == name) ?? throw new DomainException($"no passage named '{name}' on map '{Name}'");

    internal bool HasPassageNamed(string name) => passages.Any(p => p.Name == name);
}
