namespace GolemHost.Domain;

/// <summary>
/// The golem: the executor that is entrusted missions, carries them out, and keeps how
/// each one ended. Aggregate root of its journal; refuses a repeated handle.
/// </summary>
internal sealed class Golem
{
    private readonly List<Mission> missions = new();
    private int lastHandle;   // a handle names one mission forever — even after letting go (idempotency keys hang on it)

    internal Golem() { }

    // ---- missions ----

    /// <summary>Entrusts a mission under its handle. A repeated or spent handle is a caller bug.</summary>
    internal int Assign(int id, double x, double y)
    {
        if (Knows(id)) throw new DomainException($"mission {id} already exists");
        if (id <= lastHandle) throw new DomainException($"handle {id} was already spent: handles are never reused");
        missions.Add(new Mission(id, new Waypoint(x, y)));
        lastHandle = id;
        return id;
    }

    /// <summary>Takes up a point a peer says it visited, as a mission of its own.</summary>
    internal int Take(double x, double y) => Assign(NextHandle(), x, y);

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
    internal bool HasPendingMission() => missions.Any(m => m.IsPending());
    internal int Pending() => missions.Count(m => m.IsPending());
    internal int Total() => missions.Count;
    internal string StatusOf(int id) => Find(id).ReadStatus();

    // ---- reads (guarded: consult HasPendingMission() first) ----

    internal int NextId() => NextPending().Id;
    internal double NextX() => NextPending().At.X;
    internal double NextY() => NextPending().At.Y;

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
