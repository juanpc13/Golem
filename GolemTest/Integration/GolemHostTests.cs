using System.Globalization;
using System.Text.Json;
using Choreography.Transport.Brokered;
using GolemAPI;
using GolemAPI.Choreography;
using GolemAPI.Membrane;
using GolemAPI.Panel;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;

namespace GolemTest;

// THE SAME GOLEM, IN THIS PROCESS (propuesta 52, fase 0, 23-sep-2026): GolemHost assembles what Program.cs used to assemble in
// line — the actor with the domain's assembly and its releases, the speech, the embodiment with its roles, the mechanics as the
// output target — over whatever wires it is given. Here: a body standing still that only records the orders published to it,
// and a broker in memory. What this proves is the seam, not the world: the scenario tests (fase 1) give it a body that moves.
[TestClass]
public class GolemHostTests
{
    // A body that stands where it was put and remembers every order the mechanics published to it.
    private sealed class BodyStandingStill : IBodyWire
    {
        public readonly List<(DateTime At, string Json)> Orders = new();
        public BodyStandingStill(double x, double y, double theta) { LatestPose = new Pose(x, y, theta); LatestTruth = LatestPose; }
        public PoseSource Source => PoseSource.World;
        public Pose LatestPose { get; private set; }
        public Pose LatestTruth { get; private set; }
        public Contact LatestContact => null;
        public string OrderTopic => "/golem/lab/order";
        public event Action<string> ResultReported { add { } remove { } }   // it never moves: it never reports
        public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;
        public Task BindAsync(CancellationToken ct) => Task.CompletedTask;
        public Task PublishAsync(string topic, string json) { lock (Orders) Orders.Add((DateTime.UtcNow, json)); return Task.CompletedTask; }
        public Task TeleportAsync(double x, double y, double theta, CancellationToken ct)
        {
            LatestPose = new Pose(x, y, theta);
            LatestTruth = LatestPose;
            return Task.CompletedTask;
        }
        public ValueTask DisposeAsync() => ValueTask.CompletedTask;
        public void Nudge(double x, double y, double theta) { LatestPose = new Pose(x, y, theta); LatestTruth = LatestPose; }
    }

    // The tells' wire in memory: the engine's own broker, with no peers to ask.
    private sealed class TellsInMemory : ITellWire
    {
        private readonly InProcessBroker broker = new();
        public IReadOnlyCollection<Uri> Peers => Array.Empty<Uri>();
        public Task<bool> AskPeerAsync(Uri peer, string relativePath, string json) => Task.FromResult(false);
        public Task ProduceAsync(string topic, string key, IReadOnlyDictionary<string, string> headers, string value, CancellationToken ct) =>
            broker.ProduceAsync(topic, key, headers, value, ct);
        public IDisposable Subscribe(string topic, Action<BrokerRecord> onRecord) => broker.Subscribe(topic, onRecord);
    }

    [TestInitialize]
    public void PinTheCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [TestMethod]
    public async Task TheSameGolem_AssemblesInProcess_WakesWhereItsBodyStands_AndAnErrandPushesItsFirstOrderToTheBody()
    {
        var body = new BodyStandingStill(2.0, 9.5, 0.0);
        var settings = new GolemSettings("lab-" + Guid.NewGuid().ToString("N"), "lab", (2.0, 9.5), Capabilities.Parse(null),
                                         Array.Empty<string>(), null, DatabaseType.IN_MEMORY, "");
        await using var host = GolemHost.Build(settings, body, new TellsInMemory(), new PanelFeed());
        Assert.IsTrue(host.BornThisBoot, "a journal in memory is always brand-new");

        using var cancel = new CancellationTokenSource(TimeSpan.FromSeconds(30));
        await host.ConnectAsync(cancel.Token);                       // reborn: the body is put back on its mark
        var clock = host.RunAsync(cancel.Token);                     // the clock wakes the golem where its body stands
        await Until(() => Read(host, "print g.KnowsWhereItStands 'v';").GetBoolean(), cancel.Token);
        Assert.AreEqual("warehouse", Read(host, "print map.Name 'v';").GetString(), "the releases the host carries built the map");

        // the pose the body reported entered the journal at its resolution: to the millimetre (ajuste 53)
        body.Nudge(2.0004, 9.5006, 0.00049);
        host.Embodiment.Wake();
        Assert.AreEqual(2.0, Read(host, "print g.Standing.X 'v';").GetDouble(), 0.0, "2.0004 m is written as 2.000");
        Assert.AreEqual(9.501, Read(host, "print g.Standing.Y 'v';").GetDouble(), 0.0, "9.5006 m is written as 9.501");
        Assert.AreEqual(0.0, Read(host, "print g.Standing.Heading 'v';").GetDouble(), 0.0, "0.00049 rad is written as 0.000");

        // what the body heard before the errand: the lever that put it on its mark (stop, and anchor there)
        lock (body.Orders) StringAssert.Contains(body.Orders.First().Json, "\"anchor\":true", "reborn: stopped and anchored on the mark");

        var sent = DateTime.UtcNow;
        var answer = host.Embodiment.Displacer.Move(new[] { (9.0, 9.5) });   // kitchen → storage, through the north hall
        Assert.IsTrue(answer.Ok, "the errand is written: " + answer.Refused);
        static bool Moves(string json) => json.Contains("\"order\"");   // the body's words alone: a ticket, an action, an amount (ajuste 55)
        await Until(() => { lock (body.Orders) return body.Orders.Any(o => Moves(o.Json)); }, cancel.Token);
        (DateTime At, string Json) first; lock (body.Orders) first = body.Orders.First(o => Moves(o.Json));
        using var order = JsonDocument.Parse(first.Json);
        StringAssert.Matches(order.RootElement.GetProperty("action").GetString(), new System.Text.RegularExpressions.Regex("^(advance|turnLeft|turnRight)$"),
            "the mechanics published the robot's words to the body: " + first.Json);
        // the first measure of fase 1: how long from the act written to the order on the body's wire (the reaction's push)
        double ms = (first.At - sent).TotalMilliseconds;
        Console.WriteLine($"[lab] errand written -> first order on the body's wire: {ms:0} ms");
        File.WriteAllText(Path.Combine(AppContext.BaseDirectory, "lab-host-latency.txt"), $"errand written -> first order on the body's wire: {ms:0} ms" + Environment.NewLine);

        cancel.Cancel();
        try { await clock; } catch (OperationCanceledException) { }
    }

    private static JsonElement Read(GolemHost host, string script)
    {
        using var doc = JsonDocument.Parse(host.Performance.Actor.Using(script).PerformQuery());
        return doc.RootElement.GetProperty("v").Clone();
    }

    private static async Task Until(Func<bool> condition, CancellationToken ct)
    {
        while (!condition())
        {
            ct.ThrowIfCancellationRequested();
            await Task.Delay(50, ct);
        }
    }
}
