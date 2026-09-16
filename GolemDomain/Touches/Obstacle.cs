using GolemDomain.Geometry;

namespace GolemDomain.Touches;

/// <summary>
/// An obstacle — Juan's OBSTÁCULO (hipótesis): what the golem hypothesizes from what its body touched — never
/// journaled, always recomputed from the facts (paper 08: the facts are testimony, the figure is inference). Two
/// truths of the domain, hence two kinds: a <see cref="Thing"/> outlined by marks, which the roads avoid; and a
/// <see cref="Peer"/>, another body met on the way — transitory, kept as history, never planned around.
/// </summary>
internal abstract class Obstacle
{
    /// <summary>thing or peer.</summary>
    internal abstract string Kind { get; }
    /// <summary>The zone its centre stands in, so a table can name it; "" when it stands nowhere the layout holds.</summary>
    internal abstract string Where { get; }
    internal abstract Position Center { get; }
    internal abstract int Size { get; }
    /// <summary>The figure: point, line or polygon.</summary>
    internal abstract string Shape { get; }
    /// <summary>Who it was, for a peer; "" for a thing.</summary>
    internal virtual string Who => "";
    /// <summary>The marks that outline it, ordered around the centre so they can be joined; none for a peer.</summary>
    internal abstract IReadOnlyList<Mark> Vertices();
}

/// <summary>
/// A thing: marks close to one another taken to be vertices of one object (a crate, furniture, a wall nobody
/// charted). One mark is a point, two a line, three or more a polygon, and every new touch refines the figure.
/// <para>Origins: grouping touches by closeness is single-linkage clustering (union-find); keeping the set of
/// contacts as a belief about what occupies space, rather than a map cell, is what contact-sensing planners call
/// collision hypothesis sets (Saund &amp; Berenson, ISER 2018; "The Blindfolded Robot", Saund, Choudhury,
/// Srinivasa &amp; Berenson, ISRR 2019 — planning with contact feedback alone, our very case).</para>
/// </summary>
internal sealed class Thing : Obstacle
{
    private readonly IReadOnlyList<Mark> vertices;
    private readonly Position center;
    private readonly string where;

    internal Thing(IReadOnlyList<Mark> vertices, Position center, string where)
    {
        if (center == null) throw new GolemDomainException("a thing has a centre");
        if (vertices == null || vertices.Count == 0) throw new GolemDomainException("a thing is outlined by at least one mark");
        this.vertices = vertices;
        this.center = center;
        this.where = where ?? "";
    }

    internal override string Kind => "thing";
    internal override string Where => where;
    internal override Position Center => center;
    internal override int Size => vertices.Count;
    internal override string Shape => Size == 1 ? "point" : Size == 2 ? "line" : "polygon";
    internal override IReadOnlyList<Mark> Vertices() => vertices;

    /// <summary>The figure a way keeps clear of: the box around the vertices, grown by what every mark reaches, by
    /// half of what the thing showed of its size between touches (the spread, up to one more reach), and by a margin —
    /// so the second way around a crate already takes the crate's real width, not a vertex's (Juan, 14-sep-2026: "girar más"). Inflate it by the
    /// body's radius to get where the body's CENTRE may not go.</summary>
    internal Rectangle Extent(double margin)
    {
        double minX = vertices.Min(v => v.At.X), maxX = vertices.Max(v => v.At.X);
        double minY = vertices.Min(v => v.At.Y), maxY = vertices.Max(v => v.At.Y);
        double grow = Collisions.MarkReach + margin;
        return new Rectangle(minX - grow, minY - grow, (maxX - minX) + 2 * grow, (maxY - minY) + 2 * grow);
    }
}

/// <summary>
/// A peer: another body the golem met — it touched something, a peer said it bumped there and then, so it was
/// that peer. History, not geometry: a peer moves on, so nothing is planned around it.
/// </summary>
internal sealed class Peer : Obstacle
{
    private readonly string who;
    private readonly Position at;
    private readonly string where;

    internal Peer(string who, Position at, string where)
    {
        if (at == null) throw new GolemDomainException("a peer was met somewhere");
        if (string.IsNullOrWhiteSpace(who)) throw new GolemDomainException("a peer met has a name");
        this.who = who;
        this.at = at;
        this.where = where ?? "";
    }

    internal override string Kind => "peer";
    internal override string Where => where;
    internal override string Who => who;
    internal override Position Center => at;
    internal override int Size => 1;
    internal override string Shape => "point";
    internal override IReadOnlyList<Mark> Vertices() => Array.Empty<Mark>();
}
