using GolemHost.Membrane;
using Microsoft.AspNetCore.Mvc;

namespace GolemHost.Controllers;

// The receiving side of the HttpBroker wire (after VeladaApp's PhoneToPhone):
// a peer golem POSTs one broker record here; Deliver fans it out to whoever
// subscribed the topic locally (the tell uptake, the ack handler).
public class TellController : Controller
{
    private readonly HttpBroker wire;

    public TellController(HttpBroker wire)
    {
        this.wire = wire;
    }

    public sealed record Frame(string Topic, string Key, Dictionary<string, string> Headers, string Value);

    [HttpPost("tell")]
    public IActionResult Receive([FromBody] Frame frame)
    {
        if (frame == null || string.IsNullOrWhiteSpace(frame.Topic))
            return BadRequest("a record needs at least a topic");
        wire.Deliver(frame.Topic, frame.Key, frame.Headers, frame.Value);
        return Ok();
    }
}
