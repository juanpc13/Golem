using System.Collections.Concurrent;
using System.Text.Json;
using Choreography.Transport.Brokered;
using GolemAPI;
using GolemAPI.Choreography;
using GolemAPI.Membrane;
using GolemAPI.Panel;
using Puppeteer;

namespace GolemTest.World;

// THE WORLD HELD IN MEMORY (propuesta 52, fase 1, 23-sep-2026): the floor the simulator is built from, the kiosk's crates, and a
// body per golem that does what `sim/bridge/body.py` does — takes ONE order at a time on its wire, turns in place or runs
// straight holding its heading, and the moment its shell meets something (a wall piece, a crate, another body) the motors stop
// and it reports the bump in a bumper's words: where it stood, facing which way, and where on its shell it was pressed. Each
// golem is the SAME golem the containers run (GolemHost: the actor with the domain's assembly, the speech, the embodiment, the
// mechanics as output target), with the tells travelling a broker in memory. The world does not run in time: an order is done
// the instant it is taken, so a scenario lasts as long as its acts, not as long as the bodies would drive.
public sealed class FloorWorld : ILabWorld
{
    private const double Step = 0.005;   // metres a run advances between two looks at the shell
    private readonly FloorPlan plan;
    private readonly List<Box> crates = new();
    private readonly Dictionary<string, Golem> golems = new();
    private readonly InProcessBroker broker = new();
    private readonly List<WorldContact> contacts = new();
    private readonly ConcurrentQueue<string> dirty = new();         // bodies with an order waiting
    private readonly SemaphoreSlim wake = new(0);
    private readonly CancellationTokenSource stop = new();
    private Task pump;
    private int sequence;

    private sealed class Golem
    {
        public string Name;
        public GolemHost Host;
        public KinematicBody Body;
        public Task Clock;
    }

    public FloorWorld() : this(FloorPlan.Load()) { }

    public FloorWorld(FloorPlan plan)
    {
        this.plan = plan ?? throw new ArgumentNullException(nameof(plan));
    }

    public void PlaceCrate(string spot)
    {
        if (!FloorPlan.Crates.TryGetValue(spot, out var crate)) throw new ArgumentException($"the kiosk has no crate spot '{spot}'", nameof(spot));
        lock (crates) crates.Add(crate);
    }

    /// <summary>A golem comes into the world: its body on its mark (the plan's, or the one given), the same GolemHost the
    /// containers run, reborn with a journal in memory, connected, awake. Peers are the other golems added so far and after;
    /// `follower` is the peer told every stop it reaches (TELL_DONE_TO).</summary>
    public async Task AddGolemAsync(string name, (double X, double Y)? home = null, string follower = null, IReadOnlyList<string> peers = null)
    {
        var mark = plan.Bodies.FirstOrDefault(b => b.Name == name);
        var at = home ?? (mark != null ? (mark.X, mark.Y) : throw new ArgumentException($"the plan has no body '{name}': give it a home"));
        var body = new KinematicBody(this, name, at.X, at.Y, 0.0);
        var settings = new GolemSettings(name, name, at, Capabilities.Parse(null), peers ?? Array.Empty<string>(), follower,
                                         DatabaseType.IN_MEMORY, "");
        var host = GolemHost.Build(settings, body, new TellsInMemory(broker), new PanelFeed());
        var golem = new Golem { Name = name, Host = host, Body = body };
        lock (golems) golems[name] = golem;
        pump ??= Task.Run(PumpAsync);
        await host.ConnectAsync(stop.Token);
        golem.Clock = host.RunAsync(stop.Token);
        await Until(() => Read(name, "print g.KnowsWhereItStands 'v';").GetBoolean(), TimeSpan.FromSeconds(10));
    }

    public Task PlaceGolemAsync(string golem, (double X, double Y)? at = null) => AddGolemAsync(golem, at);

    public void Send(string golem, params (double X, double Y)[] stops)
    {
        var answer = Of(golem).Host.Embodiment.Displacer.Move(stops);
        if (!answer.Ok) throw new InvalidOperationException($"{golem} refused the errand: {answer.Refused}");
    }

    public async Task RunUntilSettledAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        var quietSince = DateTime.MaxValue;
        while (DateTime.UtcNow < until)
        {
            bool busy = !dirty.IsEmpty || golems.Values.Any(g => g.Body.Busy || Read(g.Name, "print g.HasPendingMission() 'v';").GetBoolean());
            if (busy) quietSince = DateTime.MaxValue;
            else if (quietSince == DateTime.MaxValue) quietSince = DateTime.UtcNow;
            else if (DateTime.UtcNow - quietSince > TimeSpan.FromMilliseconds(400)) return;   // quiet a while: no push still in flight
            await Task.Delay(20);
        }
        throw new TimeoutException("the world did not settle in " + timeout + ": " + string.Join("; ", golems.Keys.Select(n => $"{n} {Outcome(n)}")));
    }

    public ErrandOutcome Outcome(string golem) => new(
        Read(golem, "print g.Newest().Status 'v';").GetString(),
        Read(golem, "print g.Newest().Bumps 'v';").GetInt32(),
        Read(golem, "print collisions.MarkCount 'v';").GetInt32(),
        Read(golem, "print collisions.EncounterCount 'v';").GetInt32());

    public IReadOnlyList<WorldContact> Contacts(string golem)
    {
        lock (contacts) return contacts.Where(c => c.Golem == golem).ToList();
    }

    public (double X, double Y, double Heading) TruePose(string golem)
    {
        var p = Of(golem).Body.LatestTruth;
        return (p.X, p.Y, p.Theta);
    }

    /// <summary>A question to a golem's journal: the value printed as 'v'.</summary>
    public JsonElement Read(string golem, string script)
    {
        using var doc = JsonDocument.Parse(Of(golem).Host.Performance.Actor.Using(script).PerformQuery());
        return doc.RootElement.GetProperty("v").Clone();
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        wake.Release();
        foreach (var g in golems.Values)
        {
            try { await g.Clock; } catch (OperationCanceledException) { }
            await g.Host.DisposeAsync();
        }
        if (pump != null) try { await pump; } catch (OperationCanceledException) { }
    }

    private Golem Of(string name)
    {
        lock (golems) return golems.TryGetValue(name, out var g) ? g : throw new ArgumentException($"no golem '{name}' in this world");
    }

    private static async Task Until(Func<bool> condition, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException("the condition did not hold in " + timeout);
            await Task.Delay(20);
        }
    }

    // ---- the world's beat: an order taken is done at once and reported OFF the engine's push (never re-entering the actor) ----

    internal void OrderArrived(string body)
    {
        dirty.Enqueue(body);
        wake.Release();
    }

    private async Task PumpAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            await wake.WaitAsync(stop.Token).ContinueWith(_ => { });
            while (dirty.TryDequeue(out var name))
            {
                Golem golem;
                lock (golems) if (!golems.TryGetValue(name, out golem)) continue;
                try { Carry(golem); }
                catch (Exception e) { Console.WriteLine($"[world] {name}: {e.Message}"); }
            }
        }
    }

    // One order, the one the body carries now (a newer one replaced any older one not yet begun).
    private void Carry(Golem golem)
    {
        var order = golem.Body.Take();
        if (order == null) return;
        string action = order.Value.GetProperty("action").GetString();
        if (action is "stop" or "continue") return;
        double amount = order.Value.TryGetProperty("amount", out var a) ? a.GetDouble() : 0.0;
        int route = order.Value.TryGetProperty("route", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : 0;
        var body = golem.Body;
        if (action is "turnLeft" or "turnRight")
        {
            body.Put(body.X, body.Y, Normalize(body.Theta + (action == "turnLeft" ? amount : -amount)));
            golem.Host.Embodiment.Displacer.Arrived(route);
            return;
        }
        if (action is not ("advance" or "back")) return;
        double direction = action == "advance" ? body.Theta : body.Theta + Math.PI;
        double dx = Math.Cos(direction), dy = Math.Sin(direction);
        double travelled = 0;
        while (travelled < amount)
        {
            double stepLength = Math.Min(Step, amount - travelled);
            double nx = body.X + dx * stepLength, ny = body.Y + dy * stepLength;
            var hit = FirstTouch(golem.Name, nx, ny);
            if (hit != null)
            {
                Bumped(golem, hit.Value.With, hit.Value.PointX, hit.Value.PointY);
                if (hit.Value.Other != null) Bumped(hit.Value.Other, golem.Name, nx - dx * FloorPlan.BodyRadius, ny - dy * FloorPlan.BodyRadius);
                return;
            }
            body.Put(nx, ny, body.Theta);
            travelled += stepLength;
        }
        golem.Host.Embodiment.Displacer.Arrived(route);
    }

    // The bumper fired: the motors already stopped (the body stands where it was); it says where it stood, facing which way,
    // and where on its shell it was pressed — the bearing from the direction it faces toward the point it touched.
    private void Bumped(Golem golem, string with, double pointX, double pointY)
    {
        var body = golem.Body;
        double bearing = Normalize(Math.Atan2(pointY - body.Y, pointX - body.X) - body.Theta);
        lock (contacts) contacts.Add(new WorldContact(golem.Name, with, body.X, body.Y, body.Theta, bearing, ++sequence));
        body.Touched(with, bearing);
        golem.Host.Embodiment.Captor.Bumped(body.X, body.Y, body.Theta, bearing);
    }

    // What a disc centred at (x, y) would press against: a wall piece or a crate (the point touched), or another body.
    private (string With, double PointX, double PointY, Golem Other)? FirstTouch(string me, double x, double y)
    {
        double r = FloorPlan.BodyRadius;
        List<Box> solid;
        lock (crates) solid = plan.Walls.Concat(crates).ToList();
        foreach (var box in solid)
        {
            var (px, py) = box.Closest(x, y);
            if ((px - x) * (px - x) + (py - y) * (py - y) < r * r) return (box.Name, px, py, null);
        }
        List<Golem> others;
        lock (golems) others = golems.Values.Where(g => g.Name != me).ToList();
        foreach (var other in others)
        {
            double ox = other.Body.X, oy = other.Body.Y;
            double d = Math.Sqrt((ox - x) * (ox - x) + (oy - y) * (oy - y));
            if (d < 2 * r) return (other.Name, x + (ox - x) * r / d, y + (oy - y) * r / d, other);
        }
        return null;
    }

    private static double Normalize(double a) => Math.Atan2(Math.Sin(a), Math.Cos(a));

    // ---- a body on the world's floor, behind the golem's wire ----

    internal sealed class KinematicBody : IBodyWire
    {
        private readonly FloorWorld world;
        private readonly string name;
        private readonly object gate = new();
        private string carrying;   // the newest order not yet taken
        private volatile Pose pose;
        private volatile Contact contact;

        public KinematicBody(FloorWorld world, string name, double x, double y, double theta)
        {
            this.world = world;
            this.name = name;
            pose = new Pose(x, y, theta);
        }

        public double X => pose.X;
        public double Y => pose.Y;
        public double Theta => pose.Theta;
        public bool Busy { get { lock (gate) return carrying != null; } }

        public PoseSource Source => PoseSource.World;
        public Pose LatestPose => pose;
        public Pose LatestTruth => pose;
        public Contact LatestContact => contact;
        public string OrderTopic => $"/golem/{name}/order";

        public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;
        public Task BindAsync(CancellationToken ct) => Task.CompletedTask;

        public Task PublishAsync(string topic, string json)
        {
            lock (gate) carrying = json;   // a different order replaces what the body was doing
            world.OrderArrived(name);
            return Task.CompletedTask;
        }

        public Task TeleportAsync(double x, double y, double theta, CancellationToken ct)
        {
            Put(x, y, theta);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        internal JsonElement? Take()
        {
            string json;
            lock (gate) { json = carrying; carrying = null; }
            if (json == null) return null;
            using var doc = JsonDocument.Parse(json);
            return doc.RootElement.Clone();
        }

        internal void Put(double x, double y, double theta) => pose = new Pose(x, y, theta);
        internal void Touched(string with, double bearing) => contact = new Contact(with, DateTime.UtcNow, bearing);
    }

    // ---- the tells' wire, in memory: one broker for the whole world ----

    private sealed class TellsInMemory : ITellWire
    {
        private readonly InProcessBroker broker;
        public TellsInMemory(InProcessBroker broker) { this.broker = broker; }
        public IReadOnlyCollection<Uri> Peers => Array.Empty<Uri>();
        public Task<bool> AskPeerAsync(Uri peer, string relativePath, string json) => Task.FromResult(false);
        public Task ProduceAsync(string topic, string key, IReadOnlyDictionary<string, string> headers, string value, CancellationToken ct) =>
            broker.ProduceAsync(topic, key, headers, value, ct);
        public IDisposable Subscribe(string topic, Action<BrokerRecord> onRecord) => broker.Subscribe(topic, onRecord);
    }
}
