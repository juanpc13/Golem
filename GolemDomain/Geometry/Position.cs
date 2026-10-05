using NetTopologySuite.Algorithm;
using NetTopologySuite.Mathematics;
using Coordinate = NetTopologySuite.Geometries.Coordinate;
using CoordinateZ = NetTopologySuite.Geometries.CoordinateZ;

namespace GolemDomain.Geometry;

/// <summary>
/// A position: the coordinate (x, y, z) in the world's Euclidean space — Juan's POSICIÓN. Everything the robot's
/// motion is described with is a position: where it stands (telemetry, a query parameter), where it is sent,
/// where it touched something. The bodies of this spike live on the floor, so <c>Position(4.0, 9.5)</c> is the
/// point at height zero; <c>Position(4.0, 9.5, 1.2)</c> keeps the third dimension for the day a body climbs or a
/// thing is touched above the floor (Juan, 14-sep-2026). Distance is measured in space; the layout, which lays walls
/// and zones on the plane, reads x and y only. Open, because a <see cref="Location"/> is a position that means something.
/// The arithmetic is NetTopologySuite's (ajuste 89, 5-oct-2026; Juan: "no que nosotros hagamos los cálculos"): the domain
/// names the operation, the library computes it.
/// </summary>
internal class Position
{
    internal double X { get; }
    internal double Y { get; }
    /// <summary>The height above the floor; zero for everything that stands on it.</summary>
    internal double Z { get; }

    /// <summary>A point on the floor: height zero.</summary>
    internal Position(double x, double y) : this(x, y, 0.0) { }

    /// <summary>A point in space.</summary>
    internal Position(double x, double y, double z)
    {
        if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(z)) throw new GolemDomainException("a position needs numbers for its coordinates");
        X = x;
        Y = y;
        Z = z;
    }

    /// <summary>The position a library coordinate stands for (a plane coordinate has no height: the floor).</summary>
    internal static Position Of(Coordinate c)
    {
        if (c == null) throw new GolemDomainException("Position.Of: 'c' was not given");
        return new Position(c.X, c.Y, double.IsNaN(c.Z) ? 0.0 : c.Z);
    }

    /// <summary>This position as the library's coordinate, height included.</summary>
    internal Coordinate AsCoordinate() => new CoordinateZ(X, Y, Z);

    /// <summary>The straight distance to another position, in space (on the floor, the plane's).</summary>
    internal double DistanceTo(Position other)
    {
        if (other == null) throw new GolemDomainException("Position.DistanceTo: 'other' was not given");
        return new Vector3D(AsCoordinate(), other.AsCoordinate()).Length();
    }

    /// <summary>The heading from here toward another position: radians, counter-clockwise from +x.</summary>
    internal double HeadingTo(Position other)
    {
        if (other == null) throw new GolemDomainException("Position.HeadingTo: 'other' was not given");
        return AngleUtility.Angle(AsCoordinate(), other.AsCoordinate());
    }

    /// <summary>The position reached by running this far along a heading, at the same height.</summary>
    internal Position Along(double heading, double distance)
    {
        var reached = Vector2D.Create(distance, 0.0).Rotate(heading).Translate(AsCoordinate());
        return new Position(reached.X, reached.Y, Z);
    }

    /// <summary>The position displaced by (dx, dy), at the same height.</summary>
    internal Position Moved(double dx, double dy) => new(X + dx, Y + dy, Z);
}
