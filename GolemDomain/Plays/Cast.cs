namespace GolemDomain.Plays;

/// <summary>THE CAST of a play (propuesta 108, fase 2): who plays pacman and who the ghosts — names, like a <see cref="Formations.Fleet"/>,
/// so every golem told the play computes the same: <c>cast = Cast(@star, @crew)</c>. A golem asks whether it is in the cast and which part
/// it plays by the name it was born with (ajuste 72).</summary>
internal sealed class Cast
{
    private readonly List<string> ghosts;

    /// <summary>Pacman's name, and the ghosts' names comma-separated (sorted here; a ghost named like pacman is refused).</summary>
    internal Cast(string pacman, string ghosts)
    {
        if (string.IsNullOrWhiteSpace(pacman)) throw new GolemDomainException("a play needs a pacman");
        if (ghosts == null) throw new GolemDomainException("Cast.Cast: 'ghosts' was not given");
        Pacman = pacman.Trim().ToLowerInvariant();
        this.ghosts = ghosts.Split(',').Select(n => n.Trim().ToLowerInvariant()).Where(n => n != "").Distinct().OrderBy(n => n, StringComparer.Ordinal).ToList();
        if (this.ghosts.Contains(Pacman)) throw new GolemDomainException($"'{Pacman}' cannot be pacman and a ghost at once");
    }

    internal string Pacman { get; }
    internal IReadOnlyList<string> Ghosts => ghosts;
    /// <summary>The ghosts' names comma-separated: what a tell carries.</summary>
    internal string Roster => string.Join(",", ghosts);
    internal int Count => 1 + ghosts.Count;

    /// <summary>Whether that golem is in the cast — asked before its part: <c>if (cast.Has(g)) { … }</c>.</summary>
    internal bool Has(Golem golem)
    {
        if (golem == null) throw new GolemDomainException("Cast.Has: 'golem' was not given");
        return golem.Name != "" && (golem.Name == Pacman || ghosts.Contains(golem.Name));
    }

    /// <summary>The part that golem plays, by the name it was born with.</summary>
    internal Part PartOf(Golem golem)
    {
        if (golem == null) throw new GolemDomainException("Cast.PartOf: 'golem' was not given");
        if (!Has(golem)) throw new GolemDomainException($"'{golem.Name}' is not in the cast: pacman is {Pacman}, the ghosts {Roster}");
        return golem.Name == Pacman ? Part.Pacman : Part.Ghost;
    }

    /// <summary>Whether that name is in the cast — a peer's word is about a member or about nobody of the play (fase 4).</summary>
    internal bool Knows(string name) => !string.IsNullOrWhiteSpace(name) && (name.Trim().ToLowerInvariant() == Pacman || ghosts.Contains(name.Trim().ToLowerInvariant()));

    /// <summary>The part a member plays, by its name (fase 4: who touched whom is said by name). Refused for a name outside the cast.</summary>
    internal Part PartOf(string name)
    {
        if (!Knows(name)) throw new GolemDomainException($"'{name}' is not in the cast: pacman is {Pacman}, the ghosts {Roster}");
        return name.Trim().ToLowerInvariant() == Pacman ? Part.Pacman : Part.Ghost;
    }

    /// <summary>A ghost's RANK in the cast (fase 3): its place in the ghosts' names sorted, 0 the first — the order they leave the den in.
    /// Refused for pacman or a golem outside the cast.</summary>
    internal int RankOf(Golem golem)
    {
        if (golem == null) throw new GolemDomainException("Cast.RankOf: 'golem' was not given");
        if (PartOf(golem) != Part.Ghost) throw new GolemDomainException($"'{golem.Name}' is pacman: the ghosts are ranked, not pacman");
        return ghosts.IndexOf(golem.Name);
    }
}
