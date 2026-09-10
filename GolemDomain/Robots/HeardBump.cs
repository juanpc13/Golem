using GolemHost.Domain.Geometry;

namespace GolemHost.Domain.Robots;

/// <summary>
/// A bump a peer told about: who bumped, where the touch was, and where that peer stood when it told. Kept for
/// two reasons: a touch of my own there and then is known to be that peer, and once we know we met, its
/// position is what lets each of us step out of the other's way instead of shoving.
/// </summary>
internal sealed class HeardBump
{
    internal string Who { get; }
    internal Position At { get; }
    /// <summary>Where the peer stood when it told — its own position, not the point it touched.</summary>
    internal Position PeerAt { get; }

    internal HeardBump(string who, Position at, Position peerAt)
    {
        if (string.IsNullOrWhiteSpace(who)) throw new DomainException("a heard bump needs to say who bumped");
        if (at == null) throw new DomainException("a heard bump needs to say where");
        if (peerAt == null) throw new DomainException("a heard bump needs to say where the peer stood");
        Who = who;
        At = at;
        PeerAt = peerAt;
    }
}
