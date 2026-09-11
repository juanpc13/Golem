using System.Globalization;
using Choreography.Input;
using Choreography.Told;
using Choreography.Transport.Brokered;
using GolemAPI.Membrane;
using GolemAPI.Navigation;
using GolemAPI.Panel;
using Puppeteer;

namespace GolemAPI.Choreography;

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
                $"tell BumpedAt with @x, @y, @heading, @who, @px, @py to {p} once 'bump-' + @who + '-' + @x + ',' + @y + '-{p}';"));
            perf.Actor.Reactions.DefineReaction("echo-bumped")
                .Cue().Company().WithSharedHydration()
                .Seek("Bumped").One()
                    .OnMatch(@"
                        expose $x x, $y y, $heading heading, $who who, $px px, $py py;
                    ")
                .Causation.Continue(bumped);

            // A standing body touched: a body did it (things do not move). Told so the mover knows it met one; no mark.
            string touched = string.Join("\n", peers.Select(p =>
                $"tell TouchedAt with @x, @y, @who, @px, @py to {p} once 'touch-' + @who + '-' + @x + ',' + @y + '-{p}';"));
            perf.Actor.Reactions.DefineReaction("echo-touched")
                .Cue().Company().WithSharedHydration()
                .Seek("Touched").One()
                    .OnMatch(@"
                        expose $x tx, $y ty, $who twho, $px tpx, $py tpy;
                    ")
                .Causation.Continue(touched);

            // It was a body: the mark the bump presumed is taken back, here and in every peer that learned it.
            string met = string.Join("\n", peers.Select(p =>
                $"tell MetPeer with @x, @y to {p} once 'met-{golem}-' + @x + ',' + @y + '-{p}';"));
            perf.Actor.Reactions.DefineReaction("echo-met")
                .Cue().Company().WithSharedHydration()
                .Seek("Met").One()
                    .OnMatch(@"
                        expose $x ex, $y ey;
                    ")
                .Causation.Continue(met);

            // Somebody took a thing away: the fleet must forget it together, or one golem would keep skirting
            // what another can already drive through.
            string forgotten = string.Join("\n", peers.Select(p =>
                $"tell ObstacleGone with @x, @y to {p} once 'gone-{golem}-' + @x + ',' + @y + '-{p}';"));
            perf.Actor.Reactions.DefineReaction("echo-forgotten")
                .Cue().Company().WithSharedHydration()
                .Seek("Forgotten").One()
                    .OnMatch(@"
                        expose $x gx, $y gy;
                    ")
                .Causation.Continue(forgotten);

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
                    expose $missionId rid, $x rx, $y ry;
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
        dispatch.On<StopReached>((actor, m) =>
        {
            string refused = actor.Using(
                @"
                    Check(g.Knows(@id) && g.IsPending(@id) && g.IsStopAhead(@id, @x, @y)) Error 'that is not a stop ahead';
                ",
                @"
                    { point = Position(@x, @y); g.Reach(@id, point); }
                    expose @id rid, @x rx, @y ry;
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
            // The decision, act by act: Route opens it, a Via / Around / Aside per leg, a Stop per stop — one
            // journal entry. The values ride as @params; the passages are objects of the map.
            var legs = RoadLeg.Decode(m.Road);
            string refused = actor.Using(
                @"
                    Check(g.Knows(@id) && g.IsPending(@id)) Error 'mission is not pending';
                ",
                RoadLeg.Script(legs))
            .WithParameters(p => {
                p["id", typeof(int)] = m.Id;
                RoadLeg.Bind(p, legs);
            })
            .PerformCheckThenCommand();
            Settle(refused, $"mission {m.Id} takes the road {RoadLeg.Describe(legs)}");
        });

        // A bump: the body touched something the map does not hold — on a mission's road, or standing idle
        // (id 0). Repeated per touch. What it was is settled afterwards, by what the peers say.
        dispatch.On<MissionBumped>((actor, m) =>
        {
            if (m.Id == 0)
            {
                actor.Using(@"
                    { touch = Pose(@x, @y, @heading); g.Bump(touch); }
                    expose @x tx, @y ty, @me twho, @px tpx, @py tpy;
                ")
                .WithParameters(p => {
                    p["x",       typeof(double)] = m.X;
                    p["y",       typeof(double)] = m.Y;
                    p["heading", typeof(double)] = m.Heading;
                    p["me",      typeof(string)] = golem;
                    p["px",      typeof(double)] = m.PoseX;
                    p["py",      typeof(double)] = m.PoseY;
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
                    { touch = Pose(@x, @y, @heading); g.Bump(@id, touch); }
                    expose @x x, @y y, @heading heading, @me who, @px px, @py py;
                ")
            .WithParameters(p => {
                p["id",      typeof(int)]    = m.Id;
                p["x",       typeof(double)] = m.X;
                p["y",       typeof(double)] = m.Y;
                p["heading", typeof(double)] = m.Heading;
                p["me",      typeof(string)] = golem;
                p["px",      typeof(double)] = m.PoseX;
                p["py",      typeof(double)] = m.PoseY;
            })
            .PerformCheckThenCommand();
            Settle(refused, $"mission {m.Id} bumped into something at ({m.X:0.0}, {m.Y:0.0})");
        });

        // It was a peer: the body met another body there. History, told to nobody (the peer lived it too).
        dispatch.On<PeerMet>((actor, m) =>
        {
            actor.Using(@"
                { at = Position(@x, @y); g.Met(@who, at); }
                expose @x ex, @y ey;
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
                    { at = Position(@x, @y); g.Graze(@id, at); }
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
                .Command("{ point = Position(@x, @y); g.Follow(point); }")
            .Told("BumpedAt").With<double>("x").With<double>("y").With<double>("heading").With<string>("who").With<double>("px").With<double>("py")
                .Command("{ touch = Pose(@x, @y, @heading); peer = Position(@px, @py); g.HearBump(@who, touch, peer); }")
            .Told("TouchedAt").With<double>("x").With<double>("y").With<string>("who").With<double>("px").With<double>("py")
                .Command("{ at = Position(@x, @y); peer = Position(@px, @py); g.HearTouch(@who, at, peer); }")
            .Told("MetPeer").With<double>("x").With<double>("y")
                .Command("{ at = Position(@x, @y); g.LearnMet(at); }")
            .Told("ObstacleGone").With<double>("x").With<double>("y")
                .Command("{ at = Position(@x, @y); g.LearnForget(at); }")
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
            "reached"   => StopReached.TypeId,
            "bumped"    => MissionBumped.TypeId,
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
        int announcedLeg = -1;
        int yields = 0;        // times the golem waited for a peer on the current leg
        int marksAtRoute = -1; // how many marks the map held when the current plan was decided
        bool gaveWay = false;  // the body moved off its road to let a peer pass: the road is decided again from where it stands
        // A plan underway at boot (a mission already pending when the golem woke) is decided again from where the
        // body stands — it may be anywhere. A mission that arrives later comes with its plan and is walked as it came.
        bool woke = ReadPlan().Has;
        List<RoadLeg> legs = null;   // the plan ahead, as the golem handed it out — walked here, in memory, leg by leg
        int legsFor = 0;             // the mission that plan belongs to
        int plans = 0;               // plans taken up, so a stop reached on a later plan is never taken for a redelivery
        int cursor = 0;              // the leg being walked
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

            // The plan is written whole and walked in silence (Juan, 10-sep). It is decided HERE only when the mission
            // has none (a followed point), when the body bumped into something on it (the plan is interrupted: another
            // road from where the body stands, past the marks it did not know), or when the golem woke up with a plan
            // underway (its body may be anywhere). A mission that arrived with its plan is walked as it came.
            bool newMarks = marksAtRoute < 0 || Marks() != marksAtRoute;
            bool reRoute = plan.BumpedSinceRoute && (newMarks || gaveWay);
            if (!plan.Routed || reRoute || (woke && plan.Routed))
            {
                var here = ros.LatestPose;
                if (here == null) { await Task.Delay(200, ct); continue; }
                List<RoadLeg> road;
                try
                {
                    road = RoadFrom(plan.Id, here.X, here.Y);
                }
                catch (Exception ex)
                {
                    // No road fits the body past the marks. Before giving the mission up, the body feels its way:
                    // the map is pessimistic by a margin, and a touch may find the gap the plan cannot see. Each
                    // touch on the way is journaled like any other, so the feeling is told even if it fails.
                    if (plan.BumpedSinceRoute)
                    {
                        probe ??= new Probe(plan.Id, legs != null && cursor < legs.Count ? LegName(legs[cursor]) : "", lane);
                        if (await FeelForAWayPastAsync(plan.Id, key, probe, ct))
                        {
                            string felt = $"mission {plan.Id}: no road on the map, but a way by feel — deciding the road again from here";
                            Console.WriteLine($"[golem {golem}] {felt}");
                            feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", felt, DateTime.UtcNow));
                            continue;
                        }
                    }
                    Produce("failed", $"{key}:failed", MissionFailed.Payload(plan.Id, "no road: " + Reason(ex)));
                    if (!await WaitUntilAsync(() => IsSettled(plan.Id), ct)) await Task.Delay(TimeSpan.FromSeconds(2), ct);
                    continue;
                }
                string verb = !plan.Routed ? "road" : woke ? "road again, awake with a plan underway" : "another road";
                Console.WriteLine($"[golem {golem}] mission {plan.Id}: {verb} {RoadLeg.Describe(road)}");
                feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", $"mission {plan.Id}: {verb} {RoadLeg.Describe(road)}", DateTime.UtcNow));
                Produce("routed", $"{key}:routed:{plan.Bumps}:{(woke ? "woke" : "")}{Marks()}", MissionRouted.Payload(plan.Id, RoadLeg.Encode(road)));
                await WaitUntilAsync(() => IsRouted(plan.Id) && !HasBumpedSinceRoute(plan.Id), ct);
                marksAtRoute = Marks();
                gaveWay = false;
                probe = null;
                woke = false;
                legs = null;
                continue;
            }
            woke = false;

            // Take up the plan: every leg ahead, with how each is walked. From here on the host walks it in memory
            // and comes back to the journal only for what fulfils it (a stop reached) or interrupts it (a touch).
            if (legs == null || legsFor != plan.Id)
            {
                legs = RoadAhead(plan.Id);
                legsFor = plan.Id;
                cursor = 0;
                plans++;
                marksAtRoute = Marks();
                if (legs.Count == 0) { await Task.Delay(500, ct); legs = null; continue; }
                string taken = $"mission {plan.Id}: walking the plan — {RoadLeg.Describe(legs)}";
                Console.WriteLine($"[golem {golem}] {taken}");
                feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", taken, DateTime.UtcNow));
            }
            if (cursor >= legs.Count)
            {
                // every leg walked: the last stop reached settles the mission; if the journal says otherwise, re-read
                if (!await WaitUntilAsync(() => IsSettled(plan.Id), ct)) legs = null;
                continue;
            }
            var leg = legs[cursor];
            bool lastStop = leg.Kind == "stop" && legs.Skip(cursor + 1).All(l => l.Kind != "stop");

            // The last stop of a followed mission is met a body's length short: the leader may still be there.
            bool standoff = plan.Following && lastStop;
            if (announcedFor != plan.Id || announcedLeg != cursor)
            {
                announcedFor = plan.Id;
                announcedLeg = cursor;
                yields = 0;
                probe = null;
                string what = leg.Kind == "stop" ? "heading to a stop"
                            : leg.Kind == "around" ? "heading around a mark"
                            : leg.Kind == "aside" ? "stepping aside" : $"heading to the passage {LegName(leg)}";
                if (standoff) what += $", stopping {LeaderStandoff:0.0} short of the leader's spot";
                Console.WriteLine($"[golem {golem}] mission {plan.Id}: {what} ({leg.X:0.0}, {leg.Y:0.0}) — leg {cursor + 1} of {legs.Count}");
                feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", $"mission {plan.Id}: {what} ({leg.X:0.0}, {leg.Y:0.0}) — leg {cursor + 1} of {legs.Count}", DateTime.UtcNow));
            }

            // A door is crossed straight: line up in front of it, then run through to the far side.
            // The plan says where those two points are; an opening, a point or a stop is a single run.
            Outcome outcome = Outcome.Arrived;
            heardAtDriveStart = HeardBumpCount();
            var from = ros.LatestPose;
            if (from != null)
                lane = leg.IsCrossedStraight
                    ? Math.Atan2(leg.EY - leg.AY, leg.EX - leg.AX)
                    : Math.Atan2(leg.EY - from.Y, leg.EX - from.X);
            if (leg.IsCrossedStraight)
                outcome = await navigator.GoToAsync(leg.AX, leg.AY, LineUpWithin, ct);
            if (outcome.Reached && !ct.IsCancellationRequested)
                outcome = await navigator.GoToAsync(leg.EX, leg.EY, standoff ? LeaderStandoff : ArriveWithin, ct);
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
                        // A wall I know: my own execution error. The host reports it and waits: the golem either keeps
                        // its patience (the leg is tried again) or has spent it (the mission ends). Neither is the host's call.
                        int grazesBefore = Grazes(plan.Id);
                        string note = $"mission {plan.Id}: grazed {outcome.Hit.With} at {where}, a wall I know — telling the golem";
                        Console.WriteLine($"[golem {golem}] {note}");
                        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", note, DateTime.UtcNow));
                        Produce("grazed", $"{key}:b{plan.Bumps}:graze:{grazesBefore + 1}",
                                MissionGrazed.Payload(plan.Id, outcome.Hit.X, outcome.Hit.Y));
                        await WaitUntilAsync(() => Grazes(plan.Id) > grazesBefore, ct);
                        if (MayRetryLeg(plan.Id)) continue;
                        reason = $"still grazing {outcome.Hit.With} at {where} after {Grazes(plan.Id)} grazes: patience spent";
                    }
                    else
                    {
                        // Something the map does not hold. The touch is journaled and told; then the golem waits for
                        // the peers to speak and asks the domain what it suspects. A peer: met, coordinate. Nobody: a
                        // thing — marked (told), and the plan is interrupted: the loop above decides another road.
                        string who = await BumpAndListenAsync(plan.Id, key, outcome.Hit, ct);
                        if (who != "")
                        {
                            if (yields < MaxYields)
                            {
                                yields++;
                                // It was a body, and it told where it stands. The golem decides a road that steps out of
                                // its way — the courtesy step is the first leg (aside), walked like any other. Both bodies
                                // do this, each to its own right, which is how two of them pass instead of shove.
                                var mine = ros.LatestPose;
                                if (mine == null) { await Task.Delay(200, ct); continue; }
                                var past = RoadPast(plan.Id, who, mine.X, mine.Y, lane);
                                string note = $"mission {plan.Id}: met {who} at {where} — stepping out of its way: {RoadLeg.Describe(past)}";
                                Console.WriteLine($"[golem {golem}] {note}");
                                feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", note, DateTime.UtcNow));
                                Produce("routed", $"{key}:routed:{Bumps(plan.Id)}:met{yields}", MissionRouted.Payload(plan.Id, RoadLeg.Encode(past)));
                                await WaitUntilAsync(() => IsRouted(plan.Id) && !HasBumpedSinceRoute(plan.Id), ct);
                                marksAtRoute = Marks();
                                probe = null;
                                legs = null;
                                continue;
                            }
                            reason = $"blocked by {who} at {where} after meeting it {MaxYields} times";
                        }
                        else
                        {
                            // A thing, now marked: the plan is interrupted. The golem decides its road again and the
                            // planner puts the way round the mark in it — an 'around' leg, walked like any other.
                            // Feeling by touch is the LAST resort, only when no road fits at all (above, where the planner refuses).
                            legs = null;
                            continue;
                        }
                    }
                }
                Produce("failed", $"{key}:failed", MissionFailed.Payload(plan.Id, reason));
                if (!await WaitUntilAsync(() => IsSettled(plan.Id), ct)) await Task.Delay(TimeSpan.FromSeconds(2), ct);
                continue;
            }

            // A passage crossed or a point passed is walked in silence: the plan said it, the body did it.
            if (leg.Kind != "stop")
            {
                cursor++;
                continue;
            }

            // A stop reached is journaled: it fulfils part of the plan (the legs before it were walked) and tells the
            // peers; the last one completes the mission. One message id per stop of THIS plan.
            int stopsBefore = plan.StopsLeft;
            Produce("reached", $"{key}:p{plans}:reach:{cursor}", StopReached.Payload(plan.Id, leg.X, leg.Y));
            if (!await WaitUntilAsync(() => lastStop ? IsSettled(plan.Id) : StopsLeft(plan.Id) < stopsBefore, ct))
                await Task.Delay(TimeSpan.FromSeconds(2), ct); // never re-drive on a timeout; back off and re-read
            cursor++;
            if (!lastStop) continue;

            ReportLocalization(plan.Id);

            // Pacing: a stop a peer told us about is a stop that peer is already past. Lingering there
            // a while keeps the leader's lead. How long is the golem's own property (the body release);
            // the pause itself is runtime — the mission is settled and nothing about the wait belongs
            // in the journal. And the follower pulls over first: its standoff spot lies on the line the
            // leader walked in on, which is the line the leader walks out on.
            if (plan.Following)
            {
                await PullOverAsync(ct);
                var hold = TimeSpan.FromSeconds(LingerAfterTold());
                if (hold > TimeSpan.Zero)
                {
                    Console.WriteLine($"[golem {golem}] lingering {hold.TotalSeconds:0} s at ({leg.X:0.0}, {leg.Y:0.0}) to keep the leader's lead");
                    feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "",
                        $"lingering {hold.TotalSeconds:0} s at ({leg.X:0.0}, {leg.Y:0.0}) to keep the leader's lead", DateTime.UtcNow));
                    await StandAsync(hold, ct);
                }
            }
        }
    }

    private static string LegName(RoadLeg leg) => leg.Kind switch { "door" => $"{leg.A}/{leg.B}", "opening" => $"{leg.A}~{leg.B}", _ => leg.Kind };

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
        var standing = ros.LatestPose;
        Produce("bumped", $"{key}:bump:{before + 1}",
                MissionBumped.Payload(id, hit.X, hit.Y, hit.Heading, standing?.X ?? hit.X, standing?.Y ?? hit.Y));
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
        string thing = $"mission {id}: nobody else bumped there and then — the mark the bump presumed at ({hit.X:0.0}, {hit.Y:0.0}) stands; reconsidering for {Reconsider.TotalSeconds:0}s";
        Console.WriteLine($"[golem {golem}] {thing}");
        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", thing, DateTime.UtcNow));
        _ = ReconsiderAsync(id, hit, since, ct);
        return "";
    }

    // A peer may speak after the window: its own row goes through its journal, its reaction and the wire before it
    // reaches mine, and a standing body tells its touch at most once a second. The host owns the clock, so it keeps
    // asking the domain for a while; if the domain then suspects a peer, the conclusion is the same Met — the mark the
    // bump presumed goes, here and in every peer that learned it. Otherwise the mark stands (10-sep-2026 lab: a ghost
    // mark in the garage lived in three journals because red's TouchedAt came after the 2.5 s window).
    private static readonly TimeSpan Reconsider = TimeSpan.FromSeconds(12);

    private async Task ReconsiderAsync(int id, Collision hit, int since, CancellationToken ct)
    {
        try
        {
            var until = DateTime.UtcNow + Reconsider;
            while (DateTime.UtcNow < until && !ct.IsCancellationRequested)
            {
                await Task.Delay(500, ct);
                var suspicion = Suspect(hit, since);
                if (suspicion.Kind != "peer") continue;
                int metBefore = MetCount();
                string late = $"mission {id}: {suspicion.Who} spoke after the window — it was there too: the mark the bump presumed at ({hit.X:0.0}, {hit.Y:0.0}) is taken back ({suspicion.Conclusion})";
                Console.WriteLine($"[golem {golem}] {late}");
                feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", late, DateTime.UtcNow));
                Produce("met", $"{golem}:met:{suspicion.Who}:{hit.X:0.00},{hit.Y:0.00}:{DateTime.UtcNow.Ticks}", PeerMet.Payload(suspicion.Who, hit.X, hit.Y));
                await WaitUntilAsync(() => MetCount() > metBefore, ct);
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

    // The touch of a standing body is a fact worth telling (idle overload of Bump), at most once a second.
    private void TellTouchedStanding()
    {
        if (DateTime.UtcNow - lastToldStanding < TimeSpan.FromSeconds(1)) return;
        lastToldStanding = DateTime.UtcNow;
        var pose = ros.LatestPose;
        var touch = ros.LatestContact;
        if (pose == null || touch == null) return;
        var (hx, hy, hh) = touch.On(pose, Radius());   // where on the shell it was pressed, on the plane
        Produce("bumped", $"{golem}:standing-bump:{DateTime.UtcNow.Ticks}", MissionBumped.Payload(0, hx, hy, hh, pose.X, pose.Y));
        string note = $"touched while standing at ({hx:0.0}, {hy:0.0}) — telling the peers";
        Console.WriteLine($"[golem {golem}] {note}");
        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", note, DateTime.UtcNow));
    }

    // A touch met while feeling by hand: journal it and let the golem conclude. If it turns out to be a body,
    // the loop above meets it again on the next leg and decides a road out of its way there — feeling by hand is
    // no place to be polite.
    private async Task BumpAsync(int id, string key, Collision hit, CancellationToken ct) =>
        await BumpAndListenAsync(id, key, hit, ct);

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
    private (bool Has, int Id, bool Routed, int StopsLeft, bool Following, int Newer, int Bumps, bool BumpedSinceRoute) ReadPlan()
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @has = g.HasPendingMission();
            if (g.HasPendingMission()) {
                @id = g.NextId();
                @routed = g.IsRouted(g.NextId());
                @stops = g.StopsLeft(g.NextId());
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
            p[Parameter.Out, "routed",    typeof(bool)]   = default;
            p[Parameter.Out, "stops",     typeof(int)]    = default;
            p[Parameter.Out, "following", typeof(bool)]   = default;
            p[Parameter.Out, "newer",     typeof(int)]    = default;
            p[Parameter.Out, "bumps",     typeof(int)]    = default;
            p[Parameter.Out, "bumped",    typeof(bool)]   = default;
        })
        .PerformQuery();
        return (rented["has"].GetValue<bool>(), rented["id"].GetValue<int>(),
                rented["routed"].GetValue<bool>(), rented["stops"].GetValue<int>(),
                rented["following"].GetValue<bool>(), rented["newer"].GetValue<int>(),
                rented["bumps"].GetValue<int>(), rented["bumped"].GetValue<bool>());
    }

    // The plan ahead of a mission, as the golem hands it out: every leg not yet known to be walked, with how each is walked.
    private List<RoadLeg> RoadAhead(int id) =>
        RoadLeg.FromQuery(perf.Actor.Using("foreach (legs in g.RoadAhead(@id).Legs()) { " + RoadLeg.PrintLegs + " }")
        .WithParameters(p => { p["id", typeof(int)] = id; })
        .PerformQuery());

    private int StopsLeft(int id)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @stops = g.StopsLeft(@id);
        ")
        .WithParameters(rented, p => {
            p["id", typeof(int)]                   = id;
            p[Parameter.Out, "stops", typeof(int)] = default;
        })
        .PerformQuery();
        return rented["stops"].GetValue<int>();
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

    // The road from where the body stands, as the golem's own objects: a query walks the legs and prints what
    // each one is; the host carries them to the act that writes them, inventing nothing.
    private List<RoadLeg> RoadFrom(int id, double x, double y) =>
        RoadLeg.FromQuery(perf.Actor.Using("foreach (legs in g.Road(@id, @x, @y).Legs()) { " + RoadLeg.PrintLegs + " }")
        .WithParameters(p => {
            p["id", typeof(int)]    = id;
            p["x",  typeof(double)] = x;
            p["y",  typeof(double)] = y;
        })
        .PerformQuery());

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

    // The road out of a peer's way: the golem answers with the courtesy step first and then its errand.
    private List<RoadLeg> RoadPast(int id, string who, double x, double y, double heading) =>
        RoadLeg.FromQuery(perf.Actor.Using("foreach (legs in g.RoadPast(@id, @who, @x, @y, @heading).Legs()) { " + RoadLeg.PrintLegs + " }")
        .WithParameters(p => {
            p["id",      typeof(int)]    = id;
            p["who",     typeof(string)] = who;
            p["x",       typeof(double)] = x;
            p["y",       typeof(double)] = y;
            p["heading", typeof(double)] = heading;
        })
        .PerformQuery());

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
