using GolemDomain.Attention;
using GolemDomain.Units;

namespace GolemDomain.Formations;

/// <summary>
/// THE INQUIRY (propuesta 121, 21-oct-2026): the commitment a formation that starts AFTER another arms in its asker's golem — "ask that golem
/// whether that formation finished, every so often, until the answer is yes" — the pull of propuesta 118 as a <see cref="Commitment"/>
/// instead of a question on every tick. Kept, it asks (<see cref="Formation.Ask"/>: the act the reaction tells <c>DidYouFinish</c> on);
/// desisted by the formation when the answer came (<see cref="Formation.TakeCue"/>) or it was dissolved.
/// </summary>
internal sealed class Inquiry : Commitment
{
    private readonly Formation formation;

    internal Inquiry(Formation formation, Duration every)
        : base(formation == null ? throw new GolemDomainException("Inquiry.Inquiry: 'formation' was not given") : formation.Name,
               $"ask {formation.AskTo} whether {formation.After} finished", every)
    {
        this.formation = formation;
    }

    /// <summary>The formation that asks.</summary>
    internal Formation Formation => formation;

    protected override void Keep() => formation.Ask();
}
