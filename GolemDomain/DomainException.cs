namespace GolemDomain;

/// <summary>A contract violation inside the golem's domain — "this should never happen".</summary>
internal sealed class DomainException : Exception
{
    internal DomainException(string message) : base(message) { }
}
