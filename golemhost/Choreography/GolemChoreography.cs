using System.Text.Json;
using Choreography.Input;
using Choreography.Theater;
using Choreography.Transport.Brokered;
using GolemHost.Domain;
using GolemHost.Membrane;
using GolemHost.Panel;

namespace GolemHost.Choreography;

// The golem's choreography. Every WRITE flows through one serial Dispatch
// (consume-and-dispatch pattern): the panel and the mission loop are producers
// on an in-process ops topic; the typed handlers are the only place a command
// is performed. Accepted (queued on the topic) is not committed (journaled) —
// the journal remains the only truth, and the ops queue is ephemeral on purpose.
internal sealed class GolemChoreography
{
    private readonly GolemPerformance perf;
    private readonly Rosbridge ros;
    private readonly ControlPanel panel;
    private readonly string golem;
    private readonly string turtle;
    private readonly InProcessBroker ops = new();
    private readonly string topic;

    public GolemChoreography(GolemPerformance perf, Rosbridge ros, ControlPanel panel,
                             string golem, string turtle)
    {
        this.perf = perf;
        this.ros = ros;
        this.panel = panel;
        this.golem = golem;
        this.turtle = turtle;
        topic = $"{golem}-ops";
    }

    // The birth itself already happened inside perf.Start(): GolemPerformance's
    // OnHydrated performed the upgrade chain (LottoPerformance-style). Here we
    // only announce it and wire the dispatch.
    public void Awaken()
    {
        panel.Broadcast(perf.BornThisBoot
            ? new PanelEvent(perf.CurrentEntryId, "command", "upgrade('birth') { g = Golem(); };",
                "the golem is born — first hydration ran the upgrade chain", DateTime.UtcNow)
            : new PanelEvent(perf.CurrentEntryId, "info", "",
                "upgrade chain no-op — this golem was already born", DateTime.UtcNow));
        Console.WriteLine(perf.BornThisBoot
            ? $"[golem {golem}] born by upgrade('birth') (entry {perf.CurrentEntryId})"
            : $"[golem {golem}] upgrade chain no-op — awake at entry {perf.CurrentEntryId}");

        perf.CreateDispatch(o => o.MaxParallelism = 1) // one golem, one serial command flow
            .On<MissionOrdered>((actor, m) =>
            {
                actor
                    .Using("g.Assign(x, y);")
                    .WithParameters(p =>
                    {
                        p.UserParameter("x", m.X);
                        p.UserParameter("y", m.Y);
                    })
                    .PerformCommand();
                panel.Broadcast(new PanelEvent(perf.CurrentEntryId, "command",
                    $"g.Assign({m.X:0.0}, {m.Y:0.0});",
                    $"mission queued for ({m.X:0.0}, {m.Y:0.0})", DateTime.UtcNow));
            })
            .On<MissionSucceeded>((actor, m) =>
            {
                actor
                    .Using("g.Complete(id);")
                    .WithParameters(p => p.UserParameter("id", m.Id))
                    .PerformCommand();
                panel.Broadcast(new PanelEvent(perf.CurrentEntryId, "command",
                    $"g.Complete({m.Id});", $"mission {m.Id} completed", DateTime.UtcNow));
                Console.WriteLine($"[golem {golem}] mission {m.Id} COMPLETED and journaled (entry {perf.CurrentEntryId})");
            })
            .On<MissionFailed>((actor, m) =>
            {
                actor
                    .Using("g.Fail(id, reason);")
                    .WithParameters(p =>
                    {
                        p.UserParameter("id", m.Id);
                        p.UserParameter("reason", m.Reason);
                    })
                    .PerformCommand();
                panel.Broadcast(new PanelEvent(perf.CurrentEntryId, "command",
                    $"g.Fail({m.Id}, '{m.Reason}');", $"mission {m.Id} failed: {m.Reason}", DateTime.UtcNow));
                Console.WriteLine($"[golem {golem}] mission {m.Id} FAILED ({m.Reason}), journaled (entry {perf.CurrentEntryId})");
            })
            .ConsumeFrom(
                new BrokerInputSource(ops, topic),
                signal => new DispatchCommand(signal.Id, signal.Value)); // producers pre-tag the value
    }

    // Producer side — the panel orders a mission. Accepted, not yet committed:
    // the journaled g.Assign arrives on the feed when the dispatch handler commits.
    public PanelEvent OrderMission(double x, double y)
    {
        Produce(MissionOrdered.Wire(x, y));
        var e = new PanelEvent(perf.CurrentEntryId, "runtime", "",
            $"mission ordered for ({x:0.0}, {y:0.0}) — queued to dispatch", DateTime.UtcNow);
        panel.Broadcast(e);
        return e;
    }

    // The mission loop: perceive -> decide -> produce to dispatch -> act.
    public async Task RunAsync(CancellationToken ct)
    {
        while (!ct.IsCancellationRequested)
        {
            int id = QryInt("{ print g.NextId() 'value'; }");
            if (id < 0)
            {
                await Task.Delay(TimeSpan.FromSeconds(2), ct);
                continue;
            }

            double targetX = QryDouble("{ print g.NextX() 'value'; }");
            double targetY = QryDouble("{ print g.NextY() 'value'; }");
            Console.WriteLine($"[golem {golem}] mission {id}: go to ({targetX:0.0}, {targetY:0.0})");
            panel.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "",
                $"mission {id} started — driving to ({targetX:0.0}, {targetY:0.0})", DateTime.UtcNow));

            bool arrived = await GoToAsync(targetX, targetY, ct);
            if (ct.IsCancellationRequested) break;

            Produce(arrived ? MissionSucceeded.Wire(id) : MissionFailed.Wire(id, "timeout"));
            await WaitUntilSettledAsync(id, ct); // don't re-drive before the handler commits
        }
    }

    public string StateJson()
    {
        var pose = ros.LatestPose;
        return JsonSerializer.Serialize(new
        {
            golem,
            turtle,
            entry = perf.CurrentEntryId,
            pending = QryInt("{ print g.Pending() 'value'; }"),
            total = QryInt("{ print g.Total() 'value'; }"),
            pose = pose == null ? null : new { x = pose.X, y = pose.Y, theta = pose.Theta }
        });
    }

    // Ad-hoc PerformQry against in-memory state. Queries never touch the journal —
    // but they are not sandboxed: keep them read-only by discipline.
    public string AdHocQuery(string script)
    {
        script = script.Trim();
        if (script.Length == 0)
            return JsonSerializer.Serialize(new { error = "empty query" });
        if (!script.StartsWith("{"))
            script = "{ print " + script.TrimEnd(';') + " 'value'; }";
        try
        {
            return perf.PerformQry(script);
        }
        catch (Exception ex)
        {
            return JsonSerializer.Serialize(new { error = ex.Message });
        }
    }

    private void Produce(string taggedMessage) =>
        ops.ProduceAsync(topic, Guid.NewGuid().ToString("N"), null, taggedMessage)
           .GetAwaiter().GetResult();

    private async Task WaitUntilSettledAsync(int id, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        while (QryInt("{ print g.NextId() 'value'; }") == id
               && DateTime.UtcNow - start < TimeSpan.FromSeconds(5)
               && !ct.IsCancellationRequested)
        {
            await Task.Delay(100, ct);
        }
    }

    // Simple controller: turn towards the target, advance, arrive at < 0.25 units.
    private async Task<bool> GoToAsync(double targetX, double targetY, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < TimeSpan.FromSeconds(90) && !ct.IsCancellationRequested)
        {
            var pose = ros.LatestPose;
            if (pose == null) { await Task.Delay(100, ct); continue; }

            double dx = targetX - pose.X, dy = targetY - pose.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < 0.25)
            {
                await ros.DriveAsync(0, 0, ct);
                return true;
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
        return false;
    }

    private static double NormalizeAngle(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }

    private int QryInt(string script) =>
        JsonDocument.Parse(perf.PerformQry(script)).RootElement.GetProperty("value").GetInt32();

    private double QryDouble(string script) =>
        JsonDocument.Parse(perf.PerformQry(script)).RootElement.GetProperty("value").GetDouble();
}
