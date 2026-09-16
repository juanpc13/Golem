using System.Text.Json;
using Choreography.Theater;
using Puppeteer;
using GolemAPI.Choreography;
using GolemAPI.Membrane;
using GolemAPI.Panel;
using Microsoft.AspNetCore.Mvc;

namespace GolemAPI.Controllers;

// The operator's face on the golem — not the golem's own verbs: the debug panel,
// host telemetry, the live projection feed, an ad-hoc query console for the lab,
// and the "let go of everything" lever.
public class OperatorController : Controller
{
    private readonly PerformanceV2 performance;
    private readonly ActorV2 golemActor;
    private readonly Robot robot;
    private readonly PanelFeed feed;
    private readonly Rosbridge ros;
    private readonly GolemIdentity identity;

    public OperatorController(PerformanceV2 performance, ActorV2 golemActor, Robot robot, PanelFeed feed, Rosbridge ros, GolemIdentity identity)
    {
        this.performance = performance;
        this.golemActor = golemActor;
        this.robot = robot;
        this.feed = feed;
        this.ros = ros;
        this.identity = identity;
    }

    [HttpGet("/")]
    public IActionResult Panel() =>
        PhysicalFile(Path.Combine(AppContext.BaseDirectory, "panel.html"), "text/html; charset=utf-8");

    // Host telemetry (not domain state): who I am, my body, the journal's entry, the pose the
    // golem BELIEVES (and acts on), the pose the world reports (for the operator's eyes: the
    // panel's ghost), how far apart they are, and the last thing the body touched.
    [HttpGet("body")]
    public IActionResult Body()
    {
        var pose = ros.LatestPose;
        var truth = ros.LatestTruth;
        var touch = ros.LatestContact;
        double? error = pose == null || truth == null ? null
            : Math.Sqrt((pose.X - truth.X) * (pose.X - truth.X) + (pose.Y - truth.Y) * (pose.Y - truth.Y));
        return Content(JsonSerializer.Serialize(new
        {
            golem = identity.Golem,
            body = identity.Body,
            entry = performance.CurrentEntryId,
            poseSource = ros.Source == PoseSource.Wheels ? "wheels" : "world",
            pose = pose == null ? null : new { x = pose.X, y = pose.Y, theta = pose.Theta },
            truth = truth == null ? null : new { x = truth.X, y = truth.Y, theta = truth.Theta },
            error,
            contact = touch == null ? null : new { with = touch.With, bearingDeg = touch.Bearing * 180 / Math.PI, agoSeconds = (DateTime.UtcNow - touch.At).TotalSeconds }
        }), "application/json");
    }

    // Lab console: an ad-hoc read against the golem's state. A bare expression is
    // wrapped as a query; a full block runs verbatim. Queries never journal — and are
    // not sandboxed: the operator keeps them read-only.
    [HttpPost("query")]
    public IActionResult AnswerAdHoc([FromBody] QueryRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + QueryRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        string script = request.Script.Trim();
        if (!script.StartsWith("{"))
            script = "{ print " + script.TrimEnd(';') + " 'value'; }";
        try
        {
            return Content(golemActor.Using(script).PerformQuery(), "application/json");
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    [HttpPost("reset")]
    public async Task<IActionResult> LetGo()
    {
        await robot.LetGoAsync();
        return Accepted();
    }

    // The hard reset: wipe the journal(s) and reboot reborn. {"cascade": true} (the panel) resets
    // the peers too; a peer asked by another golem gets {"cascade": false}.
    [HttpPost("reset-everything")]
    public async Task<IActionResult> ResetEverything([FromBody] ResetRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + ResetRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        await robot.ResetEverythingAsync(request.Cascade.Value);
        return Accepted();
    }

    // The live feed: recent history replayed, then server-sent events.
    [HttpGet("events")]
    public async Task Events(CancellationToken ct)
    {
        Response.ContentType = "text/event-stream";
        Response.Headers.CacheControl = "no-cache";

        var (replay, live, ticket) = feed.Attach();
        using (ticket)
        {
            foreach (var e in replay)
                await WriteEventAsync(e, ct);
            await Response.Body.FlushAsync(ct);

            try
            {
                await foreach (var e in live.ReadAllAsync(ct))
                    await WriteEventAsync(e, ct);
            }
            catch (OperationCanceledException) { }
        }
    }

    private async Task WriteEventAsync(PanelEvent e, CancellationToken ct)
    {
        await Response.WriteAsync("data: " + JsonSerializer.Serialize(e) + "\n\n", ct);
        await Response.Body.FlushAsync(ct);
    }
}
