namespace GolemDomain.Formations;

/// <summary>
/// The SENSE of a step around a figure (ajuste 90, 5-oct-2026; Juan: "corregir el enumerable de clockwise"): a closed set of two
/// the operator chooses across the wire, so an enum discriminator and not a string (training-lab, domain modeling E16: "prefer an
/// enum discriminator for a small, fixed, wire-selected set"). The host passes the member's NAME (parameters guide; as text here,
/// because the step's script exposes it for the tell to the peers and the engine cannot expose an Enum symbol): the engine resolves it
/// at the verb, the journal keeps the name — <c>Rotate('Clockwise')</c> — and the peers' uptake coerces it to the enum by name.
/// </summary>
internal enum Sense
{
    Clockwise,
    Counterclockwise,
}
