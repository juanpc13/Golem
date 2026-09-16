using System.Globalization;

namespace GolemAPI.Controllers;

// What the operator sends the golem, as JSON bodies — typed, and VALIDATED before anything reaches the journal (Juan,
// 16-sep-2026: "la data debería llegar por JSON… y hay que validar que venga correcta"). Each request says what is
// wrong with it in plain words; the endpoint answers 400 with that and performs nothing.

/// <summary>One stop of an errand: a place by name — <c>{"area": "kitchen"}</c> — or a point — <c>{"x": 9.0, "y": 8.0}</c>. Never both, never neither.</summary>
public sealed record StopRequest(string Area, double? X, double? Y)
{
    public bool IsPoint => X.HasValue || Y.HasValue;

    public IEnumerable<string> Problems(int position)
    {
        bool named = !string.IsNullOrWhiteSpace(Area);
        if (named && IsPoint) yield return $"stop {position}: give an area or a point, not both";
        else if (!named && !IsPoint) yield return $"stop {position}: give an area ({{\"area\": \"kitchen\"}}) or a point ({{\"x\": 9.0, \"y\": 8.0}})";
        else if (IsPoint)
        {
            if (!X.HasValue || !Y.HasValue) yield return $"stop {position}: a point needs both x and y";
            else if (!double.IsFinite(X.Value) || !double.IsFinite(Y.Value)) yield return $"stop {position}: x and y must be finite numbers";
        }
        else if (Area.Trim().Length > 64) yield return $"stop {position}: an area's name is at most 64 characters";
    }

    public string Describe() => IsPoint
        ? $"({(X ?? 0).ToString("0.##", CultureInfo.InvariantCulture)}, {(Y ?? 0).ToString("0.##", CultureInfo.InvariantCulture)})"
        : $"'{Area?.Trim()}'";
}

/// <summary>An errand: the stops, in order — <c>{"stops": [{"area": "kitchen"}, {"x": 9.0, "y": 8.0}]}</c>.</summary>
public sealed record ErrandRequest(List<StopRequest> Stops)
{
    public const int MostStops = 12;
    public const string Shape = "{\"stops\": [{\"area\": \"kitchen\"}, {\"x\": 9.0, \"y\": 8.0}]}";

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

// ---- what the ROBOT reports (sim/bridge/body.py) — its whole vocabulary: it ARRIVED (a turn made, a point reached),
//      it BUMPED into something, or it is STUCK (Juan, 16-sep-2026: "la interfaz del robot es extremadamente sencilla:
//      mover / girar / choque / llegué") ----

/// <summary>The body did the one thing it was told — turned, or reached the point — <c>{"route": 3}</c> (route 0: a courtesy step).</summary>
public sealed record ArrivedReport(int? Route)
{
    public IEnumerable<string> Problems()
    {
        if (!Route.HasValue || Route.Value < 0) yield return "give the route the order belonged to (0 for a courtesy step)";
    }
}

/// <summary>The body bumped into something: its motors stopped at once. What the world calls it, where the touch landed on
/// the plane and heading into it, and where the body stood facing which way —
/// <c>{"route": 3, "with": "crate_center", "x": 5.2, "y": 5.8, "heading": -1.57, "px": 5.2, "py": 6.05, "ptheta": -1.57}</c>; route 0 while standing.</summary>
public sealed record BumpReport(int? Route, string With, double? X, double? Y, double? Heading, double? Px, double? Py, double? Ptheta)
{
    public IEnumerable<string> Problems()
    {
        if (!Route.HasValue || Route.Value < 0) yield return "give the route (0 when standing)";
        if (string.IsNullOrWhiteSpace(With)) yield return "give what was touched, as the world names it";
        if (!X.HasValue || !Y.HasValue || !Heading.HasValue) yield return "give the touch: x, y and heading";
        else if (!double.IsFinite(X.Value) || !double.IsFinite(Y.Value) || !double.IsFinite(Heading.Value)) yield return "x, y and heading must be finite numbers";
        if (!Px.HasValue || !Py.HasValue || !Ptheta.HasValue) yield return "give where the body stood: px, py and ptheta";
        else if (!double.IsFinite(Px.Value) || !double.IsFinite(Py.Value) || !double.IsFinite(Ptheta.Value)) yield return "px, py and ptheta must be finite numbers";
    }
}

/// <summary>The body could not do what it was told — <c>{"route": 3, "reason": "stalled: no progress for 3 s"}</c>.</summary>
public sealed record StuckReport(int? Route, string Reason)
{
    public IEnumerable<string> Problems()
    {
        if (!Route.HasValue || Route.Value <= 0) yield return "give the route that got stuck";
        if (string.IsNullOrWhiteSpace(Reason)) yield return "give the reason, in the body's words";
    }
}
