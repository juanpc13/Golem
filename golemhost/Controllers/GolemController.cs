using System.Text.Json;
using GolemHost.Choreography;
using GolemHost.Panel;
using Microsoft.AspNetCore.Mvc;

namespace GolemHost.Controllers;

// The golem's own endpoints — the actor is managed through controllers, the
// way every API in the house works. Routes match what the panel page calls.
public class GolemController : Controller
{
    private readonly GolemChoreography flow;
    private readonly PanelFeed feed;

    public GolemController(GolemChoreography flow, PanelFeed feed)
    {
        this.flow = flow;
        this.feed = feed;
    }

    [HttpGet("/")]
    public IActionResult Panel() =>
        PhysicalFile(Path.Combine(AppContext.BaseDirectory, "panel.html"), "text/html; charset=utf-8");

    [HttpPost("assign")]
    public IActionResult Assign([FromQuery] double x, [FromQuery] double y) =>
        Content(JsonSerializer.Serialize(flow.OrderMission(x, y)), "application/json");

    [HttpGet("state")]
    public IActionResult State() =>
        Content(flow.StateJson(), "application/json");

    [HttpPost("query")]
    public async Task<IActionResult> Query()
    {
        using var reader = new StreamReader(Request.Body);
        string script = await reader.ReadToEndAsync();
        return Content(flow.AdHocQuery(script), "application/json");
    }

    [HttpPost("reset")]
    public IActionResult Reset() =>
        Content(JsonSerializer.Serialize(flow.ResetEverything()), "application/json");

    // The live journal feed: recent history replayed, then server-sent events.
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
