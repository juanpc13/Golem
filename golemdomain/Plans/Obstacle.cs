using GolemHost.Domain.Geometry;

namespace GolemHost.Domain.Plans;

/// <summary>
/// An obstacle as the marks outline it — a HYPOTHESIS: marks close to one another are taken to be vertices of
/// one thing (a crate, a piece of furniture, a wall nobody charted). One mark is a point, two a line, three
/// or more a polygon, and every new touch refines the figure. Its vertices are the facts; the figure is the guess.
/// <para>Origins: grouping touches by closeness is single-linkage clustering (union-find); keeping the set of
/// contacts as a belief about what occupies space, rather than a map cell, is what contact-sensing planners call
/// collision hypothesis sets (Saund &amp; Berenson, ISER 2018; carried into "The Blindfolded Robot", Saund,
/// Choudhury, Srinivasa &amp; Berenson, ISRR 2019 — planning with contact feedback alone, our very case). The
/// puppet's twist: the hypothesis is never journaled — it is recomputed from the marks, which are the only
/// testimony (paper 08, inference without authority).</para>
/// </summary>
internal sealed class Obstacle
{
    private readonly IReadOnlyList<Mark> vertices;

    internal Position Center { get; }
    internal int Size => vertices.Count;
    /// <summary>The figure the vertices draw: point, line or polygon.</summary>
    internal string Shape => Size == 1 ? "point" : Size == 2 ? "line" : "polygon";

    internal Obstacle(IReadOnlyList<Mark> vertices, Position center)
    {
        if (vertices == null || vertices.Count == 0) throw new DomainException("an obstacle is outlined by at least one mark");
        if (center == null) throw new DomainException("an obstacle has a center");
        this.vertices = vertices;
        Center = center;
    }

    /// <summary>The marks, ordered around the center so they can be joined into a figure.</summary>
    internal IReadOnlyList<Mark> Vertices() => vertices;
}
