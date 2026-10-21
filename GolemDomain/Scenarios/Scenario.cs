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
    /// <summary>The scenario whose FRAME this one is read in (propuesta 120): itself for a map of a release; the scenario it was deduced in
    /// for a map the explorer exported — the body's pose is read in that frame, the world's region is that one's.</summary>
    internal string Frame { get; }
    /// <summary>Whether the explorer exported this scenario's map (propuesta 120): only such a scenario may be dropped by the warden.</summary>
    internal bool Exported { get; }

    internal Scenario(MapLayout map, Collisions collisions) : this(map, collisions, null, false) { }

    internal Scenario(MapLayout map, Collisions collisions, string frame, bool exported)
    {
        if (map == null) throw new GolemDomainException("a scenario needs its map, laid out");
        if (collisions == null) throw new GolemDomainException("a scenario needs its collisions module, even empty");
        Map = map;
        Collisions = collisions;
        Frame = string.IsNullOrWhiteSpace(frame) ? map.Name : frame;
        Exported = exported;
    }
}
