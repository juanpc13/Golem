namespace GolemHost.Domain;

/// <summary>The life of a mission: a closed set, one instance per member, compared by identity.</summary>
internal sealed class MissionStatus
{
    internal static readonly MissionStatus Pending   = new("pending");
    internal static readonly MissionStatus Completed = new("completed");
    internal static readonly MissionStatus Failed    = new("failed");
    internal static readonly MissionStatus Superseded = new("superseded");   // a told point made obsolete by a newer one

    internal string Name { get; }

    private MissionStatus(string name) => Name = name;
}
