namespace GolemDomain.Units;

/// <summary>
/// A length — Juan's MAGNITUD, 11-sep-2026: "una clase que nos especifique qué son esos números, tipo las unidades
/// de medida". The magnitude is abstract; its UNIT is the concrete class the journal constructs, so a value says what
/// it is where it is written: <c>radius = Meters(0.25)</c>. Every length reads in the base unit of the International
/// System (<see cref="InMeters"/>), whatever unit it was written in. A length is never negative.
/// </summary>
internal abstract class Length
{
    /// <summary>The length in metres, the SI base unit.</summary>
    internal double InMeters { get; }

    protected Length(double meters)
    {
        if (double.IsNaN(meters) || double.IsInfinity(meters)) throw new GolemDomainException("a length needs a number");
        if (meters < 0) throw new GolemDomainException("a length cannot be negative");
        InMeters = meters;
    }

    internal bool IsZero => InMeters == 0;
}

/// <summary>A length written in metres: <c>Meters(0.25)</c>.</summary>
internal sealed class Meters : Length
{
    internal Meters(double meters) : base(meters) { }
}

/// <summary>A length written in centimetres: <c>Centimeters(25.0)</c> is a quarter of a metre.</summary>
internal sealed class Centimeters : Length
{
    internal Centimeters(double centimeters) : base(centimeters / 100.0) { }
}
