using System.Globalization;
using GolemDomain.Geometry;
using GolemDomain.Routes;
using GolemDomain.Touches;

namespace GolemDomain.Formations;

/// <summary>Who holds a place of a formation, as this golem's copy knows it: the member's name, the index of its place in the figure's
/// fixed order, where that place stands, and the orbit (the ring) it lies in.</summary>
internal sealed class Holder
{
    internal string Name { get; }
    internal int Index { get; }
    internal Position At { get; }
    internal int Orbit { get; }
    internal double X => At.X;
    internal double Y => At.Y;

    internal Holder(string name, int index, Position at, int orbit)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("Holder.Holder: 'name' was not given");
        if (at == null) throw new GolemDomainException("Holder.Holder: 'at' was not given");
        Name = name; Index = index; At = at; Orbit = orbit;
    }
}

/// <summary>
/// THE FORMATION (propuesta 104, 8-oct-2026; Juan: "las formaciones le pertenecerán al golem… a los otros golems involucrados se les
/// proporcionará la misma formación… la lógica de las coreografías con esto de las formaciones, que al final vienen siendo la misma cosa"):
/// a NAME the warden gave it (<c>square-2</c>), its FIGURE, its FLEET, its POLICY — the places shared BY RANK (the names sorted) or BY
/// DISTANCE (who stands nearest, the first law) — and THE PLACE EVERY MEMBER HOLDS. Every golem of the fleet keeps its own copy, fed by the
/// same words, so all compute the same table. What was the CONVOCATION (<c>Muster</c>, ajustes 73–86) with a name: every golem CONVENES
/// (<see cref="Convene"/>, it says where it stands); every peer's word (<see cref="Stood"/>) is where it stood. SINCE AJUSTE 80 (1-oct-2026;
/// Juan: "sólo pedir la posición de los demás al inicio de una coreografía y luego ya no") THE ROUND OPENS THE ROUTES: nobody sets out until
/// every member said where it stands — the word that completes the round, the golem's own or the last heard, opens its route — so every
/// copy shares the same table before anyone moves, and the OTHER members enter the planner as berths (<see cref="Berths"/>). A later word,
/// by distance, applies the first law again and may make the route yield (<see cref="Halted"/>, propuesta 74). Then every golem says when
/// it stands on its place (<see cref="Placed"/>), moves are queued (<see cref="Queue"/> — the move the FIGURE says, <see cref="Figure.Rotate(Sense, Ring)"/>,
/// ajuste 84) and the next one opens once everybody is placed (<see cref="Step"/>): all set out together; a move of one ring of a double
/// ring leaves the other ring standing. <see cref="Join"/> stays in the repertoire: by rank, at once, for the tests.
/// </summary>
internal sealed class Formation
{
    private readonly Golem golem;                                        // whose copy this is: it opens every route to a place (ajustes 70, 78)
    private readonly bool byDistance;                                    // the policy, the formation's from birth (ajuste 80): rank, or who stands nearest
    private Position convenedFrom;                                       // where this golem said it stood, to open its route from once the round completes
    private readonly Dictionary<string, Position> stood = new();         // where each member said it stood, by name
    private readonly Dictionary<string, int> indices = new(StringComparer.Ordinal);   // THE PLACE EVERY MEMBER HOLDS: by rank from birth, by distance once it spoke; shifted by every step
    private readonly HashSet<string> placed = new(StringComparer.Ordinal);   // who said it stands on its place, this round
    private readonly Dictionary<int, HashSet<string>> aligned = new();      // who said it is lined up, BY ROUND (propuesta 106): a word may come before this copy reached that round
    private HashSet<string> movers = new(StringComparer.Ordinal);            // the members the round underway moves: whose line-up the start waits for (the take: everybody)
    private readonly Queue<(Move Move, string Id)> steps = new();        // the moves queued, in the order they were asked (ajuste 84: the figure's)
    private readonly HashSet<string> stepIds = new(StringComparer.Ordinal);
    private Member me;                                                   // this golem, once it joined or convened
    private Position place;                                             // the place it is going to, or holds
    private Route route;                                                // …and the route there
    private string yieldedTo;                                           // the peer whose word made that route yield, until it is replaced

    internal Formation(string name, Figure figure, Fleet fleet, Golem golem, bool byDistance) : this(name, figure, fleet, golem, byDistance, name) { }

    /// <param name="stamp">the ONCE of the formation (ajuste 71's call id, kept): the host mints it when the warden forms, every copy keeps the same, and
    /// every tell about the formation carries it, so a formation told again under the same name is a new round of words to the peers</param>
    internal Formation(string name, Figure figure, Fleet fleet, Golem golem, bool byDistance, string stamp)
    {
        if (string.IsNullOrWhiteSpace(stamp)) throw new GolemDomainException("a formation needs its stamp: the once of its call");
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("a formation needs a name");
        if (figure == null) throw new GolemDomainException("Formation.Formation: 'figure' was not given");
        if (fleet == null) throw new GolemDomainException("Formation.Formation: 'fleet' was not given");
        if (golem == null) throw new GolemDomainException("Formation.Formation: 'golem' was not given");
        Name = name.Trim().ToLowerInvariant();
        Stamp = stamp;
        Figure = figure;
        Fleet = fleet;
        this.golem = golem;
        this.byDistance = byDistance;
        figure.Places(fleet.Count);   // a figure the fleet does not fit is refused at birth
        if (!byDistance) foreach (var member in fleet.Names) indices[member] = fleet.Member(member).Rank;   // by rank every place is known from birth
    }

    /// <summary>The name the warden gave it, lower case: its identity in every journal of the fleet.</summary>
    internal string Name { get; }
    /// <summary>The once of this formation's call, minted by the host: what every tell about it carries, so the peers take each word once.</summary>
    internal string Stamp { get; }
    /// <summary>How the places are shared: <c>rank</c> (the names sorted) or <c>distance</c> (who stands nearest, the first law).</summary>
    internal string Policy => byDistance ? "distance" : "rank";
    internal Figure Figure { get; }
    internal Fleet Fleet { get; }
    /// <summary>Dissolved by the warden (propuesta 104): it stays in the journal's history and in no list.</summary>
    internal bool Dissolved { get; private set; }
    /// <summary>How many members said where they stood — this golem among them once it convened.</summary>
    internal int StoodCount => stood.Count;
    /// <summary>Every member of the fleet said where it stands: the round is complete, every copy has the same table, the routes open.</summary>
    internal bool IsComplete => stood.Count == Fleet.Count;
    /// <summary>This golem's route in the formation; null before it joined or convened.</summary>
    internal Route Route => route;
    /// <summary>This golem as a member of the fleet; null before it joined or convened.</summary>
    internal Member Me => me;
    /// <summary>The index, in the figure's fixed order of places, of the place this golem holds or heads to — by rank known from birth, by
    /// distance once the words are in; −1 before, and for a golem that is in no fleet.</summary>
    internal int PlaceIndex => golem.Name != null && indices.TryGetValue(golem.Name, out int i) ? i : -1;
    /// <summary>How many members said they stand on their place, this round.</summary>
    internal int PlacedCount => placed.Count;
    /// <summary>How many members said they are lined up for the round underway (propuesta 106).</summary>
    internal int AlignedCount => AlignedIn(Round).Count;
    private HashSet<string> AlignedIn(int round) => aligned.TryGetValue(round, out var set) ? set : aligned[round] = new HashSet<string>(StringComparer.Ordinal);
    /// <summary>This golem's route waits for the fleet's start and its own word is not recorded yet — what the arrival asks before writing
    /// <see cref="Aligned"/> (<c>Check(g.Choreography.Current.AwaitsMyWord)</c>).</summary>
    internal bool AwaitsMyWord => route != null && route.Waiting && me != null && !AlignedIn(Round).Contains(me.Name);
    /// <summary>Which ROUND the fleet is in: 0 while taking the formation, one more per step opened — the word "placed" of a round is
    /// not the word of the last (the once of the tell that spreads it carries the round, ajuste 79).</summary>
    internal int Round { get; private set; }
    /// <summary>How many steps wait in the queue.</summary>
    internal int Queued => steps.Count;
    /// <summary>Whether the next step may open now: this golem has its route, EVERY member said it stands on its place, and a step is queued.</summary>
    internal bool CanStep => route != null && steps.Count > 0 && placed.SetEquals(Fleet.Names);
    /// <summary>How many places the figure has for this fleet — more than the fleet when vertices stay free (ajuste 86).</summary>
    internal int PlaceCount => Figure.Places(Fleet.Count).Count;

    /// <summary>WHO HOLDS WHAT, as this copy knows it (propuesta 104: the formations read): by rank every member from birth; by distance the
    /// members that spoke, by the table; after every step, where the step took them. In the fleet's order.</summary>
    internal IReadOnlyList<Holder> Holders()
    {
        var places = Figure.Places(Fleet.Count);
        var holders = new List<Holder>();
        foreach (var name in Fleet.Names)
            if (indices.TryGetValue(name, out int index)) holders.Add(new Holder(name, index, places[index], Figure.OrbitOf(index, places.Count)));
        return holders;
    }

    /// <summary>Whether that member already said where it stood — this golem, once it convened.</summary>
    internal bool Knows(Member member)
    {
        if (member == null) throw new GolemDomainException("Formation.Knows: 'member' was not given");
        return stood.ContainsKey(member.Name);
    }

    /// <summary>This golem STANDS on its place and has not said so yet: its route reached the place and its own word is not recorded —
    /// what the arrival asks before writing <see cref="Placed"/> (<c>Check(g.Choreography.Current.Reached)</c>).</summary>
    internal bool Reached => route != null && IsReached(route) && !placed.Contains(me.Name);

    /// <summary>Whether that route is this golem's route to its place in this formation, and it reached it.</summary>
    internal bool IsReached(Route candidate)
    {
        if (candidate == null) throw new GolemDomainException("Formation.IsReached: 'candidate' was not given");
        return ReferenceEquals(candidate, route) && !route.IsPending() && !route.EndedShort;
    }

    /// <summary>THE FIRST LAW, with what is known: over every member that spoke, the pairs (member, place) from the shortest to the
    /// longest — a tie by name, then by place — each member and each place taken once. The place that member gets.</summary>
    internal Position PlaceOf(Member member)
    {
        if (member == null) throw new GolemDomainException("Formation.PlaceOf: 'member' was not given");
        if (!Table().TryGetValue(member.Name, out int index)) throw new GolemDomainException($"'{member.Name}' has not said where it stands in {Name}");
        return Figure.Places(Fleet.Count)[index];
    }

    /// <summary>This golem JOINS BY RANK (propuesta 59, paso 1): the place of its rank among as many places as the fleet has bodies, and the
    /// route to it — <c>route = formation.Join(from, me);</c>. Once. The repertoire's, for the tests: the scripts convene.</summary>
    internal Route Join(Position from, Member member)
    {
        if (from == null) throw new GolemDomainException("Formation.Join: 'from' was not given");
        if (member == null) throw new GolemDomainException("Formation.Join: 'member' was not given");
        MustBeFree(member);
        me = member;
        stood[me.Name] = new Position(from.X, from.Y);
        indices[me.Name] = member.Rank;
        place = Figure.Place(member);
        route = golem.TakePlace(from, place, Figure.Center, Berths(), $"place {me.Rank + 1} of {me.Of} of the {Figure.Name} at ({Fmt(place.X)}, {Fmt(place.Y)})");
        return route;
    }

    /// <summary>This golem CONVENES — once: it says where it stands (<c>formation.Convene(from, me)</c>). When with its word the
    /// round is complete, its route to its place opens and comes back (<see cref="Route"/> holds it); else nothing yet (null): the word
    /// that completes the round will open it (<see cref="Stood"/>). Ajuste 80: nobody sets out before everybody spoke.</summary>
    internal Route Convene(Position from, Member member)
    {
        if (from == null) throw new GolemDomainException("Formation.Convene: 'from' was not given");
        if (member == null) throw new GolemDomainException("Formation.Convene: 'member' was not given");
        if (Dissolved) throw new GolemDomainException($"{Name} was dissolved");
        MustBeFree(member);
        if (!byDistance) golem.CheckPlace(Figure.Place(member), $"place {member.Rank + 1} of {member.Of} of the {Figure.Name}");   // known at once by rank: refused now, not after the fleet waited
        me = member;
        convenedFrom = from;
        stood[me.Name] = new Position(from.X, from.Y);
        if (byDistance) Retable();
        return IsComplete ? Open() : null;
    }

    // The round is complete: this golem's place by the policy, and the route there from where it said it stood — the other members as
    // berths, so the way goes around them (ajuste 80).
    private Route Open()
    {
        place = Figure.Places(Fleet.Count)[indices[me.Name]];
        route = golem.TakePlace(convenedFrom, place, Figure.Center, Berths(), Where(place));
        movers = Fleet.Names.ToHashSet(StringComparer.Ordinal);   // the take moves everybody: the fleet sets out together once every body faces its place (propuesta 106)
        route.StartTogether();
        return route;
    }

    /// <summary>The OTHER members as bodies in the way (ajuste 80): where each said it stands and the place it gets — by rank its rank's,
    /// by distance the table's, once it spoke. What the golem plans around when it opens its route to its place.</summary>
    internal IReadOnlyList<Peer> Berths()
    {
        var berths = new List<Peer>();
        var places = Figure.Places(Fleet.Count);
        foreach (var name in Fleet.Names)
        {
            if (me != null && name == me.Name) continue;
            if (stood.TryGetValue(name, out var at)) berths.Add(new Peer(name, at));
            if (indices.TryGetValue(name, out int index)) berths.Add(new Peer(name, places[index]));
        }
        return berths;
    }

    /// <summary>A PEER'S WORD — where it stood when it convened. Recorded. If with it the round is complete and this golem convened but
    /// has no route yet, its route opens and comes back (ajuste 80). Already on its way, by rank nothing moves it; by distance the law is
    /// applied again (a corrected word): when the peer took its place, with the route still pending — an order of it in the body — the
    /// route YIELDS (propuesta 74): it asks the body to stop, the new place is kept, and the route that replaces it opens once the body said
    /// where it stood (<see cref="Halted"/>); with the route completed, the pose of its last arrival is the truth and the new route opens
    /// at once from there. A route that ended short is not revived by a word; a word after a step changes nothing (the round of words is
    /// the formation's start). Before this golem convened, the word is only kept (null).</summary>
    internal Route Stood(Member peer, Position at)
    {
        if (peer == null) throw new GolemDomainException("Formation.Stood: 'peer' was not given");
        if (at == null) throw new GolemDomainException("Formation.Stood: 'at' was not given");
        if (me != null && peer.Name == me.Name) throw new GolemDomainException($"'{peer.Name}' is this golem: its own word is its Convene");
        if (!Fleet.Names.Contains(peer.Name)) throw new GolemDomainException($"'{peer.Name}' is not a member of the fleet of {Name}");
        if (Round > 0) return route;
        stood[peer.Name] = new Position(at.X, at.Y);
        if (byDistance) Retable();
        if (me == null) return null;
        if (route == null) return IsComplete ? Open() : null;
        if (!byDistance) return route;                            // by rank the place is the rank's: no word moves it
        if (route.EndedShort) return route;
        var now = Figure.Places(Fleet.Count)[indices[me.Name]];
        if (now.DistanceTo(place) < 1e-6) return route;
        place = now;
        yieldedTo = peer.Name;
        if (route.IsPending() && !route.Waiting)
        {
            if (!route.Yielding) route.Yield();   // the body is carrying an order of it: it stops first and says where it stood
            return route;
        }
        if (route.IsPending()) route.Abandon($"{yieldedTo} took this place by distance: {me.Name} goes to ({Fmt(place.X)}, {Fmt(place.Y)})");   // waiting, lined up, the body stands: nothing to halt (propuesta 106)
        else placed.Remove(me.Name);              // it leaves the place it had reached
        route = golem.TakePlace(route.Standing, place, Figure.Center, Berths(), Where(place));
        route.StartTogether();                    // the same round: the others' line-up words stay counted, so it sets out as soon as its own body is lined up
        yieldedTo = null;
        return route;
    }

    /// <summary>The body STOPPED for the route that yields, and says where it stood (propuesta 74): that route is abandoned in the words
    /// of the peer that took its place, and the route that replaces it opens from where the body really is, to the place kept —
    /// <c>route = formation.Halted(me)</c>. Refused when this golem's route does not yield.</summary>
    internal Route Halted(Pose me)
    {
        if (me == null) throw new GolemDomainException("Formation.Halted: 'me' was not given");
        if (route == null || !route.Yielding || !route.IsPending()) throw new GolemDomainException($"no route of {Name} yields its place: nothing to halt");
        route.Abandon($"{yieldedTo} took this place by distance: {this.me.Name} goes to ({Fmt(place.X)}, {Fmt(place.Y)})");
        route = golem.TakePlace(me, place, Figure.Center, Berths(), Where(place));
        route.StartTogether();                    // the same round: it sets out as soon as its own body is lined up (propuesta 106)
        yieldedTo = null;
        return route;
    }

    /// <summary>THIS GOLEM STANDS ON ITS PLACE (ajuste 77) — its own word, written by the arrival when its route reached the place:
    /// <c>route = formation.Placed(me)</c>. Recorded for this round; when with it EVERY member is placed and a step is queued, the next step
    /// opens and its route comes back (<see cref="Step"/>); else the route in place comes back, so the print is the same as ever. Told to
    /// every peer by the reaction on this act — so a peer's word is another act, <see cref="Heard"/>, and the reaction never meets it.</summary>
    internal Route Placed(Member member)
    {
        if (member == null) throw new GolemDomainException("Formation.Placed: 'member' was not given");
        if (me == null || member.Name != me.Name) throw new GolemDomainException($"'{member.Name}' is not this golem: a peer's word is Heard");
        return Record(member);
    }

    /// <summary>A PEER'S WORD — it stands on its place (by tell, <c>PlacedAt</c>): <c>route = formation.Heard(peer)</c>. Recorded like this
    /// golem's own; the step opens the same way when the round is complete. Before this golem has a route, the word is only kept (null
    /// comes back).</summary>
    internal Route Heard(Member peer)
    {
        if (peer == null) throw new GolemDomainException("Formation.Heard: 'peer' was not given");
        if (me != null && peer.Name == me.Name) throw new GolemDomainException($"'{peer.Name}' is this golem: its own word is Placed");
        return Record(peer);
    }

    /// <summary>THIS GOLEM IS LINED UP for the round underway (propuesta 106, 9-oct-2026; Juan: "que todos se sincronicen en posición de salida y
    /// avancen a la vez"; then: "cuando tienen que moverse todos a un nuevo centro… todos deberían moverse juntos si ya están en el ángulo
    /// correcto"): its body turned to face its place and stands — <c>route = formation.Aligned(me)</c>, written by the arrival of that turn —
    /// on the take and on every step. Told to every peer by the reaction on this act (<c>AlignedAt</c>, with the round); a peer's word is
    /// <see cref="HeardAligned(Member, int)"/>. When every mover of the round is lined up, this copy lets its route go (<see cref="Route.Go"/>):
    /// the print turns from stop to advance, and the fleet sets out within the tells' latency of each other.</summary>
    internal Route Aligned(Member member)
    {
        if (member == null) throw new GolemDomainException("Formation.Aligned: 'member' was not given");
        if (me == null || member.Name != me.Name) throw new GolemDomainException($"'{member.Name}' is not this golem: a peer's word is HeardAligned");
        if (route == null || !route.Waiting) throw new GolemDomainException($"'{Name}': this golem's route is not waiting for the fleet's start");
        return RecordAligned(member, Round);
    }

    /// <summary>A PEER'S WORD — it is lined up for that round (by tell, <c>AlignedAt</c>, which carries the round): <c>route =
    /// formation.HeardAligned(peer, @round)</c>. Kept in its round even before this copy reached it (the words travel faster than the last
    /// placed one, at times); the start is given the same way. The words of a round stay until the formation leaves it, so a route opened
    /// again in the same round (by distance, a corrected word) starts as soon as its own body is lined up.</summary>
    internal Route HeardAligned(Member peer, int round)
    {
        if (peer == null) throw new GolemDomainException("Formation.HeardAligned: 'peer' was not given");
        if (me != null && peer.Name == me.Name) throw new GolemDomainException($"'{peer.Name}' is this golem: its own word is Aligned");
        if (round < 0) throw new GolemDomainException($"Formation.HeardAligned: a round is never negative ({round})");
        return RecordAligned(peer, round);
    }

    /// <summary>A peer's word for the round underway (the tests' shorthand).</summary>
    internal Route HeardAligned(Member peer) => HeardAligned(peer, Round);

    private Route RecordAligned(Member member, int round)
    {
        if (!Fleet.Names.Contains(member.Name)) throw new GolemDomainException($"'{member.Name}' is not a member of the fleet of {Name}");
        AlignedIn(round).Add(member.Name);
        if (route != null && route.Waiting && round == Round && movers.All(AlignedIn(Round).Contains)) route.Go();   // THE FLEET'S START: everybody who moves is lined up
        return route;
    }

    private Route Record(Member member)
    {
        if (!Fleet.Names.Contains(member.Name)) throw new GolemDomainException($"'{member.Name}' is not a member of the fleet of {Name}");
        placed.Add(member.Name);
        if (route == null) return null;
        return CanStep ? Step() : route;
    }

    /// <summary>A MOVE is queued (ajuste 77; Juan: "una lista de formación/coreografías para cumplir todas"; ajuste 84: the move is the
    /// FIGURE's — <c>move = formation.Figure.Rotate(@sense, @ring); formation.Queue(move, @stepId);</c>): every body does it once everybody
    /// stands on its place. Nothing moves here. The step's id is the once of the tell that spreads it: the same step twice is refused. How
    /// many wait comes back.</summary>
    internal int Queue(Move move, string stepId)
    {
        if (move == null) throw new GolemDomainException("Formation.Queue: 'move' was not given");
        if (string.IsNullOrWhiteSpace(stepId)) throw new GolemDomainException("Formation.Queue: 'stepId' was not given");
        if (Dissolved) throw new GolemDomainException($"{Name} was dissolved");
        if (!stepIds.Add(stepId)) throw new GolemDomainException($"the step {stepId} is already queued in {Name}");
        steps.Enqueue((move, stepId));
        return steps.Count;
    }

    /// <summary>The next step OPENS (ajuste 77): every member stands on its place and a move waits — every member the move touches heads to
    /// the next place along its orbit (<see cref="Move.Next"/>: the figure's own geometry, ajuste 84), this golem from where its route in
    /// place left the body; a member of a ring the move leaves alone stays placed. A new round begins. <c>route = formation.Step();</c>.
    /// Refused when it cannot step yet.</summary>
    internal Route Step()
    {
        if (route == null) throw new GolemDomainException($"'{Name}': this golem has no place in the formation yet");
        if (steps.Count == 0) throw new GolemDomainException($"'{Name}': no step is queued");
        if (!placed.SetEquals(Fleet.Names)) throw new GolemDomainException($"'{Name}': {placed.Count} of {Fleet.Count} stand on their places — the step waits for everybody");
        var (move, _) = steps.Dequeue();
        var places = Figure.Places(Fleet.Count);           // the figure's places — more than the fleet when vertices stay free (ajuste 86): the hole moves with everybody
        var orbits = Figure.Orbits(places.Count);
        var staying = new List<string>();
        foreach (var name in indices.Keys.ToList())
        {
            int orbit = Figure.OrbitOf(indices[name], places.Count);
            if (!move.Moves(orbit)) { staying.Add(name); continue; }
            indices[name] = move.Next(indices[name], orbits[orbit].Start, orbits[orbit].Count);
        }
        placed.Clear();
        foreach (var name in staying) placed.Add(name);      // a ring the move leaves alone is still in place
        movers = indices.Keys.Where(name => !staying.Contains(name)).ToHashSet(StringComparer.Ordinal);   // whose line-up the start waits for (propuesta 106)
        aligned.Remove(Round);                                // the words of the round left behind
        Round++;
        if (staying.Contains(me.Name)) return route;          // this golem does not move on this step: its route in place stands
        int next = indices[me.Name];
        place = places[next];
        route = golem.TakePlace(route.Standing, place, Figure.Center, Array.Empty<Peer>(), $"place {next + 1} of {places.Count} of the {Figure.Name}, {move.Name}, at ({Fmt(place.X)}, {Fmt(place.Y)})");   // in lockstep: no berths, the one ahead leaves as I come
        route.StartTogether();                                // the body turns to face its next place, then stands until every mover is lined up (propuesta 106)
        return route;
    }

    /// <summary>DISSOLVED by the warden (propuesta 104): a route of this golem still pending is abandoned, nothing is queued any more, and
    /// the formation leaves the golem's list. Journaled like everything, so the history keeps what the fleet had formed.</summary>
    internal void Dissolve()
    {
        if (Dissolved) return;
        Dissolved = true;
        if (route != null && route.IsPending()) route.Abandon($"{Name} was dissolved");
        steps.Clear();
    }

    // by distance: the table of who gets what, over every member that spoke (the first law), kept as the indices of the ones who spoke
    private void Retable()
    {
        foreach (var pair in Table()) indices[pair.Key] = pair.Value;
    }

    private void MustBeFree(Member member)
    {
        if (me != null) throw new GolemDomainException($"'{me.Name}' already took its place in {Name}");
        if (member.Of != Fleet.Count || member.Rank >= Fleet.Names.Count || Fleet.Names[member.Rank] != member.Name)
            throw new GolemDomainException($"'{member.Name}' is not a member of the fleet of {Name}");
    }

    private IReadOnlyDictionary<string, int> Table()
    {
        var places = Figure.Places(Fleet.Count);
        var pairs = new List<(double Distance, string Name, int Place)>();
        foreach (var said in stood)
            for (int i = 0; i < places.Count; i++) pairs.Add((said.Value.DistanceTo(places[i]), said.Key, i));
        pairs.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance)
                           : string.CompareOrdinal(a.Name, b.Name) != 0 ? string.CompareOrdinal(a.Name, b.Name)
                           : a.Place.CompareTo(b.Place));
        var taken = new HashSet<int>();
        var table = new Dictionary<string, int>();
        foreach (var pair in pairs)
        {
            if (table.ContainsKey(pair.Name) || taken.Contains(pair.Place)) continue;
            table[pair.Name] = pair.Place;
            taken.Add(pair.Place);
        }
        return table;
    }

    private string Where(Position at) => $"the place of {me.Name} in {Name} by {Policy} at ({Fmt(at.X)}, {Fmt(at.Y)})";
    private static string Fmt(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
}
