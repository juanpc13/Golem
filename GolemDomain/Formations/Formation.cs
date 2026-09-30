using GolemDomain.Geometry;

namespace GolemDomain.Formations;

/// <summary>
/// A FIGURE the fleet takes on the floor (propuesta 59, 28-sep-2026; Juan: "comandar una coreografía… un círculo… cada uno decide el
/// vértice que quiera tomar"): a centre and, for a fleet of N bodies, N PLACES in a FIXED order — so every golem, alone, computes the
/// same places and takes its own (paso 1: by rank, no negotiation). What figure it is, is the concrete class's: a Circle today.
/// Pure geometry: it knows no map — whether a place is on the map and leaves room for a body is the golem's to refuse.
/// </summary>
internal abstract class Formation
{
    internal Position Center { get; }

    protected Formation(Position center)
    {
        if (center == null) throw new GolemDomainException("Formation.Formation: 'center' was not given");
        Center = center;
    }

    /// <summary>What the journal and the panel call this figure.</summary>
    internal abstract string Name { get; }

    /// <summary>The places of this figure for a fleet of that many bodies, in a fixed order.</summary>
    internal abstract IReadOnlyList<Position> Places(int count);

    /// <summary>The place a member of the fleet takes: the one of its rank among as many places as the fleet has bodies.</summary>
    internal Position Place(Member member)
    {
        if (member == null) throw new GolemDomainException("Formation.Place: 'member' was not given");
        return Places(member.Of)[member.Rank];
    }

    /// <summary>The STAGES of a rotation for a member (paso 3): the next place in the sense of the turn, and the next, as many as
    /// fit in the rotation's duration at a body's cruise speed — the run between neighbouring places over the speed, a step each;
    /// at least one, a hundred at most. Nothing for a fleet of one: there is nowhere to turn to.</summary>
    internal IReadOnlyList<Position> Stages(Member member, Rotation turn, double speed)
    {
        if (member == null) throw new GolemDomainException("Formation.Stages: 'member' was not given");
        if (turn == null) throw new GolemDomainException("Formation.Stages: 'turn' was not given");
        if (speed <= 0) throw new GolemDomainException("Formation.Stages: a body needs a speed greater than zero to turn around a figure");
        var places = Places(member.Of);
        if (places.Count < 2) return Array.Empty<Position>();
        double step = places[0].DistanceTo(places[1]) / speed;   // seconds from one place to the next, at cruise
        int steps = Math.Clamp((int)Math.Floor(turn.Lasting.InSeconds / step), 1, 100);
        var stages = new List<Position>();
        int rank = member.Rank;
        for (int i = 0; i < steps; i++)
        {
            rank = ((turn.Clockwise ? rank - 1 : rank + 1) % places.Count + places.Count) % places.Count;
            stages.Add(places[rank]);
        }
        return stages;
    }
}

/// <summary>A circle of places around a centre, said by its radius: N bodies evenly spaced from due east, counter-clockwise — three
/// make a triangle, four a square. <c>Circle(Position(5.5, 5.5), Meters(1.0))</c>. Set aside from the command line for now (ajuste 65:
/// the square alone), kept in the repertoire.</summary>
internal sealed class Circle : Formation
{
    internal Units.Length Radius { get; }

    internal Circle(Position center, Units.Length radius) : base(center)
    {
        if (radius == null) throw new GolemDomainException("Circle.Circle: 'radius' was not given");
        if (radius.InMeters <= 0) throw new GolemDomainException("a circle needs a radius greater than zero");
        Radius = radius;
    }

    internal override string Name => "circle";

    internal override IReadOnlyList<Position> Places(int count)
    {
        if (count < 1) throw new GolemDomainException("Circle.Places: a circle needs at least one body");
        var places = new List<Position>();
        for (int k = 0; k < count; k++)
        {
            double angle = 2 * Math.PI * k / count;
            places.Add(new Position(Center.X + Radius.InMeters * Math.Cos(angle), Center.Y + Radius.InMeters * Math.Sin(angle)));
        }
        return places;
    }
}

/// <summary>A regular POLYGON of places, said by its SIDE (ajuste 65, 30-sep-2026; Juan: "el cuadrado maneja radius, ¿no debería ser
/// lateral/largo del cuadro?"): its vertices on the circle that passes through them — the polygon's own business, never the operator's —
/// and the fleet spread evenly along its PERIMETER from the first vertex, counter-clockwise: as many bodies as vertices take the
/// vertices; more stand between them, where the perimeter divides. What polygon it is, is the concrete class's: the Square (a corner
/// north-east, its sides square to the map) and the Triangle (apex north).</summary>
internal abstract class Polygon : Formation
{
    internal Units.Length Side { get; }

    protected Polygon(Position center, Units.Length side) : base(center)
    {
        if (side == null) throw new GolemDomainException($"{GetType().Name}.{GetType().Name}: 'side' was not given");
        if (side.InMeters <= 0) throw new GolemDomainException($"a {Name} needs a side greater than zero");
        Side = side;
    }

    /// <summary>The radius of the circle through the vertices, in metres: the side over twice the sine of half the angle each side
    /// spans from the centre — the side over √2 for a square, over √3 for a triangle.</summary>
    internal double Circumradius => Side.InMeters / (2 * Math.Sin(Math.PI / Bearings.Count));

    /// <summary>The vertices' bearings from the centre, in degrees, the first one first and then counter-clockwise.</summary>
    protected abstract IReadOnlyList<double> Bearings { get; }

    /// <summary>The vertices, the first one first, counter-clockwise.</summary>
    internal IReadOnlyList<Position> Vertices()
    {
        var vertices = new List<Position>();
        foreach (double degrees in Bearings)
        {
            double angle = degrees * Math.PI / 180;
            vertices.Add(new Position(Center.X + Circumradius * Math.Cos(angle), Center.Y + Circumradius * Math.Sin(angle)));
        }
        return vertices;
    }

    internal override IReadOnlyList<Position> Places(int count)
    {
        if (count < 1) throw new GolemDomainException($"{Name}: a formation needs at least one body");
        var v = Vertices();
        int n = v.Count;
        double side = v[0].DistanceTo(v[1]);
        double spacing = n * side / count;
        var places = new List<Position>();
        for (int k = 0; k < count; k++)
        {
            double along = k * spacing;                       // the way along the perimeter from the first vertex
            int edge = Math.Min((int)Math.Floor(along / side), n - 1);
            double t = (along - edge * side) / side;          // how far along that edge
            Position from = v[edge], to = v[(edge + 1) % n];
            places.Add(new Position(from.X + (to.X - from.X) * t, from.Y + (to.Y - from.Y) * t));
        }
        return places;
    }
}

/// <summary>A triangle of places: the equilateral triangle of that side, its apex due north, the fleet spread along its perimeter from
/// the apex — three bodies take the vertices. <c>Triangle(Position(5.5, 5.5), Meters(2.0))</c>. Set aside from the command line for
/// now (ajuste 65: the square alone), kept in the repertoire.</summary>
internal sealed class Triangle : Polygon
{
    internal Triangle(Position center, Units.Length side) : base(center, side) { }
    internal override string Name => "triangle";
    protected override IReadOnlyList<double> Bearings => new[] { 90.0, 210.0, 330.0 };
}

/// <summary>A square of places (29-sep-2026; Juan: "la figura de cuadro"), said by its side (ajuste 65): its sides square to the map,
/// the first corner north-east, the fleet spread along its perimeter — four bodies take the corners, eight the corners and the middles
/// of the sides. <c>Square(Position(5.5, 5.5), Meters(2.0))</c>: corners a metre from the centre each way.</summary>
internal sealed class Square : Polygon
{
    internal Square(Position center, Units.Length side) : base(center, side) { }
    internal override string Name => "square";
    protected override IReadOnlyList<double> Bearings => new[] { 45.0, 135.0, 225.0, 315.0 };
}
