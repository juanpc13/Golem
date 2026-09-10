using GolemHost.Domain.Geometry;

namespace GolemHost.Domain.Plans;

/// <summary>A door as one place sees it: the place across, where the door stands, how wide it is. Read, never written.</summary>
internal sealed class Doorway
{
    internal string To { get; }
    internal Position At { get; }
    internal double Width { get; }

    internal Doorway(string to, Position at, double width)
    {
        To = to;
        At = at;
        Width = width;
    }
}

/// <summary>An open boundary as one place sees it: the place across. Read, never written.</summary>
internal sealed class Opening
{
    internal string To { get; }

    internal Opening(string to) => To = to;
}
