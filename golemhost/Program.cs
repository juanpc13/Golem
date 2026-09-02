using Choreography.Theater;
using GolemHost;
using GolemHost.Choreography;
using GolemHost.Domain;
using GolemHost.Membrane;
using GolemHost.Panel;
using Puppeteer;

// GolemHost bootstrap. The pieces:
//   Domain       — plain puppets (Golem, Mission): the DSL verbs.
//   Membrane     — rosbridge websocket (telemetry) + HttpBroker (tell wire).
//   Panel        — the page + the SSE feed behind it.
//   Controllers  — the actor's endpoints: assign/state/query/reset/events/tell.
//   Choreography — the wiring: upgrade birth, serial Dispatch, tells, mission loop.
// The journal is the only truth; if this process dies, it rehydrates and resumes.

string journalPath = Environment.GetEnvironmentVariable("JOURNAL_PATH")
                     ?? Path.Combine(AppContext.BaseDirectory, "journal");
string rosbridgeUrl = Environment.GetEnvironmentVariable("ROSBRIDGE_URL") ?? "ws://localhost:9090";
string golem = Environment.GetEnvironmentVariable("GOLEM") ?? "blue";     // who I am (names the journal)
string turtle = Environment.GetEnvironmentVariable("TURTLE") ?? "turtle1"; // which body I drive (ROS topics)
int panelPort = int.Parse(Environment.GetEnvironmentVariable("PANEL_PORT") ?? "8080");
string tellRoutes = Environment.GetEnvironmentVariable("TELL_ROUTES");     // topic=http://peer,... (the wire's route table)
string tellDoneTo = Environment.GetEnvironmentVariable("TELL_DONE_TO");    // peer golem to echo visited points to

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

var ros = new Rosbridge(rosbridgeUrl, turtle);
var feed = new PanelFeed();
var wire = new HttpBroker(golem, HttpBroker.ParseRoutes(tellRoutes));
var flow = new GolemChoreography(perf, ros, feed, wire, golem, turtle, tellDoneTo, journalPath);

// --- The controllers: the actor's endpoints. ---
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://*:{panelPort}");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddControllers();
builder.Services.AddSingleton(flow);
builder.Services.AddSingleton(feed);
builder.Services.AddSingleton(wire);

var app = builder.Build();
app.MapControllers();
app.Lifetime.ApplicationStopping.Register(() => shutdown.Cancel());

await app.StartAsync();
Console.WriteLine($"[golem {golem}] controllers listening on :{panelPort}");

feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "info", "",
    $"golem awake — rehydrated at entry {perf.CurrentEntryId}", DateTime.UtcNow));

// --- The choreography: announce birth, wire dispatch + tells. ---
flow.Awaken();

// --- The membrane: connect, ensure my body exists, then bind telemetry. ---
await ros.ConnectAsync(ct);
feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "",
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
feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "",
    $"body {turtle} ensured in the world (spawn at {spawnAt[0]},{spawnAt[1]}; pen {penRgb ?? "default"})",
    DateTime.UtcNow));

await ros.BindAsync(ct); // subscribe once the body is guaranteed to exist

// --- The mission loop, until shutdown. ---
try
{
    await flow.RunAsync(ct);
}
catch (OperationCanceledException) { }

await ros.DisposeAsync();
perf.Dispose();
await app.StopAsync();
Console.WriteLine($"[golem {golem}] clean shutdown at entry {perf.CurrentEntryId}");
