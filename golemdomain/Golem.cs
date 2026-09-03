namespace GolemHost.Domain;

/// <summary>
/// The golem: the executor that is entrusted missions, carries them out in its world, and keeps
/// how each one ended. Aggregate root of its journal; refuses a repeated handle.
/// </summary>
internal sealed class Golem
{
    private readonly List<Mission> missions = new();
    private int lastHandle;   // a handle names one mission forever — even after letting go (idempotency keys hang on it)
    private World world;

    internal Golem() { }

    // ---- releases (issued by upgrade blocks) ----

    /// <summary>Gives the golem its world. Returns the world size.</summary>
    internal double Inhabit(double size, double margin)
    {
        world = new World(size, margin);
        return size;
    }

    /// <summary>Places the rock in the world. Returns its radius.</summary>
    internal double PlaceRock(double x, double y, double r)
    {
        Home().PlaceRock(x, y, r);
        return r;
    }

    // ---- missions ----

    /// <summary>Entrusts a mission under its handle. A repeated handle is a caller bug.</summary>
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

    /// <summary>The alternate route: a journaled decision. The ordered point stays on record.</summary>
    internal int Reroute(int id, double x, double y, string reason)
    {
        Mission mission = Find(id);
        mission.Reroute(new Waypoint(x, y), reason);
        return mission.Reroutes;
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
    internal int Reroutes(int id) => Find(id).Reroutes;
    internal string StatusOf(int id) => Find(id).ReadStatus();

    // ---- reads (guarded: consult HasPendingMission() first) ----

    internal int NextId() => NextPending().Id;
    internal double NextX() => NextPending().Target.X;
    internal double NextY() => NextPending().Target.Y;

    // ---- the world, as wire-shaped answers ----

    internal bool CanStandAt(double x, double y) => Home().CanStandAt(new Waypoint(x, y));
    internal double NearestStandableX(double x, double y) => Home().NearestStandableTo(new Waypoint(x, y)).X;
    internal double NearestStandableY(double x, double y) => Home().NearestStandableTo(new Waypoint(x, y)).Y;
    internal bool RunCrossesRock(double fromX, double fromY, double toX, double toY) =>
        Home().RunCrossesRock(new Waypoint(fromX, fromY), new Waypoint(toX, toY));
    internal double DetourX(double fromX, double fromY, double toX, double toY) =>
        Home().DetourAround(new Waypoint(fromX, fromY), new Waypoint(toX, toY)).X;
    internal double DetourY(double fromX, double fromY, double toX, double toY) =>
        Home().DetourAround(new Waypoint(fromX, fromY), new Waypoint(toX, toY)).Y;
    internal double WorldSize() => Home().Size;
    internal double WallMargin() => Home().Margin;
    internal bool HasRock() => Home().Rock.Exists();
    internal double RockX() => Home().Rock.X;
    internal double RockY() => Home().Rock.Y;
    internal double RockRadius() => Home().Rock.Radius;

    private World Home()
    {
        if (world == null) throw new DomainException("the golem has no world yet: the world release must run first");
        return world;
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
