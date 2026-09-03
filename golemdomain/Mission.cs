namespace GolemHost.Domain;

/// <summary>A task entrusted to the golem: where it was told to go, and how it ended.</summary>
internal sealed class Mission
{
    internal int Id { get; }
    internal Waypoint At { get; }

    private MissionStatus status = MissionStatus.Pending;
    private string reason = "";

    internal Mission(int id, Waypoint at)
    {
        Id = id;
        At = at;
    }

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

    private void MustBePending()
    {
        if (!IsPending())
            throw new DomainException($"mission {Id} is already {status.Name}");
    }
}
