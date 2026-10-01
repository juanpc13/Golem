using GolemDomain.Geometry;
using GolemDomain.Routes;

namespace GolemDomain.Formations;

/// <summary>
/// The golem's CHOREOGRAPHIES module (ajuste 69, 30-sep-2026; Juan: "el módulo asociado a coreografías… g.coreografia.formacion('cuadrado'),
/// y ese creará el objeto de la variable formación, que puede ser cualquier forma al final"): born with the golem, like its strategies,
/// and reached as <c>g.Choreography</c>. It makes a FORMATION by its name — the name enters where the object is created — with a centre and
/// the formation's natural measure: <c>formation = g.Choreography.Formation(@figure, center, side);</c>. The script no longer says which
/// shape it is. Since ajuste 77 (1-oct-2026) every formation is taken THROUGH A CONVOCATION (<see cref="Formations.Muster"/>) — found or
/// opened here by its call, <c>muster = g.Choreography.Muster(@callId, formation, fleet, @by);</c> — which takes the words
/// (<c>muster.Convene(from, me)</c>, ajuste 81: the act on the object that keeps it), gives the routes through the golem it was born with,
/// stays as the formation in place and takes the fleet's steps. The concrete figures stay constructible for the tests and for this module.
/// </summary>
internal sealed class Choreographies
{
    private static readonly string[] Known = { "square", "triangle", "circle" };
    private readonly Golem golem;                   // the golem it was born with: it opens the route to a place (refused off the map or without room)
    private readonly List<Muster> musters = new();  // the convocations it heard of, joined or convened in (ajustes 73, 77)

    // born with its golem (like the strategies' switches, ajuste 63): objects that know each other, no delegate (ajuste 78)
    internal Choreographies(Golem golem)
    {
        if (golem == null) throw new GolemDomainException("Choreographies.Choreographies: 'golem' was not given");
        this.golem = golem;
    }

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

    /// <summary>The CONVOCATION of that call (ajuste 73; by rank too since ajuste 77): found by the call's identity, or opened — by the
    /// golem's own word or a peer's that came first — <c>muster = g.Choreography.Muster(@callId, formation, fleet);</c>.</summary>
    internal Muster Muster(string call, Formation formation, Fleet fleet)
    {
        if (string.IsNullOrWhiteSpace(call)) throw new GolemDomainException("Choreographies.Muster: 'call' was not given");
        if (formation == null) throw new GolemDomainException("Choreographies.Muster: 'formation' was not given");
        if (fleet == null) throw new GolemDomainException("Choreographies.Muster: 'fleet' was not given");
        return Muster(call, formation, fleet, "rank");
    }

    /// <summary>The convocation of that call WITH ITS POLICY (ajuste 80) — <c>rank</c> or <c>distance</c>, the word that travels in the
    /// tell so every copy shares it — found or opened: <c>muster = g.Choreography.Muster(@callId, formation, fleet, @by);</c>.</summary>
    internal Muster Muster(string call, Formation formation, Fleet fleet, string by)
    {
        if (string.IsNullOrWhiteSpace(call)) throw new GolemDomainException("Choreographies.Muster: 'call' was not given");
        if (formation == null) throw new GolemDomainException("Choreographies.Muster: 'formation' was not given");
        if (fleet == null) throw new GolemDomainException("Choreographies.Muster: 'fleet' was not given");
        if (by == null) throw new GolemDomainException("Choreographies.Muster: 'by' was not given");
        string policy = by.Trim().ToLowerInvariant();
        if (policy is not ("rank" or "distance")) throw new GolemDomainException($"the places are shared by 'rank' or by 'distance', not by '{by}'");
        var known = musters.FirstOrDefault(m => m.Call == call);
        if (known != null) return known;
        var muster = new Muster(call, formation, fleet, golem, policy == "distance");
        musters.Add(muster);
        return muster;
    }

    /// <summary>The convocation of that call, already known — <c>muster = g.Choreography.Muster(@call);</c> in the uptakes of the words
    /// about a formation in place (ajuste 77). Refused when unknown: ask <see cref="Knows"/> first.</summary>
    internal Muster Muster(string call)
    {
        if (string.IsNullOrWhiteSpace(call)) throw new GolemDomainException("Choreographies.Muster: 'call' was not given");
        return musters.FirstOrDefault(m => m.Call == call) ?? throw new GolemDomainException($"the golem knows no call {call}");
    }

    /// <summary>Whether this golem has its place in the convocation of that call — so a word about it (a member placed, a step) is its
    /// business: <c>if (g.Choreography.Knows(@call)) { … }</c>.</summary>
    internal bool Knows(string call)
    {
        if (string.IsNullOrWhiteSpace(call)) throw new GolemDomainException("Choreographies.Knows: 'call' was not given");
        return musters.Any(m => m.Call == call && m.Route != null);
    }

    /// <summary>The FORMATION IN PLACE (ajuste 77): the convocation of the latest route this golem took a place by — what a step
    /// (<c>rotate</c>) is about. Refused when the golem stands in no formation.</summary>
    internal Muster Current =>
        musters.Where(m => m.Route != null).OrderByDescending(m => m.Route.Id).FirstOrDefault()
        ?? throw new GolemDomainException("the fleet stands in no formation: call one first (choreograph)");

    /// <summary>Whether that route was this golem's route to its place in a convocation, and reached it — the arrival asks it to say the
    /// golem is placed: <c>if (g.Choreography.Reached(route)) { muster = g.Choreography.MusterOf(route); … }</c>.</summary>
    internal bool Reached(Route route)
    {
        if (route == null) throw new GolemDomainException("Choreographies.Reached: 'route' was not given");
        return musters.Any(m => m.IsReached(route));
    }

    /// <summary>The convocation a route of this golem belongs to (propuesta 74): the one the route was given by, or the one that holds it
    /// now — <c>muster = g.Choreography.MusterOf(route);</c>, the body's word on a route that yields. Refused for a route of no call.</summary>
    internal Muster MusterOf(Route route)
    {
        if (route == null) throw new GolemDomainException("Choreographies.MusterOf: 'route' was not given");
        return musters.FirstOrDefault(m => ReferenceEquals(m.Route, route))
            ?? throw new GolemDomainException($"route {route.Id} was given by no convocation");
    }
}
