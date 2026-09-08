using GolemHost.Domain.Geometry;

namespace GolemHost.Domain.Robots;

/// <summary>A bump a peer told about: who bumped, and where. Kept so a touch of my own there and then is known to be that peer.</summary>
internal sealed class HeardBump
{
    internal string Who { get; }
    internal Position At { get; }

    internal HeardBump(string who, Position at)
    {
        if (string.IsNullOrWhiteSpace(who)) throw new DomainException("a heard bump needs to say who bumped");
        if (at == null) throw new DomainException("a heard bump needs to say where");
        Who = who;
        At = at;
    }
}
