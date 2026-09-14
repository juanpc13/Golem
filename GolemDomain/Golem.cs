using System.Globalization;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Maps;
using GolemDomain.Robots;
using GolemDomain.Routes;
using GolemDomain.Touches;

namespace GolemDomain;

/// <summary>
/// The golem: the robot's mind — the executor that is entrusted routes, carries them out over its layout
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
    private readonly List<Route> routes = new();
    private readonly Body body;
    private readonly MapLayout layout;
    private readonly Collisions collisions;
    private int idleBumps;       // times something touched the body while it stood without a mission
    private int lastHandle;      // a handle names one route forever — even after letting go (idempotency keys hang on it)

    internal Golem(Body body, MapLayout map, Collisions collisions)
    {
        if (body == null) throw new GolemDomainException("a golem needs a body to drive");
        if (map == null) throw new GolemDomainException("a golem needs its map, laid out");
        if (collisions == null) throw new GolemDomainException("a golem needs its collisions module, even empty");
        this.body = body;
        layout = map;
        this.collisions = collisions;
        if (collisions.Layout != layout) throw new GolemDomainException("the collisions must be measured over the golem's own layout");
    }

    // ---- the body, in base units, for the golem's own sums (the journal reads the module: body.Radius.InMeters) ----

    private double Radius() => body.Radius.InMeters;
    private double Speed() => body.Speed.InMetersPerSecond;
    private double LingerAfterTold() => body.LingerAfterTold.InSeconds;

    // ---- the map, read through the golem only where the BODY enters the answer (the map itself is the global `map`) ----

    /// <summary>The shortest road between two areas, centre to centre, through the passages, for this body —
    /// <c>g.Distance(map.Find('kitchen'), map.Find('garage'))</c>.</summary>
    internal double Distance(Area from, Area to)
    {
        if (from == null) throw new GolemDomainException("Golem.Distance: 'from' was not given");
        if (to == null) throw new GolemDomainException("Golem.Distance: 'to' was not given");
        if (ReferenceEquals(from, to)) throw new GolemDomainException("Golem.Distance: 'from' and 'to' are the same area");
        return Planner().RoadLength(layout.Of(from).Center, layout.Of(to).Center);
    }

    /// <summary>Whether this body stands clear at a point: on the map, off the walls and off every mark — <c>g.FitsAt(Position(@x, @y))</c>.</summary>
    internal bool FitsAt(Position at)
    {
        if (at == null) throw new GolemDomainException("Golem.FitsAt: 'at' was not given");
        return layout.HasRoom(at, Radius()) && !collisions.Blocks(at, Radius());
    }

    /// <summary>Whether the walls alone leave room for this body at a point — what it asks while feeling around a mark.</summary>
    internal bool HasRoomAt(Position at)
    {
        if (at == null) throw new GolemDomainException("Golem.HasRoomAt: 'at' was not given");
        return layout.HasRoom(at, Radius());
    }

    // ---- routes: the entrusting (the operator's voice) — the golem hands the route out and every act on it is its own ----

    /// <summary>The operator sends the golem to a point: a new route, handle minted here, returned to be told the rest —
    /// <c>route = g.Visit(point); route.Then(point2); route.Via(via1); route.Stop(point);</c>. A stop off the map is refused.</summary>
    internal Route Visit(Position stop)
    {
        if (stop == null) throw new GolemDomainException("Golem.Visit: 'stop' was not given");
        return Entrust(stop, following: false, choosesOrder: false);
    }

    /// <summary>The operator sends the golem to an area: its centre — <c>route = g.Visit(map.Find(@area))</c>.</summary>
    internal Route Visit(Area area)
    {
        if (area == null) throw new GolemDomainException("Golem.Visit: 'area' was not given");
        return Entrust(Centre(area), following: false, choosesOrder: false);
    }

    /// <summary>The operator opens a route whose order of stops the golem may choose, so the whole way is shortest.</summary>
    internal Route Cover(Position stop)
    {
        if (stop == null) throw new GolemDomainException("Golem.Cover: 'stop' was not given");
        return Entrust(stop, following: false, choosesOrder: true);
    }

    /// <summary>The operator opens a route through areas whose order the golem may choose.</summary>
    internal Route Cover(Area area)
    {
        if (area == null) throw new GolemDomainException("Golem.Cover: 'area' was not given");
        return Entrust(Centre(area), following: false, choosesOrder: true);
    }

    /// <summary>The golem follows its leader to a point a peer says it reached — a route of its own, handle minted here.
    /// (Leader–follower formation by told waypoints, not by sensing the leader: the follower knows where the leader
    /// WAS, which is why a newer told point supersedes an older one and the host keeps a standoff on arrival.)</summary>
    internal Route Follow(Position at)
    {
        if (at == null) throw new GolemDomainException("Golem.Follow: 'at' was not given");
        return Entrust(at, following: true, choosesOrder: false);
    }

    /// <summary>A route the golem already holds, by its handle — to act on it later: <c>route = g.Find(@id); route.Reach(point);</c>.</summary>
    internal Route Find(int id)
    {
        foreach (Route r in routes)
            if (r.Id == id) return r;
        throw new GolemDomainException($"unknown route {id}: consult Knows(id) first");
    }

    // ---- routes: the way, previewed — reads that take the route and the body's pose as objects ----

    /// <summary>The way the golem would walk from a point through a route's stops still ahead, as objects: the legs,
    /// each knowing its kind (door, opening, around, aside, stop), its passage's areas and its point — what the host
    /// reads to write the decision act by act. For a Cover route the stops come out in the order the golem chose.
    /// Marks are skirted ('around' legs) or, where the body would not fit past them, avoided by another way.</summary>
    internal Trajectory Road(Route route, Position from)
    {
        if (route == null) throw new GolemDomainException("Golem.Road: 'route' was not given");
        if (from == null) throw new GolemDomainException("Golem.Road: 'from' was not given");
        var ahead = route.StopsAhead.ToList();
        var planner = Planner();
        var stops = route.ChoosesOrder ? planner.BestOrder(from, ahead) : ahead;
        return planner.Road(from, stops);
    }

    /// <summary>The way out of a peer's way and on to the stops still ahead, as objects: the first leg is the
    /// courtesy step — a body's width to ONE SIDE of where the golem faces, chosen so it moves away from the peer
    /// and where its own body fits; both bodies step to their own right when they can, which is how two of them
    /// pass instead of shove. The peer's position is what it told when it bumped (HearBump); without it, or with
    /// nowhere to step, the way is the plain one from here.</summary>
    internal Trajectory RoadPast(Route route, string who, Pose me)
    {
        if (route == null) throw new GolemDomainException("Golem.RoadPast: 'route' was not given");
        if (me == null) throw new GolemDomainException("Golem.RoadPast: 'me' was not given");
        var ahead = route.StopsAhead.ToList();
        var planner = Planner();
        var aside = StepOutOfTheWayOf(who, me);
        if (aside == null) return planner.Road(me, ahead);
        var legs = new List<Leg> { new(aside, Leg.Courtesy) };
        legs.AddRange(planner.Road(aside, ahead).Legs());
        return new Trajectory(legs);
    }

    /// <summary>The courtesy step: two radii of the body to one side of where it faces.</summary>
    internal const double CourtesyStep = 0.5;

    // A body's width to one side of where the golem faces: its own right first (so two bodies facing each other
    // separate), then its left. The step must fit the body and must not walk INTO the peer.
    private Position StepOutOfTheWayOf(string who, Pose me)
    {
        var peer = collisions.LastKnownPositionOf(who);
        foreach (var turn in new[] { -Math.PI / 2, Math.PI / 2 })   // right, then left
        {
            var step = me.Along(me.Heading + turn, CourtesyStep);
            if (!FitsAt(step)) continue;
            if (peer != null && step.DistanceTo(peer) <= me.DistanceTo(peer)) continue;
            return step;
        }
        return null;
    }

    /// <summary>The way a NEW errand would take, before it exists: an object that is told the stops one by one and
    /// answers the legs — <c>preview = g.Preview(Position(@x, @y), @cover); preview.Then(map.Find(@area)); …
    /// preview.Legs()</c> — for this body over this map and what it learned. What the operator's command reads to
    /// write the errand and its whole way in one entry.</summary>
    internal Preview Preview(Position from, bool choosesOrder)
    {
        if (from == null) throw new GolemDomainException("Golem.Preview: 'from' was not given");
        return new Preview(layout, Planner(), from, choosesOrder);
    }

    /// <summary>Where the last pending route ends: the point a NEW errand's way should start from when the golem is
    /// busy — it will stand there when the new errand comes up. Consult HasPendingMission first.</summary>
    internal Position PlannedEnd() => LastPending().StopsAhead.Last();

    // ---- touches: the facts take their objects (a Pose, a Position); what a reaction must tell the peers is exposed
    //      beside the act as @params, because the matcher captures no object (Fase 0, P3). ONE row per touch (Juan,
    //      10-sep): the bump presumes it touched a THING and marks it at once; if a peer says it bumped there and
    //      then, Met takes the mark back — and the peers, told, take back what they learned (LearnMet). ----

    /// <summary>Something touched the body while it stood without a mission — a body, since things do not move; told
    /// to the peers so the one that moved knows it met a body. No mark. Returns how many such touches so far.</summary>
    internal int Bump(Pose touch)
    {
        if (touch == null) throw new GolemDomainException("a bump needs the pose of the touch");
        return ++idleBumps;
    }

    /// <summary>A moving peer says it bumped — where, heading which way — while it stood at another point: heard and kept,
    /// so a touch of my own there and then is known to be that peer (and I know where it is, to step out of its way); and
    /// learned as a mark, as the peer itself presumed — until it says it met a body (LearnMet). Returns how many bumps heard.</summary>
    internal int HearBump(string who, Pose touch, Position peerAt)
    {
        if (touch == null) throw new GolemDomainException("Golem.HearBump: 'touch' was not given");
        if (peerAt == null) throw new GolemDomainException("Golem.HearBump: 'peerAt' was not given");
        int heard = collisions.Hear(who, touch, peerAt);
        collisions.Mark(touch);
        return heard;
    }

    /// <summary>A standing peer says something touched it — where, while it stood at another point: heard and kept, so a
    /// touch of my own there and then is known to be that peer. No mark: what touches a standing body is a body.</summary>
    internal int HearTouch(string who, Position at, Position peerAt)
    {
        if (at == null) throw new GolemDomainException("Golem.HearTouch: 'at' was not given");
        if (peerAt == null) throw new GolemDomainException("Golem.HearTouch: 'peerAt' was not given");
        if (ReferenceEquals(at, peerAt)) throw new GolemDomainException("Golem.HearTouch: 'at' and 'peerAt' are the same point");
        return collisions.Hear(who, at, peerAt);
    }

    /// <summary>The golem concludes what it touched was a peer — who said it bumped there and then: the mark its bump
    /// presumed is taken back, and the encounter is kept among the obstacles as a Peer, history, nothing to plan around.
    /// Told to the peers, who take back what they learned. Returns how many bodies it has met.</summary>
    internal int Met(string who, Position at)
    {
        if (at == null) throw new GolemDomainException("Golem.Met: 'at' was not given");
        int met = collisions.Meet(who, at);
        collisions.Unmark(at);                    // what I presumed a thing was that body
        collisions.UnmarkHeardFrom(who, at);      // and what it presumed there was mine: the encounter was mutual
        return met;
    }

    /// <summary>A peer says its touch there was a body: the golem takes back the mark it learned from that bump. Returns how many marks went.</summary>
    internal int LearnMet(Position at)
    {
        if (at == null) throw new GolemDomainException("Golem.LearnMet: 'at' was not given");
        return collisions.Unmark(at);
    }

    /// <summary>The operator says what stood at a point is gone — somebody took it away — and the golem forgets the
    /// obstacle there with EVERY mark that outlined it: a body may pass again, and a touch after this is a NEW
    /// obstacle. Told to the peers, who forget it too. Returns how many facts it dropped.</summary>
    internal int Forget(Position at)
    {
        if (at == null) throw new GolemDomainException("forgetting needs where");
        return collisions.Forget(at);
    }

    /// <summary>A peer says what stood at a point is gone: the golem forgets it too, without having gone to see.</summary>
    internal int LearnForget(Position at)
    {
        if (at == null) throw new GolemDomainException("forgetting needs where");
        return collisions.Forget(at);
    }

    /// <summary>What the golem suspects its body touched — the pose of the touch, as telemetry builds it in the query
    /// (<c>g.Suspect(Pose(@x, @y, @heading), @since)</c>) — given what it has heard since the given count: a wall it
    /// knows (Kind 'wall': conclude Graze), a peer that bumped near there and then (Kind 'peer', Who: conclude Met), or
    /// a thing nobody charted (Kind 'thing': the mark the bump presumed stands). The domain reasons; the host waits for
    /// the peers to speak, asks, and writes the conclusion the suspicion names.</summary>
    internal Suspicion Suspect(Pose touch, int sinceCount)
    {
        if (touch == null) throw new GolemDomainException("Golem.Suspect: 'touch' was not given");
        return collisions.Suspect(touch, sinceCount);
    }


    // ---- routes: the progress — only what fulfils the plan is journaled ----

    // ---- reads (total: never throw when nothing is there) — per-route questions are the ROUTE's own (g.Find(@id).StopsLeft) ----

    internal bool Knows(int id) => routes.Any(m => m.Id == id);
    internal bool HasPendingMission() => routes.Any(m => m.IsPending());
    /// <summary>Every route the golem was ever handed, as objects — <c>g.Routes().Count</c> is how many.</summary>
    internal IReadOnlyList<Route> Routes() => routes.ToList();
    /// <summary>The routes still pending, as objects — <c>foreach (route in g.PendingRoutes()) { route.Abandon(@reason); }</c>, <c>g.PendingRoutes().Count</c>.</summary>
    internal IReadOnlyList<Route> PendingRoutes() => routes.Where(m => m.IsPending()).ToList();
    /// <summary>The newest pending point a peer told about — where the leader is now, as far as the follower knows. Consult HasNewerFollowing first.</summary>
    internal int NewestFollowingId() => routes.Where(m => m.IsPending() && m.Following).Select(m => m.Id).DefaultIfEmpty(0).Max();
    /// <summary>Whether a FOLLOWED route has been overtaken by a newer followed one; an operator's route never is.</summary>
    internal bool HasNewerFollowing(Route route)
    {
        if (route == null) throw new GolemDomainException("Golem.HasNewerFollowing: 'route' was not given");
        return route.Following && routes.Any(m => m.IsPending() && m.Following && m.Id > route.Id);
    }
    private int FollowingCount() => routes.Count(m => m.IsPending() && m.Following);

    // ---- the road ahead, answered from the golem's own knowledge ----

    /// <summary>The road through every stop still ahead, in the order they will run, through the passages. Zero with one stop or none.</summary>
    internal double RouteLength()
    {
        var planner = Planner();
        double road = 0;
        Position previous = null;
        foreach (Route m in routes)
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

    /// <summary>The way still ahead for a body standing at a point — <c>g.DistanceLeft(Position(@x, @y))</c>: to the first stop
    /// ahead through the passages, then the route. When no way fits the body (marks closing every way), the distance as
    /// the crow flies: a read never refuses.</summary>
    internal double DistanceLeft(Position here)
    {
        if (here == null) throw new GolemDomainException("Golem.DistanceLeft: 'here' was not given");
        if (!HasPendingMission()) return 0;
        var first = NextPending().StopsAhead.FirstOrDefault();
        if (first == null) return RouteLength();
        try { return Planner().RoadLength(here, first) + RouteLength(); }
        catch (GolemDomainException) { return here.DistanceTo(first) + RouteLength(); }
    }

    /// <summary>Seconds until every pending route is done, for a body standing at a point, at the body's speed and with its lingers.</summary>
    internal double SecondsLeft(Position here) => DistanceLeft(here) / Speed() + FollowingCount() * LingerAfterTold();

    // ---- reads (guarded: consult HasPendingMission() first) ----

    /// <summary>The route the golem is on: the first pending one — <c>g.Next().Id</c>, <c>g.Next().NextLeg.At.X</c>, <c>g.Next().StopsLeft</c>.</summary>
    internal Route Next() => NextPending();

    // ---- inside ----

    // The planner for this body: the layout says where the walls and doors stand, the collisions module what
    // nobody charted, and between the two it finds the shortest road.
    private RoutePlanner Planner() => new(layout, collisions, Radius());

    // A new route with this stop: the handle is the next one, minted here (a deterministic function of the routes the
    // golem holds, so the same on replay), and never reused — the idempotency keys of the host hang on it.
    private Route Entrust(Position stop, bool following, bool choosesOrder)
    {
        var route = new Route(lastHandle + 1, stop, following, choosesOrder, layout, collisions);
        routes.Add(route);
        lastHandle = route.Id;
        return route;
    }

    private Position Centre(Area area)
    {
        if (area == null) throw new GolemDomainException("a route needs an area");
        return layout.Of(area).Center;
    }

    private Route NextPending()
    {
        foreach (Route m in routes)
            if (m.IsPending()) return m;
        throw new GolemDomainException("no pending mission: consult HasPendingMission() first");
    }

    private Route LastPending()
    {
        for (int i = routes.Count - 1; i >= 0; i--)
            if (routes[i].IsPending()) return routes[i];
        throw new GolemDomainException("no pending mission: consult HasPendingMission() first");
    }

    private static string Fmt(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
}
