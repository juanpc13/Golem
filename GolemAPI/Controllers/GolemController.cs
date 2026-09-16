using System.Globalization;
using System.Text.Json;
using GolemAPI.Choreography;
using GolemAPI.Membrane;
using Microsoft.AspNetCore.Mvc;
using Puppeteer;

namespace GolemAPI.Controllers;

// EVERY SCRIPT THE GOLEM'S JOURNAL RECEIVES LIVES HERE (Juan, 16-sep-2026: "uno esperaría que todo esté ordenado en el
// GolemController… ahí se ve todo el script entero relacionado a la acción, que tiene los print del punto que deberá
// moverse"). The operator's verbs are endpoints; the body's reports (a point reached, a touch, a wall grazed, a peer
// met, the way decided, an ending) are the static scripts below, which the driver performs. Each script is one act in
// the golem's words — a braced block, values as @params, the object found or built and handed to the act — and ENDS
// WITH THE SAME PRINT: what the route asks now (NextOrder). The command returns that print at write time; it is handed
// to the Robot (the output target), which switches on it and sends the same JSON to the body over the websocket. The
// body reports on the /robot/* endpoints below, whose scripts write the act and whose print is the next order. A
// refused Check comes back in the domain's own words. Reads (queries) the panel needs are endpoints too; nothing here renders a document —
// the perform's print output IS the response.
public class GolemController : Controller
{
    private readonly ActorV2 golemActor;   // the golem itself: every script below is performed on it
    private readonly Rosbridge ros;
    private readonly Robot robot;          // the output target: every print ends up in its switch and travels to the body

    public GolemController(ActorV2 golemActor, Rosbridge ros, Robot robot)
    {
        this.golemActor = golemActor;
        this.ros = ros;
        this.robot = robot;
    }

    // ==================================================================
    // What the route asks now — the print every script ends with. `hold`: the operator paused it. `decide`: it has
    // no way, or a bump interrupted it — the body's driver writes `route.Decide(from)` from where the body stands.
    // `turn`: turn in place to the next leg's heading. `run`: run to the next leg's point — how it is walked (approach,
    // exit) rides along. Nothing pending: nothing printed, no order.
    // ==================================================================
    public const string NextOrder = @"
        if (g.HasPendingMission()) { print g.Next().Id 'route', g.Next().Order 'order'; }
        if (g.HasPendingMission() && g.Next().IsWalkable) {
            print g.Next().NextLeg.Kind 'kind', g.Next().NextLeg.Name 'name',
                  g.Next().NextLeg.At.X 'x', g.Next().NextLeg.At.Y 'y',
                  g.Next().NextLeg.Approach.X 'ax', g.Next().NextLeg.Approach.Y 'ay',
                  g.Next().NextLeg.Exit.X 'ex', g.Next().NextLeg.Exit.Y 'ey',
                  g.Next().NextLeg.HasHeading 'hasHeading', g.Next().NextLeg.Heading 'heading',
                  g.Next().Following 'following', g.Next().StopsLeft 'stopsLeft';
        }
    ";

    // ==================================================================
    // The operator's verbs (endpoints). Each endpoint writes its own script, whole, right here — nothing assembled,
    // nothing shared but the print every command ends with (NextOrder) — so the endpoint explains the script as such
    // (Juan, 16-sep-2026: "deja los scripts completos, no hagas método para generalizarlos"). What the operator sends
    // comes as a JSON body, typed and validated (Requests.cs) before a script runs.
    // ==================================================================

    // Send the golem through stops in THIS order — {"stops": [{"area": "kitchen"}, {"x": 9.0, "y": 8.0}]}. The first
    // stop opens the route from where the errand starts and the route decides its whole way inside; every further stop
    // is told to it, one entry each, and it decides again through them all.
    [HttpPost("move")]
    public IActionResult MoveTo([FromBody] ErrandRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + ErrandRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        var start = WhereTheErrandStarts();
        if (start == null) return StatusCode(503, "no telemetry from the body yet: the errand needs a starting point");

        var first = request.Stops[0];
        Answer answer;
        try
        {
            answer = first.IsPoint
                ? Answer.Of(golemActor.Using(
                    @"
                        Check(map.IsOnMap(Position(@x, @y))) Error 'that point is nowhere on the map';
                    ",
                    @"
                        {
                            from = Position(@fx, @fy);
                            point = Position(@x, @y);
                            route = g.Visit(from, point);
                        }
                    " + NextOrder)
                    .WithParameters(p => {
                        p["fx", typeof(double)] = start.Value.X; p["fy", typeof(double)] = start.Value.Y;
                        p["x", typeof(double)] = first.X.Value; p["y", typeof(double)] = first.Y.Value;
                    })
                    .PerformCheckThenCommand())
                : Answer.Of(golemActor.Using(
                    @"
                        Check(map.Knows(@area)) Error 'unknown area';
                    ",
                    @"
                        {
                            from = Position(@fx, @fy);
                            point = map.Find(@area);
                            route = g.Visit(from, point);
                        }
                    " + NextOrder)
                    .WithParameters(p => {
                        p["fx", typeof(double)] = start.Value.X; p["fy", typeof(double)] = start.Value.Y;
                        p["area", typeof(string)] = first.Area.Trim();
                    })
                    .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Conflict($"stop {first.Describe()}: " + Innermost(ex)); }   // no way fits the body, or the domain refused inside
        if (!answer.Ok) return Conflict($"stop {first.Describe()}: " + answer.Refused);

        int id = Newest();
        foreach (var stop in request.Stops.Skip(1))
        {
            try
            {
                answer = stop.IsPoint
                    ? Answer.Of(golemActor.Using(
                        @"
                            Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'the route is no longer pending';
                            Check(map.IsOnMap(Position(@x, @y))) Error 'that point is nowhere on the map';
                        ",
                        @"
                            {
                                route = g.Find(@id);
                                point = Position(@x, @y);
                                route.Then(point);
                            }
                        " + NextOrder)
                        .WithParameters(p => { p["id", typeof(int)] = id; p["x", typeof(double)] = stop.X.Value; p["y", typeof(double)] = stop.Y.Value; })
                        .PerformCheckThenCommand())
                    : Answer.Of(golemActor.Using(
                        @"
                            Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'the route is no longer pending';
                            Check(map.Knows(@area)) Error 'unknown area';
                        ",
                        @"
                            {
                                route = g.Find(@id);
                                point = map.Find(@area);
                                route.Then(point);
                            }
                        " + NextOrder)
                        .WithParameters(p => { p["id", typeof(int)] = id; p["area", typeof(string)] = stop.Area.Trim(); })
                        .PerformCheckThenCommand());
            }
            catch (Exception ex) { return Conflict($"stop {stop.Describe()}: " + Innermost(ex)); }
            if (!answer.Ok) return Conflict($"stop {stop.Describe()}: " + answer.Refused);
        }
        return Answered(answer);
    }

    // Send the golem through several stops and let it choose the order that makes the way shortest: the same errand,
    // opened with g.Cover — the route reorders the stops still ahead every time one is told to it.
    [HttpPost("cover")]
    public IActionResult Cover([FromBody] ErrandRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + ErrandRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        var start = WhereTheErrandStarts();
        if (start == null) return StatusCode(503, "no telemetry from the body yet: the errand needs a starting point");

        var first = request.Stops[0];
        Answer answer;
        try
        {
            answer = first.IsPoint
                ? Answer.Of(golemActor.Using(
                    @"
                        Check(map.IsOnMap(Position(@x, @y))) Error 'that point is nowhere on the map';
                    ",
                    @"
                        {
                            from = Position(@fx, @fy);
                            point = Position(@x, @y);
                            route = g.Cover(from, point);
                        }
                    " + NextOrder)
                    .WithParameters(p => {
                        p["fx", typeof(double)] = start.Value.X; p["fy", typeof(double)] = start.Value.Y;
                        p["x", typeof(double)] = first.X.Value; p["y", typeof(double)] = first.Y.Value;
                    })
                    .PerformCheckThenCommand())
                : Answer.Of(golemActor.Using(
                    @"
                        Check(map.Knows(@area)) Error 'unknown area';
                    ",
                    @"
                        {
                            from = Position(@fx, @fy);
                            point = map.Find(@area);
                            route = g.Cover(from, point);
                        }
                    " + NextOrder)
                    .WithParameters(p => {
                        p["fx", typeof(double)] = start.Value.X; p["fy", typeof(double)] = start.Value.Y;
                        p["area", typeof(string)] = first.Area.Trim();
                    })
                    .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Conflict($"stop {first.Describe()}: " + Innermost(ex)); }   // no way fits the body, or the domain refused inside
        if (!answer.Ok) return Conflict($"stop {first.Describe()}: " + answer.Refused);

        int id = Newest();
        foreach (var stop in request.Stops.Skip(1))
        {
            try
            {
                answer = stop.IsPoint
                    ? Answer.Of(golemActor.Using(
                        @"
                            Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'the route is no longer pending';
                            Check(map.IsOnMap(Position(@x, @y))) Error 'that point is nowhere on the map';
                        ",
                        @"
                            {
                                route = g.Find(@id);
                                point = Position(@x, @y);
                                route.Then(point);
                            }
                        " + NextOrder)
                        .WithParameters(p => { p["id", typeof(int)] = id; p["x", typeof(double)] = stop.X.Value; p["y", typeof(double)] = stop.Y.Value; })
                        .PerformCheckThenCommand())
                    : Answer.Of(golemActor.Using(
                        @"
                            Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'the route is no longer pending';
                            Check(map.Knows(@area)) Error 'unknown area';
                        ",
                        @"
                            {
                                route = g.Find(@id);
                                point = map.Find(@area);
                                route.Then(point);
                            }
                        " + NextOrder)
                        .WithParameters(p => { p["id", typeof(int)] = id; p["area", typeof(string)] = stop.Area.Trim(); })
                        .PerformCheckThenCommand());
            }
            catch (Exception ex) { return Conflict($"stop {stop.Describe()}: " + Innermost(ex)); }
            if (!answer.Ok) return Conflict($"stop {stop.Describe()}: " + answer.Refused);
        }
        return Answered(answer);
    }

    // The operator holds the route underway: the body stops where it stands; plan and cursor keep.
    [HttpPost("pause")]
    public IActionResult Pause()
    {
        int? id = RouteUnderway();
        if (id == null) return Conflict("nothing underway: no pending route");
        var answer = Answer.Of(golemActor.Using(
            @"
                Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'the route is no longer pending';
                Check(!g.Find(@id).Paused) Error 'the route is already paused';
            ",
            @"
                {
                    route = g.Find(@id);
                    route.Pause();
                }
            " + NextOrder)
            .WithParameters(p => { p["id", typeof(int)] = id.Value; })
            .PerformCheckThenCommand());
        return Answered(answer);
    }

    // The operator lets the route go on: the body takes up the thing it was doing, from where it stands.
    [HttpPost("resume")]
    public IActionResult Resume()
    {
        int? id = RouteUnderway();
        if (id == null) return Conflict("nothing underway: no pending route");
        var answer = Answer.Of(golemActor.Using(
            @"
                Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'the route is no longer pending';
                Check(g.Find(@id).Paused) Error 'the route is not paused';
            ",
            @"
                {
                    route = g.Find(@id);
                    route.Resume();
                }
            " + NextOrder)
            .WithParameters(p => { p["id", typeof(int)] = id.Value; })
            .PerformCheckThenCommand());
        return Answered(answer);
    }

    // ---- the reads the endpoints above lean on (queries, no act) ----

    // Where an errand starts: where the body stands — or, when the golem is busy, where its last pending route ends
    // (it will stand there when the new errand comes up). Null with no telemetry yet.
    private (double X, double Y)? WhereTheErrandStarts()
    {
        using var busy = JsonDocument.Parse(golemActor.Using(@"
            print g.HasPendingMission() 'busy';
            if (g.HasPendingMission()) { print g.PlannedEnd().X 'x', g.PlannedEnd().Y 'y'; }
        ").PerformQuery());
        if (busy.RootElement.GetProperty("busy").GetBoolean())
            return (busy.RootElement.GetProperty("x").GetDouble(), busy.RootElement.GetProperty("y").GetDouble());
        var pose = ros.LatestPose;
        return pose == null ? null : (pose.X, pose.Y);
    }

    // The handle of the route just opened, to tell it the rest.
    private int Newest()
    {
        using var doc = JsonDocument.Parse(golemActor.Using("print g.Newest().Id 'id';").PerformQuery());
        return doc.RootElement.GetProperty("id").GetInt32();
    }

    // The route the golem is on, if any.
    private int? RouteUnderway()
    {
        using var board = JsonDocument.Parse(Board());
        return board.RootElement.TryGetProperty("nextId", out var next) ? next.GetInt32() : null;
    }

    // Somebody took it away: the golem forgets the obstacle standing there, with every mark that outlined it. The
    // reaction tells the peers (the point rides beside the act as an expose: a reaction captures no object), who
    // forget it too. No order changes: the way stands.
    [HttpPost("forget")]
    public IActionResult Forget([FromBody] PointRequest request)
    {
        if (request == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + PointRequest.Shape);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        var (x, y) = (request.X, request.Y);
        var answer = Answer.Of(golemActor.Using(
            @"
                Check(collisions.KnowsAt(Position(@x, @y))) Error 'the golem holds no obstacle there';
            ",
            @"
                {
                    at = Position(@x, @y);
                    g.Forget(at);
                }
                expose @x gx, @y gy;
            ")
        .WithParameters(p => { p["x", typeof(double)] = x.Value; p["y", typeof(double)] = y.Value; })
        .PerformCheckThenCommand());
        return answer.Ok ? Content(Board(), "application/json") : Conflict(answer.Refused);
    }

    // The operator lets go of everything: every pending route abandoned with the reason, in ONE command.
    public static Answer LetGo(ActorV2 golemActor, string reason) => Answer.Of(golemActor.Using(@"
            foreach (route in g.PendingRoutes()) {
                route.Abandon(@reason);
            }
        " + NextOrder)
        .WithParameters(p => { p["reason", typeof(string)] = reason; })
        .PerformCommand());

    // ==================================================================
    // THE ROBOT'S REPORTS (endpoints sim/bridge/body.py posts to when it finished the one thing it was told). Each writes
    // the act, whole, right here, and hands the answer to the Robot: its print is the next order, sent on to the body.
    // A report about an order the body was not given (superseded meanwhile) is acknowledged and not journaled.
    // ==================================================================

    // The body turned in place to the heading the next leg asked: the route now asks it to run.
    [HttpPost("robot/turned")]
    public IActionResult RobotTurned([FromBody] TurnedReport report)
    {
        if (report == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + "{\"route\": 3, \"heading\": -1.57}");
        var problems = report.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (!robot.Expects(report.Route.Value, "turn")) return Accepted("not the turn the body was given");
        var answer = Answer.Of(golemActor.Using(
            @"
                Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'route is not pending';
            ",
            @"
                {
                    route = g.Find(@id);
                    route.Turn();
                }
            " + NextOrder)
            .WithParameters(p => { p["id", typeof(int)] = report.Route.Value; })
            .PerformCheckThenCommand());
        robot.Turned(answer);
        return answer.Ok ? Accepted() : Conflict(answer.Refused);
    }

    // The body reached the point it was sent to. A stop is counted (the last one completes the route) and told to the
    // follower — the point rides beside the act as an expose: the reaction that tells captures no object; a point of
    // the way that is no stop — a door, an opening, a point to pass — just moves the route past it. A courtesy step
    // (route 0) is the golem's own business: nothing to journal.
    [HttpPost("robot/reached")]
    public IActionResult RobotReached([FromBody] ReachedReport report)
    {
        if (report == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + "{\"route\": 3, \"x\": 4.0, \"y\": 9.5}");
        var problems = report.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (!robot.Expects(report.Route.Value, "run")) return Accepted("not the run the body was given");
        if (report.Route.Value == 0) { robot.Reached(default); return Accepted(); }
        Answer answer;
        if (robot.Carrying?.Kind == "stop")
            answer = Answer.Of(golemActor.Using(
                @"
                    Check(g.Knows(@id) && g.Find(@id).IsPending() && g.Find(@id).IsStopAhead(Position(@x, @y))) Error 'that is not a stop ahead';
                ",
                @"
                    {
                        route = g.Find(@id);
                        point = Position(@x, @y);
                        route.Reach(point);
                    }
                    expose @id rid, @x rx, @y ry;
                " + NextOrder)
                .WithParameters(p => { p["id", typeof(int)] = report.Route.Value; p["x", typeof(double)] = report.X.Value; p["y", typeof(double)] = report.Y.Value; })
                .PerformCheckThenCommand());
        else
            answer = Answer.Of(golemActor.Using(
                @"
                    Check(g.Knows(@id) && g.Find(@id).IsPending() && g.Find(@id).IsLegAhead(Position(@x, @y))) Error 'that is not a point ahead';
                ",
                @"
                    {
                        route = g.Find(@id);
                        point = Position(@x, @y);
                        route.Reach(point);
                    }
                " + NextOrder)
                .WithParameters(p => { p["id", typeof(int)] = report.Route.Value; p["x", typeof(double)] = report.X.Value; p["y", typeof(double)] = report.Y.Value; })
                .PerformCheckThenCommand());
        robot.Reached(answer);
        return answer.Ok ? Accepted() : Conflict(answer.Refused);
    }

    // The body touched something and backed off. What it was — a wall it knows, a peer, a thing — is the domain's to
    // say, after the peers had their window to speak: the Robot runs that protocol with the scripts below (Grazed,
    // Bumped, Met, Decided, DecidedPast, TouchedStanding) and the print of the last of them is the next order.
    [HttpPost("robot/touched")]
    public IActionResult RobotTouched([FromBody] TouchedReport report)
    {
        if (report == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + "{\"route\": 3, \"with\": \"crate_center\", \"x\": 5.2, \"y\": 5.8, \"heading\": -1.57, \"px\": 5.2, \"py\": 6.6}");
        var problems = report.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        _ = robot.TouchedAsync(report.Route.Value, report.With.Trim(), report.X.Value, report.Y.Value, report.Heading.Value, report.Px.Value, report.Py.Value);
        return Accepted();
    }

    // The body could not: stalled, timed out. The route fails in the body's words.
    [HttpPost("robot/stuck")]
    public IActionResult RobotStuck([FromBody] StuckReport report)
    {
        if (report == null) return BadRequest((ModelState.IsValid ? "a JSON body is required: " : "the JSON body could not be read; expected ") + "{\"route\": 3, \"reason\": \"stalled\"}");
        var problems = report.Problems().ToList();
        if (problems.Count > 0) return BadRequest(string.Join("; ", problems));
        if (!robot.Expects(report.Route.Value, "run") && !robot.Expects(report.Route.Value, "turn")) return Accepted("not the order the body was given");
        robot.Stuck(report.Route.Value, report.Reason.Trim());
        return Accepted();
    }

    // ==================================================================
    // The touch protocol's scripts — performed by the Robot once the peers had their say. Each returns the next order.
    // ==================================================================

    // The body touched something the map does not hold, on its way: the route is interrupted (its order becomes
    // `decide`) and the golem presumes a thing — a mark. Told to every peer with the golem's name and the pose of the
    // touch (the expose: what a reaction can capture), so a peer that bumped there and then knows it met a body.
    public static Answer Bumped(ActorV2 golemActor, int route, string me, double x, double y, double heading, double poseX, double poseY) => Answer.Of(golemActor.Using(
        @"
            Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'route is not pending';
        ",
        @"
            {
                route = g.Find(@id);
                touch = Pose(@x, @y, @heading);
                route.Bump(touch);
            }
            expose @x x, @y y, @heading heading, @me who, @px px, @py py;
        " + NextOrder)
        .WithParameters(p => {
            p["id", typeof(int)] = route; p["x", typeof(double)] = x; p["y", typeof(double)] = y; p["heading", typeof(double)] = heading;
            p["me", typeof(string)] = me; p["px", typeof(double)] = poseX; p["py", typeof(double)] = poseY;
        })
        .PerformCheckThenCommand());

    // Something touched the body while it stood: a body did it (things do not move). Told, no mark, no order changes.
    public static Answer TouchedStanding(ActorV2 golemActor, string me, double x, double y, double heading, double poseX, double poseY) => Answer.Of(golemActor.Using(
        @"
            {
                touch = Pose(@x, @y, @heading);
                g.Bump(touch);
            }
            expose @x tx, @y ty, @me twho, @px tpx, @py tpy;
        ")
        .WithParameters(p => {
            p["x", typeof(double)] = x; p["y", typeof(double)] = y; p["heading", typeof(double)] = heading;
            p["me", typeof(string)] = me; p["px", typeof(double)] = poseX; p["py", typeof(double)] = poseY;
        })
        .PerformCommand());

    // The body grazed a wall the map KNOWS: its own execution error, counted against the route's patience on the leg.
    public static Answer Grazed(ActorV2 golemActor, int route, double x, double y) => Answer.Of(golemActor.Using(
        @"
            Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'route is not pending';
        ",
        @"
            {
                route = g.Find(@id);
                at = Position(@x, @y);
                route.Graze(at);
            }
        " + NextOrder)
        .WithParameters(p => { p["id", typeof(int)] = route; p["x", typeof(double)] = x; p["y", typeof(double)] = y; })
        .PerformCheckThenCommand());

    // The golem concluded its touch was a peer: it met that body there. The mark its bump presumed comes back, here
    // and — told — in every peer that learned it. History among the obstacles, never geometry.
    public static Answer Met(ActorV2 golemActor, string who, double x, double y) => Answer.Of(golemActor.Using(
        @"
            {
                at = Position(@x, @y);
                g.Met(@who, at);
            }
            expose @x ex, @y ey;
        ")
        .WithParameters(p => { p["who", typeof(string)] = who; p["x", typeof(double)] = x; p["y", typeof(double)] = y; })
        .PerformCommand());

    // The way decided again on a route in hand — after a bump, or awake with a plan underway — from where the body
    // stands (its pose is telemetry: the only thing the host adds). The route plans it inside; the first order follows.
    public static Answer Decided(ActorV2 golemActor, int route, double x, double y) => Answer.Of(golemActor.Using(
        @"
            Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'route is not pending';
        ",
        @"
            {
                route = g.Find(@id);
                from = Position(@x, @y);
                route.Decide(from);
            }
        " + NextOrder)
        .WithParameters(p => { p["id", typeof(int)] = route; p["x", typeof(double)] = x; p["y", typeof(double)] = y; })
        .PerformCheckThenCommand());

    // The way decided out of a peer's way: the route's first leg is the courtesy step it chooses, then the road on.
    public static Answer DecidedPast(ActorV2 golemActor, int route, string who, double x, double y, double heading) => Answer.Of(golemActor.Using(
        @"
            Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'route is not pending';
        ",
        @"
            {
                route = g.Find(@id);
                me = Pose(@x, @y, @heading);
                route.DecidePast(@who, me);
            }
        " + NextOrder)
        .WithParameters(p => { p["id", typeof(int)] = route; p["who", typeof(string)] = who; p["x", typeof(double)] = x; p["y", typeof(double)] = y; p["heading", typeof(double)] = heading; })
        .PerformCheckThenCommand());

    // The world said no — a collision, a stall, no way — in the navigator's words; the route ends, the next one's order follows.
    public static Answer Failed(ActorV2 golemActor, int route, string reason) => Answer.Of(golemActor.Using(
        @"
            Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'route is not pending';
        ",
        @"
            {
                route = g.Find(@id);
                route.Fail(@reason);
            }
        " + NextOrder)
        .WithParameters(p => { p["id", typeof(int)] = route; p["reason", typeof(string)] = reason; })
        .PerformCheckThenCommand());

    // The golem lets a followed route go: a newer told point made it pointless.
    public static Answer Abandoned(ActorV2 golemActor, int route, string reason) => Answer.Of(golemActor.Using(
        @"
            Check(g.Knows(@id) && g.Find(@id).IsPending() && g.Find(@id).Following && g.HasNewerFollowing(g.Find(@id))) Error 'nothing newer was told';
        ",
        @"
            {
                route = g.Find(@id);
                route.Abandon(@reason);
            }
        " + NextOrder)
        .WithParameters(p => { p["id", typeof(int)] = route; p["reason", typeof(string)] = reason; })
        .PerformCheckThenCommand());

    // ==================================================================
    // What the peers' tells become in MY journal (the uptakes GolemSpeech binds): only plain @params — a nested call
    // as an argument faults the reaction matcher — the object built inside the block.
    // ==================================================================
    public const string UptakePointVisited = @"
        {
            point = Position(@x, @y);
            g.Follow(point);
        }
        ";
    public const string UptakeBumpedAt = @"
        {
            touch = Pose(@x, @y, @heading);
            peer = Position(@px, @py);
            g.HearBump(@who, touch, peer);
        }
        ";
    public const string UptakeTouchedAt = @"
        {
            at = Position(@x, @y);
            peer = Position(@px, @py);
            g.HearTouch(@who, at, peer);
        }
        ";
    public const string UptakeMetPeer = @"
        {
            at = Position(@x, @y);
            g.LearnMet(at);
        }
        ";
    public const string UptakeObstacleGone = @"
        {
            at = Position(@x, @y);
            g.LearnForget(at);
        }
        ";

    // ==================================================================
    // Reads (queries) the panel asks
    // ==================================================================

    // The map as it is laid out: zones, doors and open sides — read from the map module itself.
    [HttpGet("map")]
    public IActionResult Map() =>
        Content(golemActor.Using(@"
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
        Content(golemActor.Using(@"
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
        var pose = ros.LatestPose;
        if (pose == null) return StatusCode(503, "no telemetry from the body yet");
        return Content(golemActor.Using(@"
            print g.HasPendingMission() 'hasNext', g.PendingRoutes().Count 'pendingMissions',
                  body.Speed.InMetersPerSecond 'speed', body.LingerAfterTold.InSeconds 'lingerAfterTold';
            if (g.HasPendingMission()) {
                print g.Next().Id 'mission', g.Next().StopsLeft 'stopsLeft',
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
        golemActor.Using(@"
            print g.PendingRoutes().Count 'pending', g.Routes().Count 'total', g.HasPendingMission() 'hasNext';
            if (g.HasPendingMission()) {
                print g.Next().Id 'nextId',
                      g.Next().NextLeg.At.X 'nextX', g.Next().NextLeg.At.Y 'nextY',
                      g.Next().StopsLeft 'stopsLeft', g.Next().Paused 'paused';
            }
        ")
        .PerformQuery();

    // The script's answer becomes the response: refused → 409 in the domain's words; done → the print handed to the
    // Robot — the output target: it switches on it and the body gets its order — and the board back to the operator.
    private IActionResult Answered(Answer answer)
    {
        if (!answer.Ok) return Conflict(answer.Refused);
        robot.Obey(answer.Print);
        return Content(Board(), "application/json");
    }

    private static string Innermost(Exception ex)
    {
        while (ex.InnerException != null) ex = ex.InnerException;
        return ex.Message;
    }

}
