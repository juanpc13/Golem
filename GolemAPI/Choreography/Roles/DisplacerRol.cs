using System.Text.Json;
using GolemAPI.Membrane;
using Puppeteer;

using GolemAPI.Commanding;

namespace GolemAPI.Choreography.Roles;

// THE DISPLACER — the role of the MOTORS: the body displaces itself through the world (Juan, 17-sep-2026: "Displacer";
// 18-sep: "las capacidades del robot: los motores"). A ROLE is the encapsulation unit of the acts ONE capability of the body
// writes in the golem's journal (puppeteer-reactions: "an actor name is a role = the encapsulation unit"); the golem keeps one
// journal, so a role is the unit INSIDE it. This one owns everything whose subject is the body's DISPLACEMENT — the errand
// and the way it decides, the hold and the letting go on, the turn and the move the body reports, the stall — and, since 16-oct-2026 (Juan: "la combinación del golemActor.Using + el script debe
// ser parte del rol"), the body's pace and the strategy of navigation; the formations are the ChoreographerRol's and the play the DynamicsRol's,
// roles of the mind's modules this one asks at an arrival. It RECEIVES the
// robot (the golem with its body: the actor every act is performed on, the pose, the mechanics, the log) and builds nothing.
// The embodiment plays it only when the body declared it (Capabilities.Displacer, `ROLES` in the compose); the controller
// asks for the role and refuses the endpoint when the body has none.
//
// EVERY ACTION IS ONE ACT IN THE GOLEM'S WORDS (Juan, 18-sep-2026): the validated parameters, one script — a Check that
// refuses in the domain's voice, a braced block whose values enter as @params and whose objects are found or built inside,
// the print of what the route asks now, asked of the route the act was on — performed on the golem's actor, its answer
// returned to the caller. The print RETURNS to whoever performed the action; what reaches the body is pushed by the reaction
// the act fires (RobotMechanics.DefineReactions), so no action here dispatches anything.
public sealed class DisplacerRol
{
    private readonly GolemEmbodiment robot;   // the golem with its body: every act is written to its actor

    public DisplacerRol(GolemEmbodiment robot)
    {
        if (robot == null) throw new ArgumentNullException(nameof(robot), "a displacer needs the robot whose body it displaces");
        this.robot = robot;
    }

    // ==================================================================
    // THE OPERATOR'S ERRANDS (the controller validated the JSON; the script validates again, in the domain's voice)
    // ==================================================================

    /// <summary>Send the golem through points in THIS order (points only, never places). The first point opens the route from
    /// where the errand starts — the domain's own answer, `g.Destination` (ajuste 67): where the body stands, or where its last
    /// pending route ends — and the route decides its
    /// whole way inside; every further point is told to it, one entry each, and it decides again through them all. The
    /// first order is pushed to the body by the reaction on the act. Returns the last answer, or the first refusal.</summary>
    public Answer Move(IReadOnlyList<(double X, double Y)> points)
    {
        var first = points[0];
        Answer answer;
        try
        {
            answer = Answer.Of(robot.Actor.Using(
                @"
                    Check(g.KnowsWhereItStands) Error 'the golem does not know yet where its body stands: it wakes on its mark first';
                    Check(g.Current.Map.IsOnMap(Position(@sx, @sy))) Error 'that point is nowhere on the map';
                ",
                @"
                    {
                        from = g.Destination;
                        point = Position(@sx, @sy);
                        route = g.Visit(from, point);
                        if (g.Strategy.OnTheWay.IsActive) {
                            route = g.Dash(route);
                        }
                        if (route.IsPending()) {
                            print route.Id 'route', route.Order 'action', route.Waiting 'waiting';
                            if (route.IsWalkable) {
                                print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                      route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                      route.Following 'following', route.StopsLeft 'stopsLeft';
                            }
                        } else {
                            print route.Id 'route', route.Status 'ended';
                            if (route.EndedShort) {
                                print route.Why 'why';
                            }
                        }
                    }
                ")
                .WithParameters(p => {
                    p["sx", typeof(double)] = Resolution.Metres(first.X);
                    p["sy", typeof(double)] = Resolution.Metres(first.Y);
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
        var first = points[0];
        Answer answer;
        try
        {
            answer = Answer.Of(robot.Actor.Using(
                @"
                    Check(g.KnowsWhereItStands) Error 'the golem does not know yet where its body stands: it wakes on its mark first';
                    Check(g.Current.Map.IsOnMap(Position(@sx, @sy))) Error 'that point is nowhere on the map';
                ",
                @"
                    {
                        from = g.Destination;
                        point = Position(@sx, @sy);
                        route = g.Cover(from, point);
                        if (g.Strategy.OnTheWay.IsActive) {
                            route = g.Dash(route);
                        }
                        if (route.IsPending()) {
                            print route.Id 'route', route.Order 'action', route.Waiting 'waiting';
                            if (route.IsWalkable) {
                                print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                      route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                      route.Following 'following', route.StopsLeft 'stopsLeft';
                            }
                        } else {
                            print route.Id 'route', route.Status 'ended';
                            if (route.EndedShort) {
                                print route.Why 'why';
                            }
                        }
                    }
                ")
                .WithParameters(p => {
                    p["sx", typeof(double)] = Resolution.Metres(first.X);
                    p["sy", typeof(double)] = Resolution.Metres(first.Y);
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"stop ({first.X:0.##}, {first.Y:0.##}): " + GolemEmbodiment.Reason(ex)); }
        if (!answer.Ok) return Answer.Refusal($"stop ({first.X:0.##}, {first.Y:0.##}): " + answer.Refused);
        return ThenEach(robot.Newest(), points.Skip(1), answer);
    }

    /// <summary>One more stop, told to the NEWEST route while it is pending — the console's `then`, the panel's next point on a route
    /// underway (propuesta 58) — the same entry Move writes for every point after the first; the route decides its way again through
    /// them all. Refused when there is no route, or the newest is no longer pending.</summary>
    public Answer Then((double X, double Y) point)
    {
        int id;
        try { id = robot.Newest(); }
        catch (Exception ex) { return Answer.Refusal("no route to tell a stop to: " + GolemEmbodiment.Reason(ex)); }
        return ThenEach(id, new[] { point }, Answer.Refusal("no route to tell a stop to"));
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
                        Check(g.Current.Map.IsOnMap(Position(@sx, @sy))) Error 'that point is nowhere on the map';
                    ",
                    @"
                        {
                            route = g.Find(@id);
                            point = Position(@sx, @sy);
                            route.Then(point);
                            if (g.Strategy.OnTheWay.IsActive) {
                                route = g.Dash(route);
                            }
                            if (route.IsPending()) {
                                print route.Id 'route', route.Order 'action', route.Waiting 'waiting';
                                if (route.IsWalkable) {
                                    print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                          route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                          route.Following 'following', route.StopsLeft 'stopsLeft';
                                }
                            } else {
                                print route.Id 'route', route.Status 'ended';
                                if (route.EndedShort) {
                                    print route.Why 'why';
                                }
                            }
                        }
                    ")
                    .WithParameters(p => {
                        p["id", typeof(int)] = id;
                        p["sx", typeof(double)] = Resolution.Metres(point.X);
                        p["sy", typeof(double)] = Resolution.Metres(point.Y);
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
                    me = Pose(@px, @py, @ptheta);
                    route = g.Pause(me);
                    if (route.IsPending()) {
                        print route.Id 'route', route.Order 'action', route.Waiting 'waiting', g.HeldAt.X 'heldX', g.HeldAt.Y 'heldY';
                    } else {
                        print route.Id 'route', route.Status 'ended';
                        if (route.EndedShort) {
                            print route.Why 'why';
                        }
                    }
                }
            ")
            .WithParameters(p => {
                p["px", typeof(double)] = Resolution.Metres(pose.X);
                p["py", typeof(double)] = Resolution.Metres(pose.Y);
                p["ptheta", typeof(double)] = Resolution.Radians(pose.Theta);
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
                    me = Pose(@px, @py, @ptheta);
                    route = g.Resume(me);
                    if (route.IsPending()) {
                        print route.Id 'route', route.Order 'action', route.Waiting 'waiting';
                        if (route.IsWalkable) {
                            print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                  route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                  route.Following 'following', route.StopsLeft 'stopsLeft';
                        }
                    } else {
                        print route.Id 'route', route.Status 'ended';
                        if (route.EndedShort) {
                            print route.Why 'why';
                        }
                    }
                }
            ")
            .WithParameters(p => {
                p["px", typeof(double)] = Resolution.Metres(pose.X);
                p["py", typeof(double)] = Resolution.Metres(pose.Y);
                p["ptheta", typeof(double)] = Resolution.Radians(pose.Theta);
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
    /// the body echoes is the host's TICKET on the order (ajuste 55: it knows nothing of routes), to tell a stale report from the order carried. A follower on its last stop lingers
    /// before anything else, so the leader keeps its lead: the clock is the host's. Null: not the order the body was given.</summary>
    public Answer? Arrived(int order)
    {
        var was = robot.Mechanics.Carrying;
        if (was == null || was.Ticket != order) return null;   // the ticket the body echoes: a word about another order is stale (ajuste 55)
        robot.Mechanics.Done();
        if (was.IsHalt) return robot.Choreographer is { } choreographer ? choreographer.Halted(was) : Answer.Refusal("the body stopped for a route that yields its place, but this body plays no choreographer");   // (propuesta 74)
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
                        leg = route.NextLeg;
                        route.Arrive(leg);
                        if (g.Strategy.OnTheWay.IsActive) {
                            route = g.Dash(route);
                        }
                        if (route.IsPending()) {
                            print route.Id 'route', route.Order 'action', route.Waiting 'waiting';
                            if (route.IsWalkable) {
                                print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                      route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                      route.Following 'following', route.StopsLeft 'stopsLeft';
                            }
                        } else {
                            print route.Id 'route', route.Status 'ended';
                            if (route.EndedShort) {
                                print route.Why 'why';
                            }
                        }
                    }
                    expose @id rid, @stop reached, @sx x, @sy y;
                ")
                .WithParameters(p => {
                    p["id", typeof(int)] = was.Route;
                    p["stop", typeof(bool)] = stop;
                    p["sx", typeof(double)] = Resolution.Metres(was.X);   // the point the order the body did headed to — told to a follower when it was a stop
                    p["sy", typeof(double)] = Resolution.Metres(was.Y);
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal(GolemEmbodiment.Reason(ex)); }
        robot.Report(answer, $"route {was.Route}: {was.Describe()} done, standing at ({here.X:0.0}, {here.Y:0.0}) facing {here.Theta:0.00}");
        if (answer.Ok && answer.Order is { IsEnded: true, Ended: "completed" })
        {
            // the route completed: the roles of the mind's modules say what follows (16-oct-2026: each role its own scripts) — the choreographer, if it
            // was the route to this golem's place in a formation (ajuste 77); the player, if a run or a chase of the play — each its own act, so the
            // arrival keeps its one expose (a script carries one expose: a second one unbinds the first's parameters, lab 1-oct)
            var placed = robot.Choreographer?.Placed();
            if (placed is { Ok: true } placedNow) { robot.Report(placedNow, $"route {was.Route}: on its place in the formation — the fleet is told"); return placedNow; }
            // …or the run of a play (propuesta 108): pacman stands at a decision cell and the rule opens its next run
            var ran = robot.Dynamics?.Ran(here);   // from where the body really stands (ajuste 119)
            if (ran is { Ok: true } ranNow) { robot.Report(ranNow, $"route {was.Route}: at a decision cell — the next run opens"); return ranNow; }
            // …or a ghost's chase (fase 3): it arrived where pacman was seen, and pacman was seen somewhere new since
            var chased = robot.Dynamics?.Chased(here);
            if (chased is { Ok: true } chasedNow) { robot.Report(chasedNow, $"route {was.Route}: where pacman was seen — the chase goes on"); return chasedNow; }
            // …or a probe of the exploration (propuesta 120): the step into the unknown done, or backed off a touch — the next probe opens
            var probed = robot.Explorer?.Probed(here);
            if (probed is { Ok: true } probedNow) { robot.Report(probedNow, $"route {was.Route}: a probe ended — the next one opens"); return probedNow; }
        }
        if (answer.Ok && answer.Order is { Action: "stop", Waiting: true })
        {
            // the turn lined the body up for a step of its formation and the route waits for the fleet's start (propuesta 106): the golem says it is
            // lined up — its own act, told to every peer; with the last mover's word every copy lets its route go
            var aligned = robot.Choreographer?.Aligned();
            if (aligned is { Ok: true } alignedNow) { robot.Report(alignedNow, $"route {was.Route}: lined up for the step — the fleet is told"); return alignedNow; }
            // …or a car behind lined up whose predecessor is at its place already (propuesta 125 §12): it sets out now
            var setOut = robot.Dynamics?.SetOutIfAhead();
            if (setOut is { Ok: true } setOutNow) { robot.Report(setOutNow, $"route {was.Route}: the one ahead is at its place — setting out"); return setOutNow; }
            // …or a car at its place (propuesta 125): the route waits at its gate and the car says so — the light counts the line's word
            var stopped = robot.Dynamics?.Stopped(here);
            if (stopped is { Ok: true } stoppedNow) { robot.Report(stoppedNow, $"route {was.Route}: at the stop line — the light is told"); return stoppedNow; }
        }
        // a car past the crossing (propuesta 125): its exit point reached with the green — its word frees the crossing; the drive goes on
        if (answer.Ok && robot.Dynamics?.Cleared(here) is { Ok: true } clearedNow) robot.Report(clearedNow, $"route {was.Route}: past the crossing — the light is told");
        if (robot.Choreographer?.Retaken(answer, was) is { } retaken) return retaken;   // the route ended short on this arrival (a correction with no road): the retake (propuesta 113)
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

    // THE STRATEGIES OF NAVIGATION (ajuste 63): the golem's own objects, one fixed template per strategy — a property, never a name,
    // never a constructor (Juan: "if (g.Strategy.OnTheWay.IsActive), así déjalo, no como string")
    // ==================================================================
    // THE PACE AND THE STRATEGY OF NAVIGATION — the motors' own (16-oct-2026, from the embodiment: every script in its role)
    // ==================================================================

    /// <summary>The golem SWITCHES ON one of its strategies of navigation (ajuste 63; Juan: "más bien es como un toggle y esos objetos
    /// ya existen dentro del golem"): `on-the-way` (the doors crossed on the way, fewer stops) or `door-by-door` (every door a leg of
    /// its own, the direct way). The strategies live in the golem from birth — `g.Strategy.OnTheWay`, `g.Strategy.DoorByDoor` — and the
    /// operator's word picks the template that activates one; nothing is built, nothing found by a name. Every route is born direct and
    /// improved by the script while on-the-way is active (ajuste 62). Named `optimize` since ajuste 82 (2-oct-2026; Juan: "más bien es como
    /// una estrategia de conversión para optimizar la route calculada"): the word says what happens to the route computed.</summary>
    public Answer Optimize(string navigation)
    {
        string which = (navigation ?? "").Trim().ToLowerInvariant();
        if (which is not ("on-the-way" or "door-by-door")) return Answer.Refusal($"a strategy of navigation is on-the-way or door-by-door, not '{navigation}'");
        try
        {
            if (which == "on-the-way")
                return Answer.Of(robot.Actor.Using(@"
                    {
                        strategy = g.Strategy.OnTheWay;
                        strategy.Activate();
                        print strategy.Name 'navigation', strategy.IsActive 'active';
                    }
                ").PerformCommand());
            return Answer.Of(robot.Actor.Using(@"
                    {
                        strategy = g.Strategy.DoorByDoor;
                        strategy.Activate();
                        print strategy.Name 'navigation', strategy.IsActive 'active';
                    }
                ").PerformCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"optimize {which}: " + GolemEmbodiment.Reason(ex)); }
    }

    // THE PACE (propuesta 111, 12-oct-2026): the body's cruise, and how fast it turns, told again — the body's own act on the journal's
    // `body` (the module the golem drives; the golem wraps nothing), its magnitudes built from the parameters, never named like a print label
    /// <summary>The body's pace from here on: its cruise in m/s and, when given, its turn rate in rad/s (propuesta 111). Every order after
    /// this carries them; a route underway keeps its way and goes on at the new pace from its next order.</summary>
    public Answer Pace(double cruise, double? turning)
    {
        if (double.IsNaN(cruise) || double.IsInfinity(cruise) || cruise <= 0) return Answer.Refusal($"a cruise speed is metres per second above zero, not '{cruise}'");
        if (turning is double t && (double.IsNaN(t) || double.IsInfinity(t) || t <= 0)) return Answer.Refusal($"a turn rate is radians per second above zero, not '{turning}'");
        try
        {
            if (turning == null)
                return Answer.Of(robot.Actor.Using(@"
                    {
                        pace = MetersPerSecond(@cruise);
                        body.CruiseAt(pace);
                        print body.Speed.InMetersPerSecond 'speed', body.TurnRate.InRadiansPerSecond 'turn';
                    }
                ")
                .WithParameters(p => { p["cruise", typeof(double)] = cruise; })
                .PerformCommand());
            return Answer.Of(robot.Actor.Using(@"
                    {
                        pace = MetersPerSecond(@cruise);
                        rate = RadiansPerSecond(@turning);
                        body.CruiseAt(pace);
                        body.TurnAt(rate);
                        print body.Speed.InMetersPerSecond 'speed', body.TurnRate.InRadiansPerSecond 'turn';
                    }
                ")
                .WithParameters(p => { p["cruise", typeof(double)] = cruise; p["turning", typeof(double)] = turning.Value; })
                .PerformCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"speed {cruise}: " + GolemEmbodiment.Reason(ex)); }
    }

    // ==================================================================
    // THE FOLLOWER'S UPTAKE (GolemSpeech binds it): a peer told the stop it reached — this golem follows it there, a route of its own
    // ==================================================================
    public const string UptakePointVisited = @"
        {
            point = Position(@x, @y);
            route = g.Follow(point);
            if (g.Strategy.OnTheWay.IsActive) {
                route = g.Dash(route);
            }
        }
        ";

    /// <summary>The body could not: stalled, timed out. The route fails in the body's words; the next route's order follows.
    /// Null: not the order the body was given.</summary>
    public Answer? Stuck(int order, string reason)
    {
        var was = robot.Mechanics.Carrying;
        if (was == null || was.Ticket != order) return null;
        robot.Mechanics.Done();
        // a STALL is written with where the body stands (propuesta 120): on a probe the domain takes it as a touch — the mark learned, the body
        // back where the step began — on any other route it fails it; without telemetry, or stuck otherwise (a turn timeout), the route fails
        var answer = reason.StartsWith("stalled", StringComparison.OrdinalIgnoreCase) && robot.Pose is { } here ? Stalled(here, reason) : Failed(reason);
        robot.Report(answer, answer.Ok && answer.Order is { IsEnded: false } ? $"route {was.Route}: stalled — a touch where it stands, the body backs off" : $"route {was.Route} failed: {reason}");
        if (robot.Choreographer?.Retaken(answer, was) is { } retaken) return retaken;   // the body could not: the formation takes the place again (propuesta 113)
        return answer;
    }

    // The body stalled where it stands: the golem decides what it was — a touch on a probe, a failure on any other route (propuesta 120)
    private Answer Stalled(Pose here, string reason) => Answer.Of(robot.Actor.Using(
        @"
            Check(g.HasPendingMission()) Error 'nothing underway: no pending route';
        ",
        @"
            {
                me = Pose(@px, @py, @ptheta);
                route = g.Stall(me, @reason);
                if (route.IsPending()) {
                    print route.Id 'route', route.Order 'action', route.Waiting 'waiting';
                    if (route.IsWalkable) {
                        print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                              route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                              route.Following 'following', route.StopsLeft 'stopsLeft';
                    }
                } else {
                    print route.Id 'route', route.Status 'ended';
                    if (route.EndedShort) {
                        print route.Why 'why';
                    }
                }
            }
        ")
        .WithParameters(p => {
            p["px", typeof(double)] = Resolution.Metres(here.X);
            p["py", typeof(double)] = Resolution.Metres(here.Y);
            p["ptheta", typeof(double)] = Resolution.Radians(here.Theta);
            p["reason", typeof(string)] = reason;
        })
        .PerformCheckThenCommand());

    // The world said no — a collision, a stall, no way — in the body's words; the route ends, the next one's order follows.
    private Answer Failed(string reason) => Answer.Of(robot.Actor.Using(
        @"
            Check(g.HasPendingMission()) Error 'nothing underway: no pending route';
        ",
        @"
            {
                route = g.Underway();
                route.Fail(@reason);
                if (route.IsPending()) {
                    print route.Id 'route', route.Order 'action', route.Waiting 'waiting';
                    if (route.IsWalkable) {
                        print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                              route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                              route.Following 'following', route.StopsLeft 'stopsLeft';
                    }
                } else {
                    print route.Id 'route', route.Status 'ended';
                    if (route.EndedShort) {
                        print route.Why 'why';
                    }
                }
            }
        ")
        .WithParameters(p => {
            p["reason", typeof(string)] = reason;
        })
        .PerformCheckThenCommand());
}
