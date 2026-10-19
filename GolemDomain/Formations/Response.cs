namespace GolemDomain.Formations;

/// <summary>
/// THE ANSWER TO A QUESTION (propuesta 118, 20-oct-2026): a golem asked whether a formation finished (<c>DidYouFinish</c>), and this golem answers
/// from its own copy — <c>response = g.Choreography.Answer();</c>: which formation (<see cref="Call"/>), whom to answer (<see cref="To"/>), whether
/// it finished as this copy knows it (<see cref="Done"/>), and the number of this answer (<see cref="Number"/>: the once of the tell that carries
/// it). A datum, nothing else; the reaction on the act tells <c>FinishedState</c>.
/// </summary>
internal sealed class Response
{
    internal string Call { get; }
    internal string To { get; }
    internal bool Done { get; }
    internal int Number { get; }

    internal Response(string call, string to, bool done, int number)
    {
        if (string.IsNullOrWhiteSpace(call)) throw new GolemDomainException("Response.Response: 'call' was not given");
        if (string.IsNullOrWhiteSpace(to)) throw new GolemDomainException("Response.Response: 'to' was not given");
        Call = call; To = to; Done = done; Number = number;
    }
}
