namespace GolemHost.Domain;

/// <summary>A point of the world a golem can be sent to.</summary>
internal sealed class Waypoint
{
    internal double X { get; }
    internal double Y { get; }

    internal Waypoint(double x, double y)
    {
        X = x;
        Y = y;
    }

    internal double DistanceTo(Waypoint other) =>
        Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));
}
