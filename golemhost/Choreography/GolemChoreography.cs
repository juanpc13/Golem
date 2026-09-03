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
// navigator (the body's locomotion, behind a seam) and reports its verdict to a Saga
// keyed by mission id — every write goes through the actor, guarded by a domain
// Check, and the journal stays the only truth.
// Speech (tells) is a Reaction on the golem's own journal; the panel's journal lane is the
// journal itself, tapped record by record (Panel/JournalTap).
public sealed class GolemChoreography
{
    private readonly GolemPerformance perf;
    private readonly Rosbridge ros;
    private readonly INavigator navigator;
    private readonly PanelFeed feed;
    private readonly HttpBroker wire;
    private readonly string golem;
    private readonly string turtle;
    private readonly string tellDoneTo;
    private readonly string journalPath;
    private readonly InProcessBroker ops = new();
    private readonly string topic;
    private readonly TellBindingTable bindings = new();
    private ToldListener toldListener;

    internal GolemChoreography(GolemPerformance perf, Rosbridge ros, INavigator navigator, PanelFeed feed, HttpBroker wire,
                               string golem, string turtle, string tellDoneTo, string journalPath)
    {
        this.perf = perf;
        this.ros = ros;
        this.navigator = navigator;
        this.feed = feed;
        this.wire = wire;
        this.golem = golem;
        this.turtle = turtle;
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

        // The loop's outcomes are steps of ONE run per mission: a Saga keyed by mission
        // id serializes them per key; a domain Check on each step is the real guard
        // against a redelivered or repeated outcome. Letting go is an independent command.
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

        perf.DefineSaga("Mission")
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
        // hearer's verb (Take mints the handle inside), one journaled perform per tell.
        // Only plain @params here: a nested call as an argument faults the reaction matcher.
        toldListener = perf
            .ListenAs(golem, bindings, wire)
            .Told("PointVisited").With<double>("x").With<double>("y")
                .Command("g.Take(@x, @y);")
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

    // The operator asks the golem to let go of every mission: the body is put back
    // (ephemeral) and the forgetting is journaled through the same ops surface.
    public async Task LetGoAsync()
    {
        try
        {
            await ros.DriveAsync(0, 0, CancellationToken.None);
            await ros.CallServiceAsync($"/{turtle}/teleport_absolute",
                new { x = 5.544445, y = 5.544445, theta = 0.0 }, CancellationToken.None);
            await ros.CallServiceAsync("/clear", null, CancellationToken.None);
        }
        catch { /* the world reset is best-effort; the journaled fact is the point */ }

        Produce("retired", $"{golem}:retire:{DateTime.UtcNow.Ticks}", GolemRetired.Payload("operator reset from the panel"));
        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "", "letting go of every mission", DateTime.UtcNow));
    }

    // The operator's hard reset — a LAB lever, not a domain fact: put the body back, wipe
    // THIS golem's journal and exit; Docker restarts the container and the golem is born
    // again at entry 1. With cascade, every peer is asked to do the same: the world is
    // shared, and the hearer dedups the sender's once-ids ('visited-N'), so resetting
    // one side alone would make the peer swallow the next tells as repeats.
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
            await ros.CallServiceAsync($"/{turtle}/teleport_absolute",
                new { x = 5.544445, y = 5.544445, theta = 0.0 }, CancellationToken.None);
            await ros.CallServiceAsync("/clear", null, CancellationToken.None);
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
    // The mission loop: next mission -> navigator -> verdict -> journaled outcome.
    // What counts as failure (a stall, a timeout, later a blocked run) is the
    // navigator's to say; the golem only journals how the mission ended.
    // ------------------------------------------------------------------

    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            var plan = ReadPlan();
            if (!plan.Has)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                continue;
            }

            Console.WriteLine($"[golem {golem}] mission {plan.Id}: go to ({plan.X:0.0}, {plan.Y:0.0})");
            feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "",
                $"mission {plan.Id} started — driving to ({plan.X:0.0}, {plan.Y:0.0})", DateTime.UtcNow));

            Outcome outcome = await navigator.GoToAsync(plan.X, plan.Y, ct);
            if (ct.IsCancellationRequested) break;

            string key = $"{golem}:mission:{plan.Id}";
            if (outcome.Reached)
                Produce("succeeded", $"{key}:succeeded", MissionSucceeded.Payload(plan.Id));
            else
                Produce("failed", $"{key}:failed", MissionFailed.Payload(plan.Id, outcome.Reason));

            if (!await WaitUntilSettledAsync(plan.Id, ct))
                await Task.Delay(TimeSpan.FromSeconds(2), ct); // never re-drive on a timeout; back off and re-read

            // Pacing: a point a peer told us about is a point that peer is already past.
            // Holding there a while keeps the leader's lead. How long is the golem's own
            // property (the pace release); the pause itself is runtime — the mission is
            // settled and nothing about the wait belongs in the journal.
            if (outcome.Reached && WasTold(plan.Id))
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

    // Settled = the journal moved past this mission (completed/failed) — or a timeout.
    private async Task<bool> WaitUntilSettledAsync(int id, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        while (!ct.IsCancellationRequested)
        {
            if (IsSettled(id)) return true;
            if (DateTime.UtcNow - start > TimeSpan.FromSeconds(5))
            {
                Console.WriteLine($"[golem {golem}] mission {id} not settled yet — backing off");
                return false;
            }
            await Task.Delay(100, ct);
        }
        return false;
    }

    // ------------------------------------------------------------------
    // Typed reads: Out parameters through the rent-lease (never parsing print).
    // ------------------------------------------------------------------
    private (bool Has, int Id, double X, double Y) ReadPlan()
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @has = g.HasPendingMission();
            if (g.HasPendingMission()) {
                @id = g.NextId();
                @x = g.NextX();
                @y = g.NextY();
            }
        ")
        .WithParameters(rented, p => {
            p[Parameter.Out, "has", typeof(bool)]   = default;
            p[Parameter.Out, "id",  typeof(int)]    = default;
            p[Parameter.Out, "x",   typeof(double)] = default;
            p[Parameter.Out, "y",   typeof(double)] = default;
        })
        .PerformQuery();
        return (rented["has"].GetValue<bool>(), rented["id"].GetValue<int>(),
                rented["x"].GetValue<double>(), rented["y"].GetValue<double>());
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
