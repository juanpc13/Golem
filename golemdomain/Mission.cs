namespace GolemHost.Domain;

/// <summary>
/// A task entrusted to the golem: the stops to reach (one, or several), who ordered it, whether the
/// golem may choose the order of the stops, the road it chose, and how it ended.
/// </summary>
internal sealed class Mission
{
    internal int Id { get; }
    /// <summary>Where to go, in the order given.</summary>
    internal IReadOnlyList<Waypoint> Stops { get; }
    /// <summary>True when the stop came from a peer's tell (the golem follows), false when the operator ordered it.</summary>
    internal bool Following { get; }
    /// <summary>True when the golem may reorder the stops for the shortest road (Cover), false when the order is the operator's (MoveTo).</summary>
    internal bool ChoosesOrder { get; }
    /// <summary>True once the golem has announced a reached stop to its peer.</summary>
    internal bool Announced { get; private set; }

    private MissionStatus status = MissionStatus.Pending;
    private string reason = "";
    private readonly List<Leg> legs = new();   // the road chosen at start: passages to cross and stops to reach, in order
    private int nextLeg;
    private int reached;                       // stops reached so far
    private int bumps;                         // times the body touched something the map did not hold, on this mission
    private int bumpsSinceRoute;               // ...since the road was last decided: a reason to decide it again

    internal Mission(int id, IReadOnlyList<Waypoint> stops, bool following, bool choosesOrder)
    {
        if (stops == null || stops.Count == 0) throw new DomainException($"mission {id} needs at least one stop");
        Id = id;
        Stops = stops;
        Following = following;
        ChoosesOrder = choosesOrder;
    }

    internal bool IsPending() => status == MissionStatus.Pending;
    internal string ReadStatus() => status.Name;
    internal string ReadReason() => reason;

    // ---- the road ----

    internal bool IsRouted => legs.Count > 0;
    internal int LegsLeft => legs.Count - nextLeg;
    internal Leg NextLeg => IsRouted ? legs[nextLeg] : new Leg(Stops[0], "");
    /// <summary>Stops not reached yet: the stop legs ahead once routed, every stop before that.</summary>
    internal IEnumerable<Waypoint> StopsAhead => IsRouted ? legs.Skip(nextLeg).Where(l => l.IsStop).Select(l => l.At) : Stops.Skip(reached);
    internal int StopsLeft => StopsAhead.Count();
    internal int Bumps => bumps;
    internal bool BumpedSinceRoute => bumpsSinceRoute > 0;

    /// <summary>The golem decided its road: passages and stops, in order, the last stop last. A road already
    /// decided is decided again only after the body bumped into something on it — then the new road replaces
    /// what was left of the old one and must still reach every stop ahead.</summary>
    internal void Route(IEnumerable<Leg> road)
    {
        MustBePending();
        if (IsRouted && bumpsSinceRoute == 0) throw new DomainException($"mission {Id} already has its road");
        var fresh = road.ToList();
        if (fresh.Count == 0) throw new DomainException($"mission {Id} needs at least the stop as a leg");
        if (!fresh[^1].IsStop) throw new DomainException($"mission {Id}'s road must end at a stop, not at '{fresh[^1].Name}'");
        int ahead = Stops.Count - reached;
        if (fresh.Count(l => l.IsStop) != ahead) throw new DomainException($"mission {Id} has {ahead} stops ahead but the road reaches {fresh.Count(l => l.IsStop)}");
        legs.Clear();
        legs.AddRange(fresh);
        nextLeg = 0;
        bumpsSinceRoute = 0;
    }

    /// <summary>The body touched something the map does not hold, on this mission's road.</summary>
    internal void Bump()
    {
        MustBePending();
        bumps++;
        bumpsSinceRoute++;
    }

    /// <summary>The golem crossed the next passage of its road (or skirted a mark: the leg named 'around'). Returns what it crossed.</summary>
    internal string Cross(string passage)
    {
        MustBePending();
        if (!IsRouted) throw new DomainException($"mission {Id} has no road to cross along");
        var leg = legs[nextLeg];
        if (leg.IsStop) throw new DomainException($"mission {Id} is heading to the stop '{leg.Name}', a stop, not a passage: reach it");
        if (leg.Name != passage) throw new DomainException($"mission {Id} is heading to '{leg.Name}', not '{passage}'");
        nextLeg++;
        return passage;
    }

    /// <summary>The golem reached the next stop of its road. Reaching the last one completes the mission.</summary>
    internal void Reach(double x, double y)
    {
        MustBePending();
        if (!IsRouted) throw new DomainException($"mission {Id} has no road to reach a stop along");
        var leg = legs[nextLeg];
        if (!leg.IsStop) throw new DomainException($"mission {Id} is heading to the passage '{leg.Name}': cross it, no stop is next");
        if (Math.Abs(leg.At.X - x) > 1e-6 || Math.Abs(leg.At.Y - y) > 1e-6)
            throw new DomainException($"mission {Id}'s next stop is ({leg.At.X}, {leg.At.Y}), not ({x}, {y})");
        nextLeg++;
        reached++;
        if (nextLeg == legs.Count) status = MissionStatus.Completed;
    }

    // ---- the ending ----

    internal void Fail(string why)
    {
        MustBePending();
        if (string.IsNullOrWhiteSpace(why)) throw new DomainException($"failing mission {Id} needs a reason");
        status = MissionStatus.Failed;
        reason = why;
    }

    /// <summary>The golem lets the mission go: a newer told point made it pointless, or the operator let go of everything.</summary>
    internal void Abandon(string why)
    {
        MustBePending();
        if (string.IsNullOrWhiteSpace(why)) throw new DomainException($"abandoning mission {Id} needs a reason");
        status = MissionStatus.Abandoned;
        reason = why;
    }

    internal void Announce()
    {
        if (reached == 0) throw new DomainException($"mission {Id} has reached no stop yet: only a reached stop is announced");
        Announced = true;
    }

    private void MustBePending()
    {
        if (!IsPending())
            throw new DomainException($"mission {Id} is already {status.Name}");
    }
}
