using GolemAPI.Membrane;
using Puppeteer;

namespace GolemAPI.Choreography.Roles;

// THE DISPLACER — the role of the MOTORS: the body displaces itself through the world (Juan, 17-sep-2026: "Displacer";
// 18-sep: "las capacidades del robot: los motores"). A ROLE is the encapsulation unit of the acts ONE capability of the body
// writes in the golem's journal (puppeteer-reactions: "an actor name is a role = the encapsulation unit"); the golem keeps one
// journal, so a role is the unit INSIDE it. This one owns everything whose subject is the body's DISPLACEMENT — the errand
// and the way it decides, the hold and the letting go on, the turn and the move the body reports, the stall. It RECEIVES the
// robot (the golem with its body: the actor every act is performed on, the pose, the mechanics, the log) and builds nothing.
// The embodiment plays it only when the body declared it (Capabilities.Displacer, `ROLES` in the compose); the controller
// asks for the role and refuses the endpoint when the body has none.
//
// EVERY ACTION IS ONE ACT IN THE GOLEM'S WORDS (Juan, 18-sep-2026): the validated parameters, one script — a Check that
// refuses in the domain's voice, a braced block whose values enter as @params and whose objects are found or built inside,
// the print of what the route asks now, asked of the route the act was on — performed on the golem's actor, its answer
// returned to the caller. The print RETURNS to whoever performed the action; what reaches the body is pushed by the reaction
// the act fires (RobotMechanics.DefineReactions), so no action here dispatches anything.
public sealed class Displacer
{
    private readonly GolemEmbodiment robot;   // the golem with its body: every act is written to its actor

    public Displacer(GolemEmbodiment robot)
    {
        if (robot == null) throw new ArgumentNullException(nameof(robot), "a displacer needs the robot whose body it displaces");
        this.robot = robot;
    }

    // ==================================================================
    // THE OPERATOR'S ERRANDS (the controller validated the JSON; the script validates again, in the domain's voice)
    // ==================================================================

    /// <summary>Send the golem through points in THIS order (points only, never places). The first point opens the route from
    /// where the errand starts — where the body stands, or where its last pending route ends — and the route decides its
    /// whole way inside; every further point is told to it, one entry each, and it decides again through them all. The
    /// first order is pushed to the body by the reaction on the act. Returns the last answer, or the first refusal.</summary>
    public Answer Move(IReadOnlyList<(double X, double Y)> points)
    {
        var start = robot.WhereTheErrandStarts();
        if (start == null) return Answer.Refusal("no telemetry from the body yet: the errand needs a starting point");
        var first = points[0];
        Answer answer;
        try
        {
            answer = Answer.Of(robot.Actor.Using(
                @"
                    Check(map.IsOnMap(Position(@x, @y))) Error 'that point is nowhere on the map';
                ",
                @"
                    {
                        from = Pose(@fx, @fy, @ftheta);
                        point = Position(@x, @y);
                        route = g.Visit(from, point);
                        print route.Id 'route', route.Order 'action', route.Amount 'amount', route.IsPending() 'pending';
                        if (route.IsWalkable) {
                            print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                  route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                  route.Following 'following', route.StopsLeft 'stopsLeft';
                        }
                    }
                ")
                .WithParameters(p => {
                    p["fx", typeof(double)] = start.Value.X;
                    p["fy", typeof(double)] = start.Value.Y;
                    p["ftheta", typeof(double)] = start.Value.Theta;
                    p["x", typeof(double)] = first.X;
                    p["y", typeof(double)] = first.Y;
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"stop ({first.X:0.##}, {first.Y:0.##}): " + GolemEmbodiment.Reason(ex)); }   // no way fits the body, or the domain refused inside
        if (!answer.Ok) return Answer.Refusal($"stop ({first.X:0.##}, {first.Y:0.##}): " + answer.Refused);
        return ThenEach(robot.Newest(), points.Skip(1), answer);
    }

    /// <summary>Send the golem through several points and let it choose the order that makes the way shortest: the same
    /// errand, opened with g.Cover — the route reorders the points still ahead every time one is told to it.</summary>
    public Answer Cover(IReadOnlyList<(double X, double Y)> points)
    {
        var start = robot.WhereTheErrandStarts();
        if (start == null) return Answer.Refusal("no telemetry from the body yet: the errand needs a starting point");
        var first = points[0];
        Answer answer;
        try
        {
            answer = Answer.Of(robot.Actor.Using(
                @"
                    Check(map.IsOnMap(Position(@x, @y))) Error 'that point is nowhere on the map';
                ",
                @"
                    {
                        from = Pose(@fx, @fy, @ftheta);
                        point = Position(@x, @y);
                        route = g.Cover(from, point);
                        print route.Id 'route', route.Order 'action', route.Amount 'amount', route.IsPending() 'pending';
                        if (route.IsWalkable) {
                            print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                  route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                  route.Following 'following', route.StopsLeft 'stopsLeft';
                        }
                    }
                ")
                .WithParameters(p => {
                    p["fx", typeof(double)] = start.Value.X;
                    p["fy", typeof(double)] = start.Value.Y;
                    p["ftheta", typeof(double)] = start.Value.Theta;
                    p["x", typeof(double)] = first.X;
                    p["y", typeof(double)] = first.Y;
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"stop ({first.X:0.##}, {first.Y:0.##}): " + GolemEmbodiment.Reason(ex)); }
        if (!answer.Ok) return Answer.Refusal($"stop ({first.X:0.##}, {first.Y:0.##}): " + answer.Refused);
        return ThenEach(robot.Newest(), points.Skip(1), answer);
    }

    // Every further point is told to the route just opened, one entry each; the route decides its way again through them all.
    private Answer ThenEach(int id, IEnumerable<(double X, double Y)> points, Answer answer)
    {
        foreach (var point in points)
        {
            try
            {
                answer = Answer.Of(robot.Actor.Using(
                    @"
                        Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'the route is no longer pending';
                        Check(map.IsOnMap(Position(@x, @y))) Error 'that point is nowhere on the map';
                    ",
                    @"
                        {
                            route = g.Find(@id);
                            point = Position(@x, @y);
                            route.Then(point);
                            print route.Id 'route', route.Order 'action', route.Amount 'amount', route.IsPending() 'pending';
                            if (route.IsWalkable) {
                                print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                      route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                      route.Following 'following', route.StopsLeft 'stopsLeft';
                            }
                        }
                    ")
                    .WithParameters(p => {
                        p["id", typeof(int)] = id;
                        p["x", typeof(double)] = point.X;
                        p["y", typeof(double)] = point.Y;
                    })
                    .PerformCheckThenCommand());
            }
            catch (Exception ex) { return Answer.Refusal($"stop ({point.X:0.##}, {point.Y:0.##}): " + GolemEmbodiment.Reason(ex)); }
            if (!answer.Ok) return Answer.Refusal($"stop ({point.X:0.##}, {point.Y:0.##}): " + answer.Refused);
        }
        return answer;
    }

    // ==================================================================
    // THE HOLD (Juan, 17-sep: "pausamos el cerebro, no la ruta"): the golem is held where its body stands, and let go on.
    // ==================================================================

    /// <summary>The operator holds the GOLEM where its body stands: the pose is kept, the route underway is held with it and
    /// handed back; the body stops. Plan and cursor keep.</summary>
    public Answer Pause()
    {
        var pose = robot.Pose;
        if (pose == null) return Answer.Refusal("no telemetry from the body yet: the hold needs where it stands");
        return Answer.Of(robot.Actor.Using(
            @"
                Check(g.HasPendingMission()) Error 'nothing underway: no pending route';
                Check(!g.Held) Error 'the golem is already paused';
            ",
            @"
                {
                    me = Pose(@x, @y, @theta);
                    route = g.Pause(me);
                    print route.Id 'route', route.Order 'action', route.Amount 'amount', route.IsPending() 'pending',
                          g.HeldAt.X 'heldX', g.HeldAt.Y 'heldY';
                }
            ")
            .WithParameters(p => {
                p["x", typeof(double)] = pose.X;
                p["y", typeof(double)] = pose.Y;
                p["theta", typeof(double)] = pose.Theta;
            })
            .PerformCheckThenCommand());
    }

    /// <summary>The operator lets the golem go on: the route underway takes up its next leg from where the body stands now
    /// (it may have been pushed while standing) — the route gives that leg its heading again from there.</summary>
    public Answer Resume()
    {
        var pose = robot.Pose;
        if (pose == null) return Answer.Refusal("no telemetry from the body yet: the resume needs where it stands");
        return Answer.Of(robot.Actor.Using(
            @"
                Check(g.Held) Error 'the golem is not paused';
                Check(g.HasPendingMission()) Error 'nothing underway: no pending route';
            ",
            @"
                {
                    me = Pose(@x, @y, @theta);
                    route = g.Resume(me);
                    print route.Id 'route', route.Order 'action', route.Amount 'amount', route.IsPending() 'pending';
                    if (route.IsWalkable) {
                        print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                              route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                              route.Following 'following', route.StopsLeft 'stopsLeft';
                    }
                }
            ")
            .WithParameters(p => {
                p["x", typeof(double)] = pose.X;
                p["y", typeof(double)] = pose.Y;
                p["theta", typeof(double)] = pose.Theta;
            })
            .PerformCheckThenCommand());
    }

    // ==================================================================
    // WHAT THE MOTORS REPORT (sim/bridge/body.py posts them; the controller validated the JSON). Each is ONE act with the pose
    // the body reports; the domain concludes inside. A report about an order the body was not given (superseded meanwhile) is
    // acknowledged and not journaled.
    // ==================================================================

    /// <summary>The body did the one thing it was told and reports it: `route.Arrive(me)` on the ROUTE UNDERWAY — the golem
    /// knows which it is; no id enters the act (Juan, 18-sep-2026; lab-underway.txt: a reaction on `[_:Golem].Underway()` pushes
    /// the print) — with where the body stands now. The EXPOSE carries only what the act does not say (ajuste 42): the route's
    /// id and whether the order done was a stop, so the reaction that tells the follower fires on a stop alone (the literal
    /// `true`) and announces for the right route; the pose it tells is captured from the `Pose(@…)` beside the act. The `route`
    /// the body echoes is the host's token to tell a stale report from the order carried. A follower on its last stop lingers
    /// before anything else, so the leader keeps its lead: the clock is the host's. Null: not the order the body was given.</summary>
    public Answer? Arrived(int route)
    {
        var was = robot.Mechanics.Carrying;
        if (was == null || was.Route != route) return null;
        robot.Mechanics.Done();
        var here = robot.Pose ?? new Pose(was.X, was.Y, was.Heading);
        bool stop = was.IsMove && was.Kind == "stop";
        Answer answer;
        try
        {
            answer = Answer.Of(robot.Actor.Using(
                @"
                    Check(g.HasPendingMission()) Error 'nothing underway: no pending route';
                ",
                @"
                    {
                        route = g.Underway();
                        me = Pose(@px, @py, @ptheta);
                        route.Arrive(me);
                        print route.Id 'route', route.Order 'action', route.Amount 'amount', route.IsPending() 'pending';
                        if (route.IsWalkable) {
                            print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                  route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                  route.Following 'following', route.StopsLeft 'stopsLeft';
                        }
                    }
                    expose @id rid, @stop reached;
                ")
                .WithParameters(p => {
                    p["id", typeof(int)] = was.Route;
                    p["stop", typeof(bool)] = stop;
                    p["px", typeof(double)] = here.X;
                    p["py", typeof(double)] = here.Y;
                    p["ptheta", typeof(double)] = here.Theta;
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal(GolemEmbodiment.Reason(ex)); }
        robot.Report(answer, $"route {was.Route}: {was.Describe()} done, standing at ({here.X:0.0}, {here.Y:0.0}) facing {here.Theta:0.00}");
        if (!answer.Ok || !stop) return answer;
        robot.ReportLocalization(was.Route);
        if (was.Following && was.IsLastStop)
        {
            var linger = TimeSpan.FromSeconds(robot.LingerAfterTold());
            robot.Mechanics.Linger(linger);
            robot.Note($"lingering {linger.TotalSeconds:0} s at ({was.X:0.0}, {was.Y:0.0}) to keep the leader's lead");
        }
        return answer;
    }

    /// <summary>The body could not: stalled, timed out. The route fails in the body's words; the next route's order follows.
    /// Null: not the order the body was given.</summary>
    public Answer? Stuck(int route, string reason)
    {
        var was = robot.Mechanics.Carrying;
        if (was == null || was.Route != route) return null;
        robot.Mechanics.Done();
        var answer = Failed(reason);
        robot.Report(answer, $"route {route} failed: {reason}");
        return answer;
    }

    // The world said no — a collision, a stall, no way — in the body's words; the route ends, the next one's order follows.
    private Answer Failed(string reason) => Answer.Of(robot.Actor.Using(
        @"
            Check(g.HasPendingMission()) Error 'nothing underway: no pending route';
        ",
        @"
            {
                route = g.Underway();
                route.Fail(@reason);
                print route.Id 'route', route.Order 'action', route.Amount 'amount', route.IsPending() 'pending';
                if (route.IsWalkable) {
                    print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                          route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                          route.Following 'following', route.StopsLeft 'stopsLeft';
                }
            }
        ")
        .WithParameters(p => {
            p["reason", typeof(string)] = reason;
        })
        .PerformCheckThenCommand());
}
