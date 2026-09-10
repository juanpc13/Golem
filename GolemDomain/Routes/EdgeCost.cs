using GolemHost.Domain.Geometry;

namespace GolemHost.Domain.Routes;

/// <summary>
/// What an edge of the road graph costs: generic by design (time, risk, energy could come), the distance
/// as the crow flies in the first designs. The planner minimizes the sum of these.
/// </summary>
internal abstract class EdgeCost
{
    internal abstract string Name { get; }
    internal abstract double Between(Position a, Position b);
}

/// <summary>The cost of an edge is its length.</summary>
internal sealed class DistanceCost : EdgeCost
{
    internal override string Name => "distance";
    internal override double Between(Position a, Position b) => a.DistanceTo(b);
}
