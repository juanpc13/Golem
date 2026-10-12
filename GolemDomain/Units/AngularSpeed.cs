namespace GolemDomain.Units;

/// <summary>An ANGULAR speed: how much angle per unit of time — how fast a body turns in place (propuesta 111, 12-oct-2026; Juan:
/// "¿podríamos aumentar la velocidad del golem para giros y avanzar mucho más rápido?"). Abstract magnitude, concrete unit; reads in
/// radians per second. Never negative: the sense of a turn is the order's business (turnLeft, turnRight), not the rate's.</summary>
internal abstract class AngularSpeed
{
    /// <summary>The rate in radians per second, the SI derived unit.</summary>
    internal double InRadiansPerSecond { get; }

    protected AngularSpeed(double radiansPerSecond)
    {
        if (double.IsNaN(radiansPerSecond) || double.IsInfinity(radiansPerSecond)) throw new GolemDomainException("an angular speed needs a number");
        if (radiansPerSecond < 0) throw new GolemDomainException("an angular speed cannot be negative");
        InRadiansPerSecond = radiansPerSecond;
    }

    internal bool IsZero => InRadiansPerSecond == 0;

    /// <summary>How long an angle takes at this rate.</summary>
    internal Duration TimeFor(Angle angle)
    {
        if (angle == null) throw new GolemDomainException("an angular speed measures the time for an angle");
        if (IsZero) throw new GolemDomainException("nothing is turned at no rate");
        return new Seconds(System.Math.Abs(angle.InRadians) / InRadiansPerSecond);
    }
}

/// <summary>An angular speed written in radians per second: <c>RadiansPerSecond(1.5)</c>.</summary>
internal sealed class RadiansPerSecond : AngularSpeed
{
    internal RadiansPerSecond(double radiansPerSecond) : base(radiansPerSecond) { }
}
