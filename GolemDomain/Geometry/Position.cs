namespace GolemHost.Domain.Geometry;

/// <summary>
/// A position: the coordinate (x, y) on the world's Euclidean plane — Juan's POSICIÓN. Everything the robot's
/// motion is described with is a position: where it stands (telemetry, a query parameter), where it is sent,
/// where it touched something. Open, because a <see cref="Location"/> is a position that means something.
/// </summary>
internal class Position
{
    internal double X { get; }
    internal double Y { get; }

    internal Position(double x, double y)
    {
        X = x;
        Y = y;
    }

    internal double DistanceTo(Position other) =>
        Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));

    /// <summary>The heading from here toward another position: radians, counter-clockwise from +x.</summary>
    internal double HeadingTo(Position other) => Math.Atan2(other.Y - Y, other.X - X);

    /// <summary>The position reached by running this far along a heading.</summary>
    internal Position Along(double heading, double distance) =>
        new(X + distance * Math.Cos(heading), Y + distance * Math.Sin(heading));

    /// <summary>The position displaced by (dx, dy).</summary>
    internal Position Moved(double dx, double dy) => new(X + dx, Y + dy);
}
