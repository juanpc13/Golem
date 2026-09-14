using GolemDomain.Geometry;
using GolemDomain.Maps;

namespace GolemDomain.Layouts;

/// <summary>
/// A door realized on the plane: the door the map disposes (which areas it joins, how wide it is) together with
/// the point where the layout put it on the shared wall — and from the two, its jambs. Crossed straight: line up
/// in front, leave behind.
/// </summary>
internal sealed class PlacedDoor
{
    private readonly MapLayout layout;

    internal Door Door { get; }
    internal Position At { get; }

    internal PlacedDoor(Door door, Position at, MapLayout layout)
    {
        if (door == null) throw new GolemDomainException("a placed door realizes a door of the map");
        if (at == null) throw new GolemDomainException($"the door {door.Name} needs its point");
        if (layout == null) throw new GolemDomainException($"the door {door.Name} is placed by a layout");
        Door = door;
        At = at;
        this.layout = layout;
    }

    internal string Name => Door.Name;
    internal Area A => Door.AreaA;
    internal Area B => Door.AreaB;
    internal double Width => Door.Width;
    internal double Height => Door.Height;

    /// <summary>The two jambs: locations on the wall, half a width to each side of the point, along the wall.</summary>
    internal IReadOnlyList<Location> Jambs()
    {
        var step = layout.StepInto(Door, Door.AreaB);        // across the wall
        double ax = -step.Y, ay = step.X;                   // along it
        return new[]
        {
            new Location($"{Name} jamb", At.X - ax * Width / 2, At.Y - ay * Width / 2),
            new Location($"{Name} jamb", At.X + ax * Width / 2, At.Y + ay * Width / 2),
        };
    }

    /// <summary>A unit step through the door into one of its two areas, from the other.</summary>
    internal Position StepInto(Area side)
    {
        if (side == null) throw new GolemDomainException("PlacedDoor.StepInto: 'side' was not given");
        return layout.StepInto(Door, side);
    }
}

/// <summary>A door as one zone sees it: the area across, where the door stands, how wide it is. Read, never written.</summary>
internal sealed class Doorway
{
    internal Area Across { get; }
    internal string To => Across.Name;
    internal Position At { get; }
    internal double Width { get; }

    internal Doorway(Area across, Position at, double width)
    {
        if (across == null) throw new GolemDomainException("Doorway.Doorway: 'across' was not given");
        if (at == null) throw new GolemDomainException("Doorway.Doorway: 'at' was not given");
        Across = across;
        At = at;
        Width = width;
    }
}

/// <summary>An open stretch as one zone sees it: the area across. Read, never written.</summary>
internal sealed class OpenSide
{
    internal Area Across { get; }
    internal string To => Across.Name;

    internal OpenSide(Area across)
    {
        if (across == null) throw new GolemDomainException("OpenSide.OpenSide: 'across' was not given");
        Across = across;
    }
}
