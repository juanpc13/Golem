using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Maps;
using GolemDomain.Touches;

namespace GolemDomain.Routes;

/// <summary>
/// A route — Juan's RUTA, 14-sep-2026: "visit, luego route devuelve un objeto con todos los puntos a visitar y eso es
/// lo que se está siguiendo". The errand AND its way, one object: the stops to reach (one, or several), whether the
/// golem may choose their order, the legs it decided (written as points; the route names each against the map — a
/// door where a door stands, an opening on a shared edge, a stop where a stop is, a point anywhere else), the cursor,
/// the touches on the way, the hold, and how it ended. The golem hands it out (<c>route = g.Visit(point)</c>,
/// <c>g.Cover</c>, <c>g.Follow</c>) or finds it again (<c>route = g.Find(@id)</c>); every act on it is its own:
/// <c>route.Then(point2); route.Via(via1); route.Stop(point); route.Reach(point); route.Bump(touch); route.Graze(at);
/// route.Pause(); route.Resume(); route.Fail(why); route.Abandon(why); route.Announce();</c>. The way is the PLAN,
/// written whole; walking it is not journaled step by step — only what changes the plan (a touch, another way) or
/// fulfils it (a stop reached) is (Juan, 10-sep-2026: "no estar diciéndole cada cosa que va haciendo").
/// </summary>
internal sealed class Route
{
    internal int Id { get; }
    private readonly MapLayout layout;         // where its doors stand and its stops lie
    private readonly Collisions collisions;    // what a bump on the way teaches
    private readonly List<Position> stops = new();
    /// <summary>Where to go, in the order given.</summary>
    internal IReadOnlyList<Position> Stops => stops;
    /// <summary>True when the stop came from a peer's tell (the golem follows), false when the operator ordered it.</summary>
    internal bool Following { get; }
    /// <summary>True when the golem may reorder the stops for the shortest way (Cover), false when the order is the operator's (Visit).</summary>
    internal bool ChoosesOrder { get; }
    /// <summary>True once the golem has announced a reached stop to its peer.</summary>
    internal bool Announced { get; private set; }
    /// <summary>Whether the operator holds the route: the body stands where it is until it is resumed. The plan, the
    /// cursor and the stops ahead are untouched — a pause is a hold, not an interruption.</summary>
    internal bool Paused { get; private set; }

    private RouteStatus status = RouteStatus.Pending;
    private string reason = "";
    private Trajectory way = new(Array.Empty<Leg>());   // the plan: passages to cross, points to pass, stops to reach, in order
    private List<Leg> deciding;                          // the legs being written, until the last stop closes them; null between decisions
    private int nextLeg;                                 // the first leg not yet known to be walked
    private int reached;                                 // stops reached so far
    private int bumps;                                   // times the body touched something the map did not hold, on this route
    private int bumpsSinceRoute;                         // ...since the way was last decided: a reason to decide it again
    private int grazes;                                  // times the body grazed a wall it knows, on this route
    private int grazesOnLeg;                             // ...since the last stop reached or way decided: the golem's patience with its own error

    /// <summary>How many times the golem retries after grazing a known wall before it gives the route up.</summary>
    internal const int PatienceWithWalls = 3;

    internal Route(int id, Position stop, bool following, bool choosesOrder, MapLayout layout, Collisions collisions)
    {
        this.layout = layout ?? throw new GolemDomainException($"route {id} is decided on a layout");
        this.collisions = collisions ?? throw new GolemDomainException($"route {id} needs the collisions module, even empty");
        Id = id;
        Following = following;
        ChoosesOrder = choosesOrder;
        stops.Add(OnTheMap(stop));
    }

    // ---- the stops ----

    /// <summary>One more stop, after the ones given — while the route is pending and before its way is decided.</summary>
    internal Route Then(Position stop)
    {
        MustBePending();
        if (Following) throw new GolemDomainException($"route {Id} follows a peer: a told point is one route each");
        if (IsRouted) throw new GolemDomainException($"route {Id} already has its way: no stop can be added");
        stops.Add(OnTheMap(stop));
        return this;
    }

    /// <summary>One more stop: an area's centre — <c>route.Then(map.Find(@area))</c>.</summary>
    internal Route Then(Area area) => Then(layout.Of(area ?? throw new GolemDomainException($"route {Id} needs an area to add")).Center);

    private Position OnTheMap(Position stop)
    {
        if (stop == null) throw new GolemDomainException($"route {Id} needs a stop");
        if (layout.ZoneCount > 0 && !layout.IsOnMap(stop))
            throw new GolemDomainException($"the point ({Fmt(stop.X)}, {Fmt(stop.Y)}) is nowhere on the map");
        return stop;
    }

    internal bool IsPending() => status == RouteStatus.Pending;
    internal string ReadStatus() => status.Name;
    internal string ReadReason() => reason;

    // ---- the way: written as points, one act per leg, the last stop last — the route names each against the map ----

    /// <summary>A leg of the way being decided: pass through this point. The route says what the point is — a door of
    /// the map (where the layout stands it), an opening (on the edge two areas share), or a plain point to pass. The
    /// first leg after a decided way opens a new decision, which replaces what was left of the old one once its last
    /// stop is written.</summary>
    internal Route Via(Position at)
    {
        MustBePending();
        if (at == null) throw new GolemDomainException($"route {Id} passes through a point");
        deciding ??= new List<Leg>();
        deciding.Add(NameLeg(at));
        return this;
    }

    /// <summary>A leg of the way being decided: reach this stop — one of the stops ahead, named by the zone it stands in.
    /// When every stop ahead has its leg, the way is decided: doors gain their straight crossings and the route takes it.</summary>
    internal Route Stop(Position at)
    {
        MustBePending();
        if (at == null) throw new GolemDomainException($"route {Id} stops at a point");
        if (!StopsAhead.Any(s => Same(s, at)))
            throw new GolemDomainException($"({Fmt(at.X)}, {Fmt(at.Y)}) is not a stop ahead of route {Id}");
        deciding ??= new List<Leg>();
        deciding.Add(new Leg(at, layout.ZoneAt(at).Name));
        if (deciding.Count(l => l.IsStop) == StopsAhead.Count())
        {
            Take(layout.WithDoorCrossings(new Trajectory(deciding)));
            deciding = null;
        }
        return this;
    }

    /// <summary>A stop written as the area the errand named — <c>route.Stop(point)</c> where <c>point = map.Find(@area)</c>:
    /// its centre. (The engine binds a method by the runtime type of what it is given: an area needs its own way in.)</summary>
    internal Route Stop(Area area) => Stop(layout.Of(area ?? throw new GolemDomainException($"route {Id} stops at an area")).Center);

    private Leg NameLeg(Position at)
    {
        foreach (var door in layout.PlacedDoors)
            if (Same(door.At, at)) return new Leg(at, door.Door.Name);
        foreach (var opening in layout.Openings)
            if (layout.Touches(opening) && layout.EdgeOf(opening).DistanceTo(at) < 1e-6) return new Leg(at, opening.Name);
        return new Leg(at, Leg.Waypoint);
    }

    // The way decided: passages, points and stops, in order, the last stop last. A new way replaces what was left of
    // the old one and must still reach every stop ahead.
    private void Take(Trajectory fresh)
    {
        if (fresh == null || fresh.IsEmpty) throw new GolemDomainException($"route {Id} needs at least the stop as a leg");
        if (!fresh.Last.IsStop) throw new GolemDomainException($"route {Id}'s way must end at a stop, not at '{fresh.Last.Name}'");
        int ahead = stops.Count - reached;
        if (fresh.StopCount != ahead) throw new GolemDomainException($"route {Id} has {ahead} stops ahead but the way reaches {fresh.StopCount}");
        way = fresh;
        nextLeg = 0;
        bumpsSinceRoute = 0;
        grazesOnLeg = 0;
    }

    internal bool IsRouted => !way.IsEmpty;
    internal int LegsLeft => way.Count - nextLeg;
    /// <summary>The legs not yet known to be walked: the plan ahead, from the first one on.</summary>
    internal IEnumerable<Leg> LegsAhead => way.Legs().Skip(nextLeg);
    /// <summary>The leg the body heads to: the first ahead once routed; before that, the next stop itself.</summary>
    internal Leg NextLeg
    {
        get
        {
            if (IsRouted)
            {
                if (nextLeg >= way.Count) throw new GolemDomainException($"route {Id} has walked its whole way");
                return way.LegAt(nextLeg);
            }
            if (reached >= stops.Count) throw new GolemDomainException($"route {Id} has reached every stop");
            return new Leg(stops[reached], "");
        }
    }
    /// <summary>Whether there is still somewhere to head to: a leg ahead once routed, a stop ahead before that.</summary>
    internal bool HasNextPoint => IsPending() && (IsRouted ? nextLeg < way.Count : reached < stops.Count);
    /// <summary>Stops not reached yet: the stop legs ahead once routed, every stop before that.</summary>
    internal IEnumerable<Position> StopsAhead => IsRouted ? way.StopsFrom(nextLeg) : stops.Skip(reached);
    internal int StopsLeft => StopsAhead.Count();
    internal int Bumps => bumps;
    internal bool BumpedSinceRoute => bumpsSinceRoute > 0;
    internal int Grazes => grazes;
    internal int GrazesOnLeg => grazesOnLeg;
    /// <summary>Whether the golem still retries after grazing a known wall — patience not yet spent since the last stop or way.</summary>
    internal bool MayRetryLeg => IsPending() && grazesOnLeg < PatienceWithWalls;

    // ---- the walk: only what fulfils the plan or interrupts it ----

    /// <summary>The golem reached a stop of its way: the legs before it were walked, whatever they were. Reaching the
    /// last stop completes the route. A way is not required: a route without one reaches its stops in order.</summary>
    internal Route Reach(Position at)
    {
        MustBePending();
        if (at == null) throw new GolemDomainException($"route {Id} reaches a point");
        if (IsRouted)
        {
            int index = -1;
            for (int i = nextLeg; i < way.Count; i++)
            {
                var leg = way.LegAt(i);
                if (leg.IsStop && Same(leg.At, at)) { index = i; break; }
            }
            if (index < 0)
            {
                var next = way.Legs().Skip(nextLeg).FirstOrDefault(l => l.IsStop);
                throw new GolemDomainException(next == null
                    ? $"route {Id} has no stop ahead at ({at.X}, {at.Y})"
                    : $"route {Id}'s next stop is ({next.At.X}, {next.At.Y}), not ({at.X}, {at.Y})");
            }
            nextLeg = index + 1;
        }
        else
        {
            var next = stops[reached];
            if (!Same(next, at))
                throw new GolemDomainException($"route {Id}'s next stop is ({next.X}, {next.Y}), not ({at.X}, {at.Y})");
        }
        reached++;
        grazesOnLeg = 0;
        bool done = IsRouted ? !way.Legs().Skip(nextLeg).Any(l => l.IsStop) : reached == stops.Count;
        if (done) status = RouteStatus.Completed;
        return this;
    }

    /// <summary>Whether a stop of this route lies ahead at (x, y) — what to consult before saying it was reached.</summary>
    internal bool IsStopAhead(double x, double y) =>
        IsPending() && StopsAhead.Any(s => Math.Abs(s.X - x) < 1e-6 && Math.Abs(s.Y - y) < 1e-6);

    /// <summary>The body touched something the map does not hold — where, heading which way — on this route: the plan
    /// is interrupted, and the golem presumes a THING there: a mark, at once (a peer that says it was there takes it
    /// back — Met). Told to the peers (exposed beside the act).</summary>
    internal Route Bump(Pose touch)
    {
        MustBePending();
        if (touch == null) throw new GolemDomainException($"route {Id}'s bump needs the pose of the touch");
        bumps++;
        bumpsSinceRoute++;
        collisions.Mark(touch);
        return this;
    }

    /// <summary>The body grazed a wall the map KNOWS, on this route: its own execution error, no discovery, counted
    /// against its patience since the last stop reached or way decided.</summary>
    internal Route Graze(Position at)
    {
        MustBePending();
        if (at == null) throw new GolemDomainException($"route {Id}'s graze needs where it happened");
        grazes++;
        grazesOnLeg++;
        return this;
    }

    // ---- the hold (Juan, 14-sep-2026: "pausa/continuar el trayecto actual en ejecución") ----

    /// <summary>The operator holds the route: the body stops where it stands and waits. Only a pending route can be
    /// held, and only once.</summary>
    internal Route Pause()
    {
        MustBePending();
        if (Paused) throw new GolemDomainException($"route {Id} is already paused");
        Paused = true;
        return this;
    }

    /// <summary>The operator lets the route go on: the body takes up the leg it was walking, from where it stands.</summary>
    internal Route Resume()
    {
        MustBePending();
        if (!Paused) throw new GolemDomainException($"route {Id} is not paused");
        Paused = false;
        return this;
    }

    // ---- the ending ----

    /// <summary>The world said no — a collision, a stall, no way — and the reason is kept.</summary>
    internal Route Fail(string why)
    {
        MustBePending();
        if (string.IsNullOrWhiteSpace(why)) throw new GolemDomainException($"failing route {Id} needs a reason");
        status = RouteStatus.Failed;
        reason = why;
        return this;
    }

    /// <summary>The golem lets the route go: a newer told point made it pointless, or the operator let go of everything.</summary>
    internal Route Abandon(string why)
    {
        MustBePending();
        if (string.IsNullOrWhiteSpace(why)) throw new GolemDomainException($"abandoning route {Id} needs a reason");
        status = RouteStatus.Abandoned;
        reason = why;
        return this;
    }

    /// <summary>The golem puts on record that it announces a reached stop to its peer (the tell follows in the same entry).</summary>
    internal Route Announce()
    {
        if (reached == 0) throw new GolemDomainException($"route {Id} has reached no stop yet: only a reached stop is announced");
        Announced = true;
        return this;
    }

    private void MustBePending()
    {
        if (!IsPending())
            throw new GolemDomainException($"route {Id} is already {status.Name}");
    }

    private static bool Same(Position a, Position b) => Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;

    private static string Fmt(double d) => d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
