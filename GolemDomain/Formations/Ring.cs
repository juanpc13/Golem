namespace GolemDomain.Formations;

/// <summary>
/// WHICH RING of a figure a step turns (propuesta 104, 8-oct-2026; Juan: "que se pueda rotar el superior y el inferior"): the closed set the
/// operator chooses across the wire — <c>Whole</c>, every orbit of the figure (the only one a polygon or a circle has); <c>Outer</c> the
/// wider ring of a double ring (Juan's "superior"); <c>Inner</c> the narrower one (his "inferior"). An enum like <see cref="Sense"/>
/// (ajuste 90): the host passes the member's NAME, the engine resolves it at the verb, the journal keeps it, the tell carries it as text.
/// </summary>
internal enum Ring
{
    Whole,
    Outer,
    Inner,
}
