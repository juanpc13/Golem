namespace GolemHost;

// The golem's domain: plain POCOs. Their public methods are the DSL verbs.
// Only transitions (assign, complete, fail) reach the journal — never the pose.
public class Golem
{
    private readonly List<Mission> missions = new();

    public Mission Assign(double x, double y)
    {
        var mission = new Mission(missions.Count + 1, x, y);
        missions.Add(mission);
        return mission;
    }

    public void Complete(int id) => Find(id).Status = "completed";

    public void Fail(int id, string reason)
    {
        var mission = Find(id);
        mission.Status = "failed";
        mission.Reason = reason;
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
