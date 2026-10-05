namespace GolemDomain.Formations;

/// <summary>
/// The SENSE of a step around a figure (ajuste 90, 5-oct-2026; Juan: "corregir el enumerable de clockwise"): a closed set of two
/// the operator chooses across the wire, so an enum discriminator and not a string (training-lab, domain modeling E16: "prefer an
/// enum discriminator for a small, fixed, wire-selected set"). The host passes the member's NAME as a symbol
/// (<c>typeof(Enum)</c>, parameters guide): the engine resolves it at the verb, the journal keeps the name — <c>Rotate('Clockwise')</c>.
/// </summary>
internal enum Sense
{
    Clockwise,
    Counterclockwise,
}
