using System.Globalization;
using GolemHost.Domain.Geometry;
using GolemHost.Domain.Plans;
using GolemHost.Domain.Robots;
using GolemHost.Domain.Routes;

namespace GolemHost.Domain;

/// <summary>
/// The golem: the robot's mind — the executor that is entrusted missions, carries them out over its floor
/// plan with its body, and keeps how each one ended. Aggregate root of its journal (the DSL's <c>g = Golem()</c>);
/// refuses a repeated handle. Its name is the journal's identity and its estimated position is telemetry: both
/// come from the host, as the actor's name and as query parameters, never as state here.
/// </summary>
internal sealed class Golem
{
    private readonly List<Mission> missions = new();
    private readonly Body body = new();
    private readonly FloorPlan plan = new();          // the map module: what the golem was told
    private readonly ObstacleMap learned;             // the obstacles module: what its bodies touched
    private readonly List<HeardBump> heard = new();   // peers' bumps, as they told them: who, and where
    private int idleBumps;       // times something touched the body while it stood without a mission
    private int lastHandle;      // a handle names one mission forever — even after letting go (idempotency keys hang on it)

    internal Golem() => learned = new ObstacleMap(plan);

    // ---- the body (issued by upgrade releases) ----

    /// <summary>Gives the golem a body: the radius of the disk it occupies, in world units. Returns it.</summary>
    internal double Embody(double bodyRadius) => body.Embody(bodyRadius);

    /// <summary>Sets the body's cruise speed, in world units per second. Returns it.</summary>
    internal double Cruise(double unitsPerSecond) => body.Cruise(unitsPerSecond);

    /// <summary>Sets how long the golem lingers at every stop a peer told it about — the follower's pacing. Returns it.</summary>
    internal double Linger(double seconds) => body.Linger(seconds);

    /// <summary>The body's radius; zero until the init release runs (a golem that has no body yet is a point).</summary>
    internal double Radius() => body.Radius;
    internal double Speed() => body.Speed();
    internal double LingerAfterTold() => body.LingerAfterTold;

    // ---- the map (issued by upgrade releases, one fluent chain per place) ----

    /// <summary>Charts a room of the map. Chain its passages: <c>g.Chart('kitchen', 0, 6, 4, 5).DoorTo('hall', 4, 8.5)</c>.</summary>
    internal Place Chart(string name, double x, double y, double width, double height) => plan.AddPlace(name, x, y, width, height);

    internal int PlaceCount() => plan.PlaceCount;
    internal int PassageCount() => plan.PassageCount;
    internal bool KnowsPlace(string name) => plan.Knows(name);
    internal bool IsOnMap(double x, double y) => plan.IsOnMap(new Position(x, y));
    internal string PlaceAt(double x, double y) => plan.PlaceAt(new Position(x, y)).Name;

    /// <summary>The map as objects, for whoever draws it: every place, each knowing its corners, its walls (with
    /// their doors), its doors, its open boundaries, the marks standing in it and the obstacles they outline. A
    /// query walks them with foreach and prints their properties —
    /// <c>foreach (places in g.Places()) { print places.Name 'name', places.Center.X 'cx'; foreach (doors in places.Doors()) { print doors.To 'to'; } }</c>
    /// — so the golem hands out its objects and never renders a document.</summary>
    internal IReadOnlyList<Place> Places() => plan.Places;

    /// <summary>Whether a point the body touched lies on a wall the golem KNOWS: a wall of a place, within a
    /// tolerance that absorbs the wall's thickness and the pose's error, outside its doorways. Touching a known wall
    /// is the golem's own execution error; touching anything else is reality holding something the map does not.</summary>
    internal bool KnowsWallAt(double x, double y) => plan.IsWallAt(new Position(x, y), FloorPlan.WallTolerance);

    /// <summary>The shortest road between two places, center to center, through the passages, for this body.</summary>
    internal double Distance(string from, string to) => Planner().RoadLength(plan.PlaceNamed(from).Center, plan.PlaceNamed(to).Center);

    /// <summary>Whether this body stands clear at a point: on the map, off the walls and off every mark.</summary>
    internal bool FitsAt(double x, double y)
    {
        var at = new Position(x, y);
        return plan.HasRoom(at, body.Radius) && !learned.Blocks(at, body.Radius);
    }

    /// <summary>Whether the walls alone leave room for this body at a point — what it asks while feeling around a mark.</summary>
    internal bool HasRoomAt(double x, double y) => plan.HasRoom(new Position(x, y), body.Radius);

    /// <summary>How many marks the map holds: points where a body touched something the plan does not hold.</summary>
    internal int MarkCount() => learned.MarkCount;

    /// <summary>How many obstacles the golem hypothesizes: the things the marks outline (marks close to one another
    /// are vertices of one thing) plus the peers it met.</summary>
    internal int ObstacleCount() => learned.All().Count;

    /// <summary>Every obstacle the golem hypothesizes, whatever place it stands in: the things the marks outline
    /// and the peers it met, each knowing its kind, its zone, its figure, its centre and its vertices. Walked with
    /// foreach, one row per obstacle and one per vertex —
    /// <c>foreach (obstacles in g.Obstacles()) { print obstacles.Kind 'kind', obstacles.Where 'zone'; foreach (vertices in obstacles.Vertices()) { print vertices.X 'x', vertices.Y 'y'; } }</c>
    /// — so a table can be drawn from the golem's own objects, never from a document it rendered.</summary>
    internal IReadOnlyList<Obstacle> Obstacles() => learned.All();

    /// <summary>How many things the marks outline — the obstacles the roads avoid.</summary>
    internal int ThingCount() => learned.Things().Count;

    /// <summary>How many times the body met another body.</summary>
    internal int MetCount() => learned.EncounterCount;

    /// <summary>Whether every token names a place or a point 'x,y' on the map — what a list of stops must be made of.</summary>
    internal bool AreStops(string[] stops)
    {
        if (stops == null || stops.Length == 0) return false;
        foreach (var token in stops) if (TryStop(token) == null) return false;
        return true;
    }

    // ---- missions: the entrusting (the operator's voice) ----

    /// <summary>The operator sends the golem to a point. A repeated or spent handle is a caller bug.</summary>
    internal int Visit(int id, double x, double y) => Entrust(id, new[] { new Position(x, y) }, following: false, choosesOrder: false);

    /// <summary>The operator sends the golem to a place: its center.</summary>
    internal int Visit(int id, string place) => Entrust(id, new[] { plan.PlaceNamed(place).Center }, following: false, choosesOrder: false);

    /// <summary>The operator sends the golem through several stops, in this order. A stop is a place name or a point 'x,y'.</summary>
    internal int Visit(int id, string[] stops) => Entrust(id, Stops(stops), following: false, choosesOrder: false);

    /// <summary>The operator sends the golem through several stops and lets it choose the order that makes the road shortest.</summary>
    internal int Cover(int id, string[] stops) => Entrust(id, Stops(stops), following: false, choosesOrder: true);

    /// <summary>The golem follows its leader to a point a peer says it reached — a mission with a handle of its own.
    /// (Leader–follower formation by told waypoints, not by sensing the leader: the follower knows where the leader
    /// WAS, which is why a newer told point supersedes an older one and the host keeps a standoff on arrival.)</summary>
    internal int Follow(double x, double y) => Entrust(NextHandle(), new[] { new Position(x, y) }, following: true, choosesOrder: false);

    // ---- missions: the road ----

    /// <summary>The road the golem would walk from (x, y) through a mission's stops still ahead, as the journal writes it:
    /// "kitchen/north@4,9.5 > kitchen@2,9.5 > kitchen/north@4,9.5 > north/storage@7,9.5 > storage@9,9.5".
    /// For a Cover mission the stops come out in the order the golem chose. Marks are skirted ('around' legs)
    /// or, where the body would not fit past them, avoided by another road altogether.</summary>
    internal string Plan(int id, double x, double y)
    {
        var mission = Find(id);
        var from = new Position(x, y);
        var ahead = mission.StopsAhead.ToList();
        var planner = Planner();
        var stops = mission.ChoosesOrder ? planner.BestOrder(from, ahead) : ahead;
        return planner.Road(from, stops).AsPlan();
    }

    /// <summary>The golem decides its road for a mission (the plan as text, so the decision reads in the journal). Returns the number of legs.
    /// The text names doors, openings, detours and stops; how each door is crossed (straight in, straight out) is derived from the
    /// map again. A road is decided a second time only after a bump on it: the journal then reads "bumped, bumped, took another road".</summary>
    internal int Route(int id, string road)
    {
        var trajectory = plan.WithDoorCrossings(Trajectory.Parse(road));
        Find(id).Route(trajectory);
        return trajectory.Count;
    }

    /// <summary>The road out of a peer's way and on to the stops still ahead, as the journal writes it:
    /// "aside@4.6,10.3 > kitchen/north@4,9.5 > kitchen@2,9.5". The first leg is the courtesy step — a body's
    /// width to ONE SIDE of where the golem faces, chosen so it moves away from the peer and where its own body
    /// fits; both bodies step to their own right when they can, which is how two of them pass instead of shove.
    /// The peer's position is what it told when it bumped (HearBump); without it, or with nowhere to step, the
    /// road is the plain one from here.</summary>
    internal string PlanPast(int id, string who, double x, double y, double heading)
    {
        var mission = Find(id);
        var me = new Pose(x, y, heading);
        var ahead = mission.StopsAhead.ToList();
        var planner = Planner();
        var aside = StepOutOfTheWayOf(who, me);
        if (aside == null) return planner.Road(me, ahead).AsPlan();
        return $"{Leg.Courtesy}@{Fmt(aside.X)},{Fmt(aside.Y)} > " + planner.Road(aside, ahead).AsPlan();
    }

    // A body's width to one side of where the golem faces: its own right first (so two bodies facing each other
    // separate), then its left. The step must fit the body and must not walk INTO the peer.
    private Position StepOutOfTheWayOf(string who, Pose me)
    {
        Position peer = null;
        for (int i = heard.Count - 1; i >= 0; i--)
            if (heard[i].Who == who) { peer = heard[i].PeerAt; break; }
        foreach (var side in new[] { Side.Right, Side.Left })
        {
            var step = new StepAside(side).From(me, me.Heading).Legs()[0].At;
            if (!FitsAt(step.X, step.Y)) continue;
            if (peer != null && step.DistanceTo(peer) <= me.DistanceTo(peer)) continue;
            return step;
        }
        return null;
    }

    /// <summary>The golem tells the host where to drive: one point, one segment — "take the body exactly here".
    /// The order the host carries out; it must be the point the road holds next (or the stop, on an errand walked
    /// without a road). Written by a reaction, never by the host: the host does not choose where to go. Returns the
    /// mission id.</summary>
    internal int MoveTo(int id, double x, double y)
    {
        Find(id).MoveTo(x, y);
        return id;
    }

    /// <summary>Whether the mission still has a point to head to — the guard the arrival command asks before
    /// exposing the next one, so a spent queue orders nothing.</summary>
    internal bool HasNextPoint(int id) => Find(id).HasNextPoint;

    /// <summary>Whether the golem has already said where the host must drive: an order is standing.</summary>
    internal bool IsOrdered(int id) => Find(id).IsOrdered;

    /// <summary>Whether getting from (x, y) through the stops ahead takes more than one segment — that is, whether
    /// there is a road to decide at all. An errand to a point in the same room, with nothing in between, has none:
    /// the golem heads straight there and no Route is written.</summary>
    internal bool NeedsRoad(int id, double x, double y)
    {
        var mission = Find(id);
        if (mission.IsRouted) return false;
        try { return Planner().Road(new Position(x, y), mission.StopsAhead.ToList()).Count > 1; }
        catch (DomainException) { return true; }   // no straight way: deciding a road is exactly what is needed
    }

    /// <summary>The evasion maneuver a strategy ('back-off', 'step-right', 'step-left') plans from where the body
    /// stands and the heading it had when it touched something: a trajectory to walk with foreach —
    /// <c>foreach (legs in g.Evasion(@x, @y, @heading, 'step-right').Legs()) { print legs.Name 'leg', legs.At.X 'x', legs.At.Y 'y'; }</c>.
    /// A read: the host executes it and reports what the body met.</summary>
    internal Maneuver Evasion(double x, double y, double heading, string strategy) =>
        EvasionStrategy.Named(strategy).From(new Position(x, y), heading);

    // ---- touches: the facts (what the body met), the hypothesis (what the golem suspects) and the conclusions ----

    /// <summary>The body touched something the map does not hold, at (x, y), heading that way, on this mission. A fact,
    /// told to the peers; what it was is concluded afterwards (Mark or Met, by what the peers say). Returns the mission id.</summary>
    internal int Bump(int id, double x, double y, double heading)
    {
        Find(id).Bump();
        return id;
    }

    /// <summary>Something touched the body at (x, y) while it stood without a mission, facing that way — a peer, most
    /// likely; told to the peers so the one that moved knows it met a body. Returns how many such touches so far.</summary>
    internal int Bump(double x, double y, double heading) => ++idleBumps;

    /// <summary>The body grazed a wall the map KNOWS at (x, y), on this mission: its own execution error, no discovery.
    /// Counted against the golem's patience on the leg (MayRetryLeg). Returns the mission id.</summary>
    internal int Graze(int id, double x, double y)
    {
        Find(id).Graze();
        return id;
    }

    /// <summary>A peer says it bumped at (x, y) while it stood at (px, py): heard and kept, so a touch of my own
    /// there and then is known to be that peer — and so that, once we know we met, I can step out of ITS way
    /// knowing where it is. The named counterpart of LearnMark, which hears of a MARK; this one hears of a BUMP.</summary>
    internal int HearBump(string who, double x, double y, double px, double py)
    {
        heard.Add(new HeardBump(who, new Position(x, y), new Position(px, py)));
        return heard.Count;
    }

    /// <summary>The golem concludes what it touched was a thing (no peer bumped there and then): a mark on the map
    /// with the heading of the touch as its normal, told to the peers. Returns how many marks it holds.</summary>
    internal int Mark(double x, double y, double heading) => learned.Mark(new Pose(x, y, heading));

    /// <summary>A peer says a thing stands at (x, y), touched heading that way: the golem learns the mark without the
    /// bruise. Returns how many marks it holds.</summary>
    internal int LearnMark(double x, double y, double heading) => learned.Mark(new Pose(x, y, heading));

    /// <summary>The golem concludes what it touched at (x, y) was a peer — who said it bumped there and then. History,
    /// kept among the obstacles as a Peer; nothing to plan around. Returns how many bodies it has met.</summary>
    internal int Met(string who, double x, double y) => learned.Meet(who, new Position(x, y));

    /// <summary>What the golem suspects its body touched at (x, y), heading that way, given what it has heard since
    /// the given count: a wall it knows (Kind 'wall': conclude Graze), a peer that bumped near there and then
    /// (Kind 'peer', Who: conclude Met), or a thing nobody charted (Kind 'thing': conclude Mark). The domain reasons;
    /// the host waits for the peers to speak, asks, and writes the conclusion the suspicion names.</summary>
    internal Suspicion Suspect(double x, double y, double heading, int sinceCount)
    {
        var at = new Position(x, y);
        if (plan.IsWallAt(at, FloorPlan.WallTolerance)) return new WallTouched();
        string who = HeardBumpNear(x, y, sinceCount);
        if (who != "") return new PeerMet(who);
        return new ThingFound();
    }

    /// <summary>How many bumps peers have told about so far — the count a leg starts from, so older news is not taken for this touch.</summary>
    internal int HeardBumpCount() => heard.Count;
    /// <summary>Who, among the bumps heard after the given count, bumped near (x, y) — within a meeting's reach; "" for nobody.
    /// (Robotics resolves two bodies meeting with reciprocal velocity obstacles — van den Berg, Lin &amp; Manocha,
    /// ICRA 2008; ORCA 2011 — each taking half the avoidance from what it senses of the other. Our bodies sense
    /// nothing but a touch, so they resolve it by speech: both tell the fact, and a deterministic rule in the host
    /// decides who yields. Same problem, solved with the puppet's means.)</summary>
    internal string HeardBumpNear(double x, double y, int sinceCount)
    {
        var at = new Position(x, y);
        for (int i = heard.Count - 1; i >= sinceCount && i >= 0; i--)
            if (heard[i].At.DistanceTo(at) <= Body.MeetingReach) return heard[i].Who;
        return "";
    }

    /// <summary>The golem crossed the next passage of its road. Returns the mission id.</summary>
    internal int Cross(int id, string passage)
    {
        Find(id).Cross(passage);
        return id;
    }

    /// <summary>The golem reached the next stop of its road; reaching the last one completes the mission. Returns the mission id.</summary>
    internal int Reach(int id, double x, double y)
    {
        Find(id).Reach(x, y);
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
    /// <summary>The next leg's name: a passage to cross, or the place of the stop to reach.</summary>
    internal string OrderPassage(int id) => Find(id).NextLeg.Name;
    internal bool OrderIsStop(int id) => Find(id).NextLeg.IsStop;
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

    /// <summary>The road through every stop still ahead, in the order they will run, through the map's passages. Zero with one stop or none.</summary>
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
    internal double RouteSeconds() => RouteLength() / Speed() + FollowingCount() * body.LingerAfterTold;

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
    internal double SecondsLeft(double x, double y) => DistanceLeft(x, y) / Speed() + FollowingCount() * body.LingerAfterTold;

    // ---- reads (guarded: consult HasPendingMission() first) ----

    internal int NextId() => NextPending().Id;
    /// <summary>Where the body should head now: the next leg of the road when routed, the first stop before that.</summary>
    internal double OrderX() => NextPending().NextLeg.At.X;
    internal double OrderY() => NextPending().NextLeg.At.Y;

    /// <summary>Where a GIVEN mission heads next — what the order must name. Consult Knows(id) and HasNextPoint(id).</summary>
    internal double OrderX(int id) => Find(id).NextLeg.At.X;
    internal double OrderY(int id) => Find(id).NextLeg.At.Y;
    /// <summary>How the next leg is walked: line up at the approach, end at the exit. For a door they stand off the wall on
    /// either side (the body crosses it straight); for an opening or a stop they are the point itself.</summary>
    internal double OrderApproachX() => NextPending().NextLeg.Approach.X;
    internal double OrderApproachY() => NextPending().NextLeg.Approach.Y;
    internal double OrderExitX() => NextPending().NextLeg.Exit.X;
    internal double OrderExitY() => NextPending().NextLeg.Exit.Y;

    // ---- inside ----

    // The planner for this body over this plan: the plan's geometry and the body's radius, distance as the cost.
    // The planner for this body: the map module says where the walls and doors are, the obstacles module what
    // nobody charted, and between the two it finds the shortest road.
    private RoutePlanner Planner() => new(plan, learned, body.Radius);

    private int Entrust(int id, IReadOnlyList<Position> stops, bool following, bool choosesOrder)
    {
        if (Knows(id)) throw new DomainException($"mission {id} already exists");
        if (id <= lastHandle) throw new DomainException($"handle {id} was already spent: handles are never reused");
        foreach (var stop in stops)
            if (plan.PlaceCount > 0 && !plan.IsOnMap(stop))
                throw new DomainException($"the point ({Fmt(stop.X)}, {Fmt(stop.Y)}) is nowhere on the map");
        missions.Add(new Mission(id, stops, following, choosesOrder));
        lastHandle = id;
        return id;
    }

    // A stop is a place name or a point 'x,y'; either way it must be on the map.
    private List<Position> Stops(string[] tokens)
    {
        if (tokens == null || tokens.Length == 0) throw new DomainException("a mission needs at least one stop");
        var stops = new List<Position>();
        foreach (var token in tokens)
            stops.Add(TryStop(token) ?? throw new DomainException($"'{token}' is neither a place nor a point x,y on the map"));
        return stops;
    }

    private Position TryStop(string token)
    {
        if (token == null) return null;
        token = token.Trim();
        if (plan.Knows(token)) return plan.PlaceNamed(token).Center;
        var xy = token.Split(',');
        if (xy.Length == 2
            && double.TryParse(xy[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
            && double.TryParse(xy[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
        {
            var at = new Position(x, y);
            return plan.IsOnMap(at) ? at : null;
        }
        return null;
    }

    private Mission NextPending()
    {
        foreach (Mission m in missions)
            if (m.IsPending()) return m;
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
