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

    // Bumping into a wall the golem KNOWS is its own execution error (a Graze, journaled): it backs off,
    // lines up and tries the leg again while the DOMAIN says it may (g.MayRetryLeg: its patience on the leg).

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

        // What the body bumps into is news for every peer, twice over. First the touch itself, with my
        // name: a peer that bumped there and then knows it met ME, not a thing. Then, once I conclude it
        // was a thing, the mark: something they can plan around without the bruise. One tell per peer in
        // one entry (several statements: no single-tell elision), one once-id per addressee (the same id
        // twice would make the second tell a duplicate).
        if (peers.Count > 0)
        {
            // The bump command exposes the point and my name onto its own entry (a literal in a
            // `with` clause has no name a peer could bind): one match serves a bump on a mission's
            // road and a touch while standing idle alike.
            string bumped = string.Join("\n", peers.Select(p =>
                $"tell BumpedAt with @x, @y, @who to {p} once 'bump-' + @who + '-' + @x + ',' + @y + '-{p}';"));
            perf.Actor.Reactions.DefineReaction("echo-bumped")
                .Cue().Company().WithSharedHydration()
                .Seek("Bumped").One()
                    .OnMatch(@"
                        expose $x x, $y y, $who who;
                    ")
                .Causation.Continue(bumped);

            string marked = string.Join("\n", peers.Select(p =>
                $"tell ObstacleFound with @x, @y, @heading to {p} once 'obstacle-{golem}-' + @x + ',' + @y + '-{p}';"));
            perf.Actor.Reactions.DefineReaction("echo-marked")
                .Cue().Company().WithSharedHydration()
                .Seek("Marked").One()
                    .OnMatch(@"
                        [_:Golem].Mark($x, $y, $heading)
                    ")
                .Causation.Continue(marked);
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
                    Check(g.Knows(@id) && g.IsPending(@id) && g.OrderIsStop(@id) == false && g.OrderPassage(@id) == @passage) Error 'that is not the passage ahead';
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
                    Check(g.Knows(@id) && g.IsPending(@id) && g.OrderIsStop(@id) && g.OrderX(@id) == @x && g.OrderY(@id) == @y) Error 'that is not the stop ahead';
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

        // The order: where the body must drive next. The host does not choose it — it reads the point the
        // golem's queue holds next and writes it down, so the drive is told by the journal and not by this loop.
        // Repeating the same point is legal: after a touch the golem may hand the same order back.
        dispatch.On<MissionOrdered>((actor, m) =>
        {
            string refused = actor.Using(
                @"
                    Check(g.Knows(@id) && g.IsPending(@id) && g.HasNextPoint(@id)) Error 'no point to head to';
                ",
                @"
                    g.MoveTo(@id, @x, @y);
                ")
            .WithParameters(p => {
                p["id", typeof(int)]    = m.Id;
                p["x",  typeof(double)] = m.X;
                p["y",  typeof(double)] = m.Y;
            })
            .PerformCheckThenCommand();
            Settle(refused, $"mission {m.Id}: heading to ({m.X:0.0}, {m.Y:0.0})");
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

        // A bump: the body touched something the map does not hold — on a mission's road, or standing idle
        // (id 0). Repeated per touch. What it was is settled afterwards, by what the peers say.
        dispatch.On<MissionBumped>((actor, m) =>
        {
            if (m.Id == 0)
            {
                actor.Using(@"
                    g.Bump(@x, @y, @heading);
                    expose @x x, @y y, @me who;
                ")
                .WithParameters(p => {
                    p["x",       typeof(double)] = m.X;
                    p["y",       typeof(double)] = m.Y;
                    p["heading", typeof(double)] = m.Heading;
                    p["me",      typeof(string)] = golem;
                })
                .PerformCommand();
                Console.WriteLine($"[golem {golem}] touched at ({m.X:0.0}, {m.Y:0.0}) while standing idle (entry {perf.CurrentEntryId})");
                return;
            }
            string refused = actor.Using(
                @"
                    Check(g.Knows(@id) && g.IsPending(@id)) Error 'mission is not pending';
                ",
                @"
                    g.Bump(@id, @x, @y, @heading);
                    expose @x x, @y y, @me who;
                ")
            .WithParameters(p => {
                p["id",      typeof(int)]    = m.Id;
                p["x",       typeof(double)] = m.X;
                p["y",       typeof(double)] = m.Y;
                p["heading", typeof(double)] = m.Heading;
                p["me",      typeof(string)] = golem;
            })
            .PerformCheckThenCommand();
            Settle(refused, $"mission {m.Id} bumped into something at ({m.X:0.0}, {m.Y:0.0})");
        });

        // The conclusions of a touch, in the golem's voice — the DOMAIN suspected (g.Suspect), the host only writes
        // what it named. A mark: it was a thing (nobody else bumped there and then); the heading is the mark's normal.
        dispatch.On<ObstacleMarked>((actor, m) =>
        {
            actor.Using(@"
                g.Mark(@x, @y, @heading);
            ")
            .WithParameters(p => {
                p["x",       typeof(double)] = m.X;
                p["y",       typeof(double)] = m.Y;
                p["heading", typeof(double)] = m.Heading;
            })
            .PerformCommand();
            Console.WriteLine($"[golem {golem}] marked a thing at ({m.X:0.0}, {m.Y:0.0}) (entry {perf.CurrentEntryId})");
        });

        // It was a peer: the body met another body there. History, told to nobody (the peer lived it too).
        dispatch.On<PeerMet>((actor, m) =>
        {
            actor.Using(@"
                g.Met(@who, @x, @y);
            ")
            .WithParameters(p => {
                p["who", typeof(string)] = m.Who;
                p["x",   typeof(double)] = m.X;
                p["y",   typeof(double)] = m.Y;
            })
            .PerformCommand();
            Console.WriteLine($"[golem {golem}] met {m.Who} at ({m.X:0.0}, {m.Y:0.0}) (entry {perf.CurrentEntryId})");
        });

        // It was a wall the golem knows: a graze, its own execution error, counted against its patience on the leg.
        dispatch.On<MissionGrazed>((actor, m) =>
        {
            string refused = actor.Using(
                @"
                    Check(g.Knows(@id) && g.IsPending(@id)) Error 'mission is not pending';
                ",
                @"
                    g.Graze(@id, @x, @y);
                ")
            .WithParameters(p => {
                p["id", typeof(int)]    = m.Id;
                p["x",  typeof(double)] = m.X;
                p["y",  typeof(double)] = m.Y;
            })
            .PerformCheckThenCommand();
            Settle(refused, $"mission {m.Id} grazed a wall it knows at ({m.X:0.0}, {m.Y:0.0})");
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
            .Told("BumpedAt").With<double>("x").With<double>("y").With<string>("who")
                .Command("g.HearBump(@who, @x, @y);")
            .Told("ObstacleFound").With<double>("x").With<double>("y").With<double>("heading")
                .Command("g.LearnMark(@x, @y, @heading);")
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
            "ordered"   => MissionOrdered.TypeId,
            "crossed"   => PassageCrossed.TypeId,
            "reached"   => StopReached.TypeId,
            "bumped"    => MissionBumped.TypeId,
            "marked"    => ObstacleMarked.TypeId,
            "met"       => PeerMet.TypeId,
            "grazed"    => MissionGrazed.TypeId,
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
        int yields = 0;        // times the golem waited for a peer on the current leg
        int marksAtRoute = -1; // how many marks the map held when the current road was decided
        bool gaveWay = false;  // the body moved off its road to let a peer pass: the road is decided again from where it stands
        int orders = 0;        // orders written, so a repeated point after a touch is not taken for a redelivery
        Probe probe = null;    // the feeling-past state on the current leg, after a bump into the unknown
        while (!ct.IsCancellationRequested)
        {
            var plan = ReadPlan();
            if (!plan.Has)
            {
                await MakeRoomIfBumpedAsync(ct);
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
            // something on it, the map holds marks it did not hold when the road was decided (a bump that
            // turned out to be a peer leaves none: same road), and feeling past it, right and left, found
            // no way: the next road skirts the marks or goes round; when no road fits the body, the mission fails.
            bool feltEverything = probe != null && probe.MissionId == plan.Id && probe.Exhausted;
            bool newMarks = marksAtRoute < 0 || Marks() != marksAtRoute;
            bool reRoute = plan.BumpedSinceRoute && (newMarks || gaveWay) && (probe == null || probe.MissionId != plan.Id || feltEverything);
            if (!plan.Routed || reRoute)
            {
                var here = ros.LatestPose;
                if (here == null) { await Task.Delay(200, ct); continue; }
                // Is there a road to decide at all? The domain answers with the pose the host carries: an errand
                // one segment away has none — the golem heads straight there and no road is written.
                if (!plan.Routed && !reRoute && !NeedsRoad(plan.Id, here.X, here.Y))
                {
                    // nothing to decide: fall through and say where to drive
                }
                else
                {
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
                marksAtRoute = Marks();
                gaveWay = false;
                probe = null;
                continue;
                }
            }

            // The golem says where the body must go: the point its queue holds next. The host reads it and
            // writes it down; it never picks one. A touch voids the standing order, so this is also how the
            // drive resumes after a graze or a bump.
            if (!plan.Ordered)
            {
                orders++;
                Produce("ordered", $"{key}:b{plan.Bumps}:order:{orders}", MissionOrdered.Payload(plan.Id, plan.X, plan.Y));
                if (!await WaitUntilAsync(() => IsOrdered(plan.Id), ct)) await Task.Delay(TimeSpan.FromSeconds(1), ct);
                continue;
            }

            // The last stop of a followed mission is met a body's length short: the leader may still be there.
            bool standoff = plan.Following && plan.LegsLeft == 1;
            if (announcedFor != plan.Id || announcedLeg != plan.Passage)
            {
                announcedFor = plan.Id;
                announcedLeg = plan.Passage;
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
            heardAtDriveStart = HeardBumpCount();
            var from = ros.LatestPose;
            if (from != null)
                lane = (plan.ApproachX != plan.ExitX || plan.ApproachY != plan.ExitY)
                    ? Math.Atan2(plan.ExitY - plan.ApproachY, plan.ExitX - plan.ApproachX)
                    : Math.Atan2(plan.ExitY - from.Y, plan.ExitX - from.X);
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
                    // The world said "you touched something". The DOMAIN says what it suspects (a wall it knows, a
                    // peer that spoke, a thing) and names the conclusion; the host only waits, asks and writes it.
                    string where = $"({outcome.Hit.X:0.0}, {outcome.Hit.Y:0.0})";
                    var first = Suspect(outcome.Hit, heardAtDriveStart);
                    if (first.Kind == "wall")
                    {
                        // A wall I know: my own execution error. The host reports it and waits: the golem either hands
                        // the order back (line up and try the leg again) or ends the mission. Neither is the host's call.
                        int grazesBefore = Grazes(plan.Id);
                        string note = $"mission {plan.Id}: grazed {outcome.Hit.With} at {where}, a wall I know — telling the golem";
                        Console.WriteLine($"[golem {golem}] {note}");
                        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", note, DateTime.UtcNow));
                        Produce("grazed", $"{key}:b{plan.Bumps}:graze:{grazesBefore + 1}",
                                MissionGrazed.Payload(plan.Id, outcome.Hit.X, outcome.Hit.Y));
                        await WaitUntilAsync(() => Grazes(plan.Id) > grazesBefore, ct);
                        // the golem decides whether its patience with its own error is spent; the host obeys
                        if (MayRetryLeg(plan.Id)) continue;
                        reason = $"still grazing {outcome.Hit.With} at {where} after {Grazes(plan.Id)} grazes: patience spent";
                    }
                    else
                    {
                        // Something the map does not hold. The touch is journaled and told; then the golem waits for
                        // the peers to speak and asks the domain what it suspects. A peer: met, coordinate. Nobody: a
                        // thing — marked (told), and the golem feels for a way past it; both sides given up, the loop
                        // above decides the road anew.
                        string who = await BumpAndListenAsync(plan.Id, key, outcome.Hit, ct);
                        if (who != "")
                        {
                            if (yields < MaxYields)
                            {
                                yields++;
                                if (await CoordinateWithAsync(plan.Id, who, where, yields, ct)) gaveWay = true;
                                continue;
                            }
                            reason = $"blocked by {who} at {where} after yielding {MaxYields} times";
                        }
                        else
                        {
                            probe ??= new Probe(plan.Id, plan.Passage, outcome.Hit.Heading);
                            if (await FeelForAWayPastAsync(plan.Id, key, probe, ct)) continue;
                            string give = $"mission {plan.Id}: no way past by feel, {probe.RightSteps} steps right and {probe.LeftSteps} left — deciding the road again with {Marks()} marks";
                            Console.WriteLine($"[golem {golem}] {give}");
                            feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", give, DateTime.UtcNow));
                            continue;
                        }
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
            // in the journal. And the follower pulls over first: its standoff spot lies on the line the
            // leader walked in on, which is the line the leader walks out on.
            if (plan.Following)
            {
                await PullOverAsync(ct);
                var hold = TimeSpan.FromSeconds(LingerAfterTold());
                if (hold > TimeSpan.Zero)
                {
                    Console.WriteLine($"[golem {golem}] lingering {hold.TotalSeconds:0} s at ({plan.X:0.0}, {plan.Y:0.0}) to keep the leader's lead");
                    feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "",
                        $"lingering {hold.TotalSeconds:0} s at ({plan.X:0.0}, {plan.Y:0.0}) to keep the leader's lead", DateTime.UtcNow));
                    await StandAsync(hold, ct);
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

    // How long the golem listens, after telling its bump, for a peer telling a bump there and then.
    private static readonly TimeSpan Listen = TimeSpan.FromMilliseconds(2500);

    // The touch protocol, second version (Juan, 8-sep: "the domain decides, the host follows"): journal the bump
    // (the reaction tells every peer, with my name), wait for the peers to speak, then ask the DOMAIN what it
    // suspects — a peer that bumped near there and then, or a thing — and journal the conclusion it names: Met
    // (history) or Mark (a thing with the touch's heading as its normal; the reaction tells the peers, who learn
    // it). Returns the peer's name, or "".
    private int heardAtDriveStart;   // peers' bumps heard before the current drive began are older news than this touch

    private async Task<string> BumpAndListenAsync(int id, string key, Collision hit, CancellationToken ct)
    {
        int since = heardAtDriveStart;
        int before = Bumps(id);
        string note = $"mission {id}: bumped into something at ({hit.X:0.0}, {hit.Y:0.0}) heading {hit.Heading:0.00} — nothing on my map there; telling the peers and listening";
        Console.WriteLine($"[golem {golem}] {note}");
        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", note, DateTime.UtcNow));
        Produce("bumped", $"{key}:bump:{before + 1}", MissionBumped.Payload(id, hit.X, hit.Y, hit.Heading));
        await WaitUntilAsync(() => Bumps(id) > before, ct);

        // the host owns the clock: the peers get the window to speak, then the domain is asked once
        var until = DateTime.UtcNow + Listen;
        var suspicion = Suspect(hit, since);
        while (suspicion.Kind != "peer" && DateTime.UtcNow < until && !ct.IsCancellationRequested)
        {
            await Task.Delay(250, ct);
            suspicion = Suspect(hit, since);
        }
        if (suspicion.Kind == "peer")
        {
            int metBefore = MetCount();
            string peer = $"mission {id}: the domain suspects {suspicion.Who} — it bumped there too: a body, not a thing ({suspicion.Conclusion})";
            Console.WriteLine($"[golem {golem}] {peer}");
            feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", peer, DateTime.UtcNow));
            Produce("met", $"{golem}:met:{suspicion.Who}:{hit.X:0.00},{hit.Y:0.00}:{DateTime.UtcNow.Ticks}", PeerMet.Payload(suspicion.Who, hit.X, hit.Y));
            await WaitUntilAsync(() => MetCount() > metBefore, ct);
            return suspicion.Who;
        }
        int marksBefore = Marks();
        string mark = $"mission {id}: the domain suspects a thing — nobody else bumped there: a mark at ({hit.X:0.0}, {hit.Y:0.0}) with its normal, told to the peers ({suspicion.Conclusion})";
        Console.WriteLine($"[golem {golem}] {mark}");
        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", mark, DateTime.UtcNow));
        Produce("marked", $"{golem}:mark:{hit.X:0.00},{hit.Y:0.00}:{DateTime.UtcNow.Ticks}", ObstacleMarked.Payload(hit.X, hit.Y, hit.Heading));
        // wait for the mark to be applied (a touch within a tenth of a unit of an old mark adds none: then the wait times out)
        await WaitUntilAsync(() => Marks() > marksBefore, ct);
        return "";
    }

    // Two bodies met. No talk needed once both know it: the one whose name sorts first has the way and
    // waits a moment for the other to clear; the other gives way — backs out the way it came (a doorway
    // is only free when the body leaves it), then steps off the line it was on, so the first can pass
    // and go on to wherever it was going — and waits long enough for it to pass.
    private static readonly TimeSpan WaitForTheWay = TimeSpan.FromSeconds(4);
    private static readonly TimeSpan GiveWay = TimeSpan.FromSeconds(10);
    private const double BackAway = 0.9;   // m: more than a body's length out of the other's way

    // True when this body moved off its road to give way.
    private async Task<bool> CoordinateWithAsync(int id, string who, string where, int yield, CancellationToken ct)
    {
        bool mine = string.CompareOrdinal(golem, who) < 0;
        string note = mine
            ? $"mission {id}: met {who} at {where} — the way is mine by name; waiting {WaitForTheWay.TotalSeconds:0} s for {who} to clear, {yield}/{MaxYields}"
            : $"mission {id}: met {who} at {where} — {who} has the way by name; giving way and waiting {GiveWay.TotalSeconds:0} s, {yield}/{MaxYields}";
        Console.WriteLine($"[golem {golem}] {note}");
        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", note, DateTime.UtcNow));
        bool moved = false;
        if (!mine)
        {
            moved |= await BackAwayAsync(ct);
            moved |= await StepAsideAsync(right: false, ct);
        }
        await StandAsync(mine ? WaitForTheWay : GiveWay, ct);
        return moved;
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

    // The touch of a standing body is a fact worth telling (idle overload of Bump), at most once a second.
    private void TellTouchedStanding()
    {
        if (DateTime.UtcNow - lastToldStanding < TimeSpan.FromSeconds(1)) return;
        lastToldStanding = DateTime.UtcNow;
        var pose = ros.LatestPose;
        if (pose == null) return;
        double r = Radius();
        double hx = pose.X + r * Math.Cos(pose.Theta), hy = pose.Y + r * Math.Sin(pose.Theta);
        Produce("bumped", $"{golem}:standing-bump:{DateTime.UtcNow.Ticks}", MissionBumped.Payload(0, hx, hy, pose.Theta));
        string note = $"touched while standing at ({hx:0.0}, {hy:0.0}) — telling the peers";
        Console.WriteLine($"[golem {golem}] {note}");
        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", note, DateTime.UtcNow));
    }

    // Back down the lane the way the body came, if it fits there. True when it moved.
    private async Task<bool> BackAwayAsync(CancellationToken ct)
    {
        var pose = ros.LatestPose;
        if (pose == null) return false;
        double tx = pose.X - BackAway * Math.Cos(lane), ty = pose.Y - BackAway * Math.Sin(lane);
        if (!FitsAt(tx, ty)) return false;
        Console.WriteLine($"[golem {golem}] backing away to ({tx:0.0}, {ty:0.0})");
        await navigator.GoToAsync(tx, ty, 0.15, ct);
        return true;
    }

    // The old shape: a bump that is an obstacle without asking — used while feeling past one already found.
    private async Task BumpAsync(int id, string key, Collision hit, CancellationToken ct)
    {
        string who = await BumpAndListenAsync(id, key, hit, ct);
        if (who != "") await CoordinateWithAsync(id, who, $"({hit.X:0.0}, {hit.Y:0.0})", 1, ct);
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
            heardAtDriveStart = HeardBumpCount();
            var step = await navigator.GoToAsync(tx, ty, 0.12, ct);
            if (right) probe.RightSteps++; else probe.LeftSteps++;
            if (!step.Reached)
            {
                // the step itself met something: if it was not a wall, it is told and settled (a peer, or a mark); either way this side is done
                if (step.Hit != null && Suspect(step.Hit, heardAtDriveStart).Kind != "wall")
                    await BumpAsync(id, key, step.Hit, ct);
                if (right) probe.RightDone = true; else probe.LeftDone = true;
                continue;
            }

            // in the new lane, run ahead past where the thing was before going back to the leg. A clear run
            // means "try the leg again from here". A hit here means the thing is wider than one step: another
            // mark, and the next step goes further out on the SAME side — the side is given up only when a
            // step itself is blocked or the steps run out.
            double ax = tx + AheadRun * Math.Cos(th), ay = ty + AheadRun * Math.Sin(th);
            if (!HasRoomAt(ax, ay)) return true;   // no lane ahead: still, the leg is worth trying from here
            heardAtDriveStart = HeardBumpCount();
            var run = await navigator.GoToAsync(ax, ay, 0.2, ct);
            if (run.Reached) return true;
            if (run.Hit == null) { if (right) probe.RightDone = true; else probe.LeftDone = true; continue; }   // a stall or a timeout: this side is not working
            if (Suspect(run.Hit, heardAtDriveStart).Kind == "wall") { if (right) probe.RightDone = true; else probe.LeftDone = true; continue; }
            await BumpAsync(id, key, run.Hit, ct);
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Bodies sharing a floor. Small courtesies keep them out of each other's way, all of them relative
    // to the LANE — the line of the leg the body was driving, not its nose, which points anywhere after
    // a back-off — and each to a fixed side, so two bodies on the same lane end up on opposite sides:
    // the follower pulls over to the RIGHT when it arrives; a golem giving way backs down its lane and
    // steps off it to the LEFT; a golem standing idle that gets touched journals the touch — so whoever
    // moved hears it met a body — and makes room to the right of its last lane. Runtime, except the
    // idle touch, which is a fact the peers need told.
    // ------------------------------------------------------------------
    private const double StepAside = 0.8;   // m: a body's width and a hand
    private DateTime lastMadeRoom = DateTime.MinValue;
    private double lane;                    // heading of the leg being (or last) driven

    private async Task PullOverAsync(CancellationToken ct)
    {
        var pose = ros.LatestPose;
        if (pose == null) return;
        string note = $"pulling over to the right, off the leader's way";
        if (await StepAsideAsync(right: true, ct)) { Console.WriteLine($"[golem {golem}] {note}"); feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", note, DateTime.UtcNow)); }
    }

    private async Task MakeRoomIfBumpedAsync(CancellationToken ct)
    {
        var touch = ros.LatestContact;
        if (touch == null || touch.At <= lastMadeRoom || DateTime.UtcNow - touch.At > TimeSpan.FromSeconds(2)) return;
        lastMadeRoom = DateTime.UtcNow;
        var pose = ros.LatestPose;
        if (pose == null) return;
        // the touch is a fact worth telling: whoever moved into me will hear a body was here
        TellTouchedStanding();
        Console.WriteLine($"[golem {golem}] making room");
        await StepAsideAsync(right: true, ct);
    }

    // A body's width off the lane, to the side asked for if the body fits there, else the other. True when it moved.
    private async Task<bool> StepAsideAsync(bool right, CancellationToken ct)
    {
        var pose = ros.LatestPose;
        if (pose == null) return false;
        foreach (bool side in new[] { right, !right })
        {
            double tx = pose.X + StepAside * (side ? Math.Sin(lane) : -Math.Sin(lane));
            double ty = pose.Y + StepAside * (side ? -Math.Cos(lane) : Math.Cos(lane));
            if (!FitsAt(tx, ty)) continue;
            Console.WriteLine($"[golem {golem}] stepping {(side ? "right" : "left")} of the lane to ({tx:0.0}, {ty:0.0})");
            await navigator.GoToAsync(tx, ty, 0.15, ct);
            return true;
        }
        return false;
    }

    // Does my body stand clear at this point? On the map, off the walls, off the marks.
    private bool FitsAt(double x, double y)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @fits = g.FitsAt(@x, @y);
        ")
        .WithParameters(rented, p => {
            p["x", typeof(double)]                  = x;
            p["y", typeof(double)]                  = y;
            p[Parameter.Out, "fits", typeof(bool)]  = default;
        })
        .PerformQuery();
        return rented["fits"].GetValue<bool>();
    }

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
             bool Routed, int LegsLeft, string Passage, bool IsStop, bool Following, int Newer, int Bumps, bool BumpedSinceRoute,
             bool Ordered) ReadPlan()
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @has = g.HasPendingMission();
            if (g.HasPendingMission()) {
                @id = g.NextId();
                @x = g.OrderX();
                @y = g.OrderY();
                @ax = g.OrderApproachX();
                @ay = g.OrderApproachY();
                @ex = g.OrderExitX();
                @ey = g.OrderExitY();
                @routed = g.IsRouted(g.NextId());
                @legs = g.LegsLeft(g.NextId());
                @passage = g.OrderPassage(g.NextId());
                @stop = g.OrderIsStop(g.NextId());
                @following = g.IsFollowing(g.NextId());
                @newer = 0;
                if (g.HasNewerFollowing(g.NextId())) { @newer = g.NewestFollowingId(); }
                @bumps = g.Bumps(g.NextId());
                @bumped = g.HasBumpedSinceRoute(g.NextId());
                @ordered = g.IsOrdered(g.NextId());
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
            p[Parameter.Out, "ordered",   typeof(bool)]   = default;
        })
        .PerformQuery();
        return (rented["has"].GetValue<bool>(), rented["id"].GetValue<int>(),
                rented["x"].GetValue<double>(), rented["y"].GetValue<double>(),
                rented["ax"].GetValue<double>(), rented["ay"].GetValue<double>(),
                rented["ex"].GetValue<double>(), rented["ey"].GetValue<double>(),
                rented["routed"].GetValue<bool>(), rented["legs"].GetValue<int>(),
                rented["passage"].GetValue<string>() ?? "", rented["stop"].GetValue<bool>(),
                rented["following"].GetValue<bool>(), rented["newer"].GetValue<int>(),
                rented["bumps"].GetValue<int>(), rented["bumped"].GetValue<bool>(),
                rented["ordered"].GetValue<bool>());
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

    private int HeardBumpCount()
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @heard = g.HeardBumpCount();
        ")
        .WithParameters(rented, p => {
            p[Parameter.Out, "heard", typeof(int)] = default;
        })
        .PerformQuery();
        return rented["heard"].GetValue<int>();
    }

    // Who, among the peers' bumps heard after a given count, bumped near this point; "" for nobody.
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

    // What the domain suspects the body touched: a wall it knows, a peer that spoke since the leg began, or a
    // thing — and the conclusion it names. The host never classifies; it asks.
    private (string Kind, string Who, string Conclusion) Suspect(Collision hit, int since)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @kind = g.Suspect(@x, @y, @heading, @since).Kind;
            @who = g.Suspect(@x, @y, @heading, @since).Who;
            @verb = g.Suspect(@x, @y, @heading, @since).Conclusion;
        ")
        .WithParameters(rented, p => {
            p["x",       typeof(double)]              = hit.X;
            p["y",       typeof(double)]              = hit.Y;
            p["heading", typeof(double)]              = hit.Heading;
            p["since",   typeof(int)]                 = since;
            p[Parameter.Out, "kind", typeof(string)]  = default;
            p[Parameter.Out, "who",  typeof(string)]  = default;
            p[Parameter.Out, "verb", typeof(string)]  = default;
        })
        .PerformQuery();
        return (rented["kind"].GetValue<string>() ?? "", rented["who"].GetValue<string>() ?? "", rented["verb"].GetValue<string>() ?? "");
    }

    private bool NeedsRoad(int id, double x, double y)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @needs = g.NeedsRoad(@id, @x, @y);
        ")
        .WithParameters(rented, p => {
            p["id", typeof(int)]                     = id;
            p["x",  typeof(double)]                  = x;
            p["y",  typeof(double)]                  = y;
            p[Parameter.Out, "needs", typeof(bool)]  = default;
        })
        .PerformQuery();
        return rented["needs"].GetValue<bool>();
    }

    private bool MayRetryLeg(int id)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @may = g.MayRetryLeg(@id);
        ")
        .WithParameters(rented, p => {
            p["id", typeof(int)]                   = id;
            p[Parameter.Out, "may", typeof(bool)]  = default;
        })
        .PerformQuery();
        return rented["may"].GetValue<bool>();
    }

    private bool IsOrdered(int id)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @ordered = g.IsOrdered(@id);
        ")
        .WithParameters(rented, p => {
            p["id", typeof(int)]                       = id;
            p[Parameter.Out, "ordered", typeof(bool)]  = default;
        })
        .PerformQuery();
        return rented["ordered"].GetValue<bool>();
    }

    private int Grazes(int id)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @grazes = g.Grazes(@id);
        ")
        .WithParameters(rented, p => {
            p["id", typeof(int)]                     = id;
            p[Parameter.Out, "grazes", typeof(int)]  = default;
        })
        .PerformQuery();
        return rented["grazes"].GetValue<int>();
    }

    private int MetCount()
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @met = g.MetCount();
        ")
        .WithParameters(rented, p => {
            p[Parameter.Out, "met", typeof(int)] = default;
        })
        .PerformQuery();
        return rented["met"].GetValue<int>();
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
