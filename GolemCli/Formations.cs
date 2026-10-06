namespace GolemCli;

/// <summary>Formations LAID OUT BY THE CONSOLE (propuesta 93): the CLI computes the places of a figure and writes, for every golem, the
/// queue of plain errands that takes it there and around — <c>visit x,y</c> lines, one per place, as many as the steps asked. This is the
/// console's own way to make figures, APART from the golem's choreography module (`choreograph`, `rotate`), which stays as it is: there the
/// golems agree among themselves; here the operator sees and edits every command before it goes. The places follow the domain's order so
/// both ways draw the same figure: a square's first corner north-east, a pentagon's first vertex north, a circle from due east — all
/// counter-clockwise; the places are given BY RANK, the names sorted.</summary>
public static class Formations
{
    public static readonly string[] Figures = { "square", "pentagon", "triangle", "circle" };

    /// <summary>The places of the figure, in its fixed order: a polygon by its SIDE, a circle by its RADIUS.</summary>
    public static List<(double X, double Y)> Places(string figure, double cx, double cy, double measure, int count)
    {
        var places = new List<(double, double)>();
        switch (figure)
        {
            case "square": return Polygon(cx, cy, measure, new[] { 45.0, 135.0, 225.0, 315.0 });
            case "triangle": return Polygon(cx, cy, measure, new[] { 90.0, 210.0, 330.0 });
            case "pentagon": return Polygon(cx, cy, measure, new[] { 90.0, 162.0, 234.0, 306.0, 18.0 });
            case "circle":
                int n = Math.Max(1, count);
                for (int k = 0; k < n; k++)
                {
                    double a = 2 * Math.PI * k / n;
                    places.Add((Round(cx + measure * Math.Cos(a)), Round(cy + measure * Math.Sin(a))));
                }
                return places;
            default: throw new ArgumentException($"no figure named '{figure}'");
        }
    }

    // a regular polygon of that side: its vertices on the circle through them, at the bearings given
    private static List<(double, double)> Polygon(double cx, double cy, double side, double[] bearings)
    {
        double circumradius = side / (2 * Math.Sin(Math.PI / bearings.Length));
        return bearings.Select(deg => { double a = deg * Math.PI / 180; return (Round(cx + circumradius * Math.Cos(a)), Round(cy + circumradius * Math.Sin(a))); }).ToList();
    }

    /// <summary>The queue for every golem: its place by rank (names sorted), then — for each step — the next place around the figure,
    /// clockwise down the order or counter-clockwise up it; the hole of a free vertex moves with the fleet, as in the domain.</summary>
    public static Dictionary<string, List<string>> Queues(string figure, double cx, double cy, double measure, IReadOnlyList<string> golems, int steps, bool clockwise)
    {
        var names = golems.OrderBy(n => n, StringComparer.Ordinal).ToList();
        var places = Places(figure, cx, cy, measure, names.Count);
        if (names.Count > places.Count) throw new ArgumentException($"a {figure} has {places.Count} places: {names.Count} golems do not fit — the console lays out the vertices only");
        var queues = names.ToDictionary(n => n, _ => new List<string>());
        var index = names.Select((_, i) => i).ToList();
        for (int s = 0; s <= steps; s++)
        {
            for (int g = 0; g < names.Count; g++)
            {
                var (x, y) = places[index[g]];
                queues[names[g]].Add($"visit {Fmt(x)},{Fmt(y)}");
            }
            for (int g = 0; g < names.Count; g++)
                index[g] = ((index[g] + (clockwise ? -1 : 1)) % places.Count + places.Count) % places.Count;
        }
        return queues;
    }

    private static double Round(double v) => Math.Round(v, 3);
    private static string Fmt(double v) => v.ToString("0.###", System.Globalization.CultureInfo.InvariantCulture);
}
