using System.Globalization;
using GolemDomain.Geometry;
using GolemDomain.Routes;
using GolemDomain.Touches;

namespace GolemDomain.Formations;

/// <summary>
/// A CONVOCATION — the fleet called to a formation (ajuste 73; propuesta 64, PLAN-coreografia-disputa §13 and §14), BY RANK or BY
/// DISTANCE, and since ajuste 77 (1-oct-2026) THE FORMATION IN PLACE: it remembers this golem's route and the INDEX of its place, so the
/// fleet can take the next places STEP BY STEP (Juan: "que la flota tome la posición del otro en el sentido de las agujas del reloj y
/// antihorario… se pueden encolar"). Its identity is the CALL (<c>@callId</c>): two calls to the same square are two convocations.
/// Every golem CONVENES (<see cref="Convene"/>): it says where it stands; every peer's word (<see cref="Stood"/>) is where it stood. SINCE AJUSTE 80 (1-oct-2026; Juan:
/// "sólo pedir la posición de los demás al inicio de una coreografía y luego ya no") THE ROUND OPENS THE ROUTES: nobody sets out until
/// every member said where it stands — the word that completes the round, the golem's own or the last heard, opens its route — so every
/// copy shares the same table before anyone moves, by rank or by distance (the policy is the convocation's from birth), and the OTHER
/// members enter the planner as berths (<see cref="Berths"/>): the route goes around them from its first leg. A later word, by distance,
/// applies the first law again and may make the route yield (<see cref="Halted"/>, propuesta 74). Then every golem says when it stands
/// on its place (<see cref="Placed"/>), moves are queued (<see cref="Queue"/> — the move the FIGURE says, <see cref="Formation.Rotate"/>,
/// ajuste 84) and the next one opens once everybody is placed (<see cref="Step"/>): all set out together, each following the one ahead
/// along the same side. <see cref="Join"/> stays in the
/// repertoire: by rank, at once, for the tests.
/// </summary>
internal sealed class Muster
{
    private readonly Golem golem;                                        // whose convocation this is: it opens every route to a place (ajustes 70, 78)
    private readonly bool byDistance;                                    // the policy, the convocation's from birth (ajuste 80): rank, or who stands nearest
    private Position convenedFrom;                                       // where this golem said it stood, to open its route from once the round completes
    private readonly Dictionary<string, Position> stood = new();         // by distance: where each member said it stood, by name
    private readonly HashSet<string> placed = new(StringComparer.Ordinal);   // who said it stands on its place, this round
    private readonly Queue<(Move Move, string Id)> steps = new();        // the moves queued, in the order they were asked (ajuste 84: the figure's)
    private readonly HashSet<string> stepIds = new(StringComparer.Ordinal);
    private Member me;                                                   // this golem, once it joined or convened
    private int placeIndex = -1;                                        // …the index of the place it is going to, in the formation's order
    private Position place;                                             // …that place
    private Route route;                                                // …and the route there
    private string yieldedTo;                                           // the peer whose word made that route yield, until it is replaced

    internal Muster(string call, Formation formation, Fleet fleet, Golem golem, bool byDistance)
    {
        if (string.IsNullOrWhiteSpace(call)) throw new GolemDomainException("a convocation needs its call");
        if (formation == null) throw new GolemDomainException("Muster.Muster: 'formation' was not given");
        if (fleet == null) throw new GolemDomainException("Muster.Muster: 'fleet' was not given");
        if (golem == null) throw new GolemDomainException("Muster.Muster: 'golem' was not given");
        Call = call;
        Formation = formation;
        Fleet = fleet;
        this.golem = golem;
        this.byDistance = byDistance;
    }

    internal string Call { get; }
    /// <summary>How the places are shared: <c>rank</c> (the names sorted) or <c>distance</c> (who stands nearest, the first law).</summary>
    internal string Policy => byDistance ? "distance" : "rank";
    internal Formation Formation { get; }
    internal Fleet Fleet { get; }
    /// <summary>How many members said where they stood — this golem among them once it convened.</summary>
    internal int StoodCount => stood.Count;
    /// <summary>Every member of the fleet said where it stands: the round is complete, every copy has the same table, the routes open.</summary>
    internal bool IsComplete => stood.Count == Fleet.Count;
    /// <summary>This golem's route in the convocation; null before it joined or convened.</summary>
    internal Route Route => route;
    /// <summary>This golem as a member of the fleet; null before it joined or convened.</summary>
    internal Member Me => me;
    /// <summary>The index, in the formation's fixed order of places, of the place this golem holds or heads to; −1 before.</summary>
    internal int PlaceIndex => placeIndex;
    /// <summary>How many members said they stand on their place, this round.</summary>
    internal int PlacedCount => placed.Count;
    /// <summary>Which ROUND the fleet is in: 0 while taking the formation, one more per step opened — the word "placed" of a round is
    /// not the word of the last (the once of the tell that spreads it carries the round, ajuste 79).</summary>
    internal int Round { get; private set; }
    /// <summary>How many steps wait in the queue.</summary>
    internal int Queued => steps.Count;
    /// <summary>Whether the next step may open now: this golem has its route, EVERY member said it stands on its place, and a step is queued.</summary>
    internal bool CanStep => route != null && steps.Count > 0 && placed.SetEquals(Fleet.Names);

    /// <summary>Whether that member already said where it stood (by distance) — this golem, once it convened.</summary>
    internal bool Knows(Member member)
    {
        if (member == null) throw new GolemDomainException("Muster.Knows: 'member' was not given");
        return stood.ContainsKey(member.Name);
    }

    /// <summary>This golem STANDS on its place and has not said so yet: its route reached the place and its own word is not recorded —
    /// what the arrival asks before writing <see cref="Placed"/> (<c>Check(g.Choreography.Current.Reached)</c>).</summary>
    internal bool Reached => route != null && IsReached(route) && !placed.Contains(me.Name);

    /// <summary>Whether that route is this golem's route to its place in this convocation, and it reached it.</summary>
    internal bool IsReached(Route candidate)
    {
        if (candidate == null) throw new GolemDomainException("Muster.IsReached: 'candidate' was not given");
        return ReferenceEquals(candidate, route) && !route.IsPending() && !route.EndedShort;
    }

    /// <summary>THE FIRST LAW, with what is known: over every member that spoke, the pairs (member, place) from the shortest to the
    /// longest — a tie by name, then by place — each member and each place taken once. The place that member gets.</summary>
    internal Position PlaceOf(Member member)
    {
        if (member == null) throw new GolemDomainException("Muster.PlaceOf: 'member' was not given");
        if (!Table().TryGetValue(member.Name, out var at)) throw new GolemDomainException($"'{member.Name}' has not said where it stands in the call {Call}");
        return at;
    }

    /// <summary>This golem JOINS BY RANK (propuesta 59, paso 1; through the convocation since ajuste 77): the place of its rank among as
    /// many places as the fleet has bodies, and the route to it — <c>route = muster.Join(from, me);</c>. Once.</summary>
    internal Route Join(Position from, Member member)
    {
        if (from == null) throw new GolemDomainException("Muster.Join: 'from' was not given");
        if (member == null) throw new GolemDomainException("Muster.Join: 'member' was not given");
        MustBeFree(member);
        me = member;
        stood[me.Name] = new Position(from.X, from.Y);
        placeIndex = member.Rank;
        place = Formation.Place(member);
        route = golem.TakePlace(from, place, Formation.Center, Berths(), $"place {me.Rank + 1} of {me.Of} of the {Formation.Name} at ({Fmt(place.X)}, {Fmt(place.Y)})");
        return route;
    }

    /// <summary>This golem CONVENES — once: it says where it stands (<c>muster.Convene(from, me)</c>). When with its word the
    /// round is complete, its route to its place opens and comes back (<see cref="Route"/> holds it); else nothing yet (null): the word
    /// that completes the round will open it (<see cref="Stood"/>). Ajuste 80: nobody sets out before everybody spoke.</summary>
    internal Route Convene(Position from, Member member)
    {
        if (from == null) throw new GolemDomainException("Muster.Convene: 'from' was not given");
        if (member == null) throw new GolemDomainException("Muster.Convene: 'member' was not given");
        MustBeFree(member);
        if (!byDistance) golem.CheckPlace(Formation.Place(member), $"place {member.Rank + 1} of {member.Of} of the {Formation.Name}");   // known at once by rank: refused now, not after the fleet waited
        me = member;
        convenedFrom = from;
        stood[me.Name] = new Position(from.X, from.Y);
        return IsComplete ? Open() : null;
    }

    // The round is complete: this golem's place by the policy, and the route there from where it said it stood — the other members as
    // berths, so the way goes around them (ajuste 80).
    private Route Open()
    {
        Head(byDistance ? PlaceOf(me) : Formation.Place(me));
        route = golem.TakePlace(convenedFrom, place, Formation.Center, Berths(), Where(place));
        return route;
    }

    /// <summary>The OTHER members as bodies in the way (ajuste 80): where each said it stands and the place it gets — by rank its rank's,
    /// by distance the table's, once it spoke. What the golem plans around when it opens its route to its place.</summary>
    internal IReadOnlyList<Peer> Berths()
    {
        var berths = new List<Peer>();
        var table = byDistance ? Table() : null;
        foreach (var name in Fleet.Names)
        {
            if (me != null && name == me.Name) continue;
            if (stood.TryGetValue(name, out var at)) berths.Add(new Peer(name, at));
            Position going = byDistance ? (table.TryGetValue(name, out var given) ? given : null) : Formation.Place(Fleet.Member(name));
            if (going != null) berths.Add(new Peer(name, going));
        }
        return berths;
    }

    /// <summary>A PEER'S WORD — where it stood when it convened. Recorded. If with it the round is complete and this golem convened but
    /// has no route yet, its route opens and comes back (ajuste 80). Already on its way, by rank nothing moves it; by distance the law is
    /// applied again (a corrected word): when the peer took its place, with the route still pending — an order of it in the body — the
    /// route YIELDS (propuesta 74): it asks the body to stop, the new place is kept, and the route that replaces it opens once the body said
    /// where it stood (<see cref="Halted"/>); with the route completed, the pose of its last arrival is the truth and the new route opens
    /// at once from there. A route that ended short is not revived by a word. Before this golem convened, the word is only kept (null).</summary>
    internal Route Stood(Member peer, Position at)
    {
        if (peer == null) throw new GolemDomainException("Muster.Stood: 'peer' was not given");
        if (at == null) throw new GolemDomainException("Muster.Stood: 'at' was not given");
        if (me != null && peer.Name == me.Name) throw new GolemDomainException($"'{peer.Name}' is this golem: its own word is its Convene");
        stood[peer.Name] = new Position(at.X, at.Y);
        if (me == null) return null;
        if (route == null) return IsComplete ? Open() : null;
        if (!byDistance) return route;                            // by rank the place is the rank's: no word moves it
        if (route.EndedShort) return route;
        var now = PlaceOf(me);
        if (now.DistanceTo(place) < 1e-6) return route;
        Head(now);
        yieldedTo = peer.Name;
        if (route.IsPending())
        {
            if (!route.Yielding) route.Yield();   // the body is carrying an order of it: it stops first and says where it stood
            return route;
        }
        placed.Remove(me.Name);                   // it leaves the place it had reached
        route = golem.TakePlace(route.Standing, place, Formation.Center, Berths(), Where(place));
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
        route = golem.TakePlace(me, place, Formation.Center, Berths(), Where(place));
        yieldedTo = null;
        return route;
    }

    /// <summary>THIS GOLEM STANDS ON ITS PLACE (ajuste 77) — its own word, written by the arrival when its route reached the place:
    /// <c>route = muster.Placed(me)</c>. Recorded for this round; when with it EVERY member is placed and a step is queued, the next step
    /// opens and its route comes back (<see cref="Step"/>); else the route in place comes back, so the print is the same as ever. Told to
    /// every peer by the reaction on this act — so a peer's word is another act, <see cref="Heard"/>, and the reaction never meets it.</summary>
    internal Route Placed(Member member)
    {
        if (member == null) throw new GolemDomainException("Muster.Placed: 'member' was not given");
        if (me == null || member.Name != me.Name) throw new GolemDomainException($"'{member.Name}' is not this golem: a peer's word is Heard");
        return Record(member);
    }

    /// <summary>A PEER'S WORD — it stands on its place (by tell, <c>PlacedAt</c>): <c>route = muster.Heard(peer)</c>. Recorded like this
    /// golem's own; the step opens the same way when the round is complete. Before this golem has a route, the word is only kept (null
    /// comes back).</summary>
    internal Route Heard(Member peer)
    {
        if (peer == null) throw new GolemDomainException("Muster.Heard: 'peer' was not given");
        if (me != null && peer.Name == me.Name) throw new GolemDomainException($"'{peer.Name}' is this golem: its own word is Placed");
        return Record(peer);
    }

    private Route Record(Member member)
    {
        if (!Fleet.Names.Contains(member.Name)) throw new GolemDomainException($"'{member.Name}' is not a member of the fleet of the call {Call}");
        placed.Add(member.Name);
        if (route == null) return null;
        return CanStep ? Step() : route;
    }

    /// <summary>A MOVE is queued (ajuste 77; Juan: "una lista de formación/coreografías para cumplir todas"; ajuste 84: the move is the
    /// FIGURE's — <c>move = muster.Formation.Rotate(@sense); muster.Queue(move, @stepId);</c>): every body does it once everybody stands on
    /// its place. Nothing moves here. The step's id is the once of the tell that spreads it: the same step twice is refused. How many wait
    /// comes back.</summary>
    internal int Queue(Move move, string stepId)
    {
        if (move == null) throw new GolemDomainException("Muster.Queue: 'move' was not given");
        if (string.IsNullOrWhiteSpace(stepId)) throw new GolemDomainException("Muster.Queue: 'stepId' was not given");
        if (!stepIds.Add(stepId)) throw new GolemDomainException($"the step {stepId} is already queued in the call {Call}");
        steps.Enqueue((move, stepId));
        return steps.Count;
    }

    /// <summary>The next step OPENS (ajuste 77): every member stands on its place and a move waits — this golem heads to the place the
    /// move takes its own to (<see cref="Move.Next"/>: the figure's own geometry, ajuste 84), from where its route in place left the body;
    /// a new round begins. <c>route = muster.Step();</c>. Refused when it cannot step yet.</summary>
    internal Route Step()
    {
        if (route == null) throw new GolemDomainException($"'{Call}': this golem has no place in the call yet");
        if (steps.Count == 0) throw new GolemDomainException($"'{Call}': no step is queued");
        if (!placed.SetEquals(Fleet.Names)) throw new GolemDomainException($"'{Call}': {placed.Count} of {Fleet.Count} stand on their places — the step waits for everybody");
        var (move, _) = steps.Dequeue();
        int count = Fleet.Count;
        int next = move.Next(placeIndex, count);
        var places = Formation.Places(count);
        placeIndex = next;
        place = places[next];
        placed.Clear();
        Round++;
        route = golem.TakePlace(route.Standing, place, Formation.Center, Array.Empty<Peer>(), $"place {next + 1} of {count} of the {Formation.Name}, {move.Name}, at ({Fmt(place.X)}, {Fmt(place.Y)})");   // in lockstep: no berths, the one ahead leaves as I come
        return route;
    }

    // The place this golem heads to by distance, and its index in the formation's order.
    private void Head(Position at)
    {
        place = at;
        var places = Formation.Places(Fleet.Count);
        placeIndex = -1;
        for (int i = 0; i < places.Count; i++) if (places[i].DistanceTo(at) < 1e-6) { placeIndex = i; break; }
    }

    private void MustBeFree(Member member)
    {
        if (me != null) throw new GolemDomainException($"'{me.Name}' already took its place in the call {Call}");
        if (member.Of != Fleet.Count || member.Rank >= Fleet.Names.Count || Fleet.Names[member.Rank] != member.Name)
            throw new GolemDomainException($"'{member.Name}' is not a member of the fleet of the call {Call}");
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

    private string Where(Position at) => $"the place of {me.Name} in the {Formation.Name} by {Policy} at ({Fmt(at.X)}, {Fmt(at.Y)})";
    private static string Fmt(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
}
