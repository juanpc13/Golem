using System.Globalization;
using Choreography.Input;
using Choreography.Told;
using Choreography.Transport.Brokered;
using GolemHost.Membrane;
using GolemHost.Navigation;
using GolemHost.Panel;
using Puppeteer;

namespace GolemHost.Choreography;

// The golem's choreography. The mission loop hands each pending mission to the
// navigator (the body's locomotion, behind a seam) one leg at a time, and reports the
// verdicts to a Saga keyed by mission id — every write goes through the actor, guarded
// by a domain Check, and the journal stays the only truth. A real collision (the simulator's
// contact sensor) arrives as the navigator's verdict and is journaled as the mission's failure,
// naming what the body hit: the map never knew about it.
// Speech (tells) is a Reaction on the golem's own journal; the panel's journal lane is the
// journal itself, tapped record by record (Panel/JournalTap).
public sealed class GolemChoreography
{
    // How close counts as "there". A leg's end (a door's far side, a goal of my own) is met
    // tightly; the approach in front of a door tighter still, so the run through the gap is
    // straight. A point a PEER told me about is where the leader stood — and may still stand:
    // bodies are real now, so the follower stops a body's length short instead of ramming it.
    private const double ArriveWithin = 0.25;     // m
    private const double LineUpWithin = 0.15;     // m
    private const double LeaderStandoff = 1.0;    // m

    // Bumping into a wall the golem KNOWS is its own execution error: it backs off, lines up and
    // tries the leg again — this many times before the mission is given up as failed.
    private const int RetriesAfterBump = 3;

    private readonly GolemPerformance perf;
    private readonly Rosbridge ros;
    private readonly INavigator navigator;
    private readonly PanelFeed feed;
    private readonly HttpBroker wire;
    private readonly string golem;
    private readonly string body;
    private readonly (double X, double Y) home;
    private readonly string tellDoneTo;
    private readonly string journalPath;
    private readonly InProcessBroker ops = new();
    private readonly string topic;
    private readonly TellBindingTable bindings = new();
    private ToldListener toldListener;

    internal GolemChoreography(GolemPerformance perf, Rosbridge ros, INavigator navigator, PanelFeed feed, HttpBroker wire,
                               string golem, string body, (double X, double Y) home, string tellDoneTo, string journalPath)
    {
        this.perf = perf;
        this.ros = ros;
        this.navigator = navigator;
        this.feed = feed;
        this.wire = wire;
        this.golem = golem;
        this.body = body;
        this.home = home;
        this.tellDoneTo = tellDoneTo;
        this.journalPath = journalPath;
        topic = $"{golem}-ops";
    }

    // ------------------------------------------------------------------
    // BEFORE perf.Start(): the tell transport and every Reaction. Start is
    // what arms a .Cue() reaction in continuous mode.
    // ------------------------------------------------------------------
    public void DefineReactions()
    {
        bindings.Bind(golem, $"tell-{golem}");
        if (tellDoneTo != null)
            bindings.Bind(tellDoneTo, $"tell-{tellDoneTo}");
        perf.UseTellTransport(new BrokerTellTransport(wire, bindings, golem));

        if (tellDoneTo == null) return;

        // Speech is a Reaction, never a command: "this mission was ordered" (Assign,
        // capturing its point) closed by "this SAME mission completed" (Complete,
        // unifying on $missionId). The body announces the point and tells the peer.
        //
        // WHY two statements (engine 2.0.1-beta.10017): when the ack arrives, the engine
        // elides the {tell, ack} pair — but only if the tell entry is a SINGLE tell statement.
        // Rehydration then skips elided entries and resumes the entry counter after the
        // last REPLAYED one, so an elided pair at the tail of the journal gets its ids
        // REUSED by the next boot: the next Assign lands on an id already marked elided
        // and vanishes on the following replay ("unknown mission N"). Announcing the
        // point first is a real fact of the golem and keeps this entry out of elision.
        perf.Actor.Reactions.DefineReaction("echo-visited-point")
            .Cue().Company().WithSharedHydration()
            .Seek("Ordered").One()
                .OnMatch(@"
                    [_:Golem].Assign($missionId, $x, $y)
                ")
            .ThenSeek("Done").One()
                .OnMatch(@"
                    [_:Golem].Complete($missionId)
                ")
            .Causation.Continue($@"
                g.Announce(@missionId);
                tell PointVisited with @x, @y to {tellDoneTo} once 'visited-' + @missionId;
            ");
        // A mission ordered by PLACE has its own shape (AssignPlace) and its own echo: the
        // peer is told the place, and plans its own road to it.
        perf.Actor.Reactions.DefineReaction("echo-visited-place")
            .Cue().Company().WithSharedHydration()
            .Seek("Ordered").One()
                .OnMatch(@"
                    [_:Golem].AssignPlace($missionId, $place)
                ")
            .ThenSeek("Done").One()
                .OnMatch(@"
                    [_:Golem].Complete($missionId)
                ")
            .Causation.Continue($@"
                g.Announce(@missionId);
                tell PlaceVisited with @place to {tellDoneTo} once 'visited-' + @missionId;
            ");
        // (The entries the reactions write — Announce + tell — are NOT observable by other
        // reactions in this build; the journal lane shows them anyway, record by record.)
    }

    // ------------------------------------------------------------------
    // AFTER perf.Start(): announce, wire the ops Saga, take up tells.
    // ------------------------------------------------------------------
    public void Awaken()
    {
        feed.Broadcast(perf.BornThisBoot
            ? new PanelEvent(perf.CurrentEntryId, "info", "", "the golem is born — first hydration ran the release chain", DateTime.UtcNow)
            : new PanelEvent(perf.CurrentEntryId, "info", "", "release chain no-op — this golem was already born", DateTime.UtcNow));
        Console.WriteLine(perf.BornThisBoot
            ? $"[golem {golem}] born by the release chain (entry {perf.CurrentEntryId})"
            : $"[golem {golem}] release chain no-op — awake at entry {perf.CurrentEntryId}");

        // The loop's decisions and outcomes are steps of ONE run per mission: a Saga keyed
        // by mission id serializes them per key; a domain Check on each step is the real
        // guard against a redelivered or repeated step. Letting go is an independent command.
        var dispatch = perf.CreateDispatch();

        dispatch.On<GolemRetired>((actor, m) =>
        {
            actor.Using(@"
                g.Retire(@reason);
            ")
            .WithParameters(p => {
                p["reason", typeof(string)] = m.Reason;
            })
            .PerformCommand();
            Console.WriteLine($"[golem {golem}] let go of every mission (entry {perf.CurrentEntryId})");
        });

        // Passing a door happens as many times per mission as the road has passages: a REPEATED
        // event, so it is a plain command handler (idempotent per message id — one id per leg),
        // NOT a saga step: a saga runs a given step once per key and silently drops a repeat.
        dispatch.On<MissionPassed>((actor, m) =>
        {
            string refused = actor.Using(
                @"
                    Check(g.Knows(@id) && g.IsPending(@id) && g.LegsLeft(@id) > 1 && g.NextPassage(@id) == @passage) Error 'that is not the passage ahead';
                ",
                @"
                    g.Pass(@id, @passage);
                ")
            .WithParameters(p => {
                p["id",      typeof(int)]    = m.Id;
                p["passage", typeof(string)] = m.Passage;
            })
            .PerformCheckThenCommand();
            Settle(refused, $"mission {m.Id} passed {m.Passage}");
        });

        perf.DefineSaga("Mission")
            .On<MissionRouted>(m => m.Id.ToString(CultureInfo.InvariantCulture))
                .Task("route", (actor, m) =>
                {
                    string refused = actor.Using(
                        @"
                            Check(g.Knows(@id) && g.IsPending(@id) && g.IsRouted(@id) == false) Error 'mission already has its road';
                        ",
                        @"
                            g.Route(@id, @plan);
                        ")
                    .WithParameters(p => {
                        p["id",   typeof(int)]    = m.Id;
                        p["plan", typeof(string)] = m.Plan;
                    })
                    .PerformCheckThenCommand();
                    Settle(refused, $"mission {m.Id} takes the road {m.Plan}");
                })
            .On<MissionSuperseded>(m => m.Id.ToString(CultureInfo.InvariantCulture))
                .Task("supersede", (actor, m) =>
                {
                    string refused = actor.Using(
                        @"
                            Check(g.Knows(@id) && g.IsPending(@id) && g.WasTold(@id) && g.HasNewerTold(@id)) Error 'nothing newer was told';
                        ",
                        @"
                            g.Supersede(@id, @by);
                        ")
                    .WithParameters(p => {
                        p["id", typeof(int)] = m.Id;
                        p["by", typeof(int)] = m.By;
                    })
                    .PerformCheckThenCommand();
                    Settle(refused, $"mission {m.Id} superseded by {m.By}: catching up with the leader");
                })
            .On<MissionSucceeded>(m => m.Id.ToString(CultureInfo.InvariantCulture))
                .Task("complete", (actor, m) =>
                {
                    string refused = actor.Using(
                        @"
                            Check(g.Knows(@id) && g.IsPending(@id)) Error 'mission is not pending';
                        ",
                        @"
                            g.Complete(@id);
                        ")
                    .WithParameters(p => {
                        p["id", typeof(int)] = m.Id;
                    })
                    .PerformCheckThenCommand();
                    Settle(refused, $"mission {m.Id} completed");
                })
            .On<MissionFailed>(m => m.Id.ToString(CultureInfo.InvariantCulture))
                .Task("fail", (actor, m) =>
                {
                    string refused = actor.Using(
                        @"
                            Check(g.Knows(@id) && g.IsPending(@id)) Error 'mission is not pending';
                        ",
                        @"
                            g.Fail(@id, @reason);
                        ")
                    .WithParameters(p => {
                        p["id",     typeof(int)]    = m.Id;
                        p["reason", typeof(string)] = m.Reason;
                    })
                    .PerformCheckThenCommand();
                    Settle(refused, $"mission {m.Id} failed: {m.Reason}");
                });

        dispatch.ConsumeFrom(new BrokerInputSource(ops, topic), Route);

        // Uptake: whatever point a peer visited becomes a mission of MY own — the
        // hearer's verb (AssignTold mints the handle inside), one journaled perform per tell.
        // Only plain @params here: a nested call as an argument faults the reaction matcher.
        toldListener = perf
            .ListenAs(golem, bindings, wire)
            .Told("PointVisited").With<double>("x").With<double>("y")
                .Command("g.AssignTold(@x, @y);")
            .Told("PlaceVisited").With<string>("place")
                .Command("g.AssignToldPlace(@place);")
            .Start();
        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "",
            $"listening for tells as '{golem}' on topic 'tell-{golem}'", DateTime.UtcNow));
        Console.WriteLine($"[golem {golem}] listening for tells on topic 'tell-{golem}'");
        if (tellDoneTo != null)
            Console.WriteLine($"[golem {golem}] will tell '{tellDoneTo}' every visited point");
    }

    // The boundary of the ops surface: the producer's "kind" header becomes the
    // dispatch tag here, and anything unknown is dropped (null).
    private static DispatchCommand? Route(InputSignal signal)
    {
        if (!signal.Headers.TryGetValue("kind", out string kind)) return null;
        int tag = kind switch
        {
            "routed"    => MissionRouted.TypeId,
            "passed"    => MissionPassed.TypeId,
            "superseded" => MissionSuperseded.TypeId,
            "succeeded" => MissionSucceeded.TypeId,
            "failed"    => MissionFailed.TypeId,
            "retired"   => GolemRetired.TypeId,
            _           => -1
        };
        return tag < 0 ? null : new DispatchCommand(signal.Id, (char)tag + signal.Value);
    }

    private void Produce(string kind, string idempotencyId, string payload) =>
        ops.ProduceAsync(topic, idempotencyId, new Dictionary<string, string> { ["kind"] = kind }, payload)
           .GetAwaiter().GetResult();

    private void Settle(string refused, string done)
    {
        if (refused == "")
        {
            Console.WriteLine($"[golem {golem}] {done} (entry {perf.CurrentEntryId})");
            return;
        }
        Console.WriteLine($"[golem {golem}] refused: {refused}");
        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", $"refused — {refused}", DateTime.UtcNow));
    }

    // The operator asks the golem to let go of every mission: the body is stopped and put
    // back on its mark (ephemeral, a lab lever), and the forgetting is journaled through the
    // same ops surface.
    public async Task LetGoAsync()
    {
        try
        {
            await ros.DriveAsync(0, 0, CancellationToken.None);
            await ros.TeleportAsync(home.X, home.Y, 0.0, CancellationToken.None);
        }
        catch { /* the world reset is best-effort; the journaled fact is the point */ }

        Produce("retired", $"{golem}:retire:{DateTime.UtcNow.Ticks}", GolemRetired.Payload("operator reset from the panel"));
        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", "letting go of every mission", DateTime.UtcNow));
    }

    // The operator's hard reset — a LAB lever, not a domain fact: stop the body, wipe THIS
    // golem's journal and exit; Docker restarts the container and the golem is born again at
    // entry 1 (the reborn boot puts the body back on its mark). With cascade, every peer is
    // asked to do the same: the world is shared, and the hearer dedups the sender's once-ids
    // ('visited-N'), so resetting one side alone would make the peer swallow the next tells
    // as repeats.
    public async Task ResetEverythingAsync(bool cascade)
    {
        Console.WriteLine($"[golem {golem}] RESET EVERYTHING — wiping the journal and rebooting{(cascade ? ", peers too" : "")}");
        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", "reset everything — wiping the journal, the golem reboots reborn", DateTime.UtcNow));
        if (cascade)
            foreach (var peer in wire.Peers)
                await wire.AskPeerAsync(peer, "reset-everything?cascade=false");
        try
        {
            await ros.DriveAsync(0, 0, CancellationToken.None);
        }
        catch { /* best effort: the wipe is the point */ }

        // Give the HTTP response time to leave, then wipe and go.
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            try { perf.Dispose(); } catch { /* the loop may be mid-perform; we are leaving anyway */ }
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
    // The mission loop. A mission is a point on the map; before moving, the golem decides
    // its ROAD from where the body stands — the passages to cross, the goal last — and
    // journals it (g.Route). Then the navigator is handed one leg at a time: each passage
    // crossed is journaled (g.Pass), the goal reached is the mission completed. What counts
    // as failure on a leg (a collision reported by the world, a stall, a timeout) is the
    // navigator's to say; the golem journals the verdict with its reason.
    // ------------------------------------------------------------------
    public async Task RunAsync(CancellationToken ct)
    {
        int announcedFor = 0;
        string announcedLeg = null;
        int bumps = 0;   // known walls bumped on the current leg
        while (!ct.IsCancellationRequested)
        {
            var plan = ReadPlan();
            if (!plan.Has)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                continue;
            }
            string key = $"{golem}:mission:{plan.Id}";

            // Catching up: a told point still pending when a NEWER told point arrived is where the
            // leader WAS, not where it is. The follower lets it go (a journaled decision) and heads
            // for the newest one by the shortest road — checked between legs, never mid-leg.
            if (plan.Told && plan.NewerTold > 0)
            {
                Console.WriteLine($"[golem {golem}] mission {plan.Id}: a newer told point ({plan.NewerTold}) arrived — letting this one go");
                feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", $"mission {plan.Id}: superseded by {plan.NewerTold} — catching up with the leader", DateTime.UtcNow));
                Produce("superseded", $"{key}:superseded", MissionSuperseded.Payload(plan.Id, plan.NewerTold));
                if (!await WaitUntilAsync(() => IsSettled(plan.Id), ct)) await Task.Delay(TimeSpan.FromSeconds(2), ct);
                continue;
            }

            if (!plan.Routed)
            {
                var here = ros.LatestPose;
                if (here == null) { await Task.Delay(200, ct); continue; }
                string road;
                try
                {
                    road = PlanFrom(plan.Id, here.X, here.Y);
                }
                catch (Exception ex)
                {
                    Produce("failed", $"{key}:failed", MissionFailed.Payload(plan.Id, "no road: " + Reason(ex)));
                    if (!await WaitUntilAsync(() => IsSettled(plan.Id), ct)) await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    continue;
                }
                Console.WriteLine($"[golem {golem}] mission {plan.Id}: road {road}");
                feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", $"mission {plan.Id}: road {road}", DateTime.UtcNow));
                Produce("routed", $"{key}:routed", MissionRouted.Payload(plan.Id, road));
                await WaitUntilAsync(() => IsRouted(plan.Id), ct);
                continue;
            }

            // The last leg of a told mission ends a body's length short: the leader may still be there.
            bool standoff = plan.Told && plan.LegsLeft == 1;
            if (announcedFor != plan.Id || announcedLeg != plan.Passage)
            {
                announcedFor = plan.Id;
                announcedLeg = plan.Passage;
                bumps = 0;
                string what = plan.LegsLeft > 1 ? $"heading to the passage {plan.Passage}" : $"heading to the goal in {plan.Passage}";
                if (standoff) what += $", stopping {LeaderStandoff:0.0} short of the leader's spot";
                Console.WriteLine($"[golem {golem}] mission {plan.Id}: {what} ({plan.X:0.0}, {plan.Y:0.0})");
                feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", $"mission {plan.Id}: {what} ({plan.X:0.0}, {plan.Y:0.0})", DateTime.UtcNow));
            }

            // A door is crossed straight: line up in front of it, then run through to the far side.
            // The map says where those two points are; an opening or the goal is a single run.
            Outcome outcome = Outcome.Arrived;
            if (plan.ApproachX != plan.ExitX || plan.ApproachY != plan.ExitY)
                outcome = await navigator.GoToAsync(plan.ApproachX, plan.ApproachY, LineUpWithin, ct);
            if (outcome.Reached && !ct.IsCancellationRequested)
                outcome = await navigator.GoToAsync(plan.ExitX, plan.ExitY, standoff ? LeaderStandoff : ArriveWithin, ct);
            if (ct.IsCancellationRequested) break;

            if (!outcome.Reached)
            {
                string reason = outcome.Reason;
                if (outcome.Hit != null)
                {
                    // The world said "you touched something". The golem holds the point against ITS map:
                    // a wall it knows is its own execution error — it has backed off, so it lines up and
                    // tries the leg again (runtime, nothing to journal) until patience runs out. Anything
                    // the map does not hold is reality differing from the map: the mission fails, saying so.
                    string where = $"({outcome.Hit.X:0.0}, {outcome.Hit.Y:0.0})";
                    if (KnowsWallAt(outcome.Hit.X, outcome.Hit.Y))
                    {
                        if (bumps < RetriesAfterBump)
                        {
                            bumps++;
                            string retry = $"mission {plan.Id}: bumped into {outcome.Hit.With} at {where}, a wall I know — recovering, try {bumps}/{RetriesAfterBump}";
                            Console.WriteLine($"[golem {golem}] {retry}");
                            feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", retry, DateTime.UtcNow));
                            continue;
                        }
                        reason = $"still bumping into {outcome.Hit.With} at {where} after {RetriesAfterBump} tries";
                    }
                    else
                        reason = $"collided with {outcome.Hit.With} at {where}: nothing on my map there";
                }
                Produce("failed", $"{key}:failed", MissionFailed.Payload(plan.Id, reason));
                if (!await WaitUntilAsync(() => IsSettled(plan.Id), ct)) await Task.Delay(TimeSpan.FromSeconds(2), ct);
                continue;
            }

            if (plan.LegsLeft > 1)
            {
                Produce("passed", $"{key}:pass:{plan.LegsLeft}", MissionPassed.Payload(plan.Id, plan.Passage));
                await WaitUntilAsync(() => LegsLeft(plan.Id) < plan.LegsLeft, ct);
                continue;
            }

            Produce("succeeded", $"{key}:succeeded", MissionSucceeded.Payload(plan.Id));
            if (!await WaitUntilAsync(() => IsSettled(plan.Id), ct))
                await Task.Delay(TimeSpan.FromSeconds(2), ct); // never re-drive on a timeout; back off and re-read

            // Pacing: a point a peer told us about is a point that peer is already past.
            // Holding there a while keeps the leader's lead. How long is the golem's own
            // property (the pace release); the pause itself is runtime — the mission is
            // settled and nothing about the wait belongs in the journal.
            if (WasTold(plan.Id))
            {
                var hold = TimeSpan.FromSeconds(HoldAfterTold());
                if (hold > TimeSpan.Zero)
                {
                    Console.WriteLine($"[golem {golem}] holding {hold.TotalSeconds:0} s at ({plan.X:0.0}, {plan.Y:0.0}) to keep the leader's lead");
                    feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "",
                        $"holding {hold.TotalSeconds:0} s at ({plan.X:0.0}, {plan.Y:0.0}) to keep the leader's lead", DateTime.UtcNow));
                    await Task.Delay(hold, ct);
                }
            }
        }
    }

    private static string Reason(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;
        return inner.Message;
    }

    // Wait for the journal to move as expected — or give up after a while and re-read.
    private async Task<bool> WaitUntilAsync(Func<bool> condition, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        while (!ct.IsCancellationRequested)
        {
            if (condition()) return true;
            if (DateTime.UtcNow - start > TimeSpan.FromSeconds(5))
            {
                Console.WriteLine($"[golem {golem}] the journal did not move as expected within 5 s — backing off");
                return false;
            }
            await Task.Delay(100, ct);
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Typed reads: Out parameters through the rent-lease (never parsing print).
    // ------------------------------------------------------------------
    private (bool Has, int Id, double X, double Y, double ApproachX, double ApproachY, double ExitX, double ExitY,
             bool Routed, int LegsLeft, string Passage, bool Told, int NewerTold) ReadPlan()
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @has = g.HasPendingMission();
            if (g.HasPendingMission()) {
                @id = g.NextId();
                @x = g.NextX();
                @y = g.NextY();
                @ax = g.NextApproachX();
                @ay = g.NextApproachY();
                @ex = g.NextExitX();
                @ey = g.NextExitY();
                @routed = g.IsRouted(g.NextId());
                @legs = g.LegsLeft(g.NextId());
                @passage = g.NextPassage(g.NextId());
                @told = g.WasTold(g.NextId());
                @newer = 0;
                if (g.HasNewerTold(g.NextId())) { @newer = g.NewestToldId(); }
            }
        ")
        .WithParameters(rented, p => {
            p[Parameter.Out, "told",    typeof(bool)]   = default;
            p[Parameter.Out, "newer",   typeof(int)]    = default;
            p[Parameter.Out, "has",     typeof(bool)]   = default;
            p[Parameter.Out, "id",      typeof(int)]    = default;
            p[Parameter.Out, "x",       typeof(double)] = default;
            p[Parameter.Out, "y",       typeof(double)] = default;
            p[Parameter.Out, "ax",      typeof(double)] = default;
            p[Parameter.Out, "ay",      typeof(double)] = default;
            p[Parameter.Out, "ex",      typeof(double)] = default;
            p[Parameter.Out, "ey",      typeof(double)] = default;
            p[Parameter.Out, "routed",  typeof(bool)]   = default;
            p[Parameter.Out, "legs",    typeof(int)]    = default;
            p[Parameter.Out, "passage", typeof(string)] = default;
        })
        .PerformQuery();
        return (rented["has"].GetValue<bool>(), rented["id"].GetValue<int>(),
                rented["x"].GetValue<double>(), rented["y"].GetValue<double>(),
                rented["ax"].GetValue<double>(), rented["ay"].GetValue<double>(),
                rented["ex"].GetValue<double>(), rented["ey"].GetValue<double>(),
                rented["routed"].GetValue<bool>(), rented["legs"].GetValue<int>(),
                rented["passage"].GetValue<string>() ?? "", rented["told"].GetValue<bool>(), rented["newer"].GetValue<int>());
    }

    // The road from where the body stands, as the journal will write it.
    private string PlanFrom(int id, double x, double y)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @plan = g.Plan(@id, @x, @y);
        ")
        .WithParameters(rented, p => {
            p["id", typeof(int)]                     = id;
            p["x",  typeof(double)]                  = x;
            p["y",  typeof(double)]                  = y;
            p[Parameter.Out, "plan", typeof(string)] = default;
        })
        .PerformQuery();
        return rented["plan"].GetValue<string>();
    }

    private bool IsRouted(int id)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @routed = g.IsRouted(@id);
        ")
        .WithParameters(rented, p => {
            p["id", typeof(int)]                      = id;
            p[Parameter.Out, "routed", typeof(bool)]  = default;
        })
        .PerformQuery();
        return rented["routed"].GetValue<bool>();
    }

    private int LegsLeft(int id)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @legs = g.LegsLeft(@id);
        ")
        .WithParameters(rented, p => {
            p["id", typeof(int)]                   = id;
            p[Parameter.Out, "legs", typeof(int)]  = default;
        })
        .PerformQuery();
        return rented["legs"].GetValue<int>();
    }

    // The body's properties, as the journal knows them.
    public double Speed()
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @speed = g.Speed();
        ")
        .WithParameters(rented, p => {
            p[Parameter.Out, "speed", typeof(double)] = default;
        })
        .PerformQuery();
        return rented["speed"].GetValue<double>();
    }

    private double HoldAfterTold()
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @hold = g.HoldAfterTold();
        ")
        .WithParameters(rented, p => {
            p[Parameter.Out, "hold", typeof(double)] = default;
        })
        .PerformQuery();
        return rented["hold"].GetValue<double>();
    }

    public double Radius()
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @radius = g.Radius();
        ")
        .WithParameters(rented, p => {
            p[Parameter.Out, "radius", typeof(double)] = default;
        })
        .PerformQuery();
        return rented["radius"].GetValue<double>();
    }

    // Does a touched point lie on a wall the golem knows? The map answers; the pose is the only telemetry.
    private bool KnowsWallAt(double x, double y)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @known = g.KnowsWallAt(@x, @y);
        ")
        .WithParameters(rented, p => {
            p["x", typeof(double)]                   = x;
            p["y", typeof(double)]                   = y;
            p[Parameter.Out, "known", typeof(bool)]  = default;
        })
        .PerformQuery();
        return rented["known"].GetValue<bool>();
    }

    private bool WasTold(int id)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @told = g.WasTold(@id);
        ")
        .WithParameters(rented, p => {
            p["id", typeof(int)]                    = id;
            p[Parameter.Out, "told", typeof(bool)]  = default;
        })
        .PerformQuery();
        return rented["told"].GetValue<bool>();
    }

    private bool IsSettled(int id)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @settled = g.HasPendingMission() == false || g.NextId() != @id;
        ")
        .WithParameters(rented, p => {
            p["id", typeof(int)]                       = id;
            p[Parameter.Out, "settled", typeof(bool)]  = default;
        })
        .PerformQuery();
        return rented["settled"].GetValue<bool>();
    }
}
