namespace GolemDomain.Maps;

/// <summary>
/// A map — Juan's MAPA, the MAQUETA: the ABSTRACT contract of what a map DISPOSES, and nothing else. Which
/// areas there are, which passages join them (a door through a wall, an open stretch with no wall), whether two
/// areas connect, and the attributes a map knows (how wide a door is, how tall the walls are). Not one
/// coordinate. The concrete map the golem uses is a <see cref="Layouts.MapLayout"/>: the same ways of creating
/// areas, extended with dimensions and positions (Juan, 10-sep-2026: "la abstracta es la de mapa y la clase
/// concreta ya sería la del MapLayout: las mismas formas para crear áreas, pero extiende las funciones").
/// <para>Names enter only where an object is CREATED or FOUND — <c>Area('kitchen')</c>, <c>Find('kitchen')</c>,
/// <c>FindDoor('kitchen', 'north')</c>; every other method takes the objects themselves: <c>Connects(kitchen, north)</c>,
/// <c>DoorBetween(kitchen, north)</c>, <c>Neighbours(kitchen)</c> (Juan, 10-sep: "evitemos parámetros primitivos si
/// tenemos las instancias reales").</para>
/// <para>Origins: a map of named places joined by passages is a topological map (Kuipers &amp; Byun, Robotics and
/// Autonomous Systems 1991), the metric living in another layer. Robotics keeps both as layers of one map; here
/// the layer with positions is the concrete subclass.</para>
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
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("a map needs a name");
        Name = name;
    }

    // ---- creating: by name, once ----

    /// <summary>A new area of the map, by name, ready to chain its passages: <c>map.Area('kitchen').DoorTo('north')</c>.
    /// Refuses a repeated name.</summary>
    internal virtual Area Area(string name)
    {
        if (areas.Any(a => a.Name == name)) throw new GolemDomainException($"area '{name}' already exists on map '{Name}'");
        var area = NewArea(name);
        areas.Add(area);
        return area;
    }

    // The kind of area this map holds: each concrete map says (a zone, an area with a rectangle, in a MapLayout).
    protected abstract Area NewArea(string name);

    /// <summary>A door between two areas: a gap in the wall they share. Declaring it twice (from either side) is one door.</summary>
    internal Door Door(Area a, Area b) => Door(Named(a), Named(b));

    /// <summary>A door between two areas by name — the second may not exist yet (a door is declared from the first
    /// area charted). Declaring it twice is one door.</summary>
    internal Door Door(string a, string b)
    {
        var existing = passages.OfType<Door>().FirstOrDefault(d => d.JoinsNamed(a, b));
        if (existing != null) return existing;
        var door = new Door(this, a, b);
        passages.Add(door);
        return door;
    }

    /// <summary>An open stretch between two areas: the whole boundary they share is free. Declared once from either side.</summary>
    internal Opening Open(Area a, Area b) => Open(Named(a), Named(b));

    /// <summary>An open stretch between two areas by name — the second may not exist yet.</summary>
    internal Opening Open(string a, string b)
    {
        var existing = passages.OfType<Opening>().FirstOrDefault(o => o.JoinsNamed(a, b));
        if (existing != null) return existing;
        var opening = new Opening(this, a, b);
        passages.Add(opening);
        return opening;
    }

    // ---- finding: by name, once ----

    internal bool Knows(string area) => areas.Any(a => a.Name == area);

    /// <summary>The area with this name, to read it or keep telling it what it is. Refuses an unknown name.</summary>
    internal virtual Area Find(string name)
    {
        foreach (var a in areas) if (a.Name == name) return a;
        throw new GolemDomainException($"unknown area '{name}' on map '{Name}'");
    }

    internal Door FindDoor(string a, string b) =>
        Doors.FirstOrDefault(d => d.JoinsNamed(a, b)) ?? throw new GolemDomainException($"no door between '{a}' and '{b}' on map '{Name}'");

    internal Opening FindOpening(string a, string b) =>
        Openings.FirstOrDefault(o => o.JoinsNamed(a, b)) ?? throw new GolemDomainException($"no open stretch between '{a}' and '{b}' on map '{Name}'");

    /// <summary>The passage the journal names: kitchen/north for a door, north~center for an opening.</summary>
    internal Passage FindPassage(string name) =>
        passages.FirstOrDefault(p => p.Name == name) ?? throw new GolemDomainException($"no passage named '{name}' on map '{Name}'");

    internal bool KnowsPassage(string name) => passages.Any(p => p.Name == name);

    // ---- what it disposes: asked with the objects ----

    internal int AreaCount => areas.Count;
    internal int PassageCount => passages.Count;
    internal IReadOnlyList<Area> Areas => areas;
    internal IReadOnlyList<Passage> Passages => passages;
    internal IEnumerable<Door> Doors => passages.OfType<Door>();
    internal IEnumerable<Opening> Openings => passages.OfType<Opening>();

    /// <summary>The passages out of an area: its doors and its open stretches.</summary>
    internal IEnumerable<Passage> PassagesOf(Area area) => passages.Where(p => p.Joins(area));
    internal IEnumerable<Door> DoorsOf(Area area) => Doors.Where(d => d.Joins(area));
    internal IEnumerable<Opening> OpeningsOf(Area area) => Openings.Where(o => o.Joins(area));

    /// <summary>The areas one can pass into from an area, through any passage — the ones that exist by now.</summary>
    internal IReadOnlyList<Area> Neighbours(Area area) =>
        PassagesOf(area).Select(p => p.A == area.Name ? p.B : p.A).Distinct().Where(Knows).Select(Find).ToList();
        
    internal bool Connects(Area a, Area b) => passages.Any(p => p.Joins(a, b));

    internal bool HasDoorBetween(Area a, Area b) => Doors.Any(d => d.Joins(a, b));
    internal bool HasOpeningBetween(Area a, Area b) => Openings.Any(o => o.Joins(a, b));

    internal Door DoorBetween(Area a, Area b) =>
        Doors.FirstOrDefault(d => d.Joins(a, b)) ?? throw new GolemDomainException($"no door between '{Named(a)}' and '{Named(b)}' on map '{Name}'");

    internal Opening OpeningBetween(Area a, Area b) =>
        Openings.FirstOrDefault(o => o.Joins(a, b)) ?? throw new GolemDomainException($"no open stretch between '{Named(a)}' and '{Named(b)}' on map '{Name}'");

    private static string Named(Area a) => a?.Name ?? throw new GolemDomainException("an area is needed, not nothing");
}
