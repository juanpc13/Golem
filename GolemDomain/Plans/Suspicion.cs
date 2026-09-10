namespace GolemHost.Domain.Plans;

/// <summary>
/// What the golem suspects its body touched — the hypothesis the domain forms from its own facts, so that the
/// host never reasons: a wall it knows (its own execution error), a peer that said it bumped there and then, or
/// a thing nobody charted. Each variant names the conclusion the golem then writes in its journal, in its own
/// voice: Graze, Met or Mark. (Provisional name, Juan 8-sep: may be renamed later.)
/// </summary>
internal abstract class Suspicion
{
    /// <summary>wall, peer or thing.</summary>
    internal abstract string Kind { get; }
    /// <summary>The verb the golem concludes with: Graze, Met or Mark.</summary>
    internal abstract string Conclusion { get; }
    /// <summary>Who, when a peer was met; "" otherwise.</summary>
    internal virtual string Who => "";
}

/// <summary>The point lies on a wall the plan knows, outside its doorways: the body grazed it — its own error, not a discovery.</summary>
internal sealed class WallTouched : Suspicion
{
    internal override string Kind => "wall";
    internal override string Conclusion => "Graze";
}

/// <summary>A peer told it bumped within a body's reach of the point since the leg began: it was that peer.</summary>
internal sealed class PeerMet : Suspicion
{
    private readonly string who;
    internal PeerMet(string who)
    {
        if (string.IsNullOrWhiteSpace(who)) throw new DomainException("meeting a peer needs its name");
        this.who = who;
    }
    internal override string Kind => "peer";
    internal override string Conclusion => "Met";
    internal override string Who => who;
}

/// <summary>Nobody was there and no wall is: a thing the plan does not hold.</summary>
internal sealed class ThingFound : Suspicion
{
    internal override string Kind => "thing";
    internal override string Conclusion => "Mark";
}
