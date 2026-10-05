using GolemDomain.Coordination;
using GolemDomain.Geometry;
using GolemDomain.Routes;
using GolemDomain.Touches;

namespace GolemDomain.Formations;

/// <summary>
/// The golem's CHOREOGRAPHIES module (ajuste 69, 30-sep-2026): born with the golem, reached as <c>g.Choreography</c>. It makes a
/// FORMATION by its name (<c>formation = g.Choreography.Formation(@figure, center, side);</c>) and, since propuesta 88 (2-oct-2026; Juan:
/// "centraliza la ley… que ellos sólo coordinen con el Warden"), it holds what a golem needs to OBEY A PLACE — no convocation, no law of
/// its own any more (those are the warden's, <see cref="Coordination.Muster"/>): it answers a call with where it stands
/// (<see cref="Stood"/>), TAKES the place the warden shares — its own from the table, the others as berths — opening the route from inside
/// the golem (<see cref="Take"/>), says when it stands on it (<see cref="Placement.Placed"/>) and, when a word moves it while an order
/// is in the body, yields and opens again from where the body stopped (<see cref="Halted"/>).
/// </summary>
internal sealed class Choreographies
{
    private readonly Golem golem;                       // the golem it was born with: it opens the route to a place (refused off the map or without room)
    private readonly HashSet<string> answered = new(StringComparer.Ordinal);   // the calls this golem said where it stood for
    private readonly List<Placement> placements = new();  // the places it was given, by call and round

    // born with its golem (like the strategies' switches, ajuste 63): objects that know each other, no delegate (ajuste 78)
    internal Choreographies(Golem golem)
    {
        if (golem == null) throw new GolemDomainException("Choreographies.Choreographies: 'golem' was not given");
        this.golem = golem;
    }

    /// <summary>A formation by its name — <c>square</c>, <c>pentagon</c>, <c>triangle</c> or <c>circle</c>, any case — at that centre, with its
    /// natural measure: a polygon's SIDE (ajuste 65), a circle's RADIUS. A name that is none of them is refused.</summary>
    internal Formation Formation(string name, Position center, Units.Length measure)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Formation: 'name' was not given");
        if (center == null) throw new GolemDomainException("Choreographies.Formation: 'center' was not given");
        if (measure == null) throw new GolemDomainException("Choreographies.Formation: 'measure' was not given");
        return Formations.Formation.Named(name, center, measure);
    }

    /// <summary>The formations the golem knows how to take, by name.</summary>
    internal IReadOnlyList<string> Names() => Formations.Formation.Known;

    /// <summary>The golem ANSWERS A CALL: it says where it stands — <c>g.Choreography.Stood(@call, from)</c>, the act the reaction tells the
    /// warden on. Once per call; a second time is refused.</summary>
    internal void Stood(string call, Position from)
    {
        if (string.IsNullOrWhiteSpace(call)) throw new GolemDomainException("Choreographies.Stood: 'call' was not given");
        if (from == null) throw new GolemDomainException("Choreographies.Stood: 'from' was not given");
        if (!answered.Add(call)) throw new GolemDomainException($"'{golem.Name}' said where it stood for the call {call} already");
    }

    /// <summary>Whether this golem answered that call.</summary>
    internal bool Answered(string call)
    {
        if (string.IsNullOrWhiteSpace(call)) throw new GolemDomainException("Choreographies.Answered: 'call' was not given");
        return answered.Contains(call);
    }

    /// <summary>The golem TAKES ITS PLACE from the table the warden shared (propuesta 88) — <c>route = g.Choreography.Take(@call, @round,
    /// given, facing)</c>: its own assignment is the place, the others are BERTHS (where each stands and where it goes: bodies in the way
    /// while the route lasts, ajuste 80), the route opens from where the golem knows its body stands, ending FACING the centre (ajuste 79).
    /// The same round told again: the same place leaves the route as it is; another place while an order of the route is in the body makes
    /// it YIELD (propuesta 74: the body stops, says where it stood, <see cref="Halted"/> opens the next); with the route ended, the next
    /// opens at once from its last arrival. Refused when this golem has no place in the table.</summary>
    internal Route Take(string call, int round, Assignments given, Position facing)
    {
        if (string.IsNullOrWhiteSpace(call)) throw new GolemDomainException("Choreographies.Take: 'call' was not given");
        if (round < 0) throw new GolemDomainException("Choreographies.Take: a round is 0 or more");
        if (given == null) throw new GolemDomainException("Choreographies.Take: 'given' was not given");
        if (facing == null) throw new GolemDomainException("Choreographies.Take: 'facing' was not given");
        if (!given.Has(golem)) throw new GolemDomainException($"'{golem.Name}' has no place in the call {call}");
        var own = given.Of(golem);
        var berths = new List<Peer>();
        foreach (var other in given.Others(golem))
        {
            berths.Add(new Peer(other.Who, other.From));
            berths.Add(new Peer(other.Who, other.To));
        }
        var current = placements.FirstOrDefault(p => p.Call == call && p.Round == round);
        if (current != null)
        {
            if (current.Place.DistanceTo(own.To) < 1e-6) return current.Route;      // the same place again: nothing changes
            current.Head(own.To, facing, berths);
            if (current.Route.IsPending())
            {
                if (!current.Route.Yielding) current.Route.Yield();                 // the body carries an order of it: it stops first and says where it stood
                return current.Route;
            }
            if (current.Route.EndedShort) return current.Route;                      // a route that ended short is not revived by a word
            current.Open(golem.TakePlace(current.Route.Standing, own.To, facing, berths, Where(call, own.To)));
            return current.Route;
        }
        var placement = new Placement(call, round, own.To, facing, berths);
        placement.Open(golem.TakePlace(golem.Destination, own.To, facing, berths, Where(call, own.To)));
        placements.Add(placement);
        return placement.Route;
    }

    /// <summary>The body STOPPED for the placement that yields its place (propuesta 74): where it really stood enters as <c>me</c>, the
    /// route is abandoned and the one to the place kept opens from there — <c>route = g.Choreography.Halted(me)</c>.</summary>
    internal Route Halted(Pose me)
    {
        if (me == null) throw new GolemDomainException("Choreographies.Halted: 'me' was not given");
        var yielding = placements.FirstOrDefault(p => p.Route != null && p.Route.Yielding && p.Route.IsPending())
            ?? throw new GolemDomainException("no route of a formation yields its place: nothing to halt");
        yielding.Route.Abandon($"the warden moved {golem.Name} to ({Fmt(yielding.Place.X)}, {Fmt(yielding.Place.Y)})");
        yielding.Open(golem.TakePlace(me, yielding.Place, yielding.Facing, yielding.Berths, Where(yielding.Call, yielding.Place)));
        return yielding.Route;
    }

    /// <summary>The PLACEMENT IN PLACE: the latest place this golem was given — what its word "placed" is about. Refused when it was given none.</summary>
    internal Placement Current =>
        placements.Where(p => p.Route != null).OrderByDescending(p => p.Route.Id).FirstOrDefault()
        ?? throw new GolemDomainException("the golem stands in no formation: it was given no place yet");

    /// <summary>Whether that route was this golem's route to a place and reached it, its word not said yet — the arrival asks it:
    /// <c>Check(g.Choreography.Reached(route))</c>.</summary>
    internal bool Reached(Route route)
    {
        if (route == null) throw new GolemDomainException("Choreographies.Reached: 'route' was not given");
        return placements.Any(p => ReferenceEquals(p.Route, route) && p.Reached);
    }

    private string Where(string call, Position place) => $"the place of {golem.Name} in the call {call} at ({Fmt(place.X)}, {Fmt(place.Y)})";
    private static string Fmt(double d) => d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>A PLACE this golem was given in a round of a call (propuesta 88): the place, the centre to face, the berths it was told, the
/// route to it, and whether it said it stands on it.</summary>
internal sealed class Placement
{
    internal Placement(string call, int round, Position place, Position facing, IReadOnlyList<Peer> berths)
    {
        if (string.IsNullOrWhiteSpace(call)) throw new GolemDomainException("Placement.Placement: 'call' was not given");
        if (place == null) throw new GolemDomainException("Placement.Placement: 'place' was not given");
        if (facing == null) throw new GolemDomainException("Placement.Placement: 'facing' was not given");
        if (berths == null) throw new GolemDomainException("Placement.Placement: 'berths' was not given");
        Call = call;
        Round = round;
        Place = place;
        Facing = facing;
        Berths = berths;
    }

    internal string Call { get; }
    internal int Round { get; }
    internal Position Place { get; private set; }
    internal Position Facing { get; private set; }
    internal IReadOnlyList<Peer> Berths { get; private set; }
    internal Route Route { get; private set; }
    /// <summary>Whether the golem said it stands on this place.</summary>
    internal bool Said { get; private set; }
    /// <summary>The route reached the place and the golem has not said so yet.</summary>
    internal bool Reached => Route != null && !Route.IsPending() && !Route.EndedShort && !Said;

    internal void Head(Position place, Position facing, IReadOnlyList<Peer> berths) { Place = place; Facing = facing; Berths = berths; Said = false; }
    internal void Open(Route route) { Route = route; Said = false; }

    /// <summary>THE GOLEM STANDS ON ITS PLACE (ajuste 77; propuesta 88: told to the warden): <c>placement.Placed();</c> — the act the
    /// reaction tells on. Refused when the route did not reach the place, or the word was said already.</summary>
    internal void Placed()
    {
        if (!Reached) throw new GolemDomainException($"'{Call}': the golem does not stand on its place, or said so already");
        Said = true;
    }
}
