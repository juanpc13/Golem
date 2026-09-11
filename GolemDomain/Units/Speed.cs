namespace GolemDomain.Units;

/// <summary>A speed: how much length per unit of time. Abstract magnitude, concrete unit; reads in metres per second.
/// A speed is never negative (direction is the heading's business, not the speed's).</summary>
internal abstract class Speed
{
    /// <summary>The speed in metres per second, the SI derived unit.</summary>
    internal double InMetersPerSecond { get; }

    protected Speed(double metersPerSecond)
    {
        if (double.IsNaN(metersPerSecond) || double.IsInfinity(metersPerSecond)) throw new DomainException("a speed needs a number");
        if (metersPerSecond < 0) throw new DomainException("a speed cannot be negative");
        InMetersPerSecond = metersPerSecond;
    }

    internal bool IsZero => InMetersPerSecond == 0;

    /// <summary>How long a length takes at this speed.</summary>
    internal Duration TimeFor(Length length)
    {
        if (length == null) throw new DomainException("a speed measures the time for a length");
        if (IsZero) throw new DomainException("nothing is covered at no speed");
        return new Seconds(length.InMeters / InMetersPerSecond);
    }
}

/// <summary>A speed written in metres per second: <c>MetersPerSecond(2.0)</c>.</summary>
internal sealed class MetersPerSecond : Speed
{
    internal MetersPerSecond(double metersPerSecond) : base(metersPerSecond) { }
}
