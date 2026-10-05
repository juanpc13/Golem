using System.Text.Json;
using Choreography.Theater;
using GolemAPI.Commanding;
using GolemAPI.Coordination;
using GolemAPI.Panel;
using Microsoft.AspNetCore.Mvc;

namespace GolemAPI.Controllers;

// THE WARDEN'S ENDPOINTS (propuesta 88): its page, its board, its console, its journal's feed. No body: no /move, no /body, no levers on
// a body — a golem's lever goes through its line (`golem blue reset`). The tell wire's /tell is TellController's, shared.
public class WardenController : Controller
{
    private readonly WardenMind warden;
    private readonly PerformanceV2 performance;
    private readonly PanelFeed feed;

    public WardenController(WardenMind warden, PerformanceV2 performance, PanelFeed feed)
    {
        this.warden = warden;
        this.performance = performance;
        this.feed = feed;
    }

    [HttpGet("/")]
    public IActionResult Panel() =>
        PhysicalFile(Path.Combine(AppContext.BaseDirectory, "warden.html"), "text/html; charset=utf-8");

    // Who the warden is, whom it can reach, and THE BOARD: what it knows of the golems (their words) and the formation in place.
    [HttpGet("fleet")]
    public IActionResult Fleet()
    {
        var board = JsonDocument.Parse(warden.Board()).RootElement.Clone();
        return Content(JsonSerializer.Serialize(new
        {
            warden = warden.Name,
            golems = warden.Peers,
            entry = performance.CurrentEntryId,
            board,
        }), "application/json");
    }

    // The warden's console: its own commands, and the golems' with the golem in front.
    [HttpPost("command")]
    public async Task<IActionResult> Command([FromBody] CommandRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + CommandRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        var reply = await new WardenCommander(warden).ExecuteAsync(request.Line);
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

    // The language on this console: the warden's own first, then every golem's command (carried with the golem in front).
    [HttpGet("commands")]
    public IActionResult Commands() =>
        Content(JsonSerializer.Serialize(WardenCommander.WardenHelp.Concat(CommandLine.Help.Where(h => h.Verb != "--with" && h.Verb != "place" && h.Verb != "fleet")).ToList()), "application/json");

    // Ad-hoc read-only query in the DSL, for the lab.
    [HttpPost("query")]
    public IActionResult AnswerAdHoc([FromBody] QueryRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + QueryRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        try { return Content(warden.Actor.Using(request.Script.Trim()).PerformQuery(), "application/json"); }
        catch (Exception ex) { return StatusCode(409, Choreography.GolemEmbodiment.Reason(ex)); }
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
            foreach (var e in replay) await WriteEventAsync(e, ct);
            await Response.Body.FlushAsync(ct);
            try
            {
                await foreach (var e in live.ReadAllAsync(ct)) await WriteEventAsync(e, ct);
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
