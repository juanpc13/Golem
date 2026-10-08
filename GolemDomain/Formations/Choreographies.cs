using System.Globalization;
using GolemDomain.Geometry;
using GolemDomain.Routes;
using GolemDomain.Touches;

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
    private static readonly string[] Known = { "square", "pentagon", "triangle", "circle" };   // the pentagon since ajuste 85
    private readonly Golem golem;                   // the golem it was born with: it opens the route to a place (refused off the map or without room)
    private readonly List<Muster> musters = new();  // the convocations it heard of, joined or convened in (ajustes 73, 77)
    private readonly List<Formation> named = new();   // the formations the warden named for it, kept by name (propuesta 99)

    // born with its golem (like the strategies' switches, ajuste 63): objects that know each other, no delegate (ajuste 78)
    internal Choreographies(Golem golem)
    {
        if (golem == null) throw new GolemDomainException("Choreographies.Choreographies: 'golem' was not given");
        this.golem = golem;
    }

    /// <summary>A formation by its name — <c>square</c>, <c>pentagon</c>, <c>triangle</c> or <c>circle</c>, any case — at that centre, with its natural
    /// measure: a polygon's SIDE (ajuste 65), a circle's RADIUS. A name that is none of them is refused.</summary>
    internal Formation Formation(string name, Position center, Units.Length measure)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Formation: 'name' was not given");
        if (center == null) throw new GolemDomainException("Choreographies.Formation: 'center' was not given");
        if (measure == null) throw new GolemDomainException("Choreographies.Formation: 'measure' was not given");
        return name.Trim().ToLowerInvariant() switch
        {
            "square" => new Square(center, measure),
            "pentagon" => new Pentagon(center, measure),
            "triangle" => new Triangle(center, measure),
            "circle" => new Circle(center, measure),
            _ => throw new GolemDomainException($"the golem knows no formation named '{name}': {string.Join(", ", Known)}")
        };
    }

    /// <summary>The formations the golem knows how to take, by name.</summary>
    internal IReadOnlyList<string> Names() => Known;

    // ==================================================================
    // THE FORMATIONS THE WARDEN NAMES (propuesta 99, 8-oct-2026; Juan: "darle contexto al golem pero nunca darle la coordenada exacta de su
    // visit"): the warden says which formation — its name, its figure, its centre, its side, its orientation — and which VERTEX this golem
    // takes; WHERE that vertex stands is the golem's to resolve, on its own map. Who takes which vertex is the warden's (no fleet, no rank,
    // no word between golems here); the convocations above stay for the golems' own choreography.
    // ==================================================================

    /// <summary>The formation of that NAME, made — or MADE AGAIN, when the name is known: moving, turning or resizing a formation is saying
    /// it again with its new figure — <c>formation = g.Choreography.Form(@name, @figure, center, side, turn);</c>. The figures that have
    /// vertices by number: <c>square</c>, <c>pentagon</c>, <c>triangle</c>; the circle's places hang on how many bodies take it.</summary>
    internal Polygon Form(string name, string figure, Position center, Units.Length side, Units.Angle turn)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Form: 'name' was not given");
        if (figure == null) throw new GolemDomainException("Choreographies.Form: 'figure' was not given");
        if (center == null) throw new GolemDomainException("Choreographies.Form: 'center' was not given");
        if (side == null) throw new GolemDomainException("Choreographies.Form: 'side' was not given");
        if (turn == null) throw new GolemDomainException("Choreographies.Form: 'turn' was not given");
        string called = name.Trim().ToLowerInvariant();
        if (called == "") throw new GolemDomainException("a formation needs a name");
        Polygon formation = figure.Trim().ToLowerInvariant() switch
        {
            "square" => new Square(center, side, turn),
            "pentagon" => new Pentagon(center, side, turn),
            "triangle" => new Triangle(center, side, turn),
            "circle" => throw new GolemDomainException("a circle's places depend on how many bodies take it: form a square, a pentagon or a triangle"),
            _ => throw new GolemDomainException($"the golem forms no figure named '{figure}': square, pentagon or triangle")
        };
        return Keep(formation, called);
    }

    // a formation told, kept by its name: made again when the name is known
    private T Keep<T>(T formation, string called) where T : Formation
    {
        formation.Called = called;
        int known = named.FindIndex(f => f.Called == called);
        if (known >= 0) named[known] = formation; else named.Add(formation);
        return formation;
    }

    /// <summary>A formation told FOR THAT MANY BODIES (ajuste 101, 8-oct-2026; Juan: "hagamos lo mismo en el CLI"): the same as
    /// <see cref="Form(string, string, Position, Units.Length, Units.Angle)"/>, and its places are as many as the bodies when they are more
    /// than its vertices — the corners first, the rest on the sides (ajuste 100) —
    /// <c>formation = g.Choreography.Form(@formationName, @figure, center, side, turn, @bodies);</c>. The warden says how many golems it lays
    /// out; who takes which place is still the warden's, by number.
    /// A CIRCLE too (ajuste 102, 8-oct-2026; Juan, on the double ring sent as visits: "no con el contexto del doble anillo"): told for that
    /// many bodies its places are known, so the warden names a ring — <c>circle</c>, its measure the radius — and each golem takes its place by
    /// number; a double ring is told as two circles of one centre.</summary>
    internal Formation Form(string name, string figure, Position center, Units.Length measure, Units.Angle turn, int places)
    {
        if (places < 1 || places > 100) throw new GolemDomainException($"a formation is laid out for 1 to 100 bodies; {places} were said");
        if (figure != null && figure.Trim().ToLowerInvariant() == "circle")
        {
            if (name == null) throw new GolemDomainException("Choreographies.Form: 'name' was not given");
            if (center == null) throw new GolemDomainException("Choreographies.Form: 'center' was not given");
            if (measure == null) throw new GolemDomainException("Choreographies.Form: 'measure' was not given");
            if (turn == null) throw new GolemDomainException("Choreographies.Form: 'turn' was not given");
            string called = name.Trim().ToLowerInvariant();
            if (called == "") throw new GolemDomainException("a formation needs a name");
            var circle = Keep(new Circle(center, measure, turn), called);
            circle.Bodies = places;
            return circle;
        }
        var formation = Form(name, figure, center, measure, turn);
        formation.Bodies = places;
        return formation;
    }

    /// <summary>The formation of that name, formed before — <c>formation = g.Choreography.Find(@name);</c>, the one place its name enters
    /// after <see cref="Form"/>, like a route's id. Refused when the golem was told no formation by that name.</summary>
    internal Formation Find(string name)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Find: 'name' was not given");
        string called = name.Trim().ToLowerInvariant();
        return named.FirstOrDefault(f => f.Called == called) ?? throw new GolemDomainException($"the golem was told no formation named '{called}': form it first");
    }

    /// <summary>Whether the golem was told a formation by that name — what a script asks before it finds it.</summary>
    internal bool HasFormation(string name)
    {
        if (name == null) throw new GolemDomainException("Choreographies.HasFormation: 'name' was not given");
        string called = name.Trim().ToLowerInvariant();
        return named.Any(f => f.Called == called);
    }

    /// <summary>The formations the golem was told, in the order it was first told them.</summary>
    internal IReadOnlyList<Formation> Formations() => named.ToList();

    /// <summary>THE VERTEX TAKEN (propuesta 99): the route from where the errand starts to that vertex of that formation —
    /// <c>route = g.Choreography.Take(from, formation, vertex);</c> — the golem's own errand (<see cref="Golem.TakePlace"/>): refused when the
    /// vertex is nowhere on the map or leaves no room for the body, and ending FACING THE CENTRE of the formation, as in the golem's own
    /// choreography (ajuste 79). No berths: no round of words told it where the others stand; a body met on the way is the touches'.</summary>
    internal Route Take(Position from, Formation formation, Position place)
    {
        if (from == null) throw new GolemDomainException("Choreographies.Take: 'from' was not given");
        if (formation == null) throw new GolemDomainException("Choreographies.Take: 'formation' was not given");
        if (place == null) throw new GolemDomainException("Choreographies.Take: 'place' was not given");
        string where = string.Format(CultureInfo.InvariantCulture, "the place at ({0:0.##}, {1:0.##}) of {2}", place.X, place.Y,
                                     formation.Called == "" ? "the " + formation.Name : formation.Called);
        return golem.TakePlace(from, place, formation.Center, Array.Empty<Peer>(), where);
    }

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
