using GolemDomain.Formations;
using GolemDomain.Geometry;

namespace GolemDomain.Coordination;

/// <summary>
/// A CONVOCATION — the fleet called to a formation, BY RANK or BY DISTANCE — in its ONE copy, the warden's (propuesta 88, 2-oct-2026;
/// Juan: "centraliza la ley"; before, ajustes 73–86, every golem held a copy that converged by the same law). Its identity is the CALL.
/// The warden CONVENES it (<see cref="Convene"/>: the golems are told); every member says where it stands (<see cref="Stood"/>); when the round
/// is complete the warden SHARES the places (<see cref="Share"/>): the law applied once — by rank the place of each one's rank, by
/// distance the first law (pairs member–place from the shortest to the longest, a tie by name then place, greedy) over every place of
/// the figure (ajuste 86: a polygon's places are its vertices, and fewer bodies leave the rest free) — and the whole table travels to
/// every golem as one word (<see cref="Assignments"/>). Then every member says when it stands on its place (<see cref="Placed"/>), moves
/// are queued (<see cref="Queue"/>, the figure's own, ajuste 84) and a STEP opens once everybody is placed (<see cref="Step"/>): the next
/// table, shared the same way, the hole of a free vertex moving with the fleet. The convocation opens no route: the golems do, on their
/// own maps, when they hear their place.
/// </summary>
internal sealed class Muster
{
    private readonly bool byDistance;
    private readonly Dictionary<string, Position> stood = new(StringComparer.Ordinal);    // where each member stands, as it said — or the place it reached last round
    private readonly Dictionary<string, int> index = new(StringComparer.Ordinal);         // the index of each member's place in the figure's order, once shared
    private readonly HashSet<string> placed = new(StringComparer.Ordinal);                // who said it stands on its place, this round
    private readonly HashSet<string> lost = new(StringComparer.Ordinal);                  // who said it lost its place, this round, and did not retake it yet
    private readonly HashSet<string> resaid = new(StringComparer.Ordinal);                // the lost ones the table was said again for — once per loss, then their word is waited for
    private readonly Queue<(Move Move, string Id)> steps = new();
    private readonly HashSet<string> stepIds = new(StringComparer.Ordinal);

    internal Muster(string call, Formation formation, Fleet fleet, bool byDistance)
    {
        if (string.IsNullOrWhiteSpace(call)) throw new GolemDomainException("a convocation needs its call");
        if (formation == null) throw new GolemDomainException("Muster.Muster: 'formation' was not given");
        if (fleet == null) throw new GolemDomainException("Muster.Muster: 'fleet' was not given");
        Call = call;
        Formation = formation;
        Fleet = fleet;
        this.byDistance = byDistance;
    }

    internal string Call { get; }
    internal Formation Formation { get; }
    internal Fleet Fleet { get; }
    /// <summary>How the places are shared: <c>rank</c> or <c>distance</c>.</summary>
    internal string Policy => byDistance ? "distance" : "rank";
    /// <summary>Whether the warden called it — the golems were told.</summary>
    internal bool Called { get; private set; }
    /// <summary>How many members said where they stood.</summary>
    internal int StoodCount => stood.Count;
    /// <summary>Every member said where it stands: the law may be applied.</summary>
    internal bool IsComplete => stood.Count == Fleet.Count;
    /// <summary>The places were shared: the golems know where to go.</summary>
    internal bool IsShared { get; private set; }
    /// <summary>How many members said they stand on their place, this round.</summary>
    internal int PlacedCount => placed.Count;
    /// <summary>How many said they lost their place and have not retaken it (ajuste 91).</summary>
    internal int LostCount => lost.Count;
    /// <summary>How many tables of this call travelled to the fleet: the first share, every step, every table said again — the once of the
    /// word that carries each.</summary>
    internal int Shares { get; private set; }
    /// <summary>Whether the table may be SAID AGAIN now (ajuste 91): somebody lost its place and everybody else stands on its own — nobody
    /// moves any more, so the one that lost its place finds its way clear.</summary>
    internal bool CanReshare => IsShared && lost.Any(name => !resaid.Contains(name)) && placed.Count + lost.Count == Fleet.Count;
    /// <summary>Everybody stands on its place, this round.</summary>
    internal bool AllPlaced => IsShared && placed.SetEquals(Fleet.Names);
    /// <summary>Which ROUND the fleet is in: 0 while taking the formation, one more per step.</summary>
    internal int Round { get; private set; }
    /// <summary>How many moves wait in the queue.</summary>
    internal int Queued => steps.Count;
    /// <summary>Whether the next step may open now: the places shared, everybody placed and a move queued.</summary>
    internal bool CanStep => AllPlaced && steps.Count > 0;
    /// <summary>The whole table of this round as one text — what the word <c>Shared</c> carries. Refused before the places are shared.</summary>
    internal string Table => Assignments().AsText();

    /// <summary>The warden CONVENES the fleet: <c>muster.Convene();</c> — the act the reaction tells every golem on. Once.</summary>
    internal void Convene()
    {
        if (Called) throw new GolemDomainException($"the call {Call} was made already");
        Called = true;
    }

    /// <summary>A member's word: where it stood when called — <c>muster.Stood(member, at)</c>. Refused for a stranger, for a member that
    /// spoke already, and once the places were shared (a late word changes nothing: the places are given).</summary>
    internal void Stood(Member member, Position at)
    {
        if (member == null) throw new GolemDomainException("Muster.Stood: 'member' was not given");
        if (at == null) throw new GolemDomainException("Muster.Stood: 'at' was not given");
        if (!Fleet.Names.Contains(member.Name)) throw new GolemDomainException($"'{member.Name}' is not a member of the fleet of the call {Call}");
        if (IsShared) throw new GolemDomainException($"the places of the call {Call} were shared already: '{member.Name}' speaks too late");
        if (stood.ContainsKey(member.Name)) throw new GolemDomainException($"'{member.Name}' said where it stood already in the call {Call}");
        stood[member.Name] = new Position(at.X, at.Y);
    }

    /// <summary>THE LAW, applied once (propuesta 88): with every member's word, the places are shared — by rank each one's rank, by
    /// distance the first law over every place of the figure — and the table of the round comes back, to travel to the golems:
    /// <c>muster.Share();</c>. Refused before the round is complete, or twice.</summary>
    internal Assignments Share()
    {
        if (!IsComplete) throw new GolemDomainException($"the call {Call}: {stood.Count} of {Fleet.Count} said where they stand — the places wait for everybody");
        if (IsShared) throw new GolemDomainException($"the places of the call {Call} were shared already");
        if (byDistance) foreach (var (name, i) in Law()) index[name] = i;
        else foreach (var name in Fleet.Names) index[name] = Fleet.Member(name).Rank;
        IsShared = true;
        Shares++;
        return Assignments();
    }

    /// <summary>A member's word: it LOST its place, this round — its route ended short of it — and where its body stands:
    /// <c>muster.Lost(member, at)</c> (ajuste 91). It no longer counts as placed; its standing is where it is now, so the table said
    /// again carries it to the others as a berth. Whether the table may travel again now is <see cref="CanReshare"/>.</summary>
    internal void Lost(Member member, Position at)
    {
        if (member == null) throw new GolemDomainException("Muster.Lost: 'member' was not given");
        if (at == null) throw new GolemDomainException("Muster.Lost: 'at' was not given");
        if (!Fleet.Names.Contains(member.Name)) throw new GolemDomainException($"'{member.Name}' is not a member of the fleet of the call {Call}");
        if (!IsShared) throw new GolemDomainException($"the places of the call {Call} are not shared yet: nobody can lose one");
        placed.Remove(member.Name);
        lost.Add(member.Name);
        resaid.Remove(member.Name);                       // lost again after a table said: it is said once more
        stood[member.Name] = new Position(at.X, at.Y);
    }

    /// <summary>The table SAID AGAIN (ajuste 91): the same places, everybody where it stands now — the act the reaction tells every golem
    /// on; the ones on their place change nothing, the one that lost its place retakes it from where it stands: <c>muster.Reshare();</c>.
    /// Refused unless <see cref="CanReshare"/>.</summary>
    internal Assignments Reshare()
    {
        if (!IsShared) throw new GolemDomainException($"'{Call}': the places are not shared yet");
        if (lost.Count == 0) throw new GolemDomainException($"'{Call}': nobody lost its place — nothing to say again");
        if (lost.All(resaid.Contains)) throw new GolemDomainException($"'{Call}': the table was said again already — it waits for the word of the one that lost its place");
        if (!CanReshare) throw new GolemDomainException($"'{Call}': {placed.Count} of {Fleet.Count - lost.Count} others stand on their places — the table waits for them");
        resaid.UnionWith(lost);
        Shares++;
        return Assignments();
    }

    /// <summary>The table of this round: for every member, where it stands and the place it goes to.</summary>
    internal Assignments Assignments()
    {
        if (!IsShared) throw new GolemDomainException($"the places of the call {Call} are not shared yet");
        var places = Formation.Places(Fleet.Count);
        return new Assignments(Fleet.Names.Select(name => new Assignment(name, stood[name], places[index[name]])).ToList());
    }

    /// <summary>The place that member holds or heads to in this round; refused before the places are shared.</summary>
    internal Position PlaceOf(Member member)
    {
        if (member == null) throw new GolemDomainException("Muster.PlaceOf: 'member' was not given");
        if (!IsShared) throw new GolemDomainException($"the places of the call {Call} are not shared yet");
        if (!index.TryGetValue(member.Name, out int i)) throw new GolemDomainException($"'{member.Name}' is not a member of the fleet of the call {Call}");
        return Formation.Places(Fleet.Count)[i];
    }

    /// <summary>The index, in the figure's fixed order, of the place that member holds; refused before the places are shared.</summary>
    internal int PlaceIndex(Member member)
    {
        if (member == null) throw new GolemDomainException("Muster.PlaceIndex: 'member' was not given");
        if (!IsShared) throw new GolemDomainException($"the places of the call {Call} are not shared yet");
        if (!index.TryGetValue(member.Name, out int i)) throw new GolemDomainException($"'{member.Name}' is not a member of the fleet of the call {Call}");
        return i;
    }

    /// <summary>A member's word: it stands on its place, this round — <c>muster.Placed(member)</c>. Whether the step may open now is
    /// <see cref="CanStep"/>; the step itself is <see cref="Step"/>.</summary>
    internal void Placed(Member member)
    {
        if (member == null) throw new GolemDomainException("Muster.Placed: 'member' was not given");
        if (!Fleet.Names.Contains(member.Name)) throw new GolemDomainException($"'{member.Name}' is not a member of the fleet of the call {Call}");
        if (!IsShared) throw new GolemDomainException($"the places of the call {Call} are not shared yet: nobody can stand on one");
        lost.Remove(member.Name);
        resaid.Remove(member.Name);
        placed.Add(member.Name);
    }

    /// <summary>A MOVE is queued (ajuste 77; ajuste 84: the figure's — <c>move = muster.Formation.Rotate(@sense); muster.Queue(move, @stepId);</c>):
    /// every body does it once everybody stands on its place. The step's id is the once of the word that asked it: the same twice is
    /// refused. How many wait comes back.</summary>
    internal int Queue(Move move, string stepId)
    {
        if (move == null) throw new GolemDomainException("Muster.Queue: 'move' was not given");
        if (string.IsNullOrWhiteSpace(stepId)) throw new GolemDomainException("Muster.Queue: 'stepId' was not given");
        if (!stepIds.Add(stepId)) throw new GolemDomainException($"the step {stepId} is already queued in the call {Call}");
        steps.Enqueue((move, stepId));
        return steps.Count;
    }

    /// <summary>The next STEP opens: everybody stands on its place and a move waits — every member stands where its place was and heads
    /// to the place the move takes it to (<see cref="Move.Next"/>, over all the figure's places: the hole moves too), a new round begins
    /// and its table comes back to travel: <c>muster.Step();</c>. Refused when it cannot step yet.</summary>
    internal Assignments Step()
    {
        if (!IsShared) throw new GolemDomainException($"'{Call}': the places are not shared yet");
        if (steps.Count == 0) throw new GolemDomainException($"'{Call}': no step is queued");
        if (!placed.SetEquals(Fleet.Names)) throw new GolemDomainException($"'{Call}': {placed.Count} of {Fleet.Count} stand on their places — the step waits for everybody");
        var (move, _) = steps.Dequeue();
        var places = Formation.Places(Fleet.Count);
        foreach (var name in Fleet.Names)
        {
            stood[name] = places[index[name]];                 // it stands on the place it reached
            index[name] = move.Next(index[name], places.Count);
        }
        placed.Clear();
        lost.Clear();
        resaid.Clear();
        Round++;
        Shares++;
        return Assignments();
    }

    // THE FIRST LAW: over every member that spoke, the pairs (member, place) from the shortest to the longest — a tie by name, then by
    // place — each member and each place taken once; with more places than members, the places nobody was near stay free.
    private IReadOnlyDictionary<string, int> Law()
    {
        var places = Formation.Places(Fleet.Count);
        var pairs = new List<(double Distance, string Name, int Place)>();
        foreach (var said in stood)
            for (int i = 0; i < places.Count; i++) pairs.Add((said.Value.DistanceTo(places[i]), said.Key, i));
        pairs.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance)
                           : string.CompareOrdinal(a.Name, b.Name) != 0 ? string.CompareOrdinal(a.Name, b.Name)
                           : a.Place.CompareTo(b.Place));
        var taken = new HashSet<int>();
        var table = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var pair in pairs)
        {
            if (table.ContainsKey(pair.Name) || taken.Contains(pair.Place)) continue;
            table[pair.Name] = pair.Place;
            taken.Add(pair.Place);
        }
        return table;
    }
}
