using GolemDomain.Geometry;
using GolemDomain.Routes;

namespace GolemDomain.Robots;

/// <summary>
/// A task entrusted to the golem: the stops to reach (one, or several), who ordered it, whether the
/// golem may choose the order of the stops, the road it decided (a trajectory: every point it will pass, in
/// order), and how it ended. The road is the PLAN, written whole; walking it is not journaled step by step —
/// only what changes the plan (a touch, another road) or fulfils it (a stop reached) is. Reaching a stop
/// implies the legs before it were walked (Juan, 10-sep-2026: "no estar diciéndole cada cosa que va haciendo").
/// </summary>
internal sealed class Mission
{
    internal int Id { get; }
    private readonly List<Position> stops = new();
    /// <summary>Where to go, in the order given.</summary>
    internal IReadOnlyList<Position> Stops => stops;
    /// <summary>True when the stop came from a peer's tell (the golem follows), false when the operator ordered it.</summary>
    internal bool Following { get; }
    /// <summary>True when the golem may reorder the stops for the shortest road (Cover), false when the order is the operator's (Visit).</summary>
    internal bool ChoosesOrder { get; }
    /// <summary>True once the golem has announced a reached stop to its peer.</summary>
    internal bool Announced { get; private set; }

    private MissionStatus status = MissionStatus.Pending;
    private string reason = "";
    private Trajectory road = new(Array.Empty<Leg>());   // the plan: passages to cross, points to pass, stops to reach, in order
    private int nextLeg;                                 // the first leg not yet known to be walked
    private int reached;                                 // stops reached so far
    private int bumps;                                   // times the body touched something the map did not hold, on this mission
    private int bumpsSinceRoute;                         // ...since the road was last decided: a reason to decide it again
    private int grazes;                                  // times the body grazed a wall it knows, on this mission
    private int grazesOnLeg;                             // ...since the last stop reached or road decided: the golem's patience with its own error

    /// <summary>How many times the golem retries after grazing a known wall before it gives the mission up.</summary>
    internal const int PatienceWithWalls = 3;

    internal Mission(int id, Position stop, bool following, bool choosesOrder)
    {
        if (stop == null) throw new GolemDomainException($"mission {id} needs at least one stop");
        Id = id;
        stops.Add(stop);
        Following = following;
        ChoosesOrder = choosesOrder;
    }

    /// <summary>One more stop, in this order — while the errand is pending and before its road is decided. A stop
    /// is added with the same voice the errand was opened with.</summary>
    internal void AddStop(Position stop, bool following, bool choosesOrder)
    {
        MustBePending();
        if (stop == null) throw new GolemDomainException($"mission {Id} needs a stop to add");
        if (Following || following) throw new GolemDomainException($"mission {Id} follows a peer: a told point is one mission each");
        if (choosesOrder != ChoosesOrder) throw new GolemDomainException($"mission {Id} was opened with {(ChoosesOrder ? "Cover" : "Visit")}: add its stops the same way");
        if (IsRouted) throw new GolemDomainException($"mission {Id} already has its road: no stop can be added");
        stops.Add(stop);
    }

    internal bool IsPending() => status == MissionStatus.Pending;
    internal string ReadStatus() => status.Name;
    internal string ReadReason() => reason;

    // ---- the road ----

    internal bool IsRouted => !road.IsEmpty;
    internal int LegsLeft => road.Count - nextLeg;
    /// <summary>The legs not yet known to be walked: the plan ahead, from the first one on.</summary>
    internal IEnumerable<Leg> LegsAhead => road.Legs().Skip(nextLeg);
    /// <summary>The leg the body heads to: the first ahead once routed; before that, the next stop itself.</summary>
    internal Leg NextLeg
    {
        get
        {
            if (IsRouted)
            {
                if (nextLeg >= road.Count) throw new GolemDomainException($"mission {Id} has walked its whole road");
                return road.LegAt(nextLeg);
            }
            if (reached >= stops.Count) throw new GolemDomainException($"mission {Id} has reached every stop");
            return new Leg(stops[reached], "");
        }
    }
    /// <summary>Whether there is still somewhere to head to: a leg ahead once routed, a stop ahead before that.</summary>
    internal bool HasNextPoint => IsPending() && (IsRouted ? nextLeg < road.Count : reached < stops.Count);
    /// <summary>Stops not reached yet: the stop legs ahead once routed, every stop before that.</summary>
    internal IEnumerable<Position> StopsAhead => IsRouted ? road.StopsFrom(nextLeg) : stops.Skip(reached);
    internal int StopsLeft => StopsAhead.Count();
    internal int Bumps => bumps;
    internal bool BumpedSinceRoute => bumpsSinceRoute > 0;
    internal int Grazes => grazes;
    internal int GrazesOnLeg => grazesOnLeg;
    /// <summary>Whether the golem still retries after grazing a known wall — patience not yet spent since the last stop or road.</summary>
    internal bool MayRetryLeg => IsPending() && grazesOnLeg < PatienceWithWalls;
    /// <summary>Whether a road may be decided now: while the mission is pending, always — a new plan replaces what was
    /// left of the old one (after a bump, or when the golem wakes with a plan underway and its body elsewhere).</summary>
    internal bool MayRoute => IsPending();

    /// <summary>The golem decided its road: passages, points and stops, in order, the last stop last. A new road
    /// replaces what was left of the old one and must still reach every stop ahead.</summary>
    internal void Route(Trajectory fresh)
    {
        MustBePending();
        if (fresh == null || fresh.IsEmpty) throw new GolemDomainException($"mission {Id} needs at least the stop as a leg");
        if (!fresh.Last.IsStop) throw new GolemDomainException($"mission {Id}'s road must end at a stop, not at '{fresh.Last.Name}'");
        int ahead = stops.Count - reached;
        if (fresh.StopCount != ahead) throw new GolemDomainException($"mission {Id} has {ahead} stops ahead but the road reaches {fresh.StopCount}");
        road = fresh;
        nextLeg = 0;
        bumpsSinceRoute = 0;
        grazesOnLeg = 0;
    }

    /// <summary>The body touched something the map does not hold, on this mission's road: the plan is interrupted.</summary>
    internal void Bump()
    {
        MustBePending();
        bumps++;
        bumpsSinceRoute++;
    }

    /// <summary>The body grazed a wall the map knows, on this mission's road: its own execution error, counted
    /// against its patience since the last stop reached or road decided.</summary>
    internal void Graze()
    {
        MustBePending();
        grazes++;
        grazesOnLeg++;
    }

    /// <summary>The golem reached a stop of its road: the legs before it were walked, whatever they were. Reaching the
    /// last stop completes the mission. A road is not required: an errand without one reaches its stops in order.</summary>
    internal void Reach(double x, double y)
    {
        MustBePending();
        if (IsRouted)
        {
            int at = -1;
            for (int i = nextLeg; i < road.Count; i++)
            {
                var leg = road.LegAt(i);
                if (leg.IsStop && Math.Abs(leg.At.X - x) < 1e-6 && Math.Abs(leg.At.Y - y) < 1e-6) { at = i; break; }
            }
            if (at < 0)
            {
                var next = road.Legs().Skip(nextLeg).FirstOrDefault(l => l.IsStop);
                throw new GolemDomainException(next == null
                    ? $"mission {Id} has no stop ahead at ({x}, {y})"
                    : $"mission {Id}'s next stop is ({next.At.X}, {next.At.Y}), not ({x}, {y})");
            }
            nextLeg = at + 1;
        }
        else
        {
            var next = stops[reached];
            if (Math.Abs(next.X - x) > 1e-6 || Math.Abs(next.Y - y) > 1e-6)
                throw new GolemDomainException($"mission {Id}'s next stop is ({next.X}, {next.Y}), not ({x}, {y})");
        }
        reached++;
        grazesOnLeg = 0;
        bool done = IsRouted ? !road.Legs().Skip(nextLeg).Any(l => l.IsStop) : reached == stops.Count;
        if (done) status = MissionStatus.Completed;
    }

    /// <summary>Whether a stop of this mission lies ahead at a point — what to consult before saying it was reached.</summary>
    internal bool IsStopAhead(double x, double y) =>
        IsPending() && StopsAhead.Any(s => Math.Abs(s.X - x) < 1e-6 && Math.Abs(s.Y - y) < 1e-6);

    // ---- the ending ----

    internal void Fail(string why)
    {
        MustBePending();
        if (string.IsNullOrWhiteSpace(why)) throw new GolemDomainException($"failing mission {Id} needs a reason");
        status = MissionStatus.Failed;
        reason = why;
    }

    /// <summary>The golem lets the mission go: a newer told point made it pointless, or the operator let go of everything.</summary>
    internal void Abandon(string why)
    {
        MustBePending();
        if (string.IsNullOrWhiteSpace(why)) throw new GolemDomainException($"abandoning mission {Id} needs a reason");
        status = MissionStatus.Abandoned;
        reason = why;
    }

    internal void Announce()
    {
        if (reached == 0) throw new GolemDomainException($"mission {Id} has reached no stop yet: only a reached stop is announced");
        Announced = true;
    }

    private void MustBePending()
    {
        if (!IsPending())
            throw new GolemDomainException($"mission {Id} is already {status.Name}");
    }
}
