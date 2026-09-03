namespace GolemHost.Domain;

/// <summary>A task entrusted to the golem: where it was told to go, where it aims now, and how it ended.</summary>
internal sealed class Mission
{
    internal int Id { get; }
    internal Waypoint Ordered { get; }
    internal int Reroutes { get; private set; }

    private Waypoint target;
    private MissionStatus status = MissionStatus.Pending;
    private string reason = "";

    internal Mission(int id, Waypoint at)
    {
        Id = id;
        Ordered = at;
        target = at;
    }

    internal Waypoint Target => target;

    internal bool IsPending() => status == MissionStatus.Pending;
    internal string ReadStatus() => status.Name;
    internal string ReadReason() => reason;

    internal void Complete()
    {
        MustBePending();
        status = MissionStatus.Completed;
    }

    internal void Fail(string why)
    {
        MustBePending();
        status = MissionStatus.Failed;
        reason = why;
    }

    internal void Reroute(Waypoint to, string why)
    {
        MustBePending();
        target = to;
        Reroutes++;
        reason = why;
    }

    private void MustBePending()
    {
        if (!IsPending())
            throw new DomainException($"mission {Id} is already {status.Name}");
    }
}
