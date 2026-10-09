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
