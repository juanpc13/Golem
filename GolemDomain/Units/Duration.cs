namespace GolemDomain.Units;

/// <summary>A duration: a span of time. Abstract magnitude, concrete unit; reads in seconds, the SI base unit. A
/// duration is never negative.</summary>
internal abstract class Duration
{
    /// <summary>The duration in seconds.</summary>
    internal double InSeconds { get; }

    protected Duration(double seconds)
    {
        if (double.IsNaN(seconds) || double.IsInfinity(seconds)) throw new DomainException("a duration needs a number");
        if (seconds < 0) throw new DomainException("a duration cannot be negative");
        InSeconds = seconds;
    }

    internal bool IsZero => InSeconds == 0;
}

/// <summary>A duration written in seconds: <c>Seconds(6.0)</c>.</summary>
internal sealed class Seconds : Duration
{
    internal Seconds(double seconds) : base(seconds) { }
}

/// <summary>A duration written in minutes: <c>Minutes(1.5)</c> is ninety seconds.</summary>
internal sealed class Minutes : Duration
{
    internal Minutes(double minutes) : base(minutes * 60.0) { }
}
