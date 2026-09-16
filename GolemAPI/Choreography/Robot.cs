using System.Text.Json;
using Choreography.Theater;
using GolemAPI.Controllers;
using GolemAPI.Membrane;
using GolemAPI.Panel;
using Puppeteer;

namespace GolemAPI.Choreography;

// THE ROBOT, AS THE GOLEM SEES IT: the output target of every print (Juan, 16-sep-2026: "el print retorna al controller,
// pero ese mismo objeto analiza el JSON y tiene un switch con las acciones disponibles; dentro de cada case están los
// llamados que viajan hasta el robot… el robot solo es el cuerpo, nosotros le decimos qué hacer, y cuando termina le
// dice al actor por un endpoint que ya terminó, para pedir el siguiente print"). Every command the golem performs ends
// with the same print (GolemController.NextOrder); the print comes back to whoever performed it and is handed here —
// Obey — where it is parsed, switched on, and the SAME JSON goes on to the body over the websocket (rosbridge, topic
// /golem/<body>/order) enriched with what the body needs to carry it out: the arrival tolerance and the body the golem
// declared (speed, radius, retreat). The body does that one thing and reports it on the golem's endpoints
// (/robot/turned, /robot/reached, /robot/touched, /robot/stuck); the endpoint writes the act, and its print lands here
// again — until nothing is pending. It also implements IOutputSink, so it IS the actor's OutputTarget: a Reaction that
// emitted the same print would reach the body the same way.
// What stays here of the host's own: the clock (listening after a bump, the follower's linger), the wire, and nothing
// of the plan — no cursor, no legs: the journal holds those.
public sealed class Robot : IOutputSink
{
    private const double ArriveWithin = 0.25;    // a stop is "reached" within the body's radius
    private const double LineUpWithin = 0.15;    // a door is lined up tighter (the crossing must be straight)
    private const double LeaderStandoff = 1.0;   // the follower's last stop is met this short of the leader's spot
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
    private Order carrying;             // the order the body is carrying out now; null while it stands
    private int heardAtOrderStart;      // peers' bumps heard before the current order began are older news than a touch during it
    private int yields, yieldsFor;      // courtesy steps taken on the route underway
    private DateTime lingerUntil = DateTime.MinValue;   // the follower's linger: no order goes out to the body before this
    private DateTime lastStandingTouch = DateTime.MinValue;
    private volatile bool deliberating;   // a touch is being weighed (the peers' window): the clock must not decide meanwhile

    public Robot(PerformanceV2 performance, ActorV2 golemActor, Rosbridge ros, PanelFeed feed, HttpBroker wire,
                 string golem, (double X, double Y) home, string journalPath)
    {
        this.performance = performance;
        this.golemActor = golemActor;
        this.ros = ros;
        this.feed = feed;
        this.wire = wire;
        this.golem = golem;
        this.home = home;
        this.journalPath = journalPath;
    }

    /// <summary>The order the body is carrying out now — what a report from it must be about — or null while it stands.</summary>
    public Order Carrying { get { lock (gate) return carrying; } }

    // ==================================================================
    // THE OUTPUT TARGET: a print arrives, is parsed, switched on, and travels to the body.
    // ==================================================================

    /// <summary>IOutputSink — a Reaction's emitted print lands here like a command's returned one.</summary>
    public void Push(in PushDocument document) => Obey(document.Document);

    /// <summary>The print a command returned: what the route asks now. Parsed, switched on, sent to the body.</summary>
    public void Obey(string print) => Obey(Order.Parse(print));

    public void Obey(Order order)
    {
        if (order == null) { Stand(); return; }   // nothing pending: the body stands
        switch (order.What)
        {
            case "hold":
                Stand();
                Note($"route {order.Route}: paused by the operator — the body stands until resumed");
                break;
            case "decide":
                Decide(order.Route, "another road");
                break;
            case "turn":
            case "run":
                Send(order);
                break;
            default:
                Note($"an order I do not know: '{order.What}'");
                break;
        }
    }

    // The order goes to the body as the SAME JSON the golem printed, plus what the body needs to carry it out. The
    // same order twice (the endpoint's answer after the loop already asked the journal) is not sent again; a
    // different one replaces whatever the body was doing. The follower's linger holds it back a while.
    private void Send(Order order)
    {
        TimeSpan wait;
        lock (gate)
        {
            if (carrying != null && carrying.SameAs(order)) return;
            if (order.Route != yieldsFor) { yields = 0; yieldsFor = order.Route; }
            carrying = order;
            heardAtOrderStart = HeardBumpCount();
            wait = lingerUntil - DateTime.UtcNow;
        }
        string what = order.What == "turn" ? $"turning to heading {order.Heading:0.00} for"
            : order.Kind switch { "stop" => "heading to a stop", "via" => "heading to a point", "around" => "skirting to", "aside" => "stepping aside to", _ => $"heading to the passage {order.Name}" };
        bool standoff = order.Following && order.IsLastStop;
        if (standoff) what += $", stopping {LeaderStandoff:0.0} short of the leader's spot";
        Note($"route {order.Route}: {what} ({order.X:0.0}, {order.Y:0.0}){(wait > TimeSpan.Zero ? $" — after lingering {wait.TotalSeconds:0} s" : "")}");
        double within = standoff ? LeaderStandoff : ArriveWithin;
        if (wait > TimeSpan.Zero)
            _ = Task.Run(async () =>
            {
                await Task.Delay(wait);
                lock (gate) { if (!ReferenceEquals(carrying, order)) return; }
                await PublishAsync(order, within);
            });
        else _ = PublishAsync(order, within);
    }

    private Task PublishAsync(Order order, double within) => ros.PublishAsync(ros.OrderTopic, JsonSerializer.Serialize(new
    {
        order = order.What, route = order.Route, kind = order.Kind, name = order.Name,
        x = order.X, y = order.Y, ax = order.AX, ay = order.AY, ex = order.EX, ey = order.EY,
        hasHeading = order.HasHeading, heading = order.Heading, following = order.Following, stopsLeft = order.StopsLeft,
        within,
        body = new { speed = Speed(), radius = Radius(), retreat = Retreat() }
    }));

    // The body stands: whatever it was doing is dropped.
    private void Stand(bool anchor = false)
    {
        lock (gate) carrying = null;
        _ = ros.PublishAsync(ros.OrderTopic, anchor ? "{\"order\":\"stop\",\"anchor\":true}" : "{\"order\":\"stop\"}");
    }

    // A courtesy step — out of a peer's way, or off the leader's — is a run with no route: the body reports it reached and
    // nothing is journaled (the golem chose the point: g.Aside).
    private void StepAside(string why)
    {
        var pose = ros.LatestPose;
        if (pose == null) return;
        var (x, y) = Aside(pose);
        if (Math.Abs(x - pose.X) < 1e-6 && Math.Abs(y - pose.Y) < 1e-6) return;
        Note($"{why}: stepping to ({x:0.0}, {y:0.0})");
        var step = new Order(0, "run", "aside", "aside", x, y, x, y, x, y, false, 0, false, 0);
        lock (gate) carrying = step;
        _ = PublishAsync(step, LineUpWithin);
    }

    // The way, decided by the route itself from where the body stands (the pose: the only thing the host adds). When
    // no way fits the body, the route fails with the planner's reason.
    private void Decide(int route, string verb)
    {
        var here = ros.LatestPose;
        if (here == null) { Note($"route {route}: no pose yet to decide from — asking again shortly"); return; }
        Note($"route {route}: {verb} — deciding the way from ({here.X:0.0}, {here.Y:0.0})");
        try { Report(GolemController.Decided(golemActor, route, here.X, here.Y), $"route {route} decided its way from ({here.X:0.0}, {here.Y:0.0})"); }
        catch (Exception ex) { Report(Safely(() => GolemController.Failed(golemActor, route, "no road: " + Reason(ex))), $"route {route} failed: no road"); }
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
        Obey(answer.Order);
    }

    // ==================================================================
    // What the body reports (the endpoints hand it here once the act is written).
    // ==================================================================

    /// <summary>Whether a report is about the order the body was given (a stale one — superseded meanwhile — is not journaled).</summary>
    public bool Expects(int route, string what)
    {
        lock (gate) return carrying != null && carrying.Route == route && carrying.What == what;
    }

    /// <summary>The body turned: the act was written; its print is the next order.</summary>
    public void Turned(Answer answer)
    {
        var was = Carrying;
        lock (gate) carrying = null;
        Report(answer, $"route {was?.Route} turned to heading {was?.Heading:0.00}");
    }

    /// <summary>The body reached the point it was sent to: the act was written; its print is the next order. A follower
    /// arriving at its last stop pulls over to its right and lingers, so the leader keeps its lead.</summary>
    public void Reached(Answer answer)
    {
        var was = Carrying;
        lock (gate) carrying = null;
        if (was == null) return;
        if (was.Route == 0) { Note("courtesy step done"); return; }   // an aside: nothing journaled, nothing next
        Report(answer, $"route {was.Route} {(was.Kind == "stop" ? "reached the stop" : "passed the point")} ({was.X:0.0}, {was.Y:0.0})");
        if (answer.Ok && was.IsLastStop)
        {
            ReportLocalization(was.Route);
            if (was.Following)
            {
                var linger = TimeSpan.FromSeconds(LingerAfterTold());
                lock (gate) lingerUntil = DateTime.UtcNow + linger;
                Note($"lingering {linger.TotalSeconds:0} s at ({was.X:0.0}, {was.Y:0.0}) to keep the leader's lead");
                if (Carrying == null) StepAside("pulling over to the right, off the leader's way");
            }
        }
    }

    /// <summary>The body could not: stalled, timed out. The route fails in the body's words; the next route's order follows.</summary>
    public void Stuck(int route, string reason)
    {
        lock (gate) carrying = null;
        Report(Safely(() => GolemController.Failed(golemActor, route, reason)), $"route {route} failed: {reason}");
    }

    // The body touched something — on its way (route > 0) or standing (route 0). It has already backed off and stands.
    // The DOMAIN says what it suspects (a wall it knows, a peer that spoke, a thing) and names the conclusion; the host
    // only waits, asks and writes it (Juan, 8-sep: "the domain decides, the host follows").
    public async Task TouchedAsync(int route, string with, double x, double y, double heading, double poseX, double poseY)
    {
        Order was; int since;
        lock (gate) { was = carrying; since = heardAtOrderStart; }
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
        lock (gate) carrying = null;
        deliberating = true;
        try { await WeighTouchAsync(route, with, x, y, heading, poseX, poseY, since); }
        finally { deliberating = false; }
    }

    private async Task WeighTouchAsync(int route, string with, double x, double y, double heading, double poseX, double poseY, int since)
    {
        var hit = new Collision(with, x, y, heading);
        string where = $"({x:0.0}, {y:0.0})";
        if (Suspect(hit, since).Kind == "wall")
        {
            // A wall I know: my own execution error. The golem keeps its patience (the same thing, again) or has spent
            // it (the route ends). Neither is the host's call.
            Note($"route {route}: grazed {with} at {where}, a wall I know — telling the golem");
            var grazed = Safely(() => GolemController.Grazed(golemActor, route, x, y));
            if (!grazed.Ok) { Report(grazed, ""); return; }
            if (MayRetryLeg(route)) { Report(grazed, $"route {route} grazed a wall it knows at {where}"); return; }   // the print: the same order, again
            Stuck(route, $"still grazing {with} at {where} after {Grazes(route)} grazes: patience spent");
            return;
        }

        // Something the map does not hold. The touch is journaled and told (the bump interrupts the way); then the golem
        // waits for the peers to speak and asks what it suspects — before deciding the way again.
        Note($"route {route}: bumped into something at {where} heading {heading:0.00} — nothing on my map there; telling the peers and listening");
        var bumped = Safely(() => GolemController.Bumped(golemActor, route, golem, x, y, heading, poseX, poseY));
        if (!bumped.Ok) { Report(bumped, ""); return; }
        Console.WriteLine($"[golem {golem}] route {route} bumped into something at {where} (entry {performance.CurrentEntryId})");
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
            int taken; lock (gate) taken = yields;
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
        Obey(bumped.Order);   // 'decide': the route decides its way again from where the body stands
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
    // The clock: awake, and now and then — the journal is asked what it wants when nothing came through a print
    // (a told point taken up as a Follow changes the order without any script of mine printing it).
    // ==================================================================
    public async Task RunAsync(CancellationToken ct)
    {
        // Awake with a way underway: it was decided from wherever the body stood then, and the body may be anywhere
        // now — decide it again from here before anything else.
        var first = AskOrder();
        if (first != null && (first.What == "turn" || first.What == "run")) Decide(first.Route, "awake with a plan underway");
        else Obey(first);
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct);
            if (deliberating) continue;   // a touch is being weighed: its outcome brings the next order
            var now = AskOrder();
            var mine = Carrying;
            if (now == null) { if (mine != null && mine.Route != 0) Stand(); continue; }
            if (mine != null && mine.SameAs(now)) continue;
            if (mine != null && mine.Route == 0 && now.What != "hold") continue;   // a courtesy step underway: the order waits for it
            Obey(now);
        }
    }

    // ==================================================================
    // The operator's levers on the body.
    // ==================================================================

    // Let go of everything: the body stopped and put back on its mark (ephemeral, a lab lever); the letting go itself
    // is the golem's act, one command for every pending route.
    public async Task LetGoAsync()
    {
        Stand(anchor: true);
        try { await ros.TeleportAsync(home.X, home.Y, 0.0, CancellationToken.None); }
        catch { /* the world reset is best-effort; the journaled fact is the point */ }
        Report(Safely(() => GolemController.LetGo(golemActor, "the operator let go of everything")), "let go of every pending route");
        Note("letting go of every pending route");
    }

    /// <summary>Reborn: the body is put back on its mark and told it stands there (dead reckoning re-anchors).</summary>
    public async Task PutBackHomeAsync(CancellationToken ct)
    {
        await ros.TeleportAsync(home.X, home.Y, 0.0, ct);
        Stand(anchor: true);
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
        Stand();
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

    private void Note(string text)
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
    // Reads: typed Out parameters through the rent-lease (never parsing print), or the golem's objects walked.
    // ------------------------------------------------------------------

    // What the route asks now — the same question every script ends with.
    private Order AskOrder()
    {
        try { return Order.Parse(golemActor.Using(GolemController.NextOrder).PerformQuery()); }
        catch (Exception e) { Console.WriteLine($"[golem {golem}] asking the order failed: {e.Message}"); return null; }
    }

    private (string Kind, string Who, string Conclusion) Suspect(Collision hit, int since)
    {
        using var rented = golemActor.RentedParameters();
        golemActor.Using(@"
            @kind = g.Suspect(Pose(@x, @y, @heading), @since).Kind;
            @who = g.Suspect(Pose(@x, @y, @heading), @since).Who;
            @verb = g.Suspect(Pose(@x, @y, @heading), @since).Conclusion;
        ")
        .WithParameters(rented, p => {
            p["x", typeof(double)] = hit.X; p["y", typeof(double)] = hit.Y; p["heading", typeof(double)] = hit.Heading; p["since", typeof(int)] = since;
            p[Parameter.Out, "kind", typeof(string)] = default; p[Parameter.Out, "who", typeof(string)] = default; p[Parameter.Out, "verb", typeof(string)] = default;
        })
        .PerformQuery();
        return (rented["kind"].GetValue<string>() ?? "", rented["who"].GetValue<string>() ?? "", rented["verb"].GetValue<string>() ?? "");
    }

    private (double X, double Y) Aside(Pose pose)
    {
        using var rented = golemActor.RentedParameters();
        golemActor.Using(@"
            @sx = g.Aside(Pose(@x, @y, @theta)).X;
            @sy = g.Aside(Pose(@x, @y, @theta)).Y;
        ")
        .WithParameters(rented, p => {
            p["x", typeof(double)] = pose.X; p["y", typeof(double)] = pose.Y; p["theta", typeof(double)] = pose.Theta;
            p[Parameter.Out, "sx", typeof(double)] = default; p[Parameter.Out, "sy", typeof(double)] = default;
        })
        .PerformQuery();
        return (rented["sx"].GetValue<double>(), rented["sy"].GetValue<double>());
    }

    private bool MayRetryLeg(int id)     => Read<bool>("@v = g.Find(@id).MayRetryLeg;", id);
    private int Grazes(int id)           => Read<int>("@v = g.Find(@id).Grazes;", id);
    private int HeardBumpCount()         => Read<int>("@v = collisions.HeardCount;");
    private double Speed()               => Read<double>("@v = body.Speed.InMetersPerSecond;");
    private double Radius()              => Read<double>("@v = body.Radius.InMeters;");
    private double Retreat()             => Read<double>("@v = body.Retreat.InMeters;");
    private double LingerAfterTold()     => Read<double>("@v = body.LingerAfterTold.InSeconds;");

    private T Read<T>(string script, int? id = null)
    {
        using var rented = golemActor.RentedParameters();
        golemActor.Using(script)
        .WithParameters(rented, p => {
            if (id.HasValue) p["id", typeof(int)] = id.Value;
            p[Parameter.Out, "v", typeof(T)] = default;
        })
        .PerformQuery();
        return rented["v"].GetValue<T>();
    }
}

/// <summary>What the body touched, where on the plane, heading into it — as the body reported it.</summary>
public sealed record Collision(string With, double X, double Y, double Heading);
