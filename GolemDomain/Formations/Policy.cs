namespace GolemDomain.Formations;

/// <summary>
/// HOW A FORMATION SHARES ITS PLACES (14-oct-2026; Juan: "internal string Policy => policy; debería ser un Enum"): the closed set the warden
/// chooses across the wire — <c>Rank</c>, the names sorted take the places in order (ajuste 65); <c>Distance</c>, who stands nearest, the first
/// law over the words of the round (ajustes 73, 80); <c>RockPaperScissors</c>, who stands nearest, and the game between those who want the same
/// place (propuesta 115). An enum like <see cref="Sense"/> (ajuste 90): the host passes the member's NAME, the engine resolves it at the verb,
/// the journal keeps it (<c>Form(…, 'Distance', …)</c>), the read prints it; the operator's words (<c>rank</c>, <c>distance</c>, <c>rps</c>)
/// are the host's and the console's to translate.
/// </summary>
internal enum Policy
{
    Rank,
    Distance,
    RockPaperScissors,
}
