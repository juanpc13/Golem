using System.Reflection;

namespace GolemDomain;

/// <summary>The one thing a host needs from this assembly: a handle to load it as the actor's library.</summary>
public static class DomainLibrary
{
    public static Assembly Assembly => typeof(Golem).Assembly;
}
