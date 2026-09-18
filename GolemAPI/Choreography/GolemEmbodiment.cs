using System.Text.Json;
using Choreography.Theater;
using GolemAPI.Membrane;
using GolemAPI.Panel;
using Puppeteer;

namespace GolemAPI.Choreography;

// THE GOLEM'S EMBODIMENT: the golem given a body — the whole robot as the host sees it, mind and flesh together
// (Juan, 17-sep-2026: "algo más relacionado al robot en general, el embody o el conjunto del robot"; the class was
// `Robot` from 16-sep to 17-sep). The mind is the actor (every script and query goes to it); the flesh is the body's
// pose and what the body reports; between them, RobotMechanics — the output target, how an order becomes movement.
// Juan, 16/17-sep: "el robot solo es el cuerpo; nosotros le decimos qué hacer, y cuando termina le dice al actor por un
// endpoint que ya terminó… ahí estarán los métodos-acción: el controller recibe y valida los parámetros y llama; el
// método tiene los scripts, los valida una segunda vez, corre los scripts y hace los respectivos prints, y esos prints
// terminan llamando al Push automáticamente al terminar el script, sin necesidad de que nosotros lo disparemos".
//
// EVERY SCRIPT THE GOLEM'S JOURNAL RECEIVES LIVES HERE, as an action method: the operator's (Move, Cover, Pause, Resume,
// Forget), the body's reports (Arrived, Bumped, Stuck) and what the touch protocol concludes (Grazed, Met, Decided,
// DecidedPast, Failed, LetGo). Each is one act in the golem's words — a braced block, values as @params, the object found
// or built and handed to the act, a Check that refuses in the domain's voice — and ENDS WITH THE SAME PRINT, written in
// full inside every script (Juan, 17-sep: "deja escrito el script completo, con sus prints"; NextOrder below is the same
// text, kept for the reactions' emit and the clock's query): what the route asks now. That print is PUSHED to the output
// target by the engine itself: RobotMechanics defines one Reaction per act shape (Find($id), Visit(_, _), Cover(_, _),
// Follow(_), Pause(_), Resume(_)) that emits NextOrder when the act lands, so the body gets its next action without
// anybody dispatching it (lab-push-real.txt, 17-sep-2026). The command's returned print is kept only to answer the caller
// (a refusal, in the domain's words).
// Its other face is what the body REPORTS — arrived, bumped, stuck — each ONE act too (17-sep-2026, Juan: "no siguen el
// patrón de script… manejan lógica del dominio y concurrencia, cosa que no debe ser"): the host adds the pose and relays
// the order's own words, and the DOMAIN concludes inside what the report meant (which act the arrival was, what the touch
// was, what follows). No windows, no waits, no counters here: a peer's word that lands later is concluded where it lands.
// What stays the host's is the clock (asking the journal when no push brought it, the follower's linger), the wire, and
// the operator's levers on the body (let go, reset). No plan, no cursor, no legs: the journal's.
//
// EVERY ACTION HERE IS THE SAME SHAPE (Juan, 18-sep-2026: "el método debería llamar a un script de golemActor.Using, registrar
// el acto en la global g y hacer un print con el nuevo lugar al que debe ir; ese print termina en RobotMechanics porque es el
// OutputTarget: Push > Dispatch > switch > ros > robot"): the parameters validated by the controller, ONE script — a Check in
// the domain's voice, a braced block that finds or builds the objects and performs the act on `g`, the print of what the
// route asks now — performed on golemActor.Using(…), its answer returned to the caller (a refusal, a 409). No helper
// wraps the call, no lambda: what the script needs is right there.
public sealed class GolemEmbodiment
{
    private readonly PerformanceV2 performance;   // the journal's entry id and the shutdown
    private readonly ActorV2 golemActor;          // the golem itself: every script and query goes to it
    private readonly Rosbridge ros;
    private readonly PanelFeed feed;
    private readonly HttpBroker wire;
    private readonly string golem;
    private readonly (double X, double Y) home;
    private readonly string journalPath;

    public GolemEmbodiment(PerformanceV2 performance, Rosbridge ros, PanelFeed feed, HttpBroker wire,
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
        Mechanics = new RobotMechanics(this, ros);
    }

    /// <summary>The golem itself — the actor every script is performed on.</summary>
    public ActorV2 Actor => golemActor;
    /// <summary>Where the body believes it stands (telemetry, never the journal's); null before the first word from it.</summary>
    public Pose Pose => ros.LatestPose;
    /// <summary>The robot's mechanics — the output target: the print the reactions emit, switched on and sent to the body over ROS.</summary>
    public RobotMechanics Mechanics { get; }

    // ==================================================================
    // What the route asks now, IN THE ROBOT'S WORDS — the print every act ends with, and what the next-order reactions
    // emit. It ALWAYS says something (`pending`), so the engine pushes it even when nothing is pending and the body must
    // stop. `action`: advance | back | turnLeft | turnRight | stop, or decide (the way must be decided again); `amount`:
    // the metres or radians (Juan, 17-sep-2026: "qué tanto debe moverse hacia adelante, qué tanto debe rotar"); then the
    // point the body heads to, for the panel and the log. Nothing pending: no route, no order.
    // ==================================================================
    public const string NextOrder = @"
        print g.HasPendingMission() 'pending', g.Held 'held';
        if (g.HasPendingMission()) { print g.Underway().Id 'route', g.Underway().Order 'action', g.Underway().Amount 'amount'; }
        if (g.HasPendingMission() && g.Underway().IsWalkable) {
            print g.Underway().NextLeg.Kind 'kind', g.Underway().NextLeg.Name 'name',
                  g.Underway().Target.X 'x', g.Underway().Target.Y 'y', g.Underway().Target.Heading 'heading',
                  g.Underway().Following 'following', g.Underway().StopsLeft 'stopsLeft';
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
            peer = Pose(@bodyX, @bodyY, @bodyHeading);
            g.HearBump(@who, peer, @bearing);
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
            catch (Exception ex) { return Answer.Refusal($"stop ({point.X:0.##}, {point.Y:0.##}): " + Reason(ex)); }
            if (!answer.Ok) return Answer.Refusal($"stop ({point.X:0.##}, {point.Y:0.##}): " + answer.Refused);
        }
        return answer;
    }

    /// <summary>The operator holds the GOLEM where its body stands (Juan, 17-sep: "pausamos el cerebro, no la ruta"): the
    /// pose is kept, the route underway is held with it and handed back; the body stops. Plan and cursor keep.</summary>
    public Answer Pause()
    {
        var pose = ros.LatestPose;
        if (pose == null) return Answer.Refusal("no telemetry from the body yet: the hold needs where it stands");
        return Answer.Of(golemActor.Using(
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
        var pose = ros.LatestPose;
        if (pose == null) return Answer.Refusal("no telemetry from the body yet: the resume needs where it stands");
        return Answer.Of(golemActor.Using(
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
        .WithParameters(p => {
            p["x", typeof(double)] = x;
            p["y", typeof(double)] = y;
        })
        .PerformCheckThenCommand());

    // ==================================================================
    // THE BODY'S REPORTS (sim/bridge/body.py posts them; the controller validated the JSON). Each is ONE act, the role's,
    // with the pose the body reports; the domain concludes inside. A report about an order the body was not given
    // (superseded meanwhile) is acknowledged and not journaled.
    // ==================================================================

    /// <summary>The body did the one thing it was told and reports it: `route.Arrive(me)` on the ROUTE UNDERWAY — the golem
    /// knows which it is; no id enters the act (Juan, 18-sep-2026: "¿no sería la ruta en curso la que terminamos encontrando?";
    /// lab-underway.txt: a reaction on `[_:Golem].Underway()` pushes the print) — with where the body stands now and, relayed as
    /// the domain printed them, the route's id, the point the order named and whether it was a stop, for the EXPOSE alone: the
    /// reaction that tells the follower needs the route it announces for and fires on a stop alone (the literal `true`). The
    /// `route` the body echoes is the host's token to tell a stale report from the order carried. A follower on its last stop
    /// lingers before anything else, so the leader keeps its lead: the clock is the host's. Null: not the order the body was given.</summary>
    public Answer? Arrived(int route)
    {
        var was = Mechanics.Carrying;
        if (was == null || was.Route != route) return null;
        Mechanics.Done();
        var here = ros.LatestPose ?? new Pose(was.X, was.Y, was.Heading);
        bool stop = was.IsMove && was.Kind == "stop";
        Answer answer;
        try
        {
            answer = Answer.Of(golemActor.Using(
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
                    expose @id rid, @x rx, @y ry, @stop reached;
                ")
                .WithParameters(p => {
                    p["id", typeof(int)] = was.Route;
                    p["x", typeof(double)] = was.X;
                    p["y", typeof(double)] = was.Y;
                    p["stop", typeof(bool)] = stop;
                    p["px", typeof(double)] = here.X;
                    p["py", typeof(double)] = here.Y;
                    p["ptheta", typeof(double)] = here.Theta;
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal(Reason(ex)); }
        Report(answer, $"route {was.Route}: {was.Describe()} done, standing at ({here.X:0.0}, {here.Y:0.0}) facing {here.Theta:0.00}");
        if (!answer.Ok || !stop) return answer;
        ReportLocalization(was.Route);
        if (was.Following && was.IsLastStop)
        {
            var linger = TimeSpan.FromSeconds(LingerAfterTold());
            Mechanics.Linger(linger);
            Note($"lingering {linger.TotalSeconds:0} s at ({was.X:0.0}, {was.Y:0.0}) to keep the leader's lead");
        }
        return answer;
    }

    /// <summary>The body could not: stalled, timed out. The route fails in the body's words; the next route's order follows.
    /// Null: not the order the body was given.</summary>
    public Answer? Stuck(int route, string reason)
    {
        var was = Mechanics.Carrying;
        if (was == null || was.Route != route) return null;
        Mechanics.Done();
        var answer = Failed(reason);
        Report(answer, $"route {route} failed: {reason}");
        return answer;
    }

    /// <summary>The body bumped and its motors stopped at once. It says what a bumper can say: where it stood, facing which way,
    /// and where on its shell it was pressed (the bearing). ONE script: `route = g.Bump(me, @bearing)` — the golem reckons the
    /// touch on the plane from the body it declared, finds its route underway, the route concludes inside what the touch was
    /// (a wall it knows: a graze; anything else: a thing, marked — FOR NOW EVERY TOUCH IS A BUMP, Juan 18-sep-2026) and corrects
    /// its way, and the print is what it asks now: the retreat. The peers are told by the reaction on the expose, in the same
    /// words. Nothing underway (the body was standing): the domain refuses, and that refusal is the answer.</summary>
    public Answer Bumped(double bodyX, double bodyY, double bodyHeading, double bearing)
    {
        Answer answer;
        try
        {
            answer = Answer.Of(golemActor.Using(
                @"
                    Check(g.HasPendingMission()) Error 'nothing underway: a touch while the body stands is not written';
                ",
                @"
                    {
                        me = Pose(@bodyX, @bodyY, @bodyHeading);
                        route = g.Bump(me, @bearing);
                        print route.Id 'route', route.Order 'action', route.Amount 'amount', route.IsPending() 'pending';
                        if (route.IsWalkable) {
                            print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                  route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                  route.Following 'following', route.StopsLeft 'stopsLeft';
                        }
                    }
                    expose @bodyX bodyX, @bodyY bodyY, @bodyHeading bodyHeading, @bearing bearing, @name who;
                ")
                .WithParameters(p => {
                    p["bodyX", typeof(double)] = bodyX;
                    p["bodyY", typeof(double)] = bodyY;
                    p["bodyHeading", typeof(double)] = bodyHeading;
                    p["bearing", typeof(double)] = bearing;
                    p["name", typeof(string)] = golem;
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal(Reason(ex)); }
        Report(answer, "the touch is written; the route concluded and corrected its way inside");
        return answer;
    }

    // ==================================================================
    // THE DOMAIN'S DECISIONS THE EMBODIMENT ASKS FOR ON THE BODY'S BEHALF — each ONE act ending in the print; the reaction
    // on g.Find(@id) pushes it.
    // ==================================================================

    // The way decided again on a route in hand — awake with a plan underway, or stranded after a bump with no road from
    // the retreat — from where the body stands (its pose is telemetry: the only thing the host adds).
    private Answer Decided(double x, double y, double theta) => Answer.Of(golemActor.Using(
        @"
            Check(g.HasPendingMission()) Error 'nothing underway: no pending route';
        ",
        @"
            {
                route = g.Underway();
                from = Pose(@x, @y, @theta);
                route.Decide(from);
                print route.Id 'route', route.Order 'action', route.Amount 'amount', route.IsPending() 'pending';
                if (route.IsWalkable) {
                    print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                          route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                          route.Following 'following', route.StopsLeft 'stopsLeft';
                }
            }
        ")
        .WithParameters(p => {
            p["x", typeof(double)] = x;
            p["y", typeof(double)] = y;
            p["theta", typeof(double)] = theta;
        })
        .PerformCheckThenCommand());

    // The world said no — a collision, a stall, no way — in the body's words; the route ends, the next one's order follows.
    private Answer Failed(string reason) => Answer.Of(golemActor.Using(
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
        .WithParameters(p => { p["reason", typeof(string)] = reason; })
        .PerformCheckThenCommand());

    // The operator lets go of everything: every pending route abandoned with the reason, in ONE command. No reaction
    // matches a foreach: the body is stopped by the lever itself (LetGoAsync).
    private Answer LetGo(string reason) => Answer.Of(golemActor.Using(@"
            foreach (route in g.PendingRoutes()) {
                route.Abandon(@reason);
            }
            print g.HasPendingMission() 'pending';
            if (g.HasPendingMission()) { print g.Underway().Id 'route', g.Underway().Order 'action', g.Underway().Amount 'amount'; }
            if (g.HasPendingMission() && g.Underway().IsWalkable) {
                print g.Underway().NextLeg.Kind 'kind', g.Underway().NextLeg.Name 'name',
                      g.Underway().Target.X 'x', g.Underway().Target.Y 'y', g.Underway().Target.Heading 'heading',
                      g.Underway().Following 'following', g.Underway().StopsLeft 'stopsLeft';
            }
        ")
        .WithParameters(p => { p["reason", typeof(string)] = reason; })
        .PerformCommand());

    // The way, decided by the route itself from where the body stands. When no way fits the body, the route fails with
    // the planner's reason. Asked by the output target when a print says 'decide'.
    internal void Decide(string verb)
    {
        var here = ros.LatestPose;
        if (here == null) { Note("no pose yet to decide the way from — asking again shortly"); return; }
        Note($"{verb} — the route underway decides its way from ({here.X:0.0}, {here.Y:0.0})");
        try { Report(Decided(here.X, here.Y, here.Theta), $"the route underway decided its way from ({here.X:0.0}, {here.Y:0.0})"); }
        catch (Exception ex) { Report(Failed("no road: " + Reason(ex)), "the route underway failed: no road"); }
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
        if (first != null && (first.IsTurn || first.IsMove)) Decide("awake with a plan underway");
        else Mechanics.Dispatch(first);
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct);
            var now = AskOrder();
            var mine = Mechanics.Carrying;
            if (now == null) { if (mine != null) Mechanics.Stop(); continue; }
            if (mine != null && mine.SameAs(now)) continue;
            Mechanics.Dispatch(now);
        }
    }

    // ==================================================================
    // The operator's levers on the body.
    // ==================================================================

    // Let go of everything: the body stopped and put back on its mark (ephemeral, a lab lever); the letting go itself
    // is the golem's act, one command for every pending route.
    public async Task LetGoAsync()
    {
        Mechanics.StopOnMark();
        try { await ros.TeleportAsync(home.X, home.Y, 0.0, CancellationToken.None); }
        catch { /* the world reset is best-effort; the journaled fact is the point */ }
        Report(LetGo("the operator let go of everything"), "let go of every pending route");
        Note("letting go of every pending route");
    }

    /// <summary>Reborn: the body is put back on its mark and told it stands there (dead reckoning re-anchors).</summary>
    public async Task PutBackHomeAsync(CancellationToken ct)
    {
        await ros.TeleportAsync(home.X, home.Y, 0.0, ct);
        Mechanics.StopOnMark();
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
        Mechanics.Stop();
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
    private (double X, double Y, double Theta)? WhereTheErrandStarts()
    {
        using var busy = JsonDocument.Parse(golemActor.Using(@"
            print g.HasPendingMission() 'busy';
            if (g.HasPendingMission()) { print g.PlannedEnd().X 'x', g.PlannedEnd().Y 'y', g.PlannedEnd().Heading 'theta'; }
        ").PerformQuery());
        if (busy.RootElement.GetProperty("busy").GetBoolean())
            return (busy.RootElement.GetProperty("x").GetDouble(), busy.RootElement.GetProperty("y").GetDouble(), busy.RootElement.GetProperty("theta").GetDouble());
        var pose = ros.LatestPose;
        return pose == null ? null : (pose.X, pose.Y, pose.Theta);
    }

    // The handle of the route just opened, to tell it the rest.
    private int Newest() => Read("print g.Newest().Id 'v';").GetInt32();

    // What the route asks now — the same question every script ends with.
    private Order AskOrder()
    {
        try { return Order.Parse(golemActor.Using(NextOrder).PerformQuery()); }
        catch (Exception e) { Console.WriteLine($"[golem {golem}] asking the order failed: {e.Message}"); return null; }
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

    private double LingerAfterTold()     => Read("print body.LingerAfterTold.InSeconds 'v';").GetDouble();

    private JsonElement Read(string script, int? id = null)
    {
        using var doc = JsonDocument.Parse(golemActor.Using(script)
            .WithParameters(p => { if (id.HasValue) p["id", typeof(int)] = id.Value; })
            .PerformQuery());
        return doc.RootElement.GetProperty("v").Clone();
    }
}

