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

/// <summary>The body bumped — <c>{"bodyX": 5.5, "bodyY": 6.1, "bodyHeading": -1.57, "bearing": 0.0, "route": 3, "with": "crate_center"}</c>:
/// where the body stood, facing which way, and where on its shell it was pressed (radians from the direction it faces; 0 the
/// nose, +π/2 the left flank). That is all a bumper knows; where the touch landed on the plane is the domain's to reckon from
/// the body it declared (Juan, 18-sep-2026). `route` and `with` may come but are not read: the golem finds its route
/// underway, and what was touched is the domain's to conclude (for now every touch is a bump).</summary>
public sealed record BumpReport(double? BodyX, double? BodyY, double? BodyHeading, double? Bearing, int? Route, string With)
{
    public const string Shape = "{\"bodyX\": 5.5, \"bodyY\": 6.1, \"bodyHeading\": -1.57, \"bearing\": 0.0}";

    public IEnumerable<string> Problems()
    {
        if (!BodyX.HasValue || !BodyY.HasValue || !BodyHeading.HasValue) yield return "give where the body stood: bodyX, bodyY and bodyHeading";
        else if (!double.IsFinite(BodyX.Value) || !double.IsFinite(BodyY.Value) || !double.IsFinite(BodyHeading.Value)) yield return "bodyX, bodyY and bodyHeading must be numbers";
        if (!Bearing.HasValue) yield return "give where on the shell it was pressed: bearing, radians from the direction it faces";
        else if (!double.IsFinite(Bearing.Value)) yield return "bearing must be a number";
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
