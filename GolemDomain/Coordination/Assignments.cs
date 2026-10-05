using System.Globalization;
using GolemDomain.Geometry;

namespace GolemDomain.Coordination;

/// <summary>One member's assignment in a round: who, where it stands (as it said, or the place it reached) and the place it goes to.</summary>
internal sealed class Assignment
{
    internal Assignment(string who, Position from, Position to)
    {
        if (string.IsNullOrWhiteSpace(who)) throw new GolemDomainException("Assignment.Assignment: 'who' was not given");
        if (from == null) throw new GolemDomainException("Assignment.Assignment: 'from' was not given");
        if (to == null) throw new GolemDomainException("Assignment.Assignment: 'to' was not given");
        Who = who.Trim().ToLowerInvariant();
        From = from;
        To = to;
    }

    internal string Who { get; }
    internal Position From { get; }
    internal Position To { get; }
}

/// <summary>
/// THE ASSIGNMENTS of a round (propuesta 88): the whole table the warden shares — for every member, where it stands and the place it
/// goes to. A DATUM that travels whole in one word (<c>Shared</c>): the warden renders it (<see cref="AsText"/>, like a route's
/// <c>AsPlan()</c>) and the golem builds it back where the object is created, <c>given = Assignments(@table)</c> — then takes its own
/// place from it and the others as berths. The text: <c>blue@6,6.2>6.5,6.5;red@7,6.3>4.5,6.5</c>.
/// </summary>
internal sealed class Assignments
{
    private readonly List<Assignment> all;

    internal Assignments(IReadOnlyList<Assignment> assignments)
    {
        if (assignments == null) throw new GolemDomainException("Assignments.Assignments: 'assignments' was not given");
        if (assignments.Count == 0) throw new GolemDomainException("a round assigns at least one place");
        all = assignments.ToList();
    }

    /// <summary>Built back from the word that carried it.</summary>
    internal Assignments(string text)
    {
        if (string.IsNullOrWhiteSpace(text)) throw new GolemDomainException("Assignments.Assignments: 'text' was not given");
        all = new List<Assignment>();
        foreach (var entry in text.Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int at = entry.IndexOf('@'), arrow = entry.IndexOf('>');
            if (at <= 0 || arrow < at) throw new GolemDomainException($"an assignment reads who@x,y>x,y, not '{entry}'");
            all.Add(new Assignment(entry[..at], Point(entry[(at + 1)..arrow], entry), Point(entry[(arrow + 1)..], entry)));
        }
        if (all.Count == 0) throw new GolemDomainException("a round assigns at least one place");
    }

    private static Position Point(string xy, string entry)
    {
        var parts = xy.Split(',');
        if (parts.Length != 2
            || !double.TryParse(parts[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double x)
            || !double.TryParse(parts[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double y))
            throw new GolemDomainException($"an assignment reads who@x,y>x,y, not '{entry}'");
        return new Position(x, y);
    }

    internal int Count => all.Count;
    internal IReadOnlyList<Assignment> All() => all;

    /// <summary>Whether that golem has a place here — asked before its own is taken: <c>if (given.Has(g)) { … }</c>.</summary>
    internal bool Has(Golem golem)
    {
        if (golem == null) throw new GolemDomainException("Assignments.Has: 'golem' was not given");
        return golem.Name != "" && all.Any(a => a.Who == golem.Name);
    }

    /// <summary>That golem's own assignment; refused when it has none.</summary>
    internal Assignment Of(Golem golem)
    {
        if (golem == null) throw new GolemDomainException("Assignments.Of: 'golem' was not given");
        return all.FirstOrDefault(a => a.Who == golem.Name) ?? throw new GolemDomainException($"'{golem.Name}' has no place in these assignments");
    }

    /// <summary>The assignment of that name; refused when there is none.</summary>
    internal Assignment Of(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("Assignments.Of: 'name' was not given");
        return all.FirstOrDefault(a => a.Who == name.Trim().ToLowerInvariant()) ?? throw new GolemDomainException($"'{name}' has no place in these assignments");
    }

    /// <summary>Everybody's but that golem's — the bodies in its way when it sets out.</summary>
    internal IReadOnlyList<Assignment> Others(Golem golem)
    {
        if (golem == null) throw new GolemDomainException("Assignments.Others: 'golem' was not given");
        return all.Where(a => a.Who != golem.Name).ToList();
    }

    /// <summary>The whole table as one text, what travels in the word: <c>blue@6,6.2>6.5,6.5;red@7,6.3>4.5,6.5</c>.</summary>
    internal string AsText() => string.Join(";", all.Select(a => $"{a.Who}@{Fmt(a.From.X)},{Fmt(a.From.Y)}>{Fmt(a.To.X)},{Fmt(a.To.Y)}"));

    private static string Fmt(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
}
