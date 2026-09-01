using System.Text.Json;
using Choreography.Theater;
using GolemHost.Domain;
using GolemHost.Membrane;
using GolemHost.Panel;
using Puppeteer;

// GolemHost: the first golem. A Puppeteer 2 actor with a FILESYSTEM journal
// that governs turtle1 through the rosbridge membrane, plus a debug panel
// (HTTP) that live-streams every journal commit.
//
// The loop: perceive (ephemeral pose) -> decide -> journal (transitions) -> act.
// If the process dies, the journal rehydrates and the golem resumes the pending mission.

string journalPath = Environment.GetEnvironmentVariable("JOURNAL_PATH")
                     ?? Path.Combine(AppContext.BaseDirectory, "journal");
string rosbridgeUrl = Environment.GetEnvironmentVariable("ROSBRIDGE_URL") ?? "ws://localhost:9090";
string golem = Environment.GetEnvironmentVariable("GOLEM") ?? "blue";     // who I am (names the journal)
string turtle = Environment.GetEnvironmentVariable("TURTLE") ?? "turtle1"; // which body I drive (ROS topics)
int panelPort = int.Parse(Environment.GetEnvironmentVariable("PANEL_PORT") ?? "8080");

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
var ct = shutdown.Token;

// --- The brain: Puppeteer 2 with an on-disk journal. No SQL, no transport. ---
var perf = new PerformanceV2(golem, typeof(Golem).Assembly);
perf.ConfigureStorage(DatabaseType.FileSystem, $"path={journalPath}");
perf.Start(); // rehydration from the journal happens here

var perfGate = new object(); // one writer, one gate: the loop and the panel share the actor

Console.WriteLine($"[golem {golem}] journal at {journalPath}");
Console.WriteLine($"[golem {golem}] rehydrated at entry {perf.CurrentEntryId}");

// --- The membrane (created early so the panel can read the pose). ---
await using var ros = new Rosbridge(rosbridgeUrl, turtle);

// --- The debug panel: operations + live journal feed. ---
ControlPanel panel = null;
panel = new ControlPanel(panelPort, AssignMission, StateJson, ResetEverything, AdHocQuery);
panel.Start(ct);
Console.WriteLine($"[golem {golem}] panel listening on :{panelPort}");

panel.Broadcast(new PanelEvent(perf.CurrentEntryId, "info", "",
    $"golem awake — rehydrated at entry {perf.CurrentEntryId}", DateTime.UtcNow));

if (perf.CurrentEntryId == 0)
{
    // A fresh journal holds only the birth. Missions arrive from the panel.
    long entry;
    lock (perfGate) { perf.PerformCmd("g = Golem();"); entry = perf.CurrentEntryId; }
    panel.Broadcast(new PanelEvent(entry, "command", "g = Golem();", "the golem is born", DateTime.UtcNow));
    Console.WriteLine($"[golem {golem}] born with an empty mission list (entry {entry}) — awaiting orders");
}
else
{
    Console.WriteLine($"[golem {golem}] pending missions on wake-up: {QryInt("{ print g.Pending() 'value'; }")}");
}

await ros.ConnectAsync(ct);
panel.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "",
    $"membrane connected to {rosbridgeUrl}", DateTime.UtcNow));

// --- The mission loop. ---
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

    if (arrived)
    {
        long entry;
        lock (perfGate)
        {
            perf.Actor.Using("g.Complete(id);")
                .WithParameters(p => { p["id", typeof(int)] = id; })
                .PerformCommand();
            entry = perf.CurrentEntryId;
        }
        Console.WriteLine($"[golem {golem}] mission {id} COMPLETED and journaled (entry {entry})");
        panel.Broadcast(new PanelEvent(entry, "command", $"g.Complete({id});",
            $"mission {id} completed", DateTime.UtcNow));
    }
    else if (!ct.IsCancellationRequested)
    {
        long entry;
        lock (perfGate)
        {
            perf.Actor.Using("g.Fail(id, reason);")
                .WithParameters(p => { p["id", typeof(int)] = id; p["reason", typeof(string)] = "timeout"; })
                .PerformCommand();
            entry = perf.CurrentEntryId;
        }
        Console.WriteLine($"[golem {golem}] mission {id} FAILED by timeout, journaled (entry {entry})");
        panel.Broadcast(new PanelEvent(entry, "command", $"g.Fail({id}, 'timeout');",
            $"mission {id} failed", DateTime.UtcNow));
    }
}

perf.Dispose();
Console.WriteLine($"[golem {golem}] clean shutdown at entry {perf.CurrentEntryId}");
return;

// Panel callback: queue a mission — journaled, then announced on the feed.
PanelEvent AssignMission(double x, double y)
{
    long entry;
    lock (perfGate)
    {
        perf.Actor.Using("g.Assign(x, y);")
            .WithParameters(p => { p["x", typeof(double)] = x; p["y", typeof(double)] = y; })
            .PerformCommand();
        entry = perf.CurrentEntryId;
    }
    var e = new PanelEvent(entry, "command", $"g.Assign({x:0.0}, {y:0.0});",
        $"mission queued for ({x:0.0}, {y:0.0})", DateTime.UtcNow);
    panel.Broadcast(e);
    return e;
}

// Panel callback: wipe this golem's journal and restart from scratch.
// A live actor holds its journal files open, so the honest reset is:
// stop the actor, delete its journal folder, reset the world, and exit —
// Docker's restart policy brings the process back, which rehydrates at
// entry 0 and seeds again. Reset reuses the resurrection machinery.
PanelEvent ResetEverything()
{
    var e = new PanelEvent(perf.CurrentEntryId, "info", "",
        "reset requested — wiping the journal and restarting the golem", DateTime.UtcNow);
    panel.Broadcast(e);
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

        lock (perfGate) { perf.Dispose(); }

        string actorDir = Path.Combine(journalPath, golem);
        if (Directory.Exists(actorDir))
            Directory.Delete(actorDir, recursive: true);

        Console.WriteLine($"[golem {golem}] journal wiped ({actorDir}) — exiting for a fresh start");
        Environment.Exit(0);
    });

    return e;
}

// Panel callback: ad-hoc PerformQry against the golem's in-memory state.
// Accepts a bare expression ("g.Pending()") and wraps it into query form,
// or a full DSL query block ("{ print ... 'key'; }") verbatim.
// Queries never touch the journal — but they are not sandboxed either:
// calling a mutating verb from a query changes memory WITHOUT journaling it,
// and that change evaporates on the next rehydration. Read-only by discipline.
string AdHocQuery(string script)
{
    script = script.Trim();
    if (script.Length == 0)
        return JsonSerializer.Serialize(new { error = "empty query" });
    if (!script.StartsWith("{"))
        script = "{ print " + script.TrimEnd(';') + " 'value'; }";
    try
    {
        lock (perfGate) return perf.PerformQry(script);
    }
    catch (Exception ex)
    {
        return JsonSerializer.Serialize(new { error = ex.Message });
    }
}

// Panel callback: snapshot of journaled state + ephemeral pose.
string StateJson()
{
    long entry;
    int pending, total;
    lock (perfGate)
    {
        entry = perf.CurrentEntryId;
        pending = QryIntUnlocked("{ print g.Pending() 'value'; }");
        total = QryIntUnlocked("{ print g.Total() 'value'; }");
    }
    var pose = ros.LatestPose;
    return JsonSerializer.Serialize(new
    {
        golem,
        turtle,
        entry,
        pending,
        total,
        pose = pose == null ? null : new { x = pose.X, y = pose.Y, theta = pose.Theta }
    });
}

// Simple controller: turn towards the target, advance, arrive at < 0.25 units.
async Task<bool> GoToAsync(double targetX, double targetY, CancellationToken token)
{
    var start = DateTime.UtcNow;
    while (DateTime.UtcNow - start < TimeSpan.FromSeconds(90) && !token.IsCancellationRequested)
    {
        var pose = ros.LatestPose;
        if (pose == null) { await Task.Delay(100, token); continue; }

        double dx = targetX - pose.X, dy = targetY - pose.Y;
        double distance = Math.Sqrt(dx * dx + dy * dy);
        if (distance < 0.25)
        {
            await ros.DriveAsync(0, 0, token);
            return true;
        }

        double heading = Math.Atan2(dy, dx);
        double deviation = NormalizeAngle(heading - pose.Theta);
        double angular = Math.Clamp(4.0 * deviation, -4.0, 4.0);
        double linear = Math.Abs(deviation) < 0.4 ? Math.Min(2.0, 1.5 * distance) : 0.0;

        await ros.DriveAsync(linear, angular, token);
        await Task.Delay(100, token);
    }
    if (!token.IsCancellationRequested)
        await ros.DriveAsync(0, 0, token);
    return false;
}

static double NormalizeAngle(double a)
{
    while (a > Math.PI) a -= 2 * Math.PI;
    while (a < -Math.PI) a += 2 * Math.PI;
    return a;
}

int QryInt(string script)
{
    lock (perfGate) return QryIntUnlocked(script);
}

int QryIntUnlocked(string script) =>
    JsonDocument.Parse(perf.PerformQry(script)).RootElement.GetProperty("value").GetInt32();

double QryDouble(string script)
{
    lock (perfGate)
        return JsonDocument.Parse(perf.PerformQry(script)).RootElement.GetProperty("value").GetDouble();
}
