using System.Text.Json;
using Choreography.Theater;
using GolemAPI.Controllers;
using GolemAPI.Membrane;
using GolemAPI.Panel;
using Puppeteer;

namespace GolemAPI.Choreography;

// THE ROBOT, AS THE GOLEM SEES IT (Juan, 16-sep-2026: "el robot solo es el cuerpo; nosotros le decimos qué hacer, y cuando
// termina le dice al actor por un endpoint que ya terminó, para pedir el siguiente print"). Its two faces:
//   - ToRos, the OUTPUT TARGET (RobotToRos : IOutputSink): every print the golem's commands return goes there, is switched
//     on, and travels to the body over the websocket as one of the robot's base actions.
//   - this class: what the body REPORTS back through the golem's endpoints (/robot/arrived, /robot/bump, /robot/stuck) —
//     the act is written, the touch protocol runs (the peers' window, Met, the way out of a peer's way), the follower's
//     linger and courtesy step — plus the clock that asks the journal what it wants when no print brought it, and the
//     operator's levers on the body (let go, reset). It holds no plan, no cursor, no legs: the journal holds those.
// The Robot is all a controller needs: its Actor is the golem, its Pose the body's telemetry, its ToRos the output.
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

    /// <summary>The golem itself — the actor every script is performed on (Juan, 16-sep-2026: "en los controllers se pasa el
    /// singleton de Robot y los scripts se hacen por `robot.Actor.Using(...)`").</summary>
    public ActorV2 Actor => golemActor;
    /// <summary>Where the body believes it stands (telemetry, never the journal's); null before the first word from it.</summary>
    public Pose Pose => ros.LatestPose;
    /// <summary>The output target: the print, switched on and sent to the body over ROS.</summary>
    public RobotToRos ToRos { get; }

    // ==================================================================
    // What the body reports (the endpoints hand it here once the act is written).
    // ==================================================================

    /// <summary>The body did the one thing it was told — turned, or reached the point: the act was written; its print is
    /// the next order. A follower arriving at its last stop pulls over to its right and lingers, so the leader keeps its lead.</summary>
    public void Arrived(Answer answer)
    {
        var was = ToRos.Done();
        if (was == null) return;
        if (was.Route == 0) { Note("courtesy step done"); return; }   // an aside: nothing journaled, nothing next
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
    }

    /// <summary>The body could not: stalled, timed out. The route fails in the body's words; the next route's order follows.</summary>
    public void Stuck(int route, string reason)
    {
        ToRos.Done();
        Report(Safely(() => GolemController.Failed(golemActor, route, reason)), $"route {route} failed: {reason}");
    }

    // The body bumped into something — on its way (route > 0) or standing (route 0) — and its motors stopped at once.
    // The DOMAIN says what it suspects (a wall it knows, a peer that spoke, a thing) and what follows: the route corrects
    // its way inside (back off, then the road around) and its print — 'back' — goes to the body AT ONCE; the peers get
    // their window meanwhile, and if one of them was there the conclusion is Met and the route decides its way out of
    // its way (Juan, 8-sep: "the domain decides, the host follows"; 16-sep: "el que decide todo debe ser el dominio").
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
            var told = Safely(() => GolemController.TouchedStanding(golemActor, golem, x, y, heading, poseX, poseY));
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
            var grazed = Safely(() => GolemController.Grazed(golemActor, route, x, y, poseX, poseY, poseTheta));
            if (!grazed.Ok) { Report(grazed, ""); return; }
            if (MayRetryLeg(route)) { Report(grazed, $"route {route} grazed a wall it knows at {where}: backing off to try again"); return; }
            Stuck(route, $"still grazing {with} at {where} after {Grazes(route)} grazes: patience spent");
            return;
        }

        // Something the map does not hold: the bump is journaled and told, the route corrected inside, and the body
        // told to back off at once. Then the peers' window: was it a body?
        Note($"route {route}: bumped into {with} at {where} heading {heading:0.00} — nothing on my map there; the route corrects its way; telling the peers and listening");
        var bumped = Safely(() => GolemController.Bumped(golemActor, route, golem, x, y, heading, poseX, poseY, poseTheta));
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
            var met = Safely(() => GolemController.Met(golemActor, suspicion.Who, x, y));
            if (!met.Ok) Console.WriteLine($"[golem {golem}] refused: {met.Refused}");
            int taken;
            lock (gate) { if (route != yieldsFor) { yields = 0; yieldsFor = route; } taken = yields; }
            if (taken < MaxYields)
            {
                lock (gate) yields++;
                var mine = ros.LatestPose;
                if (mine == null) { Decide(route, "met a peer, no pose"); return; }
                Note($"route {route}: met {suspicion.Who} at {where} — the route decides its way out of its way");
                try { Report(GolemController.DecidedPast(golemActor, route, suspicion.Who, mine.X, mine.Y, mine.Theta), $"route {route} decided its way past {suspicion.Who}"); }
                catch (Exception ex) { Stuck(route, "no road: " + Reason(ex)); }
                return;
            }
            Stuck(route, $"blocked by {suspicion.Who} at {where} after meeting it {MaxYields} times");
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
                var met = Safely(() => GolemController.Met(golemActor, suspicion.Who, hit.X, hit.Y));
                if (!met.Ok) Console.WriteLine($"[golem {golem}] refused: {met.Refused}");
                return;
            }
        }
        catch (Exception e) { Console.WriteLine($"[golem {golem}] reconsidering a mark failed: {e.Message}"); }
    }

    // ==================================================================
    // The domain's decisions the Robot asks for on the body's behalf.
    // ==================================================================

    // The way, decided by the route itself from where the body stands (the pose: the only thing the host adds). When
    // no way fits the body, the route fails with the planner's reason.
    internal void Decide(int route, string verb)
    {
        var here = ros.LatestPose;
        if (here == null) { Note($"route {route}: no pose yet to decide from — asking again shortly"); return; }
        Note($"route {route}: {verb} — deciding the way from ({here.X:0.0}, {here.Y:0.0})");
        try { Report(GolemController.Decided(golemActor, route, here.X, here.Y), $"route {route} decided its way from ({here.X:0.0}, {here.Y:0.0})"); }
        catch (Exception ex) { Report(Safely(() => GolemController.Failed(golemActor, route, "no road: " + Reason(ex))), $"route {route} failed: no road"); }
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

    // What a script answered: refused → said so (the domain's words); done → its print is the next order, obeyed.
    private void Report(Answer answer, string done)
    {
        if (!answer.Ok)
        {
            Console.WriteLine($"[golem {golem}] refused: {answer.Refused}");
            feed.Broadcast(new PanelEvent(performance.CurrentEntryId, "runtime", "", $"refused — {answer.Refused}", DateTime.UtcNow));
            return;
        }
        Console.WriteLine($"[golem {golem}] {done} (entry {performance.CurrentEntryId})");
        ToRos.Obey(answer.Order);
    }

    // ==================================================================
    // The clock: awake, and now and then — the journal is asked what it wants when nothing came through a print
    // (a told point taken up as a Follow changes the order without any script of mine printing it).
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
        Report(Safely(() => GolemController.LetGo(golemActor, "the operator let go of everything")), "let go of every pending route");
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
        catch (Exception ex) { return new Answer(null, Reason(ex), ""); }
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

    // What the route asks now — the same question every script ends with.
    private Order AskOrder()
    {
        try { return Order.Parse(golemActor.Using(GolemController.NextOrder).PerformQuery()); }
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
