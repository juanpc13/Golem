namespace GolemHost.Domain;

/// <summary>
/// The golem: the executor that is entrusted missions, carries them out, and keeps how
/// each one ended. Aggregate root of its journal; refuses a repeated handle.
/// </summary>
internal sealed class Golem
{
    private readonly List<Mission> missions = new();
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

    // ---- missions ----

    /// <summary>Entrusts a mission under its handle. A repeated or spent handle is a caller bug.</summary>
    internal int Assign(int id, double x, double y) => Entrust(id, x, y, told: false);

    /// <summary>Assigns itself a point a peer says it visited — a told mission, with a handle of its own.</summary>
    internal int AssignTold(double x, double y) => Entrust(NextHandle(), x, y, told: true);

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
    internal bool HasPendingMission() => missions.Any(m => m.IsPending());
    internal int Pending() => missions.Count(m => m.IsPending());
    internal int PendingTold() => missions.Count(m => m.IsPending() && m.Told);
    internal int Total() => missions.Count;
    internal string StatusOf(int id) => Find(id).ReadStatus();

    // ---- the road ahead, answered from the golem's own knowledge ----

    /// <summary>The road between the pending points, from the first pending one onward, in the order they will run. Zero with one point or none.</summary>
    internal double RouteLength()
    {
        double road = 0;
        Waypoint previous = null;
        foreach (Mission m in missions)
        {
            if (!m.IsPending()) continue;
            if (previous != null) road += previous.DistanceTo(m.At);
            previous = m.At;
        }
        return road;
    }

    /// <summary>Seconds to run the whole pending route at the body's speed, pausing at every told point. Answerable with no parameters.</summary>
    internal double RouteSeconds() => RouteLength() / Speed() + PendingTold() * holdAfterTold;

    /// <summary>The road still ahead for a body standing at (x, y): to the first pending point, then the route. The pose is the caller's telemetry, never the golem's state.</summary>
    internal double DistanceLeft(double x, double y)
    {
        if (!HasPendingMission()) return 0;
        return new Waypoint(x, y).DistanceTo(NextPending().At) + RouteLength();
    }

    /// <summary>Seconds until every pending mission is done, for a body standing at (x, y), at the body's speed and with its pauses.</summary>
    internal double SecondsLeft(double x, double y) => DistanceLeft(x, y) / Speed() + PendingTold() * holdAfterTold;

    // ---- reads (guarded: consult HasPendingMission() first) ----

    internal int NextId() => NextPending().Id;
    internal double NextX() => NextPending().At.X;
    internal double NextY() => NextPending().At.Y;

    private int Entrust(int id, double x, double y, bool told)
    {
        if (Knows(id)) throw new DomainException($"mission {id} already exists");
        if (id <= lastHandle) throw new DomainException($"handle {id} was already spent: handles are never reused");
        missions.Add(new Mission(id, new Waypoint(x, y), told));
        lastHandle = id;
        return id;
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
}
