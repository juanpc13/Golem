using GolemHost.Domain.Geometry;
using GolemHost.Domain.Routes;

namespace GolemHost.Domain.Robots;

/// <summary>
/// A task entrusted to the golem: the stops to reach (one, or several), who ordered it, whether the
/// golem may choose the order of the stops, the road it chose (a trajectory), and how it ended.
/// </summary>
internal sealed class Mission
{
    internal int Id { get; }
    /// <summary>Where to go, in the order given.</summary>
    internal IReadOnlyList<Position> Stops { get; }
    /// <summary>True when the stop came from a peer's tell (the golem follows), false when the operator ordered it.</summary>
    internal bool Following { get; }
    /// <summary>True when the golem may reorder the stops for the shortest road (Cover), false when the order is the operator's (MoveTo).</summary>
    internal bool ChoosesOrder { get; }
    /// <summary>True once the golem has announced a reached stop to its peer.</summary>
    internal bool Announced { get; private set; }

    private MissionStatus status = MissionStatus.Pending;
    private string reason = "";
    private Trajectory road = new(Array.Empty<Leg>());   // the road decided at start: passages to cross and stops to reach, in order
    private int nextLeg;
    private int reached;                                 // stops reached so far
    private int bumps;                                   // times the body touched something the map did not hold, on this mission
    private int bumpsSinceRoute;                         // ...since the road was last decided: a reason to decide it again
    private int grazes;                                  // times the body grazed a wall it knows, on this mission
    private int grazesOnLeg;                             // ...on the leg being walked: the golem's patience with its own error

    /// <summary>How many times the golem retries a leg after grazing a known wall before it gives the mission up.</summary>
    internal const int PatienceWithWalls = 3;

    internal Mission(int id, IReadOnlyList<Position> stops, bool following, bool choosesOrder)
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

    internal bool IsRouted => !road.IsEmpty;
    internal int LegsLeft => road.Count - nextLeg;
    internal Leg NextLeg => IsRouted ? road.LegAt(nextLeg) : new Leg(Stops[0], "");
    /// <summary>Stops not reached yet: the stop legs ahead once routed, every stop before that.</summary>
    internal IEnumerable<Position> StopsAhead => IsRouted ? road.StopsFrom(nextLeg) : Stops.Skip(reached);
    internal int StopsLeft => StopsAhead.Count();
    internal int Bumps => bumps;
    internal bool BumpedSinceRoute => bumpsSinceRoute > 0;
    internal int Grazes => grazes;
    internal int GrazesOnLeg => grazesOnLeg;
    /// <summary>Whether the golem still retries the leg it is walking after grazing a known wall — patience not yet spent.</summary>
    internal bool MayRetryLeg => IsPending() && grazesOnLeg < PatienceWithWalls;

    /// <summary>The golem decided its road: passages and stops, in order, the last stop last. A road already
    /// decided is decided again only after the body bumped into something on it — then the new road replaces
    /// what was left of the old one and must still reach every stop ahead.</summary>
    internal void Route(Trajectory fresh)
    {
        MustBePending();
        if (IsRouted && bumpsSinceRoute == 0) throw new DomainException($"mission {Id} already has its road");
        if (fresh == null || fresh.IsEmpty) throw new DomainException($"mission {Id} needs at least the stop as a leg");
        if (!fresh.Last.IsStop) throw new DomainException($"mission {Id}'s road must end at a stop, not at '{fresh.Last.Name}'");
        int ahead = Stops.Count - reached;
        if (fresh.StopCount != ahead) throw new DomainException($"mission {Id} has {ahead} stops ahead but the road reaches {fresh.StopCount}");
        road = fresh;
        nextLeg = 0;
        bumpsSinceRoute = 0;
        grazesOnLeg = 0;
    }

    /// <summary>The body touched something the map does not hold, on this mission's road.</summary>
    internal void Bump()
    {
        MustBePending();
        bumps++;
        bumpsSinceRoute++;
    }

    /// <summary>The body grazed a wall the map knows, on this mission's road: its own execution error, counted
    /// against its patience on the current leg.</summary>
    internal void Graze()
    {
        MustBePending();
        grazes++;
        grazesOnLeg++;
    }

    /// <summary>The golem crossed the next passage of its road (or skirted a mark: the leg named 'around'). Returns what it crossed.</summary>
    internal string Cross(string passage)
    {
        MustBePending();
        if (!IsRouted) throw new DomainException($"mission {Id} has no road to cross along");
        var leg = road.LegAt(nextLeg);
        if (leg.IsStop) throw new DomainException($"mission {Id} is heading to the stop '{leg.Name}', a stop, not a passage: reach it");
        if (leg.Name != passage) throw new DomainException($"mission {Id} is heading to '{leg.Name}', not '{passage}'");
        nextLeg++;
        grazesOnLeg = 0;
        return passage;
    }

    /// <summary>The golem reached the next stop of its road. Reaching the last one completes the mission.</summary>
    internal void Reach(double x, double y)
    {
        MustBePending();
        if (!IsRouted) throw new DomainException($"mission {Id} has no road to reach a stop along");
        var leg = road.LegAt(nextLeg);
        if (!leg.IsStop) throw new DomainException($"mission {Id} is heading to the passage '{leg.Name}': cross it, no stop is next");
        if (Math.Abs(leg.At.X - x) > 1e-6 || Math.Abs(leg.At.Y - y) > 1e-6)
            throw new DomainException($"mission {Id}'s next stop is ({leg.At.X}, {leg.At.Y}), not ({x}, {y})");
        nextLeg++;
        reached++;
        grazesOnLeg = 0;
        if (nextLeg == road.Count) status = MissionStatus.Completed;
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
