using GolemDomain.Formations;
using GolemDomain.Geometry;

namespace GolemDomain.Coordination;

/// <summary>
/// THE WARDEN (propuesta 88, 2-oct-2026; Juan: "un actor/servicio nuevo para coordinar a los demás golems… el CLI principal para
/// controlarlos… centralizar las coreografías… que ellos sólo coordinen con este nuevo actor"; "me gusta Warden"): a SUBJECT of the
/// domain, like <see cref="Golem"/>, with a journal of its own and NO BODY — the custodian that has the golems in its ward and watches
/// them without leaving its post. What it knows of them it knows because they TOLD it (<see cref="Roster"/>: where each said it stood,
/// in which scenario); what it decides — who takes which place, when a step opens — it decides ONCE, in its one copy of the
/// convocation (<see cref="Muster"/>), and the golems receive their places. It never drives a body: it gives places and carries lines.
/// Born with its name: <c>w = Warden(@wardenName)</c>.
/// </summary>
internal sealed class Warden
{
    private readonly List<Muster> musters = new();   // the convocations it called, by their call

    internal Warden(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("a warden's name must be a name");
        Name = name.Trim().ToLowerInvariant();
        Roster = new Roster();
    }

    /// <summary>Who the warden is: the journal's identity, lower case.</summary>
    internal string Name { get; }

    /// <summary>What it knows of each golem — their words, kept (the fleet is LEARNED: a golem enters the roster when it first speaks).</summary>
    internal Roster Roster { get; }

    /// <summary>A formation by its name — <c>square</c>, <c>pentagon</c>, <c>triangle</c>, <c>circle</c> — at that centre with its natural
    /// measure: <c>formation = w.Formation(@figure, center, side);</c>.</summary>
    internal Formation Formation(string name, Position center, Units.Length measure)
    {
        if (name == null) throw new GolemDomainException("Warden.Formation: 'name' was not given");
        if (center == null) throw new GolemDomainException("Warden.Formation: 'center' was not given");
        if (measure == null) throw new GolemDomainException("Warden.Formation: 'measure' was not given");
        return Formations.Formation.Named(name, center, measure);
    }

    /// <summary>The CONVOCATION of that call, with its policy — <c>rank</c> or <c>distance</c> — found by the call's identity or opened:
    /// <c>muster = w.Muster(@callId, formation, fleet, @by);</c>. One copy, the warden's.</summary>
    internal Muster Muster(string call, Formation formation, Fleet fleet, string by)
    {
        if (string.IsNullOrWhiteSpace(call)) throw new GolemDomainException("Warden.Muster: 'call' was not given");
        if (formation == null) throw new GolemDomainException("Warden.Muster: 'formation' was not given");
        if (fleet == null) throw new GolemDomainException("Warden.Muster: 'fleet' was not given");
        if (by == null) throw new GolemDomainException("Warden.Muster: 'by' was not given");
        string policy = by.Trim().ToLowerInvariant();
        if (policy is not ("rank" or "distance")) throw new GolemDomainException($"the places are shared by 'rank' or by 'distance', not by '{by}'");
        var known = musters.FirstOrDefault(m => m.Call == call);
        if (known != null) return known;
        var muster = new Muster(call, formation, fleet, policy == "distance");
        musters.Add(muster);
        return muster;
    }

    /// <summary>The convocation of that call, already called — <c>muster = w.Muster(@call);</c> in the uptakes of the golems' words.
    /// Refused when unknown.</summary>
    internal Muster Muster(string call)
    {
        if (string.IsNullOrWhiteSpace(call)) throw new GolemDomainException("Warden.Muster: 'call' was not given");
        return musters.FirstOrDefault(m => m.Call == call) ?? throw new GolemDomainException($"the warden knows no call {call}");
    }

    /// <summary>Whether the warden called that convocation.</summary>
    internal bool Knows(string call)
    {
        if (string.IsNullOrWhiteSpace(call)) throw new GolemDomainException("Warden.Knows: 'call' was not given");
        return musters.Any(m => m.Call == call);
    }

    /// <summary>The FORMATION IN PLACE: the latest convocation called — what a step (<c>rotate</c>) is about. Refused before any call.</summary>
    internal Muster Current =>
        musters.LastOrDefault(m => m.Called) ?? throw new GolemDomainException("the fleet stands in no formation: call one first (choreograph)");

    /// <summary>Every convocation called, latest last.</summary>
    internal IReadOnlyList<Muster> Musters() => musters.Where(m => m.Called).ToList();
}

/// <summary>
/// THE ROSTER — what the warden knows of each golem: nothing but their WORDS, kept (paper 08: facts are journaled testimony). A golem
/// enters it when it first speaks (the fleet is learned, never configured); every word brings where it stood and in which scenario.
/// </summary>
internal sealed class Roster
{
    private readonly Dictionary<string, Presence> known = new(StringComparer.Ordinal);

    /// <summary>A golem's word — where it stands and in which scenario — kept as the latest about it: <c>w.Roster.Heard(@who, at, @scenario)</c>.
    /// A name enters where the presence is created or found.</summary>
    internal Presence Heard(string name, Position at, string scenario)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("Roster.Heard: 'name' was not given");
        if (at == null) throw new GolemDomainException("Roster.Heard: 'at' was not given");
        if (scenario == null) throw new GolemDomainException("Roster.Heard: 'scenario' was not given");
        string who = name.Trim().ToLowerInvariant();
        if (!known.TryGetValue(who, out var presence))
        {
            presence = new Presence(who);
            known[who] = presence;
        }
        presence.Update(new Position(at.X, at.Y), scenario.Trim().ToLowerInvariant());
        return presence;
    }

    /// <summary>A golem's word about where it stands alone — it reached its place (the warden knows which): the roster's latest position,
    /// the scenario kept: <c>w.Roster.Stands(@who, muster.PlaceOf(member))</c>. Refused when it never spoke.</summary>
    internal Presence Stands(string name, Position at)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("Roster.Stands: 'name' was not given");
        if (at == null) throw new GolemDomainException("Roster.Stands: 'at' was not given");
        var presence = Of(name);
        presence.Update(new Position(at.X, at.Y), presence.Scenario);
        return presence;
    }

    /// <summary>What is known of that golem; refused when it never spoke.</summary>
    internal Presence Of(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("Roster.Of: 'name' was not given");
        return known.TryGetValue(name.Trim().ToLowerInvariant(), out var presence) ? presence
            : throw new GolemDomainException($"the warden never heard from '{name}'");
    }

    internal bool Knows(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("Roster.Knows: 'name' was not given");
        return known.ContainsKey(name.Trim().ToLowerInvariant());
    }

    internal int Count => known.Count;

    /// <summary>Every golem that spoke, by name.</summary>
    internal IReadOnlyList<Presence> All() => known.Values.OrderBy(p => p.Name, StringComparer.Ordinal).ToList();

    /// <summary>The names, sorted — what <c>Fleet</c> is built from.</summary>
    internal IReadOnlyList<string> Names() => known.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();

    /// <summary>The FLEET of everybody that spoke: what a call without a fleet told outright convenes. Refused while nobody spoke.</summary>
    internal Fleet Fleet()
    {
        if (known.Count == 0) throw new GolemDomainException("the warden heard from no golem yet: nobody to call");
        return new Fleet(string.Join(",", Names()));
    }
}

/// <summary>A golem as the warden knows it: the latest word about where it stands and in which scenario, and how many words it said.</summary>
internal sealed class Presence
{
    internal Presence(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("Presence.Presence: 'name' was not given");
        Name = name;
    }

    internal string Name { get; }
    /// <summary>Where it last said it stood.</summary>
    internal Position Standing { get; private set; }
    /// <summary>The scenario it last said it was in.</summary>
    internal string Scenario { get; private set; } = "";
    /// <summary>How many words the warden heard from it.</summary>
    internal int Words { get; private set; }

    internal void Update(Position at, string scenario)
    {
        Standing = at;
        Scenario = scenario;
        Words++;
    }
}
