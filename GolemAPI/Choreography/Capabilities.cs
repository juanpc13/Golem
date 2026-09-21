namespace GolemAPI.Choreography;

// THE ROBOT'S CAPABILITIES — which ROLES the golem may play with this body (Juan, 18-sep-2026: "en base a las capacidades
// del robot: los motores (Displacer), el botón del chasis que dice si tocamos algo (CollisionCaptor)… eventualmente un
// escáner con lidar, articulaciones de brazos; hablan de las capacidades del robot a las que este tiene acceso"). Declared
// by the operator in the golem's configuration — the compose, `ROLES=displacer,collision-captor` (Juan: "la configuración
// del golem en el compose") — and read once at boot: the embodiment builds ONLY the roles declared, and an endpoint whose
// role the body lacks refuses (409) instead of pretending. No declaration keeps every role the spike has, so a golem
// configured before this day boots as it did.
public sealed class Capabilities
{
    /// <summary>The motors: the body displaces itself — the errand, the hold, the turn and the move reported.</summary>
    public const string Displacer = "displacer";
    /// <summary>The bumper: the body says it touched something — the bump, and the operator's forget of what it learned.</summary>
    public const string CollisionCaptor = "collision-captor";

    /// <summary>Every role this host knows how to play, in the order they are listed.</summary>
    public static readonly IReadOnlyList<string> Known = new[] { Displacer, CollisionCaptor };

    private readonly List<string> roles;

    private Capabilities(List<string> roles) => this.roles = roles;

    /// <summary>The roles declared — <c>"displacer,collision-captor"</c>, case and spaces forgiven; null or empty declares them
    /// all. A role this host does not know is refused with the list it does.</summary>
    public static Capabilities Parse(string declared)
    {
        if (string.IsNullOrWhiteSpace(declared)) return new Capabilities(Known.ToList());
        var roles = new List<string>();
        foreach (var token in declared.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            string role = token.ToLowerInvariant().Replace('_', '-');
            if (!Known.Contains(role)) throw new ArgumentException($"ROLES names '{token}', a role this golem cannot play; it knows: {string.Join(", ", Known)}");
            if (!roles.Contains(role)) roles.Add(role);
        }
        return new Capabilities(roles);
    }

    /// <summary>Whether the body declared this role.</summary>
    public bool Has(string role) => roles.Contains(role);

    /// <summary>The roles declared, for the log and the panel.</summary>
    public override string ToString() => string.Join(", ", roles);
}
