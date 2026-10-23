using GolemDomain.Geometry;

namespace GolemDomain.Formations;

/// <summary>
/// A MOVE of a figure (ajuste 84, 2-oct-2026; Juan: "que la figura como tal tenga la coreografía… cada figura lo hará de forma
/// distinta"): what the figure says every body does on a step — a DATUM, nothing else: its name for the journal, how many places each
/// body passes along the figure's FIXED ORDER of places (<see cref="Shift"/>: negative down the order), and WHICH ORBIT it moves
/// (<see cref="Orbit"/>: −1 every orbit — the one every figure's rotation moves since the double ring went, 19-oct-2026). The figure hands it out
/// (<see cref="Figure.Rotate(Sense)"/>); the formation queues it and, when everybody stands, applies it (<see cref="Formation.Queue"/>,
/// <see cref="Formation.Step"/>): WHAT a move is, is the figure's; WHEN it happens is the fleet's.
/// </summary>
internal sealed class Move
{
    internal string Name { get; }
    internal int Shift { get; }
    internal int Orbit { get; }

    internal Move(string name, int shift) : this(name, shift, -1) { }

    internal Move(string name, int shift, int orbit)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("a move needs its name");
        Name = name;
        Shift = shift;
        Orbit = orbit;
    }

    /// <summary>THE REST (propuesta 124): a beat on which the formation stays — a move of no shift; queued like a step, consumed by the beat, nobody moves.</summary>
    internal static Move Rest => new("rest", 0);
    internal bool IsRest => Shift == 0 && Orbit < 0;

    /// <summary>Whether a body in that orbit moves on this step.</summary>
    internal bool Moves(int orbit) => Orbit < 0 || Orbit == orbit;

    /// <summary>The index of the place a body on that place goes to, among that many places: the shift along the order, around.</summary>
    internal int Next(int placeIndex, int count) => Next(placeIndex, 0, count);

    /// <summary>The same, round an ORBIT: the places from <paramref name="start"/>, that many of them.</summary>
    internal int Next(int placeIndex, int start, int count)
    {
        if (count < 1) throw new GolemDomainException("Move.Next: a formation needs at least one place");
        if (placeIndex < start || placeIndex >= start + count) throw new GolemDomainException($"Move.Next: place {placeIndex} is none of the {count} places from {start}");
        return start + (((placeIndex - start + Shift) % count) + count) % count;
    }
}
