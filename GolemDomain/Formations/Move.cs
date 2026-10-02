using GolemDomain.Geometry;

namespace GolemDomain.Formations;

/// <summary>
/// A MOVE of a formation (ajuste 84, 2-oct-2026; Juan: "que la figura como tal tenga la coreografía… cada figura lo hará de forma
/// distinta"): what the figure says every body does on a step — a DATUM, nothing else: its name for the journal, and how many places each
/// body passes along the figure's FIXED ORDER of places (<see cref="Shift"/>: negative down the order). The figure hands it out
/// (<see cref="Formation.Rotate"/>); the convocation queues it and, when everybody stands, applies it (<see cref="Muster.Queue"/>,
/// <see cref="Muster.Step"/>): WHAT a move is, is the figure's; WHEN it happens is the fleet's.
/// </summary>
internal sealed class Move
{
    internal string Name { get; }
    internal int Shift { get; }

    internal Move(string name, int shift)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("a move needs its name");
        Name = name;
        Shift = shift;
    }

    /// <summary>The index of the place a body on that place goes to, among that many places: the shift along the order, around.</summary>
    internal int Next(int placeIndex, int count)
    {
        if (count < 1) throw new GolemDomainException("Move.Next: a formation needs at least one place");
        if (placeIndex < 0 || placeIndex >= count) throw new GolemDomainException($"Move.Next: place {placeIndex} is none of the {count} places");
        return ((placeIndex + Shift) % count + count) % count;
    }
}
