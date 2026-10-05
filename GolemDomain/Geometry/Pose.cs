using NetTopologySuite.Mathematics;

namespace GolemDomain.Geometry;

/// <summary>
/// A pose: a position together with a heading — where a body stands and which way it faces, radians
/// counter-clockwise from +x. What a touch is estimated from (the point is the pose plus a radius along
/// the heading), and what a mark keeps as its normal: the thing lies beyond the point, in that direction.
/// Robotics' universal word for it (ROS geometry_msgs/Pose), adopted because it names what we already
/// carried apart.
/// </summary>
internal sealed class Pose : Position
{
    internal double Heading { get; }

    internal Pose(double x, double y, double heading) : base(x, y)
    {
        if (double.IsNaN(heading) || double.IsInfinity(heading)) throw new GolemDomainException("a pose needs a finite heading");
        Heading = heading;
    }

    /// <summary>The unit step along the heading.</summary>
    internal Position Forward
    {
        get { var unit = Vector2D.Create(1.0, 0.0).Rotate(Heading); return new Position(unit.X, unit.Y); }
    }

    /// <summary>How far ahead of this pose a position lies, along the heading (negative: behind).</summary>
    internal double Ahead(Position p)
    {
        if (p == null) throw new GolemDomainException("Pose.Ahead: 'p' was not given");
        return Vector2D.Create(AsCoordinate(), p.AsCoordinate()).Dot(Vector2D.Create(1.0, 0.0).Rotate(Heading));
    }
}
