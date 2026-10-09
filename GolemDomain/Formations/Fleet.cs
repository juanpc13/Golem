namespace GolemDomain.Formations;

/// <summary>
/// The fleet a formation is for: the golems' NAMES, sorted, so every golem alone computes the same RANK for each — its place
/// among the names (paso 1 of propuesta 59: deterministic, no negotiation; by distance, paso 2, the places are shared by where everybody
/// stands). A name enters here, where the object is created, and in <see cref="Member"/>, where one is found: <c>Fleet('blue,red').Member('red')</c>.
/// A fleet DIVIDED IN TWO (propuesta 104: the double ring) — <c>Fleet('blue,cyan,green', 'orange,red,yellow')</c> — keeps the outer ring's
/// names sorted first and the inner ring's sorted after them, so the ranks put every golem on its ring; <see cref="Division"/> says where the
/// inner ring begins (0 for a fleet of one ring).
/// </summary>
internal sealed class Fleet
{
    private readonly IReadOnlyList<string> names;

    internal Fleet(string names)
    {
        this.names = Sorted(names, "a fleet needs at least one golem's name");
        Division = 0;
    }

    /// <summary>A fleet in TWO RINGS: the outer ring's golems, then the inner ring's; a golem in both is refused.</summary>
    internal Fleet(string outer, string inner)
    {
        var first = Sorted(outer, "the outer ring needs at least one golem's name");
        var second = Sorted(inner, "the inner ring needs at least one golem's name");
        var both = first.Intersect(second, StringComparer.Ordinal).ToList();
        if (both.Count > 0) throw new GolemDomainException($"a golem rides one ring: {string.Join(", ", both)} on both");
        names = first.Concat(second).ToList();
        Division = first.Count;
    }

    private static IReadOnlyList<string> Sorted(string names, string empty)
    {
        if (string.IsNullOrWhiteSpace(names)) throw new GolemDomainException(empty);
        var sorted = names.Split(',').Select(n => n.Trim().ToLowerInvariant()).Where(n => n != "").Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        if (sorted.Count == 0) throw new GolemDomainException(empty);
        return sorted;
    }

    internal int Count => names.Count;
    internal IReadOnlyList<string> Names => names;
    /// <summary>The names as one comma list, in rank order — what the formations read prints, so the warden composes the fleet again.</summary>
    internal string Roster => string.Join(",", names);
    /// <summary>How many ride the outer ring — the rank the inner ring begins at; 0 when the fleet is not divided.</summary>
    internal int Division { get; }
    /// <summary>Whether the fleet is divided in two rings.</summary>
    internal bool IsDivided => Division > 0;

    /// <summary>Whether that golem is in the fleet — asked before its member is found (ajuste 72; Juan: "si el golem existe dentro de
    /// la fleet ejecutar el if"): <c>if (fleet.Has(g)) { me = fleet.Member(g); … }</c>. A golem without a name is in no fleet.</summary>
    internal bool Has(Golem golem)
    {
        if (golem == null) throw new GolemDomainException("Fleet.Has: 'golem' was not given");
        return golem.Name != "" && names.Contains(golem.Name);
    }

    /// <summary>The member that golem is in the fleet — by the name it was born with (ajuste 72): <c>me = fleet.Member(g);</c>.</summary>
    internal Member Member(Golem golem)
    {
        if (golem == null) throw new GolemDomainException("Fleet.Member: 'golem' was not given");
        if (golem.Name == "") throw new GolemDomainException("a golem without a name is in no fleet: it is born with one, Golem(body, name)");
        return Member(golem.Name);
    }

    /// <summary>The member of that name: its rank among the fleet's names, and how many they are.</summary>
    internal Member Member(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("Fleet.Member: 'name' was not given");
        int rank = names.ToList().IndexOf(name.Trim().ToLowerInvariant());
        if (rank < 0) throw new GolemDomainException($"'{name}' is not in the fleet ({string.Join(", ", names)})");
        return new Member(name.Trim().ToLowerInvariant(), rank, names.Count);
    }
}

/// <summary>One golem of a fleet: its name, its rank (0 first) and how many the fleet are.</summary>
internal sealed class Member
{
    internal string Name { get; }
    internal int Rank { get; }
    internal int Of { get; }

    internal Member(string name, int rank, int of)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("Member.Member: 'name' was not given");
        if (rank < 0 || of < 1 || rank >= of) throw new GolemDomainException($"a member's rank must lie within its fleet: rank {rank} of {of}");
        Name = name; Rank = rank; Of = of;
    }
}
