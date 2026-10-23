using System.Globalization;
using System.Text.RegularExpressions;
using GolemDomain.Geometry;
using GolemDomain.Units;

namespace GolemDomain.Formations;

/// <summary>
/// THE SCORE OF A UNION, READ FROM ITS DEFINITION (propuesta 124, 22-oct-2026, night; Juan: "yo quisiera un solo comando con toda la definición,
/// viendo los tabs como un solo union… el dominio debería crear el objeto completo del sindicato y el dominio entiende que debe terminar creando los
/// dos objetos de las formaciones con todas sus instrucciones que deben seguir del sindicato"): one text — like the maze, drawn in text — says every
/// formation of the union in order, its figure and its PART of the score, the steps it dances beat by beat:
/// <c>tri-left: triangle 3,5.5 side 3 angle 0 places 3 = cw,cw,cw; tri-right: triangle 8,5.5 side 3 angle 0 places 3 = cw,cw,-</c>
/// — a part per formation, separated by <c>;</c>; its name, then the figure (triangle, square, pentagon, circle), its centre <c>x,y</c>, its
/// measure (<c>side</c> for a polygon, <c>radius</c> for a circle), an optional <c>angle</c> in degrees, how many golems it takes (<c>places</c>;
/// left out, a polygon's vertices), and after <c>=</c> its steps, one per beat: <c>cw</c>, <c>ccw</c> (clockwise, counterclockwise) or <c>-</c>, the
/// REST — that beat the formation stays. Read, never kept: the union builds its formations from it (<see cref="Choreographies.Program"/>).
/// </summary>
internal sealed class Score
{
    private static readonly Regex Head = new(@"^(?<figure>[a-z]+)\s+(?<x>-?\d+(\.\d+)?),(?<y>-?\d+(\.\d+)?)(?<rest>.*)$", RegexOptions.Compiled);
    private static readonly Regex Pair = new(@"^(?<key>side|radius|angle|places)\s+(?<value>-?\d+(\.\d+)?)$", RegexOptions.Compiled);

    internal IReadOnlyList<Part> Parts { get; }

    private Score(IReadOnlyList<Part> parts) => Parts = parts;

    /// <summary>The names of the formations, in the order of the score, as one comma list — what the union is told.</summary>
    internal string Bill => string.Join(",", Parts.Select(p => p.Name));

    internal static Score Read(string definition)
    {
        if (definition == null) throw new GolemDomainException("Score.Read: 'definition' was not given");
        var parts = new List<Part>();
        foreach (var raw in definition.Split(';'))
        {
            string text = raw.Trim();
            if (text == "") continue;
            int colon = text.IndexOf(':');
            if (colon <= 0) throw new GolemDomainException($"a part of the score names its formation first, like 'tri-left: triangle 3,5.5 side 3 places 3 = cw,cw'; found '{text}'");
            string name = text[..colon].Trim().ToLowerInvariant();
            if (!Regex.IsMatch(name, @"^[a-z_][a-z0-9_-]*$")) throw new GolemDomainException($"'{name}' is no name for a formation");
            if (parts.Any(p => p.Name == name)) throw new GolemDomainException($"the score names {name} twice");
            string body = text[(colon + 1)..].Trim();
            string steps = "";
            int eq = body.IndexOf('=');
            if (eq >= 0) { steps = body[(eq + 1)..].Trim(); body = body[..eq].Trim(); }
            var head = Head.Match(body.ToLowerInvariant());
            if (!head.Success) throw new GolemDomainException($"{name}: expected its figure and centre, like 'triangle 3,5.5 side 3 places 3'; found '{body}'");
            string figure = head.Groups["figure"].Value;
            if (figure is not ("triangle" or "square" or "pentagon" or "circle")) throw new GolemDomainException($"{name}: a union's formation is a triangle, a square, a pentagon or a circle, not a {figure}");
            var center = new Position(double.Parse(head.Groups["x"].Value, CultureInfo.InvariantCulture), double.Parse(head.Groups["y"].Value, CultureInfo.InvariantCulture));
            double? side = null, radius = null, angle = null; int places = 0;
            foreach (var pair in Regex.Split(head.Groups["rest"].Value.Trim(), @"\s+(?=(?:side|radius|angle|places)\b)").Where(s => s.Trim() != ""))
            {
                var m = Pair.Match(pair.Trim());
                if (!m.Success) throw new GolemDomainException($"{name}: '{pair.Trim()}' is no measure — side, radius, angle or places, each with a number");
                double value = double.Parse(m.Groups["value"].Value, CultureInfo.InvariantCulture);
                switch (m.Groups["key"].Value)
                {
                    case "side": side = value; break;
                    case "radius": radius = value; break;
                    case "angle": angle = value; break;
                    default:
                        if (value < 1 || value != Math.Floor(value)) throw new GolemDomainException($"{name}: places is a whole number above zero");
                        places = (int)value; break;
                }
            }
            if (figure == "circle" && radius == null) throw new GolemDomainException($"{name}: a circle is said by its radius");
            if (figure != "circle" && side == null) throw new GolemDomainException($"{name}: a {figure} is said by its side");
            if (figure == "circle" && side != null || figure != "circle" && radius != null) throw new GolemDomainException($"{name}: a {figure} has {(figure == "circle" ? "a radius, not a side" : "a side, not a radius")}");
            if (figure == "circle" && places == 0) throw new GolemDomainException($"{name}: a circle is told for how many golems (places)");
            var beats = new List<Sense?>();
            foreach (var step in steps.Split(',').Select(s => s.Trim().ToLowerInvariant()).Where(s => s != ""))
                beats.Add(step switch
                {
                    "cw" or "clockwise" => Sense.Clockwise,
                    "ccw" or "counterclockwise" => Sense.Counterclockwise,
                    "-" or "rest" or "stay" => null,
                    _ => throw new GolemDomainException($"{name}: a step is cw, ccw or - (a rest), not '{step}'"),
                });
            parts.Add(new Part(name, figure, center, new Meters(side ?? radius ?? 0.0), new Degrees(angle ?? 0.0), places, beats));
        }
        if (parts.Count == 0) throw new GolemDomainException("a score names at least one formation");
        return new Score(parts);
    }

    /// <summary>How many beats the longest part has.</summary>
    internal int Beats => Parts.Count == 0 ? 0 : Parts.Max(p => p.Steps.Count);
}

/// <summary>One formation's part of the score: its figure and its steps, one per beat — a sense, or null for a rest.</summary>
internal sealed class Part
{
    internal string Name { get; }
    internal string Figure { get; }
    internal Position Center { get; }
    internal Length Measure { get; }
    internal Angle Turn { get; }
    internal int Places { get; }
    internal IReadOnlyList<Sense?> Steps { get; }

    internal Part(string name, string figure, Position center, Length measure, Angle turn, int places, IReadOnlyList<Sense?> steps)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("Part.Part: 'name' was not given");
        if (string.IsNullOrWhiteSpace(figure)) throw new GolemDomainException("Part.Part: 'figure' was not given");
        if (center == null) throw new GolemDomainException("Part.Part: 'center' was not given");
        if (measure == null) throw new GolemDomainException("Part.Part: 'measure' was not given");
        if (turn == null) throw new GolemDomainException("Part.Part: 'turn' was not given");
        if (steps == null) throw new GolemDomainException("Part.Part: 'steps' was not given");
        Name = name; Figure = figure; Center = center; Measure = measure; Turn = turn; Places = places; Steps = steps;
    }

    /// <summary>The part as text, one step per beat: cw, ccw or -.</summary>
    internal string StepsText => string.Join(",", Steps.Select(s => s == null ? "-" : s == Sense.Clockwise ? "cw" : "ccw"));
}
