using System.Reflection;

namespace GolemHost.Domain;

/// <summary>The one thing a host needs from this assembly: a handle to load it as the actor's library.</summary>
public static class GolemDomain
{
    public static Assembly Assembly => typeof(Golem).Assembly;
}
