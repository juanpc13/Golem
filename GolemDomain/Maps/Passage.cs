namespace GolemDomain.Maps;

/// <summary>
/// A way from one area into another. Two truths of the domain, hence two kinds (paper 01: variants, not a
/// flag): a <see cref="Door"/> is a gap in a wall the two areas share; an <see cref="Opening"/> is a shared
/// boundary with no wall at all. A passage names its areas (a door may be declared from the first area
/// before its neighbour exists) and knows nothing of where it stands: that is the layout's.
/// </summary>
internal abstract class Passage
{
    internal string A { get; }
    internal string B { get; }

    internal Passage(string a, string b)
    {
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) throw new DomainException("a passage joins two named areas");
        if (a == b) throw new DomainException($"a passage must join two different areas, not '{a}' twice");
        A = a;
        B = b;
    }

    internal bool Joins(string area) => A == area || B == area;
    internal bool Joins(string one, string other) => (A == one && B == other) || (A == other && B == one);
    internal string OtherSide(string area) => A == area ? B : A;

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
    internal Door(string a, string b) : base(a, b) { }

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
    internal Opening(string a, string b) : base(a, b) { }

    internal override string Kind => "opening";
    internal override string Name => $"{A}~{B}";
}
