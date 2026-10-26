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
/// <summary>A play staged (propuesta 108): its name, its kind, pacman and the ghosts, and the part this golem plays —
/// <c>{"name": "game-1", "kind": "pacman", "pacman": "blue", "ghosts": ["red", "green"], "as": "ghost"}</c>. Pacman is autonomous (21-oct-2026):
/// nothing says how it is driven.</summary>
public sealed record PlayRequest(string Name, string Pacman, List<string> Ghosts, string Kind = null, string As = null, string Light = null, string Cars = null)
{
    public const string Shape = "{\"name\": \"game-1\", \"kind\": \"pacman\", \"pacman\": \"blue\", \"ghosts\": [\"red\", \"green\"], \"as\": \"ghost\"}";
    public const string TrafficShape = "{\"name\": \"road-1\", \"kind\": \"traffic\", \"light\": \"orange\", \"cars\": \"red:straight,green:left,yellow:right\", \"as\": \"car\"}";

    public bool IsTraffic => (Kind ?? "").Trim().ToLowerInvariant() == "traffic";

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Name)) yield return "the play needs a name, its identity across the fleet: " + Shape;
        if (Kind != null && Kind.Trim().ToLowerInvariant() is not ("pacman" or "traffic")) yield return "the plays known are pacman and traffic: " + Shape + " or " + TrafficShape;
        if (IsTraffic)
        {
            if (As != null && As.Trim().ToLowerInvariant() is not ("light" or "car")) yield return "as is light or car: " + TrafficShape;
            if (string.IsNullOrWhiteSpace(Light)) yield return "a traffic play needs its light: " + TrafficShape;
            if (string.IsNullOrWhiteSpace(Cars) || !System.Text.RegularExpressions.Regex.IsMatch(Cars.Trim(), "^[a-zA-Z][a-zA-Z0-9-]*(:(north|south|east|west))?:(straight|left|right)(,[a-zA-Z][a-zA-Z0-9-]*(:(north|south|east|west))?:(straight|left|right))*$", System.Text.RegularExpressions.RegexOptions.IgnoreCase))
                yield return "a traffic play needs its cars and their turns, like red:straight,green:left: " + TrafficShape;
        }
        else
        {
            if (As != null && As.Trim().ToLowerInvariant() is not ("pacman" or "ghost")) yield return "as is pacman or ghost: " + Shape;
            if (string.IsNullOrWhiteSpace(Pacman)) yield return "the play needs a pacman: " + Shape;
        }
    }
}

/// <summary>A play by its name (16-oct-2026) — forgotten, or started: <c>{"name": "game-1"}</c>.</summary>
public sealed record ForgetPlayRequest(string Name)
{
    public const string Shape = "{\"name\": \"game-1\"}";

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Name)) yield return "the play's name is needed: " + Shape;
    }
}


/// <summary>The body's pace (propuesta 111): the cruise in m/s and, optionally, the turn rate in rad/s — <c>{"speed": 3.0, "turn": 2.5}</c>.</summary>
public sealed record PaceRequest(double Speed, double? Turn)
{
    public const string Shape = "{\"speed\": 3.0, \"turn\": 2.5}";

    public IEnumerable<string> Problems()
    {
        if (double.IsNaN(Speed) || double.IsInfinity(Speed) || Speed <= 0) yield return "the speed is metres per second above zero: " + Shape;
        if (Turn is double t && (double.IsNaN(t) || double.IsInfinity(t) || t <= 0)) yield return "the turn is radians per second above zero, or left out: " + Shape;
    }
}

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
/// of the fleet is told; made again when the name is known. A circle: <c>"radius"</c>. A propeller: <c>"figure": "propeller", "radius"</c> (the tips), <c>"innerRadius"</c> (the hub),
/// <c>"blades"</c>. The fleet
/// may be left out (this golem and every peer it can reach); the policy too (rank).</summary>
public sealed record FormRequest(string Name, string Figure, PointRequest Center, double? Side = null, double? Radius = null, double? InnerRadius = null,
                                 double? Angle = null, List<string> Fleet = null, string By = null, int? Blades = null,
                                 string Cue = null, bool OnCue = false, string After = null, double? Every = null, bool Open = false, int? Places = null)
{
    /// <summary>THE PULL (propuesta 118): the formation this one starts after — the one its asker asks whether it finished (propuesta 123: its first
    /// golem by rank answers) — lower case; null: none; and how often it asks, in seconds (propuesta 121; 5 when left out).</summary>
    public string StartsAfter => string.IsNullOrWhiteSpace(After) ? null : After.Trim().ToLowerInvariant();
    /// <summary>The FORMATION the last of this fleet to finish cues (propuesta 117; by its name since propuesta 123), lower case; null when none.</summary>
    public string CuesFormation => string.IsNullOrWhiteSpace(Cue) ? null : Cue.Trim().ToLowerInvariant();

    public const string Shape = "{\"name\": \"square-2\", \"figure\": \"square\", \"center\": {\"x\": 5.5, \"y\": 5.5}, \"side\": 2.0, \"angle\": 0, \"fleet\": [\"blue\", \"red\"], \"by\": \"rank\"} — a circle: \"radius\"; a propeller: \"figure\": \"propeller\", \"radius\", \"innerRadius\" (the hub), \"blades\"; the sequence (propuesta 117, by formation names since propuesta 123): \"cue\": \"tri-right\", \"onCue\": true; the pull (propuestas 118, 122): \"after\": \"tri-left\", \"every\": 5";
    public static readonly string[] Figures = { "square", "pentagon", "triangle", "circle", "propeller" };

    public string Kind => (Figure ?? "").Trim().ToLowerInvariant();
    public bool IsCircle => Kind == "circle";
    public bool IsPropeller => Kind == "propeller";

    /// <summary>What says its size: the side of a polygon, the radius of a circle, a propeller's tips.</summary>
    public double? Measure => IsCircle || IsPropeller ? Radius : Side;

    /// <summary>A propeller's hub (propuesta 114): told, or a third of its radius.</summary>
    public double? Hub => IsPropeller ? InnerRadius ?? Radius / 3 : null;

    /// <summary>A propeller's blades: told, or three.</summary>
    public int BladeCount => Blades ?? 3;

    /// <summary>How the places are shared: by rank, the default, or by distance.</summary>
    public string Policy => string.IsNullOrWhiteSpace(By) ? "rank" : By.Trim().ToLowerInvariant();

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Name) || !System.Text.RegularExpressions.Regex.IsMatch(Name.Trim(), @"^[A-Za-z_][A-Za-z0-9_-]*$")) yield return "the formation needs a name like square-2: " + Shape;
        if (!Figures.Contains(Kind)) yield return "the figure is square, pentagon, triangle, circle or propeller: " + Shape;
        if (Center == null) yield return "give the centre: " + Shape;
        else foreach (var p in Center.Problems()) yield return "centre: " + p;
        if (IsCircle || IsPropeller)
        {
            if (!Radius.HasValue || !double.IsFinite(Radius.Value) || Radius.Value <= 0) yield return $"a {Kind}'s radius must be a number of metres greater than zero";
            if (Side.HasValue) yield return $"a {Kind} is said by its radius, not a side";
        }
        else
        {
            if (!Side.HasValue || !double.IsFinite(Side.Value) || Side.Value <= 0) yield return "the side must be a number of metres greater than zero";
            if (Radius.HasValue) yield return "a polygon is said by its side, not a radius";
        }
        if (IsPropeller)
        {
            if (InnerRadius.HasValue && (!double.IsFinite(InnerRadius.Value) || InnerRadius.Value <= 0 || (Radius.HasValue && InnerRadius.Value >= Radius.Value)))
                yield return "a propeller's hub (its inner radius) is metres greater than zero and smaller than its radius";
            if (Blades.HasValue && (Blades.Value < 2 || Blades.Value > 12)) yield return "a propeller has from 2 to 12 blades";
        }
        else if (InnerRadius.HasValue) yield return "only a propeller has an inner radius (its hub)";
        if (Blades.HasValue && !IsPropeller) yield return "only a propeller has blades";
        if (Angle.HasValue && !double.IsFinite(Angle.Value)) yield return "the angle must be a number of degrees";
        if (Policy is not ("rank" or "distance" or "rps")) yield return "the policy is \"rank\", \"distance\" or \"rps\"";
        if (CuesFormation != null && !System.Text.RegularExpressions.Regex.IsMatch(CuesFormation, @"^[A-Za-z_][A-Za-z0-9_-]*$")) yield return "\"cue\" names one formation, like tri-right";
        if (CuesFormation != null && Name != null && CuesFormation == Name.Trim().ToLowerInvariant()) yield return "a formation cannot cue itself";
        if (StartsAfter != null && !System.Text.RegularExpressions.Regex.IsMatch(StartsAfter, @"^[A-Za-z_][A-Za-z0-9_-]*$")) yield return "\"after\" names one formation, like tri-left";
        if (StartsAfter != null && Name != null && StartsAfter == Name.Trim().ToLowerInvariant()) yield return "a formation cannot start after itself";
        if (Every.HasValue && (!double.IsFinite(Every.Value) || Every.Value <= 0)) yield return "\"every\" is seconds above zero";
        if (Every.HasValue && StartsAfter == null) yield return "\"every\" goes with \"after\": how often the golem asks";
        if (Fleet != null && Fleet.Any(n => string.IsNullOrWhiteSpace(n))) yield return "every name in the fleet must be a name";
        // the open formation (propuesta 123): of a union — no fleet of its own, shared by distance or rps, a polygon
        if (Open && Fleet is { Count: > 0 }) yield return "an open formation takes no fleet: the union's candidates are its fleet";
        if (Open && Policy == "rank") yield return "an open formation shares its places by distance or rps: by rank nobody chooses";
        if (Open && (IsCircle || IsPropeller) && !Places.HasValue) yield return $"a {Kind} opens for a number of golems (\"places\"); a polygon alone opens for its vertices";
        if (Places.HasValue && !Open) yield return "\"places\" goes with \"open\": how many of the union's candidates the figure takes";
        if (Open && (CuesFormation != null || OnCue || StartsAfter != null)) yield return "an open formation follows the beats of its union (propuesta 124): no \"cue\", \"onCue\" or \"after\" of its own — the link is the union's";
        if (Places.HasValue && Places.Value < 1) yield return "\"places\" is a whole number above zero";
    }
}

/// <summary>THE PROGRAMME (propuesta 123, 21-oct-2026) — <c>{"name": "two-triangles", "formations": ["tri-left", "tri-right"], "fleet": ["blue", …], "by": "distance"}</c>:
/// several open formations in order and the golems shared among them by the law; the candidates left out are this golem and every peer.</summary>
public sealed record UnionRequest(string Name, List<string> Formations, List<string> Fleet = null, string By = null, string Link = null, double? Every = null)
{
    public const string Shape = "{\"name\": \"two-triangles\", \"formations\": [\"tri-left\", \"tri-right\"], \"fleet\": [\"blue\", \"red\"], \"by\": \"distance\", \"link\": \"cue\"}";

    public string Policy => string.IsNullOrWhiteSpace(By) ? "distance" : By.Trim().ToLowerInvariant();
    /// <summary>How the beats are agreed (propuesta 124): "cue" (the default) or "ask".</summary>
    public string LinkWord => string.IsNullOrWhiteSpace(Link) ? "cue" : Link.Trim().ToLowerInvariant();
    public IReadOnlyList<string> Bill => (Formations ?? new List<string>()).Where(n => !string.IsNullOrWhiteSpace(n)).Select(n => n.Trim().ToLowerInvariant()).ToList();

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Name) || !System.Text.RegularExpressions.Regex.IsMatch(Name.Trim(), @"^[A-Za-z_][A-Za-z0-9_-]*$")) yield return "the union needs a name like two-triangles: " + Shape;
        if (Bill.Count == 0) yield return "a union names at least one formation: " + Shape;
        if (Bill.Any(n => !System.Text.RegularExpressions.Regex.IsMatch(n, @"^[A-Za-z_][A-Za-z0-9_-]*$"))) yield return "every formation of the union is named like tri-left";
        if (Bill.Distinct().Count() != Bill.Count) yield return "a union names each formation once";
        if (Name != null && Bill.Contains(Name.Trim().ToLowerInvariant())) yield return "a union cannot name itself as a formation";
        if (Policy is not ("distance" or "rps")) yield return "the policy is \"distance\" or \"rps\": by rank nobody chooses";
        if (Fleet != null && Fleet.Any(n => string.IsNullOrWhiteSpace(n))) yield return "every name in the fleet must be a name";
        if (LinkWord is not ("cue" or "ask")) yield return "the link is \"cue\" or \"ask\": how the beats are agreed among the golems";
        if (Every.HasValue && (!double.IsFinite(Every.Value) || Every.Value <= 0)) yield return "\"every\" is seconds above zero";
        if (Every.HasValue && LinkWord != "ask") yield return "\"every\" goes with the link \"ask\": how often the askers ask";
    }
}

/// <summary>THE PROGRAM (propuesta 124) — <c>{"name": "two-triangles", "definition": "tri-left: triangle 3,5.5 side 3 places 3 = cw,cw,cw; tri-right: …",
/// "fleet": […], "by": "distance", "link": "cue"}</c>: the whole union in one definition the domain reads — its formations in order, each with its figure
/// and its part of the score (cw, ccw, - a rest, one per beat); the line is this golem's word too.</summary>
public sealed record ProgramRequest(string Name, string Definition, List<string> Fleet = null, string By = null, string Link = null, double? Every = null)
{
    public const string Shape = "{\"name\": \"two-triangles\", \"definition\": \"tri-left: triangle 3,5.5 side 3 places 3 = cw,cw,cw; tri-right: triangle 8,5.5 side 3 places 3 = cw,cw,-\", \"by\": \"distance\", \"link\": \"cue\"}";

    public string Policy => string.IsNullOrWhiteSpace(By) ? "distance" : By.Trim().ToLowerInvariant();
    public string LinkWord => string.IsNullOrWhiteSpace(Link) ? "cue" : Link.Trim().ToLowerInvariant();

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Name) || !System.Text.RegularExpressions.Regex.IsMatch(Name.Trim(), @"^[A-Za-z_][A-Za-z0-9_-]*$")) yield return "the program needs a name like two-triangles: " + Shape;
        if (string.IsNullOrWhiteSpace(Definition)) yield return "a program needs its definition — the formations and their score: " + Shape;
        if (Policy is not ("distance" or "rps")) yield return "the policy is \"distance\" or \"rps\": by rank nobody chooses";
        if (Fleet != null && Fleet.Any(n => string.IsNullOrWhiteSpace(n))) yield return "every name in the fleet must be a name";
        if (LinkWord is not ("cue" or "ask")) yield return "the link is \"cue\" or \"ask\": how the beats are agreed among the golems";
        if (Every.HasValue && (!double.IsFinite(Every.Value) || Every.Value <= 0)) yield return "\"every\" is seconds above zero";
        if (Every.HasValue && LinkWord != "ask") yield return "\"every\" goes with the link \"ask\": how often the askers ask";
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

/// <summary>One STEP of a formation (ajuste 77; propuesta 104) — <c>{"name": "square-2", "sense": "clockwise"}</c>: every body of it takes the next place in that sense once everybody
/// stands on its own; steps queue.</summary>
public sealed record RotateRequest(string Name, string Sense)
{
    public const string Shape = "{\"name\": \"square-2\", \"sense\": \"clockwise\"}";
    public static readonly string[] Senses = { "clockwise", "counterclockwise" };

    public IEnumerable<string> Problems()
    {
        if (string.IsNullOrWhiteSpace(Name)) yield return "which formation? its name: " + Shape;
        if (string.IsNullOrWhiteSpace(Sense) || !Senses.Contains(Sense.Trim().ToLowerInvariant())) yield return "the sense is \"clockwise\" or \"counterclockwise\": " + Shape;
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

/// <summary>THIS GOLEM FINISHES ITS PART of a formation (propuesta 117) — <c>{"name": "tri-left"}</c>: queued like the steps, counted when the last
/// step is walked; the fleet is told, and when every member finished the last of the fleet cues the golem the formation names.</summary>
public sealed record FinishRequest(string Name)
{
    public const string Shape = "{\"name\": \"tri-left\"}";

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
