using GolemAPI.Membrane;
using Microsoft.AspNetCore.Mvc;

namespace GolemAPI.Controllers;

// The receiving side of the HttpBroker wire: a peer golem POSTs one broker record;
// Deliver fans it out to whoever subscribed the topic locally. A 2xx means a
// consumer took it — otherwise the origin keeps retrying and the gap stays visible.
public class TellController : Controller
{
    private readonly HttpBroker wire;

    public TellController(HttpBroker wire)
    {
        this.wire = wire;
    }

    [HttpPost("tell")]
    public IActionResult HearFromPeer([FromBody] Frame frame)
    {
        if (frame == null || string.IsNullOrWhiteSpace(frame.Topic))
            return BadRequest("a record needs at least a topic");
        return wire.Deliver(frame.Topic, frame.Key, frame.Headers, frame.Value)
            ? Ok()
            : StatusCode(503, $"no consumer took '{frame.Topic}' yet");
    }
}
