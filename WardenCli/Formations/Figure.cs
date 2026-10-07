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
/// each moves is the choreography's.</summary>
public abstract class Figure
{
    protected Figure(Spot center) { Center = center; }

    public Spot Center { get; }
    public abstract string Name { get; }

    /// <summary>The places, in the figure's fixed order; a circle spreads as many as asked, a polygon has its vertices.</summary>
    public abstract IReadOnlyList<Spot> Places(int count);

    /// <summary>The bearing of a place from the centre, in degrees from due east, counter-clockwise.</summary>
    public abstract double Bearing(int index, int count);

    /// <summary>The same figure — the same kind, the same measure — with its centre elsewhere (propuesta 97: a formation moved on the map).</summary>
    public abstract Figure At(Spot center);

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
}

/// <summary>A regular polygon said by its SIDE: its vertices on the circle through them, at the bearings each polygon declares.</summary>
public abstract class Polygon : Figure
{
    protected Polygon(Spot center, double side) : base(center)
    {
        if (side <= 0) throw new ArgumentException($"a {Name} needs a side above zero");
        Side = side;
    }

    public double Side { get; }
    protected abstract double[] Bearings { get; }

    public override double Bearing(int index, int count) =>
        index >= 0 && index < Bearings.Length ? Bearings[index] : throw new ArgumentException($"a {Name} has {Bearings.Length} vertices: no vertex {index}");

    /// <summary>The radius of the circle through the vertices: the side over twice the sine of half the angle each side spans.</summary>
    public double Circumradius => Side / (2 * Math.Sin(Math.PI / Bearings.Length));

    public IReadOnlyList<Spot> Vertices() => Bearings.Select(deg =>
    {
        double a = deg * Math.PI / 180;
        return new Spot(Math.Round(Center.X + Circumradius * Math.Cos(a), 3), Math.Round(Center.Y + Circumradius * Math.Sin(a), 3));
    }).ToList();

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
    public Square(Spot center, double side) : base(center, side) { }
    public override string Name => "square";
    public override Figure At(Spot center) => new Square(center, Side);
    protected override double[] Bearings => new[] { 45.0, 135.0, 225.0, 315.0 };
}

public sealed class Triangle : Polygon
{
    public Triangle(Spot center, double side) : base(center, side) { }
    public override string Name => "triangle";
    public override Figure At(Spot center) => new Triangle(center, Side);
    protected override double[] Bearings => new[] { 90.0, 210.0, 330.0 };
}

public sealed class Pentagon : Polygon
{
    public Pentagon(Spot center, double side) : base(center, side) { }
    public override string Name => "pentagon";
    public override Figure At(Spot center) => new Pentagon(center, Side);
    protected override double[] Bearings => new[] { 90.0, 162.0, 234.0, 306.0, 18.0 };
}

/// <summary>A circle said by its radius: as many places as bodies, evenly spaced from due east, counter-clockwise.</summary>
public sealed class Circle : Figure
{
    public Circle(Spot center, double radius) : base(center)
    {
        if (radius <= 0) throw new ArgumentException("a circle needs a radius above zero");
        Radius = radius;
    }

    public double Radius { get; }
    public override string Name => "circle";
    public override Figure At(Spot center) => new Circle(center, Radius);

    public override double Bearing(int index, int count) =>
        count >= 1 && index >= 0 && index < count ? 360.0 * index / count : throw new ArgumentException($"a circle of {count} has no place {index}");

    public override IReadOnlyList<Spot> Places(int count)
    {
        if (count < 1) throw new ArgumentException("a circle needs at least one body");
        var places = new List<Spot>();
        for (int k = 0; k < count; k++)
        {
            double a = 2 * Math.PI * k / count;
            places.Add(new Spot(Math.Round(Center.X + Radius * Math.Cos(a), 3), Math.Round(Center.Y + Radius * Math.Sin(a), 3)));
        }
        return places;
    }
}
