namespace GolemHost.Domain;

/// <summary>A way from one place into another: a door at a point, or an open boundary.</summary>
internal sealed class Passage
{
    internal string A { get; }
    internal string B { get; }
    /// <summary>Where a door stands; an opening has no single point (the crossing is wherever the road meets the boundary).</summary>
    internal Waypoint At { get; }
    internal bool IsDoor => At != null;

    internal Passage(string a, string b, Waypoint at)
    {
        A = a;
        B = b;
        At = at;
    }

    internal bool Joins(string place) => A == place || B == place;
    internal bool Joins(string one, string other) => (A == one && B == other) || (A == other && B == one);
    internal string OtherSide(string place) => A == place ? B : A;

    /// <summary>How the journal names the crossing: kitchen/hall for a door, living~hall for an opening.</summary>
    internal string Name => IsDoor ? $"{A}/{B}" : $"{A}~{B}";
}
