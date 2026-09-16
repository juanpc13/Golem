using Choreography.Theater;
using GolemAPI.Controllers;
using GolemAPI.Membrane;
using GolemAPI.Navigation;
using GolemAPI.Panel;
using Puppeteer;

namespace GolemAPI.Choreography;

// THE BODY'S DRIVER. It does ONE thing at a time (Juan, 16-sep-2026): it takes the order the golem's last script
// printed — a leg to walk, a hold, or "decide the way" — does it, and REPORTS through the scripts in GolemController
// (a point reached, a touch, a wall grazed…), whose print is the next order. It holds no plan, no cursor: when no
// order comes, it asks the journal the same question the scripts end with. What it owns is what the papers leave to
// the host: the clock (listening after a bump, the follower's linger), the wire, and the body's servo.
public sealed class GolemDriver
{
    // Arrival tolerances, in metres: a stop is "reached" within the body's radius; a door is lined up tighter.
    private const double ArriveWithin = 0.25;
    private const double LineUpWithin = 0.15;
    private const double LeaderStandoff = 1.0;   // the follower's last stop is met this short of the leader's spot
    private const int MaxYields = 4;             // times the golem steps out of a peer's way before giving the route up

    private readonly PerformanceV2 performance;   // the journal's entry id and the shutdown
    private readonly ActorV2 golemActor;          // the golem itself: every script and query goes to it
    private readonly Rosbridge ros;
    private readonly INavigator navigator;
    private readonly PanelFeed feed;
    private readonly HttpBroker wire;
    private readonly Orders orders;
    private readonly string golem;
    private readonly (double X, double Y) home;
    private readonly string journalPath;

    public GolemDriver(PerformanceV2 performance, ActorV2 golemActor, Rosbridge ros, INavigator navigator, PanelFeed feed, HttpBroker wire, Orders orders,
                       string golem, (double X, double Y) home, string journalPath)
    {
        this.performance = performance;
        this.golemActor = golemActor;
        this.ros = ros;
        this.navigator = navigator;
        this.feed = feed;
        this.wire = wire;
        this.orders = orders;
        this.golem = golem;
        this.home = home;
        this.journalPath = journalPath;
    }

    public async Task RunAsync(CancellationToken ct)
    {
        // Awake with a way underway: it was decided from wherever the body stood then, and the body may be anywhere
        // now — decide it again from here before walking anything.
        var first = AskOrder();
        if (first != null && (first.What == "turn" || first.What == "run")) await DecideAsync(first.Route, "awake with a plan underway", ct);
        int yields = 0, yieldsFor = 0;
        while (!ct.IsCancellationRequested)
        {
            // an order left by a script (the operator's, or my own report), else the journal asked: a told point
            // taken up as a route of my own (Follow) changes the order without any script of mine printing it
            var order = await orders.WaitAsync(TimeSpan.FromSeconds(2), ct) ?? AskOrder();
            if (order == null) { await MakeRoomIfTouchedAsync(ct); continue; }
            if (order.Route != yieldsFor) { yields = 0; yieldsFor = order.Route; }
            switch (order.What)
            {
                case "hold":   await HoldAsync(order.Route, ct); break;
                case "decide": await DecideAsync(order.Route, "another road", ct); break;
                case "turn":
                case "run":    yields = await ActAsync(order, yields, ct); break;
                default:       Console.WriteLine($"[golem {golem}] an order I do not know: '{order.What}'"); break;
            }
        }
    }

    // One thing: TURN in place to the next leg's heading, or RUN to its point — line up and run through when it is a
    // door, one run otherwise; the follower's last stop is met a body's length short (the leader may still be there).
    // A different order arriving meanwhile — a hold, a let-go, a way decided elsewhere — drops this drive: the loop
    // takes the new order next.
    private async Task<int> ActAsync(Order order, int yields, CancellationToken ct)
    {
        bool turn = order.What == "turn";
        bool lastStop = !turn && order.Kind == "stop" && order.StopsLeft <= 1;
        bool standoff = order.Following && lastStop;
        string what = turn ? $"turning to heading {order.Heading:0.00} for" : order.Kind switch { "stop" => "heading to a stop", "via" => "heading to a point", _ => $"heading to the passage {order.Name}" };
        if (standoff) what += $", stopping {LeaderStandoff:0.0} short of the leader's spot";
        Note($"route {order.Route}: {what} ({order.X:0.0}, {order.Y:0.0})");

        heardAtDriveStart = HeardBumpCount();
        using var superseded = CancellationTokenSource.CreateLinkedTokenSource(ct);
        // the same order arriving while it is being walked (the endpoint's answer after AskOrder already took it
        // from the journal) is consumed, or it would be walked twice; a different one drops this drive
        void Newer(Order o) { if (o.SameAs(order)) orders.Take(); else superseded.Cancel(); }
        orders.Arrived += Newer;
        Outcome outcome = Outcome.Arrived;
        try
        {
            if (turn)
                outcome = await navigator.TurnToAsync(order.Heading, superseded.Token);
            else
            {
                if (order.IsCrossedStraight)
                    outcome = await navigator.GoToAsync(order.AX, order.AY, LineUpWithin, superseded.Token);
                if (outcome.Reached && !superseded.IsCancellationRequested)
                    outcome = await navigator.GoToAsync(order.EX, order.EY, standoff ? LeaderStandoff : ArriveWithin, superseded.Token);
            }
        }
        catch (OperationCanceledException) when (superseded.IsCancellationRequested && !ct.IsCancellationRequested) { }
        finally { orders.Arrived -= Newer; }
        if (ct.IsCancellationRequested) return yields;
        if (superseded.IsCancellationRequested)
        {
            await ros.DriveAsync(0, 0, CancellationToken.None);
            return yields;   // a different order came: the loop takes it
        }

        if (outcome.Reached)
        {
            // the report: the act that moves the route's cursor; its print is the next order
            var answer = turn ? Safely(() => GolemController.Turned(golemActor, order.Route))
                : order.Kind == "stop" ? Safely(() => GolemController.Reached(golemActor, order.Route, order.X, order.Y))
                : Safely(() => GolemController.Passed(golemActor, order.Route, order.X, order.Y));
            Report(answer, $"route {order.Route} {(turn ? $"turned to heading {order.Heading:0.00}" : order.Kind == "stop" ? "reached the stop" : "passed the point")} ({order.X:0.0}, {order.Y:0.0})");
            if (lastStop && answer.Ok)
            {
                ReportLocalization(order.Route);
                if (order.Following) await LingerAsync(order, ct);
            }
            return yields;
        }

        string reason = outcome.Reason;
        if (outcome.Hit != null)
        {
            // The world said "you touched something". The DOMAIN says what it suspects (a wall it knows, a peer that
            // spoke, a thing) and names the conclusion; the host only waits, asks and writes it.
            string where = $"({outcome.Hit.X:0.0}, {outcome.Hit.Y:0.0})";
            if (Suspect(outcome.Hit, heardAtDriveStart).Kind == "wall")
            {
                // A wall I know: my own execution error. The golem keeps its patience (the same leg, again) or has
                // spent it (the route ends). Neither is the host's call.
                Note($"route {order.Route}: grazed {outcome.Hit.With} at {where}, a wall I know — telling the golem");
                var grazed = GolemController.Grazed(golemActor, order.Route, outcome.Hit.X, outcome.Hit.Y);
                Report(grazed, $"route {order.Route} grazed a wall it knows at {where}");
                if (MayRetryLeg(order.Route)) return yields;   // the order printed is the same leg, again, from here
                reason = $"still grazing {outcome.Hit.With} at {where} after {Grazes(order.Route)} grazes: patience spent";
                orders.Take();
            }
            else
            {
                // Something the map does not hold. The touch is journaled and told (the bump interrupts the way: the
                // order printed is 'decide'); then the golem waits for the peers to speak and asks what it suspects.
                string who = await BumpAndListenAsync(order.Route, outcome.Hit, ct);
                if (who == "") return yields;   // a thing, now marked: the order the bump printed says 'decide'
                if (yields < MaxYields)
                {
                    yields++;
                    // A body, and it told where it stands. The golem decides a way that steps out of its way — the
                    // courtesy step is its first leg. Both bodies do this, each to its own right: that is how two of
                    // them pass instead of shove. This decision supersedes the bump's 'decide'.
                    var mine = ros.LatestPose;
                    if (mine == null) { await Task.Delay(200, ct); return yields; }
                    Note($"route {order.Route}: met {who} at {where} — the route decides its way out of its way");
                    orders.Take();
                    try { Report(GolemController.DecidedPast(golemActor, order.Route, who, mine.X, mine.Y, mine.Theta), $"route {order.Route} decided its way past {who}"); }
                    catch (Exception ex) { Report(Safely(() => GolemController.Failed(golemActor, order.Route, "no road: " + Reason(ex))), $"route {order.Route} failed: no road"); }
                    return yields;
                }
                reason = $"blocked by {who} at {where} after meeting it {MaxYields} times";
            }
        }
        Report(GolemController.Failed(golemActor, order.Route, reason), $"route {order.Route} failed: {reason}");
        return yields;
    }

    // The way, decided by the route itself from where the body stands (the pose: the only thing the host adds). When
    // no way fits the body, the route fails with the planner's reason.
    private async Task DecideAsync(int route, string verb, CancellationToken ct)
    {
        var here = ros.LatestPose;
        if (here == null) { await Task.Delay(200, ct); return; }
        Note($"route {route}: {verb} — deciding the way from ({here.X:0.0}, {here.Y:0.0})");
        try { Report(GolemController.Decided(golemActor, route, here.X, here.Y), $"route {route} decided its way from ({here.X:0.0}, {here.Y:0.0})"); }
        catch (Exception ex) { Report(Safely(() => GolemController.Failed(golemActor, route, "no road: " + Reason(ex))), $"route {route} failed: no road"); }
    }

    // Held by the operator: the body stops where it stands and waits for the next order (Resume prints the leg it
    // was on), telling any touch meanwhile as a standing body's. The journal is asked now and then, in case.
    private async Task HoldAsync(int route, CancellationToken ct)
    {
        await ros.DriveAsync(0, 0, CancellationToken.None);
        var pose = ros.LatestPose;
        Note($"route {route}: paused by the operator at ({pose?.X ?? double.NaN:0.0}, {pose?.Y ?? double.NaN:0.0}) — standing until resumed");
        var seen = ros.LatestContact?.At ?? DateTime.MinValue;
        var asked = DateTime.UtcNow;
        while (!ct.IsCancellationRequested && !orders.HasPending)
        {
            await Task.Delay(250, ct);
            var touch = ros.LatestContact;
            if (touch != null && touch.At > seen) { seen = touch.At; TellTouchedStanding(); }
            if (DateTime.UtcNow - asked > TimeSpan.FromSeconds(3))
            {
                asked = DateTime.UtcNow;
                var now = AskOrder();
                if (now != null && now.What != "hold") { orders.Offer(now); break; }
            }
        }
        if (!ct.IsCancellationRequested) Note($"route {route}: resumed — taking up the way from where the body stands");
    }

    // A stop a peer told us about is a stop that peer is already past: the follower pulls over to its right (a
    // courtesy step the golem chooses) and lingers the body's declared while, so the leader keeps its lead.
    private async Task LingerAsync(Order order, CancellationToken ct)
    {
        await StepAsideAsync("pulling over to the right, off the leader's way", ct);
        var hold = TimeSpan.FromSeconds(LingerAfterTold());
        if (hold <= TimeSpan.Zero) return;
        Note($"lingering {hold.TotalSeconds:0} s at ({order.X:0.0}, {order.Y:0.0}) to keep the leader's lead");
        await StandAsync(hold, ct);
    }

    // What a script answered: refused → said so (the domain's words); done → its order left for the loop.
    private void Report(Answer answer, string done)
    {
        if (!answer.Ok)
        {
            Console.WriteLine($"[golem {golem}] refused: {answer.Refused}");
            feed.Broadcast(new PanelEvent(performance.CurrentEntryId, "runtime", "", $"refused — {answer.Refused}", DateTime.UtcNow));
            return;
        }
        Console.WriteLine($"[golem {golem}] {done} (entry {performance.CurrentEntryId})");
        orders.Offer(answer.Order);
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
        catch (Exception ex) { return new Answer(null, Reason(ex)); }
    }

    private static string Reason(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;
        return inner.Message;
    }

    // ------------------------------------------------------------------
    // The touch protocol (Juan, 8-sep: "the domain decides, the host follows"): journal the bump — a mark at once, told
    // to every peer with my name — wait for the peers to speak, then ask the DOMAIN what it suspects: a peer that bumped
    // near there and then (Met: the marks come back) or a thing (the mark stands). Returns the peer's name, or "".
    // ------------------------------------------------------------------
    private static readonly TimeSpan Listen = TimeSpan.FromMilliseconds(2500);
    private static readonly TimeSpan Reconsider = TimeSpan.FromSeconds(12);
    private int heardAtDriveStart;   // peers' bumps heard before the current drive began are older news than this touch

    private async Task<string> BumpAndListenAsync(int route, Collision hit, CancellationToken ct)
    {
        int since = heardAtDriveStart;
        Note($"route {route}: bumped into something at ({hit.X:0.0}, {hit.Y:0.0}) heading {hit.Heading:0.00} — nothing on my map there; telling the peers and listening");
        var standing = ros.LatestPose;
        Report(GolemController.Bumped(golemActor, route, golem, hit.X, hit.Y, hit.Heading, standing?.X ?? hit.X, standing?.Y ?? hit.Y),
               $"route {route} bumped into something at ({hit.X:0.0}, {hit.Y:0.0})");

        // the host owns the clock: the peers get the window to speak, then the domain is asked
        var until = DateTime.UtcNow + Listen;
        var suspicion = Suspect(hit, since);
        while (suspicion.Kind != "peer" && DateTime.UtcNow < until && !ct.IsCancellationRequested)
        {
            await Task.Delay(250, ct);
            suspicion = Suspect(hit, since);
        }
        if (suspicion.Kind == "peer")
        {
            Note($"route {route}: the domain suspects {suspicion.Who} — it bumped there too: a body, not a thing ({suspicion.Conclusion})");
            Report(GolemController.Met(golemActor, suspicion.Who, hit.X, hit.Y), $"met {suspicion.Who} at ({hit.X:0.0}, {hit.Y:0.0})");
            return suspicion.Who;
        }
        Note($"route {route}: nobody else bumped there and then — the mark the bump presumed at ({hit.X:0.0}, {hit.Y:0.0}) stands; reconsidering for {Reconsider.TotalSeconds:0}s");
        _ = ReconsiderAsync(route, hit, since, ct);
        return "";
    }

    // A peer may speak after the window: its own row goes through its journal, its reaction and the wire before it
    // reaches mine. The host keeps asking the domain for a while; if it then suspects a peer, the conclusion is the
    // same Met — the mark comes back, here and in every peer that learned it (10-sep lab: the ghost mark).
    private async Task ReconsiderAsync(int route, Collision hit, int since, CancellationToken ct)
    {
        try
        {
            var until = DateTime.UtcNow + Reconsider;
            while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct);
                var suspicion = Suspect(hit, since);
                if (suspicion.Kind != "peer") continue;
                Note($"route {route}: {suspicion.Who} spoke after the window — it was there too: the mark the bump presumed at ({hit.X:0.0}, {hit.Y:0.0}) is taken back");
                Report(GolemController.Met(golemActor, suspicion.Who, hit.X, hit.Y), $"met {suspicion.Who} at ({hit.X:0.0}, {hit.Y:0.0}), late");
                return;
            }
        }
        catch (OperationCanceledException) { }
        catch (Exception e) { Console.WriteLine($"[golem {golem}] reconsidering a mark failed: {e.Message}"); }
    }

    // Standing still for a while — and telling any touch meanwhile: whoever moved into me must hear it met a body.
    private async Task StandAsync(TimeSpan span, CancellationToken ct)
    {
        var until = DateTime.UtcNow + span;
        var seen = ros.LatestContact?.At ?? DateTime.MinValue;
        while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
        {
            await Task.Delay(250, ct);
            var touch = ros.LatestContact;
            if (touch == null || touch.At <= seen) continue;
            seen = touch.At;
            TellTouchedStanding();
        }
    }

    private DateTime lastToldStanding = DateTime.MinValue;

    // The touch of a standing body is a fact worth telling (the golem's own Bump, no route), at most once a second.
    private void TellTouchedStanding()
    {
        if (DateTime.UtcNow - lastToldStanding < TimeSpan.FromSeconds(1)) return;
        lastToldStanding = DateTime.UtcNow;
        var pose = ros.LatestPose;
        var touch = ros.LatestContact;
        if (pose == null || touch == null) return;
        var (hx, hy, hh) = touch.On(pose, Radius());   // where on the shell it was pressed, on the plane
        Note($"touched while standing at ({hx:0.0}, {hy:0.0}) — telling the peers");
        var answer = GolemController.TouchedStanding(golemActor, golem, hx, hy, hh, pose.X, pose.Y);
        if (!answer.Ok) Console.WriteLine($"[golem {golem}] refused: {answer.Refused}");
    }

    // ------------------------------------------------------------------
    // Bodies sharing a floor. The courtesy step is the golem's to choose (g.Aside); the host only walks it. A golem
    // standing idle that gets touched journals the touch — so whoever moved hears it met a body — and makes room.
    // ------------------------------------------------------------------
    private DateTime lastMadeRoom = DateTime.MinValue;

    private async Task MakeRoomIfTouchedAsync(CancellationToken ct)
    {
        var touch = ros.LatestContact;
        if (touch == null || touch.At <= lastMadeRoom || DateTime.UtcNow - touch.At > TimeSpan.FromSeconds(2)) return;
        lastMadeRoom = DateTime.UtcNow;
        TellTouchedStanding();
        await StepAsideAsync("making room", ct);
    }

    private async Task StepAsideAsync(string why, CancellationToken ct)
    {
        var pose = ros.LatestPose;
        if (pose == null) return;
        var (x, y) = Aside(pose);
        if (Math.Abs(x - pose.X) < 1e-6 && Math.Abs(y - pose.Y) < 1e-6) return;
        Note($"{why}: stepping to ({x:0.0}, {y:0.0})");
        await navigator.GoToAsync(x, y, 0.15, ct);
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

    // ------------------------------------------------------------------
    // The operator's levers on the body.
    // ------------------------------------------------------------------

    // Let go of everything: the body stopped and put back on its mark (ephemeral, a lab lever); the letting go itself
    // is the golem's act, one command for every pending route.
    public async Task LetGoAsync()
    {
        try
        {
            await ros.DriveAsync(0, 0, CancellationToken.None);
            await ros.TeleportAsync(home.X, home.Y, 0.0, CancellationToken.None);
        }
        catch { /* the world reset is best-effort; the journaled fact is the point */ }
        orders.Drop();   // whatever leg was being walked is dropped: nothing to report on a route let go
        Report(GolemController.LetGo(golemActor, "the operator let go of everything"), "let go of every pending route");
        Note("letting go of every pending route");
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
        try { await ros.DriveAsync(0, 0, CancellationToken.None); } catch { /* best effort: the wipe is the point */ }
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

    // ------------------------------------------------------------------
    // Reads: typed Out parameters through the rent-lease (never parsing print), or the golem's objects walked.
    // ------------------------------------------------------------------

    // What the route asks now — the same question every script ends with, asked when nothing was left in the mailbox.
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
    public double Speed()                => Read<double>("@v = body.Speed.InMetersPerSecond;");
    public double Radius()               => Read<double>("@v = body.Radius.InMeters;");
    public double Retreat()              => Read<double>("@v = body.Retreat.InMeters;");
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
