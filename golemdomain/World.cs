namespace GolemHost.Domain;

/// <summary>The golem's world: a square with hard walls, a wall margin, and maybe a rock — the single source of truth for where a body can stand and run.</summary>
internal sealed class World
{
    internal double Size { get; }
    internal double Margin { get; }
    internal Rock Rock { get; private set; } = Rock.None;

    internal World(double size, double margin)
    {
        if (size <= 0) throw new DomainException("the world needs a positive size");
        if (margin < 0 || margin * 2 >= size) throw new DomainException("the wall margin must fit inside the world");
        Size = size;
        Margin = margin;
    }

    internal void PlaceRock(double x, double y, double r) => Rock = new Rock(x, y, r);

    internal bool Contains(Waypoint at) =>
        at.X >= Margin && at.X <= Size - Margin && at.Y >= Margin && at.Y <= Size - Margin;

    internal bool CanStandAt(Waypoint at) => Contains(at) && !Rock.Blocks(at, Margin);

    /// <summary>The nearest point a body can stand on: clamped inside the walls, then pushed out of the rock.</summary>
    internal Waypoint NearestStandableTo(Waypoint at)
    {
        Waypoint inside = Clamp(at);
        return Rock.Blocks(inside, Margin) ? Clamp(Rock.PushOut(inside, Margin)) : inside;
    }

    internal bool RunCrossesRock(Waypoint from, Waypoint to) => Rock.IsCrossedBy(from, to, Margin);

    internal Waypoint DetourAround(Waypoint from, Waypoint to) =>
        Clamp(Rock.DetourBeside(from, to, Margin, clearance: 0.3));

    private Waypoint Clamp(Waypoint at) =>
        new(Math.Clamp(at.X, Margin, Size - Margin), Math.Clamp(at.Y, Margin, Size - Margin));
}
