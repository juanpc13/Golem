namespace GolemDomain.Attention;

/// <summary>
/// THE GOLEM'S COMMITMENTS TO ITSELF (propuesta 121, 21-oct-2026; Juan: "g.Commitments entonces") — the module born with the golem, like its
/// collisions, choreographies, dynamics and exploration: every <see cref="Commitment"/> it armed, the open ones among them (<see cref="Pending"/>)
/// and the one DUE now (<see cref="Due"/>), what the host's clock — the ATTENTION — asks on every tick and keeps when it is time:
/// <c>commitment = g.Commitments.Due(@now); commitment.Attend(@now);</c>. ONE place knows what is due (the lab's <c>attend</c> walks the subject's
/// one frontier), and the pending ones are rebuilt by the replay of the acts that armed, kept and desisted them — the record is enough.
/// A commitment is armed by the module that makes it (a formation that starts after another arms the inquiry that asks), which also tells
/// it when to desist (the answer came, the formation was dissolved).
/// </summary>
internal sealed class Commitments
{
    private readonly Golem golem;
    private readonly List<Commitment> commitments = new();   // every commitment armed, the closed ones among them (history)

    internal Commitments(Golem golem)
    {
        this.golem = golem ?? throw new GolemDomainException("Commitments.Commitments: 'golem' was not given");
    }

    /// <summary>A COMMITMENT ARMED: kept until desisted. One open commitment per occasion and doing at a time: armed again, refused.</summary>
    internal void Commit(Commitment commitment)
    {
        if (commitment == null) throw new GolemDomainException("Commitments.Commit: 'commitment' was not given");
        if (commitments.Any(c => c.IsOpen && c.About == commitment.About && c.What == commitment.What))
            throw new GolemDomainException($"{golem.Name ?? "the golem"} is committed already about {commitment.About}: {commitment.What}");
        commitments.Add(commitment);
    }

    /// <summary>The open commitments, in the order armed.</summary>
    internal IReadOnlyList<Commitment> Pending() => commitments.Where(c => c.IsOpen).ToList();
    /// <summary>Every commitment armed, the closed ones too — history.</summary>
    internal IReadOnlyList<Commitment> All() => commitments;
    internal int PendingCount => commitments.Count(c => c.IsOpen);

    /// <summary>Whether something is due at that moment: the attention's question (<c>Check(g.Commitments.IsDue(@now))</c>).</summary>
    internal bool IsDue(DateTime now) => commitments.Any(c => c.IsDue(now));

    /// <summary>The first commitment due at that moment — the one the attention keeps now; refused when none is due.</summary>
    internal Commitment Due(DateTime now) => commitments.FirstOrDefault(c => c.IsDue(now)) ?? throw new GolemDomainException("nothing is due: no commitment to keep now");

    /// <summary>Whether an open commitment is about that occasion, and the one that is (refused when none).</summary>
    internal bool HasOpen(string about)
    {
        if (about == null) throw new GolemDomainException("Commitments.HasOpen: 'about' was not given");
        string called = about.Trim().ToLowerInvariant();
        return commitments.Any(c => c.IsOpen && c.About == called);
    }

    internal Commitment Open(string about)
    {
        if (about == null) throw new GolemDomainException("Commitments.Open: 'about' was not given");
        string called = about.Trim().ToLowerInvariant();
        return commitments.LastOrDefault(c => c.IsOpen && c.About == called) ?? throw new GolemDomainException($"no open commitment about '{about}'");
    }

    /// <summary>THE GOLEM DESISTS from every open commitment about that occasion, for that reason; nothing open, nothing done.</summary>
    internal void Desist(string about, string why)
    {
        if (about == null) throw new GolemDomainException("Commitments.Desist: 'about' was not given");
        string called = about.Trim().ToLowerInvariant();
        foreach (var c in commitments.Where(c => c.IsOpen && c.About == called).ToList()) c.Desist(why);
    }
}
