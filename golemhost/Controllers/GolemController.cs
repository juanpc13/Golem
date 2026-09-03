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

    // Entrust a mission. The handle is minted at the actor (Eval) and frozen into the
    // journaled arguments, so the Reaction that echoes visited points can correlate on it.
    [HttpPost("assign")]
    public IActionResult AssignMission([FromQuery] double x, [FromQuery] double y)
    {
        if (!double.IsFinite(x) || !double.IsFinite(y)) return BadRequest("x and y must be finite numbers");

        perf.Actor.Using(@"
            g.Assign(@id, @x, @y);
        ")
        .WithParameters(p => {
            p[Parameter.Eval, "id", typeof(int)] = "g.NextHandle()";
            p["x", typeof(double)]               = x;
            p["y", typeof(double)]               = y;
        })
        .PerformCommand();

        return Content(Board(), "application/json");
    }

    [HttpGet("state")]
    public IActionResult MissionBoard() => Content(Board(), "application/json");

    // Ask the golem how much road and how much time it still has ahead — through EVERY
    // pending mission, in the order it will run them, at ITS OWN speed and with ITS OWN
    // pauses (both released into its journal). The only telemetry is where the body
    // stands right now: it enters the query as @params (queries never journal) and only
    // adds the leg to the first pending point.
    [HttpGet("progress")]
    public IActionResult Progress()
    {
        var pose = ros.LatestPose;
        if (pose == null) return StatusCode(503, "no telemetry from the body yet");

        string answer = perf.Actor.Using(@"
            print g.HasPendingMission() 'hasNext', g.Pending() 'pendingPoints', g.Speed() 'speed', g.HoldAfterTold() 'holdAfterTold';
            if (g.HasPendingMission()) {
                print g.NextId() 'mission', g.RouteLength() 'routeLength', g.RouteSeconds() 'routeSeconds',
                      g.DistanceLeft(@x, @y) 'distanceLeft', g.SecondsLeft(@x, @y) 'secondsLeft';
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
