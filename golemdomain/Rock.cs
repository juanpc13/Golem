namespace GolemHost.Domain;

/// <summary>An obstacle in the world. <see cref="None"/> is the world without one (null-object pair).</summary>
internal sealed class Rock
{
    internal static readonly Rock None = new(new Waypoint(0, 0), 0, exists: false);

    private readonly Waypoint center;
    private readonly double radius;
    private readonly bool exists;

    internal Rock(double x, double y, double r) : this(new Waypoint(x, y), r, exists: true)
    {
        if (r <= 0) throw new DomainException("a rock needs a positive radius");
    }

    private Rock(Waypoint center, double radius, bool exists)
    {
        this.center = center;
        this.radius = radius;
        this.exists = exists;
    }

    internal bool Exists() => exists;
    internal double X => center.X;
    internal double Y => center.Y;
    internal double Radius => radius;

    internal bool Blocks(Waypoint at, double margin) =>
        exists && center.DistanceTo(at) < radius + margin;

    /// <summary>The nearest point outside the rock's keep-out ring (radially out; any direction from the very center).</summary>
    internal Waypoint PushOut(Waypoint at, double margin)
    {
        double keep = radius + margin + 0.01; // a hair outside the ring, not on it
        double d = center.DistanceTo(at);
        double ux = d < 1e-6 ? 1 : (at.X - center.X) / d;
        double uy = d < 1e-6 ? 0 : (at.Y - center.Y) / d;
        return new Waypoint(center.X + ux * keep, center.Y + uy * keep);
    }

    internal bool IsCrossedBy(Waypoint from, Waypoint to, double margin) =>
        exists && ClosestPointOfRun(from, to).DistanceTo(center) < radius + margin;

    /// <summary>A waypoint beside the rock for a run that would otherwise cross it.</summary>
    internal Waypoint DetourBeside(Waypoint from, Waypoint to, double margin, double clearance)
    {
        Waypoint closest = ClosestPointOfRun(from, to);
        double vx = closest.X - center.X, vy = closest.Y - center.Y;
        double vlen = Math.Sqrt(vx * vx + vy * vy);
        if (vlen < 1e-6)
        {
            // the run goes through the center: pick the side perpendicular to it
            double dx = to.X - from.X, dy = to.Y - from.Y, len = Math.Sqrt(dx * dx + dy * dy);
            vx = -dy / len;
            vy = dx / len;
            vlen = 1;
        }
        double keep = radius + margin + clearance;
        return new Waypoint(center.X + vx / vlen * keep, center.Y + vy / vlen * keep);
    }

    private Waypoint ClosestPointOfRun(Waypoint from, Waypoint to)
    {
        double dx = to.X - from.X, dy = to.Y - from.Y;
        double len = Math.Sqrt(dx * dx + dy * dy);
        if (len < 1e-6) return from;
        double ux = dx / len, uy = dy / len;
        double t = Math.Clamp((center.X - from.X) * ux + (center.Y - from.Y) * uy, 0, len);
        return new Waypoint(from.X + ux * t, from.Y + uy * t);
    }
}
