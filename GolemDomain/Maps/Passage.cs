namespace GolemDomain.Maps;

/// <summary>
/// A way from one area into another. Two truths of the domain, hence two kinds (paper 01: variants, not a
/// flag): a <see cref="Door"/> is a gap in a wall the two areas share; an <see cref="Opening"/> is a shared
/// boundary with no wall at all. A passage is created by the names of its areas (a door may be declared from
/// the first area before its neighbour exists) and, once both exist, hands them out as the objects they are:
/// <c>AreaA</c>, <c>AreaB</c>, <c>OtherSide(area)</c>. It knows nothing of where it stands: that is the layout's.
/// </summary>
internal abstract class Passage
{
    private readonly Map map;

    /// <summary>The names the passage was declared with (the first may exist before the second does).</summary>
    internal string A { get; }
    internal string B { get; }

    internal Passage(Map map, string a, string b)
    {
        this.map = map ?? throw new GolemDomainException("a passage belongs to a map");
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) throw new GolemDomainException("a passage joins two named areas");
        if (a == b) throw new GolemDomainException($"a passage must join two different areas, not '{a}' twice");
        A = a;
        B = b;
    }

    /// <summary>The areas the passage joins, as objects. Both must exist on the map by now.</summary>
    internal Area AreaA => map.Find(A);
    internal Area AreaB => map.Find(B);

    internal bool Joins(Area area) => area != null && (A == area.Name || B == area.Name);
    internal bool Joins(Area one, Area other) => one != null && other != null && ((A == one.Name && B == other.Name) || (A == other.Name && B == one.Name));

    /// <summary>The area across from a given one of the two.</summary>
    internal Area OtherSide(Area area)
    {
        if (!Joins(area)) throw new GolemDomainException($"the passage {Name} does not join '{area?.Name}'");
        return A == area.Name ? AreaB : AreaA;
    }

    // Lookups by name, for the map's finds (the passage was declared by names).
    internal bool JoinsNamed(string area) => A == area || B == area;
    internal bool JoinsNamed(string one, string other) => (A == one && B == other) || (A == other && B == one);

    /// <summary>door or opening.</summary>
    internal abstract string Kind { get; }

    /// <summary>How the journal names the crossing: kitchen/north for a door, north~center for an opening.</summary>
    internal abstract string Name { get; }
}

/// <summary>
/// A door — Juan's PUERTA: a gap in the wall two areas share, as wide as the map says its doors are and as
/// tall as its walls. Where it stands on the wall, and its two jambs, are given by the layout.
/// </summary>
internal sealed class Door : Passage
{
    internal Door(Map map, string a, string b) : base(map, a, b) { }

    internal override string Kind => "door";
    internal override string Name => $"{A}/{B}";
    internal double Width => Map.DoorWidth;
    internal double Height => Map.Height;
}

/// <summary>
/// An opening — Juan's FRONTERA ABIERTA: the whole boundary two areas share is free, no wall and no door.
/// Which stretch of the plane that is, the layout says.
/// </summary>
internal sealed class Opening : Passage
{
    internal Opening(Map map, string a, string b) : base(map, a, b) { }

    internal override string Kind => "opening";
    internal override string Name => $"{A}~{B}";
}
