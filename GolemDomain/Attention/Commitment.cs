using GolemDomain.Units;

namespace GolemDomain.Attention;

/// <summary>
/// A COMMITMENT THE GOLEM MADE TO ITSELF (propuesta 121, 21-oct-2026; paper 0B — "a Puppet can claim future attention" — and its lab
/// <c>rung0B</c>: <c>commit_to_self</c>, <c>pending</c>, <c>attend</c>, <c>desist</c>): inside an act, the subject arms a promise to do something
/// LATER, again and again EVERY so often, until it desists. What it is ABOUT (<see cref="About"/>: the formation's name, the occasion every
/// activation names), WHAT it keeps doing (<see cref="What"/>), how often (<see cref="Every"/>), how many times it was kept
/// (<see cref="Activations"/>) and when last (<see cref="LastKept"/>). Nothing of it is a timer: the host's clock is the ATTENTION that asks
/// the module what is DUE now and writes the activation — <c>commitment.Attend(@now)</c> — which the frontier admits or refuses; before it is
/// due, nothing is written. The lab's schedule (a first due time plus the period times the activations) is here "every so often SINCE THE
/// LAST ONE KEPT", the first at once: the golem's acts have no clock of their own, the time enters as a parameter of the act.
/// What the activation DOES is the subclass's (<see cref="Keep"/>): the formations' inquiry asks a golem whether a formation finished.
/// </summary>
internal abstract class Commitment
{
    private int activations;
    private DateTime? lastKept;
    private string desistedWhy;

    protected Commitment(string about, string what, Duration every)
    {
        if (string.IsNullOrWhiteSpace(about)) throw new GolemDomainException("Commitment.Commitment: a commitment is about something named");
        if (string.IsNullOrWhiteSpace(what)) throw new GolemDomainException("Commitment.Commitment: a commitment says what it keeps doing");
        if (every == null) throw new GolemDomainException("Commitment.Commitment: 'every' was not given");
        if (every.IsZero) throw new GolemDomainException("a commitment is kept every so often: zero is no period");
        About = about.Trim().ToLowerInvariant();
        What = what.Trim();
        Every = every;
    }

    /// <summary>What the commitment is about — the occasion its activations name (a formation's name).</summary>
    internal string About { get; }
    /// <summary>What it keeps doing, in words ("ask red whether tri-left finished").</summary>
    internal string What { get; }
    /// <summary>How often it is kept.</summary>
    internal Duration Every { get; }
    /// <summary>How many times it was kept, and when last (null before the first).</summary>
    internal int Activations => activations;
    internal DateTime? LastKept => lastKept;
    /// <summary>Whether the golem desisted, and why ("" while open).</summary>
    internal bool Desisted => desistedWhy != null;
    internal string DesistedWhy => desistedWhy ?? "";
    /// <summary>Open: not desisted — the attention still looks at it.</summary>
    internal bool IsOpen => desistedWhy == null;

    /// <summary>When it is due next: the last one kept plus the period; at once before the first.</summary>
    internal DateTime? NextDue => lastKept?.Add(TimeSpan.FromSeconds(Every.InSeconds));

    /// <summary>Whether it is DUE at that moment: open, and the period passed since it was last kept (or never kept).</summary>
    internal bool IsDue(DateTime now) => IsOpen && (lastKept == null || now - lastKept.Value >= TimeSpan.FromSeconds(Every.InSeconds));

    /// <summary>THE ATTENTION KEEPS IT — <c>commitment.Attend(@now)</c>: the activation, an act that names this commitment; refused when it is
    /// not open or not due yet (nothing written). What it does is the subclass's.</summary>
    internal void Attend(DateTime now)
    {
        if (!IsOpen) throw new GolemDomainException($"the commitment about {About} was desisted: {desistedWhy}");
        if (!IsDue(now)) throw new GolemDomainException($"the commitment about {About} is not due yet: kept {Fmt(lastKept)}, every {Every.InSeconds:0.#} s");
        Keep();
        activations++;
        lastKept = now;
    }

    /// <summary>THE GOLEM DESISTS — the commitment is closed for that reason; the decision is an act like the arming was. Refused when closed already.</summary>
    internal void Desist(string why)
    {
        if (string.IsNullOrWhiteSpace(why)) throw new GolemDomainException("Commitment.Desist: a desisting says why");
        if (!IsOpen) throw new GolemDomainException($"the commitment about {About} was desisted already: {desistedWhy}");
        desistedWhy = why.Trim();
    }

    /// <summary>What one activation does.</summary>
    protected abstract void Keep();

    private static string Fmt(DateTime? at) => at?.ToString("HH:mm:ss.fff", System.Globalization.CultureInfo.InvariantCulture) ?? "never";
}
