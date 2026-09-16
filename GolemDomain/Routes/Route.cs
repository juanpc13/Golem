using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Maps;
using GolemDomain.Touches;

namespace GolemDomain.Routes;

/// <summary>
/// A route — Juan's RUTA, 14-sep-2026: "visit, luego route devuelve un objeto con todos los puntos a visitar y eso es
/// lo que se está siguiendo"; 16-sep-2026: "la route tiene la lista de los puntos, y cuando `route.Reach` internamente
/// se mueve al siguiente punto, el print dice todo lo necesario: si tiene que girar entonces gira… y así hasta
/// completar todo lo que proponía la ruta". The errand AND its way, one object, decided INSIDE: the stops to reach
/// (one, or several), whether the golem may choose their order, the legs it planned from where the body stood (the
/// planner's answer, held here, never written point by point), the cursor, the touches on the way, the hold, and how it
/// ended. The golem hands it out (<c>route = g.Visit(from, point)</c>, <c>g.Cover</c>, <c>g.Follow</c>) or finds it
/// again (<c>route = g.Find(@id)</c>); every act on it is its own: <c>route.Then(point); route.Turn(); route.Reach(point);
/// route.Decide(from); route.DecidePast(who, me); route.Bump(touch); route.Graze(at); route.Pause(); route.Resume();
/// route.Fail(why); route.Abandon(why); route.Announce();</c>. What it asks of the body NOW is <see cref="Order"/>:
/// one thing at a time — turn to the next leg's heading, run to its point, hold, or decide the way again.
/// </summary>
internal sealed class Route
{
    internal int Id { get; }
    private readonly MapLayout layout;         // where its doors stand and its stops lie
    private readonly Collisions collisions;    // what a bump on the way teaches, and what the planner skirts
    private readonly double radius;            // the body the way is planned for
    private readonly double retreat;           // how far it backs off after a touch, before anything else
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
    private Trajectory way = new(Array.Empty<Leg>());   // the plan: passages to cross, points to pass, stops to reach, in order
    private Position origin;                             // where the way was decided from (the errand's start, or where the body stood)
    private int nextLeg;                                 // the first leg not yet known to be walked
    private bool turned;                                 // the body already turned into the next leg's heading
    private int reached;                                 // stops reached so far
    private int bumps;                                   // times the body touched something the map did not hold, on this route
    private int bumpsSinceRoute;                         // ...since the way was last decided: a reason to decide it again
    private int grazes;                                  // times the body grazed a wall it knows, on this route
    private int grazesOnLeg;                             // ...since the last stop reached or way decided: the golem's patience with its own error

    /// <summary>How many times the golem retries after grazing a known wall before it gives the route up.</summary>
    internal const int PatienceWithWalls = 3;

    internal Route(int id, Position stop, bool following, bool choosesOrder, MapLayout layout, Collisions collisions, double radius, double retreat)
    {
        if (stop == null) throw new GolemDomainException("Route.Route: 'stop' was not given");
        if (layout == null) throw new GolemDomainException($"route {id} is decided on a layout");
        if (collisions == null) throw new GolemDomainException($"route {id} needs the collisions module, even empty");
        if (radius < 0) throw new GolemDomainException($"route {id} is planned for a body: its radius cannot be negative");
        if (retreat < 0) throw new GolemDomainException($"route {id} is planned for a body: its retreat cannot be negative");
        this.layout = layout;
        this.collisions = collisions;
        this.radius = radius;
        this.retreat = retreat;
        Id = id;
        Following = following;
        ChoosesOrder = choosesOrder;
        stops.Add(OnTheMap(stop));
    }

    // ---- the stops ----

    /// <summary>One more stop, after the ones given — while the route is pending and before the body set out on it.
    /// A route that already has its way decides it again from the same start, through every stop.</summary>
    internal Route Then(Position stop)
    {
        if (stop == null) throw new GolemDomainException("Route.Then: 'stop' was not given");
        MustBePending();
        if (Following) throw new GolemDomainException($"route {Id} follows a peer: a told point is one route each");
        if (nextLeg > 0 || reached > 0 || turned) throw new GolemDomainException($"route {Id} is already underway: no stop can be added");
        stops.Add(OnTheMap(stop));
        if (origin != null) Plan(origin);
        return this;
    }

    /// <summary>One more stop: an area's centre — <c>route.Then(map.Find(@area))</c>.</summary>
    internal Route Then(Area area)
    {
        if (area == null) throw new GolemDomainException($"route {Id} needs an area to add");
        return Then(layout.Of(area).Center);
    }

    private Position OnTheMap(Position stop)
    {
        if (stop == null) throw new GolemDomainException($"route {Id} needs a stop");
        if (layout.ZoneCount > 0 && !layout.IsOnMap(stop))
            throw new GolemDomainException($"the point ({Fmt(stop.X)}, {Fmt(stop.Y)}) is nowhere on the map");
        return stop;
    }

    internal bool IsPending() => status == RouteStatus.Pending;
    /// <summary>pending, completed, failed or abandoned.</summary>
    internal string Status => status.Name;

    // ---- the way: decided inside, from a point, through the stops ahead ----

    /// <summary>The way decided from a point — where the errand starts, or where the body stands after a bump, or at
    /// wake with a plan underway: the shortest road through the stops ahead (in the order given, or the one the golem
    /// chooses), doors crossed straight, openings named, marks skirted. What was left of the old way is replaced; the
    /// body turns and runs from here. Refused when no way fits the body.</summary>
    internal Route Decide(Position from)
    {
        if (from == null) throw new GolemDomainException("Route.Decide: 'from' was not given");
        MustBePending();
        Plan(from);
        return this;
    }

    /// <summary>The way decided out of a peer's way: the first leg is the courtesy step — a body's width to ONE SIDE of
    /// where the golem faces, chosen so it moves away from the peer and where its own body fits (both bodies step to their
    /// own right when they can, which is how two of them pass instead of shove) — then the road on to the stops ahead.
    /// Without room to step aside, the plain way from here.</summary>
    internal Route DecidePast(string who, Pose me)
    {
        if (me == null) throw new GolemDomainException("Route.DecidePast: 'me' was not given");
        MustBePending();
        var aside = Courtesy.StepOutOfTheWayOf(layout, collisions, radius, who, me);
        if (aside == null) { Plan(me); return this; }
        var legs = new List<Leg> { new(aside, Leg.Courtesy) };
        legs.AddRange(Planner().Road(aside, Ordered(aside)).Legs());
        Take(new Trajectory(legs), me);
        return this;
    }

    private RoutePlanner Planner() => new(layout, collisions, radius);

    // The stops still ahead, in the order they will be reached: a Cover route lets the planner choose it, and keeps
    // that order as its own from then on (so what it reached and what lies ahead read from one list).
    private IReadOnlyList<Position> Ordered(Position from)
    {
        var ahead = stops.Skip(reached).ToList();
        if (!ChoosesOrder) return ahead;
        var chosen = Planner().BestOrder(from, ahead);
        stops.RemoveRange(reached, ahead.Count);
        stops.AddRange(chosen);
        return chosen;
    }

    private void Plan(Position from) => Take(Planner().Road(from, Ordered(from)), from);

    // The way decided: passages, points and stops, in order, the last stop last, every leg with the heading it is
    // walked into — the first from the point decided from. A new way replaces what was left of the old one and must
    // still reach every stop ahead.
    private void Take(Trajectory fresh, Position from)
    {
        if (fresh == null || fresh.IsEmpty) throw new GolemDomainException($"route {Id} needs at least the stop as a leg");
        if (!fresh.Last.IsStop) throw new GolemDomainException($"route {Id}'s way must end at a stop, not at '{fresh.Last.Name}'");
        int ahead = stops.Count - reached;
        if (fresh.StopCount != ahead) throw new GolemDomainException($"route {Id} has {ahead} stops ahead but the way reaches {fresh.StopCount}");
        way = fresh.WalkedFrom(from);
        origin = from;
        nextLeg = 0;
        turned = false;
        bumpsSinceRoute = 0;
        grazesOnLeg = 0;
    }

    internal bool IsRouted => !way.IsEmpty;
    /// <summary>What the route asks of the body NOW, one thing at a time: 'hold' (the operator paused it), 'decide' (it
    /// has no way yet, or its corrections ran out without a way: the way must be decided again from where the body
    /// stands), 'back' (reverse to the correction point a touch inserted), 'turn' (turn in place to the next leg's
    /// heading) or 'run' (run to the next leg's point). The golem prints it after every act that changes it; the body
    /// does that one thing and reports it.</summary>
    internal string Order => Paused ? "hold"
        : (!IsRouted || nextLeg >= way.Count) ? "decide"
        : NextLeg.IsReverse ? "back"
        : BumpedSinceRoute ? "decide"
        : (NextLeg.HasHeading && !turned) ? "turn" : "run";
    /// <summary>Whether the body may act on the next leg now — back, turn or run: a leg ahead, not held.</summary>
    internal bool IsWalkable => IsPending() && IsRouted && nextLeg < way.Count && !Paused && Order != "decide";
    internal int LegsLeft => way.Count - nextLeg;
    /// <summary>The legs not yet known to be walked: the plan ahead, from the first one on.</summary>
    internal IReadOnlyList<Leg> LegsAhead => way.Legs().Skip(nextLeg).ToList();
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
    /// <summary>Stops not reached yet, in the order they will be reached.</summary>
    internal IEnumerable<Position> StopsAhead => stops.Skip(reached);
    internal int StopsLeft => StopsAhead.Count();
    /// <summary>The way as it reads in one line — "kitchen/west@0.75,8 > west/living@0.75,3 > living@2,1.5" — a lab reading.</summary>
    internal string AsPlan() => way.AsPlan();
    internal int Bumps => bumps;
    internal bool BumpedSinceRoute => bumpsSinceRoute > 0;
    internal int Grazes => grazes;
    internal int GrazesOnLeg => grazesOnLeg;
    /// <summary>Whether the golem still retries after grazing a known wall — patience not yet spent since the last stop or way.</summary>
    internal bool MayRetryLeg => IsPending() && grazesOnLeg < PatienceWithWalls;

    // ---- the walk: the body reports each thing done, the route moves its cursor and asks the next ----

    /// <summary>The body turned to the heading the next leg asked: the route now asks it to run.</summary>
    internal Route Turn()
    {
        MustBePending();
        if (Order != "turn") throw new GolemDomainException($"route {Id} asked no turn now: it asks '{Order}'");
        turned = true;
        return this;
    }

    /// <summary>The body reached a point of the way — the next leg, a door, an opening, a point to pass, a stop: one at a
    /// time, the body reports each and the route hands out the next (Juan, 16-sep-2026: "una cosa a la vez"). The legs
    /// before it count as walked. Reaching a stop counts it; reaching the last stop completes the route. A way is not
    /// required: a route without one reaches its stops in order.</summary>
    internal Route Reach(Position at)
    {
        MustBePending();
        if (at == null) throw new GolemDomainException($"route {Id} reaches a point");
        if (IsRouted)
        {
            int index = AheadAt(at);
            if (index < 0)
            {
                var next = nextLeg < way.Count ? way.LegAt(nextLeg) : null;
                var nextStop = StopsAhead.FirstOrDefault();
                throw new GolemDomainException(next == null
                    ? $"route {Id} has nothing ahead at ({at.X}, {at.Y})"
                    : nextStop != null && way.Legs().Skip(nextLeg).Any(l => Same(l.At, at))
                        ? $"route {Id}'s next stop is ({nextStop.X}, {nextStop.Y}), not ({at.X}, {at.Y}): a stop is not skipped"
                        : $"route {Id}'s next point is ({next.At.X}, {next.At.Y}), not ({at.X}, {at.Y})");
            }
            bool stop = way.LegAt(index).IsStop;
            nextLeg = index + 1;
            turned = false;
            grazesOnLeg = 0;
            if (!stop) return this;
        }
        else
        {
            var next = stops[reached];
            if (!Same(next, at))
                throw new GolemDomainException($"route {Id}'s next stop is ({next.X}, {next.Y}), not ({at.X}, {at.Y})");
            grazesOnLeg = 0;
        }
        reached++;
        if (reached == stops.Count) status = RouteStatus.Completed;
        return this;
    }

    /// <summary>Whether a point of the way lies ahead — a leg not yet reached, up to and including the next stop — what
    /// to consult before saying it was.</summary>
    internal bool IsLegAhead(Position at)
    {
        if (at == null) throw new GolemDomainException("Route.IsLegAhead: 'at' was not given");
        return IsPending() && (IsRouted ? AheadAt(at) >= 0 : IsStopAhead(at));
    }

    // The index of the leg at a point among those ahead, up to and including the next stop (the passages and points
    // before it may go unreported — the body ran through them — but a stop is never skipped); -1 when none.
    private int AheadAt(Position at)
    {
        for (int i = nextLeg; i < way.Count; i++)
        {
            if (Same(way.LegAt(i).At, at)) return i;
            if (way.LegAt(i).IsStop) return -1;
        }
        return -1;
    }

    /// <summary>Whether a stop of this route lies ahead at a point — what to consult before saying it was reached.</summary>
    internal bool IsStopAhead(Position at)
    {
        if (at == null) throw new GolemDomainException("Route.IsStopAhead: 'at' was not given");
        return IsPending() && StopsAhead.Any(s => Same(s, at));
    }

    /// <summary>The body touched something the map does not hold — where and heading which way (the touch), and where the
    /// body stood facing which way (me) — on this route. The golem presumes a THING there and marks it at once (a peer
    /// that says it was there takes it back — Met; told to the peers, exposed beside the act). And the route CORRECTS
    /// ITS WAY INSIDE (Juan, 16-sep-2026: "cuando choca queriendo llegar de A a B mete entre A y B otros puntos: ir para
    /// atrás un poco y pasar al lado… posiciones de corrección"): first a leg to back off — in reverse, the body's own
    /// retreat behind where it stood — then the planner's road from there through the stops ahead, skirting the figure
    /// the mark now outlines. When no way fits from there, the retreat alone stays and the route asks 'decide' after it.</summary>
    internal Route Bump(Pose touch, Pose me)
    {
        MustBePending();
        if (touch == null) throw new GolemDomainException($"route {Id}'s bump needs the pose of the touch");
        if (me == null) throw new GolemDomainException($"route {Id}'s bump needs where the body stood");
        if (ReferenceEquals(touch, me)) throw new GolemDomainException("Route.Bump: 'touch' and 'me' are the same pose");
        bumps++;
        bumpsSinceRoute++;
        collisions.Mark(touch);
        Correct(me, replan: true);
        return this;
    }

    /// <summary>The body grazed a wall the map KNOWS, on this route: its own execution error, no discovery, counted
    /// against its patience since the last stop reached or way decided. The way gains a retreat first, then goes on as
    /// it was (the same legs, tried again from a body's length back).</summary>
    internal Route Graze(Position at, Pose me)
    {
        MustBePending();
        if (at == null) throw new GolemDomainException($"route {Id}'s graze needs where it happened");
        if (me == null) throw new GolemDomainException($"route {Id}'s graze needs where the body stood");
        if (ReferenceEquals(at, me)) throw new GolemDomainException("Route.Graze: 'at' and 'me' are the same position");
        grazes++;
        grazesOnLeg++;
        Correct(me, replan: false);
        return this;
    }

    // The corrections a touch inserts ahead of what was left: back off first (a leg walked in reverse, the body's retreat
    // behind where it stood), then — replanning — the road from there through the stops ahead, or — not replanning — the
    // legs that were left, walked again from the retreat point. If no road fits from there, the retreat alone stays and
    // the route asks 'decide' once it is done (the way is exhausted without completing).
    private void Correct(Pose me, bool replan)
    {
        var back = me.Along(me.Heading + Math.PI, retreat);
        var legs = new List<Leg> { new(back, Leg.Retreat) };
        if (replan)
        {
            try { legs.AddRange(Planner().Road(back, Ordered(back)).Legs()); }
            catch (GolemDomainException)
            {
                way = new Trajectory(legs).WalkedFrom(me);   // stranded: back off, then decide again
                nextLeg = 0;
                turned = false;
                return;
            }
        }
        else
            foreach (var left in way.Legs().Skip(nextLeg))
                legs.Add(new Leg(left.At, left.Name, left.Approach, left.Exit));   // its heading is given again, from the retreat point
        int patience = grazesOnLeg;
        Take(new Trajectory(legs), me);
        if (!replan) grazesOnLeg = patience;   // the same leg, tried again: the patience spent on it stays spent
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

    /// <summary>The operator lets the route go on: the body takes up the leg it was on, from where it stands.</summary>
    internal Route Resume()
    {
        MustBePending();
        if (!Paused) throw new GolemDomainException($"route {Id} is not paused");
        Paused = false;
        return this;
    }

    // ---- the ending ----

    /// <summary>The world said no — a collision, a stall, no way. The reason is the act's, kept by the journal.</summary>
    internal Route Fail(string why)
    {
        MustBePending();
        if (string.IsNullOrWhiteSpace(why)) throw new GolemDomainException($"failing route {Id} needs a reason");
        status = RouteStatus.Failed;
        return this;
    }

    /// <summary>The golem lets the route go: a newer told point made it pointless, or the operator let go of everything.</summary>
    internal Route Abandon(string why)
    {
        MustBePending();
        if (string.IsNullOrWhiteSpace(why)) throw new GolemDomainException($"abandoning route {Id} needs a reason");
        status = RouteStatus.Abandoned;
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
