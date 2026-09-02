using Choreography.Theater;
using GolemHost;
using GolemHost.Choreography;
using GolemHost.Domain;
using GolemHost.Membrane;
using GolemHost.Panel;
using Puppeteer;

// GolemHost bootstrap. The pieces:
//   Domain       — plain puppets (Golem, Mission): the DSL verbs.
//   Membrane     — rosbridge websocket: ephemeral telemetry in, cmd_vel out.
//   Panel        — HTTP debug surface: operations in, live journal feed out.
//   Choreography — the wiring: upgrade birth, serial Dispatch, mission loop.
// The journal is the only truth; if this process dies, it rehydrates and resumes.

string journalPath = Environment.GetEnvironmentVariable("JOURNAL_PATH")
                     ?? Path.Combine(AppContext.BaseDirectory, "journal");
string rosbridgeUrl = Environment.GetEnvironmentVariable("ROSBRIDGE_URL") ?? "ws://localhost:9090";
string golem = Environment.GetEnvironmentVariable("GOLEM") ?? "blue";     // who I am (names the journal)
string turtle = Environment.GetEnvironmentVariable("TURTLE") ?? "turtle1"; // which body I drive (ROS topics)
int panelPort = int.Parse(Environment.GetEnvironmentVariable("PANEL_PORT") ?? "8080");

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
var ct = shutdown.Token;

// --- The actor: Puppeteer 2 with an on-disk journal. No SQL, no transport.
//     GolemPerformance owns its initialization: OnHydrated performs the upgrade
//     chain during Start (LottoPerformance-style versioned hydration). ---
var perf = new GolemPerformance(golem, typeof(Golem).Assembly);
perf.ConfigureStorage(DatabaseType.FileSystem, $"path={journalPath}");
perf.Start(); // rehydration + upgrade chain happen here

Console.WriteLine($"[golem {golem}] journal at {journalPath}");
Console.WriteLine($"[golem {golem}] rehydrated at entry {perf.CurrentEntryId}");

await using var ros = new Rosbridge(rosbridgeUrl, turtle);

// --- The panel, wired to the choreography by closure. ---
GolemChoreography flow = null;
ControlPanel panel = null;
panel = new ControlPanel(panelPort,
    (x, y) => flow.OrderMission(x, y),
    () => flow.StateJson(),
    ResetEverything,
    script => flow.AdHocQuery(script));
panel.Start(ct);
Console.WriteLine($"[golem {golem}] panel listening on :{panelPort}");

panel.Broadcast(new PanelEvent(perf.CurrentEntryId, "info", "",
    $"golem awake — rehydrated at entry {perf.CurrentEntryId}", DateTime.UtcNow));

// --- The choreography: birth by upgrade, then the serial dispatch. ---
flow = new GolemChoreography(perf, ros, panel, golem, turtle);
flow.Awaken();

// --- The membrane: connect, ensure my body exists, then bind telemetry. ---
await ros.ConnectAsync(ct);
panel.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "",
    $"membrane connected to {rosbridgeUrl}", DateTime.UtcNow));

// Spawn is idempotent by refusal: if the turtle already exists, turtlesim
// rejects it and we drive the existing one. Then sign the world with our pen.
var spawnAt = (Environment.GetEnvironmentVariable("SPAWN_AT") ?? "5.5,5.5")
    .Split(',', StringSplitOptions.TrimEntries);
await ros.CallServiceAsync("/spawn", new
{
    x = double.Parse(spawnAt[0], System.Globalization.CultureInfo.InvariantCulture),
    y = double.Parse(spawnAt[1], System.Globalization.CultureInfo.InvariantCulture),
    theta = 0.0,
    name = turtle
}, ct);

string penRgb = Environment.GetEnvironmentVariable("PEN_RGB");
if (penRgb != null)
{
    var rgb = penRgb.Split(',', StringSplitOptions.TrimEntries);
    await ros.CallServiceAsync($"/{turtle}/set_pen", new
    {
        r = byte.Parse(rgb[0]), g = byte.Parse(rgb[1]), b = byte.Parse(rgb[2]),
        width = (byte)3, off = (byte)0
    }, ct);
}
panel.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "",
    $"body {turtle} ensured in the world (spawn at {spawnAt[0]},{spawnAt[1]}; pen {penRgb ?? "default"})",
    DateTime.UtcNow));

await ros.BindAsync(ct); // subscribe once the body is guaranteed to exist

// --- The mission loop, until shutdown. ---
try
{
    await flow.RunAsync(ct);
}
catch (OperationCanceledException) { }

perf.Dispose();
Console.WriteLine($"[golem {golem}] clean shutdown at entry {perf.CurrentEntryId}");
return;

// Panel callback: wipe this golem's journal and restart from scratch.
// A live actor holds its journal files open, so the honest reset is:
// stop the actor, delete its journal folder, reset the world, and exit —
// Docker's restart policy brings the process back, which rehydrates at
// entry 0 and is reborn by the upgrade. Reset reuses the resurrection machinery.
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

        // No graceful shutdown here on purpose: cancelling would race Main to exit
        // before the wipe. On Linux the unlink works with the files still open,
        // and Exit tears the process down right after.
        string actorDir = Path.Combine(journalPath, golem);
        if (Directory.Exists(actorDir))
            Directory.Delete(actorDir, recursive: true);

        Console.WriteLine($"[golem {golem}] journal wiped ({actorDir}) — exiting for a fresh start");
        Environment.Exit(0);
    });

    return e;
}
