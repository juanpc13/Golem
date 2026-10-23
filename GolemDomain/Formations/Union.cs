using System.Globalization;
using GolemDomain.Geometry;
using GolemDomain.Routes;
using GolemDomain.Units;

namespace GolemDomain.Formations;

/// <summary>
/// THE PROGRAMME (propuesta 123, 21-oct-2026; Juan: "sindicalizar parece ser lo de las formaciones, pero sin que uno elija a los golems como tal…
/// se auto-selecciona de acuerdo a las distancias… dos coreografías en una sola cosa… ambas como dos objetos de formación que a la vez están
/// conectadas… el CLI no debe resolverles el problema; los golems con la ayuda del dominio deben resolverlo en conjunto… por la posición en la que
/// están en el open floor pueden decidir qué les conviene más, si la derecha o la izquierda, y si ya están llenos se va a la otra"): the ONE THING
/// that names several OPEN formations in order and shares the golems among them. Nobody is told a fleet: the CANDIDATES are every golem the
/// union was told to (this golem and its peers), and ONE ROUND OF WORDS — each candidate says once where it stands, <see cref="Convene"/>;
/// a peer's word is <see cref="Stood"/> — completes when every candidate spoke AND every formation of the union was told here
/// (<see cref="IsKnown"/>: the two facts arrive in any order, the words kept ahead by the module and taken in when the union is told). Then the
/// LAW, the same in every copy, shares every candidate among EVERY PLACE OF EVERY FIGURE: by distance each one gets the free place nearest to
/// where it stood, over all the figures at once — a figure full sends the next one to the other — ties by name, then by the figure's order in the
/// union, then by the place; by rps the places wanted by several are played. Every copy concludes the same table, so each formation is
/// ENROLLED with its members (<see cref="Formation.Enrol"/>): the union says WHO, the formation says WHERE as it always did (its own policy over
/// its members' words); the first formation opens at once, one that waits (a cue, a pull) waits as it always did, among its members. More candidates
/// than places: the farthest are LEFT OUT and free (<see cref="LeftOut"/>). Fewer: the last figures get fewer bodies; one that would get nobody
/// leaves the union UNRESOLVED and says why (<see cref="CannotResolve"/>, <see cref="Why"/>) — nothing waits in silence.
/// Policy <c>Rank</c> is refused: by rank nobody chooses.
/// THE SCORE (propuesta 124, 22-oct-2026; Juan: "la unionize es la que tiene todo el plan, pero distribuido entre todos… es el que dirá qué es lo
/// siguiente que sigue… la secuencia sí la llevaría la unionize, pero la forma en como resuelven es con formaciones y con push y pulls"): every
/// formation of the union is TAKEN AT ONCE (the cue and the pull are for loose formations alone) and the steps queued in each one are its part
/// of the score, in order — the k-th step of every formation is the k-th BEAT of the union, danced TOGETHER: a beat opens when every formation
/// of the union has every member on its place (<see cref="CanStep"/>; the ones with a step queued step, <see cref="Step"/>; one with nothing
/// queued stays), and the movers of the beat set out together — the LINK: by the cue, when every mover of every formation is lined up
/// (<see cref="LetGoIfAligned"/>, the words <c>AlignedAt</c> across the union; by asking, to come). Every copy keeps the whole score (the warden
/// writes it on every tab) and steps every formation, its own and the others, so the holders stay right everywhere. The union is DONE when every
/// member of every formation finished (<see cref="IsDone"/>).
/// </summary>
internal sealed class Union
{
    private readonly Golem golem;                                         // whose copy this is
    private readonly Choreographies choreographies;                      // the module that keeps its formations (objects that know each other, ajuste 78)
    private readonly Policy policy;                                      // how the candidates are shared among the figures: distance, or the game
    private readonly Link link;                                          // how the beats are agreed among the golems (propuesta 124): the cue, or asking
    private readonly Duration every;                                     // how often the askers ask (by asking; null by the cue)
    private int beat;                                                    // how many beats the union stepped
    private readonly Dictionary<string, Position> stood = new(StringComparer.Ordinal);    // where each candidate said it stood
    private readonly Dictionary<string, string> nonces = new(StringComparer.Ordinal);     // the stamp each word carried: its hand at the game
    private readonly Dictionary<string, (string Formation, int Place)> table = new(StringComparer.Ordinal);   // the law's table, once resolved
    private readonly List<(string Place, string Winner, string Loser, string Game)> matches = new();
    private Member me;
    private Position convenedFrom;                                       // where THIS golem stood when it spoke — the pose, with its heading (22-oct-2026, night)

    internal Union(string name, string formationNames, Fleet candidates, Golem golem, Choreographies choreographies, Policy policy, string stamp)
        : this(name, formationNames, candidates, golem, choreographies, policy, Link.Cue, null, stamp) { }

    /// <summary>The same, WITH ITS LINK (propuesta 124): how its beats are agreed — the cue, or asking every so often.</summary>
    internal Union(string name, string formationNames, Fleet candidates, Golem golem, Choreographies choreographies, Policy policy, Link link, Duration every, string stamp)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("a union needs a name");
        if (formationNames == null) throw new GolemDomainException("Union.Union: 'formationNames' was not given");
        if (candidates == null) throw new GolemDomainException("Union.Union: 'candidates' was not given");
        if (golem == null) throw new GolemDomainException("Union.Union: 'golem' was not given");
        if (choreographies == null) throw new GolemDomainException("Union.Union: 'choreographies' was not given");
        if (string.IsNullOrWhiteSpace(stamp)) throw new GolemDomainException("a union needs its stamp: the once of its call");
        if (!Enum.IsDefined(policy)) throw new GolemDomainException($"the candidates are shared by Distance or RockPaperScissors, not by '{policy}'");
        if (policy == Policy.Rank) throw new GolemDomainException("a union shares its candidates by distance or rps: by rank nobody chooses");
        if (!Enum.IsDefined(link)) throw new GolemDomainException($"the beats are agreed by Cue or Ask, not by '{link}'");
        if (link == Link.Ask) throw new GolemDomainException("the beats by asking are not built yet: --link cue");
        if (every != null && every.IsZero) throw new GolemDomainException("a union asks every so often: zero is no period");
        var names = formationNames.Split(',').Select(n => n.Trim().ToLowerInvariant()).Where(n => n != "").ToList();
        if (names.Count == 0) throw new GolemDomainException("a union names at least one formation");
        if (names.Distinct().Count() != names.Count) throw new GolemDomainException("a union names each formation once");
        Name = name.Trim().ToLowerInvariant();
        if (names.Contains(Name)) throw new GolemDomainException($"the union {Name} cannot name itself as a formation");
        Names = names;
        Candidates = candidates;
        Stamp = stamp;
        this.golem = golem;
        this.choreographies = choreographies;
        this.policy = policy;
        this.link = link;
        this.every = every;
    }

    /// <summary>Its name: the identity of the show across the fleet, like a formation's.</summary>
    internal string Name { get; }
    /// <summary>The once of its call, minted by the host; every word of its round carries it.</summary>
    internal string Stamp { get; }
    /// <summary>The names of its formations, IN ORDER: the first dances first, the next waits for it (its own --after or --on-cue); the order breaks a
    /// tie of the law too.</summary>
    internal IReadOnlyList<string> Names { get; }
    /// <summary>Everybody who may dance: the golems the union was told to.</summary>
    internal Fleet Candidates { get; }
    internal Policy Policy => policy;
    /// <summary>How its beats are agreed (propuesta 124): by the cue, or by asking.</summary>
    internal Link Link => link;
    /// <summary>How often the askers ask, by asking; 0 by the cue.</summary>
    internal double EverySeconds => every?.InSeconds ?? 0.0;
    /// <summary>How many beats the union stepped: the k-th step of every formation, danced together.</summary>
    internal int Beat => beat;
    internal bool Dissolved { get; private set; }
    /// <summary>Resolved: the law ran, every formation is enrolled with its members.</summary>
    internal bool IsResolved { get; private set; }
    /// <summary>The round complete and the law could not share: a formation would get nobody. Says why. Nothing waits in silence.</summary>
    internal bool CannotResolve { get; private set; }
    internal string Why { get; private set; } = "";
    internal int StoodCount => stood.Count;
    /// <summary>Every formation of the union was told here, open and of this union.</summary>
    internal bool IsKnown => Names.All(n => choreographies.OpenFormationOf(n, Name) != null);
    /// <summary>Every candidate spoke and every figure is known: the law may run.</summary>
    internal bool IsComplete => stood.Count == Candidates.Count && IsKnown;
    /// <summary>The formations of the union told here, in its order.</summary>
    internal IReadOnlyList<Formation> Formations() => Names.Select(n => choreographies.OpenFormationOf(n, Name)).Where(f => f != null).ToList();

    /// <summary>Whether that candidate's word is awaited: of the candidates, not spoken yet.</summary>
    internal bool Awaits(string teller)
    {
        if (teller == null) throw new GolemDomainException("Union.Awaits: 'teller' was not given");
        string who = teller.Trim().ToLowerInvariant();
        return !Dissolved && Candidates.Names.Contains(who) && !stood.ContainsKey(who);
    }

    /// <summary>Whether this golem said where it stands already.</summary>
    internal bool Knows(Member member)
    {
        if (member == null) throw new GolemDomainException("Union.Knows: 'member' was not given");
        return stood.ContainsKey(member.Name);
    }

    /// <summary>THIS GOLEM'S WORD — once: where it stands, for the whole union (<c>union.Convene(from, me)</c>). When with it the round is
    /// complete, the law runs, every formation is enrolled, and this golem's route in its own formation comes back (null when that formation waits,
    /// when this golem was left out, or when the round is not complete yet).</summary>
    internal Route Convene(Position from, Member member)
    {
        if (from == null) throw new GolemDomainException("Union.Convene: 'from' was not given");
        if (member == null) throw new GolemDomainException("Union.Convene: 'member' was not given");
        if (Dissolved) throw new GolemDomainException($"{Name} was dissolved");
        if (me != null) throw new GolemDomainException($"'{me.Name}' already spoke for {Name}");
        if (member.Of != Candidates.Count || Candidates.Names[member.Rank] != member.Name) throw new GolemDomainException($"'{member.Name}' is no candidate of {Name}");
        me = member;
        convenedFrom = from;                   // the pose as the golem knows it, heading included: what its route opens from (the word is the law's)
        stood[me.Name] = Said(from);
        nonces[me.Name] = Stamp;
        ResolveIfComplete();
        return Route;
    }

    /// <summary>A CANDIDATE'S WORD (by tell, <c>StoodFor</c> with the union's name): where it stood, with the stamp its word carried. Kept; with
    /// the round complete the law runs. This golem's own route comes back, if any.</summary>
    internal Route Stood(Member peer, Position at, string nonce)
    {
        if (peer == null) throw new GolemDomainException("Union.Stood: 'peer' was not given");
        if (at == null) throw new GolemDomainException("Union.Stood: 'at' was not given");
        if (me != null && peer.Name == me.Name) throw new GolemDomainException($"'{peer.Name}' is this golem: its own word is its Convene");
        if (!Candidates.Names.Contains(peer.Name)) throw new GolemDomainException($"'{peer.Name}' is no candidate of {Name}");
        if (IsResolved) return Route;                       // the round is over: a late word changes nothing
        stood[peer.Name] = Said(at);
        nonces[peer.Name] = nonce ?? "";
        ResolveIfComplete();
        return Route;
    }

    /// <summary>A FORMATION OF THE PROGRAMME TOLD after the words (the warden's lines reach a tab in order, a peer's words may come first): the
    /// module says so, and the law runs if with it the round is complete.</summary>
    internal void Told()
    {
        if (Dissolved) return;
        ResolveIfComplete();
    }

    private void ResolveIfComplete()
    {
        if (IsResolved || CannotResolve || !IsComplete) return;
        var formations = Formations();
        var places = new List<(string Formation, int Index, int Place, Position At)>();
        for (int f = 0; f < formations.Count; f++)
        {
            // the places the law shares on this figure: as many as the formation takes (its count, or its vertices) — a polygon told for fewer than its
            // vertices lays them all out (ajuste 86), so only the first `Capacity` are offered, the ones rank would fill first
            var spots = formations[f].Figure.Places(formations[f].Capacity);
            for (int p = 0; p < Math.Min(spots.Count, formations[f].Capacity); p++) places.Add((formations[f].Name, f, p, spots[p]));
        }
        var shared = policy == Policy.RockPaperScissors ? GameTable(places) : NearestTable(places);
        var empty = formations.FirstOrDefault(f => !shared.Values.Any(v => v.Formation == f.Name));
        if (empty != null)
        {
            CannotResolve = true;
            Why = $"{empty.Name} gets nobody: {Candidates.Count} candidate(s) for {formations.Count} formations";
            return;
        }
        table.Clear();
        foreach (var (who, got) in shared) table[who] = got;
        IsResolved = true;
        var words = stood.ToDictionary(kv => kv.Key, kv => (kv.Value, nonces.TryGetValue(kv.Key, out var n) ? n : ""), StringComparer.Ordinal);
        foreach (var formation in formations)
        {
            var members = table.Where(kv => kv.Value.Formation == formation.Name).Select(kv => kv.Key).OrderBy(n => n, StringComparer.Ordinal).ToList();
            formation.Enrol(new Fleet(string.Join(",", members)), words, convenedFrom);
        }
    }

    // the first law over every figure at once: the pairs (candidate, place of any figure) from the shortest to the longest — a tie by name, then by
    // the figure's order in the union, then by the place — each candidate and each place taken once
    private IReadOnlyDictionary<string, (string Formation, int Place)> NearestTable(List<(string Formation, int Index, int Place, Position At)> places)
    {
        var pairs = new List<(double Distance, string Name, int Index, int Place, string Formation)>();
        foreach (var said in stood)
            foreach (var spot in places) pairs.Add((said.Value.DistanceTo(spot.At), said.Key, spot.Index, spot.Place, spot.Formation));
        pairs.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance)
                           : string.CompareOrdinal(a.Name, b.Name) != 0 ? string.CompareOrdinal(a.Name, b.Name)
                           : a.Index != b.Index ? a.Index.CompareTo(b.Index) : a.Place.CompareTo(b.Place));
        var taken = new HashSet<(int, int)>();
        var shared = new Dictionary<string, (string, int)>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            if (shared.ContainsKey(pair.Name) || taken.Contains((pair.Index, pair.Place))) continue;
            shared[pair.Name] = (pair.Formation, pair.Place);
            taken.Add((pair.Index, pair.Place));
        }
        return shared;
    }

    // THE GAME over every figure at once (propuesta 115's law, one level up): every candidate wants the free place nearest to where it stood, in
    // any figure; a place wanted by several is played, the losers want their next free place, until everybody has one or no place is left
    private IReadOnlyDictionary<string, (string Formation, int Place)> GameTable(List<(string Formation, int Index, int Place, Position At)> places)
    {
        matches.Clear();
        var free = Enumerable.Range(0, places.Count).ToList();
        var shared = new Dictionary<string, (string, int)>(StringComparer.Ordinal);
        var waiting = stood.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
        var cursor = waiting.ToDictionary(n => n, _ => 0, StringComparer.Ordinal);
        while (waiting.Count > 0 && free.Count > 0)
        {
            var wants = waiting.ToDictionary(n => n, n => free.OrderBy(i => stood[n].DistanceTo(places[i].At)).ThenBy(i => i).First(), StringComparer.Ordinal);
            foreach (var group in wants.GroupBy(w => w.Value).OrderBy(g => g.Key))
            {
                var contenders = group.Select(w => w.Key).OrderBy(n => n, StringComparer.Ordinal).ToList();
                string champion = contenders[0];
                foreach (var challenger in contenders.Skip(1)) champion = Play(champion, challenger, places[group.Key], cursor);
                shared[champion] = (places[group.Key].Formation, places[group.Key].Place);
                free.Remove(group.Key);
                waiting.Remove(champion);
            }
        }
        return shared;
    }

    private string Play(string a, string b, (string Formation, int Index, int Place, Position At) place, Dictionary<string, int> cursor)
    {
        string handA = HandOf(a), handB = HandOf(b);
        string key = $"{place.Formation}:{place.Place}";
        int ties = 0;
        for (int k = 0; k < Throws.Length; k++)
        {
            char ta = handA[cursor[a]++ % Throws.Length], tb = handB[cursor[b]++ % Throws.Length];
            if (ta == tb) { ties++; continue; }
            bool aWins = Throws.Beats(ta, tb);
            matches.Add((key, aWins ? a : b, aWins ? b : a, $"{Throws.Word(aWins ? ta : tb)} beats {Throws.Word(aWins ? tb : ta)}{(ties > 0 ? $" after {ties} tie{(ties > 1 ? "s" : "")}" : "")}"));
            return aWins ? a : b;
        }
        double da = stood[a].DistanceTo(place.At), db = stood[b].DistanceTo(place.At);
        string nearer = da < db || (da == db && string.CompareOrdinal(a, b) < 0) ? a : b;
        matches.Add((key, nearer, nearer == a ? b : a, $"{Throws.Length} ties: the nearer keeps it"));
        return nearer;
    }

    private string HandOf(string who) => Throws.Of(nonces.TryGetValue(who, out var nonce) ? nonce : "", who);

    private static Position Said(Position at) => new Position(Math.Round(at.X, 3, MidpointRounding.AwayFromZero), Math.Round(at.Y, 3, MidpointRounding.AwayFromZero));

    /// <summary>Whether this golem is a member of some formation of the union, once resolved.</summary>
    internal bool IsMember => IsResolved && golem.Name != null && table.ContainsKey(golem.Name);
    /// <summary>Whether this golem was left out: resolved, and no place for it.</summary>
    internal bool IsLeftOut => IsResolved && (golem.Name == null || !table.ContainsKey(golem.Name));
    /// <summary>The formation this golem dances in. Refused before the union resolved, and for a golem left out.</summary>
    internal Formation Mine
    {
        get
        {
            if (!IsResolved) throw new GolemDomainException($"{Name} is not resolved yet: {stood.Count} of {Candidates.Count} spoke{(IsKnown ? "" : ", some formation not told yet")}");
            if (golem.Name == null || !table.TryGetValue(golem.Name, out var got)) throw new GolemDomainException($"{golem.Name ?? "this golem"} was left out of {Name}");
            return choreographies.OpenFormationOf(got.Formation, Name) ?? throw new GolemDomainException($"{got.Formation} is told no more");
        }
    }
    /// <summary>This golem's formation is STRANDED for it (propuesta 113): its route to its place ended short twice; `take <union>` takes it again.</summary>
    internal bool IsStranded => IsMember && Mine.Stranded;
    internal string StrandedWhy => IsStranded ? Mine.StrandedWhy : "";
    /// <summary>The name of this golem's formation ("" before the union resolved, or left out).</summary>
    internal string MineName => IsMember ? table[golem.Name].Formation : "";
    /// <summary>This golem's route in its formation, if it opened; null otherwise.</summary>
    internal Route Route => IsMember ? Mine.Route : null;
    /// <summary>The candidates the law left without a place, by name.</summary>
    internal IReadOnlyList<string> LeftOut() => IsResolved ? Candidates.Names.Where(n => !table.ContainsKey(n)).ToList() : new List<string>();
    /// <summary>The members of that formation as the law shared them; empty before the union resolved.</summary>
    internal IReadOnlyList<string> MembersOf(string formationName)
    {
        if (formationName == null) throw new GolemDomainException("Union.MembersOf: 'formationName' was not given");
        string called = formationName.Trim().ToLowerInvariant();
        return table.Where(kv => kv.Value.Formation == called).Select(kv => kv.Key).OrderBy(n => n, StringComparer.Ordinal).ToList();
    }
    /// <summary>The table, for the read: who, in which formation, which place — in the candidates' order.</summary>
    internal IReadOnlyList<Share> Shares() => Candidates.Names.Where(table.ContainsKey).Select(n => new Share(n, table[n].Formation, table[n].Place)).ToList();
    /// <summary>The matches played (rps), for the read: the place, who won, who lost, how.</summary>
    internal IReadOnlyList<string> Games() => matches.Select(m => $"{m.Place}: {m.Winner} over {m.Loser} — {m.Game}").ToList();
    /// <summary>The names of its formations as one comma list, in order; the candidates left out as one; the games played as one — what the read prints.</summary>
    internal string Bill => string.Join(",", Names);
    internal string LeftOutNames => string.Join(",", LeftOut());
    internal string Played => string.Join(" | ", Games());

    // ---- THE BEATS (propuesta 124) ----

    /// <summary>Whether the next beat may open: this golem a member, every formation of the union enrolled with every member on its place, and
    /// a step queued in at least one of them (the formations with none queued stay).</summary>
    internal bool CanStep => IsMember && !Dissolved && Formations().All(f => f.IsResolved && f.AllPlaced) && Formations().Any(f => f.Queued > 0);

    /// <summary>THE BEAT OPENS — <c>route = formation.Step()</c> on any formation of the union steps them ALL: every formation with a step queued takes
    /// it (<see cref="Formation.StepAlone"/>: its holders shift, its members become the movers of the beat), the others stay placed; this golem's own
    /// route in its formation comes back (the route in place when its formation stays). Refused when the beat cannot open yet.</summary>
    internal Route Step()
    {
        if (!CanStep) throw new GolemDomainException($"{Name}: the beat waits — {string.Join(", ", Formations().Select(f => $"{f.Name} {f.PlacedCount}/{f.Fleet.Count} placed, {f.Queued} queued"))}");
        bool somebodyMoves;
        do
        {
            somebodyMoves = false;
            foreach (var formation in Formations())
            {
                if (formation.Queued == 0) continue;
                bool wasPlaced = formation.AllPlaced;
                formation.StepAlone();
                if (!formation.AllPlaced || !wasPlaced) somebodyMoves = true;   // a rest leaves everybody placed; a step does not
            }
            beat++;
        } while (!somebodyMoves && CanStep);   // a beat of rests alone (every part resting at once) brings no word to open the next: it opens here
        return Route;
    }

    /// <summary>Every mover of every formation of the union is lined up for the beat underway (the take, or a step).</summary>
    internal bool MoversAligned => Formations().All(f => f.MoversAligned);

    /// <summary>THE START OF THE BEAT, BY THE CUE (propuesta 124): with the last mover of the whole union lined up — its own word, or a peer's heard in
    /// any formation of the union — this copy lets its route go (<see cref="Route.Go"/>); the print turns from stop to advance. This golem's route
    /// comes back (null when it is no member).</summary>
    internal Route LetGoIfAligned()
    {
        if (!IsMember) return null;
        var mine = Mine.Route;
        if (mine != null && mine.Waiting && MoversAligned) mine.Go();
        return mine;
    }

    /// <summary>The union is DONE: resolved and every member of every formation finished its part.</summary>
    internal bool IsDone => IsResolved && !Dissolved && Formations().All(f => f.IsFinished);

    /// <summary>DISSOLVED by the warden: its formations with it.</summary>
    internal void Dissolve()
    {
        if (Dissolved) return;
        Dissolved = true;
        foreach (var formation in Formations()) formation.Dissolve();
    }

    private static string Fmt(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
}

/// <summary>One line of a union's table: who, in which formation, which place.</summary>
internal sealed class Share
{
    internal string Who { get; }
    internal string Formation { get; }
    internal int Place { get; }
    internal Share(string who, string formation, int place)
    {
        if (string.IsNullOrWhiteSpace(who)) throw new GolemDomainException("Share.Share: 'who' was not given");
        if (string.IsNullOrWhiteSpace(formation)) throw new GolemDomainException("Share.Share: 'formation' was not given");
        Who = who; Formation = formation; Place = place;
    }
}
