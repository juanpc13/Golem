using GolemDomain.Geometry;
using GolemDomain.Routes;

namespace GolemDomain.Formations;

/// <summary>
/// The golem's CHOREOGRAPHIES module (ajuste 69, 30-sep-2026; Juan: "el módulo asociado a coreografías… g.coreografia.formacion('cuadrado'),
/// y ese creará el objeto de la variable formación, que puede ser cualquier forma al final"): born with the golem, like its strategies,
/// and reached as <c>g.Choreography</c>. Since propuesta 104 (8-oct-2026; Juan: "las formaciones le pertenecerán al golem… el CLI sólo
/// servirá como interfaz para administrar esas formaciones") it KEEPS THE FORMATIONS BY NAME — each a figure WITH its fleet, its policy and
/// the place every member holds — made here (<see cref="Form"/>, <see cref="FormRings"/>: told again, made again — moved, turned, resized),
/// found by name (<see cref="Find"/>, the one place the name enters after it is made), listed for the warden (<see cref="Formations"/>) and
/// dissolved (<see cref="Dissolve"/>). The formation takes the words (<c>formation.Convene(from, me)</c>, ajuste 81: the act on the object
/// that keeps it), gives the routes through the golem it was born with, and takes the fleet's steps. The concrete figures stay constructible
/// for the tests and for this module (<see cref="Figure(string, Position, Units.Length)"/>).
/// </summary>
internal sealed class Choreographies
{
    private static readonly string[] Known = { "square", "pentagon", "triangle", "circle", "double ring" };
    private readonly Golem golem;                        // the golem it was born with: it opens the route to a place (refused off the map or without room)
    private readonly List<Formation> formations = new();   // the formations the golem was told, by name, the dissolved ones among them (history)

    // born with its golem (like the strategies' switches, ajuste 63): objects that know each other, no delegate (ajuste 78)
    internal Choreographies(Golem golem)
    {
        if (golem == null) throw new GolemDomainException("Choreographies.Choreographies: 'golem' was not given");
        this.golem = golem;
    }

    /// <summary>A figure by its name — <c>square</c>, <c>pentagon</c>, <c>triangle</c> or <c>circle</c>, any case — at that centre, with its natural
    /// measure: a polygon's SIDE (ajuste 65), a circle's RADIUS. A name that is none of them is refused; the double ring is made by
    /// <see cref="FormRings"/>, with its two radii.</summary>
    internal Figure Figure(string name, Position center, Units.Length measure) => Figure(name, center, measure, new Units.Degrees(0.0));

    internal Figure Figure(string name, Position center, Units.Length measure, Units.Angle turn)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Figure: 'name' was not given");
        if (center == null) throw new GolemDomainException("Choreographies.Figure: 'center' was not given");
        if (measure == null) throw new GolemDomainException("Choreographies.Figure: 'measure' was not given");
        if (turn == null) throw new GolemDomainException("Choreographies.Figure: 'turn' was not given");
        return name.Trim().ToLowerInvariant() switch
        {
            "square" => new Square(center, measure, turn),
            "pentagon" => new Pentagon(center, measure, turn),
            "triangle" => new Triangle(center, measure, turn),
            "circle" => new Circle(center, measure, turn),
            "double ring" or "double-ring" => throw new GolemDomainException("a double ring is said by its two radii and its two rings of golems: FormRings"),
            _ => throw new GolemDomainException($"the golem knows no figure named '{name}': {string.Join(", ", Known)}")
        };
    }

    /// <summary>The figures the golem knows how to take, by name.</summary>
    internal IReadOnlyList<string> Names() => Known;

    /// <summary>THE FORMATION TOLD (propuesta 104) — <c>formation = g.Choreography.Form(@formationName, @figure, center, measure, turn, fleet, @by);</c>:
    /// made with its figure, its fleet and its policy (<c>rank</c> | <c>distance</c>), and KEPT by its name; told again with the same name it
    /// is MADE AGAIN — moving, turning, resizing or re-crewing a formation is saying it again — the old one dissolved (a route already given
    /// keeps the figure it was given by). The same words reach every golem of the fleet, so every copy is the same.</summary>
    internal Formation Form(string name, string figure, Position center, Units.Length measure, Units.Angle turn, Fleet fleet, string by) => Form(name, figure, center, measure, turn, fleet, by, name);

    /// <summary>The same, with the STAMP the host minted — the once of the call, carried by every tell about the formation.</summary>
    internal Formation Form(string name, string figure, Position center, Units.Length measure, Units.Angle turn, Fleet fleet, string by, string stamp)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Form: 'name' was not given");
        if (figure == null) throw new GolemDomainException("Choreographies.Form: 'figure' was not given");
        if (center == null) throw new GolemDomainException("Choreographies.Form: 'center' was not given");
        if (measure == null) throw new GolemDomainException("Choreographies.Form: 'measure' was not given");
        if (turn == null) throw new GolemDomainException("Choreographies.Form: 'turn' was not given");
        if (fleet == null) throw new GolemDomainException("Choreographies.Form: 'fleet' was not given");
        if (by == null) throw new GolemDomainException("Choreographies.Form: 'by' was not given");
        if (fleet.IsDivided) throw new GolemDomainException("a fleet in two rings takes a double ring: FormRings");
        return Keep(name, Figure(figure, center, measure, turn), fleet, by, stamp);
    }

    /// <summary>The same, with the figure in hand — the tests' and this module's way (methods take instances).</summary>
    internal Formation Form(string name, Figure figure, Fleet fleet, string by)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Form: 'name' was not given");
        if (figure == null) throw new GolemDomainException("Choreographies.Form: 'figure' was not given");
        if (fleet == null) throw new GolemDomainException("Choreographies.Form: 'fleet' was not given");
        if (by == null) throw new GolemDomainException("Choreographies.Form: 'by' was not given");
        return Keep(name, figure, fleet, by, name);
    }

    /// <summary>THE DOUBLE RING TOLD (propuesta 104; Juan: "que pregunte por los diámetros de los dos anillos… seleccionar cuáles serían los
    /// golems del anillo inferior y superior") — <c>formation = g.Choreography.FormRings(@formationName, center, outer, inner, turn, fleet, @by);</c>:
    /// the fleet DIVIDED in two (<c>Fleet(@names, @innerNames)</c>) rides the two rings, the outer radius and the inner one as given.</summary>
    internal Formation FormRings(string name, Position center, Units.Length outerRadius, Units.Length innerRadius, Units.Angle turn, Fleet fleet, string by) => FormRings(name, center, outerRadius, innerRadius, turn, fleet, by, name);

    internal Formation FormRings(string name, Position center, Units.Length outerRadius, Units.Length innerRadius, Units.Angle turn, Fleet fleet, string by, string stamp)
    {
        if (name == null) throw new GolemDomainException("Choreographies.FormRings: 'name' was not given");
        if (center == null) throw new GolemDomainException("Choreographies.FormRings: 'center' was not given");
        if (outerRadius == null) throw new GolemDomainException("Choreographies.FormRings: 'outerRadius' was not given");
        if (innerRadius == null) throw new GolemDomainException("Choreographies.FormRings: 'innerRadius' was not given");
        if (turn == null) throw new GolemDomainException("Choreographies.FormRings: 'turn' was not given");
        if (fleet == null) throw new GolemDomainException("Choreographies.FormRings: 'fleet' was not given");
        if (by == null) throw new GolemDomainException("Choreographies.FormRings: 'by' was not given");
        if (!fleet.IsDivided) throw new GolemDomainException("a double ring needs its fleet in two rings: Fleet(outer, inner)");
        var rings = new DoubleRing(center, outerRadius, innerRadius, fleet.Division, fleet.Count - fleet.Division, turn);
        return Keep(name, rings, fleet, by, stamp);
    }

    private Formation Keep(string name, Figure figure, Fleet fleet, string by, string stamp)
    {
        string policy = by.Trim().ToLowerInvariant();
        if (policy is not ("rank" or "distance")) throw new GolemDomainException($"the places are shared by 'rank' or by 'distance', not by '{by}'");
        string called = name.Trim().ToLowerInvariant();
        if (called == "") throw new GolemDomainException("a formation needs a name");
        var formation = new Formation(called, figure, fleet, golem, policy == "distance", stamp);
        var known = formations.FirstOrDefault(f => f.Name == called && !f.Dissolved);
        known?.Dissolve();                                  // told again: the old one goes, the new one takes its name
        formations.Add(formation);
        return formation;
    }

    /// <summary>The formation of that name, told before and not dissolved — <c>formation = g.Choreography.Find(@formationName);</c>, the one
    /// place its name enters after it is made, like a route's id. Refused when the golem was told no formation by that name.</summary>
    internal Formation Find(string name)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Find: 'name' was not given");
        string called = name.Trim().ToLowerInvariant();
        return formations.LastOrDefault(f => f.Name == called && !f.Dissolved) ?? throw new GolemDomainException($"the golem was told no formation named '{called}': form it first");
    }

    /// <summary>Whether the golem was told a formation by that name, still in force — what a script asks before it finds it.</summary>
    internal bool HasFormation(string name)
    {
        if (name == null) throw new GolemDomainException("Choreographies.HasFormation: 'name' was not given");
        string called = name.Trim().ToLowerInvariant();
        return formations.Any(f => f.Name == called && !f.Dissolved);
    }

    /// <summary>Whether this golem has its place in the formation of that name — so a word about it (a member placed, a step) is its
    /// business: <c>if (g.Choreography.Knows(@call)) { … }</c>.</summary>
    internal bool Knows(string name)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Knows: 'name' was not given");
        string called = name.Trim().ToLowerInvariant();
        return formations.Any(f => f.Name == called && !f.Dissolved && f.Route != null);
    }

    /// <summary>THE FORMATIONS THE GOLEM IS IN (propuesta 104: the warden's GET): every one it was told and not dissolved, in the order it
    /// was told them — each with its figure, its fleet, its policy and who holds what.</summary>
    internal IReadOnlyList<Formation> Formations() => formations.Where(f => !f.Dissolved).ToList();

    /// <summary>DISSOLVED (propuesta 104) — <c>g.Choreography.Dissolve(formation);</c>: it leaves the list; a route of this golem still pending
    /// is abandoned. Told to the fleet by the reaction on this act.</summary>
    internal void Dissolve(Formation formation)
    {
        if (formation == null) throw new GolemDomainException("Choreographies.Dissolve: 'formation' was not given");
        if (!formations.Contains(formation)) throw new GolemDomainException($"the golem was told no formation named '{formation.Name}'");
        formation.Dissolve();
    }

    /// <summary>The FORMATION IN PLACE (ajuste 77): the one of the latest route this golem took a place by — what the arrival's word is
    /// about. Refused when the golem stands in no formation.</summary>
    internal Formation Current =>
        formations.Where(f => f.Route != null && !f.Dissolved).OrderByDescending(f => f.Route.Id).FirstOrDefault()
        ?? throw new GolemDomainException("the golem stands in no formation: form one and take it first");

    /// <summary>Whether that route was this golem's route to its place in a formation, and reached it — the arrival asks it to say the
    /// golem is placed: <c>if (g.Choreography.Reached(route)) { … }</c>.</summary>
    internal bool Reached(Route route)
    {
        if (route == null) throw new GolemDomainException("Choreographies.Reached: 'route' was not given");
        return formations.Any(f => !f.Dissolved && f.IsReached(route));
    }

    /// <summary>The formation a route of this golem belongs to (propuesta 74): the one that holds it now —
    /// <c>formation = g.Choreography.FormationOf(route);</c>, the body's word on a route that yields. Refused for a route of no formation.</summary>
    internal Formation FormationOf(Route route)
    {
        if (route == null) throw new GolemDomainException("Choreographies.FormationOf: 'route' was not given");
        return formations.FirstOrDefault(f => ReferenceEquals(f.Route, route))
            ?? throw new GolemDomainException($"route {route.Id} was given by no formation");
    }
}
