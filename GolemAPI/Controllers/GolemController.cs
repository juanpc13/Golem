using GolemAPI.Choreography;
using GolemAPI.Commanding;
using GolemAPI.Choreography.Roles;
using Microsoft.AspNetCore.Mvc;

namespace GolemAPI.Controllers;

// THE GOLEM'S ENDPOINTS: the operator's verbs and the body's reports come in as JSON bodies, typed and VALIDATED here
// (Requests.cs), and go to the ROLE of the capability they belong to (Juan, 18-sep-2026: "los roles los vamos a dejar en el
// lado de los controllers… en base a las capacidades del robot; si no tiene dicho rol no podemos mandar a usarlo"): the
// errand, the hold and the reports of a turn or a move to the Displacer (the motors); the bump and the forget to the
// CollisionCaptor (the bumper). The embodiment plays only the roles the operator declared for this body (`ROLES` in the
// compose); an endpoint whose role the body lacks answers 409 saying which capability is missing. The scripts live in the
// roles, validate again in the domain's voice and run (Juan, 17-sep: "el controller recibe y valida los parámetros y llama").
// Nothing here dispatches the next order: every act's print is pushed to the output target (RobotMechanics) by the engine's
// own reaction on the act. A refusal comes back in the domain's words (409); the reads the panel needs are queries on the
// golem's actor.
public class GolemController : Controller
{
    private readonly GolemEmbodiment golemEmbodiment;

    public GolemController(GolemEmbodiment golemEmbodiment)
    {
        this.golemEmbodiment = golemEmbodiment;
    }

    // The role an endpoint needs, or the refusal that says the body has no such capability.
    private bool Motors(out Displacer displacer, out IActionResult refusal)
    {
        displacer = golemEmbodiment.Displacer;
        refusal = displacer == null ? Conflict($"this body has no motors: the role '{Capabilities.Displacer}' is not among its capabilities ({golemEmbodiment.Capabilities})") : null;
        return displacer != null;
    }

    private bool Bumper(out CollisionCaptor captor, out IActionResult refusal)
    {
        captor = golemEmbodiment.Captor;
        refusal = captor == null ? Conflict($"this body has no bumper: the role '{Capabilities.CollisionCaptor}' is not among its capabilities ({golemEmbodiment.Capabilities})") : null;
        return captor != null;
    }

    // ==================================================================
    // The operator's verbs
    // ==================================================================

    // Send the golem through points in THIS order — {"stops": [{"x": 2.0, "y": 9.5}, {"x": 9.0, "y": 8.0}]}.
    [HttpPost("move")]
    public IActionResult MoveTo([FromBody] ErrandRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + ErrandRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (!Motors(out var displacer, out var refusal)) return refusal;
        return Answered(displacer.Move(request.Stops.Select(s => (s.X.Value, s.Y.Value)).ToList()));
    }

    // Send the golem through several points and let it choose the order that makes the way shortest.
    [HttpPost("cover")]
    public IActionResult Cover([FromBody] ErrandRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + ErrandRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (!Motors(out var displacer, out var refusal)) return refusal;
        return Answered(displacer.Cover(request.Stops.Select(s => (s.X.Value, s.Y.Value)).ToList()));
    }

    /// <summary>A FORMATION TOLD (propuesta 104): the golem keeps it by its name, with its fleet and its policy, and tells every golem of the
    /// fleet; it answers the places it resolved and who holds each by rank. Left out, the fleet is this golem and every peer it can reach.</summary>
    [HttpPost("form")]
    public IActionResult Form([FromBody] FormRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + FormRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (!Motors(out var displacer, out var refusal)) return refusal;
        var fleet = request.Fleet is { Count: > 0 } ? request.Fleet.Select(n => n.Trim().ToLowerInvariant()).Distinct().ToList()
                                                    : new[] { golemEmbodiment.Name.ToLowerInvariant() }.Concat(golemEmbodiment.Peers.Select(p => p.ToLowerInvariant())).Distinct().ToList();
        var center = (request.Center.X.Value, request.Center.Y.Value);
        string name = request.Name.Trim().ToLowerInvariant();
        if (request.IsRings)
            return Answered(displacer.FormRings(name, center, request.Radius.Value, request.InnerRadius.Value, request.Angle ?? 0.0,
                                                fleet, request.InnerFleet.Select(n => n.Trim().ToLowerInvariant()).Distinct().ToList(), request.Policy));
        return Answered(displacer.Form(name, request.Kind, center, request.Measure.Value, request.Angle ?? 0.0, fleet, request.Policy));
    }

    /// <summary>A FORMATION TAKEN (propuesta 104): this golem says where it stands; the fleet's words open the routes, each to its place by
    /// the policy. It spreads by itself: command one golem.</summary>
    [HttpPost("take")]
    public IActionResult Take([FromBody] TakeRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + TakeRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (!Motors(out var displacer, out var refusal)) return refusal;
        return Answered(displacer.Take(request.Name.Trim().ToLowerInvariant()));
    }

    /// <summary>One STEP of a formation (ajuste 77; propuesta 104): every body of the ring — or of the whole figure — takes the next place
    /// in that sense once everybody stands on its own; steps queue. It spreads by itself: command one golem.</summary>
    [HttpPost("rotate")]
    public IActionResult Rotate([FromBody] RotateRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + RotateRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (!Motors(out var displacer, out var refusal)) return refusal;
        return Answered(displacer.Rotate(request.Name.Trim().ToLowerInvariant(), request.Sense.Trim().ToLowerInvariant(), request.Orbit));
    }

    /// <summary>A FORMATION DISSOLVED (propuesta 104): it leaves every golem of the fleet.</summary>
    [HttpPost("dissolve")]
    public IActionResult Dissolve([FromBody] DissolveRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + DissolveRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (!Motors(out var displacer, out var refusal)) return refusal;
        return Answered(displacer.Dissolve(request.Name.Trim().ToLowerInvariant()));
    }

    // The operator holds the golem where its body stands, or lets it go on.
    [HttpPost("pause")]
    public IActionResult Pause() => Motors(out var displacer, out var refusal) ? Answered(displacer.Pause()) : refusal;

    [HttpPost("resume")]
    public IActionResult Resume() => Motors(out var displacer, out var refusal) ? Answered(displacer.Resume()) : refusal;

    // One more stop, told to the newest route while it is pending — {"x": 5.5, "y": 5.5} (propuesta 58: the console's `then`).
    [HttpPost("then")]
    public IActionResult Then([FromBody] PointRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + PointRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (!Motors(out var displacer, out var refusal)) return refusal;
        return Answered(displacer.Then((request.X.Value, request.Y.Value)));
    }

    // Somebody took an obstacle away: the golem forgets it, with every mark that outlined it, and tells the peers.
    [HttpPost("forget")]
    public IActionResult Forget([FromBody] PointRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + PointRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (!Bumper(out var captor, out var refusal)) return refusal;
        return Answered(captor.Forget(request.X.Value, request.Y.Value));
    }

    // ==================================================================
    // Reads (queries) the panel asks
    // ==================================================================

    // The map as it is laid out: zones, doors and open sides — read from the map module itself.
    [HttpGet("map")]
    public IActionResult Map() =>
        Content(golemEmbodiment.Actor.Using(@"
            print g.Current.Map.Name 'map';
            foreach (places in g.Current.Map.Zones) {
                print places.Name 'name', places.X 'x', places.Y 'y', places.Width 'w', places.Height 'h',
                      places.Center.X 'cx', places.Center.Y 'cy';
                foreach (doors in places.Doorways()) {
                    print doors.To 'to', doors.At.X 'x', doors.At.Y 'y', doors.Width 'w';
                }
                foreach (opens in places.OpenSides()) {
                    print opens.To 'to';
                }
            }
        ")
        .PerformQuery(), "application/json");

    // The obstacles the golem hypothesizes: one row per obstacle and, under it, one per vertex — the touches that outlined it.
    [HttpGet("obstacles")]
    public IActionResult Obstacles() => Content(Readings.Obstacles(golemEmbodiment.Actor), "application/json");

    [HttpGet("state")]
    public IActionResult MissionBoard() => Content(Board(), "application/json");

    // How much way and how much time lie ahead, through every pending route, at the body's own speed and lingers;
    // the only telemetry is where the body believes it stands, entering as @params.
    [HttpGet("progress")]
    public IActionResult Progress()
    {
        var pose = golemEmbodiment.Pose;
        if (pose == null) return StatusCode(503, "no telemetry from the body yet");
        return Content(golemEmbodiment.Actor.Using(@"
            {
                print g.HasPendingMission() 'hasNext', g.PendingRoutes().Count 'pendingMissions',
                      body.Speed.InMetersPerSecond 'speed', body.LingerAfterTold.InSeconds 'lingerAfterTold';
                if (g.HasPendingMission()) {
                    route = g.Underway();
                    here = Position(@px, @py);
                    print route.Id 'mission', route.StopsLeft 'stopsLeft', g.RouteLength() 'routeLength', g.RouteSeconds() 'routeSeconds';
                    if (g.Current.Map.IsOnMap(here)) {
                        print g.DistanceLeft(here) 'distanceLeft', g.SecondsLeft(here) 'secondsLeft', g.Current.Map.ZoneAt(here).Name 'here';
                    }
                }
            }
        ")
        .WithParameters(p => {
            p["px", typeof(double)] = pose.X;
            p["py", typeof(double)] = pose.Y;
        })
        .PerformQuery(), "application/json");
    }

    // One query, one document: the board the panel paints from (Readings.Board: the console reads the same one).
    private string Board() => Readings.Board(golemEmbodiment.Actor);

    // The action's answer becomes the response: refused → 409 in the domain's words; done → the board back to the
    // operator. The next order is already on its way to the body: the reaction on the act pushed it.
    // What an operator's verb answers: the board as always, and — since 23-sep-2026 (Juan: "ese print no sería posible verlo…
    // que el Send retorne ese response") — the PRINT the command itself returned, the same order the engine pushes to the body.
    // The panel reads only whether it was refused; a scenario reads the print.
    private IActionResult Answered(Answer answer)
    {
        if (!answer.Ok) return Conflict(answer.Refused);
        var board = System.Text.Json.Nodes.JsonNode.Parse(Board())?.AsObject() ?? new System.Text.Json.Nodes.JsonObject();
        if (!string.IsNullOrWhiteSpace(answer.Print))
            try { board["print"] = System.Text.Json.Nodes.JsonNode.Parse(answer.Print); }
            catch (System.Text.Json.JsonException) { board["print"] = answer.Print; }
        return Content(board.ToJsonString(), "application/json");
    }
}
