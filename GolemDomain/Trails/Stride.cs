using GolemDomain.Geometry;

namespace GolemDomain.Trails;

/// <summary>A STRIDE of the trail (propuesta 120, 20-oct-2026): the body walked from one pose to another — a fact, as the acts
/// brought it (a turn or a move reported, a touch, a hold). Its length is what the chassis counted.</summary>
internal sealed class Stride
{
    internal Pose From { get; }
    internal Pose To { get; }
    internal double Length => From.DistanceTo(To);

    internal Stride(Pose from, Pose to)
    {
        if (from == null) throw new GolemDomainException("Stride: 'from' was not given");
        if (to == null) throw new GolemDomainException("Stride: 'to' was not given");
        if (ReferenceEquals(from, to)) throw new GolemDomainException("Stride: 'from' and 'to' are the same pose");
        From = from;
        To = to;
    }
}
