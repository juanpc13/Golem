using GolemDomain.Geometry;

namespace GolemDomain.Formations;

/// <summary>
/// A DISPUTE over a place, by distance (14-oct-2026; Juan: "las disputas que ha tenido el golem para resolver una posición con otro golem y el
/// resultado de si éste cedió o la ganó él, por la política de distance"): DERIVED from the words of the round and the law, never written — a
/// golem disputes the place NEAREST to where it said it stood; when the law gives that place to another, it YIELDED to the one who holds it;
/// when another wanted the place the law gave this golem, this golem WON it. Its formation, the place (its number and where it is), the rival,
/// the outcome for this golem, and both distances to the place, so the warden sees why.
/// </summary>
internal sealed class Dispute
{
    internal string Called { get; }
    internal int Place { get; }
    internal Position At { get; }
    internal string Rival { get; }
    internal bool Won { get; }
    internal double Mine { get; }
    internal double Theirs { get; }
    /// <summary>How it was decided when the policy plays (propuesta 115): the throw that won and the one it beat, and the ties before — "" by distance.</summary>
    internal string Game { get; }

    internal Dispute(string called, int place, Position at, string rival, bool won, double mine, double theirs) : this(called, place, at, rival, won, mine, theirs, "") { }

    internal Dispute(string called, int place, Position at, string rival, bool won, double mine, double theirs, string game)
    {
        if (called == null) throw new GolemDomainException("Dispute.Dispute: 'called' was not given");
        if (at == null) throw new GolemDomainException("Dispute.Dispute: 'at' was not given");
        if (rival == null) throw new GolemDomainException("Dispute.Dispute: 'rival' was not given");
        Called = called;
        Place = place;
        At = at;
        Rival = rival;
        Won = won;
        Mine = mine;
        Theirs = theirs;
        Game = game ?? "";
    }
}
