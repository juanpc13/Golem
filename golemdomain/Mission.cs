namespace GolemHost.Domain;

/// <summary>A task entrusted to the golem: where it was told to go, who ordered it, the road it chose, and how it ended.</summary>
internal sealed class Mission
{
    internal int Id { get; }
    internal Waypoint At { get; }
    /// <summary>True when the point came from a peer's tell (following), false when the operator ordered it.</summary>
    internal bool Told { get; }
    /// <summary>True once the golem has announced this visited point to its peer.</summary>
    internal bool Announced { get; private set; }

    private MissionStatus status = MissionStatus.Pending;
    private string reason = "";
    private readonly List<Leg> legs = new();   // the road chosen at start: passages to cross, the goal last
    private int nextLeg;

    internal Mission(int id, Waypoint at, bool told)
    {
        Id = id;
        At = at;
        Told = told;
    }

    internal bool IsPending() => status == MissionStatus.Pending;
    internal string ReadStatus() => status.Name;
    internal string ReadReason() => reason;

    // ---- the road ----

    internal bool IsRouted => legs.Count > 0;
    internal int LegsLeft => legs.Count - nextLeg;
    internal Leg NextLeg => IsRouted ? legs[nextLeg] : new Leg(At, "");

    /// <summary>The golem decided its road: the legs to walk, in order, the goal last.</summary>
    internal void Route(IEnumerable<Leg> road)
    {
        MustBePending();
        if (IsRouted) throw new DomainException($"mission {Id} already has its road");
        legs.AddRange(road);
        if (legs.Count == 0) throw new DomainException($"mission {Id} needs at least the goal as a leg");
        nextLeg = 0;
    }

    /// <summary>The golem crossed the next passage of its road. Returns what it crossed.</summary>
    internal string Pass(string passage)
    {
        MustBePending();
        if (!IsRouted) throw new DomainException($"mission {Id} has no road to pass along");
        if (LegsLeft <= 1) throw new DomainException($"mission {Id} has only the goal left: complete it, do not pass");
        if (legs[nextLeg].Name != passage) throw new DomainException($"mission {Id} is heading to '{legs[nextLeg].Name}', not '{passage}'");
        nextLeg++;
        return passage;
    }

    // ---- the ending ----

    internal void Complete()
    {
        MustBePending();
        status = MissionStatus.Completed;
    }

    internal void Fail(string why)
    {
        MustBePending();
        status = MissionStatus.Failed;
        reason = why;
    }

    /// <summary>A newer told point made this one pointless: the follower catches up instead of retracing.</summary>
    internal void Supersede(int byId)
    {
        MustBePending();
        if (!Told) throw new DomainException($"mission {Id} was ordered by the operator: only told points are superseded");
        status = MissionStatus.Superseded;
        reason = $"superseded by mission {byId}";
    }

    internal void Announce()
    {
        if (status != MissionStatus.Completed) throw new DomainException($"mission {Id} is {status.Name}: only a completed point is announced");
        Announced = true;
    }

    private void MustBePending()
    {
        if (!IsPending())
            throw new DomainException($"mission {Id} is already {status.Name}");
    }
}
