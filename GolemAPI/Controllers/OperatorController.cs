using System.Text.Json;
using Choreography.Theater;
using Puppeteer;
using GolemAPI.Choreography;
using GolemAPI.Commanding;
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
    private readonly GolemEmbodiment golemEmbodiment;   // the golem's actor is its Actor: the lab console performs on golemEmbodiment.Actor
    private readonly PanelFeed feed;
    private readonly IBodyWire ros;
    private readonly GolemIdentity identity;

    public OperatorController(PerformanceV2 performance, GolemEmbodiment golemEmbodiment, PanelFeed feed, IBodyWire ros, GolemIdentity identity)
    {
        this.performance = performance;
        this.golemEmbodiment = golemEmbodiment;
        this.feed = feed;
        this.ros = ros;
        this.identity = identity;
    }

    // The web panel is gone (propuesta 93, 6-oct-2026; Juan: "eliminemos el panel de los golems web"): the operator's console is the
    // desktop program WardenCli, a client of /command, /commands, /body and /events. The root says so to a browser.
    [HttpGet("/")]
    public IActionResult Root() =>
        Content($"golem {identity.Golem}: no web panel — command it from WardenCli (the desktop console) through POST /command; GET /commands lists the language, GET /body the telemetry, GET /events the journal's feed.", "text/plain; charset=utf-8");

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
            peers = identity.Peers,   // the golems a line may name besides this one (its tell routes)
            entry = performance.CurrentEntryId,
            poseSource = ros.Source == PoseSource.Wheels ? "wheels" : "world",
            scenario = golemEmbodiment.Scenario,   // the frame the golem reads its pose in (propuesta 109)
            at = golemEmbodiment.Pose == null ? null : new { x = golemEmbodiment.Pose.X, y = golemEmbodiment.Pose.Y, theta = golemEmbodiment.Pose.Theta },
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
            return Content(golemEmbodiment.Actor.Using(script).PerformQuery(), "application/json");
        }
        catch (Exception ex)
        {
            return BadRequest(ex.Message);
        }
    }

    // THE COMMAND LINE (propuesta 58): one line of the language — {"line": "visit (2, 9.5) (9, 8)"} — read and acted as the buttons
    // would; text back for a console (the default), the endpoint's JSON for a program (Accept: application/json). 400 when the
    // line is no command (what was expected, where), 409 when the domain refused, in its words.
    [HttpPost("command")]
    public async Task<IActionResult> Command([FromBody] CommandRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + CommandRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        var reply = await new Commander(golemEmbodiment).ExecuteAsync(request.Line);
        bool asJson = Request.Headers.Accept.Any(a => a != null && a.Contains("application/json", StringComparison.OrdinalIgnoreCase));
        string body = asJson && reply.Json != "" ? reply.Json : reply.Text;
        string type = asJson && reply.Json != "" ? "application/json" : "text/plain; charset=utf-8";
        return reply.Kind switch
        {
            "done" => Content(body, type),
            "refused" => StatusCode(409, reply.Text),
            _ => BadRequest(reply.Text),
        };
    }

    // The language, for the panel's help and anyone else: every verb, how it is written, what it does, an example.
    [HttpGet("commands")]
    public IActionResult Commands() => Content(JsonSerializer.Serialize(CommandLine.Help), "application/json");

    // The golem enters a scenario it knows (ajuste 60): {"scenario": "open-floor"} — its map and its collisions from here on.
    [HttpPost("enter")]
    public async Task<IActionResult> Enter([FromBody] ScenarioRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + ScenarioRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        var answer = await golemEmbodiment.EnterAsync(request.Scenario);
        return answer.Ok ? Content(answer.Print ?? "{}", "application/json") : StatusCode(409, answer.Refused);
    }

    // The golem optimizes the routes it computes by one of its strategies (ajuste 61; `optimize` since ajuste 82): {"navigation": "on-the-way"} — every route from here on is improved with it.
    [HttpPost("optimize")]
    public IActionResult Optimize([FromBody] NavigationRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + NavigationRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (golemEmbodiment.Displacer is not { } motors) return StatusCode(409, $"this body has no motors: the role '{Capabilities.Displacer}' is not among its capabilities ({golemEmbodiment.Capabilities})");
        var answer = motors.Optimize(request.Navigation);
        return answer.Ok ? Content(answer.Print ?? "{}", "application/json") : StatusCode(409, answer.Refused);
    }

    // The body's pace from here on (propuesta 111): {"speed": 3.0, "turn": 2.5} — the cruise in m/s and, when given, the turn rate in rad/s.
    [HttpPost("speed")]
    public IActionResult Pace([FromBody] PaceRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + PaceRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (golemEmbodiment.Displacer is not { } pacing) return StatusCode(409, $"this body has no motors: the role '{Capabilities.Displacer}' is not among its capabilities ({golemEmbodiment.Capabilities})");
        var answer = pacing.Pace(request.Speed, request.Turn);
        return answer.Ok ? Content(answer.Print ?? "{}", "application/json") : StatusCode(409, answer.Refused);
    }

    /// <summary>Where the golem stands and what is in force (16-oct-2026; the line `where`): whether it knows, held, its scenario and zone, the
    /// navigation it optimizes by, the body's cruise and turn rate — the same document the console reads.</summary>
    [HttpGet("where")]
    public IActionResult Where() => Content(Readings.Where(golemEmbodiment.Actor), "application/json");

    // THE BODY CARRIED (16-oct-2026; the line `place x,y`): {"x": 14.25, "y": 8.25} in the frame of the scenario the golem is in — every pending
    // route let go, the body teleported there and the golem woken where it stands
    [HttpPost("place")]
    public async Task<IActionResult> Place([FromBody] PointRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + PointRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        await golemEmbodiment.PlaceAsync(request.X!.Value, request.Y!.Value);
        return Content(Readings.Where(golemEmbodiment.Actor), "application/json");
    }

    // THE ANCHOR (13-oct-2026; the line `anchor`): the golem takes where its body really stands, from telemetry, as where it stands — nothing moved
    [HttpPost("anchor")]
    public IActionResult Anchor()
    {
        var answer = golemEmbodiment.Anchor();
        return answer.Ok ? Content(answer.Print ?? "{}", "application/json") : StatusCode(409, answer.Refused);
    }

    // The scenarios the golem knows, and the one it is in.
    [HttpGet("scenarios")]
    public IActionResult Scenarios() => Content(Readings.Scenarios(golemEmbodiment.Actor), "application/json");

    /// <summary>The formations the warden told this golem (propuesta 99).</summary>
    [HttpGet("formations")]
    public IActionResult Formations() => Content(Readings.Formations(golemEmbodiment.Actor), "application/json");

    /// <summary>The unions the warden told this golem (propuesta 123): the same document the line `unions` answers.</summary>
    [HttpGet("unions")]
    public IActionResult Unions() => Content(Readings.Unions(golemEmbodiment.Actor), "application/json");

    /// <summary>The disputes over the places of its formations (14-oct-2026): the same document the line `disputes` answers.</summary>
    [HttpGet("disputes")]
    public IActionResult Disputes() => Content(Readings.Disputes(golemEmbodiment.Actor), "application/json");

    /// <summary>A play forgotten (16-oct-2026; the line `forget play <name>`): off the list once over or never set out.</summary>
    [HttpPost("forget-play")]
    public IActionResult ForgetPlay([FromBody] ForgetPlayRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + ForgetPlayRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (golemEmbodiment.Dynamics is not { } dynamics) return StatusCode(409, $"this body plays no dynamics: the role '{Capabilities.Dynamics}' is not among its capabilities ({golemEmbodiment.Capabilities})");
        var answer = dynamics.ForgetPlay(request.Name);
        return answer.Ok ? Content(answer.Print ?? "{}", "application/json") : StatusCode(409, answer.Refused);
    }

    /// <summary>Every dispute forgotten (14-oct-2026; the line `forget disputes`): this copy's go, and it settles no more.</summary>
    [HttpPost("forget-disputes")]
    public IActionResult ForgetDisputes()
    {
        if (golemEmbodiment.Choreographer is not { } choreographer) return StatusCode(409, $"this body plays no choreographer: the role '{Capabilities.Choreographer}' is not among its capabilities ({golemEmbodiment.Capabilities})");
        var answer = choreographer.ForgetDisputes();
        return answer.Ok ? Content(answer.Print ?? "{}", "application/json") : StatusCode(409, answer.Refused);
    }

    [HttpPost("reset")]
    public async Task<IActionResult> LetGo()
    {
        await golemEmbodiment.LetGoAsync();
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
        await golemEmbodiment.ResetEverythingAsync(request.Cascade.Value);
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
