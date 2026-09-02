using System.Text.Json;
using Choreography.Input;
using Choreography.Told;
using Choreography.Transport.Brokered;
using GolemHost.Domain;
using GolemHost.Membrane;
using GolemHost.Panel;

namespace GolemHost.Choreography;

// The golem's choreography. Every WRITE flows through one serial Dispatch
// (consume-and-dispatch pattern): the controllers and the mission loop are
// producers on an in-process ops topic; the typed handlers are the only place
// a command is performed. Accepted (queued on the topic) is not committed
// (journaled) — the journal remains the only truth.
public sealed class GolemChoreography
{
    private readonly GolemPerformance perf;
    private readonly Rosbridge ros;
    private readonly PanelFeed feed;
    private readonly HttpBroker wire;
    private readonly string golem;
    private readonly string turtle;
    private readonly string tellDoneTo;
    private readonly string journalPath;
    private readonly InProcessBroker ops = new();
    private readonly string topic;
    private ToldListener toldListener;

    internal GolemChoreography(GolemPerformance perf, Rosbridge ros, PanelFeed feed, HttpBroker wire,
                               string golem, string turtle, string tellDoneTo, string journalPath)
    {
        this.perf = perf;
        this.ros = ros;
        this.feed = feed;
        this.wire = wire;
        this.golem = golem;
        this.turtle = turtle;
        this.tellDoneTo = tellDoneTo;
        this.journalPath = journalPath;
        topic = $"{golem}-ops";
    }

    // The birth itself already happened inside perf.Start(): GolemPerformance's
    // OnHydrated performed the upgrade chain (LottoPerformance-style). Here we
    // only announce it and wire the dispatch + the tell layer.
    public void Awaken()
    {
        feed.Broadcast(perf.BornThisBoot
            ? new PanelEvent(perf.CurrentEntryId, "command", "upgrade('init') { g = Golem(); };",
                "the golem is born — first hydration ran the upgrade chain", DateTime.UtcNow)
            : new PanelEvent(perf.CurrentEntryId, "info", "",
                "upgrade chain no-op — this golem was already born", DateTime.UtcNow));
        Console.WriteLine(perf.BornThisBoot
            ? $"[golem {golem}] born by upgrade('init') (entry {perf.CurrentEntryId})"
            : $"[golem {golem}] upgrade chain no-op — awake at entry {perf.CurrentEntryId}");

        perf.CreateDispatch(o => o.MaxParallelism = 1) // one golem, one serial command flow
            .On<MissionOrdered>((actor, m) =>
            {
                // The handler mints the mission's handle (serial dispatch: no races) so the
                // journaled Assign carries the id a Reaction can later correlate on.
                int id = QryInt("{ print g.NextHandle() 'value'; }");
                Narrated(() => actor
                        .Using("g.Assign(@id, @x, @y);")
                        .WithParameters(p =>
                        {
                            p.UserParameter("id", id);
                            p.UserParameter("x", m.X);
                            p.UserParameter("y", m.Y);
                        })
                        .PerformCommand(),
                    entry => new PanelEvent(entry, "command",
                        $"g.Assign({id}, {m.X:0.0}, {m.Y:0.0});",
                        $"mission {id} queued for ({m.X:0.0}, {m.Y:0.0})", DateTime.UtcNow));
            })
            .On<MissionSucceeded>((actor, m) =>
            {
                // One fact, one statement: the mission is completed. Its point already
                // lives in the journal (the Assign) — the tell reaction reads it from there.
                Narrated(() => actor
                        .Using("g.Complete(@id);")
                        .WithParameters(p => p.UserParameter("id", m.Id))
                        .PerformCommand(),
                    entry => new PanelEvent(entry, "command",
                        $"g.Complete({m.Id});", $"mission {m.Id} completed", DateTime.UtcNow));
                Console.WriteLine($"[golem {golem}] mission {m.Id} COMPLETED and journaled (entry {perf.CurrentEntryId})");
            })
            .On<MissionFailed>((actor, m) =>
            {
                Narrated(() => actor
                        .Using("g.Fail(@id, @reason);")
                        .WithParameters(p =>
                        {
                            p.UserParameter("id", m.Id);
                            p.UserParameter("reason", m.Reason);
                        })
                        .PerformCommand(),
                    entry => new PanelEvent(entry, "command",
                        $"g.Fail({m.Id}, '{m.Reason}');", $"mission {m.Id} failed: {m.Reason}", DateTime.UtcNow));
                Console.WriteLine($"[golem {golem}] mission {m.Id} FAILED ({m.Reason}), journaled (entry {perf.CurrentEntryId})");
            })
            .ConsumeFrom(
                new BrokerInputSource(ops, topic),
                signal => new DispatchCommand(signal.Id, signal.Value)); // producers pre-tag the value

        // The journal watch feeds the panel's journal lane with EVERY entry written —
        // ours and the engine's (tell, ack, Told uptake). In V2 a parametric command
        // is a define (template) entry the first time plus an action (args) entry per
        // invocation; the wire frame is peeked (debug-grade) so each row names the
        // template it invokes and its arguments. The grace delay batches a command's
        // entries so they flush together, in entry order.
        WarmTemplateCache();
        perf.WatchJournal((entryId, wire) =>
        {
            lock (pendingWrites) pendingWrites.Add((entryId, wire));
            _ = Task.Run(async () =>
            {
                await Task.Delay(600);
                FlushEngineWrites();
            });
        });

        EnableTells();
    }

    // Defines seen so far (actionId -> template body): a later action row can
    // say WHICH template it invokes. Warmed from the journal at boot so templates
    // defined in earlier lives of this golem resolve too.
    private readonly System.Collections.Concurrent.ConcurrentDictionary<int, string> templates = new();

    // Engine writes wait out a grace period, then flush IN ENTRY ORDER: a define
    // always precedes the action that invokes it, so the template cache is warm
    // by the time the action row is rendered, and the feed reads chronologically.
    private readonly List<(long EntryId, byte[] Wire)> pendingWrites = new();

    private void FlushEngineWrites()
    {
        (long EntryId, byte[] Wire)[] batch;
        lock (pendingWrites)
        {
            batch = pendingWrites.OrderBy(p => p.EntryId).ToArray();
            pendingWrites.Clear();
        }
        foreach (var (entryId, wire) in batch)
        {
            feed.Broadcast(DescribeEngineWrite(entryId, wire));
        }
    }

    private void WarmTemplateCache()
    {
        foreach (var record in perf.ReadJournalAfter(0))
            if (JournalPeek.TryDescribe(record.Record, out var peek) && peek.Kind == "define")
                templates[peek.ActionId] = TemplateBody(peek.Text);
    }

    private PanelEvent DescribeEngineWrite(long entryId, byte[] wire)
    {
        if (!JournalPeek.TryDescribe(wire, out var peek))
            return new PanelEvent(entryId, "command", "", "unreadable frame", DateTime.UtcNow);

        switch (peek.Kind)
        {
            case "define":
                templates[peek.ActionId] = TemplateBody(peek.Text);
                return new PanelEvent(entryId, "command", JournalPeek.OneLine(peek.Text),
                    $"define #{peek.ActionId} — the template (script)", DateTime.UtcNow);
            case "action":
                string template = templates.TryGetValue(peek.ActionId, out var body) ? body : $"action #{peek.ActionId}";
                string expose = peek.ExposeData == null ? "" : $" · expose {JournalPeek.OneLine(peek.ExposeData, 60)}";
                return new PanelEvent(entryId, "command", $"{template} ← args {JournalPeek.OneLine(peek.Text, 80)}",
                    $"action #{peek.ActionId}{expose}", DateTime.UtcNow);
            default:
                return new PanelEvent(entryId, "command", JournalPeek.OneLine(peek.Text),
                    "script", DateTime.UtcNow);
        }
    }

    // "define action 7 (x, y) as g.Assign(x, y); end;" -> "g.Assign(x, y);"
    private static string TemplateBody(string defineSentence)
    {
        string flat = JournalPeek.OneLine(defineSentence, 400);
        int asAt = flat.IndexOf(" as ", StringComparison.Ordinal);
        string body = asAt >= 0 ? flat[(asAt + 4)..] : flat;
        body = body.TrimEnd();
        if (body.EndsWith("end;", StringComparison.Ordinal)) body = body[..^4].TrimEnd();
        return JournalPeek.OneLine(body, 90);
    }

    // Perform, then narrate on the RUNTIME lane. The journal lane is fed only by
    // the journal watch, so it shows exactly what was written (define, action, script)
    // — never our paraphrase of it.
    private void Narrated(Action perform, Func<long, PanelEvent> row)
    {
        perform();
        feed.Broadcast(row(perf.CurrentEntryId) with { Kind = "runtime" });
    }

    // Cross-golem speech over the HttpBroker wire (after the bingo's PhoneToPhone):
    // the binding table maps the ADDRESSEE role to a topic, the route table maps
    // the topic to the peer container. The DSL never names the transport.
    private void EnableTells()
    {
        var bindings = new TellBindingTable();
        bindings.Bind(golem, $"tell-{golem}");
        if (tellDoneTo != null)
            bindings.Bind(tellDoneTo, $"tell-{tellDoneTo}");

        perf.UseTellTransport(new BrokerTellTransport(wire, bindings, golem));

        // Uptake: whatever point a peer visited becomes a mission of MY own —
        // the hearer's verb, one journaled perform per tell.
        toldListener = perf
            .ListenAs(golem, bindings, wire)
            .Told("PointVisited").With<double>("x").With<double>("y")
                .Command("g.Assign(@x, @y);")
            .Start();
        feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "",
            $"listening for tells as '{golem}' on topic 'tell-{golem}'", DateTime.UtcNow));
        Console.WriteLine($"[golem {golem}] listening for tells on topic 'tell-{golem}'");

        if (tellDoneTo != null)
        {
            // tell is reaction-only, and the reaction is a correlated chain: "this mission
            // was ordered" (Assign, capturing its point) closed by "this SAME mission
            // completed" (Complete, unifying on $missionId). The body is just the tell —
            // the point travels as captures, straight from the journaled Assign.
            perf.Actor.Reactions.DefineReaction("echo-visited-point")
                .Cue().Company().WithSharedHydration()
                .Seek("Ordered").One()
                    .OnMatch("[_:Golem].Assign($missionId, $x, $y)")
                .ThenSeek("Done").One()
                    .OnMatch("[_:Golem].Complete($missionId)")
                .Causation.Continue($"tell PointVisited with @x, @y to {tellDoneTo} once 'visited-' + @missionId;");

            feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "",
                $"on every completed mission I will tell '{tellDoneTo}' the visited point", DateTime.UtcNow));
            Console.WriteLine($"[golem {golem}] will tell '{tellDoneTo}' every visited point");

            // The .Cue() continuous push loop holds the calling thread — run it on
            // its own task so the boot sequence (membrane, spawn, mission loop)
            // can proceed.
            _ = Task.Run(() =>
            {
                try { perf.Actor.Reactions.Execute(); }
                catch (Exception ex)
                {
                    Console.WriteLine($"[golem {golem}] reactions loop ended: {ex.Message}");
                }
            });
        }
    }

    // Producer side — a controller orders a mission. Accepted, not yet committed:
    // the journaled g.Assign arrives on the feed when the dispatch handler commits.
    public PanelEvent OrderMission(double x, double y)
    {
        Produce(MissionOrdered.Wire(x, y));
        var e = new PanelEvent(perf.CurrentEntryId, "runtime", "",
            $"mission ordered for ({x:0.0}, {y:0.0}) — queued to dispatch", DateTime.UtcNow);
        feed.Broadcast(e);
        return e;
    }

    // Wipe this golem's journal and restart from scratch. A live actor holds its
    // journal files open, so the honest reset is: reset the world, delete the
    // journal folder (Linux unlinks happily under open handles), and exit —
    // Docker's restart policy brings the process back, reborn by the upgrade.
    public PanelEvent ResetEverything()
    {
        var e = new PanelEvent(perf.CurrentEntryId, "info", "",
            "reset requested — wiping the journal and restarting the golem", DateTime.UtcNow);
        feed.Broadcast(e);
        Console.WriteLine($"[golem {golem}] RESET requested from the panel");

        _ = Task.Run(async () =>
        {
            await Task.Delay(1000); // let the HTTP response and the SSE frame leave

            try
            {
                await ros.DriveAsync(0, 0, CancellationToken.None);
                await ros.CallServiceAsync($"/{turtle}/teleport_absolute",
                    new { x = 5.544445, y = 5.544445, theta = 0.0 }, CancellationToken.None);
                await ros.CallServiceAsync("/clear", null, CancellationToken.None);
            }
            catch { /* world reset is best-effort; the journal wipe is the point */ }

            string actorDir = Path.Combine(journalPath, golem);
            if (Directory.Exists(actorDir))
                Directory.Delete(actorDir, recursive: true);

            Console.WriteLine($"[golem {golem}] journal wiped ({actorDir}) — exiting for a fresh start");
            Environment.Exit(0);
        });

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
            feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "",
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
