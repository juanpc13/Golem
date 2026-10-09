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

/// <summary>A FORMATION TOLD (propuesta 104) — <c>{"name": "square-2", "figure": "square", "center": {"x": 5.5, "y": 5.5}, "side": 2.0,
/// "angle": 0, "fleet": ["blue", "red"], "by": "rank"}</c>: the golem keeps it by its name, with its fleet and its policy, and every golem
/// of the fleet is told; made again when the name is known. A circle: <c>"radius"</c>. A double ring: <c>"figure": "double-ring",
/// "radius"</c> the outer ring's, <c>"innerRadius"</c>, <c>"fleet"</c> the outer ring's golems, <c>"innerFleet"</c> the inner ring's. The fleet
/// may be left out (this golem and every peer it can reach); the policy too (rank).</summary>
public sealed record FormRequest(string Name, string Figure, PointRequest Center, double? Side = null, double? Radius = null, double? InnerRadius = null,
                                 double? Angle = null, List<string> Fleet = null, List<string> InnerFleet = null, string By = null)
{
    public const string Shape = "{\"name\": \"square-2\", \"figure\": \"square\", \"center\": {\"x\": 5.5, \"y\": 5.5}, \"side\": 2.0, \"angle\": 0, \"fleet\": [\"blue\", \"red\"], \"by\": \"rank\"} — a circle: \"radius\"; a double ring: \"figure\": \"double-ring\", \"radius\", \"innerRadius\", \"fleet\" (outer), \"innerFleet\"";
    public static readonly string[] Figures = { "square", "pentagon", "triangle", "circle", "double-ring" };

    public string Kind => (Figure ?? "").Trim().ToLowerInvariant().Replace("double ring", "double-ring");
    public bool IsCircle => Kind == "circle";
    public bool IsRings => Kind == "double-ring";

    /// <summary>What says its size: the side of a polygon, the radius of a circle, the outer radius of a double ring.</summary>
    public double? Measure => IsCircle || IsRings ? Radius : Side;

    /// <summary>How the places are shared: by rank, the default, or by distance.</summary>
    public string Policy => string.IsNullOrWhiteSpace(By) ? "rank" : By.Trim().ToLowerInvariant();

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Name) || !System.Text.RegularExpressions.Regex.IsMatch(Name.Trim(), @"^[A-Za-z_][A-Za-z0-9_-]*$")) yield return "the formation needs a name like square-2: " + Shape;
        if (!Figures.Contains(Kind)) yield return "the figure is square, pentagon, triangle, circle or double-ring: " + Shape;
        if (Center == null) yield return "give the centre: " + Shape;
        else foreach (var p in Center.Problems()) yield return "centre: " + p;
        if (IsCircle || IsRings)
        {
            if (!Radius.HasValue || !double.IsFinite(Radius.Value) || Radius.Value <= 0) yield return $"a {Kind.Replace('-', ' ')}'s radius must be a number of metres greater than zero";
            if (Side.HasValue) yield return $"a {Kind.Replace('-', ' ')} is said by its radius, not a side";
        }
        else
        {
            if (!Side.HasValue || !double.IsFinite(Side.Value) || Side.Value <= 0) yield return "the side must be a number of metres greater than zero";
            if (Radius.HasValue) yield return "a polygon is said by its side, not a radius";
        }
        if (IsRings)
        {
            if (!InnerRadius.HasValue || !double.IsFinite(InnerRadius.Value) || InnerRadius.Value <= 0) yield return "a double ring needs its inner radius, metres greater than zero";
            else if (Radius.HasValue && InnerRadius.Value >= Radius.Value) yield return "the inner radius of a double ring is smaller than the outer one";
            if (InnerFleet == null || InnerFleet.Count == 0) yield return "a double ring needs the inner ring's golems: \"innerFleet\"";
            if (Fleet == null || Fleet.Count == 0) yield return "a double ring needs the outer ring's golems: \"fleet\"";
        }
        else
        {
            if (InnerRadius.HasValue) yield return "only a double ring has an inner radius";
            if (InnerFleet != null && InnerFleet.Count > 0) yield return "only a double ring has an inner fleet";
        }
        if (Angle.HasValue && !double.IsFinite(Angle.Value)) yield return "the angle must be a number of degrees";
        if (Policy is not ("rank" or "distance")) yield return "the policy is \"rank\" or \"distance\"";
        if (Fleet != null && Fleet.Any(n => string.IsNullOrWhiteSpace(n))) yield return "every name in the fleet must be a name";
        if (InnerFleet != null && InnerFleet.Any(n => string.IsNullOrWhiteSpace(n))) yield return "every name in the inner fleet must be a name";
    }
}

/// <summary>A FORMATION TAKEN (propuesta 104) — <c>{"name": "square-2"}</c>: this golem says where it stands and the fleet's round of words
/// opens the routes, each to its place by the formation's policy. No place on the request: the golem resolves it.</summary>
public sealed record TakeRequest(string Name)
{
    public const string Shape = "{\"name\": \"square-2\"}";

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Name)) yield return "which formation? its name: " + Shape;
    }
}

/// <summary>One STEP of a formation (ajuste 77; propuesta 104) — <c>{"name": "square-2", "sense": "clockwise"}</c>, and for a double ring
/// <c>"ring": "outer"</c> or <c>"inner"</c> (left out, the whole figure): every body of it takes the next place in that sense once everybody
/// stands on its own; steps queue.</summary>
public sealed record RotateRequest(string Name, string Sense, string Ring = null)
{
    public const string Shape = "{\"name\": \"square-2\", \"sense\": \"clockwise\", \"ring\": \"inner\"}";
    public static readonly string[] Senses = { "clockwise", "counterclockwise" };
    public static readonly string[] Rings = { "whole", "outer", "inner" };

    public string Orbit => string.IsNullOrWhiteSpace(Ring) ? "whole" : Ring.Trim().ToLowerInvariant();

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Name)) yield return "which formation? its name: " + Shape;
        if (string.IsNullOrWhiteSpace(Sense) || !Senses.Contains(Sense.Trim().ToLowerInvariant())) yield return "the sense is \"clockwise\" or \"counterclockwise\": " + Shape;
        if (!Rings.Contains(Orbit)) yield return "the ring is \"outer\", \"inner\" or left out: " + Shape;
    }
}

/// <summary>A FORMATION DISSOLVED (propuesta 104) — <c>{"name": "square-2"}</c>: it leaves every golem of the fleet.</summary>
public sealed record DissolveRequest(string Name)
{
    public const string Shape = "{\"name\": \"square-2\"}";

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Name)) yield return "which formation? its name: " + Shape;
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
