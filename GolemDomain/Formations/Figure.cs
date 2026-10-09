using GolemDomain.Geometry;

using NetTopologySuite.Algorithm;
using LineSegment = NetTopologySuite.Geometries.LineSegment;

namespace GolemDomain.Formations;

/// <summary>
/// A FIGURE the fleet takes on the floor (propuesta 59, 28-sep-2026; Juan: "comandar una coreografía… un círculo… cada uno decide el
/// vértice que quiera tomar"): a centre and, for a fleet of N bodies, N PLACES in a FIXED order — so every golem, alone, computes the
/// same places. Pure geometry: it knows no map — whether a place is on the map and leaves room for a body is the golem's to refuse.
/// Since propuesta 104 (8-oct-2026) the figure is ONE PART of a <see cref="Formation"/> — the formation is the figure WITH its fleet, its
/// policy and the place every member holds; the figure says where the places are and what a step is. Was named <c>Formation</c> until
/// then (Juan: "las formaciones le pertenecerán al golem… al final vienen siendo la misma cosa").
/// </summary>
internal abstract class Figure
{
    internal Position Center { get; }

    /// <summary>Its ORIENTATION (propuesta 99, 8-oct-2026): how far it is turned counter-clockwise from the way it is laid out — added to every
    /// place's bearing, the order of the places unchanged.</summary>
    internal Units.Angle Turn { get; }

    protected Figure(Position center) : this(center, new Units.Degrees(0.0)) { }

    protected Figure(Position center, Units.Angle turn)
    {
        if (center == null) throw new GolemDomainException("Figure.Figure: 'center' was not given");
        if (turn == null) throw new GolemDomainException("Figure.Figure: 'turn' was not given");
        Center = center;
        Turn = turn;
    }

    /// <summary>What the journal and the console call this figure.</summary>
    internal abstract string Name { get; }

    /// <summary>What says its size: a polygon's side, a circle's radius, a double ring's outer radius — what the formations read prints.</summary>
    internal abstract Units.Length Measure { get; }

    /// <summary>The places of this figure for a fleet of that many bodies, in a fixed order.</summary>
    internal abstract IReadOnlyList<Position> Places(int count);

    /// <summary>The place a member of the fleet takes BY RANK: the one of its rank among as many places as the fleet has bodies.</summary>
    internal Position Place(Member member)
    {
        if (member == null) throw new GolemDomainException("Figure.Place: 'member' was not given");
        return Places(member.Of)[member.Rank];
    }

    /// <summary>The ORBITS of the figure (propuesta 104): the runs of places a step moves round, each as its first place and how many — one
    /// for every figure, the whole of its places; two for a double ring, the outer ring then the inner one.</summary>
    internal virtual IReadOnlyList<(int Start, int Count)> Orbits(int count) => new[] { (0, Places(count).Count) };

    /// <summary>Which orbit the place of that index lies in: 0 for every figure but the double ring.</summary>
    internal int OrbitOf(int index, int count)
    {
        var orbits = Orbits(count);
        for (int o = 0; o < orbits.Count; o++)
            if (index >= orbits[o].Start && index < orbits[o].Start + orbits[o].Count) return o;
        throw new GolemDomainException($"a {Name} of {count} has no place {index}");
    }

    /// <summary>The figure's ROTATION as a <see cref="Move"/> (ajuste 84; Juan: "que la figura como tal tenga la coreografía"): every body
    /// to the next place in that sense, <c>clockwise</c> or <c>counterclockwise</c> — <c>move = formation.Figure.Rotate(@sense, @ring)</c>.
    /// A figure that does not rotate refuses; the ones whose places run around a centre say what a step is (<see cref="Polygon"/>,
    /// <see cref="Circle"/>). The RING (propuesta 104): <c>Whole</c> turns every orbit; <c>Outer</c> or <c>Inner</c> one ring alone — a
    /// figure with one orbit refuses them. Every figure grows its own moves here, in its class, never in the formation.</summary>
    internal virtual Move Rotate(Sense sense, Ring ring)
    {
        if (ring != Ring.Whole) throw new GolemDomainException($"a {Name} has no rings: it turns whole");
        return Rotate(sense);
    }

    internal virtual Move Rotate(Sense sense) => throw new GolemDomainException($"a {Name} does not rotate");

    /// <summary>One place along the figure's fixed order in that sense, round every orbit: the places run counter-clockwise, so clockwise is
    /// one place DOWN the order. The sense is the closed set <see cref="Sense"/> (ajuste 90), never a string.</summary>
    protected static Move OnePlace(Sense sense) =>
        sense == Sense.Clockwise ? new Move("one step clockwise", -1) : new Move("one step counter-clockwise", 1);
}

/// <summary>A circle of places around a centre, said by its radius: N bodies evenly spaced from due east, counter-clockwise — three
/// make a triangle, four a square. <c>Circle(Position(5.5, 5.5), Meters(1.0))</c>. A RING, when the warden names it (ajuste 102).</summary>
internal sealed class Circle : Figure
{
    internal Units.Length Radius { get; }

    internal Circle(Position center, Units.Length radius) : this(center, radius, new Units.Degrees(0.0)) { }

    /// <summary>A circle turned (ajuste 102): its first place that far counter-clockwise from due east.</summary>
    internal Circle(Position center, Units.Length radius, Units.Angle turn) : base(center, turn)
    {
        if (radius == null) throw new GolemDomainException("Circle.Circle: 'radius' was not given");
        if (radius.InMeters <= 0) throw new GolemDomainException("a circle needs a radius greater than zero");
        Radius = radius;
    }

    internal override string Name => "circle";

    internal override Units.Length Measure => Radius;

    /// <summary>A step around the circle: its places run counter-clockwise, so clockwise is one place DOWN the order (ajuste 84).</summary>
    internal override Move Rotate(Sense sense) => OnePlace(sense);

    internal override IReadOnlyList<Position> Places(int count)
    {
        if (count < 1) throw new GolemDomainException("Circle.Places: a circle needs at least one body");
        var places = new List<Position>();
        for (int k = 0; k < count; k++)
        {
            places.Add(Center.Along(AngleUtility.ToRadians(360.0 * k / count + Turn.InDegrees), Radius.InMeters));
        }
        return places;
    }
}

/// <summary>THE DOUBLE RING (propuesta 104, 8-oct-2026; Juan: "una formación que pregunte por los diámetros de los dos anillos y que se
/// pueda rotar el superior y el inferior… seleccionar cuáles serían los golems del anillo inferior y superior"): two circles round one
/// centre, said by their RADII, each with as many places as the golems that ride it — evenly spaced from due east (turned by the figure's
/// orientation), counter-clockwise like the circle. The places run the OUTER ring first, then the INNER one, so a fleet DIVIDED in two
/// (<see cref="Fleet"/>: the outer names sorted, then the inner ones) puts every golem on its ring by rank; each ring is an ORBIT, so a step
/// may turn one ring and not the other, or both. The golem knows of both rings: the other ring's members are berths when its route opens.</summary>
internal sealed class DoubleRing : Figure
{
    internal Units.Length OuterRadius { get; }
    internal Units.Length InnerRadius { get; }
    internal int OuterPlaces { get; }
    internal int InnerPlaces { get; }

    internal DoubleRing(Position center, Units.Length outerRadius, Units.Length innerRadius, int outerPlaces, int innerPlaces, Units.Angle turn) : base(center, turn)
    {
        if (outerRadius == null) throw new GolemDomainException("DoubleRing.DoubleRing: 'outerRadius' was not given");
        if (innerRadius == null) throw new GolemDomainException("DoubleRing.DoubleRing: 'innerRadius' was not given");
        if (innerRadius.InMeters <= 0) throw new GolemDomainException("a double ring needs an inner radius greater than zero");
        if (outerRadius.InMeters <= innerRadius.InMeters) throw new GolemDomainException("the outer ring of a double ring is wider than the inner one");
        if (outerPlaces < 1 || innerPlaces < 1) throw new GolemDomainException("each ring of a double ring needs at least one body");
        OuterRadius = outerRadius;
        InnerRadius = innerRadius;
        OuterPlaces = outerPlaces;
        InnerPlaces = innerPlaces;
    }

    internal override string Name => "double ring";

    internal override Units.Length Measure => OuterRadius;

    /// <summary>Whether the place of that index is on the inner ring.</summary>
    internal bool IsInner(int index) => index >= OuterPlaces;

    internal override IReadOnlyList<Position> Places(int count)
    {
        if (count != OuterPlaces + InnerPlaces) throw new GolemDomainException($"a double ring of {OuterPlaces} and {InnerPlaces} is laid out for {OuterPlaces + InnerPlaces} bodies, not {count}");
        var places = new List<Position>();
        for (int k = 0; k < OuterPlaces; k++) places.Add(Center.Along(AngleUtility.ToRadians(360.0 * k / OuterPlaces + Turn.InDegrees), OuterRadius.InMeters));
        for (int k = 0; k < InnerPlaces; k++) places.Add(Center.Along(AngleUtility.ToRadians(360.0 * k / InnerPlaces + Turn.InDegrees), InnerRadius.InMeters));
        return places;
    }

    internal override IReadOnlyList<(int Start, int Count)> Orbits(int count) => new[] { (0, OuterPlaces), (OuterPlaces, InnerPlaces) };

    /// <summary>Both rings one place in that sense.</summary>
    internal override Move Rotate(Sense sense) => OnePlace(sense);

    /// <summary>One ring alone — <c>Outer</c> the orbit 0, <c>Inner</c> the orbit 1 — or both, <c>Whole</c>.</summary>
    internal override Move Rotate(Sense sense, Ring ring)
    {
        if (ring == Ring.Whole) return Rotate(sense);
        int shift = sense == Sense.Clockwise ? -1 : 1;
        string which = ring == Ring.Outer ? "outer" : "inner";
        return new Move($"the {which} ring one step {(sense == Sense.Clockwise ? "clockwise" : "counter-clockwise")}", shift, ring == Ring.Outer ? 0 : 1);
    }
}

/// <summary>A regular POLYGON of places, said by its SIDE (ajuste 65, 30-sep-2026; Juan: "el cuadrado maneja radius, ¿no debería ser
/// lateral/largo del cuadro?"): its vertices on the circle that passes through them — the polygon's own business, never the operator's —
/// and ITS PLACES ARE ITS VERTICES (ajuste 86, 2-oct-2026; Juan, on four bodies spread over a pentagon: "no parece un pentágono… deberían
/// dejar la posición del que falta disponible"): fewer bodies than vertices leave the rest FREE — by rank the last of the order, by distance
/// the one nobody was near — and a step moves the hole with the fleet; more bodies than vertices take the corners first and the sides
/// (ajuste 100). What polygon it is, is the concrete class's: the Square (a corner north-east, its sides square to the map), the Triangle
/// (apex north), the Pentagon (apex north).</summary>
internal abstract class Polygon : Figure
{
    internal Units.Length Side { get; }

    protected Polygon(Position center, Units.Length side) : this(center, side, new Units.Degrees(0.0)) { }

    protected Polygon(Position center, Units.Length side, Units.Angle turn) : base(center, turn)
    {
        if (side == null) throw new GolemDomainException($"{GetType().Name}.{GetType().Name}: 'side' was not given");
        if (side.InMeters <= 0) throw new GolemDomainException($"a {Name} needs a side greater than zero");
        Side = side;
    }

    internal override Units.Length Measure => Side;

    /// <summary>The radius of the circle through the vertices, in metres: the side over twice the sine of half the angle each side
    /// spans from the centre — the side over √2 for a square, over √3 for a triangle.</summary>
    internal double Circumradius => Side.InMeters / (2 * Math.Sin(Math.PI / Bearings.Count));

    /// <summary>The vertices' bearings from the centre, in degrees, the first one first and then counter-clockwise.</summary>
    protected abstract IReadOnlyList<double> Bearings { get; }

    /// <summary>A step along the perimeter (ajuste 84; Juan: "de momento quiero lograrlo para el cuadrado"): the places run
    /// counter-clockwise from the first vertex, so clockwise is one place DOWN the order — the square's and the triangle's rotation; each
    /// may add moves of its own in its class.</summary>
    internal override Move Rotate(Sense sense) => OnePlace(sense);

    /// <summary>The vertices, the first one first, counter-clockwise — turned by the polygon's orientation.</summary>
    internal IReadOnlyList<Position> Vertices()
    {
        var vertices = new List<Position>();
        foreach (double degrees in Bearings)
        {
            vertices.Add(Center.Along(AngleUtility.ToRadians(degrees + Turn.InDegrees), Circumradius));
        }
        return vertices;
    }

    /// <summary>The places for a fleet of that many bodies. Up to as many bodies as vertices, THE VERTICES — the ones nobody takes stay free
    /// (ajuste 86). With MORE bodies than vertices (ajuste 100, 8-oct-2026; Juan: "la opción 2 me parece correcta"): THE CORNERS FIRST, ALWAYS
    /// TAKEN, and the bodies left over on the SIDES — shared out by turns, the sides that get one more spread around the figure (two left on a
    /// square: two opposite sides), each side's points evenly between its two corners. So the figure still reads as itself: six on a square
    /// are its four corners and the middles of two opposite sides; eight, the corners and every middle. The order is the WAY ROUND THE
    /// PERIMETER from the first vertex, counter-clockwise — a corner, the points of its side, the next corner — so a step (one place down or up
    /// the order) moves the whole fleet round the figure and the places stay the same set.</summary>
    internal override IReadOnlyList<Position> Places(int count)
    {
        if (count < 1) throw new GolemDomainException($"{Name}: a formation needs at least one body");
        var v = Vertices();
        int n = v.Count;
        if (count <= n) return v;                             // the vertices, all of them: the ones nobody takes stay free (ajuste 86)
        int left = count - n;
        var onSide = Enumerable.Repeat(left / n, n).ToArray();                // as many on every side as divide evenly
        int rest = left % n;
        for (int i = 0; i < rest; i++)                                       // the rest, one more on sides spread round the figure
            onSide[(int)Math.Round(i * (double)n / rest, MidpointRounding.AwayFromZero) % n]++;
        var places = new List<Position>();
        for (int k = 0; k < n; k++)
        {
            places.Add(v[k]);
            var side = new LineSegment(v[k].AsCoordinate(), v[(k + 1) % n].AsCoordinate());
            for (int j = 1; j <= onSide[k]; j++)
                places.Add(Position.Of(side.PointAlong((double)j / (onSide[k] + 1))));   // evenly between the side's two corners
        }
        return places;
    }
}

/// <summary>A triangle of places: the equilateral triangle of that side, its apex due north — three bodies take the vertices.
/// <c>Triangle(Position(5.5, 5.5), Meters(2.0))</c>.</summary>
internal sealed class Triangle : Polygon
{
    internal Triangle(Position center, Units.Length side) : base(center, side) { }
    internal Triangle(Position center, Units.Length side, Units.Angle turn) : base(center, side, turn) { }
    internal override string Name => "triangle";
    protected override IReadOnlyList<double> Bearings => new[] { 90.0, 210.0, 330.0 };
}

/// <summary>A square of places (29-sep-2026; Juan: "la figura de cuadro"), said by its side (ajuste 65): its sides square to the map,
/// the first corner north-east — four bodies take the corners, eight the corners and the middles of the sides.
/// <c>Square(Position(5.5, 5.5), Meters(2.0))</c>: corners a metre from the centre each way. Its MOVES (ajuste 84): the rotation it
/// inherits from the polygon; the ones to come — the opposite corner, the diagonals swapped — are written here, in the square's class.</summary>
internal sealed class Square : Polygon
{
    internal Square(Position center, Units.Length side) : base(center, side) { }
    internal Square(Position center, Units.Length side, Units.Angle turn) : base(center, side, turn) { }
    internal override string Name => "square";
    protected override IReadOnlyList<double> Bearings => new[] { 45.0, 135.0, 225.0, 315.0 };
}

/// <summary>A pentagon of places (ajuste 85, 2-oct-2026; Juan: "agrega el pentágono como figura… quiero ver cómo se forman las figuras de
/// pentágono con 4 golems"): the regular pentagon of that side, its first vertex due north and the rest counter-clockwise — five bodies
/// take them all; four take the north, north-west, south-west and south-east ones by rank and leave the north-east one free (ajuste 86),
/// the hole turning with the fleet at every step. <c>Pentagon(Position(5.5, 5.5), Meters(2.0))</c>.</summary>
internal sealed class Pentagon : Polygon
{
    internal Pentagon(Position center, Units.Length side) : base(center, side) { }
    internal Pentagon(Position center, Units.Length side, Units.Angle turn) : base(center, side, turn) { }
    internal override string Name => "pentagon";
    protected override IReadOnlyList<double> Bearings => new[] { 90.0, 162.0, 234.0, 306.0, 18.0 };
}
