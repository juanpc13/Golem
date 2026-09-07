using Choreography.Theater;
using GolemHost;
using GolemHost.Choreography;
using GolemHost.Domain;
using GolemHost.Membrane;
using GolemHost.Navigation;
using GolemHost.Panel;
using Puppeteer;

// The DSL renders numbers into the journal with the current culture: pin it, or a
// Spanish-locale host would journal 11,08 and rehydrate a verb with the wrong arity.
System.Globalization.CultureInfo.DefaultThreadCurrentCulture = System.Globalization.CultureInfo.InvariantCulture;
System.Globalization.CultureInfo.CurrentCulture = System.Globalization.CultureInfo.InvariantCulture;

// GolemHost bootstrap — the carátula around one actor. The pieces:
//   golemdomain/   — plain puppets (Golem, Mission, Place, Atlas): the DSL verbs.
//   Membrane       — rosbridge websocket (telemetry, drive, teleport) + HttpBroker (tell wire).
//   Navigation     — the seam to the body's locomotion (DiffDriveNavigator in Gazebo today, Nav2 tomorrow).
//   Panel          — the page, the SSE feed, and the tap that shows the journal record by record.
//   Controllers    — the actor's endpoints (GolemController) and the operator's (OperatorController).
//   Choreography   — reactions (speech), the ops Saga, the mission loop.
// The journal is the only truth; if this process dies, it rehydrates and resumes.

string journalPath = Environment.GetEnvironmentVariable("JOURNAL_PATH")
                     ?? Path.Combine(AppContext.BaseDirectory, "journal");
string rosbridgeUrl = Environment.GetEnvironmentVariable("ROSBRIDGE_URL") ?? "ws://localhost:9090";
string golem = Environment.GetEnvironmentVariable("GOLEM") ?? "blue";  // who I am (names the journal)
string body = Environment.GetEnvironmentVariable("BODY") ?? golem;      // the model I drive in the world (/model/<body>/...)
var home = ParsePoint(Environment.GetEnvironmentVariable("HOME_AT") ?? "5.5,5.5"); // the body's mark: reborn or let go, it is put back here
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

var ros = new Rosbridge(rosbridgeUrl, body);
GolemChoreography flow = null;
var navigator = new DiffDriveNavigator(ros, () => flow.Speed(), () => flow.Radius()); // speed and size: the body the golem declared in its journal
var feed = new PanelFeed();
var wire = new HttpBroker(golem, HttpBroker.ParseRoutes(tellRoutes), tellRetry);
if (tellDoneTo != null && !wire.CanRoute($"tell-{tellDoneTo}"))
    throw new InvalidOperationException($"TELL_ROUTES lacks 'tell-{tellDoneTo}' — tells to '{tellDoneTo}' would have nowhere to go");

flow = new GolemChoreography(perf, ros, navigator, feed, wire, golem, body, home, tellDoneTo, journalPath);
flow.DefineReactions();

perf.Start(); // rehydration + release chain + the .Cue() reactions come alive here
new JournalTap(perf, feed).Start(); // the panel's journal lane: the whole diary, then every record as it lands
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
builder.Services.AddSingleton(new GolemIdentity(golem, body));

var app = builder.Build();
app.MapControllers();
app.Lifetime.ApplicationStopping.Register(() => shutdown.Cancel());

await app.StartAsync();
Console.WriteLine($"[golem {golem}] controllers listening on :{panelPort}");

feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "info", "",
    $"golem awake — rehydrated at entry {perf.CurrentEntryId}", DateTime.UtcNow));

// --- The choreography: the ops Saga and the tell uptake. ---
flow.Awaken();

// --- The membrane: connect and bind to my body. The body exists in the world from the start
//     (the sim builds it from the floor plan). A REBORN golem puts it back on its mark; a golem
//     that merely restarted resumes with the body where it stands — as with a real robot. ---
await ros.ConnectAsync(ct);
await ros.BindAsync(ct);
feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "",
    $"membrane connected to {rosbridgeUrl} — driving body '{body}'", DateTime.UtcNow));
if (perf.BornThisBoot)
{
    await Task.Delay(500, ct); // let the advertise settle before the first publish
    await ros.TeleportAsync(home.X, home.Y, 0.0, ct);
    feed.Broadcast(new PanelEvent(perf.CurrentEntryId, "runtime", "",
        $"reborn — body '{body}' put back on its mark at ({home.X}, {home.Y})", DateTime.UtcNow));
}

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

static (double X, double Y) ParsePoint(string xy)
{
    var parts = xy.Split(',', StringSplitOptions.TrimEntries);
    return (double.Parse(parts[0], System.Globalization.CultureInfo.InvariantCulture),
            double.Parse(parts[1], System.Globalization.CultureInfo.InvariantCulture));
}
