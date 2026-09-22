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
/// again (<c>route = g.Find(@id)</c>); every act on it is its own: <c>route.Then(point); route.Turn(me); route.Reach(me);
/// route.Decide(from); route.DecidePast(who, me); route.Bump(touch, me); route.Graze(at, me); route.Pause(me); route.Resume(me);
/// route.Fail(why); route.Abandon(why); route.Announce();</c>. What it asks of the body NOW is <see cref="Order"/>, IN THE
/// ROBOT'S OWN WORDS (Juan, 17-sep-2026: "al robot se le dice muy sencillamente lo que debe moverse hacia adelante, qué
/// tanto debe rotar"): one base action at a time — <c>advance</c>, <c>back</c>, <c>turnLeft</c>, <c>turnRight</c>, <c>stop</c> —
/// with its <see cref="Amount"/> (metres, or radians). Never 'decide' (18-sep-2026): a route is born with its way and decides
/// it again BY ITSELF when it must (stranded after a retreat, awake with a plan underway). The route remembers where the
/// body STANDS and faces (every act of the cursor brings the pose, and tells its golem), so the amounts start from where
/// the body really is, not from where it should be.
/// </summary>
internal sealed class Route
{
    internal int Id { get; }
    private readonly MapLayout layout;         // where its doors stand and its stops lie
    private readonly Collisions collisions;    // what a bump on the way teaches, and what the planner skirts
    private readonly double radius;            // the body the way is planned for
    private readonly double retreat;           // how far it backs off after a touch, before anything else
    private readonly Action<Pose> stood;       // what the route tells its golem when the body reported where it stood (a turn, a move)
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
    /// <summary>Where the body stood, facing which way, when the operator held the route (Juan, 17-sep-2026: "guardar la
    /// posición actual… y cuando le den resume, desde su posición hacia la siguiente que tenía en ruta"). Null until held.</summary>
    internal Pose HeldAt { get; private set; }

    private RouteStatus status = RouteStatus.Pending;
    private Trajectory way = new(Array.Empty<Leg>());   // the plan: passages to cross, points to pass, stops to reach, in order
    private Position origin;                             // where the way was decided from (the errand's start, or where the body stood)
    private Pose standing;                               // where the body stands and faces, as its last act reported it
    private int nextLeg;                                 // the first leg not yet known to be walked
    private bool turned;                                 // the body already turned to face the next leg's point
    private int turnsOnLeg;                              // turns asked on the leg ahead: a body that cannot line up is not asked forever
    private bool linedUp;                                // the body reached the next leg's approach: a door lined up, to be crossed to its exit
    private int reached;                                 // stops reached so far
    private int bumps;                                   // times the body touched something the map did not hold, on this route
    private int bumpsSinceRoute;                         // ...since the way was last decided: a reason to decide it again
    private int grazes;                                  // times the body grazed a wall it knows, on this route
    private int grazesOnLeg;                             // ...since the last stop reached or way decided: the golem's patience with its own error
    private Position lastTouch;                          // where the last touch landed: what the way past it must leave behind

    /// <summary>How many times the golem retries after grazing a known wall before it gives the route up.</summary>
    internal const int PatienceWithWalls = 3;
    /// <summary>How many things the body may bump into on one route before the golem gives it up.</summary>
    internal const int PatienceWithThings = 6;
    /// <summary>A turn smaller than this (radians) is not asked: the body already faces its point closely enough.</summary>
    internal const double TurnTolerance = 0.05;
    /// <summary>How many turns a route asks on one leg before it takes the heading the body reached (22-sep-2026).</summary>
    internal const int TurnsAtMost = 3;
    /// <summary>The clearance a follower keeps beyond the two bodies on its last stop, in metres.</summary>
    internal const double FollowerClearance = 0.5;
    /// <summary>How short of a leader's spot a follower stops on its last stop: the two bodies and a clearance (the leader may
    /// still be there). The fleet shares one body, so the leader's radius is this body's (18-sep-2026: the tell carries no
    /// radius yet — a heterogeneous fleet would need it).</summary>
    internal double FollowerStandoff => 2 * radius + FollowerClearance;

    internal Route(int id, Position stop, bool following, bool choosesOrder, MapLayout layout, Collisions collisions, double radius, double retreat, Action<Pose> stood)
    {
        if (stop == null) throw new GolemDomainException("Route.Route: 'stop' was not given");
        if (layout == null) throw new GolemDomainException($"route {id} is decided on a layout");
        if (collisions == null) throw new GolemDomainException($"route {id} needs the collisions module, even empty");
        if (stood == null) throw new GolemDomainException("Route.Route: 'stood' was not given");
        if (radius < 0) throw new GolemDomainException($"route {id} is planned for a body: its radius cannot be negative");
        if (retreat < 0) throw new GolemDomainException($"route {id} is planned for a body: its retreat cannot be negative");
        this.layout = layout;
        this.collisions = collisions;
        this.radius = radius;
        this.retreat = retreat;
        this.stood = stood;
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
        if (nextLeg > 0 || reached > 0 || turned || linedUp) throw new GolemDomainException($"route {Id} is already underway: no stop can be added");
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

    /// <summary>The way decided from a point — where the errand starts (the golem writes it when it hands the route out),
    /// or wherever the lab puts the body: the shortest road through the stops ahead (in the order given, or the one the
    /// golem chooses), doors crossed straight, openings named, marks skirted. What was left of the old way is replaced; the
    /// body turns and runs from here. Refused when no way fits the body. Repertoire since 18-sep-2026: the host writes no
    /// 'decide' — the route decides again by itself (<see cref="Awake"/>, PlanAgainFrom).</summary>
    internal Route Decide(Position from)
    {
        if (from == null) throw new GolemDomainException("Route.Decide: 'from' was not given");
        MustBePending();
        Plan(from);
        return this;
    }

    /// <summary>The golem woke with this route underway and its body wherever it was carried meanwhile: the way is decided
    /// again from where the body stands, by the route itself; no way from there, the route fails by itself (the golem's
    /// <c>Wake(me)</c>, 18-sep-2026).</summary>
    internal Route Awake(Pose me)
    {
        if (me == null) throw new GolemDomainException("Route.Awake: 'me' was not given");
        MustBePending();
        PlanAgainFrom(me);
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
        var aside = Courtesy.StepOutOfTheWayOf(layout, collisions, radius, who, me, PassingStep);   // as far as passing a body wants (ajuste 47)
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
        standing = StandingAt(from);
        nextLeg = 0;
        turned = false;
        turnsOnLeg = 0;
        linedUp = false;
        bumpsSinceRoute = 0;
        grazesOnLeg = 0;
    }

    /// <summary>Whether the body has set out on this route: a turn made, a door lined up, a leg or a stop behind.</summary>
    internal bool HasSetOut => turned || linedUp || nextLeg > 0 || reached > 0;

    /// <summary>Where the body stands NOW, told by the golem to a route the body has not set out on yet — a route queued behind
    /// another was planned from where that one was expected to end, and the body ends up nearby, not there (22-sep-2026 live:
    /// blue's first turn on a queued route was measured from the planned pose and sent it the wrong way). The way keeps; the
    /// first order is measured from the real pose.</summary>
    internal Route StandAt(Pose me)
    {
        if (me == null) throw new GolemDomainException("Route.StandAt: 'me' was not given");
        if (HasSetOut) throw new GolemDomainException($"route {Id} is already underway: the body's pose enters through its acts");
        standing = me;
        if (IsRouted && way.Count > 0)
        {
            var legs = way.Legs().ToList();
            legs[0] = legs[0].WalkedFrom(me);
            way = new Trajectory(legs);
        }
        return this;
    }

    // Where the body stands after an act that brings a point: the pose itself when it is one; a bare position keeps the
    // heading the body was last known to face (an errand opened from a point, a way decided again from one).
    private Pose StandingAt(Position at) => at as Pose ?? new Pose(at.X, at.Y, standing?.Heading ?? 0.0);

    internal bool IsRouted => !way.IsEmpty;
    /// <summary>What the route asks of the body NOW, in the robot's own words, one base action at a time — or, once it is
    /// no longer pending, how it ended (completed, failed, abandoned): 'stop' (the operator holds the golem), 'back'
    /// (reverse, the correction a touch inserted), 'turnLeft' / 'turnRight' (turn in place, the shorter way round, to face
    /// the point it heads to) or 'advance' (move forward to that point). How much is <see cref="Amount"/>. The golem prints
    /// both after every act that changes them; the body does that one thing and reports it. Never 'decide' (18-sep-2026): a
    /// pending route always has a way ahead — born with it, decided again by itself — or its invariant is broken.</summary>
    internal string Order
    {
        get
        {
            if (!IsPending()) return Status;
            if (Paused) return "stop";
            if (!IsRouted || nextLeg >= way.Count) throw new GolemDomainException($"route {Id} is pending with no way ahead: a route is born with its way and decides it again by itself");
            if (NextLeg.IsReverse) return "back";
            if (!turned && Math.Abs(TurnAhead) > TurnTolerance) return TurnAhead > 0 ? "turnLeft" : "turnRight";
            return "advance";
        }
    }
    /// <summary>How much of the order: metres to advance or back, radians to turn (always positive: the order says which
    /// way), zero when nothing is asked of the body's motors. A follower's last stop is met a standoff short of the
    /// leader's spot.</summary>
    internal double Amount => !IsWalkable ? 0.0
        : Order switch
        {
            "advance" => Math.Max(0.0, standing.DistanceTo(Target) - (Following && NextLeg.IsStop && StopsLeft == 1 ? FollowerStandoff : 0.0)),
            "back" => standing.DistanceTo(Target),
            "turnLeft" or "turnRight" => Math.Abs(TurnAhead),
            _ => 0.0
        };
    /// <summary>Where the body heads to NOW and the heading it must face to get there: the next leg's point — or, for a
    /// door, first its approach (lined up in front of it) and then its exit (straight through) — as a pose from where the
    /// body stands. A retreat keeps the heading the body faces: it is walked in reverse.</summary>
    internal Pose Target
    {
        get
        {
            var leg = NextLeg;
            var point = leg.IsReverse ? leg.At : linedUp ? leg.Exit : leg.Approach;
            double heading = standing == null ? leg.Heading : leg.IsReverse ? standing.Heading : standing.HeadingTo(point);
            return new Pose(point.X, point.Y, heading);
        }
    }
    /// <summary>Where the body stands and faces, as its last act reported it; null before the way was decided.</summary>
    internal Pose Standing => standing;
    /// <summary>Where this route's way ends and facing which way: its last stop, in the last leg's heading — where the next
    /// errand starts when this one is still underway.</summary>
    internal Pose PlannedEnd
    {
        get
        {
            var last = stops[^1];
            if (!IsRouted) return new Pose(last.X, last.Y, standing?.Heading ?? 0.0);
            var leg = way.Last;
            return new Pose(leg.At.X, leg.At.Y, leg.Heading);
        }
    }
    // The turn, signed, from where the body faces to where it must face (positive: to the left, counter-clockwise).
    private double TurnAhead => standing == null ? 0.0 : Normalize(Target.Heading - standing.Heading);
    /// <summary>Whether the body may act on the next leg now — back, turn or advance: a leg ahead, not held.</summary>
    internal bool IsWalkable => IsPending() && IsRouted && nextLeg < way.Count && !Paused;
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

    // ---- the walk: the body reports each thing done, WHERE IT STANDS NOW, and the route moves its cursor and asks the next ----

    /// <summary>The body did the one thing the route asked of its motors and says where it stands: the route itself knows
    /// whether that was the turn (<see cref="Turn"/>) or the move (<see cref="Reach"/>) — the one act the robot's report
    /// writes (17-sep-2026: the host relays, the domain decides). Refused when nothing was asked of the motors.</summary>
    internal Route Arrive(Pose me)
    {
        if (me == null) throw new GolemDomainException("Route.Arrive: 'me' was not given");
        MustBePending();
        if (Order == "turnLeft" || Order == "turnRight") return Turn(me);
        if (Order == "advance" || Order == "back") return Reach(me);
        throw new GolemDomainException($"route {Id} asked nothing of the body's motors now: it asks '{Order}'");
    }

    /// <summary>The body turned in place as asked and says where it stands, facing which way. If it now faces its point — the
    /// turn left is within two tolerances — the route asks it to advance (from there: the amount is measured from where it really
    /// is); if not (22-sep-2026 live: a turn measured from a stale pose left the body facing a wall), the route asks another turn,
    /// measured from the real pose — up to TurnsAtMost on one leg, then it takes the heading the body reached.</summary>
    internal Route Turn(Pose me)
    {
        if (me == null) throw new GolemDomainException("Route.Turn: 'me' was not given");
        MustBePending();
        if (Order != "turnLeft" && Order != "turnRight") throw new GolemDomainException($"route {Id} asked no turn now: it asks '{Order}'");
        standing = me;
        stood(me);
        turnsOnLeg++;
        turned = turnsOnLeg >= TurnsAtMost || Math.Abs(TurnAhead) <= 2 * TurnTolerance;
        return this;
    }

    /// <summary>The body moved as asked — forward, or in reverse — and says where it stands now. The route moves its cursor
    /// one thing on (Juan, 16-sep-2026: "una cosa a la vez"): a door's approach reached means the door is lined up and the
    /// next advance goes straight through it to its exit; any other point reached puts its leg behind. Reaching a stop
    /// counts it; reaching the last stop completes the route.</summary>
    internal Route Reach(Pose me)
    {
        if (me == null) throw new GolemDomainException("Route.Reach: 'me' was not given");
        MustBePending();
        if (Order != "advance" && Order != "back") throw new GolemDomainException($"route {Id} asked no move now: it asks '{Order}'");
        var leg = NextLeg;
        standing = me;
        stood(me);
        turned = false;
        turnsOnLeg = 0;
        if (!leg.IsReverse && !linedUp && !Same(leg.Approach, leg.Exit)) { linedUp = true; return this; }   // lined up in front of the door: now through it
        linedUp = false;
        nextLeg++;
        grazesOnLeg = 0;
        if (leg.IsStop)
        {
            reached++;
            if (reached == stops.Count && Following) PullOver(me);
        }
        if (reached == stops.Count && nextLeg >= way.Count) End(RouteStatus.Completed);
        else if (nextLeg >= way.Count) PlanAgainFrom(me);   // the way ran out short of a stop (a retreat with no road from it): decided again from here
        return this;
    }

    // Stranded — the touch left only the retreat, no road fit from the point it was decided at — or awake with the body
    // carried elsewhere: the route decides its way again from where the body actually stands (18-sep-2026: the domain, not a
    // 'decide' round trip through the host). No road from here: the route ends.
    private void PlanAgainFrom(Pose me)
    {
        try { Plan(me); }
        catch (GolemDomainException) { End(RouteStatus.Failed); }
    }

    // A follower that reached its last stop — the leader's spot — pulls over before it is done: a courtesy step to one
    // side, off the leader's way, as one more leg of its own (the route completes once it is walked). Nowhere to step: done.
    private void PullOver(Pose me)
    {
        var aside = Courtesy.StepOutOfTheWayOf(layout, collisions, radius, null, me);
        if (aside == null) return;
        var legs = way.Legs().ToList();
        legs.Add(new Leg(aside, Leg.Courtesy));
        way = new Trajectory(legs);
    }

    /// <summary>Whether a stop of this route lies ahead at a point — what to consult before saying it was reached.</summary>
    internal bool IsStopAhead(Position at)
    {
        if (at == null) throw new GolemDomainException("Route.IsStopAhead: 'at' was not given");
        return IsPending() && StopsAhead.Any(s => Same(s, at));
    }

    /// <summary>The body touched SOMETHING — where and heading which way (the touch), and where the body stood facing which
    /// way (me) — and the route concludes what it was over its own facts (17-sep-2026: the host writes the touch, the domain
    /// decides): a wall the map knows — a <see cref="Graze"/>, its own execution error; anything else — a thing nobody
    /// charted: a <see cref="Bump"/>. FOR NOW EVERYTHING ELSE IS A BUMP (Juan, 18-sep-2026: "todo se considera un bump de
    /// momento"): whether the thing was another body is not concluded here yet — that protocol comes back later.</summary>
    internal Route Touched(Pose touch, Pose me)
    {
        if (touch == null) throw new GolemDomainException("Route.Touched: 'touch' was not given");
        if (me == null) throw new GolemDomainException("Route.Touched: 'me' was not given");
        if (ReferenceEquals(touch, me)) throw new GolemDomainException("Route.Touched: 'touch' and 'me' are the same pose");
        MustBePending();
        if (layout.IsWallAt(touch, MapLayout.WallTolerance)) return Graze(touch, me);
        return Bump(touch, me);
    }

    /// <summary>The body touched something the map does not hold — where and heading which way (the touch), and where the
    /// body stood facing which way (me) — on this route. The golem presumes a THING there and marks it at once (a peer
    /// that says it was there takes it back — Met; told to the peers, exposed beside the act). And the route CORRECTS
    /// ITS WAY INSIDE (Juan, 16-sep-2026: "cuando choca queriendo llegar de A a B mete entre A y B otros puntos: ir para
    /// atrás un poco y pasar al lado… posiciones de corrección"): first a leg to back off — in reverse, the body's own
    /// retreat behind where it stood — then the planner's road from there through the stops ahead, skirting the figure
    /// the mark now outlines. When no way fits from there, the retreat alone stays and, once the body reached it, the route
    /// decides its way again from there by itself (PlanAgainFrom).</summary>
    internal Route Bump(Pose touch, Pose me)
    {
        MustBePending();
        if (touch == null) throw new GolemDomainException($"route {Id}'s bump needs the pose of the touch");
        if (me == null) throw new GolemDomainException($"route {Id}'s bump needs where the body stood");
        if (ReferenceEquals(touch, me)) throw new GolemDomainException("Route.Bump: 'touch' and 'me' are the same pose");
        bumps++;
        bumpsSinceRoute++;
        collisions.Mark(touch);
        lastTouch = touch;
        Correct(me, replan: true);
        if (bumps > PatienceWithThings) End(RouteStatus.Failed);   // the golem's patience with things is spent
        return this;
    }

    /// <summary>The last bump on this route was no thing — a peer's word landed on my body (22-sep-2026): it counts no more
    /// against the patience with things, and the way is decided again WITHOUT the mark but NOT straight back into the body just
    /// met (ajuste 47): the retreat, if not yet walked, is kept, then the courtesy step to the body's own right, then the road
    /// from there; if the retreat was already walked, the step and the road from where the body stands. No road: the retreat
    /// alone stays (PlanAgainFrom decides once it is reached), or the route fails by itself.</summary>
    internal Route Unbump()
    {
        MustBePending();
        if (bumps == 0) throw new GolemDomainException($"route {Id} has no bump to annul");
        bumps--;
        if (bumpsSinceRoute > 0) bumpsSinceRoute--;
        if (IsRouted && nextLeg < way.Count && NextLeg.IsReverse)
        {
            var back = NextLeg.At;
            var legs = new List<Leg> { new(back, Leg.Retreat) };
            try { legs.AddRange(PastFrom(back, standing.Heading, lastTouch)); }
            catch (GolemDomainException) { Strand(legs, standing); return this; }   // stranded still: the retreat alone, decided again once reached
            Take(new Trajectory(legs), standing);
            return this;
        }
        try { Take(new Trajectory(PastFrom(standing, standing.Heading, lastTouch)), standing); }
        catch (GolemDomainException) { End(RouteStatus.Failed); }
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
        if (grazesOnLeg >= PatienceWithWalls) End(RouteStatus.Failed);   // patience spent: the route ends by itself
        return this;
    }

    // Stranded: no road fits from the retreat. The retreat alone is the way for now — walked from where the body stands — and
    // the route decides again once it is reached (Reach → PlanAgainFrom).
    private void Strand(List<Leg> retreat, Pose me)
    {
        way = new Trajectory(retreat).WalkedFrom(me);
        standing = me;
        nextLeg = 0;
        turned = false;
        turnsOnLeg = 0;
        linedUp = false;
    }

    // The corrections a touch inserts ahead of what was left: back off first (a leg walked in reverse, the body's retreat
    // behind where it stood), then — replanning — the road from there through the stops ahead, or — not replanning — the
    // legs that were left, walked again from the retreat point. If no road fits from there, the retreat alone stays and
    // the route decides again from it once reached (Reach → PlanAgainFrom).
    private void Correct(Pose me, bool replan)
    {
        var back = RetreatFrom(me);
        var legs = new List<Leg> { new(back, Leg.Retreat) };
        if (replan)
        {
            try { legs.AddRange(PastFrom(back, me.Heading, lastTouch)); }
            catch (GolemDomainException) { Strand(legs, me); return; }   // stranded: back off, then decide again
        }
        else
            foreach (var left in way.Legs().Skip(nextLeg))
                legs.Add(new Leg(left.At, left.Name, left.Approach, left.Exit));   // its heading is given again, from the retreat point
        int patience = grazesOnLeg;
        Take(new Trajectory(legs), me);
        if (!replan) grazesOnLeg = patience;   // the same leg, tried again: the patience spent on it stays spent
    }

    // The way past what was bumped, from a point the body backed off to, facing as it faced: THE COURTESY STEP TO THE BODY'S OWN
    // RIGHT first (Juan, 22-sep-2026: "cada choque debería intentar siempre el tramo de la derecha") — two radii to the side,
    // where the body fits; to the left when the right does not fit; none when neither does — then the planner's road from there
    // through the stops ahead. So a thing is skirted by the right first, and two bodies meeting head-on step each to its own
    // right and pass. Throws when no road fits from there (the caller decides what stays).
    private List<Leg> PastFrom(Position from, double heading, Position touched)
    {
        var legs = new List<Leg>();
        var aside = Courtesy.StepOutOfTheWayOf(layout, collisions, radius, null, new Pose(from.X, from.Y, heading), PassingStep);
        if (aside != null)
        {
            legs.Add(new Leg(aside, Leg.Courtesy));
            from = aside;
            // then AHEAD, parallel to the way it came, until what was touched is a body's length behind - so the run on to the
            // stop does not converge back onto it (22-sep-2026 live: the straight run from the step grazed the peer again)
            double dx = Math.Cos(heading), dy = Math.Sin(heading);
            double along = (touched.X - aside.X) * dx + (touched.Y - aside.Y) * dy;
            var ahead = aside.Along(heading, Math.Max(0.0, along) + 3 * radius + Collisions.MarkMargin);   // the touch is on its shell: its centre one radius beyond, then a body's length
            if (layout.HasRoom(ahead, radius) && !collisions.Blocks(ahead, radius)) { legs.Add(new Leg(ahead, Leg.Waypoint)); from = ahead; }
        }
        legs.AddRange(Planner().Road(from, Ordered(from)).Legs());
        return legs;
    }

    /// <summary>How far to the side a body steps to pass what it bumped into: three radii - two would leave the centres exactly a
    /// body apart when the other does not move.</summary>
    internal double PassingStep => 3 * radius;

    /// <summary>How far the retreat may grow, in retreats, when the body's own retreat leaves it no room to turn.</summary>
    internal const double RetreatAtMost = 3;

    // Where the body backs off to: straight back along the reverse of its heading, the body's own retreat at least, and
    // FURTHER — up to RetreatAtMost retreats, a half radius at a time — until it stands with room to turn in place there:
    // clear of the walls, and clear of every figure by a whole extra radius, so the shell sweeping round touches nothing
    // (Juan, 17-sep-2026: "retroceder más y girar alejándose de la marca, para rodearla si aún cabe su cuerpo o ir por
    // otra ruta si ya no cabe" — the 16-sep run touched the crate's corner again while turning after a plain retreat). Where
    // nothing behind is clear, the plain retreat: the road from there will say whether a way fits.
    private Position RetreatFrom(Pose me)
    {
        for (double d = retreat; d <= retreat * RetreatAtMost + 1e-9; d += radius / 2)
        {
            var back = me.Along(me.Heading + Math.PI, d);
            if (!layout.HasRoom(back, radius)) break;                   // a wall behind: no further
            if (!collisions.Blocks(back, radius * 2)) return back;      // room to turn: an extra radius clear of every figure
        }
        return me.Along(me.Heading + Math.PI, retreat);
    }

    // ---- the hold (Juan, 14-sep-2026: "pausa/continuar el trayecto actual en ejecución") ----

    /// <summary>The operator holds the route: the body stops where it stands — the pose the golem believes, kept — and
    /// waits. Only a pending route can be held, and only once.</summary>
    internal Route Pause(Pose me)
    {
        if (me == null) throw new GolemDomainException($"route {Id}'s pause needs where the body stands");
        MustBePending();
        if (Paused) throw new GolemDomainException($"route {Id} is already paused");
        Paused = true;
        HeldAt = me;
        standing = me;
        return this;
    }

    /// <summary>The operator lets the route go on: the body takes up the next leg from where it stands NOW (it may have
    /// been pushed while standing) — that leg's heading is given again from there, and the turn is asked again.</summary>
    internal Route Resume(Pose me)
    {
        if (me == null) throw new GolemDomainException($"route {Id}'s resume needs where the body stands");
        MustBePending();
        if (!Paused) throw new GolemDomainException($"route {Id} is not paused");
        Paused = false;
        standing = me;
        if (IsRouted && nextLeg < way.Count)
        {
            var legs = way.Legs().ToList();
            legs[nextLeg] = legs[nextLeg].WalkedFrom(me);
            way = new Trajectory(legs);
            turned = false;
            turnsOnLeg = 0;
        }
        return this;
    }

    // ---- the ending ----

    /// <summary>The world said no — a collision, a stall, no way. The reason is the act's, kept by the journal.</summary>
    internal Route Fail(string why)
    {
        MustBePending();
        if (string.IsNullOrWhiteSpace(why)) throw new GolemDomainException($"failing route {Id} needs a reason");
        End(RouteStatus.Failed);
        return this;
    }

    /// <summary>The golem lets the route go: a newer told point made it pointless, or the operator let go of everything.</summary>
    internal Route Abandon(string why)
    {
        MustBePending();
        if (string.IsNullOrWhiteSpace(why)) throw new GolemDomainException($"abandoning route {Id} needs a reason");
        End(RouteStatus.Abandoned);
        return this;
    }

    /// <summary>The golem puts on record that it announces a reached stop to its peer (the tell follows in the same entry).</summary>
    internal Route Announce()
    {
        if (reached == 0) throw new GolemDomainException($"route {Id} has reached no stop yet: only a reached stop is announced");
        Announced = true;
        return this;
    }

    // The route ends, one way or another: the peers met on it have moved on — bodies do — and are planned around no more
    // (Juan, 22-sep-2026: "tenerlos presentes al momento de la ruta nada más").
    private void End(RouteStatus ending)
    {
        status = ending;
        collisions.PeersMovedOn();
    }

    private void MustBePending()
    {
        if (!IsPending())
            throw new GolemDomainException($"route {Id} is already {status.Name}");
    }

    private static bool Same(Position a, Position b) => Math.Abs(a.X - b.X) < 1e-6 && Math.Abs(a.Y - b.Y) < 1e-6;

    private static double Normalize(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    private static string Fmt(double d) => d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
