using GolemDomain.Geometry;

namespace GolemDomain.Plans;

/// <summary>
/// A way from one place into another. Two truths of the domain, hence two kinds: a <see cref="Door"/> is a
/// gap in a wall, crossed straight through a point; an <see cref="OpenBoundary"/> is a shared edge with no
/// wall at all, crossed wherever the road meets it. A passage names its places (not the objects: a door is
/// declared from the first place charted, before its neighbour exists) and resolves them through its plan.
/// </summary>
internal abstract class Passage
{
    internal string A { get; }
    internal string B { get; }
    protected readonly FloorPlan plan;

    internal Passage(FloorPlan plan, string a, string b)
    {
        if (plan == null) throw new DomainException("a passage belongs to a floor plan");
        if (string.IsNullOrWhiteSpace(a) || string.IsNullOrWhiteSpace(b)) throw new DomainException("a passage joins two named places");
        if (a == b) throw new DomainException($"a passage must join two different places, not '{a}' twice");
        this.plan = plan;
        A = a;
        B = b;
    }

    internal bool Joins(string place) => A == place || B == place;
    internal bool Joins(string one, string other) => (A == one && B == other) || (A == other && B == one);
    internal string OtherSide(string place) => A == place ? B : A;

    /// <summary>How the journal names the crossing: kitchen/north for a door, north~center for an opening.</summary>
    internal abstract string Name { get; }

    /// <summary>Whether both places are charted already (a passage may be declared before its far side).</summary>
    internal bool BothCharted => plan.Knows(A) && plan.Knows(B);
    internal Place PlaceA => plan.PlaceNamed(A);
    internal Place PlaceB => plan.PlaceNamed(B);
}

/// <summary>
/// A door — Juan's PUERTA: a gap in the wall two places share, defined by two jamb locations on that wall:
/// its point (the middle), its width (the plan's door width) and the plan's height. Crossed straight: line
/// up in front, leave behind.
/// </summary>
internal sealed class Door : Passage
{
    internal Position At { get; }
    internal double Width => FloorPlan.DoorWidth;
    internal double Height => FloorPlan.Height;

    internal Door(FloorPlan plan, string a, string b, Position at) : base(plan, a, b)
    {
        if (at == null) throw new DomainException($"the door {a}/{b} needs a point on the shared wall");
        At = at;
    }

    internal override string Name => $"{A}/{B}";

    /// <summary>The two jambs: locations on the wall, half a width to each side of the point, along the wall.</summary>
    internal IReadOnlyList<Location> Jambs()
    {
        var step = PlaceA.StepInto(PlaceB);                  // across the wall
        double ax = -step.Y, ay = step.X;                   // along it
        return new[]
        {
            new Location($"{Name} jamb", At.X - ax * Width / 2, At.Y - ay * Width / 2),
            new Location($"{Name} jamb", At.X + ax * Width / 2, At.Y + ay * Width / 2),
        };
    }

    /// <summary>A unit step through the door into the named side (one of its two places), from the other.</summary>
    internal Position StepInto(string side)
    {
        if (!Joins(side)) throw new DomainException($"the door {Name} does not open into '{side}'");
        return plan.PlaceNamed(OtherSide(side)).StepInto(plan.PlaceNamed(side));
    }
}

/// <summary>
/// An open boundary: the whole edge two places share is free — no wall, no door. The road crosses it wherever
/// it meets it, kept away from the corners where the walls end; its midpoint is a node of the road graph so a
/// road made of openings alone can be walked.
/// </summary>
internal sealed class OpenBoundary : Passage
{
    internal OpenBoundary(FloorPlan plan, string a, string b) : base(plan, a, b) { }

    internal override string Name => $"{A}~{B}";

    /// <summary>Whether the two places are charted and actually share an edge.</summary>
    internal bool Touches => BothCharted && PlaceA.Touches(PlaceB);

    /// <summary>The shared edge. Consult Touches first.</summary>
    internal Segment Edge => PlaceA.SharedEdgeWith(PlaceB);
    internal Position Midpoint => Edge.Midpoint;

    /// <summary>Whether the straight run u→v crosses the shared edge.</summary>
    internal bool IsCrossedBy(Position u, Position v)
    {
        var edge = Edge;
        if (edge.IsVertical)
        {
            double x = edge.From.X, y0 = Math.Min(edge.From.Y, edge.To.Y), y1 = Math.Max(edge.From.Y, edge.To.Y);
            if ((u.X - x) * (v.X - x) > 0) return false;      // both on the same side: no crossing
            if (Math.Abs(v.X - u.X) < 1e-9) return false;
            double t = (x - u.X) / (v.X - u.X);
            double y = u.Y + t * (v.Y - u.Y);
            return y >= y0 - 1e-9 && y <= y1 + 1e-9;
        }
        else
        {
            double y = edge.From.Y, x0 = Math.Min(edge.From.X, edge.To.X), x1 = Math.Max(edge.From.X, edge.To.X);
            if ((u.Y - y) * (v.Y - y) > 0) return false;
            if (Math.Abs(v.Y - u.Y) < 1e-9) return false;
            double t = (y - u.Y) / (v.Y - u.Y);
            double x = u.X + t * (v.X - u.X);
            return x >= x0 - 1e-9 && x <= x1 + 1e-9;
        }
    }

    /// <summary>Where the straight run u→v meets the edge — kept OpeningMargin away from the corners, where
    /// the walls of the solid blocks stand; a narrow opening is crossed through its middle.</summary>
    internal Position CrossingPoint(Position u, Position v)
    {
        var edge = Edge;
        if (edge.IsVertical)
        {
            double x = edge.From.X, t = (x - u.X) / (v.X - u.X);
            double y0 = Math.Min(edge.From.Y, edge.To.Y), y1 = Math.Max(edge.From.Y, edge.To.Y);
            return new Position(x, AwayFromCorners(u.Y + t * (v.Y - u.Y), y0, y1));
        }
        else
        {
            double y = edge.From.Y, t = (y - u.Y) / (v.Y - u.Y);
            double x0 = Math.Min(edge.From.X, edge.To.X), x1 = Math.Max(edge.From.X, edge.To.X);
            return new Position(AwayFromCorners(u.X + t * (v.X - u.X), x0, x1), y);
        }
    }

    private static double AwayFromCorners(double along, double from, double to)
    {
        if (to - from <= 2 * FloorPlan.OpeningMargin) return (from + to) / 2;   // a narrow opening: straight through the middle
        return Math.Clamp(along, from + FloorPlan.OpeningMargin, to - FloorPlan.OpeningMargin);
    }
}
