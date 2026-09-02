namespace GolemHost.Domain;

// The golem's domain: plain POCOs. Their public methods are the DSL verbs.
// Only transitions (assign, complete, fail) reach the journal — never the pose.
public class Golem
{
    private readonly List<Mission> missions = new();

    // A mission is born with its handle. Journaling the id at Assign is what lets a
    // Reaction correlate "this mission was ordered" with "this same mission completed".
    public Mission Assign(int id, double x, double y)
    {
        if (missions.Any(m => m.Id == id))
            throw new InvalidOperationException($"mission {id} already exists");
        var mission = new Mission(id, x, y);
        missions.Add(mission);
        return mission;
    }

    // A mission ordered by a peer's tell carries no handle: it takes the next one.
    public Mission Assign(double x, double y) => Assign(NextHandle(), x, y);

    public int NextHandle() => missions.Count == 0 ? 1 : missions.Max(m => m.Id) + 1;

    // Verbs a Reaction observes must yield a value: the reaction resolver reads the
    // journaled call as an expression and cannot see void methods.
    public Mission Complete(int id)
    {
        var mission = Find(id);
        mission.Status = "completed";
        return mission;
    }

    public Mission Fail(int id, string reason)
    {
        var mission = Find(id);
        mission.Status = "failed";
        mission.Reason = reason;
        return mission;
    }

    public int NextId()
    {
        var next = NextPending();
        return next == null ? -1 : next.Id;
    }

    public double NextX() => NextPending()?.X ?? 0;
    public double NextY() => NextPending()?.Y ?? 0;
    public int Pending() => missions.Count(m => m.Status == "pending");
    public int Total() => missions.Count;

    private Mission NextPending() => missions.FirstOrDefault(m => m.Status == "pending");
    private Mission Find(int id) => missions.First(m => m.Id == id);
}
