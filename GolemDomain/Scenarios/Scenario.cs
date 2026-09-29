using GolemDomain.Layouts;
using GolemDomain.Touches;

namespace GolemDomain.Scenarios;

/// <summary>
/// A SCENARIO the golem may be in (ajuste 60, 29-sep-2026; Juan: "una clase wrapper que tenga el mapa y el objeto de colisiones
/// asociados a ese mapa… así podemos cambiarle al robot el mapa donde se encuentra"): a map laid out, and what the bodies learned
/// by touching IN that map — its own collisions, empty when the scenario is new. The golem knows several and is in one at a time
/// (<c>g.Enter(scenario)</c>); every way it decides, it decides on the one it is in. Named after its map: <c>Scenario(map, Collisions())</c>.
/// </summary>
internal sealed class Scenario
{
    internal MapLayout Map { get; }
    internal Collisions Collisions { get; }
    internal string Name => Map.Name;

    internal Scenario(MapLayout map, Collisions collisions)
    {
        if (map == null) throw new GolemDomainException("a scenario needs its map, laid out");
        if (collisions == null) throw new GolemDomainException("a scenario needs its collisions module, even empty");
        Map = map;
        Collisions = collisions;
    }
}
