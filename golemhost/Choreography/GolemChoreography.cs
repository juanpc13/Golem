using System.Globalization;
using Choreography.Input;
using Choreography.Told;
using Choreography.Transport.Brokered;
using GolemHost.Membrane;
using GolemHost.Panel;
using Puppeteer;

namespace GolemHost.Choreography;

// The golem's choreography. The mission loop perceives the body (ROS telemetry),
// drives, and reports outcomes to a Saga keyed by mission id — every write goes
// through the actor, guarded by a domain Check, and the journal stays the only truth.
// Speech (tells) and the panel's projections are Reactions on the golem's own journal.
public sealed class GolemChoreography
{
    private readonly GolemPerformance perf;
    private readonly Rosbridge ros;
    private readonly PanelFeed feed;
    private readonly HttpBroker wire;
    private readonly string golem;
    private readonly string turtle;
    private readonly string tellDoneTo;
    private readonly InProcessBroker ops = new();
    private readonly string topic;
    private readonly TellBindingTable bindings = new();
    private ToldListener toldListener;

    internal GolemChoreography(GolemPerformance perf, Rosbridge ros, PanelFeed feed, HttpBroker wire,
                               string golem, string turtle, string tellDoneTo)
    {
        this.perf = perf;
        this.ros = ros;
        this.feed = feed;
        this.wire = wire;
        this.golem = golem;
        this.turtle = turtle;
        this.tellDoneTo = tellDoneTo;
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

        // The panel's journal lane: one view per journaled fact, projected with print
        // and pushed to the PanelSink (a projection, never the journal's storage).
        // Reactions observe V2 Actions (define + invocation): a literal script (no
        // @params, e.g. the release chain) is NOT observable here.
        perf.Actor.Reactions.DefineReaction("Assigned")
            .Cue().Company().WithSharedHydration()
            .Seek("Assigned")
                .OnMatch(@"
                    [_:Golem].Assign($mission, $x, $y)
                ")
            .Program.Emit(@"
                print @mission 'mission', @x 'x', @y 'y';
            ");

        perf.Actor.Reactions.DefineReaction("Taken")
            .Cue().Company().WithSharedHydration()
            .Seek("Taken")
                .OnMatch(@"
                    [_:Golem].Take($x, $y)
                ")
            .Program.Emit(@"
                print @x 'x', @y 'y';
            ");

        perf.Actor.Reactions.DefineReaction("Completed")
            .Cue().Company().WithSharedHydration()
            .Seek("Completed")
                .OnMatch(@"
                    [_:Golem].Complete($mission)
                ")
            .Program.Emit(@"
                print @mission 'mission';
            ");

        perf.Actor.Reactions.DefineReaction("Failed")
            .Cue().Company().WithSharedHydration()
            .Seek("Failed")
                .OnMatch(@"
                    [_:Golem].Fail($mission, $reason)
                ")
            .Program.Emit(@"
                print @mission 'mission', @reason 'reason';
            ");

        perf.Actor.Reactions.DefineReaction("Retired")
            .Cue().Company().WithSharedHydration()
            .Seek("Retired")
                .OnMatch(@"
                    [_:Golem].Retire($reason)
                ")
            .Program.Emit(@"
                print @reason 'reason';
            ");

        if (tellDoneTo == null) return;

        // Speech is a Reaction, never a command: "this mission was ordered" (Assign,
        // capturing its point) closed by "this SAME mission completed" (Complete,
        // unifying on $missionId). The body is just the tell.
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
                tell PointVisited with @x, @y to {tellDoneTo} once 'visited-' + @missionId;
            ");

        // The peer heard us: its ack is journaled on OUR side — project it too.
        perf.Actor.Reactions.DefineReaction("Acked")
            .Cue().Company().WithSharedHydration()
            .Seek("Acked")
                .OnMatch($@"
                    tell ack $id from {tellDoneTo}
                ")
            .Program.Emit(@"
                print @id 'tell';
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

    // ------------------------------------------------------------------
    // The mission loop: perceive -> drive -> produce the outcome.
    // Failure is defined here for now (turtlesim never fails on its own): the body
    // stalls against a wall, or the run times out. Whether the golem should know
    // its world (walls, obstacles, alternate routes) is an open design question.
    // ------------------------------------------------------------------
    private enum Outcome { Arrived, Stalled, Timeout }

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

            Outcome outcome = await GoToAsync(plan.X, plan.Y, ct);
            if (ct.IsCancellationRequested) break;

            string key = $"{golem}:mission:{plan.Id}";
            switch (outcome)
            {
                case Outcome.Arrived:
                    Produce("succeeded", $"{key}:succeeded", MissionSucceeded.Payload(plan.Id));
                    break;
                case Outcome.Stalled:
                    Produce("failed", $"{key}:failed", MissionFailed.Payload(plan.Id, "stuck against a wall"));
                    break;
                default:
                    Produce("failed", $"{key}:failed", MissionFailed.Payload(plan.Id, "timeout"));
                    break;
            }

            if (!await WaitUntilSettledAsync(plan.Id, ct))
                await Task.Delay(TimeSpan.FromSeconds(2), ct); // never re-drive on a timeout; back off and re-read
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

    // ------------------------------------------------------------------
    // The body: a simple controller against ROS telemetry. Stall detection: if
    // the distance stops improving for 3 s while we push, the body is against a
    // wall — no point in waiting out the whole timeout.
    // ------------------------------------------------------------------
    private async Task<Outcome> GoToAsync(double targetX, double targetY, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        double bestDistance = double.MaxValue;
        var lastImprovement = DateTime.UtcNow;

        while (DateTime.UtcNow - start < TimeSpan.FromSeconds(90) && !ct.IsCancellationRequested)
        {
            var pose = ros.LatestPose;
            if (pose == null) { await Task.Delay(100, ct); continue; }

            double dx = targetX - pose.X, dy = targetY - pose.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < 0.25)
            {
                await ros.DriveAsync(0, 0, ct);
                return Outcome.Arrived;
            }

            if (distance < bestDistance - 0.05)
            {
                bestDistance = distance;
                lastImprovement = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - lastImprovement > TimeSpan.FromSeconds(3))
            {
                await ros.DriveAsync(0, 0, ct);
                return Outcome.Stalled;
            }

            double heading = Math.Atan2(dy, dx);
            double deviation = NormalizeAngle(heading - pose.Theta);
            double angular = Math.Clamp(4.0 * deviation, -4.0, 4.0);
            double linear = Math.Abs(deviation) < 0.4 ? Math.Min(2.0, 1.5 * distance) : 0.0;

            await ros.DriveAsync(linear, angular, ct);
            await Task.Delay(100, ct);
        }
        if (!ct.IsCancellationRequested)
            await ros.DriveAsync(0, 0, ct);
        return Outcome.Timeout;
    }

    private static double NormalizeAngle(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }
}
