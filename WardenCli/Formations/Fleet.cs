namespace WardenCli.Formations;

/// <summary>A golem that takes part, as the console knows it: its name and where it was last seen standing — the last `where` it
/// answered — when the console knows it (null when it never answered).</summary>
public sealed record Member(string Name, Spot? Standing);

/// <summary>THE FLEET the console lays a figure out for: the members by name, SORTED — the rank every assignment by rank reads, the same
/// rule as the golem's own `Fleet` (names sorted, the first takes the first place).</summary>
public sealed class Fleet
{
    private readonly List<Member> members;

    public Fleet(IEnumerable<Member> members)
    {
        var list = members.ToList();
        if (list.Count == 0) throw new ArgumentException("a fleet needs at least one golem");
        var dup = list.GroupBy(m => m.Name, StringComparer.Ordinal).FirstOrDefault(g => g.Count() > 1);
        if (dup != null) throw new ArgumentException($"'{dup.Key}' is in the fleet twice");
        this.members = list.OrderBy(m => m.Name, StringComparer.Ordinal).ToList();
    }

    public IReadOnlyList<Member> Members => members;
    public int Count => members.Count;
    public IReadOnlyList<string> Names => members.Select(m => m.Name).ToList();
    public int RankOf(string name)
    {
        int i = members.FindIndex(m => m.Name == name);
        return i < 0 ? throw new ArgumentException($"'{name}' is not in the fleet ({string.Join(", ", Names)})") : i;
    }
}

/// <summary>HOW THE PLACES ARE SHARED — the console's copy of the golem's two laws, so the operator may choose either and see the result on the
/// tabs before anything goes: by RANK (names sorted take the places in order) or by DISTANCE (the first law: the pairs member–place from the
/// shortest to the longest, a tie by name then by place, each member and each place once; with more places than members the ones nobody
/// was near stay free). One operation per variant, never a flag.</summary>
public abstract class Assignment
{
    public abstract string Name { get; }

    /// <summary>For every member, the index of its place in the figure's order.</summary>
    public abstract IReadOnlyDictionary<string, int> Share(Fleet fleet, IReadOnlyList<Spot> places);

    public static Assignment Named(string name) => name.Trim().ToLowerInvariant() switch
    {
        "rank" => new ByRank(),
        "distance" => new ByDistance(),
        _ => throw new ArgumentException($"no assignment named '{name}': rank or distance"),
    };
}

public sealed class ByRank : Assignment
{
    public override string Name => "rank";
    public override IReadOnlyDictionary<string, int> Share(Fleet fleet, IReadOnlyList<Spot> places)
    {
        if (fleet.Count > places.Count) throw new ArgumentException($"{fleet.Count} golems and {places.Count} places: the figure has no place for everybody");
        return fleet.Names.Select((n, i) => (n, i)).ToDictionary(p => p.n, p => p.i);
    }
}

public sealed class ByDistance : Assignment
{
    public override string Name => "distance";
    public override IReadOnlyDictionary<string, int> Share(Fleet fleet, IReadOnlyList<Spot> places)
    {
        if (fleet.Count > places.Count) throw new ArgumentException($"{fleet.Count} golems and {places.Count} places: the figure has no place for everybody");
        var blind = fleet.Members.Where(m => m.Standing == null).Select(m => m.Name).ToList();
        if (blind.Count > 0) throw new ArgumentException($"by distance needs where everybody stands — ask 'where' first; unknown: {string.Join(", ", blind)}");
        var pairs = new List<(double D, string Name, int Place)>();
        foreach (var m in fleet.Members)
            for (int p = 0; p < places.Count; p++)
                pairs.Add((m.Standing!.Value.DistanceTo(places[p]), m.Name, p));
        var given = new Dictionary<string, int>(StringComparer.Ordinal);
        var taken = new HashSet<int>();
        foreach (var (_, name, place) in pairs.OrderBy(x => x.D).ThenBy(x => x.Name, StringComparer.Ordinal).ThenBy(x => x.Place))
        {
            if (given.ContainsKey(name) || taken.Contains(place)) continue;
            given[name] = place;
            taken.Add(place);
            if (given.Count == fleet.Count) break;
        }
        return given;
    }
}
