namespace GolemDomain.Routes;

/// <summary>The life of a route: a closed set, one instance per member, compared by identity.</summary>
internal sealed class RouteStatus
{
    internal static readonly RouteStatus Pending   = new("pending");
    internal static readonly RouteStatus Completed = new("completed");   // the last stop was reached
    internal static readonly RouteStatus Failed    = new("failed");      // the world said no: a collision, a stall, no way
    internal static readonly RouteStatus Abandoned = new("abandoned");   // the golem let it go: a newer told point, or the operator

    internal string Name { get; }

    private RouteStatus(string name) => Name = name;
}
