using System.Globalization;
using GolemDomain.Geometry;
using GolemDomain.Routes;

namespace GolemDomain.Formations;

/// <summary>
/// The golem's CHOREOGRAPHIES module (ajuste 69, 30-sep-2026; Juan: "el módulo asociado a coreografías… g.coreografia.formacion('cuadrado'),
/// y ese creará el objeto de la variable formación, que puede ser cualquier forma al final"): born with the golem, like its strategies,
/// and reached as <c>g.Choreography</c>. It makes a FORMATION by its name — the name enters where the object is created — with a centre and
/// the formation's natural measure: <c>formation = g.Choreography.Formation(@figure, center, side);</c>. The script no longer says which
/// shape it is. And it GIVES THE ROUTE to the place (ajuste 70; Juan: "el método Join es como algo que debe proporcionarlo el módulo de
/// Choreography, dar la route"): <c>route = g.Choreography.Join(from, formation, me);</c> — the module decides the place; the golem, from
/// inside, opens the route, so every route stays the golem's (its handle, its list, its scenario). The concrete figures stay
/// constructible for the tests and for this module.
/// </summary>
internal sealed class Choreographies
{
    private static readonly string[] Known = { "square", "triangle", "circle" };
    private readonly Func<Position, Position, string, Route> takePlace;   // the golem it was born with: the route to a place, refused off the map or without room
    private readonly Func<double> speed;                                  // …and its body's cruise, in m/s, for the stages of a turn
    private readonly List<Muster> musters = new();                        // the convocations by distance it heard of or convened in (ajuste 73)

    // born with its golem, which hands it how to ask for a route and how fast its body goes (like the strategies' switches, ajuste 63)
    internal Choreographies(Func<Position, Position, string, Route> takePlace, Func<double> speed)
    {
        if (takePlace == null) throw new GolemDomainException("Choreographies.Choreographies: 'takePlace' was not given");
        if (speed == null) throw new GolemDomainException("Choreographies.Choreographies: 'speed' was not given");
        this.takePlace = takePlace;
        this.speed = speed;
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

    /// <summary>The golem takes ITS PLACE in a formation BY RANK (propuesta 59, paso 1; ajuste 70: the module's, was the golem's): the
    /// place of its rank among as many places as the fleet has bodies, and the route to it — <c>route = g.Choreography.Join(from,
    /// formation, me);</c>. Refused when the place is nowhere on the map or leaves no room for the body.</summary>
    internal Route Join(Position from, Formation formation, Member me)
    {
        if (from == null) throw new GolemDomainException("Choreographies.Join: 'from' was not given");
        if (formation == null) throw new GolemDomainException("Choreographies.Join: 'formation' was not given");
        if (me == null) throw new GolemDomainException("Choreographies.Join: 'me' was not given");
        var place = formation.Place(me);
        string where = $"place {me.Rank + 1} of {me.Of} of the {formation.Name} at ({Fmt(place.X)}, {Fmt(place.Y)})";
        return takePlace(from, place, where);
    }

    /// <summary>The same, TURNING with the fleet (paso 3): its place first, then every stage of the rotation told to the route —
    /// <c>turn = Rotation(@direction, lasting); route = g.Choreography.Join(from, formation, me, turn);</c>. The stages are decided before
    /// the route is minted: a refusal mints nothing.</summary>
    internal Route Join(Position from, Formation formation, Member me, Rotation turn)
    {
        if (from == null) throw new GolemDomainException("Choreographies.Join: 'from' was not given");
        if (formation == null) throw new GolemDomainException("Choreographies.Join: 'formation' was not given");
        if (me == null) throw new GolemDomainException("Choreographies.Join: 'me' was not given");
        if (turn == null) throw new GolemDomainException("Choreographies.Join: 'turn' was not given");
        var stages = formation.Stages(me, turn, speed());
        var route = Join(from, formation, me);
        foreach (var stage in stages) route.Then(stage);
        return route;
    }

    /// <summary>The CONVOCATION of that call BY DISTANCE (ajuste 73): found by the call's identity, or opened — by the golem's own
    /// word or a peer's that came first — <c>muster = g.Choreography.Muster(@callId, formation, fleet);</c>.</summary>
    internal Muster Muster(string call, Formation formation, Fleet fleet)
    {
        if (string.IsNullOrWhiteSpace(call)) throw new GolemDomainException("Choreographies.Muster: 'call' was not given");
        if (formation == null) throw new GolemDomainException("Choreographies.Muster: 'formation' was not given");
        if (fleet == null) throw new GolemDomainException("Choreographies.Muster: 'fleet' was not given");
        var known = musters.FirstOrDefault(m => m.Call == call);
        if (known != null) return known;
        var muster = new Muster(call, formation, fleet, takePlace);
        musters.Add(muster);
        return muster;
    }

    /// <summary>The convocation a route of this golem belongs to (propuesta 74): the one the route was given by, or the one that holds it
    /// now — <c>muster = g.Choreography.MusterOf(route);</c>, the body's word on a route that yields. Refused for a route of no call.</summary>
    internal Muster MusterOf(Route route)
    {
        if (route == null) throw new GolemDomainException("Choreographies.MusterOf: 'route' was not given");
        return musters.FirstOrDefault(m => ReferenceEquals(m.Route, route))
            ?? throw new GolemDomainException($"route {route.Id} was given by no convocation");
    }

    /// <summary>The golem CONVENES by distance (ajuste 73; Juan: "el Convene… termina dando la route del punto donde se moverá"): it is
    /// recorded where it stands and given the route to the best place it knows now — <c>route = g.Choreography.Convene(from, muster, me);</c>.
    /// A peer's word may interrupt that route later (<see cref="Formations.Muster.Stood"/>).</summary>
    internal Route Convene(Position from, Muster muster, Member me)
    {
        if (from == null) throw new GolemDomainException("Choreographies.Convene: 'from' was not given");
        if (muster == null) throw new GolemDomainException("Choreographies.Convene: 'muster' was not given");
        if (me == null) throw new GolemDomainException("Choreographies.Convene: 'me' was not given");
        if (!musters.Contains(muster)) throw new GolemDomainException($"the call {muster.Call} is not this golem's: it is found with g.Choreography.Muster");
        return muster.Convene(from, me);
    }

    private static string Fmt(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
}
