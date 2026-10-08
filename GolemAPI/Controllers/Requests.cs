using System.Globalization;

namespace GolemAPI.Controllers;

// What the operator sends the golem, as JSON bodies — typed, and VALIDATED before anything reaches the journal (Juan,
// 16-sep-2026: "la data debería llegar por JSON… y hay que validar que venga correcta"). Each request says what is
// wrong with it in plain words; the endpoint answers 400 with that and performs nothing.

/// <summary>One stop of an errand: a point on the floor — <c>{"x": 9.0, "y": 8.0}</c> (Juan, 16-sep-2026: "solo serán puntos, ya no places").</summary>
public sealed record StopRequest(double? X, double? Y)
{
    public IEnumerable<string> Problems(int position)
    {
        if (!X.HasValue || !Y.HasValue) yield return $"stop {position}: a point needs both x and y";
        else if (!double.IsFinite(X.Value) || !double.IsFinite(Y.Value)) yield return $"stop {position}: x and y must be finite numbers";
    }

    public string Describe() => $"({(X ?? 0).ToString("0.##", CultureInfo.InvariantCulture)}, {(Y ?? 0).ToString("0.##", CultureInfo.InvariantCulture)})";
}

/// <summary>An errand: the points to reach, in order — <c>{"stops": [{"x": 2.0, "y": 9.5}, {"x": 9.0, "y": 8.0}]}</c>.</summary>
public sealed record ErrandRequest(List<StopRequest> Stops)
{
    public const int MostStops = 12;
    public const string Shape = "{\"stops\": [{\"x\": 2.0, \"y\": 9.5}, {\"x\": 9.0, \"y\": 8.0}]}";

    public IEnumerable<string> Problems()
    {
        if (Stops == null || Stops.Count == 0) { yield return "give at least one stop: " + Shape; yield break; }
        if (Stops.Count > MostStops) yield return $"at most {MostStops} stops in one errand";
        for (int i = 0; i < Stops.Count; i++)
        {
            if (Stops[i] == null) { yield return $"stop {i + 1}: is empty"; continue; }
            foreach (var problem in Stops[i].Problems(i + 1)) yield return problem;
        }
    }
}

/// <summary>A point on the floor — <c>{"x": 5.2, "y": 5.8}</c> — what the operator points at to forget an obstacle.</summary>
public sealed record PointRequest(double? X, double? Y)
{
    public const string Shape = "{\"x\": 5.2, \"y\": 5.8}";

    public IEnumerable<string> Problems()
    {
        if (!X.HasValue || !Y.HasValue) yield return "give both x and y: " + Shape;
        else if (!double.IsFinite(X.Value) || !double.IsFinite(Y.Value)) yield return "x and y must be finite numbers";
    }
}

/// <summary>The strategy the golem optimizes its routes by (ajuste 61; `optimize`, ajuste 82) — <c>{"navigation": "on-the-way"}</c> or <c>door-by-door</c>.</summary>
public sealed record NavigationRequest(string Navigation)
{
    public const string Shape = "{\"navigation\": \"on-the-way\"}";
    public static readonly string[] Strategies = { "on-the-way", "door-by-door" };

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Navigation) || !Strategies.Contains(Navigation.Trim().ToLowerInvariant())) yield return "the navigation must be one of " + string.Join(", ", Strategies) + ": " + Shape;
    }
}

/// <summary>The scenario the golem enters (ajuste 60) — <c>{"scenario": "open-floor"}</c>.</summary>
public sealed record ScenarioRequest(string Scenario)
{
    public const string Shape = "{\"scenario\": \"open-floor\"}";

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Scenario)) yield return "give the scenario's name: " + Shape;
    }
}

/// <summary>The fleet called to a SQUARE (propuesta 59; ajuste 65) — <c>{"figure": "square", "center": {"x": 5.5, "y": 5.5}, "side": 2.0,
/// "by": "rank", "fleet": ["blue", "red"]}</c>: said by its side; the places taken by rank or by distance (ajuste 73); the fleet may be
/// left out (this golem and every peer it can reach). The timed turn (effect, for) is gone: a step is <see cref="RotateRequest"/> (ajuste 77).</summary>
public sealed record FormationRequest(string Figure, PointRequest Center, double? Side, List<string> Fleet, string By = null, double? Radius = null,
                                      string Effect = null, double? For = null)
{
    public const string Shape = "{\"figure\": \"square\", \"center\": {\"x\": 5.5, \"y\": 5.5}, \"side\": 2.0, \"by\": \"rank\", \"fleet\": [\"blue\", \"red\"]}";
    public static readonly string[] Figures = { "square", "pentagon" };   // the circle and the triangle set aside for now (ajuste 65); the pentagon since ajuste 85

    /// <summary>How the places are shared: by rank, the default, or by distance.</summary>
    public string Policy => string.IsNullOrWhiteSpace(By) ? "rank" : By.Trim().ToLowerInvariant();

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Figure) || !Figures.Contains(Figure.Trim().ToLowerInvariant())) yield return "the figure is square or pentagon for now (the circle and the triangle are set aside): " + Shape;
        if (Center == null) yield return "give the centre: " + Shape;
        else foreach (var p in Center.Problems()) yield return "centre: " + p;
        if (Radius.HasValue) yield return "a polygon is said by its side, \"side\": 2.0 — not by a radius";
        if (!Side.HasValue || !double.IsFinite(Side.Value) || Side.Value <= 0) yield return "the side must be a number of metres greater than zero";
        if (Policy is not ("rank" or "distance")) yield return "the policy is \"rank\" or \"distance\"";
        if (Fleet != null && Fleet.Any(n => string.IsNullOrWhiteSpace(n))) yield return "every name in the fleet must be a name";
        if (!string.IsNullOrWhiteSpace(Effect) || For.HasValue) yield return "the timed turn is gone (ajuste 77): take the square, then POST /rotate " + RotateRequest.Shape + ", one step a time";
    }
}

/// <summary>A FORMATION TOLD (propuesta 99) — <c>{"name": "square-1", "figure": "square", "center": {"x": 5.5, "y": 5.5}, "side": 2.0,
/// "angle": 0}</c>: the golem keeps it by its name, made again when the name is known; <c>"places": 6</c> lays it out for that many golems
/// (ajuste 101: the corners first, the rest on the sides).</summary>
public sealed record FormRequest(string Name, string Figure, PointRequest Center, double? Side, double? Angle = null, int? Places = null, double? Radius = null)
{
    public const string Shape = "{\"name\": \"square-1\", \"figure\": \"square\", \"center\": {\"x\": 5.5, \"y\": 5.5}, \"side\": 2.0, \"angle\": 0} — a circle: \"radius\": 1.5, \"places\": 4";
    public static readonly string[] Figures = { "square", "pentagon", "triangle", "circle" };   // a circle with how many it is for (ajuste 102)

    private bool IsCircle => Figure?.Trim().ToLowerInvariant() == "circle";

    /// <summary>What says its size: the side of a polygon, the radius of a circle.</summary>
    public double? Measure => IsCircle ? Radius : Side;

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Name) || !System.Text.RegularExpressions.Regex.IsMatch(Name.Trim(), @"^[A-Za-z_][A-Za-z0-9_-]*$")) yield return "the formation needs a name like square-1: " + Shape;
        if (string.IsNullOrWhiteSpace(Figure) || !Figures.Contains(Figure.Trim().ToLowerInvariant())) yield return "the figure is square, pentagon, triangle or circle: " + Shape;
        if (Center == null) yield return "give the centre: " + Shape;
        else foreach (var p in Center.Problems()) yield return "centre: " + p;
        if (IsCircle)
        {
            if (!Radius.HasValue || !double.IsFinite(Radius.Value) || Radius.Value <= 0) yield return "a circle's radius must be a number of metres greater than zero";
            if (!Places.HasValue) yield return "a circle's places hang on how many golems ride it: give \"places\"";
            if (Side.HasValue) yield return "a circle is said by its radius, not a side";
        }
        else
        {
            if (!Side.HasValue || !double.IsFinite(Side.Value) || Side.Value <= 0) yield return "the side must be a number of metres greater than zero";
            if (Radius.HasValue) yield return "a polygon is said by its side, not a radius";
        }
        if (Angle.HasValue && !double.IsFinite(Angle.Value)) yield return "the angle must be a number of degrees";
        if (Places.HasValue && (Places.Value < 1 || Places.Value > 100)) yield return "the places are how many golems it is laid out for, 1 to 100";
    }
}

/// <summary>A PLACE TAKEN (propuesta 99; ajuste 101) — <c>{"name": "square-1", "place": 2}</c>, or <c>"vertex": 2</c>, which says the same:
/// the golem resolves where that place of that formation stands and goes there.</summary>
public sealed record TakeRequest(string Name, int? Vertex, int? Place = null)
{
    public const string Shape = "{\"name\": \"square-1\", \"place\": 2}";

    /// <summary>The number of the place, whichever word said it.</summary>
    public int? Number => Place ?? Vertex;

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Name)) yield return "which formation? its name: " + Shape;
        if (Place.HasValue && Vertex.HasValue) yield return "say the place once — place or vertex, not both: " + Shape;
        else if (!Number.HasValue || Number.Value < 0) yield return "the place is a number from 0: " + Shape;
    }
}

/// <summary>One STEP of the formation in place (ajuste 77) — <c>{"sense": "clockwise"}</c> or <c>"counterclockwise"</c>: every body takes
/// the next corner in that sense once everybody stands on its own; steps queue.</summary>
public sealed record RotateRequest(string Sense)
{
    public const string Shape = "{\"sense\": \"clockwise\"}";
    public static readonly string[] Senses = { "clockwise", "counterclockwise" };

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Sense) || !Senses.Contains(Sense.Trim().ToLowerInvariant())) yield return "the sense is \"clockwise\" or \"counterclockwise\": " + Shape;
    }
}

/// <summary>A line of the command language (propuesta 58) — <c>{"line": "visit (2, 9.5) (9, 8)"}</c>: read by CommandLine, acted by the Commander.</summary>
public sealed record CommandRequest(string Line)
{
    public const string Shape = "{\"line\": \"visit (2, 9.5) (9, 8)\"}";

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Line)) yield return "give a line: " + Shape;
        else if (Line.Length > 4000) yield return "a line is at most 4000 characters";
    }
}

/// <summary>An ad-hoc read in the golem's language — <c>{"script": "g.PendingRoutes().Count"}</c>.</summary>
public sealed record QueryRequest(string Script)
{
    public const int LongestScript = 4000;
    public const string Shape = "{\"script\": \"g.PendingRoutes().Count\"}";

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Script)) yield return "give a script: " + Shape;
        else if (Script.Length > LongestScript) yield return $"a script is at most {LongestScript} characters";
    }
}

/// <summary>The hard reset's reach — <c>{"cascade": true}</c> resets the peers too; a peer asked by another golem gets false.</summary>
public sealed record ResetRequest(bool? Cascade)
{
    public const string Shape = "{\"cascade\": true}";

    public IEnumerable<string> Problems()
    {
        if (!Cascade.HasValue) yield return "say whether the peers reset too: " + Shape;
    }
}
