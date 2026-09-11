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
        if (vertices == null || vertices.Count == 0) throw new GolemDomainException("a thing is outlined by at least one mark");
        this.vertices = vertices;
        this.center = center ?? throw new GolemDomainException("a thing has a centre");
        this.where = where ?? "";
    }

    internal override string Kind => "thing";
    internal override string Where => where;
    internal override Position Center => center;
    internal override int Size => vertices.Count;
    internal override string Shape => Size == 1 ? "point" : Size == 2 ? "line" : "polygon";
    internal override IReadOnlyList<Mark> Vertices() => vertices;
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
        if (string.IsNullOrWhiteSpace(who)) throw new GolemDomainException("a peer met has a name");
        this.who = who;
        this.at = at ?? throw new GolemDomainException("a peer was met somewhere");
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
