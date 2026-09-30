using GolemDomain.Geometry;

namespace GolemDomain.Formations;

/// <summary>
/// The golem's CHOREOGRAPHIES module (ajuste 69, 30-sep-2026; Juan: "el módulo asociado a coreografías… g.coreografia.formacion('cuadrado'),
/// y ese creará el objeto de la variable formación, que puede ser cualquier forma al final"): born with the golem, like its strategies,
/// and reached as <c>g.Choreography</c>. It makes a FORMATION by its name — the name enters where the object is created — with a centre and
/// the formation's natural measure: <c>formation = g.Choreography.Formation(@figure, center, side);</c>. The script no longer says which
/// shape it is; <c>g.Join</c> takes any. The concrete figures stay constructible for the tests and for this module.
/// </summary>
internal sealed class Choreographies
{
    private static readonly string[] Known = { "square", "triangle", "circle" };

    /// <summary>A formation by its name — <c>square</c>, <c>triangle</c> or <c>circle</c>, any case — at that centre, with its natural
    /// measure: a polygon's SIDE (ajuste 65), a circle's RADIUS. A name that is none of them is refused.</summary>
    internal Formation Formation(string name, Position center, Units.Length measure)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Formation: 'name' was not given");
        if (center == null) throw new GolemDomainException("Choreographies.Formation: 'center' was not given");
        if (measure == null) throw new GolemDomainException("Choreographies.Formation: 'measure' was not given");
        return name.Trim().ToLowerInvariant() switch
        {
            "square" => new Square(center, measure),
            "triangle" => new Triangle(center, measure),
            "circle" => new Circle(center, measure),
            _ => throw new GolemDomainException($"the golem knows no formation named '{name}': {string.Join(", ", Known)}")
        };
    }

    /// <summary>The formations the golem knows how to take, by name.</summary>
    internal IReadOnlyList<string> Names() => Known;
}
