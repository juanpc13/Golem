namespace GolemDomain.Units;

/// <summary>An acceleration: how much speed per unit of time. Abstract magnitude, concrete unit; reads in metres per
/// second squared. Defined with the other magnitudes for the body that will declare one (11-sep-2026); no module
/// takes it yet.</summary>
internal abstract class Acceleration
{
    /// <summary>The acceleration in metres per second squared, the SI derived unit.</summary>
    internal double InMetersPerSecondSquared { get; }

    protected Acceleration(double metersPerSecondSquared)
    {
        if (double.IsNaN(metersPerSecondSquared) || double.IsInfinity(metersPerSecondSquared)) throw new GolemDomainException("an acceleration needs a number");
        if (metersPerSecondSquared < 0) throw new GolemDomainException("an acceleration cannot be negative");
        InMetersPerSecondSquared = metersPerSecondSquared;
    }
}

/// <summary>An acceleration written in metres per second squared: <c>MetersPerSecondSquared(0.5)</c>.</summary>
internal sealed class MetersPerSecondSquared : Acceleration
{
    internal MetersPerSecondSquared(double metersPerSecondSquared) : base(metersPerSecondSquared) { }
}
