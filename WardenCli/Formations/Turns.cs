namespace WardenCli.Formations;

/// <summary>The SENSE of a step round a figure — the console's own words to compose a `rotate` line, the same two as the golem's.</summary>
public enum Sense
{
    Clockwise,
    Counterclockwise,
}

/// <summary>WHICH RING of a double ring a step turns (propuesta 104): the whole figure, the outer ring (Juan's "superior") or the inner one.</summary>
public enum Ring
{
    Whole,
    Outer,
    Inner,
}

/// <summary>HOW A FORMATION SHARES ITS PLACES (14-oct-2026; Juan: "debería ser un Enum") — the console's own words to compose a `form` line and
/// read the golem's answer: the same three as the golem's domain.</summary>
public enum Policy
{
    Rank,
    Distance,
    RockPaperScissors,
}

/// <summary>The policy in the line's words (<c>--by rank|distance|rps</c>) and back; the golem's read prints the member's name.</summary>
public static class Policies
{
    public static string Word(Policy policy) => policy switch { Policy.Distance => "distance", Policy.RockPaperScissors => "rps", _ => "rank" };

    /// <summary>A line's word or the golem's member name, any case; null when it is neither.</summary>
    public static Policy? Parse(string text) => text?.Trim().ToLowerInvariant() switch
    {
        "rank" => Policy.Rank,
        "distance" => Policy.Distance,
        "rps" or "rockpaperscissors" => Policy.RockPaperScissors,
        _ => null,
    };
}
