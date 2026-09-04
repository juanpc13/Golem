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

    // Entrust a mission to a point. The handle is minted at the actor (Eval) and frozen
    // into the journaled arguments, so the Reaction that echoes visited points can
    // correlate on it. A point off the map is refused before anything is journaled.
    [HttpPost("assign")]
    public IActionResult AssignMission([FromQuery] double x, [FromQuery] double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return BadRequest("x and y must be finite numbers");

        string refused = perf.Actor.Using(
            @"
                Check(g.IsOnMap(@x, @y)) Error 'that point is nowhere on the map';
            ",
            @"
                g.Assign(@id, @x, @y);
            ")
        .WithParameters(p => {
            p[Parameter.Eval, "id", typeof(int)] = "g.NextHandle()";
            p["x", typeof(double)]               = x;
            p["y", typeof(double)]               = y;
        })
        .PerformCheckThenCommand();
        if (refused != "") return Conflict(refused);

        return Content(Board(), "application/json");
    }

    // Entrust a mission to a place: the golem heads for its center, through the passages.
    [HttpPost("goto")]
    public IActionResult GoToPlace([FromQuery] string place)
    {
        if (string.IsNullOrWhiteSpace(place)) return BadRequest("a place name is required");

        string refused = perf.Actor.Using(
            @"
                Check(g.KnowsPlace(@place)) Error 'no such place on the map';
            ",
            @"
                g.AssignPlace(@id, @place);
            ")
        .WithParameters(p => {
            p[Parameter.Eval, "id", typeof(int)] = "g.NextHandle()";
            p["place", typeof(string)]           = place.Trim();
        })
        .PerformCheckThenCommand();
        if (refused != "") return Conflict(refused);

        return Content(Board(), "application/json");
    }

    // The map as the golem knows it: places, doors and open boundaries (one print).
    [HttpGet("map")]
    public IActionResult Map() =>
        Content(perf.Actor.Using(@"
            print g.DescribeMap() 'map', g.Places() 'places', g.Passages() 'passages';
        ")
        .PerformQuery(), "application/json");

    [HttpGet("state")]
    public IActionResult MissionBoard() => Content(Board(), "application/json");

    // Ask the golem how much road and how much time it still has ahead — through EVERY
    // pending mission, in the order it will run them, along the map's passages, at ITS
    // OWN speed and with ITS OWN pauses. The only telemetry is where the body stands
    // right now: it enters the query as @params (queries never journal).
    [HttpGet("progress")]
    public IActionResult Progress()
    {
        var pose = ros.LatestPose;
        if (pose == null) return StatusCode(503, "no telemetry from the body yet");

        string answer = perf.Actor.Using(@"
            print g.HasPendingMission() 'hasNext', g.Pending() 'pendingPoints', g.Speed() 'speed', g.HoldAfterTold() 'holdAfterTold';
            if (g.HasPendingMission()) {
                print g.NextId() 'mission', g.RouteLength() 'routeLength', g.RouteSeconds() 'routeSeconds';
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
                print g.NextId() 'nextId', g.NextX() 'nextX', g.NextY() 'nextY';
            }
        ")
        .PerformQuery();
}
