using GolemDomain.Geometry;

using NetTopologySuite.Algorithm;
using NetTopologySuite.LinearReferencing;
using GeometryFactory = NetTopologySuite.Geometries.GeometryFactory;
using LineSegment = NetTopologySuite.Geometries.LineSegment;

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

    /// <summary>Its ORIENTATION (propuesta 99, 8-oct-2026): how far it is turned counter-clockwise from the way it is laid out — added to every
    /// place's bearing, the order of the places unchanged. Zero for the figures of the golem's own choreography; a circle has one too since
    /// ajuste 102.</summary>
    internal Units.Angle Turn { get; }

    protected Formation(Position center) : this(center, new Units.Degrees(0.0)) { }

    protected Formation(Position center, Units.Angle turn)
    {
        if (center == null) throw new GolemDomainException("Formation.Formation: 'center' was not given");
        if (turn == null) throw new GolemDomainException("Formation.Formation: 'turn' was not given");
        Center = center;
        Turn = turn;
    }

    /// <summary>What says its size: a polygon's side, a circle's radius (ajuste 102) — what the formations read prints.</summary>
    internal abstract Units.Length Measure { get; }

    /// <summary>How many bodies the warden laid it out for (ajuste 101, 8-oct-2026; Juan: "hagamos lo mismo en el CLI") — 0 when it was not
    /// said: then a polygon's places are its vertices. A circle told by the warden is always told for how many (ajuste 102).</summary>
    internal int Bodies { get; set; }

    /// <summary>Its places AS TOLD — a polygon's vertices, or, laid out for more bodies than vertices, the corners and the bodies left over on
    /// the sides (ajuste 100); a circle's places for the bodies it was told — in the figure's fixed order.</summary>
    internal IReadOnlyList<Position> Places() => Places(Math.Max(Bodies, 1));

    /// <summary>How many places it has as told.</summary>
    internal int PlaceCount => Places().Count;

    /// <summary>The place of that NUMBER among its places as told (ajuste 101): what a golem told "your place is 5" resolves by itself. Out of
    /// range refused.</summary>
    internal Position PlaceNumbered(int index)
    {
        var places = Places();
        if (index < 0 || index >= places.Count) throw new GolemDomainException($"{(Called == "" ? "the " + Name : Called)} has places 0 to {places.Count - 1}: there is no place {index}");
        return places[index];
    }

    /// <summary>THE WAY OF A STEP (ajuste 102, 8-oct-2026; Juan, on the double ring sent as visits: "no con el contexto del doble anillo para
    /// decirle cuál es su posición del vértice que le pertenece"): the stops a body takes from where it stands to a place of the figure when it
    /// moves round it in that sense — the place alone on a polygon (a step runs along a side), along the ARC on a circle (<see cref="Circle"/>):
    /// two bodies on a ring are half a turn apart, and the chord would send both through the centre at once.</summary>
    internal virtual IReadOnlyList<Position> Way(Position from, Position place, Sense sense)
    {
        if (from == null) throw new GolemDomainException("Formation.Way: 'from' was not given");
        if (place == null) throw new GolemDomainException("Formation.Way: 'place' was not given");
        return new[] { place };
    }

    /// <summary>What the journal and the panel call this figure.</summary>
    internal abstract string Name { get; }

    /// <summary>The name the warden gave this formation (propuesta 99: <c>square-1</c>), "" for a figure nobody named — the convocations'.</summary>
    internal string Called { get; set; } = "";

    /// <summary>The place of the vertex of that NUMBER, in the figure's fixed order (propuesta 99) — what a golem told "your vertex is 2"
    /// resolves by itself. A figure whose places hang on how many bodies there are (the circle) has no vertex by number.</summary>
    internal virtual Position Vertex(int index) => throw new GolemDomainException($"a {Name}'s places depend on how many bodies take it: it has no vertex by number");

    /// <summary>The places of this figure for a fleet of that many bodies, in a fixed order.</summary>
    internal abstract IReadOnlyList<Position> Places(int count);

    /// <summary>The place a member of the fleet takes: the one of its rank among as many places as the fleet has bodies.</summary>
    internal Position Place(Member member)
    {
        if (member == null) throw new GolemDomainException("Formation.Place: 'member' was not given");
        return Places(member.Of)[member.Rank];
    }

    /// <summary>The figure's ROTATION as a <see cref="Move"/> (ajuste 84; Juan: "que la figura como tal tenga la coreografía"): every body
    /// to the next place in that sense, <c>clockwise</c> or <c>counterclockwise</c> — <c>move = muster.Formation.Rotate(@sense)</c>. A figure
    /// that does not rotate refuses; the ones whose places run around a centre say what a step is (<see cref="Polygon"/>, <see cref="Circle"/>).
    /// Every figure grows its own moves here, in its class, never in the convocation.</summary>
    internal virtual Move Rotate(Sense sense) => throw new GolemDomainException($"a {Name} does not rotate");

    /// <summary>One place along the figure's fixed order in that sense: the places run counter-clockwise, so clockwise is one place DOWN the
    /// order. The sense is the closed set <see cref="Sense"/> (ajuste 90), never a string.</summary>
    protected static Move OnePlace(Sense sense) =>
        sense == Sense.Clockwise ? new Move("one step clockwise", -1) : new Move("one step counter-clockwise", 1);
}

/// <summary>A circle of places around a centre, said by its radius: N bodies evenly spaced from due east, counter-clockwise — three
/// make a triangle, four a square. <c>Circle(Position(5.5, 5.5), Meters(1.0))</c>. Set aside from the command line for now (ajuste 65:
/// the square alone), kept in the repertoire.</summary>
internal sealed class Circle : Formation
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

    /// <summary>The way of a step round the circle (ajuste 102): ALONG THE ARC from where the body stands — its bearing from the centre — to
    /// the place, in that sense, through points of the circle no more than 60° apart, the place last; a body already there goes straight.</summary>
    internal override IReadOnlyList<Position> Way(Position from, Position place, Sense sense)
    {
        if (from == null) throw new GolemDomainException("Circle.Way: 'from' was not given");
        if (place == null) throw new GolemDomainException("Circle.Way: 'place' was not given");
        double start = AngleUtility.ToDegrees(AngleUtility.Angle(Center.AsCoordinate(), from.AsCoordinate()));
        double end = AngleUtility.ToDegrees(AngleUtility.Angle(Center.AsCoordinate(), place.AsCoordinate()));
        double delta = ((end - start) % 360 + 360) % 360;                 // counter-clockwise, in [0, 360)
        if (delta < 1e-6 || 360 - delta < 1e-6) return new[] { place };
        if (sense == Sense.Clockwise) delta -= 360;                       // clockwise, the other way round
        int segments = (int)Math.Ceiling(Math.Abs(delta) / 60 - 1e-9);
        var way = new List<Position>();
        for (int k = 1; k < segments; k++)
            way.Add(Center.Along(AngleUtility.ToRadians(start + delta * k / segments), Radius.InMeters));
        way.Add(place);
        return way;
    }

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

/// <summary>A regular POLYGON of places, said by its SIDE (ajuste 65, 30-sep-2026; Juan: "el cuadrado maneja radius, ¿no debería ser
/// lateral/largo del cuadro?"): its vertices on the circle that passes through them — the polygon's own business, never the operator's —
/// and ITS PLACES ARE ITS VERTICES (ajuste 86, 2-oct-2026; Juan, on four bodies spread over a pentagon: "no parece un pentágono… deberían
/// dejar la posición del que falta disponible"): fewer bodies than vertices leave the rest FREE — by rank the last of the order, by distance
/// the one nobody was near — and a step moves the hole with the fleet; more bodies than vertices are spread evenly along the PERIMETER from
/// the first vertex, counter-clockwise (ajuste 65: eight on a square take the corners and the middles of the sides). What polygon it is, is
/// the concrete class's: the Square (a corner north-east, its sides square to the map), the Triangle (apex north), the Pentagon (apex north).</summary>
internal abstract class Polygon : Formation
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

    /// <summary>The vertex of that number (propuesta 99): 0 the first — the square's north-east, the pentagon's and the triangle's north, before
    /// any turn —, then counter-clockwise. A number the polygon does not have is refused.</summary>
    internal override Position Vertex(int index)
    {
        var vertices = Vertices();
        if (index < 0 || index >= vertices.Count) throw new GolemDomainException($"a {Name} has vertices 0 to {vertices.Count - 1}: there is no vertex {index}");
        return vertices[index];
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

/// <summary>A triangle of places: the equilateral triangle of that side, its apex due north, the fleet spread along its perimeter from
/// the apex — three bodies take the vertices. <c>Triangle(Position(5.5, 5.5), Meters(2.0))</c>. Set aside from the command line for
/// now (ajuste 65: the square alone), kept in the repertoire.</summary>
internal sealed class Triangle : Polygon
{
    internal Triangle(Position center, Units.Length side) : base(center, side) { }
    internal Triangle(Position center, Units.Length side, Units.Angle turn) : base(center, side, turn) { }
    internal override string Name => "triangle";
    protected override IReadOnlyList<double> Bearings => new[] { 90.0, 210.0, 330.0 };
}

/// <summary>A square of places (29-sep-2026; Juan: "la figura de cuadro"), said by its side (ajuste 65): its sides square to the map,
/// the first corner north-east, the fleet spread along its perimeter — four bodies take the corners, eight the corners and the middles
/// of the sides. <c>Square(Position(5.5, 5.5), Meters(2.0))</c>: corners a metre from the centre each way. Its MOVES (ajuste 84): the
/// rotation it inherits from the polygon (<c>Rotate(sense)</c>: the next corner in that sense); the ones to come — the opposite corner, the
/// diagonals swapped — are written here, in the square's class.</summary>
internal sealed class Square : Polygon
{
    internal Square(Position center, Units.Length side) : base(center, side) { }
    internal Square(Position center, Units.Length side, Units.Angle turn) : base(center, side, turn) { }
    internal override string Name => "square";
    protected override IReadOnlyList<double> Bearings => new[] { 45.0, 135.0, 225.0, 315.0 };
}

/// <summary>A pentagon of places (ajuste 85, 2-oct-2026; Juan: "agrega el pentágono como figura… quiero ver cómo se forman las figuras de
/// pentágono con 4 golems"): the regular pentagon of that side, its first vertex due north and the rest counter-clockwise, the fleet
/// places its vertices — five bodies take them all; four take the north, north-west, south-west and south-east ones by rank and leave the
/// north-east one free (ajuste 86), the hole turning with the fleet at every step. <c>Pentagon(Position(5.5, 5.5), Meters(2.0))</c>.</summary>
internal sealed class Pentagon : Polygon
{
    internal Pentagon(Position center, Units.Length side) : base(center, side) { }
    internal Pentagon(Position center, Units.Length side, Units.Angle turn) : base(center, side, turn) { }
    internal override string Name => "pentagon";
    protected override IReadOnlyList<double> Bearings => new[] { 90.0, 162.0, 234.0, 306.0, 18.0 };
}
