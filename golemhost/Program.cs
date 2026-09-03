using Choreography.Theater;
using GolemHost;
using GolemHost.Choreography;
using GolemHost.Domain;
using GolemHost.Membrane;
using GolemHost.Navigation;
using GolemHost.Panel;
using Puppeteer;

// The DSL renders numbers into the journal with the current culture: pin it, or a
// Spanish-locale host would journal 11,08 and rehydrate Inhabit with four arguments.
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

// GolemHost bootstrap — the carátula around one actor. The pieces:
//   golemdomain/   — plain puppets (Golem, Mission, World, Rock): the DSL verbs.
//   Membrane       — rosbridge websocket (telemetry) + HttpBroker (tell wire).
//   Navigation     — the seam to the body's locomotion (TurtlesimNavigator today, Nav2 tomorrow).
//   Panel          — the page, the SSE feed, and the sink the golem's projections push to.
//   Controllers    — the actor's endpoints (GolemController) and the operator's (OperatorController).
//   Choreography   — reactions (views, speech), the ops Saga, the mission loop.
// The journal is the only truth; if this process dies, it rehydrates and resumes.

string journalPath = Environment.GetEnvironmentVariable("JOURNAL_PATH")
                     ?? Path.Combine(AppContext.BaseDirectory, "journal");
string rosbridgeUrl = Environment.GetEnvironmentVariable("ROSBRIDGE_URL") ?? "ws://localhost:9090";
string golem = Environment.GetEnvironmentVariable("GOLEM") ?? "blue";     // who I am (names the journal)
string turtle = Environment.GetEnvironmentVariable("TURTLE") ?? "turtle1"; // which body I drive (ROS topics)
int panelPort = int.Parse(Environment.GetEnvironmentVariable("PANEL_PORT") ?? "8080");
string tellRoutes = Environment.GetEnvironmentVariable("TELL_ROUTES");     // topic=http://peer,... (the wire's route table)
string tellDoneTo = Environment.GetEnvironmentVariable("TELL_DONE_TO");    // peer golem to echo visited points to
var tellRetry = TimeSpan.FromSeconds(int.Parse(Environment.GetEnvironmentVariable("TELL_RETRY_SECONDS") ?? "30"));

using var shutdown = new CancellationTokenSource();
Console.CancelKeyPress += (_, e) => { e.Cancel = true; shutdown.Cancel(); };
var ct = shutdown.Token;

// --- The actor. Storage first; then the tell transport and every Reaction, because
//     Start is what arms them and runs the release chain (OnHydrated). ---
var perf = new GolemPerformance(golem, GolemDomain.Assembly);
perf.ConfigureStorage(DatabaseType.FileSystem, $"path={journalPath}");

var ros = new Rosbridge(rosbridgeUrl, turtle);
var navigator = new TurtlesimNavigator(ros);
var feed = new PanelFeed();
var wire = new HttpBroker(golem, HttpBroker.ParseRoutes(tellRoutes), tellRetry);
if (tellDoneTo != null && !wire.CanRoute($"tell-{tellDoneTo}"))
    throw new InvalidOperationException($"TELL_ROUTES lacks 'tell-{tellDoneTo}' — tells to '{tellDoneTo}' would have nowhere to go");

var flow = new GolemChoreography(perf, ros, navigator, feed, wire, golem, turtle, tellDoneTo);
flow.DefineReactions();
perf.OutputTarget(new PanelSink(feed));

perf.Start(); // rehydration + release chain + the .Cue() reactions come alive here
Console.WriteLine($"[golem {golem}] journal at {journalPath}");
Console.WriteLine($"[golem {golem}] rehydrated at entry {perf.CurrentEntryId}");

// --- The controllers: the actor's endpoints and the operator's. ---
var builder = WebApplication.CreateBuilder(args);
builder.WebHost.UseUrls($"http://*:{panelPort}");
builder.Logging.SetMinimumLevel(LogLevel.Warning);
builder.Services.AddControllers();
builder.Services.AddSingleton<PerformanceV2>(perf);
builder.Services.AddSingleton(flow);
builder.Services.AddSingleton(feed);
builder.Services.AddSingleton(wire);
builder.Services.AddSingleton(ros);
builder.Services.AddSingleton(new GolemIdentity(golem, turtle));

var app = builder.Build();
app.MapControllers();
app.Lifetime.ApplicationStopping.Register(() => shutdown.Cancel());

await app.StartAsync();
Console.WriteLine($"[golem {golem}] controllers listening on :{panelPort}");

feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "info", "",
    $"golem awake — rehydrated at entry {perf.CurrentEntryId}", DateTime.UtcNow));

// --- The choreography: the ops Saga and the tell uptake. ---
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
