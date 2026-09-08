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
// navigator (the body's locomotion, behind a seam) one leg at a time — passages to cross,
// stops to reach — and reports the verdicts to the actor: every write goes through it,
// guarded by a domain Check, and the journal stays the only truth. A real collision (the
// simulator's contact sensor) arrives as the navigator's verdict; the golem holds the touched
// point against its map and either recovers (a wall it knows) or fails the mission naming
// the point (something the map does not hold).
// Speech (tells) is a Reaction on the golem's own journal; the panel's journal lane is the
// journal itself, tapped record by record (Panel/JournalTap).
public sealed class GolemChoreography
{
    // How close counts as "there". A leg's end (a door's far side, a stop of my own) is met
    // tightly; the approach in front of a door tighter still, so the run through the gap is
    // straight. A stop a PEER told me about is where the leader stood — and may still stand:
    // bodies are real, so the follower stops a body's length short instead of ramming it.
    private const double ArriveWithin = 0.25;     // m
    private const double LineUpWithin = 0.15;     // m
    private const double LeaderStandoff = 1.0;    // m

    // Bumping into a wall the golem KNOWS is its own execution error: it backs off, lines up and
    // tries the leg again — this many times before the mission is given up as failed.
    private const int RetriesAfterBump = 3;

    // Bumping into something the map does NOT hold: the golem feels for a way past it — a step
    // aside (right first, then left), one body's width at a time, and the leg again — before it
    // decides the road anew with the marks it has. Beyond this many steps a side is given up.
    private const double SideStep = 0.5;   // m
    private const int MaxSideSteps = 4;

    // Bumping into a PEER — a body the golem knows by name, its fleet — is neither a wall nor an
    // obstacle to map: peers move. The golem yields: waits, tries the leg again, and gives the
    // mission up only after this many yields.
    private static readonly TimeSpan Yield = TimeSpan.FromSeconds(6);
    private const int MaxYields = 4;

    private readonly GolemPerformance perf;
    private readonly Rosbridge ros;
    private readonly INavigator navigator;
    private readonly PanelFeed feed;
    private readonly HttpBroker wire;
    private readonly string golem;
    private readonly string body;
    private readonly (double X, double Y) home;
    private readonly string tellDoneTo;
    private readonly IReadOnlyList<string> peers;
    private readonly string journalPath;
    private readonly InProcessBroker ops = new();
    private readonly string topic;
    private readonly TellBindingTable bindings = new();
    private ToldListener toldListener;

    internal GolemChoreography(GolemPerformance perf, Rosbridge ros, INavigator navigator, PanelFeed feed, HttpBroker wire,
                               string golem, string body, (double X, double Y) home, string tellDoneTo, IReadOnlyList<string> peers, string journalPath)
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
        this.peers = peers;
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
        foreach (var peer in peers.Union(tellDoneTo == null ? Array.Empty<string>() : new[] { tellDoneTo }))
            bindings.Bind(peer, $"tell-{peer}");
        perf.UseTellTransport(new BrokerTellTransport(wire, bindings, golem));

        // What the body bumps into is news for every peer: a mark they can plan around without the
        // bruise. One tell per peer in one entry (several statements: no single-tell elision).
        if (peers.Count > 0)
        {
            // one once-id per addressee: the same id twice would make the second tell a duplicate
            string tells = string.Join("\n", peers.Select(p =>
                $"tell ObstacleFound with @x, @y to {p} once 'obstacle-' + @missionId + '-' + @x + ',' + @y + '-{p}';"));
            perf.Actor.Reactions.DefineReaction("echo-bumped")
                .Cue().Company().WithSharedHydration()
                .Seek("Bumped").One()
                    .OnMatch(@"
                        [_:Golem].Bump($missionId, $x, $y)
                    ")
                .Causation.Continue(tells);
        }

        if (tellDoneTo == null) return;

        // Speech is a Reaction, never a command: every stop the body reaches is told to the peer,
        // whatever mission it belongs to — one reaction, keyed off the progress verb, not off the
        // shape of the entrusting. The once-id names the mission and the point.
        //
        // WHY two statements (engine 2.0.1-beta.10017): when the ack arrives, the engine elides the
        // {tell, ack} pair — but only if the tell entry is a SINGLE tell statement. Rehydration then
        // skips elided entries and resumes the entry counter after the last REPLAYED one, so an
        // elided pair at the tail of the journal gets its ids REUSED by the next boot. Announcing the
        // stop first is a real fact of the golem and keeps this entry out of elision.
        perf.Actor.Reactions.DefineReaction("echo-reached")
            .Cue().Company().WithSharedHydration()
            .Seek("Reached").One()
                .OnMatch(@"
                    [_:Golem].Reach($missionId, $x, $y)
                ")
            .Causation.Continue($@"
                g.Announce(@missionId);
                tell PointVisited with @x, @y to {tellDoneTo} once 'visited-' + @missionId + '-' + @x + ',' + @y;
            ");
    }

    // ------------------------------------------------------------------
    // AFTER perf.Start(): announce, wire the ops handlers, take up tells.
    // ------------------------------------------------------------------
    public void Awaken()
    {
        feed.Broadcast(perf.BornThisBoot
            ? new PanelEvent(perf.CurrentEntryId, "info", "", "the golem is born — first hydration ran the release chain", DateTime.UtcNow)
            : new PanelEvent(perf.CurrentEntryId, "info", "", "release chain no-op — this golem was already born", DateTime.UtcNow));
        Console.WriteLine(perf.BornThisBoot
            ? $"[golem {golem}] born by the release chain (entry {perf.CurrentEntryId})"
            : $"[golem {golem}] release chain no-op — awake at entry {perf.CurrentEntryId}");

        var dispatch = perf.CreateDispatch();

        // Letting go of everything is ONE command: the loop over the pending missions runs inside the
        // actor, one journal entry, each mission abandoned with the operator's reason.
        dispatch.On<EverythingLetGo>((actor, m) =>
        {
            actor.Using(@"
                foreach (id in g.PendingIds()) {
                    g.Abandon(id, @reason);
                }
            ")
            .WithParameters(p => {
                p["reason", typeof(string)] = m.Reason;
            })
            .PerformCommand();
            Console.WriteLine($"[golem {golem}] let go of every pending mission (entry {perf.CurrentEntryId})");
        });

        // Crossing a passage and reaching a stop happen as many times per mission as the road has
        // legs: REPEATED events, so they are plain command handlers (idempotent per message id — one
        // id per leg), NOT saga steps: a saga runs a given step once per key and silently drops a repeat.
        dispatch.On<PassageCrossed>((actor, m) =>
        {
            string refused = actor.Using(
                @"
                    Check(g.Knows(@id) && g.IsPending(@id) && g.NextIsStop(@id) == false && g.NextPassage(@id) == @passage) Error 'that is not the passage ahead';
                ",
                @"
                    g.Cross(@id, @passage);
                ")
            .WithParameters(p => {
                p["id",      typeof(int)]    = m.Id;
                p["passage", typeof(string)] = m.Passage;
            })
            .PerformCheckThenCommand();
            Settle(refused, $"mission {m.Id} crossed {m.Passage}");
        });

        dispatch.On<StopReached>((actor, m) =>
        {
            string refused = actor.Using(
                @"
                    Check(g.Knows(@id) && g.IsPending(@id) && g.NextId() == @id && g.NextIsStop(@id) && g.NextX() == @x && g.NextY() == @y) Error 'that is not the stop ahead';
                ",
                @"
                    g.Reach(@id, @x, @y);
                ")
            .WithParameters(p => {
                p["id", typeof(int)]    = m.Id;
                p["x",  typeof(double)] = m.X;
                p["y",  typeof(double)] = m.Y;
            })
            .PerformCheckThenCommand();
            Settle(refused, $"mission {m.Id} reached the stop ({m.X:0.0}, {m.Y:0.0})");
        });

        // Deciding the road can happen more than once per mission — again after every bump that
        // closed the way — so it is a plain handler too, guarded by the domain's own rule.
        dispatch.On<MissionRouted>((actor, m) =>
        {
            string refused = actor.Using(
                @"
                    Check(g.Knows(@id) && g.IsPending(@id) && (g.IsRouted(@id) == false || g.HasBumpedSinceRoute(@id))) Error 'mission already has its road';
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
        });

        // A bump: the body touched something the map does not hold. Repeated per touch.
        dispatch.On<MissionBumped>((actor, m) =>
        {
            string refused = actor.Using(
                @"
                    Check(g.Knows(@id) && g.IsPending(@id)) Error 'mission is not pending';
                ",
                @"
                    g.Bump(@id, @x, @y);
                ")
            .WithParameters(p => {
                p["id", typeof(int)]    = m.Id;
                p["x",  typeof(double)] = m.X;
                p["y",  typeof(double)] = m.Y;
            })
            .PerformCheckThenCommand();
            Settle(refused, $"mission {m.Id} bumped into something at ({m.X:0.0}, {m.Y:0.0}): a mark on the map");
        });

        // Once-per-mission decisions are steps of ONE run per mission: a Saga keyed by mission id
        // serializes them per key; a domain Check on each step is the real guard against a
        // redelivered or repeated step.
        perf.DefineSaga("Mission")
            .On<MissionAbandoned>(m => m.Id.ToString(CultureInfo.InvariantCulture))
                .Task("abandon", (actor, m) =>
                {
                    string refused = actor.Using(
                        @"
                            Check(g.Knows(@id) && g.IsPending(@id) && g.IsFollowing(@id) && g.HasNewerFollowing(@id)) Error 'nothing newer was told';
                        ",
                        @"
                            g.Abandon(@id, @reason);
                        ")
                    .WithParameters(p => {
                        p["id",     typeof(int)]    = m.Id;
                        p["reason", typeof(string)] = m.Reason;
                    })
                    .PerformCheckThenCommand();
                    Settle(refused, $"mission {m.Id} abandoned: {m.Reason}");
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

        // Uptake: whatever stop a peer reached becomes a mission of MY own — the follower's verb
        // (Follow mints the handle inside), one journaled perform per tell. Only plain @params here:
        // a nested call as an argument faults the reaction matcher.
        toldListener = perf
            .ListenAs(golem, bindings, wire)
            .Told("PointVisited").With<double>("x").With<double>("y")
                .Command("g.Follow(@x, @y);")
            .Told("ObstacleFound").With<double>("x").With<double>("y")
                .Command("g.Learn(@x, @y);")
            .Start();
        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "",
            $"listening for tells as '{golem}' on topic 'tell-{golem}'", DateTime.UtcNow));
        Console.WriteLine($"[golem {golem}] listening for tells on topic 'tell-{golem}'");
        if (tellDoneTo != null)
            Console.WriteLine($"[golem {golem}] will tell '{tellDoneTo}' every stop it reaches");
    }

    // The boundary of the ops surface: the producer's "kind" header becomes the
    // dispatch tag here, and anything unknown is dropped (null).
    private static DispatchCommand? Route(InputSignal signal)
    {
        if (!signal.Headers.TryGetValue("kind", out string kind)) return null;
        int tag = kind switch
        {
            "routed"    => MissionRouted.TypeId,
            "crossed"   => PassageCrossed.TypeId,
            "reached"   => StopReached.TypeId,
            "bumped"    => MissionBumped.TypeId,
            "abandoned" => MissionAbandoned.TypeId,
            "failed"    => MissionFailed.TypeId,
            "letgo"     => EverythingLetGo.TypeId,
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
    // back on its mark (ephemeral, a lab lever), and the letting go is journaled through the
    // same ops surface — every pending mission abandoned, with the reason.
    public async Task LetGoAsync()
    {
        try
        {
            await ros.DriveAsync(0, 0, CancellationToken.None);
            await ros.TeleportAsync(home.X, home.Y, 0.0, CancellationToken.None);
        }
        catch { /* the world reset is best-effort; the journaled fact is the point */ }

        Produce("letgo", $"{golem}:letgo:{DateTime.UtcNow.Ticks}", EverythingLetGo.Payload("the operator let go of everything"));
        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", "letting go of every pending mission", DateTime.UtcNow));
    }

    // The operator's hard reset — a LAB lever, not a domain fact: stop the body, wipe THIS
    // golem's journal and exit; Docker restarts the container and the golem is born again at
    // entry 1 (the reborn boot puts the body back on its mark). With cascade, every peer is
    // asked to do the same: the world is shared, and the hearer dedups the sender's once-ids
    // ('visited-N-x,y'), so resetting one side alone would make the peer swallow the next tells
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
    // The mission loop. A mission is one or more stops; before moving, the golem decides its
    // ROAD from where the body stands — the passages to cross, the stops to reach, in order (its
    // own order, for a Cover) — and journals it (g.Route). Then the navigator is handed one leg
    // at a time: each passage crossed is journaled (g.Cross), each stop reached is journaled
    // (g.Reach), and the last stop reached completes the mission. What counts as failure on a leg
    // (a collision reported by the world, a stall, a timeout) is the navigator's to say; the golem
    // journals the verdict with its reason.
    // ------------------------------------------------------------------
    public async Task RunAsync(CancellationToken ct)
    {
        int announcedFor = 0;
        string announcedLeg = null;
        int wallBumps = 0;     // known walls bumped on the current leg
        int yields = 0;        // times the golem waited for a peer on the current leg
        Probe probe = null;    // the feeling-past state on the current leg, after a bump into the unknown
        while (!ct.IsCancellationRequested)
        {
            var plan = ReadPlan();
            if (!plan.Has)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                continue;
            }
            string key = $"{golem}:mission:{plan.Id}";

            // Catching up: a followed point still pending when a NEWER one arrived is where the leader
            // WAS, not where it is. The follower lets it go (a journaled decision) and heads for the
            // newest one by the shortest road — checked between legs, never mid-leg.
            if (plan.Following && plan.Newer > 0)
            {
                Console.WriteLine($"[golem {golem}] mission {plan.Id}: a newer told point ({plan.Newer}) arrived — letting this one go");
                feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", $"mission {plan.Id}: superseded by {plan.Newer} — catching up with the leader", DateTime.UtcNow));
                Produce("abandoned", $"{key}:abandoned", MissionAbandoned.Payload(plan.Id, $"superseded by mission {plan.Newer}"));
                if (!await WaitUntilAsync(() => IsSettled(plan.Id), ct)) await Task.Delay(TimeSpan.FromSeconds(2), ct);
                continue;
            }

            // The road is decided when the mission comes up — and decided AGAIN when the body bumped into
            // something on it and feeling past it, right and left, found no way: the marks are on the map
            // now, and the next road skirts them or goes round; when no road fits the body, the mission fails.
            bool feltEverything = probe != null && probe.MissionId == plan.Id && probe.Exhausted;
            if (!plan.Routed || (plan.BumpedSinceRoute && (probe == null || probe.MissionId != plan.Id || feltEverything)))
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
                string verb = plan.Routed ? "another road" : "road";
                Console.WriteLine($"[golem {golem}] mission {plan.Id}: {verb} {road}");
                feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", $"mission {plan.Id}: {verb} {road}", DateTime.UtcNow));
                Produce("routed", $"{key}:routed:{plan.Bumps}", MissionRouted.Payload(plan.Id, road));
                await WaitUntilAsync(() => IsRouted(plan.Id) && !HasBumpedSinceRoute(plan.Id), ct);
                probe = null;
                continue;
            }

            // The last stop of a followed mission is met a body's length short: the leader may still be there.
            bool standoff = plan.Following && plan.LegsLeft == 1;
            if (announcedFor != plan.Id || announcedLeg != plan.Passage)
            {
                announcedFor = plan.Id;
                announcedLeg = plan.Passage;
                wallBumps = 0;
                yields = 0;
                probe = null;
                string what = plan.IsStop ? $"heading to the stop in {plan.Passage}"
                            : plan.Passage == "around" ? "heading around a mark" : $"heading to the passage {plan.Passage}";
                if (standoff) what += $", stopping {LeaderStandoff:0.0} short of the leader's spot";
                Console.WriteLine($"[golem {golem}] mission {plan.Id}: {what} ({plan.X:0.0}, {plan.Y:0.0})");
                feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", $"mission {plan.Id}: {what} ({plan.X:0.0}, {plan.Y:0.0})", DateTime.UtcNow));
            }

            // A door is crossed straight: line up in front of it, then run through to the far side.
            // The map says where those two points are; an opening or a stop is a single run.
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
                    // The world said "you touched something". The golem holds the point against ITS map.
                    // A wall it knows is its own execution error — it has backed off, so it lines up and
                    // tries the leg again (runtime, nothing to journal) until patience runs out.
                    string where = $"({outcome.Hit.X:0.0}, {outcome.Hit.Y:0.0})";
                    if (KnowsWallAt(outcome.Hit.X, outcome.Hit.Y))
                    {
                        if (wallBumps < RetriesAfterBump)
                        {
                            wallBumps++;
                            string retry = $"mission {plan.Id}: bumped into {outcome.Hit.With} at {where}, a wall I know — recovering, try {wallBumps}/{RetriesAfterBump}";
                            Console.WriteLine($"[golem {golem}] {retry}");
                            feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", retry, DateTime.UtcNow));
                            continue;
                        }
                        reason = $"still bumping into {outcome.Hit.With} at {where} after {RetriesAfterBump} tries";
                    }
                    else if (peers.Contains(outcome.Hit.With) || outcome.Hit.With == tellDoneTo)
                    {
                        // A peer: a body the golem knows by name (its fleet), and one that moves. Not a mark —
                        // yield a while and try the leg again; give the mission up only after several yields.
                        if (yields < MaxYields)
                        {
                            yields++;
                            string wait = $"mission {plan.Id}: bumped into {outcome.Hit.With} at {where}, a peer — yielding {Yield.TotalSeconds:0} s, {yields}/{MaxYields}";
                            Console.WriteLine($"[golem {golem}] {wait}");
                            feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", wait, DateTime.UtcNow));
                            await Task.Delay(Yield, ct);
                            continue;
                        }
                        reason = $"blocked by {outcome.Hit.With} at {where} after yielding {MaxYields} times";
                    }
                    else
                    {
                        // Something the map does not hold: a mark on the map (journaled, told to the peers),
                        // then the golem feels for a way past it. A step aside found: the leg again from there.
                        // Both sides given up: the loop above decides the road anew with the marks it has.
                        await BumpAsync(plan.Id, key, outcome.Hit, ct);
                        probe ??= new Probe(plan.Id, plan.Passage, ros.LatestPose?.Theta ?? 0);
                        if (await FeelForAWayPastAsync(plan.Id, key, probe, ct)) continue;
                        string give = $"mission {plan.Id}: no way past by feel, {probe.RightSteps} steps right and {probe.LeftSteps} left — deciding the road again with {Marks()} marks";
                        Console.WriteLine($"[golem {golem}] {give}");
                        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", give, DateTime.UtcNow));
                        continue;
                    }
                }
                Produce("failed", $"{key}:failed", MissionFailed.Payload(plan.Id, reason));
                if (!await WaitUntilAsync(() => IsSettled(plan.Id), ct)) await Task.Delay(TimeSpan.FromSeconds(2), ct);
                continue;
            }

            // One message id per leg — of THIS road: a road decided again after bumps counts its legs anew,
            // so the bump count tells the roads apart.
            if (!plan.IsStop)
            {
                Produce("crossed", $"{key}:b{plan.Bumps}:cross:{plan.LegsLeft}", PassageCrossed.Payload(plan.Id, plan.Passage));
                await WaitUntilAsync(() => LegsLeft(plan.Id) < plan.LegsLeft, ct);
                continue;
            }

            bool last = plan.LegsLeft == 1;
            Produce("reached", $"{key}:b{plan.Bumps}:reach:{plan.LegsLeft}", StopReached.Payload(plan.Id, plan.X, plan.Y));
            if (!await WaitUntilAsync(() => last ? IsSettled(plan.Id) : LegsLeft(plan.Id) < plan.LegsLeft, ct))
                await Task.Delay(TimeSpan.FromSeconds(2), ct); // never re-drive on a timeout; back off and re-read
            if (!last) continue;

            ReportLocalization(plan.Id);

            // Pacing: a stop a peer told us about is a stop that peer is already past. Lingering there
            // a while keeps the leader's lead. How long is the golem's own property (the init release);
            // the pause itself is runtime — the mission is settled and nothing about the wait belongs
            // in the journal.
            if (plan.Following)
            {
                var hold = TimeSpan.FromSeconds(LingerAfterTold());
                if (hold > TimeSpan.Zero)
                {
                    Console.WriteLine($"[golem {golem}] lingering {hold.TotalSeconds:0} s at ({plan.X:0.0}, {plan.Y:0.0}) to keep the leader's lead");
                    feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "",
                        $"lingering {hold.TotalSeconds:0} s at ({plan.X:0.0}, {plan.Y:0.0}) to keep the leader's lead", DateTime.UtcNow));
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

    // ------------------------------------------------------------------
    // Feeling past the unknown. A mark is journaled where the body touched; then, standing where it
    // backed off to, the body steps aside — right first, then left — one body's width at a time and
    // tries the leg again from there. A side is given up when the step would not fit (a wall, a mark,
    // the edge of the map), when the step itself touches something, or after MaxSideSteps. Both sides
    // given up means the gap, if any, is narrower than the body: time to decide the road again.
    // ------------------------------------------------------------------
    private sealed class Probe
    {
        internal Probe(int missionId, string leg, double heading) { MissionId = missionId; Leg = leg; Heading = heading; }
        internal int MissionId { get; }
        internal string Leg { get; }
        /// <summary>Where the nose pointed at the first bump: "right", "left" and "ahead" are taken from it.</summary>
        internal double Heading { get; }
        internal int RightSteps, LeftSteps;
        internal bool RightDone, LeftDone;
        internal bool Exhausted => RightDone && LeftDone;
    }

    // How far the body runs ahead in the lane it stepped into, before trying the leg again: enough to
    // be past a mark's reach and its own body, so a thing the size of itself is left behind.
    private const double AheadRun = 1.2;   // m

    private async Task BumpAsync(int id, string key, Collision hit, CancellationToken ct)
    {
        int before = Bumps(id);
        string note = $"mission {id}: bumped into {hit.With} at ({hit.X:0.0}, {hit.Y:0.0}) — nothing on my map there: a mark";
        Console.WriteLine($"[golem {golem}] {note}");
        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", note, DateTime.UtcNow));
        Produce("bumped", $"{key}:bump:{before + 1}", MissionBumped.Payload(id, hit.X, hit.Y));
        await WaitUntilAsync(() => Bumps(id) > before, ct);
    }

    // True when a step aside succeeded (the leg is worth trying again from there); false when both sides are given up.
    private async Task<bool> FeelForAWayPastAsync(int id, string key, Probe probe, CancellationToken ct)
    {
        while (!probe.Exhausted && !ct.IsCancellationRequested)
        {
            bool right = !probe.RightDone;
            if (right && probe.RightSteps >= MaxSideSteps) { probe.RightDone = true; continue; }
            if (!right && probe.LeftSteps >= MaxSideSteps + probe.RightSteps) { probe.LeftDone = true; continue; }

            var pose = ros.LatestPose;
            if (pose == null) return false;
            // a body's width to the side of where the nose pointed at the bump: right is (sin θ, -cos θ)
            double th = probe.Heading;
            double tx = pose.X + SideStep * (right ? Math.Sin(th) : -Math.Sin(th));
            double ty = pose.Y + SideStep * (right ? -Math.Cos(th) : Math.Cos(th));
            string side = right ? "right" : "left";
            // room is measured against the WALLS only: the mark just made is what is being felt around
            if (!HasRoomAt(tx, ty))
            {
                string blocked = $"mission {id}: no room for a step {side} to ({tx:0.0}, {ty:0.0}): a wall there";
                Console.WriteLine($"[golem {golem}] {blocked}");
                feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", blocked, DateTime.UtcNow));
                if (right) probe.RightDone = true; else probe.LeftDone = true;
                continue;
            }

            string trying = $"mission {id}: feeling for a way past — a step {side} to ({tx:0.0}, {ty:0.0})";
            Console.WriteLine($"[golem {golem}] {trying}");
            feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", trying, DateTime.UtcNow));
            var step = await navigator.GoToAsync(tx, ty, 0.12, ct);
            if (right) probe.RightSteps++; else probe.LeftSteps++;
            if (!step.Reached)
            {
                // the step itself met something: if it was unknown (and not a peer), that is a mark too; either way this side is done
                if (step.Hit != null && !KnowsWallAt(step.Hit.X, step.Hit.Y) && !IsPeer(step.Hit.With))
                    await BumpAsync(id, key, step.Hit, ct);
                if (right) probe.RightDone = true; else probe.LeftDone = true;
                continue;
            }

            // in the new lane, run ahead past where the thing was before going back to the leg;
            // a hit here is another mark and the end of this side, a clear run means "try the leg again"
            double ax = tx + AheadRun * Math.Cos(th), ay = ty + AheadRun * Math.Sin(th);
            if (!HasRoomAt(ax, ay)) return true;   // no lane ahead: still, the leg is worth trying from here
            var run = await navigator.GoToAsync(ax, ay, 0.2, ct);
            if (run.Reached) return true;
            if (run.Hit != null && !KnowsWallAt(run.Hit.X, run.Hit.Y) && !IsPeer(run.Hit.With))
                await BumpAsync(id, key, run.Hit, ct);
            if (right) probe.RightDone = true; else probe.LeftDone = true;
        }
        return false;
    }

    private bool IsPeer(string name) => peers.Contains(name) || name == tellDoneTo;

    // The localization experiment's readout: when the golem lives on dead reckoning, say — at every
    // mission's end, in the runtime lane only — where it believes it stands and where the world says
    // it stands. The journal already holds the last stop reached; whether that is TRUE of the world
    // is not the golem's to know.
    private void ReportLocalization(int id)
    {
        if (ros.Source != PoseSource.Wheels) return;
        var believed = ros.LatestPose;
        var truth = ros.LatestTruth;
        if (believed == null || truth == null) return;
        double off = Math.Sqrt((believed.X - truth.X) * (believed.X - truth.X) + (believed.Y - truth.Y) * (believed.Y - truth.Y));
        string note = $"mission {id}: the body believes it stands at ({believed.X:0.00}, {believed.Y:0.00}); the world says ({truth.X:0.00}, {truth.Y:0.00}) — {off:0.00} m apart";
        Console.WriteLine($"[golem {golem}] {note}");
        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", note, DateTime.UtcNow));
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
             bool Routed, int LegsLeft, string Passage, bool IsStop, bool Following, int Newer, int Bumps, bool BumpedSinceRoute) ReadPlan()
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
                @stop = g.NextIsStop(g.NextId());
                @following = g.IsFollowing(g.NextId());
                @newer = 0;
                if (g.HasNewerFollowing(g.NextId())) { @newer = g.NewestFollowingId(); }
                @bumps = g.Bumps(g.NextId());
                @bumped = g.HasBumpedSinceRoute(g.NextId());
            }
        ")
        .WithParameters(rented, p => {
            p[Parameter.Out, "has",       typeof(bool)]   = default;
            p[Parameter.Out, "id",        typeof(int)]    = default;
            p[Parameter.Out, "x",         typeof(double)] = default;
            p[Parameter.Out, "y",         typeof(double)] = default;
            p[Parameter.Out, "ax",        typeof(double)] = default;
            p[Parameter.Out, "ay",        typeof(double)] = default;
            p[Parameter.Out, "ex",        typeof(double)] = default;
            p[Parameter.Out, "ey",        typeof(double)] = default;
            p[Parameter.Out, "routed",    typeof(bool)]   = default;
            p[Parameter.Out, "legs",      typeof(int)]    = default;
            p[Parameter.Out, "passage",   typeof(string)] = default;
            p[Parameter.Out, "stop",      typeof(bool)]   = default;
            p[Parameter.Out, "following", typeof(bool)]   = default;
            p[Parameter.Out, "newer",     typeof(int)]    = default;
            p[Parameter.Out, "bumps",     typeof(int)]    = default;
            p[Parameter.Out, "bumped",    typeof(bool)]   = default;
        })
        .PerformQuery();
        return (rented["has"].GetValue<bool>(), rented["id"].GetValue<int>(),
                rented["x"].GetValue<double>(), rented["y"].GetValue<double>(),
                rented["ax"].GetValue<double>(), rented["ay"].GetValue<double>(),
                rented["ex"].GetValue<double>(), rented["ey"].GetValue<double>(),
                rented["routed"].GetValue<bool>(), rented["legs"].GetValue<int>(),
                rented["passage"].GetValue<string>() ?? "", rented["stop"].GetValue<bool>(),
                rented["following"].GetValue<bool>(), rented["newer"].GetValue<int>(),
                rented["bumps"].GetValue<int>(), rented["bumped"].GetValue<bool>());
    }

    private int Bumps(int id)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @bumps = g.Bumps(@id);
        ")
        .WithParameters(rented, p => {
            p["id", typeof(int)]                    = id;
            p[Parameter.Out, "bumps", typeof(int)]  = default;
        })
        .PerformQuery();
        return rented["bumps"].GetValue<int>();
    }

    private bool HasBumpedSinceRoute(int id)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @bumped = g.HasBumpedSinceRoute(@id);
        ")
        .WithParameters(rented, p => {
            p["id", typeof(int)]                      = id;
            p[Parameter.Out, "bumped", typeof(bool)]  = default;
        })
        .PerformQuery();
        return rented["bumped"].GetValue<bool>();
    }

    private int Marks()
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @marks = g.MarkCount();
        ")
        .WithParameters(rented, p => {
            p[Parameter.Out, "marks", typeof(int)] = default;
        })
        .PerformQuery();
        return rented["marks"].GetValue<int>();
    }

    // Do the walls leave room for my body at this point? (Marks not counted: they are what I feel around.)
    private bool HasRoomAt(double x, double y)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @room = g.HasRoomAt(@x, @y);
        ")
        .WithParameters(rented, p => {
            p["x", typeof(double)]                  = x;
            p["y", typeof(double)]                  = y;
            p[Parameter.Out, "room", typeof(bool)]  = default;
        })
        .PerformQuery();
        return rented["room"].GetValue<bool>();
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

    private double LingerAfterTold()
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @linger = g.LingerAfterTold();
        ")
        .WithParameters(rented, p => {
            p[Parameter.Out, "linger", typeof(double)] = default;
        })
        .PerformQuery();
        return rented["linger"].GetValue<double>();
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
