using System.Globalization;
using GolemDomain.Geometry;
using GolemDomain.Routes;

namespace GolemDomain.Formations;

/// <summary>
/// A CONVOCATION — the fleet called to a formation BY DISTANCE (ajuste 73; propuesta 64, PLAN-coreografia-disputa §13 and §14):
/// every golem keeps its own copy, fed by the same facts — where each member said it stood — and applies the same law, so the copies
/// agree once everybody spoke. Its identity is the CALL (<c>@callId</c>): two calls to the same square are two convocations. The golem
/// convenes once (<see cref="Convene"/>) and is given the route to the best place it knows; every peer's word (<see cref="Stood"/>)
/// applies the law again and, if the golem lost its place to a nearer peer, interrupts its route and gives it the next — at once when
/// the body stands, or, with an order of the route in course, once the body stopped and said where it stood (<see cref="Halted"/>,
/// propuesta 74).
/// </summary>
internal sealed class Muster
{
    private readonly Func<Position, Position, string, Route> takePlace;   // the golem's own: the route to a place (ajuste 70)
    private readonly Dictionary<string, Position> stood = new();         // where each member said it stood, by name
    private Member me;                                                   // this golem, once it convened
    private Position place;                                             // …the place it is going to
    private Route route;                                                // …and the route there
    private string yieldedTo;                                           // the peer whose word made that route yield, until it is replaced

    internal Muster(string call, Formation formation, Fleet fleet, Func<Position, Position, string, Route> takePlace)
    {
        if (string.IsNullOrWhiteSpace(call)) throw new GolemDomainException("a convocation needs its call");
        if (formation == null) throw new GolemDomainException("Muster.Muster: 'formation' was not given");
        if (fleet == null) throw new GolemDomainException("Muster.Muster: 'fleet' was not given");
        if (takePlace == null) throw new GolemDomainException("Muster.Muster: 'takePlace' was not given");
        Call = call;
        Formation = formation;
        Fleet = fleet;
        this.takePlace = takePlace;
    }

    internal string Call { get; }
    internal Formation Formation { get; }
    internal Fleet Fleet { get; }
    /// <summary>How many members said where they stood — this golem among them once it convened.</summary>
    internal int StoodCount => stood.Count;
    /// <summary>Every member of the fleet spoke: every copy has the same table.</summary>
    internal bool IsComplete => stood.Count == Fleet.Count;
    /// <summary>This golem's route in the convocation; null before it convened.</summary>
    internal Route Route => route;

    /// <summary>Whether that member already said where it stood — this golem, once it convened.</summary>
    internal bool Knows(Member member)
    {
        if (member == null) throw new GolemDomainException("Muster.Knows: 'member' was not given");
        return stood.ContainsKey(member.Name);
    }

    /// <summary>THE FIRST LAW, with what is known: over every member that spoke, the pairs (member, place) from the shortest to the
    /// longest — a tie by name, then by place — each member and each place taken once. The place that member gets.</summary>
    internal Position PlaceOf(Member member)
    {
        if (member == null) throw new GolemDomainException("Muster.PlaceOf: 'member' was not given");
        if (!Table().TryGetValue(member.Name, out var at)) throw new GolemDomainException($"'{member.Name}' has not said where it stands in the call {Call}");
        return at;
    }

    /// <summary>This golem convenes — once: it is recorded where it stands and given the route to the best place it knows now
    /// (<c>route = g.Choreography.Convene(from, muster, me)</c>).</summary>
    internal Route Convene(Position from, Member member)
    {
        if (from == null) throw new GolemDomainException("Muster.Convene: 'from' was not given");
        if (member == null) throw new GolemDomainException("Muster.Convene: 'member' was not given");
        if (me != null) throw new GolemDomainException($"'{me.Name}' already convened for the call {Call}");
        if (member.Of != Fleet.Count || member.Rank >= Fleet.Names.Count || Fleet.Names[member.Rank] != member.Name)
            throw new GolemDomainException($"'{member.Name}' is not a member of the fleet of the call {Call}");
        me = member;
        stood[me.Name] = new Position(from.X, from.Y);
        place = PlaceOf(me);
        route = takePlace(from, place, Where(place));
        return route;
    }

    /// <summary>A PEER'S WORD — where it stood when it convened. Recorded; if this golem already convened, the law is applied again.
    /// When the peer took its place: with the route still pending — an order of it in the body — the route YIELDS (propuesta 74): it
    /// asks the body to stop, the new place is kept, and the route that replaces it opens once the body said where it stood
    /// (<see cref="Halted"/>); with the route completed, the pose of its last arrival is the truth and the new route opens at once from
    /// there. A route that ended short (a reset, a failure) is not revived by a word; a word to a route that already yields only
    /// moves the place kept. Else the same route comes back. Before this golem convened, the word is only kept (null comes back).</summary>
    internal Route Stood(Member peer, Position at)
    {
        if (peer == null) throw new GolemDomainException("Muster.Stood: 'peer' was not given");
        if (at == null) throw new GolemDomainException("Muster.Stood: 'at' was not given");
        if (me != null && peer.Name == me.Name) throw new GolemDomainException($"'{peer.Name}' is this golem: its own word is its Convene");
        stood[peer.Name] = new Position(at.X, at.Y);
        if (me == null) return null;
        if (route.EndedShort) return route;
        var now = PlaceOf(me);
        if (now.DistanceTo(place) < 1e-6) return route;
        place = now;
        yieldedTo = peer.Name;
        if (route.IsPending())
        {
            if (!route.Yielding) route.Yield();   // the body is carrying an order of it: it stops first and says where it stood
            return route;
        }
        route = takePlace(route.Standing, place, Where(place));
        yieldedTo = null;
        return route;
    }

    /// <summary>The body STOPPED for the route that yields, and says where it stood (propuesta 74): that route is abandoned in the words
    /// of the peer that took its place, and the route that replaces it opens from where the body really is, to the place kept —
    /// <c>route = muster.Halted(me)</c>. Refused when this golem's route does not yield.</summary>
    internal Route Halted(Pose me)
    {
        if (me == null) throw new GolemDomainException("Muster.Halted: 'me' was not given");
        if (route == null || !route.Yielding || !route.IsPending()) throw new GolemDomainException($"no route of the call {Call} yields its place: nothing to halt");
        route.Abandon($"{yieldedTo} took this place by distance: {this.me.Name} goes to ({Fmt(place.X)}, {Fmt(place.Y)})");
        route = takePlace(me, place, Where(place));
        yieldedTo = null;
        return route;
    }

    private IReadOnlyDictionary<string, Position> Table()
    {
        var places = Formation.Places(Fleet.Count);
        var pairs = new List<(double Distance, string Name, int Place)>();
        foreach (var said in stood)
            for (int i = 0; i < places.Count; i++) pairs.Add((said.Value.DistanceTo(places[i]), said.Key, i));
        pairs.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance)
                           : string.CompareOrdinal(a.Name, b.Name) != 0 ? string.CompareOrdinal(a.Name, b.Name)
                           : a.Place.CompareTo(b.Place));
        var taken = new HashSet<int>();
        var table = new Dictionary<string, Position>();
        foreach (var pair in pairs)
        {
            if (table.ContainsKey(pair.Name) || taken.Contains(pair.Place)) continue;
            table[pair.Name] = places[pair.Place];
            taken.Add(pair.Place);
        }
        return table;
    }

    private string Where(Position at) => $"the place of {me.Name} in the {Formation.Name} by distance at ({Fmt(at.X)}, {Fmt(at.Y)})";
    private static string Fmt(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
}
