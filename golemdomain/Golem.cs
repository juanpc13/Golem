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
    private int lastHandle;   // a handle names one mission forever — even after letting go (idempotency keys hang on it)
    private double speed;     // the body's cruise speed, units/s — a property released into the journal
    private double holdAfterTold; // seconds the golem pauses at every point a peer told it about

    internal Golem() { }

    // ---- the body (issued by upgrade releases) ----

    /// <summary>Gives the golem its body's cruise speed, in world units per second. Returns it.</summary>
    internal double Embody(double unitsPerSecond)
    {
        if (unitsPerSecond <= 0) throw new DomainException("a body needs a speed above zero");
        speed = unitsPerSecond;
        return speed;
    }

    /// <summary>Sets how long the golem pauses at every point a peer told it about — the follower's pacing. Returns it.</summary>
    internal double Pace(double holdSeconds)
    {
        if (holdSeconds < 0) throw new DomainException("a hold cannot be negative");
        holdAfterTold = holdSeconds;
        return holdAfterTold;
    }

    internal double Speed()
    {
        if (speed <= 0) throw new DomainException("the golem has no body speed yet: the body release must run first");
        return speed;
    }

    internal double HoldAfterTold() => holdAfterTold;

    // ---- the map (issued by upgrade releases, one fluent chain per place) ----

    /// <summary>A room of the map. Chain its passages: <c>g.AddPlace('kitchen', 0, 6, 4, 5).Door('hall', 4, 8.5)</c>.</summary>
    internal Place AddPlace(string name, double x, double y, double width, double height) => atlas.AddPlace(name, x, y, width, height);

    internal int Places() => atlas.PlaceCount;
    internal int Passages() => atlas.PassageCount;
    internal bool KnowsPlace(string name) => atlas.Knows(name);
    internal bool IsOnMap(double x, double y) => atlas.IsOnMap(new Waypoint(x, y));
    internal string PlaceAt(double x, double y) => atlas.PlaceAt(new Waypoint(x, y)).Name;
    internal string DescribeMap() => atlas.Describe();

    /// <summary>The shortest road between two places, center to center, through the passages.</summary>
    internal double Distance(string from, string to) => atlas.RoadLength(atlas.PlaceNamed(from).Center, atlas.PlaceNamed(to).Center);

    // ---- missions ----

    /// <summary>Entrusts a mission to a point. A repeated or spent handle is a caller bug.</summary>
    internal int Assign(int id, double x, double y) => Entrust(id, new Waypoint(x, y), told: false);

    /// <summary>Entrusts a mission to a place: its center.</summary>
    internal int AssignPlace(int id, string place) => Entrust(id, atlas.PlaceNamed(place).Center, told: false);

    /// <summary>Assigns itself a point a peer says it visited — a told mission, with a handle of its own.</summary>
    internal int AssignTold(double x, double y) => Entrust(NextHandle(), new Waypoint(x, y), told: true);

    /// <summary>Assigns itself a place a peer says it visited — a told mission to the place's center.</summary>
    internal int AssignToldPlace(string place) => Entrust(NextHandle(), atlas.PlaceNamed(place).Center, told: true);

    /// <summary>The road the golem would walk from (x, y) to a mission's point, as the journal writes it: "kitchen/hall@4,8.5 > hall/garage@7,3 > garage@9,3".</summary>
    internal string Plan(int id, double x, double y)
    {
        var legs = atlas.Road(new Waypoint(x, y), Find(id).At);
        return string.Join(" > ", legs.Select(l => $"{l.Name}@{Fmt(l.At.X)},{Fmt(l.At.Y)}"));
    }

    /// <summary>The golem decides its road for a mission (the plan as text, so the decision reads in the journal). Returns the number of legs.</summary>
    internal int Route(int id, string plan)
    {
        var legs = plan.Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(ParseLeg)
            .ToList();
        Find(id).Route(legs);
        return legs.Count;
    }

    /// <summary>The golem crossed the next passage of its road. Returns the mission id.</summary>
    internal int Pass(int id, string passage)
    {
        Find(id).Pass(passage);
        return id;
    }

    internal int Complete(int id)
    {
        Find(id).Complete();
        return id;
    }

    internal int Fail(int id, string reason)
    {
        Find(id).Fail(reason);
        return id;
    }

    /// <summary>The golem puts on record that it announces this visited point to its peer (the tell follows in the same entry).</summary>
    internal int Announce(int id)
    {
        Find(id).Announce();
        return id;
    }

    /// <summary>The golem lets go of every mission and starts over — and that forgetting, and why, is on the record.</summary>
    internal int Retire(string reason)
    {
        if (string.IsNullOrWhiteSpace(reason)) throw new DomainException("letting go needs a reason");
        int retired = missions.Count;
        missions.Clear();
        return retired;
    }

    // ---- reads (total: never throw when nothing is there) ----

    internal int NextHandle() => lastHandle + 1;
    internal bool Knows(int id) => missions.Any(m => m.Id == id);
    internal bool IsPending(int id) => Find(id).IsPending();
    internal bool WasTold(int id) => Find(id).Told;
    internal bool WasAnnounced(int id) => Find(id).Announced;
    internal bool IsRouted(int id) => Find(id).IsRouted;
    internal int LegsLeft(int id) => Find(id).LegsLeft;
    internal string NextPassage(int id) => Find(id).NextLeg.Name;
    internal bool HasPendingMission() => missions.Any(m => m.IsPending());
    internal int Pending() => missions.Count(m => m.IsPending());
    internal int PendingTold() => missions.Count(m => m.IsPending() && m.Told);
    internal int Total() => missions.Count;
    internal string StatusOf(int id) => Find(id).ReadStatus();

    // ---- the road ahead, answered from the golem's own knowledge ----

    /// <summary>The road between the pending points, in the order they will run, through the map's passages. Zero with one point or none.</summary>
    internal double RouteLength()
    {
        double road = 0;
        Waypoint previous = null;
        foreach (Mission m in missions)
        {
            if (!m.IsPending()) continue;
            if (previous != null) road += atlas.RoadLength(previous, m.At);
            previous = m.At;
        }
        return road;
    }

    /// <summary>Seconds to run the whole pending route at the body's speed, pausing at every told point. Answerable with no parameters.</summary>
    internal double RouteSeconds() => RouteLength() / Speed() + PendingTold() * holdAfterTold;

    /// <summary>The road still ahead for a body standing at (x, y): to the first pending point through the passages, then the route.</summary>
    internal double DistanceLeft(double x, double y)
    {
        if (!HasPendingMission()) return 0;
        return atlas.RoadLength(new Waypoint(x, y), NextPending().At) + RouteLength();
    }

    /// <summary>Seconds until every pending mission is done, for a body standing at (x, y), at the body's speed and with its pauses.</summary>
    internal double SecondsLeft(double x, double y) => DistanceLeft(x, y) / Speed() + PendingTold() * holdAfterTold;

    // ---- reads (guarded: consult HasPendingMission() first) ----

    internal int NextId() => NextPending().Id;
    /// <summary>Where the body should head now: the next leg of the road when routed, the point itself before that.</summary>
    internal double NextX() => NextPending().NextLeg.At.X;
    internal double NextY() => NextPending().NextLeg.At.Y;

    private int Entrust(int id, Waypoint at, bool told)
    {
        if (Knows(id)) throw new DomainException($"mission {id} already exists");
        if (id <= lastHandle) throw new DomainException($"handle {id} was already spent: handles are never reused");
        if (atlas.PlaceCount > 0 && !atlas.IsOnMap(at))
            throw new DomainException($"the point ({Fmt(at.X)}, {Fmt(at.Y)}) is nowhere on the map");
        missions.Add(new Mission(id, at, told));
        lastHandle = id;
        return id;
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
