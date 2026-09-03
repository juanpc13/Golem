namespace GolemHost.Domain;

/// <summary>A task entrusted to the golem: where it was told to go, who ordered it, and how it ended.</summary>
internal sealed class Mission
{
    internal int Id { get; }
    internal Waypoint At { get; }
    /// <summary>True when the point came from a peer's tell (following), false when the operator ordered it.</summary>
    internal bool Told { get; }
    /// <summary>True once the golem has announced this visited point to its peer.</summary>
    internal bool Announced { get; private set; }

    private MissionStatus status = MissionStatus.Pending;
    private string reason = "";

    internal Mission(int id, Waypoint at, bool told)
    {
        Id = id;
        At = at;
        Told = told;
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

    internal void Announce()
    {
        if (status != MissionStatus.Completed) throw new DomainException($"mission {Id} is {status.Name}: only a completed point is announced");
        Announced = true;
    }

    private void MustBePending()
    {
        if (!IsPending())
            throw new DomainException($"mission {Id} is already {status.Name}");
    }
}
