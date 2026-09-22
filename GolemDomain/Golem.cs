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
    private Pose held;           // where the body stood when the operator held the golem; null while it is free to move
    private Pose standing;       // where the body last stood, facing which way, as the acts brought it; null until the first act with a pose
    private Pose lastBumpBody;   // the LAST bump into a thing: where the body stood — a peer's touch landing there annuls it (22-sep-2026)
    private Pose lastBumpTouch;  // …where the touch landed (the mark it left)
    private Route lastBumpRoute; // …and the route that took it
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

    /// <summary>The operator sends the golem to a point, from where it stands (or where its last pending route ends):
    /// a new route, handle minted here, its way DECIDED INSIDE from that start — <c>{ from = Position(@fx, @fy);
    /// point = Position(@x, @y); route = g.Visit(from, point); }</c> — and returned, to be told more stops
    /// (<c>route.Then</c>) and to hand out one leg at a time (Juan, 16-sep-2026: "uno crea el visit, crea el objeto en
    /// memoria con todos los puntos… y solo imprime el punto target"). Refused when no way fits the body.</summary>
    internal Route Visit(Position from, Position stop)
    {
        if (from == null) throw new GolemDomainException("Golem.Visit: 'from' was not given");
        if (stop == null) throw new GolemDomainException("Golem.Visit: 'stop' was not given");
        if (ReferenceEquals(from, stop)) throw new GolemDomainException("Golem.Visit: 'from' and 'stop' are the same position");
        return Entrust(from, stop, following: false, choosesOrder: false);
    }

    /// <summary>The operator sends the golem to a place: its centre — <c>route = g.Visit(from, map.Find(@area))</c>.</summary>
    internal Route Visit(Position from, Area area)
    {
        if (from == null) throw new GolemDomainException("Golem.Visit: 'from' was not given");
        if (area == null) throw new GolemDomainException("Golem.Visit: 'area' was not given");
        return Entrust(from, Centre(area), following: false, choosesOrder: false);
    }

    /// <summary>The operator opens a route whose order of stops the golem may choose, so the whole way is shortest.</summary>
    internal Route Cover(Position from, Position stop)
    {
        if (from == null) throw new GolemDomainException("Golem.Cover: 'from' was not given");
        if (stop == null) throw new GolemDomainException("Golem.Cover: 'stop' was not given");
        if (ReferenceEquals(from, stop)) throw new GolemDomainException("Golem.Cover: 'from' and 'stop' are the same position");
        return Entrust(from, stop, following: false, choosesOrder: true);
    }

    /// <summary>The operator opens a route through areas whose order the golem may choose.</summary>
    internal Route Cover(Position from, Area area)
    {
        if (from == null) throw new GolemDomainException("Golem.Cover: 'from' was not given");
        if (area == null) throw new GolemDomainException("Golem.Cover: 'area' was not given");
        return Entrust(from, Centre(area), following: false, choosesOrder: true);
    }

    /// <summary>The golem follows its leader to a point a peer says it reached — a route of its own, handle minted here,
    /// its way DECIDED AT ONCE from where the golem knows its body stands (Juan, 18-sep-2026: "ese mismo comando crea la
    /// route para llegar al punto… veo innecesario el decide"): where its last pending route ends when it is busy, else
    /// where its body last stood (<see cref="Standing"/>). Refused before any act brought the body's pose. (Leader–follower
    /// formation by told waypoints, not by sensing the leader: the follower knows where the leader WAS, which is why a newer
    /// told point supersedes an older one and its last stop is met a standoff short.)</summary>
    internal Route Follow(Position at)
    {
        if (at == null) throw new GolemDomainException("Golem.Follow: 'at' was not given");
        var from = Whereabouts();
        if (from == null) throw new GolemDomainException("the golem does not know where its body stands yet: no act brought its pose");
        return Entrust(from, at, following: true, choosesOrder: false);
    }

    /// <summary>The golem wakes where its body stands — <c>g.Wake(Pose(@x, @y, @theta))</c>, the first act of every boot once
    /// the body said where it is: it keeps the pose (a told point can be planned from there at once) and, with a plan underway
    /// and the golem not held, the route decides its way again from there INSIDE — the body may have been carried anywhere
    /// while the golem was down (18-sep-2026: the domain's, not a 'decide' round trip through the host). Returns whether a
    /// plan underway was decided again.</summary>
    internal bool Wake(Pose me)
    {
        if (me == null) throw new GolemDomainException("Golem.Wake: 'me' was not given");
        Stood(me);
        if (!HasPendingMission() || Held) return false;
        Underway().Awake(me);
        return true;
    }

    /// <summary>Where the body last stood, facing which way, as the acts brought it — the errand opened while free, a turn or
    /// a move reported, a touch, a hold, a resume, the waking. Null before any act brought a pose: consult KnowsWhereItStands.</summary>
    internal Pose Standing => standing;
    /// <summary>Whether any act has brought the golem where its body stands.</summary>
    internal bool KnowsWhereItStands => standing != null;

    /// <summary>A route the golem already holds, by its handle — to act on it later: <c>route = g.Find(@id); route.Reach(point);</c>.</summary>
    internal Route Find(int id)
    {
        foreach (Route r in routes)
            if (r.Id == id) return r;
        throw new GolemDomainException($"unknown route {id}: consult Knows(id) first");
    }

    // ---- routes: the way from a point, READ without deciding it (the lab's reading; the route decides its own) ----

    /// <summary>The way the golem would walk from a point through a route's stops still ahead, as objects — what
    /// <c>route.Decide(from)</c> would take. For a Cover route the stops come out in the order the golem would choose.
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

    /// <summary>The courtesy step: two radii of the body to one side of where it faces.</summary>
    internal const double CourtesyStep = Courtesy.Step;

    /// <summary>Where the body steps to get out of the way from where it stands — a courtesy step to its right if it fits,
    /// else to its left — <c>g.Aside(Pose(@x, @y, @theta))</c>; the pose itself when neither side fits (the host reads it
    /// and moves nothing). What a follower does on arriving, and what a standing body does when something touches it.</summary>
    internal Position Aside(Pose me)
    {
        if (me == null) throw new GolemDomainException("Golem.Aside: 'me' was not given");
        return Courtesy.StepOutOfTheWayOf(layout, collisions, Radius(), null, me) ?? me;
    }

    /// <summary>Where the last pending route ends: the point a NEW errand's way should start from when the golem is
    /// busy — it will stand there when the new errand comes up. Consult HasPendingMission first.</summary>
    internal Pose PlannedEnd() => LastPending().PlannedEnd;

    // ---- touches: the facts take their objects (a Pose, a Position); what a reaction must tell the peers is exposed
    //      beside the act as @params, because the matcher captures no object (Fase 0, P3). ONE row per touch (Juan,
    //      10-sep): the bump presumes it touched a THING and marks it at once; if a peer says it bumped there and
    //      then, Met takes the mark back — and the peers, told, take back what they learned (LearnMet). ----

    /// <summary>How many times something touched the body while it stood without a route — a body each time, since things do not move.</summary>
    internal int IdleBumps => idleBumps;

    /// <summary>The body bumped into something on its way — WHERE THE BODY STOOD, facing which way (<paramref name="me"/>), and
    /// WHERE ON ITS SHELL it was pressed (<paramref name="bearing"/>: radians from the direction it faces; 0 the nose, +π/2 the
    /// left flank) — <c>route = g.Bump(me, @bearing);</c>. A bumper knows no more than that; where the touch landed on the
    /// plane is the DOMAIN's to reckon, from the body it declared (Juan, 18-sep-2026: "el método del dominio debe calcular la
    /// coordenada de la colisión basado en el cuerpo del robot: la posición del golpe más el radio, así sabemos con más certeza
    /// dónde está realmente el obstáculo"): one radius from the centre, in the direction of the bearing, heading into the
    /// thing. With a route underway the golem hands it the touch: the route concludes what it was and corrects its way inside.
    /// With NOTHING underway the body stood and something touched it — a body, since things do not move (Juan, 22-sep-2026,
    /// ajuste 48): no mark, counted, remembered, and told like any bump, so the one that moved learns it met a body. Returns
    /// whether a route took the touch.</summary>
    internal bool Bump(Pose me, double bearing)
    {
        if (me == null) throw new GolemDomainException("Golem.Bump: 'me' was not given");
        if (!double.IsFinite(bearing)) throw new GolemDomainException("Golem.Bump: 'bearing' must be an angle");
        var touch = TouchOn(me, bearing);
        if (!HasPendingMission())
        {
            idleBumps++;
            lastBumpBody = me; lastBumpTouch = touch; lastBumpRoute = null;   // a body touched me: its word will land here
            Stood(me);
            return false;
        }
        var route = Underway();
        int bumps = route.Bumps;
        route.Touched(touch, me);
        if (route.Bumps > bumps) { lastBumpBody = me; lastBumpTouch = touch; lastBumpRoute = route; }   // a thing, for now: remembered, a peer's word may annul it
        Stood(me);
        return true;
    }

    /// <summary>How far from where my body stood a peer's touch may land and still be a touch ON MY BODY: the shell itself is one
    /// radius away; another radius and a mark's margin for the estimation error of two bumps (Juan, 22-sep-2026: "si su toque está
    /// dentro de mi radio o cerca").</summary>
    private double MeetingTolerance() => 2 * Radius() + Collisions.MarkMargin;

    /// <summary>Where a touch on the shell landed on the plane, heading into what was touched: one radius of the body from
    /// where it stands, in the direction it faces turned by the bearing.</summary>
    private Pose TouchOn(Pose body, double bearing)
    {
        double heading = Math.Atan2(Math.Sin(body.Heading + bearing), Math.Cos(body.Heading + bearing));
        var at = body.Along(heading, Radius());
        return new Pose(at.X, at.Y, heading);
    }

    /// <summary>A moving peer says it bumped — where ITS BODY stood, facing which way, and where on its shell (the bearing): the
    /// same words its own bump was written in. Where the touch landed is reckoned here as the peer reckoned it (the fleet's
    /// bodies share one size, body_v1). Heard and kept, so I know where that peer is. Then the golem CONCLUDES (Juan,
    /// 22-sep-2026: "si su toque está dentro de mi radio o cerca, anular el último bump"): if the peer's touch landed ON MY
    /// BODY — within MeetingTolerance of where I stood at my last bump — what I bumped into was that body: my mark is taken
    /// back, the peer's touch is not learned, the encounter is kept as history (Met) and the route that took the bump annuls
    /// it (Route.Unbump). Otherwise the peer's touch is learned as a mark, unless it lies on a wall we both know. Returns how
    /// many bumps heard.</summary>
    internal int HearBump(string who, Pose peerAt, double bearing)
    {
        if (peerAt == null) throw new GolemDomainException("Golem.HearBump: 'peerAt' was not given");
        if (!double.IsFinite(bearing)) throw new GolemDomainException("Golem.HearBump: 'bearing' must be an angle");
        var touch = TouchOn(peerAt, bearing);
        if (collisions.HeardAlready(who, touch, peerAt)) return collisions.HeardCount;   // the wire said it twice: heard once
        int heard = collisions.Hear(who, touch, peerAt);
        if (lastBumpTouch != null && touch.DistanceTo(lastBumpBody) <= MeetingTolerance())
        {
            collisions.Meet(who, peerAt);                                                 // the peer's touch landed on my body: we met — it stands THERE, in my way while my route lasts…
            if (lastBumpRoute != null)
            {
                collisions.Unmark(lastBumpTouch);                                         // …the mark my bump left is taken back — that one alone…
                if (lastBumpRoute.IsPending()) lastBumpRoute.Unbump();                    // …and my route annuls the bump (a standing body marked nothing)
            }
            lastBumpBody = null; lastBumpTouch = null; lastBumpRoute = null;             // annulled once: the next word is about something else
            return heard;
        }
        // a third party's ear (22-sep-2026): this touch landed on the body of another peer heard before, or that peer's touch
        // landed on this one's body — those two met each other; nothing stands there, and what I learned from the first word goes
        var other = collisions.Heard.LastOrDefault(h => h.Who != who
            && (h.PeerAt.DistanceTo(touch) <= MeetingTolerance() || h.At.DistanceTo(peerAt) <= MeetingTolerance()));
        if (other != null) { collisions.Unmark(other.At); return heard; }
        if (layout.IsWallAt(touch, MapLayout.WallTolerance)) return heard;             // the peer grazed a wall we both know: nothing learned
        collisions.Mark(touch);                                                          // a thing, as the peer presumed
        return heard;
    }

    /// <summary>The golem concludes what it touched was a peer — who said it bumped there and then: the mark its bump
    /// presumed is taken back, and the encounter is kept among the obstacles as a Peer, history, nothing to plan around.
    /// Concluded inside HearBump when the peer's touch landed on my body (22-sep-2026); each side hears the other and annuls
    /// its own, so no tell is needed. Returns how many bodies it has met.</summary>
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

    /// <summary>The route UNDERWAY — the one the golem is on, the first pending: <c>route = g.Underway();</c>, <c>g.Underway().Order</c>,
    /// <c>g.Underway().NextLeg.At.X</c> (Juan, 17-sep-2026: a name that says it, not "Next").</summary>
    internal Route Underway() => NextPending();

    // ---- the hold: the operator holds the GOLEM, not an errand (Juan, 17-sep-2026: "¿por qué pausamos la ruta y no el
    //      cerebro?") — the body stands whatever route is underway, and stays standing if that route ends meanwhile ----

    /// <summary>Whether the operator holds the golem: the body stands where it is until it is resumed.</summary>
    internal bool Held => held != null;
    /// <summary>Where the body stood, facing which way, when the golem was held. Null while it is free to move.</summary>
    internal Pose HeldAt => held;

    /// <summary>The operator holds the golem where its body stands — <c>route = g.Pause(Pose(@x, @y, @theta));</c>: the
    /// route underway is held too (it keeps where it was interrupted) and handed back, to be asked what it says now.
    /// Refused when nothing is underway or the golem is already held.</summary>
    internal Route Pause(Pose me)
    {
        if (me == null) throw new GolemDomainException("Golem.Pause: 'me' was not given");
        if (Held) throw new GolemDomainException("the golem is already paused");
        var route = Underway();
        route.Pause(me);
        held = me;
        Stood(me);
        return route;
    }

    /// <summary>The operator lets the golem go on — <c>route = g.Resume(Pose(@x, @y, @theta));</c>, where the body stands
    /// now: the route underway takes up its next leg from there (its heading given again) and is handed back. Refused
    /// when not held.</summary>
    internal Route Resume(Pose me)
    {
        if (me == null) throw new GolemDomainException("Golem.Resume: 'me' was not given");
        if (!Held) throw new GolemDomainException("the golem is not paused");
        held = null;
        Stood(me);
        var route = Underway();
        if (route.Paused) route.Resume(me);
        return route;
    }

    /// <summary>The route handed out last — the one an errand just opened, to tell it more stops (<c>g.Newest().Id</c>).</summary>
    internal Route Newest()
    {
        if (routes.Count == 0) throw new GolemDomainException("no route yet: consult Routes().Count first");
        return routes[^1];
    }

    // ---- inside ----

    // The planner for this body: the layout says where the walls and doors stand, the collisions module what
    // nobody charted, and between the two it finds the shortest road.
    private RoutePlanner Planner() => new(layout, collisions, Radius());

    // A new route with this stop, its way decided from `from` at once: the handle is the next one, minted here (a
    // deterministic function of the routes the golem holds, so the same on replay), and never reused — the idempotency
    // keys of the host hang on it. Opened while the golem is free, `from` is where its body stands: kept.
    private Route Entrust(Position from, Position stop, bool following, bool choosesOrder)
    {
        if (!HasPendingMission()) collisions.PeersMovedOn();   // an idle golem sets out afresh: whoever it met while standing has moved on
        var route = new Route(lastHandle + 1, stop, following, choosesOrder, layout, collisions, Radius(), body.Retreat.InMeters, Stood);
        route.Decide(from);   // refused (no way fits) before the golem holds it: nothing is minted
        if (!HasPendingMission()) standing = from as Pose ?? new Pose(from.X, from.Y, standing?.Heading ?? 0.0);
        routes.Add(route);
        lastHandle = route.Id;
        return route;
    }

    // Where the body stands now — to plan a told point from: where the last pending route ends when the golem is busy,
    // else where its body last stood; null before any act brought a pose.
    private Pose Whereabouts() => HasPendingMission() ? PlannedEnd() : standing;

    // What a route tells its golem when the body reported a turn or a move: where it stood then — and every route the body
    // has not set out on yet measures its first order from there, not from where its planning expected the body to be.
    private void Stood(Pose me)
    {
        standing = me;
        foreach (var route in routes)
            if (route.IsPending() && !route.HasSetOut) route.StandAt(me);
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
