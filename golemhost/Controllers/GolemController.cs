using Choreography.Theater;
using Microsoft.AspNetCore.Mvc;
using Puppeteer;

namespace GolemHost.Controllers;

// The golem's own verbs, as endpoints (output-and-controller guide): C# validates
// the inputs, the actor is reused, the request values become @params, and the
// perform's print output IS the response — no DTO, no re-serialization.
public class GolemController : Controller
{
    private readonly PerformanceV2 perf;

    public GolemController(PerformanceV2 perf)
    {
        this.perf = perf;
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

    // One query, one document: the board the panel paints from.
    private string Board() =>
        perf.Actor.Using(@"
            print g.Pending() 'pending', g.Total() 'total', g.HasPendingMission() 'hasNext';
            if (g.HasPendingMission()) {
                print g.NextId() 'nextId', g.NextX() 'nextX', g.NextY() 'nextY';
            }
            print g.WorldSize() 'worldSize', g.WallMargin() 'wallMargin', g.HasRock() 'hasRock';
            if (g.HasRock()) {
                print g.RockX() 'rockX', g.RockY() 'rockY', g.RockRadius() 'rockR';
            }
        ")
        .PerformQuery();
}
