using GolemAPI.Choreography;
using Microsoft.AspNetCore.Mvc;

namespace GolemAPI.Controllers;

// THE GOLEM'S ENDPOINTS: the operator's verbs and the body's reports come in as JSON bodies, typed and VALIDATED here
// (Requests.cs), and go to the Robot's action methods — where the scripts live, validate again in the domain's voice
// and run (Juan, 17-sep-2026: "el controller recibe y valida los parámetros y llama a robot"). Nothing here dispatches
// the next order: every act's print is pushed to the output target (RobotToRos) by the engine's own reaction on the
// act. A refusal comes back in the domain's words (409); the reads the panel needs are queries on the golem's actor.
public class GolemController : Controller
{
    private readonly Robot robot;

    public GolemController(Robot robot)
    {
        this.robot = robot;
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
        return Answered(robot.Move(request.Stops.Select(s => (s.X.Value, s.Y.Value)).ToList()));
    }

    // Send the golem through several points and let it choose the order that makes the way shortest.
    [HttpPost("cover")]
    public IActionResult Cover([FromBody] ErrandRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + ErrandRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        return Answered(robot.Cover(request.Stops.Select(s => (s.X.Value, s.Y.Value)).ToList()));
    }

    // The operator holds the route underway, or lets it go on.
    [HttpPost("pause")]
    public IActionResult Pause() => Answered(robot.Pause());

    [HttpPost("resume")]
    public IActionResult Resume() => Answered(robot.Resume());

    // Somebody took an obstacle away: the golem forgets it, with every mark that outlined it, and tells the peers.
    [HttpPost("forget")]
    public IActionResult Forget([FromBody] PointRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + PointRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        return Answered(robot.Forget(request.X.Value, request.Y.Value));
    }

    // ==================================================================
    // The body's reports (sim/bridge/body.py posts them): arrived, bump, stuck — the robot's whole vocabulary.
    // ==================================================================

    // The body did the one thing it was told: a turn made, a point reached.
    [HttpPost("robot/arrived")]
    public IActionResult RobotArrived([FromBody] ArrivedReport report)
    {
        if (report == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + "{\"route\": 3}");
        var problems = report.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        var answer = robot.Arrived(report.Route.Value);
        if (answer == null) return Accepted("not the order the body was given");
        return answer.Value.Ok ? Accepted() : Conflict(answer.Value.Refused);
    }

    // The body bumped into something: its motors stopped at once. What it was and what follows is the domain's.
    [HttpPost("robot/bump")]
    public IActionResult RobotBump([FromBody] BumpReport report)
    {
        if (report == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + "{\"route\": 3, \"with\": \"crate_center\", \"x\": 5.2, \"y\": 5.8, \"heading\": -1.57, \"px\": 5.2, \"py\": 6.05, \"ptheta\": -1.57}");
        var problems = report.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        _ = robot.BumpedAsync(report.Route.Value, report.With.Trim(), report.X.Value, report.Y.Value, report.Heading.Value, report.Px.Value, report.Py.Value, report.Ptheta.Value);
        return Accepted();
    }

    // The body could not: stalled, timed out.
    [HttpPost("robot/stuck")]
    public IActionResult RobotStuck([FromBody] StuckReport report)
    {
        if (report == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + "{\"route\": 3, \"reason\": \"stalled\"}");
        var problems = report.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        var answer = robot.Stuck(report.Route.Value, report.Reason.Trim());
        if (answer == null) return Accepted("not the order the body was given");
        return answer.Value.Ok ? Accepted() : Conflict(answer.Value.Refused);
    }

    // ==================================================================
    // Reads (queries) the panel asks
    // ==================================================================

    // The map as it is laid out: zones, doors and open sides — read from the map module itself.
    [HttpGet("map")]
    public IActionResult Map() =>
        Content(robot.Actor.Using(@"
            print map.Name 'map';
            foreach (places in map.Zones) {
                print places.Name 'name', places.X 'x', places.Y 'y', places.Width 'w', places.Height 'h',
                      places.Center.X 'cx', places.Center.Y 'cy';
                foreach (doors in places.Doorways()) {
                    print doors.To 'to', doors.At.X 'x', doors.At.Y 'y';
                }
                foreach (opens in places.OpenSides()) {
                    print opens.To 'to';
                }
            }
        ")
        .PerformQuery(), "application/json");

    // The obstacles the golem hypothesizes: one row per obstacle and, under it, one per vertex — the touches that outlined it.
    [HttpGet("obstacles")]
    public IActionResult Obstacles() =>
        Content(robot.Actor.Using(@"
            print collisions.All().Count 'total', collisions.Things().Count 'things',
                  collisions.EncounterCount 'met', collisions.MarkCount 'marks';
            foreach (obstacles in collisions.All()) {
                print obstacles.Kind 'kind', obstacles.Where 'zone', obstacles.Shape 'shape', obstacles.Size 'size',
                      obstacles.Who 'who', obstacles.Center.X 'cx', obstacles.Center.Y 'cy';
                foreach (vertices in obstacles.Vertices()) {
                    print vertices.At.X 'x', vertices.At.Y 'y', vertices.Heading 'normal', vertices.Reach 'reach';
                }
            }
        ")
        .PerformQuery(), "application/json");

    [HttpGet("state")]
    public IActionResult MissionBoard() => Content(Board(), "application/json");

    // How much way and how much time lie ahead, through every pending route, at the body's own speed and lingers;
    // the only telemetry is where the body believes it stands, entering as @params.
    [HttpGet("progress")]
    public IActionResult Progress()
    {
        var pose = robot.Pose;
        if (pose == null) return StatusCode(503, "no telemetry from the body yet");
        return Content(robot.Actor.Using(@"
            print g.HasPendingMission() 'hasNext', g.PendingRoutes().Count 'pendingMissions',
                  body.Speed.InMetersPerSecond 'speed', body.LingerAfterTold.InSeconds 'lingerAfterTold';
            if (g.HasPendingMission()) {
                print g.Underway().Id 'mission', g.Underway().StopsLeft 'stopsLeft',
                      g.RouteLength() 'routeLength', g.RouteSeconds() 'routeSeconds';
                if (map.IsOnMap(Position(@x, @y))) {
                    print g.DistanceLeft(Position(@x, @y)) 'distanceLeft',
                          g.SecondsLeft(Position(@x, @y)) 'secondsLeft',
                          map.ZoneAt(Position(@x, @y)).Name 'here';
                }
            }
        ")
        .WithParameters(p => { p["x", typeof(double)] = pose.X; p["y", typeof(double)] = pose.Y; })
        .PerformQuery(), "application/json");
    }

    // One query, one document: the board the panel paints from.
    private string Board() =>
        robot.Actor.Using(@"
            print g.PendingRoutes().Count 'pending', g.Routes().Count 'total', g.HasPendingMission() 'hasNext';
            if (g.HasPendingMission()) {
                print g.Underway().Id 'nextId',
                      g.Underway().NextLeg.Target.X 'nextX', g.Underway().NextLeg.Target.Y 'nextY',
                      g.Underway().StopsLeft 'stopsLeft', g.Underway().Paused 'paused';
            }
        ")
        .PerformQuery();

    // The action's answer becomes the response: refused → 409 in the domain's words; done → the board back to the
    // operator. The next order is already on its way to the body: the reaction on the act pushed it.
    private IActionResult Answered(Answer answer) => answer.Ok ? Content(Board(), "application/json") : Conflict(answer.Refused);
}
