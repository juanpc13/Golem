namespace GolemDomain.Formations;

/// <summary>HOW THE BEATS OF A UNION ARE AGREED among its golems (propuesta 124, 22-oct-2026; Juan: "la seña se la hacen los golems, pero el
/// sindicato sabe cómo resolverán entre los golems, si con push o pull"): by the CUE — the last lined up of a beat lets everybody go (the words
/// <c>AlignedAt</c> across every formation of the union; the push) — or by ASKING — the first by rank of each formation asks the others whether
/// their beat is done, every so often (the pull; not built yet). An enum like <see cref="Policy"/> and <see cref="Sense"/>: the operator says
/// <c>cue|ask</c>, the host passes the member's name, the engine resolves it at the verb.</summary>
public enum Link
{
    Cue,
    Ask,
}
