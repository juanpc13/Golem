namespace GolemDomain.Geometry;

/// <summary>
/// A location — Juan's UBICACIÓN: a position where something stands that the map can name. The corner of a
/// place, the jamb of a door, the mark a body left where it touched something. The robot stands at a
/// position; the moment that position matters to the map, it is a location.
/// </summary>
internal class Location : Position
{
    /// <summary>What stands here, as the map calls it: "kitchen ne", "kitchen/north jamb", "mark".</summary>
    internal string Label { get; }

    internal Location(string label, double x, double y) : base(x, y)
    {
        if (string.IsNullOrWhiteSpace(label)) throw new DomainException("a location needs a label: what stands there");
        Label = label;
    }
}
