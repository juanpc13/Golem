namespace GolemDomain;

/// <summary>A contract violation inside the golem's domain — "this should never happen".</summary>
internal sealed class GolemDomainException : Exception
{
    internal GolemDomainException(string message) : base(message) { }
}
