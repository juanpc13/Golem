using GolemDomain.Geometry;

namespace GolemDomain.Trails;

/// <summary>What the explorer knows of one cell of its grain (propuesta 120): FREE — the trail crossed it; WALL — a touch's normal
/// pointed into it. Derived with the hypothesis, never kept.</summary>
internal sealed class Cell
{
    internal const string Free = "free";
    internal const string Wall = "wall";

    internal int Col { get; }
    internal int Row { get; }
    internal string Kind { get; }
    /// <summary>The cell's centre, in the scenario's frame.</summary>
    internal Position Center { get; }
    internal double X => Center.X;
    internal double Y => Center.Y;
    internal bool IsFree => Kind == Free;

    internal Cell(int col, int row, string kind, double grain)
    {
        if (kind != Free && kind != Wall) throw new GolemDomainException($"Cell: '{kind}' is no kind of cell");
        if (grain <= 0) throw new GolemDomainException("Cell: the grain must be a length");
        Col = col;
        Row = row;
        Kind = kind;
        Center = new Position((col + 0.5) * grain, (row + 0.5) * grain);
    }
}
