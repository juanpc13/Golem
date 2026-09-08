namespace GolemHost.Domain.Robots;

/// <summary>The life of a mission: a closed set, one instance per member, compared by identity.</summary>
internal sealed class MissionStatus
{
    internal static readonly MissionStatus Pending   = new("pending");
    internal static readonly MissionStatus Completed = new("completed");   // the last stop was reached
    internal static readonly MissionStatus Failed    = new("failed");      // the world said no: a collision, a stall, no road
    internal static readonly MissionStatus Abandoned = new("abandoned");   // the golem let it go: a newer told point, or the operator

    internal string Name { get; }

    private MissionStatus(string name) => Name = name;
}
