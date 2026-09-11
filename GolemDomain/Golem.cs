using System.Globalization;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Maps;
using GolemDomain.Robots;
using GolemDomain.Routes;
using GolemDomain.Touches;

namespace GolemDomain;

/// <summary>
/// The golem: the robot's mind — the executor that is entrusted missions, carries them out over its layout
/// with its body, and keeps how each one ended. The subject of its journal (the DSL's
/// <c>g = Golem(body, map, collisions)</c>): it RECEIVES its modules, it does not build them — the
/// <see cref="Body"/> it drives, the <see cref="MapLayout"/> (the concrete <see cref="Map"/> it was told, with
/// its positions), the <see cref="Collisions"/> its bodies learned by touching. Each module is a global of the actor in its own
/// right, so a query can calculate with one of them alone. Refuses a repeated handle. Its name is the journal's
/// identity and its estimated position is telemetry: both come from the host, as the actor's name and as query
/// parameters, never as state here.
/// </summary>
internal sealed class Golem
{
    private readonly List<Mission> missions = new();
    private readonly Body body;
    private readonly MapLayout layout;
    private readonly Collisions collisions;
    private int idleBumps;       // times something touched the body while it stood without a mission
    private int lastHandle;      // a handle names one mission forever — even after letting go (idempotency keys hang on it)

    internal Golem(Body body, MapLayout map, Collisions collisions)
    {
        this.body = body ?? throw new DomainException("a golem needs a body to drive");
        layout = map ?? throw new DomainException("a golem needs its map, laid out");
        this.collisions = collisions ?? throw new DomainException("a golem needs its collisions module, even empty");
        if (collisions.Layout != layout) throw new DomainException("the collisions must be measured over the golem's own layout");
    }

    // ---- the body ----

    internal double Radius() => body.Radius.InMeters;
    internal double Speed() => body.Speed.InMetersPerSecond;
    internal double LingerAfterTold() => body.LingerAfterTold.InSeconds;

    // ---- the map and its layout, read through the golem ----

    internal int PlaceCount() => layout.ZoneCount;
    internal int PassageCount() => layout.PassageCount;
    internal bool KnowsPlace(string name) => layout.Knows(name);
    internal bool IsOnMap(double x, double y) => layout.IsOnMap(new Position(x, y));
    /// <summary>The zone a point stands in — an object; <c>.Name</c> for its name.</summary>
    internal Zone PlaceAt(double x, double y) => layout.ZoneAt(new Position(x, y));

    /// <summary>The layout as objects, for whoever draws it: every zone, each knowing its corners, its walls (with
    /// their doors), its doorways and its open sides —
    /// <c>foreach (places in g.Places()) { print places.Name 'name', places.Center.X 'cx'; foreach (doors in places.Doorways()) { print doors.To 'to'; } }</c>.
    /// The same objects the global <c>map</c> hands out: the golem is one way to reach them, not the only one.</summary>
    internal IReadOnlyList<Zone> Places() => layout.Zones.ToList();

    /// <summary>Whether a point the body touched lies on a wall the golem KNOWS: a wall of a zone, within a tolerance
    /// that absorbs the wall's thickness and the pose's error, outside its doorways. Touching a known wall is the
    /// golem's own execution error; touching anything else is reality holding something the map does not.</summary>
    internal bool KnowsWallAt(double x, double y) => layout.IsWallAt(new Position(x, y), MapLayout.WallTolerance);

    /// <summary>The shortest road between two areas, centre to centre, through the passages, for this body —
    /// <c>g.Distance(map.Find('kitchen'), map.Find('garage'))</c>.</summary>
    internal double Distance(Area from, Area to) => Planner().RoadLength(layout.Of(from).Center, layout.Of(to).Center);

    /// <summary>Whether this body stands clear at a point: on the map, off the walls and off every mark.</summary>
    internal bool FitsAt(double x, double y)
    {
        var at = new Position(x, y);
        return layout.HasRoom(at, Radius()) && !collisions.Blocks(at, Radius());
    }

    /// <summary>Whether the walls alone leave room for this body at a point — what it asks while feeling around a mark.</summary>
    internal bool HasRoomAt(double x, double y) => layout.HasRoom(new Position(x, y), Radius());

    // ---- the collisions, read through the golem ----

    internal int MarkCount() => collisions.MarkCount;
    internal int ObstacleCount() => collisions.All().Count;
    internal IReadOnlyList<Obstacle> Obstacles() => collisions.All();
    internal int ThingCount() => collisions.Things().Count;
    internal int MetCount() => collisions.EncounterCount;

    // ---- missions: the entrusting (the operator's voice) ----

    /// <summary>The operator sends the golem to a point: a new errand when the handle is new, one more stop of the
    /// same errand — in this order — when the handle is the errand's. A stop off the map is refused.</summary>
    internal int Visit(int id, Position stop) => Entrust(id, stop, following: false, choosesOrder: false);

    /// <summary>The operator sends the golem to an area: its centre — <c>g.Visit(@id, map.Find(@area))</c>.</summary>
    internal int Visit(int id, Area area) => Entrust(id, layout.Of(area).Center, following: false, choosesOrder: false);

    /// <summary>The operator adds a stop to an errand whose order the golem may choose, so the whole road is shortest.</summary>
    internal int Cover(int id, Position stop) => Entrust(id, stop, following: false, choosesOrder: true);

    /// <summary>The operator adds an area to an errand whose order the golem may choose.</summary>
    internal int Cover(int id, Area area) => Entrust(id, layout.Of(area).Center, following: false, choosesOrder: true);

    /// <summary>The golem follows its leader to a point a peer says it reached — a mission with a handle of its own.
    /// (Leader–follower formation by told waypoints, not by sensing the leader: the follower knows where the leader
    /// WAS, which is why a newer told point supersedes an older one and the host keeps a standoff on arrival.)</summary>
    internal int Follow(Position at) => Entrust(NextHandle(), at, following: true, choosesOrder: false);

    // ---- missions: the road ----

    /// <summary>The road the golem would walk from (x, y) through a mission's stops still ahead, as objects: the legs,
    /// each knowing its kind (door, opening, around, aside, stop), its passage's areas and its point — what the host
    /// reads to write the decision act by act. For a Cover mission the stops come out in the order the golem chose.
    /// Marks are skirted ('around' legs) or, where the body would not fit past them, avoided by another road.</summary>
    internal Trajectory Road(int id, double x, double y)
    {
        var mission = Find(id);
        var from = new Position(x, y);
        var ahead = mission.StopsAhead.ToList();
        var planner = Planner();
        var stops = mission.ChoosesOrder ? planner.BestOrder(from, ahead) : ahead;
        return planner.Road(from, stops);
    }

    /// <summary>The same road, in one line of text — for a human or a test to read at a glance:
    /// "kitchen/north@4,9.5 > kitchen@2,9.5 > north/storage@7,9.5 > storage@9,9.5".</summary>
    internal string Plan(int id, double x, double y) => Road(id, x, y).AsPlan();

    /// <summary>The road out of a peer's way and on to the stops still ahead, as objects: the first leg is the
    /// courtesy step — a body's width to ONE SIDE of where the golem faces, chosen so it moves away from the peer
    /// and where its own body fits; both bodies step to their own right when they can, which is how two of them
    /// pass instead of shove. The peer's position is what it told when it bumped (HearBump); without it, or with
    /// nowhere to step, the road is the plain one from here.</summary>
    internal Trajectory RoadPast(int id, string who, double x, double y, double heading)
    {
        var mission = Find(id);
        var me = new Pose(x, y, heading);
        var ahead = mission.StopsAhead.ToList();
        var planner = Planner();
        var aside = StepOutOfTheWayOf(who, me);
        if (aside == null) return planner.Road(me, ahead);
        var legs = new List<Leg> { new(aside, Leg.Courtesy) };
        legs.AddRange(planner.Road(aside, ahead).Legs());
        return new Trajectory(legs);
    }

    /// <summary>The road past a peer, in one line of text.</summary>
    internal string PlanPast(int id, string who, double x, double y, double heading) => RoadPast(id, who, x, y, heading).AsPlan();

    // A body's width to one side of where the golem faces: its own right first (so two bodies facing each other
    // separate), then its left. The step must fit the body and must not walk INTO the peer.
    private Position StepOutOfTheWayOf(string who, Pose me)
    {
        var peer = collisions.LastKnownPositionOf(who);
        foreach (var side in new[] { Side.Right, Side.Left })
        {
            var step = new StepAside(side).From(me, me.Heading).Legs()[0].At;
            if (!FitsAt(step.X, step.Y)) continue;
            if (peer != null && step.DistanceTo(peer) <= me.DistanceTo(peer)) continue;
            return step;
        }
        return null;
    }

    /// <summary>The golem starts deciding a mission's road and returns it, empty: the acts on the road (Via, Around,
    /// Aside, Stop) are its legs, in order, the last stop last — all in one journal entry, with the errand itself when
    /// it is new: <c>route = g.Route(@id); route.Via(door1, at1); … route.Stop(stop3);</c>. The last stop decides it and
    /// the mission takes it. A new road replaces what was left of the old one: after a bump, or when the golem wakes
    /// with a plan underway.</summary>
    internal Trajectory Route(int id)
    {
        var mission = Find(id);
        if (!mission.MayRoute) throw new DomainException($"mission {id} is not pending: no road to decide");
        return new Trajectory(layout, mission);
    }

    /// <summary>The road a NEW errand would take, before it exists: from a point, through stops given as two arrays
    /// (x and y, in order — or in the order the golem chooses, for a Cover), for this body over this map and what it
    /// learned. What the operator's command reads to write the errand and its whole plan in one entry.</summary>
    internal Trajectory Preview(double fromX, double fromY, double[] xs, double[] ys, bool choosesOrder)
    {
        if (xs == null || ys == null || xs.Length == 0 || xs.Length != ys.Length) throw new DomainException("a preview needs its stops as two arrays of the same length");
        var from = new Position(fromX, fromY);
        var stops = xs.Select((x, i) => new Position(x, ys[i])).ToList();
        foreach (var stop in stops)
            if (!layout.IsOnMap(stop)) throw new DomainException($"the point ({Fmt(stop.X)}, {Fmt(stop.Y)}) is nowhere on the map");
        var planner = Planner();
        return planner.Road(from, choosesOrder ? planner.BestOrder(from, stops) : stops);
    }

    /// <summary>The plan ahead of a mission: every leg not yet known to be walked, each with how it is walked (line up
    /// at the approach, end at the exit). What the host takes to walk the plan in its memory, leg by leg, journaling
    /// nothing but what changes the plan or fulfils it.</summary>
    internal Trajectory RoadAhead(int id) => new(Find(id).LegsAhead);

    /// <summary>Whether a stop of the mission lies ahead at (x, y) — what to consult before saying it was reached.</summary>
    internal bool IsStopAhead(int id, double x, double y) => Find(id).IsStopAhead(x, y);

    /// <summary>Where the last pending mission ends: the point a NEW errand's plan should start from when the golem is
    /// busy — it will stand there when the new errand comes up. Consult HasPendingMission first.</summary>
    internal double PlannedEndX() => LastPending().StopsAhead.Last().X;
    internal double PlannedEndY() => LastPending().StopsAhead.Last().Y;

    /// <summary>The evasion maneuver a strategy ('back-off', 'step-right', 'step-left') plans from where the body
    /// stands and the heading it had when it touched something: a trajectory to walk with foreach. A read: the
    /// host executes it and reports what the body met.</summary>
    internal Maneuver Evasion(double x, double y, double heading, string strategy) =>
        EvasionStrategy.Named(strategy).From(new Position(x, y), heading);

    // ---- touches: the facts take their objects (a Pose, a Position); what a reaction must tell the peers is exposed
    //      beside the act as @params, because the matcher captures no object (Fase 0, P3). ONE row per touch (Juan,
    //      10-sep): the bump presumes it touched a THING and marks it at once; if a peer says it bumped there and
    //      then, Met takes the mark back — and the peers, told, take back what they learned (LearnMet). ----

    /// <summary>The body touched something the map does not hold — where, and heading which way — on this mission. A fact,
    /// told to the peers, and a mark at once: the golem presumes a thing until a peer says it was there too (Met takes
    /// the mark back). The plan is interrupted. Returns the mission id.</summary>
    internal int Bump(int id, Pose touch)
    {
        if (touch == null) throw new DomainException($"mission {id}'s bump needs the pose of the touch");
        Find(id).Bump();
        collisions.Mark(touch);
        return id;
    }

    /// <summary>Something touched the body while it stood without a mission — a body, since things do not move; told
    /// to the peers so the one that moved knows it met a body. No mark. Returns how many such touches so far.</summary>
    internal int Bump(Pose touch)
    {
        if (touch == null) throw new DomainException("a bump needs the pose of the touch");
        return ++idleBumps;
    }

    /// <summary>The body grazed a wall the map KNOWS, on this mission: its own execution error, no discovery. Counted
    /// against the golem's patience (MayRetryLeg). Returns the mission id.</summary>
    internal int Graze(int id, Position at)
    {
        if (at == null) throw new DomainException($"mission {id}'s graze needs where it happened");
        Find(id).Graze();
        return id;
    }

    /// <summary>A moving peer says it bumped — where, heading which way — while it stood at another point: heard and kept,
    /// so a touch of my own there and then is known to be that peer (and I know where it is, to step out of its way); and
    /// learned as a mark, as the peer itself presumed — until it says it met a body (LearnMet). Returns how many bumps heard.</summary>
    internal int HearBump(string who, Pose touch, Position peerAt)
    {
        int heard = collisions.Hear(who, touch, peerAt);
        collisions.Mark(touch);
        return heard;
    }

    /// <summary>A standing peer says something touched it — where, while it stood at another point: heard and kept, so a
    /// touch of my own there and then is known to be that peer. No mark: what touches a standing body is a body.</summary>
    internal int HearTouch(string who, Position at, Position peerAt) => collisions.Hear(who, at, peerAt);

    /// <summary>A peer tells of a mark it concluded on its own: the golem learns it without the bruise. Returns how many marks it holds.</summary>
    internal int LearnMark(Pose touch) => collisions.Mark(touch);

    /// <summary>The golem concludes what it touched was a peer — who said it bumped there and then: the mark its bump
    /// presumed is taken back, and the encounter is kept among the obstacles as a Peer, history, nothing to plan around.
    /// Told to the peers, who take back what they learned. Returns how many bodies it has met.</summary>
    internal int Met(string who, Position at)
    {
        int met = collisions.Meet(who, at);
        collisions.Unmark(at);                    // what I presumed a thing was that body
        collisions.UnmarkHeardFrom(who, at);      // and what it presumed there was mine: the encounter was mutual
        return met;
    }

    /// <summary>A peer says its touch there was a body: the golem takes back the mark it learned from that bump. Returns how many marks went.</summary>
    internal int LearnMet(Position at) => collisions.Unmark(at);

    /// <summary>The operator says what stood at a point is gone — somebody took it away — and the golem forgets the
    /// obstacle there with EVERY mark that outlined it: a body may pass again, and a touch after this is a NEW
    /// obstacle. Told to the peers, who forget it too. Returns how many facts it dropped.</summary>
    internal int Forget(Position at) => collisions.Forget(at ?? throw new DomainException("forgetting needs where"));

    /// <summary>A peer says what stood at a point is gone: the golem forgets it too, without having gone to see.</summary>
    internal int LearnForget(Position at) => collisions.Forget(at ?? throw new DomainException("forgetting needs where"));

    /// <summary>Whether the golem holds an obstacle at (x, y) — what to consult before saying it is gone.</summary>
    internal bool KnowsObstacleAt(double x, double y) => collisions.KnowsAt(new Position(x, y));

    /// <summary>What the golem suspects its body touched at (x, y), heading that way, given what it has heard since
    /// the given count: a wall it knows (Kind 'wall': conclude Graze), a peer that bumped near there and then
    /// (Kind 'peer', Who: conclude Met), or a thing nobody charted (Kind 'thing': conclude Mark). The domain reasons;
    /// the host waits for the peers to speak, asks, and writes the conclusion the suspicion names.</summary>
    internal Suspicion Suspect(double x, double y, double heading, int sinceCount) =>
        collisions.Suspect(new Pose(x, y, heading), sinceCount);

    /// <summary>How many bumps peers have told about so far — the count a leg starts from, so older news is not taken for this touch.</summary>
    internal int HeardBumpCount() => collisions.HeardCount;

    /// <summary>Who, among the bumps heard after the given count, bumped near (x, y) — within a meeting's reach; "" for nobody.</summary>
    internal string HeardBumpNear(double x, double y, int sinceCount) => collisions.HeardNear(new Position(x, y), sinceCount);

    // ---- missions: the progress — only what fulfils the plan is journaled ----

    /// <summary>The golem reached a stop of its road: the legs before it were walked, whatever they were; reaching the
    /// last one completes the mission. Told to the follower (exposed beside the act). Returns the mission id.</summary>
    internal int Reach(int id, Position at)
    {
        if (at == null) throw new DomainException($"mission {id} reaches a point");
        Find(id).Reach(at.X, at.Y);
        return id;
    }

    // ---- missions: the ending ----

    /// <summary>The world said no — a collision, a stall, no road — and the reason is kept.</summary>
    internal int Fail(int id, string reason)
    {
        Find(id).Fail(reason);
        return id;
    }

    /// <summary>The golem lets a mission go, and why: a newer told point made it pointless, or the operator let go of everything.</summary>
    internal int Abandon(int id, string reason)
    {
        Find(id).Abandon(reason);
        return id;
    }

    /// <summary>The golem puts on record that it announces a reached stop to its peer (the tell follows in the same entry).</summary>
    internal int Announce(int id)
    {
        Find(id).Announce();
        return id;
    }

    // ---- reads (total: never throw when nothing is there) ----

    internal int NextHandle() => lastHandle + 1;
    internal bool Knows(int id) => missions.Any(m => m.Id == id);
    internal bool IsPending(int id) => Find(id).IsPending();
    internal bool IsFollowing(int id) => Find(id).Following;
    internal bool WasAnnounced(int id) => Find(id).Announced;
    internal bool IsRouted(int id) => Find(id).IsRouted;
    internal int LegsLeft(int id) => Find(id).LegsLeft;
    internal int StopsLeft(int id) => Find(id).StopsLeft;
    internal int Bumps(int id) => Find(id).Bumps;
    /// <summary>The body bumped since the road was last decided: the road may be decided again.</summary>
    internal bool HasBumpedSinceRoute(int id) => Find(id).BumpedSinceRoute;
    internal int Grazes(int id) => Find(id).Grazes;
    /// <summary>Whether the golem still retries the leg after grazing a known wall — its patience on this leg is not spent.</summary>
    internal bool MayRetryLeg(int id) => Find(id).MayRetryLeg;
    /// <summary>What the mission heads to first: a passage's name, around/aside for a point, or the zone of the stop.</summary>
    internal string HeadingTo(int id) => Find(id).NextLeg.Name;
    /// <summary>The kind of the leg ahead: door, opening, around, aside or stop.</summary>
    internal string HeadingKind(int id) => Find(id).NextLeg.Kind;
    internal bool HeadsToAStop(int id) => Find(id).NextLeg.IsStop;
    internal bool HasPendingMission() => missions.Any(m => m.IsPending());
    internal int Pending() => missions.Count(m => m.IsPending());
    internal int[] PendingIds() => missions.Where(m => m.IsPending()).Select(m => m.Id).ToArray();
    internal int FollowingCount() => missions.Count(m => m.IsPending() && m.Following);
    internal int Total() => missions.Count;
    internal string StatusOf(int id) => Find(id).ReadStatus();
    /// <summary>The newest pending point a peer told about — where the leader is now, as far as the follower knows. Consult HasNewerFollowing first.</summary>
    internal int NewestFollowingId() => missions.Where(m => m.IsPending() && m.Following).Select(m => m.Id).DefaultIfEmpty(0).Max();
    /// <summary>Whether a FOLLOWED mission has been overtaken by a newer followed one; an operator's mission never is.</summary>
    internal bool HasNewerFollowing(int id) => Find(id).Following && missions.Any(m => m.IsPending() && m.Following && m.Id > id);

    // ---- the road ahead, answered from the golem's own knowledge ----

    /// <summary>The road through every stop still ahead, in the order they will run, through the passages. Zero with one stop or none.</summary>
    internal double RouteLength()
    {
        var planner = Planner();
        double road = 0;
        Position previous = null;
        foreach (Mission m in missions)
        {
            if (!m.IsPending()) continue;
            foreach (var stop in m.StopsAhead)
            {
                if (previous != null) road += planner.RoadLength(previous, stop);
                previous = stop;
            }
        }
        return road;
    }

    /// <summary>Seconds to run the whole pending route at the body's speed, lingering at every told stop. Answerable with no parameters.</summary>
    internal double RouteSeconds() => RouteLength() / Speed() + FollowingCount() * LingerAfterTold();

    /// <summary>The road still ahead for a body standing at (x, y): to the first stop ahead through the passages, then the
    /// route. When no road fits the body (marks closing every way), the distance as the crow flies: a read never refuses.</summary>
    internal double DistanceLeft(double x, double y)
    {
        if (!HasPendingMission()) return 0;
        var first = NextPending().StopsAhead.FirstOrDefault();
        if (first == null) return RouteLength();
        var here = new Position(x, y);
        try { return Planner().RoadLength(here, first) + RouteLength(); }
        catch (DomainException) { return here.DistanceTo(first) + RouteLength(); }
    }

    /// <summary>Seconds until every pending mission is done, for a body standing at (x, y), at the body's speed and with its lingers.</summary>
    internal double SecondsLeft(double x, double y) => DistanceLeft(x, y) / Speed() + FollowingCount() * LingerAfterTold();

    // ---- reads (guarded: consult HasPendingMission() first) ----

    internal int NextId() => NextPending().Id;
    /// <summary>Where the body heads now: the first leg ahead of the next pending mission.</summary>
    internal double HeadingX() => NextPending().NextLeg.At.X;
    internal double HeadingY() => NextPending().NextLeg.At.Y;

    // ---- inside ----

    // The planner for this body: the layout says where the walls and doors stand, the collisions module what
    // nobody charted, and between the two it finds the shortest road.
    private RoutePlanner Planner() => new(layout, collisions, Radius());

    // A new handle opens an errand with this stop; the errand's own handle adds one more stop to it.
    private int Entrust(int id, Position stop, bool following, bool choosesOrder)
    {
        if (stop == null) throw new DomainException($"mission {id} needs a stop");
        if (layout.ZoneCount > 0 && !layout.IsOnMap(stop))
            throw new DomainException($"the point ({Fmt(stop.X)}, {Fmt(stop.Y)}) is nowhere on the map");
        if (Knows(id))
        {
            Find(id).AddStop(stop, following, choosesOrder);
            return id;
        }
        if (id <= lastHandle) throw new DomainException($"handle {id} was already spent: handles are never reused");
        missions.Add(new Mission(id, stop, following, choosesOrder));
        lastHandle = id;
        return id;
    }

    private Mission NextPending()
    {
        foreach (Mission m in missions)
            if (m.IsPending()) return m;
        throw new DomainException("no pending mission: consult HasPendingMission() first");
    }

    private Mission LastPending()
    {
        for (int i = missions.Count - 1; i >= 0; i--)
            if (missions[i].IsPending()) return missions[i];
        throw new DomainException("no pending mission: consult HasPendingMission() first");
    }

    private Mission Find(int id)
    {
        foreach (Mission m in missions)
            if (m.Id == id) return m;
        throw new DomainException($"unknown mission {id}: consult Knows(id) first");
    }

    private static string Fmt(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
}
