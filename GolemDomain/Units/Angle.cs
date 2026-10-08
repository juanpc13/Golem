using NetTopologySuite.Algorithm;

namespace GolemDomain.Units;

/// <summary>
/// An ANGLE — a magnitude like the length (Juan, 11-sep-2026: "una clase que nos especifique qué son esos números"), so a turn is written
/// as what it is where it is written: <c>turn = Degrees(45.0)</c> (propuesta 99, 8-oct-2026: the orientation of a formation the warden gives
/// the golem). The magnitude is abstract; its UNIT is the concrete class the journal constructs. It reads in degrees and in radians, the
/// conversion the library's (<see cref="AngleUtility"/>), never a formula of ours. An angle may be negative: a turn the other way.
/// </summary>
internal abstract class Angle
{
    /// <summary>The angle in degrees, counter-clockwise.</summary>
    internal double InDegrees { get; }

    /// <summary>The same angle in radians.</summary>
    internal double InRadians => AngleUtility.ToRadians(InDegrees);

    protected Angle(double degrees)
    {
        if (double.IsNaN(degrees) || double.IsInfinity(degrees)) throw new GolemDomainException("an angle needs a number");
        InDegrees = degrees;
    }
}

/// <summary>An angle written in degrees, counter-clockwise: <c>Degrees(45.0)</c>.</summary>
internal sealed class Degrees : Angle
{
    internal Degrees(double degrees) : base(degrees) { }
}
