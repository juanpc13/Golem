using System.Text.Json;
using Choreography.Theater;
using GolemAPI.Choreography;
using GolemAPI.Membrane;
using Microsoft.AspNetCore.Mvc;
using Puppeteer;

namespace GolemAPI.Controllers;

// The golem's own verbs, as endpoints (output-and-controller guide): C# validates
// the inputs, the actor is reused, the request values become @params, and the
// perform's print output IS the response — no DTO, no re-serialization.
public class GolemController : Controller
{
    private readonly PerformanceV2 perf;
    private readonly Rosbridge ros;

    public GolemController(PerformanceV2 perf, Rosbridge ros)
    {
        this.perf = perf;
        this.ros = ros;
    }

    // Send the golem somewhere: one place (?place=), one point (?x=&y=), or several stops in THIS
    // order (a JSON body {"stops": ["kitchen", "9,8", "garage"]}). The handle is minted at the
    // actor (Eval) and frozen into the journaled arguments. A stop off the map is refused before
    // anything is journaled.
    [HttpPost("move")]
    public async Task<IActionResult> MoveTo([FromQuery] string place, [FromQuery] double? x, [FromQuery] double? y)
    {
        if (!string.IsNullOrWhiteSpace(place)) return Errand("Visit", new[] { place.Trim() });
        if (x.HasValue || y.HasValue)
        {
            if (!(x.HasValue && y.HasValue) || !double.IsFinite(x.Value) || !double.IsFinite(y.Value))
                return BadRequest("x and y must both be finite numbers");
            return Errand("Visit", new[] { $"{x.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)},{y.Value.ToString("R", System.Globalization.CultureInfo.InvariantCulture)}" });
        }
        var stops = await ReadStopsAsync();
        if (stops == null) return BadRequest("give ?place=, ?x=&y=, or a JSON body {\"stops\": [\"kitchen\", \"9,8\"]}");
        return Errand("Visit", stops);
    }

    // Send the golem through several stops and let it choose the order that makes the road shortest.
    [HttpPost("cover")]
    public async Task<IActionResult> Cover()
    {
        var stops = await ReadStopsAsync();
        if (stops == null) return BadRequest("give a JSON body {\"stops\": [\"garage\", \"kitchen\", \"storage\"]}");
        return Errand("Cover", stops);
    }

    // The errand as the journal writes it — the stops AND the whole plan, in ONE entry (Juan, 10-sep: "con un solo
    // [comando] podemos tener toda la ruta y seguir ese plan de punto"): each stop found or built from its @params
    // (one is `point`, several are `point1`, `point2`…), then Route and one act per leg. The plan is asked of the
    // golem beforehand (g.Preview: a read, in this same request), from where the body stands — or, when the golem is
    // busy, from where its last pending mission ends, since that is where it will stand when this one comes up.
    private IActionResult Errand(string verb, string[] stops)
    {
        // where each stop is: an area's centre, or the point itself
        var xs = new double[stops.Length];
        var ys = new double[stops.Length];
        for (int i = 0; i < stops.Length; i++)
        {
            if (IsPoint(stops[i]))
            {
                var xy = stops[i].Split(',');
                xs[i] = double.Parse(xy[0], System.Globalization.CultureInfo.InvariantCulture);
                ys[i] = double.Parse(xy[1], System.Globalization.CultureInfo.InvariantCulture);
            }
            else
            {
                try
                {
                    using var centre = System.Text.Json.JsonDocument.Parse(perf.Actor.Using("print map.Find(@area).Center.X 'x', map.Find(@area).Center.Y 'y';")
                        .WithParameters(p => { p["area", typeof(string)] = stops[i]; }).PerformQuery());
                    xs[i] = centre.RootElement.GetProperty("x").GetDouble();
                    ys[i] = centre.RootElement.GetProperty("y").GetDouble();
                }
                catch (Exception) { return Conflict($"'{stops[i]}' is neither an area nor a point on the map"); }
            }
        }

        // where the plan starts: the body, or the end of the plan the golem is already on
        var pose = ros.LatestPose;
        double fromX, fromY;
        using (var busy = System.Text.Json.JsonDocument.Parse(perf.Actor.Using(@"
            print g.HasPendingMission() 'busy';
            if (g.HasPendingMission()) { print g.PlannedEndX() 'x', g.PlannedEndY() 'y'; }
        ").PerformQuery()))
        {
            if (busy.RootElement.GetProperty("busy").GetBoolean())
            {
                fromX = busy.RootElement.GetProperty("x").GetDouble();
                fromY = busy.RootElement.GetProperty("y").GetDouble();
            }
            else
            {
                if (pose == null) return StatusCode(503, "no telemetry from the body yet: the plan needs a starting point");
                fromX = pose.X;
                fromY = pose.Y;
            }
        }

        // the plan, asked of the golem: the whole road through the stops, in the order given or the one it chooses
        List<RoadLeg> legs;
        try
        {
            legs = RoadLeg.FromQuery(perf.Actor.Using("foreach (legs in g.Preview(@fx, @fy, @xs, @ys, @cover).Legs()) { " + RoadLeg.PrintLegs + " }")
                .WithParameters(p => {
                    p["fx",    typeof(double)]   = fromX;
                    p["fy",    typeof(double)]   = fromY;
                    p["xs",    typeof(double[])] = xs;
                    p["ys",    typeof(double[])] = ys;
                    p["cover", typeof(bool)]     = verb == "Cover";
                })
                .PerformQuery());
        }
        catch (Exception ex) { return Conflict("no road: " + Innermost(ex)); }

        // one entry: the stops, then the plan
        var check = new System.Text.StringBuilder("Check(");
        var acts = new System.Text.StringBuilder("{\n");
        for (int i = 0; i < stops.Length; i++)
        {
            string n = stops.Length == 1 ? "" : (i + 1).ToString();
            if (i > 0) check.Append(" && ");
            if (IsPoint(stops[i]))
            {
                check.Append($"g.IsOnMap(@x{n}, @y{n})");
                acts.Append($"    point{n} = Position(@x{n}, @y{n});\n    g.{verb}(@id, point{n});\n");
            }
            else
            {
                check.Append($"map.Knows(@area{n})");
                acts.Append($"    point{n} = map.Find(@area{n});\n    g.{verb}(@id, point{n});\n");
            }
        }
        acts.Append(RoadLeg.Acts(legs));
        acts.Append("}\n");
        check.Append(") Error 'a stop is neither an area nor a point on the map';");
        return Refusable(perf.Actor.Using(check.ToString(), acts.ToString())
        .WithParameters(p => {
            p[Parameter.Eval, "id", typeof(int)] = "g.NextHandle()";
            for (int i = 0; i < stops.Length; i++)
            {
                string n = stops.Length == 1 ? "" : (i + 1).ToString();
                if (IsPoint(stops[i]))
                {
                    p[$"x{n}", typeof(double)] = xs[i];
                    p[$"y{n}", typeof(double)] = ys[i];
                }
                else p[$"area{n}", typeof(string)] = stops[i];
            }
            RoadLeg.Bind(p, legs);
        })
        .PerformCheckThenCommand());
    }

    private static string Innermost(Exception ex)
    {
        while (ex.InnerException != null) ex = ex.InnerException;
        return ex.Message;
    }

    private static bool IsPoint(string token)
    {
        var xy = token.Split(',');
        return xy.Length == 2
            && double.TryParse(xy[0].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)
            && double.TryParse(xy[1].Trim(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _);
    }

    // The map as the golem knows it, read as objects: one query walks the places and prints their
    // properties, and inside each place walks its doors, its open boundaries and the marks standing in
    // it. The engine renders each foreach as an array named after the loop variable, nested where the
    // loop is nested — places, and within a place: doors, opens, marks; a place with none of something
    // simply lacks that key. The golem hands out its objects and never renders a document itself.
    // The map as it is laid out: zones, doors and open sides — read from the map module itself, not through the
    // golem (the modules are globals of the actor). What the bodies TOUCHED is the collisions module
    // and has its own endpoint (/obstacles), which also names the zone of each figure.
    [HttpGet("map")]
    public IActionResult Map() =>
        Content(perf.Actor.Using(@"
            print map.Name 'map';
            foreach (places in map.Zones) {
                print places.Name 'name', places.X 'x', places.Y 'y', places.Width 'w', places.Height 'h', places.Center.X 'cx', places.Center.Y 'cy';
                foreach (doors in places.Doorways()) {
                    print doors.To 'to', doors.At.X 'x', doors.At.Y 'y';
                }
                foreach (opens in places.OpenSides()) {
                    print opens.To 'to';
                }
            }
        ")
        .PerformQuery(), "application/json");

    // The obstacles the golem hypothesizes, as a flat list for a table: one row per obstacle — its kind, the
    // zone it stands in, the figure its vertices draw, its centre — and under it one row per vertex, the touch
    // that outlined it with its normal. A thing is geometry the roads avoid; a peer is a body it met, history.
    // One query walking the golem's own objects: the table is drawn from them, never from a document it rendered.
    [HttpGet("obstacles")]
    public IActionResult Obstacles() =>
        Content(perf.Actor.Using(@"
            print g.ObstacleCount() 'total', g.ThingCount() 'things', collisions.EncounterCount 'met', collisions.MarkCount 'marks';
            foreach (obstacles in collisions.All()) {
                print obstacles.Kind 'kind', obstacles.Where 'zone', obstacles.Shape 'shape', obstacles.Size 'size',
                      obstacles.Who 'who', obstacles.Center.X 'cx', obstacles.Center.Y 'cy';
                foreach (vertices in obstacles.Vertices()) {
                    print vertices.At.X 'x', vertices.At.Y 'y', vertices.Heading 'normal', vertices.Reach 'reach';
                }
            }
        ")
        .PerformQuery(), "application/json");

    // Somebody took it away: the golem forgets the obstacle standing there, with every mark that outlined it.
    // A reaction tells the peers, who forget it too. From here on a body may pass, and if it touches something
    // that is a NEW obstacle with new marks — nothing of the old one survives in the map (the journal keeps
    // both acts: what was believed, and when it stopped being believed).
    [HttpPost("forget")]
    public IActionResult Forget([FromQuery] double? x, [FromQuery] double? y)
    {
        if (!(x.HasValue && y.HasValue) || !double.IsFinite(x.Value) || !double.IsFinite(y.Value))
            return BadRequest("x and y must both be finite numbers");
        return Refusable(perf.Actor.Using(
            @"
                Check(g.KnowsObstacleAt(@x, @y)) Error 'the golem holds no obstacle there';
            ",
            @"
                { at = Position(@x, @y); g.Forget(at); }
                expose @x gx, @y gy;
            ")
        .WithParameters(p => {
            p["x", typeof(double)] = x.Value;
            p["y", typeof(double)] = y.Value;
        })
        .PerformCheckThenCommand());
    }

    [HttpGet("state")]
    public IActionResult MissionBoard() => Content(Board(), "application/json");

    // Ask the golem how much road and how much time it still has ahead — through EVERY
    // pending mission, in the order it will run them, along the map's passages, at ITS
    // OWN speed and with ITS OWN lingers. The only telemetry is where the body believes
    // it stands right now: it enters the query as @params (queries never journal).
    [HttpGet("progress")]
    public IActionResult Progress()
    {
        var pose = ros.LatestPose;
        if (pose == null) return StatusCode(503, "no telemetry from the body yet");

        string answer = perf.Actor.Using(@"
            print g.HasPendingMission() 'hasNext', g.Pending() 'pendingMissions', g.Speed() 'speed', g.LingerAfterTold() 'lingerAfterTold';
            if (g.HasPendingMission()) {
                print g.NextId() 'mission', g.StopsLeft(g.NextId()) 'stopsLeft', g.RouteLength() 'routeLength', g.RouteSeconds() 'routeSeconds';
                if (g.IsOnMap(@x, @y)) {
                    print g.DistanceLeft(@x, @y) 'distanceLeft', g.SecondsLeft(@x, @y) 'secondsLeft', g.PlaceAt(@x, @y).Name 'here';
                }
            }
        ")
        .WithParameters(p => {
            p["x", typeof(double)] = pose.X;
            p["y", typeof(double)] = pose.Y;
        })
        .PerformQuery();

        return Content(answer, "application/json");
    }

    // One query, one document: the board the panel paints from.
    private string Board() =>
        perf.Actor.Using(@"
            print g.Pending() 'pending', g.Total() 'total', g.HasPendingMission() 'hasNext';
            if (g.HasPendingMission()) {
                print g.NextId() 'nextId', g.HeadingX() 'nextX', g.HeadingY() 'nextY', g.StopsLeft(g.NextId()) 'stopsLeft';
            }
        ")
        .PerformQuery();

    private IActionResult Refusable(string refused) =>
        refused != "" ? Conflict(refused) : Content(Board(), "application/json");

    // The stops of a JSON body {"stops": ["kitchen", "9,8", ...]}; null when there is no such body.
    private async Task<string[]> ReadStopsAsync()
    {
        if (Request.ContentLength is null or 0) return null;
        try
        {
            using var reader = new StreamReader(Request.Body);
            using var doc = JsonDocument.Parse(await reader.ReadToEndAsync());
            if (!doc.RootElement.TryGetProperty("stops", out var array) || array.ValueKind != JsonValueKind.Array) return null;
            var stops = array.EnumerateArray().Select(e => e.GetString()?.Trim()).Where(s => !string.IsNullOrEmpty(s)).ToArray();
            return stops.Length == 0 ? null : stops;
        }
        catch (JsonException)
        {
            return null;
        }
    }
}
