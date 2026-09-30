namespace GolemDomain.Formations;

/// <summary>
/// The fleet a formation is called for: the golems' NAMES, sorted, so every golem alone computes the same RANK for each — its place
/// among the names (paso 1 of propuesta 59: deterministic, no negotiation; paso 2 will assign the places by where everybody stands).
/// A name enters here, where the object is created, and in <see cref="Member"/>, where one is found: <c>Fleet('blue,red').Member('red')</c>.
/// </summary>
internal sealed class Fleet
{
    private readonly IReadOnlyList<string> names;

    internal Fleet(string names)
    {
        if (string.IsNullOrWhiteSpace(names)) throw new GolemDomainException("a fleet needs at least one golem's name");
        this.names = names.Split(',').Select(n => n.Trim().ToLowerInvariant()).Where(n => n != "").Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        if (this.names.Count == 0) throw new GolemDomainException("a fleet needs at least one golem's name");
    }

    internal int Count => names.Count;

    /// <summary>Whether that name is in the fleet — asked before its member is found (ajuste 71: a golem that hears a call joins only
    /// if it was called): <c>if (fleet.Knows(@member)) { me = fleet.Member(@member); … }</c>.</summary>
    internal bool Knows(string name) => !string.IsNullOrWhiteSpace(name) && names.Contains(name.Trim().ToLowerInvariant());
    internal IReadOnlyList<string> Names => names;

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
