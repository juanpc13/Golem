using System.Text.Json;
using Choreography.Theater;
using GolemHost.Membrane;
using Microsoft.AspNetCore.Mvc;
using Puppeteer;

namespace GolemHost.Controllers;

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
        if (!string.IsNullOrWhiteSpace(place))
            return Refusable(perf.Actor.Using(
                @"
                    Check(g.KnowsPlace(@place)) Error 'no such place on the map';
                ",
                @"
                    g.Visit(@id, @place);
                ")
            .WithParameters(p => {
                p[Parameter.Eval, "id", typeof(int)] = "g.NextHandle()";
                p["place", typeof(string)]           = place.Trim();
            })
            .PerformCheckThenCommand());

        if (x.HasValue || y.HasValue)
        {
            if (!(x.HasValue && y.HasValue) || !double.IsFinite(x.Value) || !double.IsFinite(y.Value))
                return BadRequest("x and y must both be finite numbers");
            return Refusable(perf.Actor.Using(
                @"
                    Check(g.IsOnMap(@x, @y)) Error 'that point is nowhere on the map';
                ",
                @"
                    g.Visit(@id, @x, @y);
                ")
            .WithParameters(p => {
                p[Parameter.Eval, "id", typeof(int)] = "g.NextHandle()";
                p["x", typeof(double)]               = x.Value;
                p["y", typeof(double)]               = y.Value;
            })
            .PerformCheckThenCommand());
        }

        var stops = await ReadStopsAsync();
        if (stops == null) return BadRequest("give ?place=, ?x=&y=, or a JSON body {\"stops\": [\"kitchen\", \"9,8\"]}");
        return Refusable(perf.Actor.Using(
            @"
                Check(g.AreStops(@stops)) Error 'a stop is neither a place nor a point on the map';
            ",
            @"
                g.Visit(@id, @stops);
            ")
        .WithParameters(p => {
            p[Parameter.Eval, "id", typeof(int)] = "g.NextHandle()";
            p["stops", typeof(string[])]         = stops;
        })
        .PerformCheckThenCommand());
    }

    // Send the golem through several stops and let it choose the order that makes the road shortest.
    [HttpPost("cover")]
    public async Task<IActionResult> Cover()
    {
        var stops = await ReadStopsAsync();
        if (stops == null) return BadRequest("give a JSON body {\"stops\": [\"garage\", \"kitchen\", \"storage\"]}");
        return Refusable(perf.Actor.Using(
            @"
                Check(g.AreStops(@stops)) Error 'a stop is neither a place nor a point on the map';
            ",
            @"
                g.Cover(@id, @stops);
            ")
        .WithParameters(p => {
            p[Parameter.Eval, "id", typeof(int)] = "g.NextHandle()";
            p["stops", typeof(string[])]         = stops;
        })
        .PerformCheckThenCommand());
    }

    // The map as the golem knows it, read as objects: one query walks the places and prints their
    // properties, and inside each place walks its doors, its open boundaries and the marks standing in
    // it. The engine renders each foreach as an array named after the loop variable, nested where the
    // loop is nested — places, and within a place: doors, opens, marks; a place with none of something
    // simply lacks that key. The golem hands out its objects and never renders a document itself.
    // The floor plan as the golem was told it: places, doors and open boundaries. What its bodies TOUCHED is
    // the other module and has its own endpoint (/obstacles), which also names the zone of each figure.
    [HttpGet("map")]
    public IActionResult Map() =>
        Content(perf.Actor.Using(@"
            foreach (places in g.Places()) {
                print places.Name 'name', places.X 'x', places.Y 'y', places.Width 'w', places.Height 'h', places.Center.X 'cx', places.Center.Y 'cy';
                foreach (doors in places.Doors()) {
                    print doors.To 'to', doors.At.X 'x', doors.At.Y 'y';
                }
                foreach (opens in places.Openings()) {
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
            print g.ObstacleCount() 'total', g.ThingCount() 'things', g.MetCount() 'met', g.MarkCount() 'marks';
            foreach (obstacles in g.Obstacles()) {
                print obstacles.Kind 'kind', obstacles.Where 'zone', obstacles.Shape 'shape', obstacles.Size 'size',
                      obstacles.Who 'who', obstacles.Center.X 'cx', obstacles.Center.Y 'cy';
                foreach (vertices in obstacles.Vertices()) {
                    print vertices.X 'x', vertices.Y 'y', vertices.Heading 'normal', vertices.Reach 'reach';
                }
            }
        ")
        .PerformQuery(), "application/json");

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
                    print g.DistanceLeft(@x, @y) 'distanceLeft', g.SecondsLeft(@x, @y) 'secondsLeft', g.PlaceAt(@x, @y) 'here';
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
                print g.NextId() 'nextId', g.OrderX() 'nextX', g.OrderY() 'nextY', g.StopsLeft(g.NextId()) 'stopsLeft';
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
