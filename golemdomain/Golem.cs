using System.Globalization;

namespace GolemHost.Domain;

/// <summary>
/// The golem: the executor that is entrusted missions, carries them out, and keeps how
/// each one ended. Aggregate root of its journal; refuses a repeated handle.
/// </summary>
internal sealed class Golem
{
    private readonly List<Mission> missions = new();
    private readonly Atlas atlas = new();
    private int lastHandle;      // a handle names one mission forever — even after letting go (idempotency keys hang on it)
    private double speed;        // the body's cruise speed, units/s — a property released into the journal
    private double linger;       // seconds the golem lingers at every stop a peer told it about
    private double radius;       // the body's size: the radius of the disk it occupies — a property released into the journal

    internal Golem() { }

    // ---- the body (issued by upgrade releases) ----

    /// <summary>Gives the golem a body: the radius of the disk it occupies, in world units. Returns it.</summary>
    internal double Embody(double bodyRadius)
    {
        if (bodyRadius <= 0) throw new DomainException("a body needs a radius above zero");
        radius = bodyRadius;
        return radius;
    }

    /// <summary>Sets the body's cruise speed, in world units per second. Returns it.</summary>
    internal double Cruise(double unitsPerSecond)
    {
        if (unitsPerSecond <= 0) throw new DomainException("a body needs a cruise speed above zero");
        speed = unitsPerSecond;
        return speed;
    }

    /// <summary>Sets how long the golem lingers at every stop a peer told it about — the follower's pacing. Returns it.</summary>
    internal double Linger(double seconds)
    {
        if (seconds < 0) throw new DomainException("a linger cannot be negative");
        linger = seconds;
        return linger;
    }

    /// <summary>The body's radius; zero until the init release runs (a golem that has no body yet is a point).</summary>
    internal double Radius() => radius;

    internal double Speed()
    {
        if (speed <= 0) throw new DomainException("the golem has no cruise speed yet: the init release must run first");
        return speed;
    }

    internal double LingerAfterTold() => linger;

    // ---- the map (issued by upgrade releases, one fluent chain per place) ----

    /// <summary>Charts a room of the map. Chain its passages: <c>g.Chart('kitchen', 0, 6, 4, 5).DoorTo('hall', 4, 8.5)</c>.</summary>
    internal Place Chart(string name, double x, double y, double width, double height) => atlas.AddPlace(name, x, y, width, height);

    internal int PlaceCount() => atlas.PlaceCount;
    internal int PassageCount() => atlas.PassageCount;
    internal bool KnowsPlace(string name) => atlas.Knows(name);
    internal bool IsOnMap(double x, double y) => atlas.IsOnMap(new Waypoint(x, y));
    internal string PlaceAt(double x, double y) => atlas.PlaceAt(new Waypoint(x, y)).Name;

    /// <summary>The map as objects, for whoever draws it: every place, each knowing its doors, its open boundaries
    /// and the marks standing in it. A query walks them with foreach and prints their properties —
    /// <c>foreach (places in g.Places()) { print places.Name 'name', places.Center.X 'cx'; foreach (doors in places.Doors()) { print doors.To 'to'; } }</c>
    /// — so the golem hands out its objects and never renders a document.</summary>
    internal IReadOnlyList<Place> Places() => atlas.Places;

    /// <summary>Whether a point the body touched lies on a wall the golem KNOWS: a boundary of a place that is not
    /// open, within a tolerance that absorbs the wall's thickness and the pose's error. Touching a known wall is the
    /// golem's own execution error; touching anything else is reality holding something the map does not.</summary>
    internal bool KnowsWallAt(double x, double y) => atlas.IsWallAt(new Waypoint(x, y), Atlas.WallTolerance);

    /// <summary>The shortest road between two places, center to center, through the passages, for this body.</summary>
    internal double Distance(string from, string to) => atlas.RoadLength(atlas.PlaceNamed(from).Center, atlas.PlaceNamed(to).Center, radius);

    /// <summary>Whether this body stands clear at a point: on the map, off the walls and off every mark.</summary>
    internal bool FitsAt(double x, double y) => atlas.Fits(new Waypoint(x, y), radius);

    /// <summary>Whether the walls alone leave room for this body at a point — what it asks while feeling around a mark.</summary>
    internal bool HasRoomAt(double x, double y) => atlas.HasRoom(new Waypoint(x, y), radius);

    /// <summary>How many marks the map holds: points where a body touched something the plan does not hold.</summary>
    internal int MarkCount() => atlas.MarkCount;

    /// <summary>How many things the marks outline: marks close to one another are vertices of one obstacle.</summary>
    internal int ObstacleCount() => atlas.Obstacles().Count;

    /// <summary>Whether every token names a place or a point 'x,y' on the map — what a list of stops must be made of.</summary>
    internal bool AreStops(string[] stops)
    {
        if (stops == null || stops.Length == 0) return false;
        foreach (var token in stops) if (TryStop(token) == null) return false;
        return true;
    }

    // ---- missions: the entrusting ----

    /// <summary>The operator sends the golem to a point. A repeated or spent handle is a caller bug.</summary>
    internal int MoveTo(int id, double x, double y) => Entrust(id, new[] { new Waypoint(x, y) }, following: false, choosesOrder: false);

    /// <summary>The operator sends the golem to a place: its center.</summary>
    internal int MoveTo(int id, string place) => Entrust(id, new[] { atlas.PlaceNamed(place).Center }, following: false, choosesOrder: false);

    /// <summary>The operator sends the golem through several stops, in this order. A stop is a place name or a point 'x,y'.</summary>
    internal int MoveTo(int id, string[] stops) => Entrust(id, Stops(stops), following: false, choosesOrder: false);

    /// <summary>The operator sends the golem through several stops and lets it choose the order that makes the road shortest.</summary>
    internal int Cover(int id, string[] stops) => Entrust(id, Stops(stops), following: false, choosesOrder: true);

    /// <summary>The golem follows its leader to a point a peer says it reached — a mission with a handle of its own.</summary>
    internal int Follow(double x, double y) => Entrust(NextHandle(), new[] { new Waypoint(x, y) }, following: true, choosesOrder: false);

    // ---- missions: the road ----

    /// <summary>The road the golem would walk from (x, y) through a mission's stops still ahead, as the journal writes it:
    /// "kitchen/north@4,9.5 > kitchen@2,9.5 > kitchen/north@4,9.5 > north/storage@7,9.5 > storage@9,9.5".
    /// For a Cover mission the stops come out in the order the golem chose. Marks are skirted ('around' legs)
    /// or, where the body would not fit past them, avoided by another road altogether.</summary>
    internal string Plan(int id, double x, double y)
    {
        var mission = Find(id);
        var from = new Waypoint(x, y);
        var ahead = mission.StopsAhead.ToList();
        var stops = mission.ChoosesOrder ? atlas.BestOrder(from, ahead, radius) : ahead;
        var legs = atlas.Road(from, stops, radius);
        return string.Join(" > ", legs.Select(l => $"{l.Name}@{Fmt(l.At.X)},{Fmt(l.At.Y)}"));
    }

    /// <summary>The golem decides its road for a mission (the plan as text, so the decision reads in the journal). Returns the number of legs.
    /// The text names doors, openings, detours and stops; how each door is crossed (straight in, straight out) is derived from the
    /// map again. A road is decided a second time only after a bump on it: the journal then reads "bumped, bumped, took another road".</summary>
    internal int Route(int id, string plan)
    {
        var legs = atlas.WithDoorCrossings(plan.Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseLeg)
            .ToList());
        Find(id).Route(legs);
        return legs.Count;
    }

    /// <summary>The body touched something the map does not hold, at (x, y), on this mission: a mark on the map of
    /// touches, and a reason to look for another way. Returns the mission id.</summary>
    internal int Bump(int id, double x, double y)
    {
        Find(id).Bump();
        atlas.AddMark(new Waypoint(x, y));
        return id;
    }

    /// <summary>A peer says a body touched something at (x, y): the golem learns the mark without the bruise. Returns how many marks it holds.</summary>
    internal int Learn(double x, double y) => atlas.AddMark(new Waypoint(x, y));

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
    /// <summary>The next leg's name: a passage to cross, or the place of the stop to reach.</summary>
    internal string NextPassage(int id) => Find(id).NextLeg.Name;
    internal bool NextIsStop(int id) => Find(id).NextLeg.IsStop;
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
        double road = 0;
        Waypoint previous = null;
        foreach (Mission m in missions)
        {
            if (!m.IsPending()) continue;
            foreach (var stop in m.StopsAhead)
            {
                if (previous != null) road += atlas.RoadLength(previous, stop, radius);
                previous = stop;
            }
        }
        return road;
    }

    /// <summary>Seconds to run the whole pending route at the body's speed, lingering at every told stop. Answerable with no parameters.</summary>
    internal double RouteSeconds() => RouteLength() / Speed() + FollowingCount() * linger;

    /// <summary>The road still ahead for a body standing at (x, y): to the first stop ahead through the passages, then the route.</summary>
    internal double DistanceLeft(double x, double y)
    {
        if (!HasPendingMission()) return 0;
        var first = NextPending().StopsAhead.FirstOrDefault();
        if (first == null) return RouteLength();
        return atlas.RoadLength(new Waypoint(x, y), first, radius) + RouteLength();
    }

    /// <summary>Seconds until every pending mission is done, for a body standing at (x, y), at the body's speed and with its lingers.</summary>
    internal double SecondsLeft(double x, double y) => DistanceLeft(x, y) / Speed() + FollowingCount() * linger;

    // ---- reads (guarded: consult HasPendingMission() first) ----

    internal int NextId() => NextPending().Id;
    /// <summary>Where the body should head now: the next leg of the road when routed, the first stop before that.</summary>
    internal double NextX() => NextPending().NextLeg.At.X;
    internal double NextY() => NextPending().NextLeg.At.Y;
    /// <summary>How the next leg is walked: line up at the approach, end at the exit. For a door they stand off the wall on
    /// either side (the body crosses it straight); for an opening or a stop they are the point itself.</summary>
    internal double NextApproachX() => NextPending().NextLeg.Approach.X;
    internal double NextApproachY() => NextPending().NextLeg.Approach.Y;
    internal double NextExitX() => NextPending().NextLeg.Exit.X;
    internal double NextExitY() => NextPending().NextLeg.Exit.Y;

    // ---- inside ----

    private int Entrust(int id, IReadOnlyList<Waypoint> stops, bool following, bool choosesOrder)
    {
        if (Knows(id)) throw new DomainException($"mission {id} already exists");
        if (id <= lastHandle) throw new DomainException($"handle {id} was already spent: handles are never reused");
        foreach (var stop in stops)
            if (atlas.PlaceCount > 0 && !atlas.IsOnMap(stop))
                throw new DomainException($"the point ({Fmt(stop.X)}, {Fmt(stop.Y)}) is nowhere on the map");
        missions.Add(new Mission(id, stops, following, choosesOrder));
        lastHandle = id;
        return id;
    }

    // A stop is a place name or a point 'x,y'; either way it must be on the map.
    private List<Waypoint> Stops(string[] tokens)
    {
        if (tokens == null || tokens.Length == 0) throw new DomainException("a mission needs at least one stop");
        var stops = new List<Waypoint>();
        foreach (var token in tokens)
            stops.Add(TryStop(token) ?? throw new DomainException($"'{token}' is neither a place nor a point x,y on the map"));
        return stops;
    }

    private Waypoint TryStop(string token)
    {
        if (token == null) return null;
        token = token.Trim();
        if (atlas.Knows(token)) return atlas.PlaceNamed(token).Center;
        var xy = token.Split(',');
        if (xy.Length == 2
            && double.TryParse(xy[0].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
            && double.TryParse(xy[1].Trim(), NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
        {
            var at = new Waypoint(x, y);
            return atlas.IsOnMap(at) ? at : null;
        }
        return null;
    }

    private static Leg ParseLeg(string token)
    {
        int at = token.LastIndexOf('@');
        if (at <= 0) throw new DomainException($"a leg reads name@x,y — not '{token}'");
        var xy = token[(at + 1)..].Split(',');
        if (xy.Length != 2) throw new DomainException($"a leg reads name@x,y — not '{token}'");
        return new Leg(new Waypoint(
            double.Parse(xy[0], CultureInfo.InvariantCulture),
            double.Parse(xy[1], CultureInfo.InvariantCulture)), token[..at]);
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
