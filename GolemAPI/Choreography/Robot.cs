using System.Text.Json;
using Choreography.Theater;
using GolemAPI.Membrane;
using GolemAPI.Panel;
using Puppeteer;

namespace GolemAPI.Choreography;

// THE ROBOT, AS THE GOLEM SEES IT (Juan, 16/17-sep-2026: "el robot solo es el cuerpo; nosotros le decimos qué hacer, y
// cuando termina le dice al actor por un endpoint que ya terminó… en la clase Robot estarán los métodos-acción: el
// controller recibe y valida los parámetros y llama a robot; el método en el robot tiene los scripts, los valida una
// segunda vez, corre los scripts y hace los respectivos prints, y esos prints terminan llamando al Push de RobotToRos
// automáticamente al terminar el script, sin necesidad de que nosotros lo disparemos").
//
// EVERY SCRIPT THE GOLEM'S JOURNAL RECEIVES LIVES HERE, as an action method: the operator's (Move, Cover, Pause, Resume,
// Forget), the body's reports (Arrived, Bumped, Stuck) and what the touch protocol concludes (Grazed, Met, Decided,
// DecidedPast, Failed, LetGo). Each is one act in the golem's words — a braced block, values as @params, the object found
// or built and handed to the act, a Check that refuses in the domain's voice — and ENDS WITH THE SAME PRINT, written in
// full inside every script (Juan, 17-sep: "deja escrito el script completo, con sus prints"; NextOrder below is the same
// text, kept for the reactions' emit and the clock's query): what the route asks now. That print is PUSHED to the output target by the engine itself: RobotToRos defines one
// Reaction per act shape (Find($id), Visit(_, _), Cover(_, _), Follow(_)) that emits NextOrder when the act lands, so the
// body gets its next action without anybody dispatching it (lab-push-real.txt, 17-sep-2026). The command's returned
// print is kept only to answer the caller (a refusal, in the domain's words).
// Its other face is what the body REPORTS: the act is written, the touch protocol runs (the peers' window, Met, the way
// out of a peer's way), the follower's linger and courtesy step — plus the clock that asks the journal what it wants when
// no push brought it, and the operator's levers on the body (let go, reset). No plan, no cursor, no legs: the journal's.
public sealed class Robot
{
    private const int MaxYields = 4;             // times the golem steps out of a peer's way before giving the route up
    private static readonly TimeSpan Listen = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan Reconsider = TimeSpan.FromSeconds(12);

    private readonly PerformanceV2 performance;   // the journal's entry id and the shutdown
    private readonly ActorV2 golemActor;          // the golem itself: every script and query goes to it
    private readonly Rosbridge ros;
    private readonly PanelFeed feed;
    private readonly HttpBroker wire;
    private readonly string golem;
    private readonly (double X, double Y) home;
    private readonly string journalPath;

    private readonly object gate = new();
    private int yields, yieldsFor;      // courtesy steps taken on the route underway
    private DateTime lastStandingTouch = DateTime.MinValue;

    public Robot(PerformanceV2 performance, Rosbridge ros, PanelFeed feed, HttpBroker wire,
                 string golem, (double X, double Y) home, string journalPath)
    {
        this.performance = performance;
        this.golemActor = performance.Actor;   // the golem itself, taken from the performance once
        this.ros = ros;
        this.feed = feed;
        this.wire = wire;
        this.golem = golem;
        this.home = home;
        this.journalPath = journalPath;
        ToRos = new RobotToRos(this, ros);
    }

    /// <summary>The golem itself — the actor every script is performed on.</summary>
    public ActorV2 Actor => golemActor;
    /// <summary>Where the body believes it stands (telemetry, never the journal's); null before the first word from it.</summary>
    public Pose Pose => ros.LatestPose;
    /// <summary>The output target: the print the reactions emit, switched on and sent to the body over ROS.</summary>
    public RobotToRos ToRos { get; }

    // ==================================================================
    // What the route asks now — the print every act ends with, and what the next-order reactions emit. It ALWAYS says
    // something (`pending`), so the engine pushes it even when nothing is pending and the body must stop. `hold`: the
    // operator paused it. `decide`: it has no way, or its corrections ran out. `back` / `turn` / `run`: the next leg — its
    // point, how it is walked (approach, exit) and the heading the way gives it. Nothing pending: no route, no order.
    // ==================================================================
    public const string NextOrder = @"
        print g.HasPendingMission() 'pending';
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
    // THE OPERATOR'S ACTIONS (the controller validated the JSON; the script validates again, in the domain's voice)
    // ==================================================================

    /// <summary>Send the golem through points in THIS order (points only, never places). The first point opens the route from
    /// where the errand starts — where the body stands, or where its last pending route ends — and the route decides its
    /// whole way inside; every further point is told to it, one entry each, and it decides again through them all. The
    /// first order is pushed to the body by the reaction on the act. Returns the last answer, or the first refusal.</summary>
    public Answer Move(IReadOnlyList<(double X, double Y)> points)
    {
        var start = WhereTheErrandStarts();
        if (start == null) return Answer.Refusal("no telemetry from the body yet: the errand needs a starting point");
        var first = points[0];
        Answer answer;
        try
        {
            answer = Answer.Of(golemActor.Using(
                @"
                    Check(map.IsOnMap(Position(@x, @y))) Error 'that point is nowhere on the map';
                ",
                @"
                    {
                        from = Position(@fx, @fy);
                        point = Position(@x, @y);
                        route = g.Visit(from, point);
                        print route.Id 'route', route.Order 'order', route.IsPending() 'pending';
                        if (route.IsWalkable) {
                            print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                  route.NextLeg.At.X 'x', route.NextLeg.At.Y 'y',
                                  route.NextLeg.Approach.X 'ax', route.NextLeg.Approach.Y 'ay',
                                  route.NextLeg.Exit.X 'ex', route.NextLeg.Exit.Y 'ey',
                                  route.NextLeg.HasHeading 'hasHeading', route.NextLeg.Heading 'heading',
                                  route.Following 'following', route.StopsLeft 'stopsLeft';
                        }
                    }
                ")
                .WithParameters(p => {
                    p["fx", typeof(double)] = start.Value.X; p["fy", typeof(double)] = start.Value.Y;
                    p["x", typeof(double)] = first.X; p["y", typeof(double)] = first.Y;
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"stop ({first.X:0.##}, {first.Y:0.##}): " + Reason(ex)); }   // no way fits the body, or the domain refused inside
        if (!answer.Ok) return Answer.Refusal($"stop ({first.X:0.##}, {first.Y:0.##}): " + answer.Refused);
        return ThenEach(Newest(), points.Skip(1), answer);
    }

    /// <summary>Send the golem through several points and let it choose the order that makes the way shortest: the same
    /// errand, opened with g.Cover — the route reorders the points still ahead every time one is told to it.</summary>
    public Answer Cover(IReadOnlyList<(double X, double Y)> points)
    {
        var start = WhereTheErrandStarts();
        if (start == null) return Answer.Refusal("no telemetry from the body yet: the errand needs a starting point");
        var first = points[0];
        Answer answer;
        try
        {
            answer = Answer.Of(golemActor.Using(
                @"
                    Check(map.IsOnMap(Position(@x, @y))) Error 'that point is nowhere on the map';
                ",
                @"
                    {
                        from = Position(@fx, @fy);
                        point = Position(@x, @y);
                        route = g.Cover(from, point);
                        print route.Id 'route', route.Order 'order', route.IsPending() 'pending';
                        if (route.IsWalkable) {
                            print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                  route.NextLeg.At.X 'x', route.NextLeg.At.Y 'y',
                                  route.NextLeg.Approach.X 'ax', route.NextLeg.Approach.Y 'ay',
                                  route.NextLeg.Exit.X 'ex', route.NextLeg.Exit.Y 'ey',
                                  route.NextLeg.HasHeading 'hasHeading', route.NextLeg.Heading 'heading',
                                  route.Following 'following', route.StopsLeft 'stopsLeft';
                        }
                    }
                ")
                .WithParameters(p => {
                    p["fx", typeof(double)] = start.Value.X; p["fy", typeof(double)] = start.Value.Y;
                    p["x", typeof(double)] = first.X; p["y", typeof(double)] = first.Y;
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"stop ({first.X:0.##}, {first.Y:0.##}): " + Reason(ex)); }
        if (!answer.Ok) return Answer.Refusal($"stop ({first.X:0.##}, {first.Y:0.##}): " + answer.Refused);
        return ThenEach(Newest(), points.Skip(1), answer);
    }

    // Every further point is told to the route just opened, one entry each; the route decides its way again through them all.
    private Answer ThenEach(int id, IEnumerable<(double X, double Y)> points, Answer answer)
    {
        foreach (var point in points)
        {
            try
            {
                answer = Answer.Of(golemActor.Using(
                    @"
                        Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'the route is no longer pending';
                        Check(map.IsOnMap(Position(@x, @y))) Error 'that point is nowhere on the map';
                    ",
                    @"
                        {
                            route = g.Find(@id);
                            point = Position(@x, @y);
                            route.Then(point);
                            print route.Id 'route', route.Order 'order', route.IsPending() 'pending';
                            if (route.IsWalkable) {
                                print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                      route.NextLeg.At.X 'x', route.NextLeg.At.Y 'y',
                                      route.NextLeg.Approach.X 'ax', route.NextLeg.Approach.Y 'ay',
                                      route.NextLeg.Exit.X 'ex', route.NextLeg.Exit.Y 'ey',
                                      route.NextLeg.HasHeading 'hasHeading', route.NextLeg.Heading 'heading',
                                      route.Following 'following', route.StopsLeft 'stopsLeft';
                            }
                        }
                    ")
                    .WithParameters(p => { p["id", typeof(int)] = id; p["x", typeof(double)] = point.X; p["y", typeof(double)] = point.Y; })
                    .PerformCheckThenCommand());
            }
            catch (Exception ex) { return Answer.Refusal($"stop ({point.X:0.##}, {point.Y:0.##}): " + Reason(ex)); }
            if (!answer.Ok) return Answer.Refusal($"stop ({point.X:0.##}, {point.Y:0.##}): " + answer.Refused);
        }
        return answer;
    }

    /// <summary>The operator holds the route underway: the body stops where it stands; plan and cursor keep.</summary>
    public Answer Pause()
    {
        int? id = RouteUnderway();
        if (id == null) return Answer.Refusal("nothing underway: no pending route");
        return Answer.Of(golemActor.Using(
            @"
                Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'the route is no longer pending';
                Check(!g.Find(@id).Paused) Error 'the route is already paused';
            ",
            @"
                {
                    route = g.Find(@id);
                    route.Pause();
                    print route.Id 'route', route.Order 'order', route.IsPending() 'pending';
                    if (route.IsWalkable) {
                        print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                              route.NextLeg.At.X 'x', route.NextLeg.At.Y 'y',
                              route.NextLeg.Approach.X 'ax', route.NextLeg.Approach.Y 'ay',
                              route.NextLeg.Exit.X 'ex', route.NextLeg.Exit.Y 'ey',
                              route.NextLeg.HasHeading 'hasHeading', route.NextLeg.Heading 'heading',
                              route.Following 'following', route.StopsLeft 'stopsLeft';
                    }
                }
            ")
            .WithParameters(p => { p["id", typeof(int)] = id.Value; })
            .PerformCheckThenCommand());
    }

    /// <summary>The operator lets the route go on: the body takes up the thing it was doing, from where it stands.</summary>
    public Answer Resume()
    {
        int? id = RouteUnderway();
        if (id == null) return Answer.Refusal("nothing underway: no pending route");
        return Answer.Of(golemActor.Using(
            @"
                Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'the route is no longer pending';
                Check(g.Find(@id).Paused) Error 'the route is not paused';
            ",
            @"
                {
                    route = g.Find(@id);
                    route.Resume();
                    print route.Id 'route', route.Order 'order', route.IsPending() 'pending';
                    if (route.IsWalkable) {
                        print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                              route.NextLeg.At.X 'x', route.NextLeg.At.Y 'y',
                              route.NextLeg.Approach.X 'ax', route.NextLeg.Approach.Y 'ay',
                              route.NextLeg.Exit.X 'ex', route.NextLeg.Exit.Y 'ey',
                              route.NextLeg.HasHeading 'hasHeading', route.NextLeg.Heading 'heading',
                              route.Following 'following', route.StopsLeft 'stopsLeft';
                    }
                }
            ")
            .WithParameters(p => { p["id", typeof(int)] = id.Value; })
            .PerformCheckThenCommand());
    }

    /// <summary>Somebody took it away: the golem forgets the obstacle standing there, with every mark that outlined it. The
    /// reaction tells the peers (the point rides beside the act as an expose: a reaction captures no object), who forget it
    /// too. No order changes: the way stands.</summary>
    public Answer Forget(double x, double y) => Answer.Of(golemActor.Using(
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
        .WithParameters(p => { p["x", typeof(double)] = x; p["y", typeof(double)] = y; })
        .PerformCheckThenCommand());

    // ==================================================================
    // THE BODY'S REPORTS (sim/bridge/body.py posts them; the controller validated the JSON). A report about an order the
    // body was not given (superseded meanwhile) is acknowledged and not journaled.
    // ==================================================================

    /// <summary>The body did the one thing it was told: a turn made (route.Turn) or a point reached (route.Reach). A stop
    /// reached is counted (the last one completes the route) and told to the follower — the point rides beside the act as
    /// an expose: the reaction that tells captures no object; a door, an opening, a point to pass just moves the route past
    /// it. A courtesy step (route 0) is the golem's own business: nothing to journal. A follower arriving at its last stop
    /// pulls over to its right and lingers, so the leader keeps its lead. Null: not the order the body was given.</summary>
    public Answer? Arrived(int route)
    {
        var was = ToRos.Carrying;
        if (was == null || was.Route != route) return null;
        ToRos.Done();
        if (was.Route == 0) { Note("courtesy step done"); return default(Answer); }
        Answer answer;
        if (was.What == "turn")
            answer = Answer.Of(golemActor.Using(
                @"
                    Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'route is not pending';
                ",
                @"
                    {
                        route = g.Find(@id);
                        route.Turn();
                        print route.Id 'route', route.Order 'order', route.IsPending() 'pending';
                        if (route.IsWalkable) {
                            print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                  route.NextLeg.At.X 'x', route.NextLeg.At.Y 'y',
                                  route.NextLeg.Approach.X 'ax', route.NextLeg.Approach.Y 'ay',
                                  route.NextLeg.Exit.X 'ex', route.NextLeg.Exit.Y 'ey',
                                  route.NextLeg.HasHeading 'hasHeading', route.NextLeg.Heading 'heading',
                                  route.Following 'following', route.StopsLeft 'stopsLeft';
                        }
                    }
                ")
                .WithParameters(p => { p["id", typeof(int)] = was.Route; })
                .PerformCheckThenCommand());
        else if (was.Kind == "stop")
            answer = Answer.Of(golemActor.Using(
                @"
                    Check(g.Knows(@id) && g.Find(@id).IsPending() && g.Find(@id).IsStopAhead(Position(@x, @y))) Error 'that is not a stop ahead';
                ",
                @"
                    {
                        route = g.Find(@id);
                        point = Position(@x, @y);
                        route.Reach(point);
                        print route.Id 'route', route.Order 'order', route.IsPending() 'pending';
                        if (route.IsWalkable) {
                            print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                  route.NextLeg.At.X 'x', route.NextLeg.At.Y 'y',
                                  route.NextLeg.Approach.X 'ax', route.NextLeg.Approach.Y 'ay',
                                  route.NextLeg.Exit.X 'ex', route.NextLeg.Exit.Y 'ey',
                                  route.NextLeg.HasHeading 'hasHeading', route.NextLeg.Heading 'heading',
                                  route.Following 'following', route.StopsLeft 'stopsLeft';
                        }
                    }
                    expose @id rid, @x rx, @y ry;
                ")
                .WithParameters(p => { p["id", typeof(int)] = was.Route; p["x", typeof(double)] = was.X; p["y", typeof(double)] = was.Y; })
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
                        print route.Id 'route', route.Order 'order', route.IsPending() 'pending';
                        if (route.IsWalkable) {
                            print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                  route.NextLeg.At.X 'x', route.NextLeg.At.Y 'y',
                                  route.NextLeg.Approach.X 'ax', route.NextLeg.Approach.Y 'ay',
                                  route.NextLeg.Exit.X 'ex', route.NextLeg.Exit.Y 'ey',
                                  route.NextLeg.HasHeading 'hasHeading', route.NextLeg.Heading 'heading',
                                  route.Following 'following', route.StopsLeft 'stopsLeft';
                        }
                    }
                ")
                .WithParameters(p => { p["id", typeof(int)] = was.Route; p["x", typeof(double)] = was.X; p["y", typeof(double)] = was.Y; })
                .PerformCheckThenCommand());
        Report(answer, was.What == "turn" ? $"route {was.Route} turned to heading {was.Heading:0.00}"
            : was.What == "back" ? $"route {was.Route} backed off to ({was.X:0.0}, {was.Y:0.0})"
            : $"route {was.Route} {(was.Kind == "stop" ? "reached the stop" : "passed the point")} ({was.X:0.0}, {was.Y:0.0})");
        if (answer.Ok && was.IsLastStop)
        {
            ReportLocalization(was.Route);
            if (was.Following)
            {
                var linger = TimeSpan.FromSeconds(LingerAfterTold());
                ToRos.Linger(linger);
                Note($"lingering {linger.TotalSeconds:0} s at ({was.X:0.0}, {was.Y:0.0}) to keep the leader's lead");
                if (ToRos.Carrying == null) StepAside("pulling over to the right, off the leader's way");
            }
        }
        return answer;
    }

    /// <summary>The body could not: stalled, timed out. The route fails in the body's words; the next route's order follows.
    /// Null: not the order the body was given.</summary>
    public Answer? Stuck(int route, string reason)
    {
        var was = ToRos.Carrying;
        if (was == null || was.Route != route) return null;
        ToRos.Done();
        var answer = Safely(() => Failed(route, reason));
        Report(answer, $"route {route} failed: {reason}");
        return answer;
    }

    /// <summary>The body bumped into something — on its way (route > 0) or standing (route 0) — and its motors stopped at
    /// once. The DOMAIN says what it suspects (a wall it knows, a peer that spoke, a thing) and what follows: the route
    /// corrects its way inside (back off, then the road around) and its print — 'back' — is pushed to the body AT ONCE;
    /// the peers get their window meanwhile, and if one of them was there the conclusion is Met and the route decides its
    /// way out of its way (Juan, 8-sep: "the domain decides, the host follows"; 16-sep: "el que decide todo debe ser el dominio").</summary>
    public async Task BumpedAsync(int route, string with, double x, double y, double heading, double poseX, double poseY, double poseTheta)
    {
        var was = ToRos.Carrying;
        int since = ToRos.HeardBefore;
        bool onMyWay = route > 0 && was != null && was.Route == route;
        if (!onMyWay)
        {
            // standing (idle, held, lingering) or on a courtesy step: a body did it — told, no mark — and room is made
            if (DateTime.UtcNow - lastStandingTouch < TimeSpan.FromSeconds(1)) return;
            lastStandingTouch = DateTime.UtcNow;
            Note($"touched while standing at ({x:0.0}, {y:0.0}) — telling the peers");
            var told = Safely(() => TouchedStanding(x, y, heading, poseX, poseY));
            if (!told.Ok) Console.WriteLine($"[golem {golem}] refused: {told.Refused}");
            StepAside("making room");
            return;
        }
        ToRos.Done();
        var hit = new Collision(with, x, y, heading);
        string where = $"({x:0.0}, {y:0.0})";
        if (Suspect(hit, since).Kind == "wall")
        {
            // A wall I know: my own execution error. The route backs off and tries the same legs again while the golem's
            // patience lasts; spent, the route ends. Neither is the host's call.
            Note($"route {route}: grazed {with} at {where}, a wall I know — telling the golem");
            var grazed = Safely(() => Grazed(route, x, y, poseX, poseY, poseTheta));
            if (!grazed.Ok) { Report(grazed, ""); return; }
            if (MayRetryLeg(route)) { Report(grazed, $"route {route} grazed a wall it knows at {where}: backing off to try again"); return; }
            Report(Safely(() => Failed(route, $"still grazing {with} at {where} after {Grazes(route)} grazes: patience spent")), $"route {route} failed: patience spent");
            return;
        }

        // Something the map does not hold: the bump is journaled and told, the route corrected inside, and the body
        // told to back off at once (the reaction pushes the print). Then the peers' window: was it a body?
        Note($"route {route}: bumped into {with} at {where} heading {heading:0.00} — nothing on my map there; the route corrects its way; telling the peers and listening");
        var bumped = Safely(() => Bumped(route, x, y, heading, poseX, poseY, poseTheta));
        if (!bumped.Ok) { Report(bumped, ""); return; }
        Report(bumped, $"route {route} bumped into something at {where}: the way corrected, backing off first");
        var until = DateTime.UtcNow + Listen;
        var suspicion = Suspect(hit, since);
        while (suspicion.Kind != "peer" && DateTime.UtcNow < until)
        {
            await Task.Delay(250);
            suspicion = Suspect(hit, since);
        }
        if (suspicion.Kind == "peer")
        {
            Note($"route {route}: the domain suspects {suspicion.Who} — it bumped there too: a body, not a thing ({suspicion.Conclusion})");
            var met = Safely(() => Met(suspicion.Who, x, y));
            if (!met.Ok) Console.WriteLine($"[golem {golem}] refused: {met.Refused}");
            int taken;
            lock (gate) { if (route != yieldsFor) { yields = 0; yieldsFor = route; } taken = yields; }
            if (taken < MaxYields)
            {
                lock (gate) yields++;
                var mine = ros.LatestPose;
                if (mine == null) { Decide(route, "met a peer, no pose"); return; }
                Note($"route {route}: met {suspicion.Who} at {where} — the route decides its way out of its way");
                try { Report(DecidedPast(route, suspicion.Who, mine.X, mine.Y, mine.Theta), $"route {route} decided its way past {suspicion.Who}"); }
                catch (Exception ex) { Report(Safely(() => Failed(route, "no road: " + Reason(ex))), $"route {route} failed: no road"); }
                return;
            }
            Report(Safely(() => Failed(route, $"blocked by {suspicion.Who} at {where} after meeting it {MaxYields} times")), $"route {route} failed: blocked");
            return;
        }
        Note($"route {route}: nobody else bumped there and then — the mark the bump presumed at {where} stands; reconsidering for {Reconsider.TotalSeconds:0}s");
        _ = ReconsiderAsync(route, hit, since);
    }

    // A peer may speak after the window: its own row goes through its journal, its reaction and the wire before it
    // reaches mine. The host keeps asking the domain for a while; if it then suspects a peer, the conclusion is the
    // same Met — the mark comes back, here and in every peer that learned it (10-sep lab: the ghost mark).
    private async Task ReconsiderAsync(int route, Collision hit, int since)
    {
        try
        {
            var until = DateTime.UtcNow + Reconsider;
            while (DateTime.UtcNow < until)
            {
                await Task.Delay(500);
                var suspicion = Suspect(hit, since);
                if (suspicion.Kind != "peer") continue;
                Note($"route {route}: {suspicion.Who} spoke after the window — it was there too: the mark the bump presumed at ({hit.X:0.0}, {hit.Y:0.0}) is taken back");
                var met = Safely(() => Met(suspicion.Who, hit.X, hit.Y));
                if (!met.Ok) Console.WriteLine($"[golem {golem}] refused: {met.Refused}");
                return;
            }
        }
        catch (Exception e) { Console.WriteLine($"[golem {golem}] reconsidering a mark failed: {e.Message}"); }
    }

    // ==================================================================
    // THE TOUCH PROTOCOL'S SCRIPTS and the domain's decisions the Robot asks for on the body's behalf. Each ends in
    // NextOrder; the reaction on g.Find(@id) pushes it.
    // ==================================================================

    // The body bumped into something the map does not hold, on its way: the golem presumes a thing — a mark — and the
    // route corrects its way inside (back off, then the road around; its print is 'back'). Told to every peer with the
    // golem's name and the pose of the touch (the expose: what a reaction can capture), so a peer that bumped there and
    // then knows it met a body.
    private Answer Bumped(int route, double x, double y, double heading, double poseX, double poseY, double poseTheta) => Answer.Of(golemActor.Using(
        @"
            Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'route is not pending';
        ",
        @"
            {
                route = g.Find(@id);
                touch = Pose(@x, @y, @heading);
                me = Pose(@px, @py, @ptheta);
                route.Bump(touch, me);
                print route.Id 'route', route.Order 'order', route.IsPending() 'pending';
                if (route.IsWalkable) {
                    print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                          route.NextLeg.At.X 'x', route.NextLeg.At.Y 'y',
                          route.NextLeg.Approach.X 'ax', route.NextLeg.Approach.Y 'ay',
                          route.NextLeg.Exit.X 'ex', route.NextLeg.Exit.Y 'ey',
                          route.NextLeg.HasHeading 'hasHeading', route.NextLeg.Heading 'heading',
                          route.Following 'following', route.StopsLeft 'stopsLeft';
                }
            }
            expose @x x, @y y, @heading heading, @name who, @px px, @py py;
        ")
        .WithParameters(p => {
            p["id", typeof(int)] = route; p["x", typeof(double)] = x; p["y", typeof(double)] = y; p["heading", typeof(double)] = heading;
            p["name", typeof(string)] = golem; p["px", typeof(double)] = poseX; p["py", typeof(double)] = poseY; p["ptheta", typeof(double)] = poseTheta;
        })
        .PerformCheckThenCommand());

    // Something touched the body while it stood: a body did it (things do not move). Told, no mark, no order changes.
    private Answer TouchedStanding(double x, double y, double heading, double poseX, double poseY) => Answer.Of(golemActor.Using(
        @"
            {
                touch = Pose(@x, @y, @heading);
                g.Bump(touch);
            }
            expose @x tx, @y ty, @me twho, @px tpx, @py tpy;
        ")
        .WithParameters(p => {
            p["x", typeof(double)] = x; p["y", typeof(double)] = y; p["heading", typeof(double)] = heading;
            p["me", typeof(string)] = golem; p["px", typeof(double)] = poseX; p["py", typeof(double)] = poseY;
        })
        .PerformCommand());

    // The body grazed a wall the map KNOWS: its own execution error, counted against the route's patience on the leg; the
    // route backs off first and tries the same legs again (its print is 'back').
    private Answer Grazed(int route, double x, double y, double poseX, double poseY, double poseTheta) => Answer.Of(golemActor.Using(
        @"
            Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'route is not pending';
        ",
        @"
            {
                route = g.Find(@id);
                at = Position(@x, @y);
                me = Pose(@px, @py, @ptheta);
                route.Graze(at, me);
                print route.Id 'route', route.Order 'order', route.IsPending() 'pending';
                if (route.IsWalkable) {
                    print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                          route.NextLeg.At.X 'x', route.NextLeg.At.Y 'y',
                          route.NextLeg.Approach.X 'ax', route.NextLeg.Approach.Y 'ay',
                          route.NextLeg.Exit.X 'ex', route.NextLeg.Exit.Y 'ey',
                          route.NextLeg.HasHeading 'hasHeading', route.NextLeg.Heading 'heading',
                          route.Following 'following', route.StopsLeft 'stopsLeft';
                }
            }
        ")
        .WithParameters(p => { p["id", typeof(int)] = route; p["x", typeof(double)] = x; p["y", typeof(double)] = y; p["px", typeof(double)] = poseX; p["py", typeof(double)] = poseY; p["ptheta", typeof(double)] = poseTheta; })
        .PerformCheckThenCommand());

    // The golem concluded its touch was a peer: it met that body there. The mark its bump presumed comes back, here
    // and — told — in every peer that learned it. History among the obstacles, never geometry.
    private Answer Met(string who, double x, double y) => Answer.Of(golemActor.Using(
        @"
            {
                at = Position(@x, @y);
                g.Met(@who, at);
            }
            expose @x ex, @y ey;
        ")
        .WithParameters(p => { p["who", typeof(string)] = who; p["x", typeof(double)] = x; p["y", typeof(double)] = y; })
        .PerformCommand());

    // The way decided again on a route in hand — awake with a plan underway, or stranded after a bump with no road from
    // the retreat — from where the body stands (its pose is telemetry: the only thing the host adds).
    private Answer Decided(int route, double x, double y) => Answer.Of(golemActor.Using(
        @"
            Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'route is not pending';
        ",
        @"
            {
                route = g.Find(@id);
                from = Position(@x, @y);
                route.Decide(from);
                print route.Id 'route', route.Order 'order', route.IsPending() 'pending';
                if (route.IsWalkable) {
                    print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                          route.NextLeg.At.X 'x', route.NextLeg.At.Y 'y',
                          route.NextLeg.Approach.X 'ax', route.NextLeg.Approach.Y 'ay',
                          route.NextLeg.Exit.X 'ex', route.NextLeg.Exit.Y 'ey',
                          route.NextLeg.HasHeading 'hasHeading', route.NextLeg.Heading 'heading',
                          route.Following 'following', route.StopsLeft 'stopsLeft';
                }
            }
        ")
        .WithParameters(p => { p["id", typeof(int)] = route; p["x", typeof(double)] = x; p["y", typeof(double)] = y; })
        .PerformCheckThenCommand());

    // The way decided out of a peer's way: the route's first leg is the courtesy step it chooses, then the road on.
    private Answer DecidedPast(int route, string who, double x, double y, double heading) => Answer.Of(golemActor.Using(
        @"
            Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'route is not pending';
        ",
        @"
            {
                route = g.Find(@id);
                me = Pose(@x, @y, @heading);
                route.DecidePast(@who, me);
                print route.Id 'route', route.Order 'order', route.IsPending() 'pending';
                if (route.IsWalkable) {
                    print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                          route.NextLeg.At.X 'x', route.NextLeg.At.Y 'y',
                          route.NextLeg.Approach.X 'ax', route.NextLeg.Approach.Y 'ay',
                          route.NextLeg.Exit.X 'ex', route.NextLeg.Exit.Y 'ey',
                          route.NextLeg.HasHeading 'hasHeading', route.NextLeg.Heading 'heading',
                          route.Following 'following', route.StopsLeft 'stopsLeft';
                }
            }
        ")
        .WithParameters(p => { p["id", typeof(int)] = route; p["who", typeof(string)] = who; p["x", typeof(double)] = x; p["y", typeof(double)] = y; p["heading", typeof(double)] = heading; })
        .PerformCheckThenCommand());

    // The world said no — a collision, a stall, no way — in the body's words; the route ends, the next one's order follows.
    private Answer Failed(int route, string reason) => Answer.Of(golemActor.Using(
        @"
            Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'route is not pending';
        ",
        @"
            {
                route = g.Find(@id);
                route.Fail(@reason);
                print route.Id 'route', route.Order 'order', route.IsPending() 'pending';
                if (route.IsWalkable) {
                    print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                          route.NextLeg.At.X 'x', route.NextLeg.At.Y 'y',
                          route.NextLeg.Approach.X 'ax', route.NextLeg.Approach.Y 'ay',
                          route.NextLeg.Exit.X 'ex', route.NextLeg.Exit.Y 'ey',
                          route.NextLeg.HasHeading 'hasHeading', route.NextLeg.Heading 'heading',
                          route.Following 'following', route.StopsLeft 'stopsLeft';
                }
            }
        ")
        .WithParameters(p => { p["id", typeof(int)] = route; p["reason", typeof(string)] = reason; })
        .PerformCheckThenCommand());

    // The operator lets go of everything: every pending route abandoned with the reason, in ONE command. No reaction
    // matches a foreach: the body is stopped by the lever itself (LetGoAsync).
    private Answer LetGo(string reason) => Answer.Of(golemActor.Using(@"
            foreach (route in g.PendingRoutes()) {
                route.Abandon(@reason);
            }
            print g.HasPendingMission() 'pending';
            if (g.HasPendingMission()) { print g.Next().Id 'route', g.Next().Order 'order'; }
            if (g.HasPendingMission() && g.Next().IsWalkable) {
                print g.Next().NextLeg.Kind 'kind', g.Next().NextLeg.Name 'name',
                      g.Next().NextLeg.At.X 'x', g.Next().NextLeg.At.Y 'y',
                      g.Next().NextLeg.Approach.X 'ax', g.Next().NextLeg.Approach.Y 'ay',
                      g.Next().NextLeg.Exit.X 'ex', g.Next().NextLeg.Exit.Y 'ey',
                      g.Next().NextLeg.HasHeading 'hasHeading', g.Next().NextLeg.Heading 'heading',
                      g.Next().Following 'following', g.Next().StopsLeft 'stopsLeft';
            }
        ")
        .WithParameters(p => { p["reason", typeof(string)] = reason; })
        .PerformCommand());

    // The way, decided by the route itself from where the body stands. When no way fits the body, the route fails with
    // the planner's reason. Asked by the output target when a print says 'decide'.
    internal void Decide(int route, string verb)
    {
        var here = ros.LatestPose;
        if (here == null) { Note($"route {route}: no pose yet to decide from — asking again shortly"); return; }
        Note($"route {route}: {verb} — deciding the way from ({here.X:0.0}, {here.Y:0.0})");
        try { Report(Decided(route, here.X, here.Y), $"route {route} decided its way from ({here.X:0.0}, {here.Y:0.0})"); }
        catch (Exception ex) { Report(Safely(() => Failed(route, "no road: " + Reason(ex))), $"route {route} failed: no road"); }
    }

    // The courtesy step is the golem's to choose (g.Aside); the body only walks it.
    private void StepAside(string why)
    {
        var pose = ros.LatestPose;
        if (pose == null) return;
        var (x, y) = Aside(pose);
        if (Math.Abs(x - pose.X) < 1e-6 && Math.Abs(y - pose.Y) < 1e-6) return;
        ToRos.StepAside(x, y, why);
    }

    // What a script answered, for the record: refused → said so (the domain's words). The next order is NOT dispatched
    // here: the reaction on the act pushes it to the output target.
    private void Report(Answer answer, string done)
    {
        if (!answer.Ok)
        {
            Console.WriteLine($"[golem {golem}] refused: {answer.Refused}");
            feed.Broadcast(new PanelEvent(performance.CurrentEntryId, "runtime", "", $"refused — {answer.Refused}", DateTime.UtcNow));
            return;
        }
        if (done != "") Console.WriteLine($"[golem {golem}] {done} (entry {performance.CurrentEntryId})");
    }

    // ==================================================================
    // The clock: awake, and now and then — the journal is asked what it wants when no push brought it (a push is
    // ephemeral: the engine promises no delivery), and the answer is obeyed only when it differs from what the body carries.
    // ==================================================================
    public async Task RunAsync(CancellationToken ct)
    {
        // Awake with a way underway: it was decided from wherever the body stood then, and the body may be anywhere
        // now — decide it again from here before anything else (once the body has said where it is).
        var patience = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (ros.LatestPose == null && DateTime.UtcNow < patience && !ct.IsCancellationRequested) await Task.Delay(200, ct);
        var first = AskOrder();
        if (first != null && (first.What == "turn" || first.What == "run" || first.What == "back")) Decide(first.Route, "awake with a plan underway");
        else ToRos.Obey(first);
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct);
            var now = AskOrder();
            var mine = ToRos.Carrying;
            if (now == null) { if (mine != null && mine.Route != 0) ToRos.Stop(); continue; }
            if (mine != null && mine.SameAs(now)) continue;
            if (mine != null && mine.Route == 0 && now.What != "hold") continue;   // a courtesy step underway: the order waits for it
            ToRos.Obey(now);
        }
    }

    // ==================================================================
    // The operator's levers on the body.
    // ==================================================================

    // Let go of everything: the body stopped and put back on its mark (ephemeral, a lab lever); the letting go itself
    // is the golem's act, one command for every pending route.
    public async Task LetGoAsync()
    {
        ToRos.Stop(anchor: true);
        try { await ros.TeleportAsync(home.X, home.Y, 0.0, CancellationToken.None); }
        catch { /* the world reset is best-effort; the journaled fact is the point */ }
        Report(Safely(() => LetGo("the operator let go of everything")), "let go of every pending route");
        Note("letting go of every pending route");
    }

    /// <summary>Reborn: the body is put back on its mark and told it stands there (dead reckoning re-anchors).</summary>
    public async Task PutBackHomeAsync(CancellationToken ct)
    {
        await ros.TeleportAsync(home.X, home.Y, 0.0, ct);
        ToRos.Stop(anchor: true);
    }

    // The hard reset — a LAB lever, not a domain fact: stop the body, wipe THIS golem's journal and exit; Docker
    // restarts the container reborn at entry 1. With cascade, every peer does the same (the hearer dedups the
    // sender's once-ids, so one side alone would swallow the next tells as repeats).
    public async Task ResetEverythingAsync(bool cascade)
    {
        Note($"reset everything — wiping the journal, the golem reboots reborn{(cascade ? ", peers too" : "")}");
        if (cascade)
            foreach (var peer in wire.Peers)
                await wire.AskPeerAsync(peer, "reset-everything", "{\"cascade\": false}");
        ToRos.Stop();
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            try { performance.Dispose(); } catch { /* the loop may be mid-perform; we are leaving anyway */ }
            string mine = Path.Combine(journalPath, golem);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { if (Directory.Exists(mine)) Directory.Delete(mine, recursive: true); break; }
                catch (IOException) { await Task.Delay(200); }
            }
            Console.WriteLine($"[golem {golem}] journal wiped at {mine} — exiting for a reborn boot");
            Environment.Exit(0);
        });
    }

    // The localization experiment's readout: on dead reckoning, say — at every route's end, in the runtime lane only —
    // where the body believes it stands and where the world says it stands.
    private void ReportLocalization(int route)
    {
        if (ros.Source != PoseSource.Wheels) return;
        var believed = ros.LatestPose;
        var truth = ros.LatestTruth;
        if (believed == null || truth == null) return;
        double off = Math.Sqrt((believed.X - truth.X) * (believed.X - truth.X) + (believed.Y - truth.Y) * (believed.Y - truth.Y));
        Note($"route {route}: the body believes it stands at ({believed.X:0.00}, {believed.Y:0.00}); the world says ({truth.X:0.00}, {truth.Y:0.00}) — {off:0.00} m apart");
    }

    internal void Note(string text)
    {
        Console.WriteLine($"[golem {golem}] {text}");
        feed.Broadcast(new PanelEvent(performance.CurrentEntryId, "runtime", "", text, DateTime.UtcNow));
    }

    // A script that faults (the domain refused inside the command, not in its Check) answers as a refusal, in its words.
    private static Answer Safely(Func<Answer> perform)
    {
        try { return perform(); }
        catch (Exception ex) { return Answer.Refusal(Reason(ex)); }
    }

    private static string Reason(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;
        return inner.Message;
    }

    // ------------------------------------------------------------------
    // Reads: the golem's answers, from the print of a query (JSON) — never from a shared lease: the panel's polls and the
    // body's reports reach the actor on different threads at once, and a rented Out parameter was found empty under that
    // concurrency ("Unknown parameter v", 16-sep-2026 lab).
    // ------------------------------------------------------------------

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
    private int Newest() => Read("print g.Newest().Id 'v';").GetInt32();

    // The route the golem is on, if any.
    private int? RouteUnderway()
    {
        using var doc = JsonDocument.Parse(golemActor.Using(@"
            print g.HasPendingMission() 'busy';
            if (g.HasPendingMission()) { print g.Next().Id 'id'; }
        ").PerformQuery());
        return doc.RootElement.TryGetProperty("id", out var id) ? id.GetInt32() : null;
    }

    // What the route asks now — the same question every script ends with.
    private Order AskOrder()
    {
        try { return Order.Parse(golemActor.Using(NextOrder).PerformQuery()); }
        catch (Exception e) { Console.WriteLine($"[golem {golem}] asking the order failed: {e.Message}"); return null; }
    }

    private (string Kind, string Who, string Conclusion) Suspect(Collision hit, int since)
    {
        using var doc = JsonDocument.Parse(golemActor.Using(@"
            print g.Suspect(Pose(@x, @y, @heading), @since).Kind 'kind',
                  g.Suspect(Pose(@x, @y, @heading), @since).Who 'who',
                  g.Suspect(Pose(@x, @y, @heading), @since).Conclusion 'verb';
        ")
        .WithParameters(p => { p["x", typeof(double)] = hit.X; p["y", typeof(double)] = hit.Y; p["heading", typeof(double)] = hit.Heading; p["since", typeof(int)] = since; })
        .PerformQuery());
        var e = doc.RootElement;
        return (e.GetProperty("kind").GetString() ?? "", e.GetProperty("who").GetString() ?? "", e.GetProperty("verb").GetString() ?? "");
    }

    private (double X, double Y) Aside(Pose pose)
    {
        using var doc = JsonDocument.Parse(golemActor.Using(@"
            print g.Aside(Pose(@x, @y, @theta)).X 'sx', g.Aside(Pose(@x, @y, @theta)).Y 'sy';
        ")
        .WithParameters(p => { p["x", typeof(double)] = pose.X; p["y", typeof(double)] = pose.Y; p["theta", typeof(double)] = pose.Theta; })
        .PerformQuery());
        return (doc.RootElement.GetProperty("sx").GetDouble(), doc.RootElement.GetProperty("sy").GetDouble());
    }

    /// <summary>The body the golem declared in its journal: what the robot needs to carry an order out.</summary>
    internal (double Speed, double Radius, double Retreat) BodyDeclared()
    {
        using var doc = JsonDocument.Parse(golemActor.Using(@"
            print body.Speed.InMetersPerSecond 'speed', body.Radius.InMeters 'radius', body.Retreat.InMeters 'retreat';
        ").PerformQuery());
        var e = doc.RootElement;
        return (e.GetProperty("speed").GetDouble(), e.GetProperty("radius").GetDouble(), e.GetProperty("retreat").GetDouble());
    }

    internal int HeardBumpCount()        => Read("print collisions.HeardCount 'v';").GetInt32();
    private bool MayRetryLeg(int id)     => Read("print g.Find(@id).MayRetryLeg 'v';", id).GetBoolean();
    private int Grazes(int id)           => Read("print g.Find(@id).Grazes 'v';", id).GetInt32();
    private double LingerAfterTold()     => Read("print body.LingerAfterTold.InSeconds 'v';").GetDouble();

    private JsonElement Read(string script, int? id = null)
    {
        using var doc = JsonDocument.Parse(golemActor.Using(script)
            .WithParameters(p => { if (id.HasValue) p["id", typeof(int)] = id.Value; })
            .PerformQuery());
        return doc.RootElement.GetProperty("v").Clone();
    }
}

/// <summary>What the body touched, where on the plane, heading into it — as the body reported it.</summary>
public sealed record Collision(string With, double X, double Y, double Heading);
