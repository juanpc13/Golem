using GolemDomain.Geometry;

namespace GolemDomain.Touches;

/// <summary>
/// A bump a peer told about: who bumped, where the touch was, and where that peer stood when it told. A fact of
/// the collisions module, kept for two reasons: a touch of my own there and then is known to be that peer, and
/// once we know we met, its position is what lets each of us step out of the other's way instead of shoving.
/// </summary>
internal sealed class HeardBump
{
    internal string Who { get; }
    internal Position At { get; }
    /// <summary>Where the peer stood when it told — its own position, not the point it touched.</summary>
    internal Position PeerAt { get; }

    internal HeardBump(string who, Position at, Position peerAt)
    {
        if (string.IsNullOrWhiteSpace(who)) throw new GolemDomainException("a heard bump needs to say who bumped");
        Who = who;
        At = at ?? throw new GolemDomainException("a heard bump needs to say where");
        PeerAt = peerAt ?? throw new GolemDomainException("a heard bump needs to say where the peer stood");
    }
}
