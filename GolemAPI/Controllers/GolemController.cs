using GolemAPI.Choreography;
using GolemAPI.Commanding;
using GolemAPI.Choreography.Roles;
using Microsoft.AspNetCore.Mvc;

namespace GolemAPI.Controllers;

// THE GOLEM'S ENDPOINTS: the operator's verbs and the body's reports come in as JSON bodies, typed and VALIDATED here
// (Requests.cs), and go to the ROLE of the capability they belong to (Juan, 18-sep-2026: "los roles los vamos a dejar en el
// lado de los controllers… en base a las capacidades del robot; si no tiene dicho rol no podemos mandar a usarlo"): the
// errand, the hold and the reports of a turn or a move to the Displacer (the motors); the bump and the forget to the
// BumperRol (the bumper); the formations to the ChoreographerRol and the plays to the DynamicsRol (16-oct-2026: a role per module
// of the mind, each with every script of its acts). The embodiment plays only the roles the operator declared for this body (`ROLES` in the
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
    private bool Motors(out DisplacerRol displacer, out IActionResult refusal)
    {
        displacer = golemEmbodiment.Displacer;
        refusal = displacer == null ? Conflict($"this body has no motors: the role '{Capabilities.Displacer}' is not among its capabilities ({golemEmbodiment.Capabilities})") : null;
        return displacer != null;
    }

    private bool Bumper(out BumperRol captor, out IActionResult refusal)
    {
        captor = golemEmbodiment.Bumper;
        refusal = captor == null ? Conflict($"this body has no bumper: the role '{Capabilities.Bumper}' is not among its capabilities ({golemEmbodiment.Capabilities})") : null;
        return captor != null;
    }

    // the roles of the mind's modules (16-oct-2026): the formations and the plays, each with its own scripts
    private bool Choreographer(out ChoreographerRol choreographer, out IActionResult refusal)
    {
        choreographer = golemEmbodiment.Choreographer;
        refusal = choreographer == null ? Conflict($"this body plays no choreographer: the role '{Capabilities.Choreographer}' is not among its capabilities ({golemEmbodiment.Capabilities})") : null;
        return choreographer != null;
    }

    private bool Explorer(out ExplorerRol explorer, out IActionResult refusal)
    {
        explorer = golemEmbodiment.Explorer;
        refusal = explorer == null ? Conflict($"this body explores nothing: the role '{Capabilities.Explorer}' is not among its capabilities ({golemEmbodiment.Capabilities})") : null;
        return explorer != null;
    }

    // THE EXPLORATION MODE (propuesta 120): on — the golem probes by itself until nothing unknown is left next to what it knows — and off.
    [HttpPost("explore")]
    public IActionResult Explore()
    {
        if (!Explorer(out var explorer, out var refusal)) return refusal;
        var answer = explorer.Explore();
        return answer.Ok ? Content(answer.Print ?? "{}", "application/json") : StatusCode(409, answer.Refused);
    }

    [HttpPost("explore-stop")]
    public IActionResult StopExploring()
    {
        if (!Explorer(out var explorer, out var refusal)) return refusal;
        var answer = explorer.StopExploring();
        return answer.Ok ? Content(answer.Print ?? "{}", "application/json") : StatusCode(409, answer.Refused);
    }

    // What was explored here, cleared (propuesta 120): the trail afresh, the marks forgotten — stopped first.
    [HttpPost("explore-clear")]
    public IActionResult ClearExploration()
    {
        if (!Explorer(out var explorer, out var refusal)) return refusal;
        var answer = explorer.ClearExploration();
        return answer.Ok ? Content(answer.Print ?? "{}", "application/json") : StatusCode(409, answer.Refused);
    }

    // The deduced map exported as a scenario of the golem's (propuesta 120), under the name given.
    [HttpPost("export-map")]
    public IActionResult ExportMap([FromBody] ForgetPlayRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + "{\"name\": \"explored-1\"}");
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("the map's name is needed: {\"name\": \"explored-1\"}");
        if (!Explorer(out var explorer, out var refusal)) return refusal;
        var answer = explorer.ExportMap(request.Name);
        return answer.Ok ? Content(answer.Print ?? "{}", "application/json") : StatusCode(409, answer.Refused);
    }

    // A scenario the explorer exported, dropped (propuesta 120).
    [HttpPost("forget-map")]
    public IActionResult ForgetMap([FromBody] ForgetPlayRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + "{\"name\": \"explored-1\"}");
        if (string.IsNullOrWhiteSpace(request.Name)) return BadRequest("the map's name is needed: {\"name\": \"explored-1\"}");
        var answer = golemEmbodiment.DropScenario(request.Name);
        return answer.Ok ? Content(answer.Print ?? "{}", "application/json") : StatusCode(409, answer.Refused);
    }

    private bool Dynamics(out DynamicsRol dynamics, out IActionResult refusal)
    {
        dynamics = golemEmbodiment.Dynamics;
        refusal = dynamics == null ? Conflict($"this body plays no dynamics: the role '{Capabilities.Dynamics}' is not among its capabilities ({golemEmbodiment.Capabilities})") : null;
        return dynamics != null;
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
        if (!Choreographer(out var choreographer, out var refusal)) return refusal;
        var fleet = request.Fleet is { Count: > 0 } ? request.Fleet.Select(n => n.Trim().ToLowerInvariant()).Distinct().ToList()
                                                    : new[] { golemEmbodiment.Name.ToLowerInvariant() }.Concat(golemEmbodiment.Peers.Select(p => p.ToLowerInvariant())).Distinct().ToList();
        var center = (request.Center.X.Value, request.Center.Y.Value);
        string name = request.Name.Trim().ToLowerInvariant();
        if (request.IsPropeller)
            return Answered(choreographer.FormPropeller(name, center, request.Radius.Value, request.Hub.Value, request.BladeCount, request.Angle ?? 0.0, fleet, request.Policy, request.CuedGolem, request.OnCue, request.StartsAfter, request.AskedGolem, request.Every));
        return Answered(choreographer.Form(name, request.Kind, center, request.Measure.Value, request.Angle ?? 0.0, fleet, request.Policy, request.CuedGolem, request.OnCue, request.StartsAfter, request.AskedGolem, request.Every));
    }

    /// <summary>A FORMATION TAKEN (propuesta 104): this golem says where it stands; the fleet's words open the routes, each to its place by
    /// the policy. It spreads by itself: command one golem.</summary>
    [HttpPost("take")]
    public IActionResult Take([FromBody] TakeRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + TakeRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (!Choreographer(out var choreographer, out var refusal)) return refusal;
        return Answered(choreographer.Take(request.Name.Trim().ToLowerInvariant()));
    }

    /// <summary>A PLAY STAGED (propuesta 108, fase 2): pacman by name, the ghosts by name (left out, every peer but pacman); it spreads by itself to the cast.</summary>
    [HttpPost("play")]
    public IActionResult Play([FromBody] PlayRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + PlayRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (!Dynamics(out var player, out var refusal)) return refusal;
        string pacman = request.Pacman.Trim().ToLowerInvariant();
        var ghosts = request.Ghosts is { Count: > 0 } ? request.Ghosts.Select(n => n.Trim().ToLowerInvariant()).Distinct().ToList()
                                                      : new[] { golemEmbodiment.Name.ToLowerInvariant() }.Concat(golemEmbodiment.Peers.Select(p => p.ToLowerInvariant())).Where(n => n != pacman).Distinct().ToList();
        return Answered(player.Play(request.Name.Trim().ToLowerInvariant(), request.Kind ?? "pacman", pacman, ghosts, request.As));
    }

    /// <summary>Every play this golem was told (16-oct-2026): the same document the line `plays` answers.</summary>
    [HttpGet("plays")]
    public IActionResult Plays() => Content(Readings.Plays(golemEmbodiment.Actor), "application/json");

    /// <summary>The start of a play on this golem (16-oct-2026; the line `start <name>`): pacman's first run, a ghost's hunt.</summary>
    [HttpPost("start")]
    public IActionResult Start([FromBody] ForgetPlayRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + ForgetPlayRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (!Dynamics(out var player, out var refusal)) return refusal;
        return Answered(player.Start(request.Name));
    }

    /// <summary>The warden ends the play for the whole cast (propuesta 108, fase 4; the line `play over`).</summary>
    [HttpPost("over")]
    public IActionResult Over()
    {
        if (!Dynamics(out var player, out var refusal)) return refusal;
        return Answered(player.Over());
    }

    /// <summary>The play this golem is in (propuesta 108).</summary>
    [HttpGet("play")]
    public IActionResult PlayRead() => Content(Readings.Play(golemEmbodiment.Actor), "application/json");

    /// <summary>One STEP of a formation (ajuste 77; propuesta 104): every body of the ring — or of the whole figure — takes the next place
    /// in that sense once everybody stands on its own; steps queue. It spreads by itself: command one golem.</summary>
    [HttpPost("rotate")]
    public IActionResult Rotate([FromBody] RotateRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + RotateRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (!Choreographer(out var choreographer, out var refusal)) return refusal;
        return Answered(choreographer.Rotate(request.Name.Trim().ToLowerInvariant(), request.Sense.Trim().ToLowerInvariant()));
    }

    /// <summary>THIS GOLEM FINISHES ITS PART of a formation (propuesta 117): queued like the steps, counted when its part is done; the fleet is told,
    /// and when every member finished the last of the fleet cues the next group.</summary>
    [HttpPost("finish")]
    public IActionResult Finish([FromBody] FinishRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + FinishRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (!Choreographer(out var choreographer, out var refusal)) return refusal;
        return Answered(choreographer.Finish(request.Name.Trim().ToLowerInvariant()));
    }

    /// <summary>A FORMATION DISSOLVED (propuesta 104): it leaves every golem of the fleet.</summary>
    [HttpPost("dissolve")]
    public IActionResult Dissolve([FromBody] DissolveRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + DissolveRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (!Choreographer(out var choreographer, out var refusal)) return refusal;
        return Answered(choreographer.Dissolve(request.Name.Trim().ToLowerInvariant()));
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

    /// <summary>The newest route: its status, what it asks now, its plan, why it ended short (16-oct-2026; the line `route`).</summary>
    [HttpGet("route")]
    public IActionResult Route() => Content(Readings.Route(golemEmbodiment.Actor), "application/json");

    // The obstacles the golem hypothesizes: one row per obstacle and, under it, one per vertex — the touches that outlined it.
    [HttpGet("obstacles")]
    public IActionResult Obstacles() => Content(Readings.Obstacles(golemEmbodiment.Actor), "application/json");

    // What the golem explored (propuesta 120): the trail, the cells it knows and the frontier, as its exploration module derives them.
    [HttpGet("explored")]
    public IActionResult Explored() => Content(Readings.Explored(golemEmbodiment.Actor), "application/json");

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
