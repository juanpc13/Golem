namespace WardenCli.Formations;

/// <summary>A point on the floor, as the console reckons it: metres, x east, y north.</summary>
public readonly record struct Spot(double X, double Y)
{
    public double DistanceTo(Spot other) => Math.Sqrt((X - other.X) * (X - other.X) + (Y - other.Y) * (Y - other.Y));
    public override string ToString() => $"{Fmt(X)},{Fmt(Y)}";
    public static string Fmt(double v) => Math.Round(v, 3).ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}

/// <summary>THE FIGURE the console lays out (propuesta 95, 6-oct-2026; Juan: "el CLI en su dominio tiene que poder manejar lógica similar
/// al otro del golem para poder crear todos los scripts"): a centre and its PLACES in a FIXED ORDER, the same order the golem's own module
/// uses so both ways draw the same figure — a square's first corner north-east, a pentagon's first vertex north, a triangle's apex north, a
/// circle from due east; all counter-clockwise. The console's figures are geometry alone: who takes which place is the assignment's, when
/// each moves is the choreography's.
///
/// Since propuesta 98 (7-oct-2026; Juan: "poder hacer cosas como rotar y redimensionar esa figura de la formación") a figure also has its
/// ORIENTATION — <see cref="Angle"/>, degrees counter-clockwise added to every bearing, 0 the figure as the golem's module lays it — and its
/// MEASURE says what makes it bigger (a polygon's side, a circle's radius); the places keep their order whatever the angle.</summary>
public abstract class Figure
{
    protected Figure(Spot center, double angle)
    {
        Center = center;
        Angle = ((angle % 360) + 360) % 360;
    }

    public Spot Center { get; }

    /// <summary>The orientation: degrees counter-clockwise added to every bearing, in [0, 360); 0 is the figure as the golem's module lays it.</summary>
    public double Angle { get; }

    public abstract string Name { get; }

    /// <summary>What says the figure's size: a polygon's side, a circle's radius, in metres.</summary>
    public abstract double Measure { get; }

    /// <summary>How far a place stands from the centre: the radius of the circle through the places.</summary>
    public abstract double Reach { get; }

    /// <summary>The measure that puts the places that far from the centre (a polygon's side for that circumradius, a circle's radius).</summary>
    public abstract double MeasureFor(double reach);

    /// <summary>The places, in the figure's fixed order; a circle spreads as many as asked, a polygon has its vertices.</summary>
    public abstract IReadOnlyList<Spot> Places(int count);

    /// <summary>The bearing of a place from the centre, in degrees from due east, counter-clockwise, the orientation included.</summary>
    public abstract double Bearing(int index, int count);

    /// <summary>The same KIND of figure with this centre, measure and orientation — what a move, a resize and a turn are made of.</summary>
    public abstract Figure With(Spot center, double measure, double angle);

    /// <summary>The same figure with its centre elsewhere (propuesta 97: a formation moved on the map).</summary>
    public Figure At(Spot center) => With(center, Measure, Angle);

    /// <summary>The same figure bigger or smaller (propuesta 98): a polygon by its side, a circle by its radius.</summary>
    public Figure Sized(double measure) => With(Center, measure, Angle);

    /// <summary>The same figure turned to this orientation (propuesta 98), degrees counter-clockwise.</summary>
    public Figure Oriented(double angle) => With(Center, Measure, angle);

    /// <summary>What the operator calls a vertex: its compass point from the centre (NE, N, NW…), the nearest of eight (propuesta 96).</summary>
    public string Label(int index, int count)
    {
        string[] points = { "E", "NE", "N", "NW", "W", "SW", "S", "SE" };
        double deg = ((Bearing(index, count) % 360) + 360) % 360;
        return points[(int)Math.Round(deg / 45) % 8];
    }

    /// <summary>The figure named, with its natural measure: a polygon by its side, a circle by its radius.</summary>
    public static Figure Named(string name, Spot center, double measure) => name.Trim().ToLowerInvariant() switch
    {
        "square" => new Square(center, measure),
        "pentagon" => new Pentagon(center, measure),
        "triangle" => new Triangle(center, measure),
        "circle" => new Circle(center, measure),
        _ => throw new ArgumentException($"no figure named '{name}': square, pentagon, triangle or circle"),
    };

    public static readonly string[] Known = { "square", "pentagon", "triangle", "circle" };

    protected Spot AtBearing(double degrees, double reach)
    {
        double a = degrees * Math.PI / 180;
        return new Spot(Math.Round(Center.X + reach * Math.Cos(a), 3), Math.Round(Center.Y + reach * Math.Sin(a), 3));
    }
}

/// <summary>A regular polygon said by its SIDE: its vertices on the circle through them, at the bearings each polygon declares, turned by
/// its orientation.</summary>
public abstract class Polygon : Figure
{
    protected Polygon(Spot center, double side, double angle) : base(center, angle)
    {
        if (side <= 0) throw new ArgumentException($"a {Name} needs a side above zero");
        Side = side;
    }

    public double Side { get; }
    public override double Measure => Side;
    public override double Reach => Circumradius;
    public override double MeasureFor(double reach) => reach * 2 * Math.Sin(Math.PI / Bearings.Length);
    protected abstract double[] Bearings { get; }

    public override double Bearing(int index, int count) =>
        index >= 0 && index < Bearings.Length ? Bearings[index] + Angle : throw new ArgumentException($"a {Name} has {Bearings.Length} vertices: no vertex {index}");

    /// <summary>The radius of the circle through the vertices: the side over twice the sine of half the angle each side spans.</summary>
    public double Circumradius => Side / (2 * Math.Sin(Math.PI / Bearings.Length));

    public IReadOnlyList<Spot> Vertices() => Bearings.Select(deg => AtBearing(deg + Angle, Circumradius)).ToList();

    /// <summary>The vertices, all of them, while the bodies do not exceed them — fewer bodies leave vertices free, as the golem's module does
    /// (ajuste 86); more bodies than vertices are refused: the console lays out vertices only.</summary>
    public override IReadOnlyList<Spot> Places(int count)
    {
        var v = Vertices();
        if (count > v.Count) throw new ArgumentException($"a {Name} has {v.Count} places: {count} golems do not fit — the console lays out the vertices only");
        return v;
    }
}

public sealed class Square : Polygon
{
    public Square(Spot center, double side, double angle = 0) : base(center, side, angle) { }
    public override string Name => "square";
    public override Figure With(Spot center, double measure, double angle) => new Square(center, measure, angle);
    protected override double[] Bearings => new[] { 45.0, 135.0, 225.0, 315.0 };
}

public sealed class Triangle : Polygon
{
    public Triangle(Spot center, double side, double angle = 0) : base(center, side, angle) { }
    public override string Name => "triangle";
    public override Figure With(Spot center, double measure, double angle) => new Triangle(center, measure, angle);
    protected override double[] Bearings => new[] { 90.0, 210.0, 330.0 };
}

public sealed class Pentagon : Polygon
{
    public Pentagon(Spot center, double side, double angle = 0) : base(center, side, angle) { }
    public override string Name => "pentagon";
    public override Figure With(Spot center, double measure, double angle) => new Pentagon(center, measure, angle);
    protected override double[] Bearings => new[] { 90.0, 162.0, 234.0, 306.0, 18.0 };
}

/// <summary>A circle said by its radius: as many places as bodies, evenly spaced from due east (turned by its orientation), counter-clockwise.</summary>
public sealed class Circle : Figure
{
    public Circle(Spot center, double radius, double angle = 0) : base(center, angle)
    {
        if (radius <= 0) throw new ArgumentException("a circle needs a radius above zero");
        Radius = radius;
    }

    public double Radius { get; }
    public override string Name => "circle";
    public override double Measure => Radius;
    public override double Reach => Radius;
    public override double MeasureFor(double reach) => reach;
    public override Figure With(Spot center, double measure, double angle) => new Circle(center, measure, angle);

    public override double Bearing(int index, int count) =>
        count >= 1 && index >= 0 && index < count ? 360.0 * index / count + Angle : throw new ArgumentException($"a circle of {count} has no place {index}");

    public override IReadOnlyList<Spot> Places(int count)
    {
        if (count < 1) throw new ArgumentException("a circle needs at least one body");
        return Enumerable.Range(0, count).Select(k => AtBearing(360.0 * k / count + Angle, Radius)).ToList();
    }
}
