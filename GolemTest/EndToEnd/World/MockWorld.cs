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
// mechanics as output target), with the tells travelling a broker in memory. The world runs on the CLOCK, faster than life by
// TimeScale: at every beat each body carrying an order does as much of it as the time elapsed allows at the speed its journal
// declares — all of them interleaved, looked at every centimetre — so two bodies driving at once really meet halfway, and an
// order that takes a while to arrive (the reaction's push) costs the same distance it would in Gazebo, scaled. The golems are
// the fleet as the compose deploys it: each can tell every other, and red tells blue every stop it reaches.
public sealed class MockWorld : ILabWorld
{
    private const double Step = 0.01;        // metres a run advances between two looks at the shell
    private const double TurnSpeed = 1.5;    // rad/s, body.py's fastest turn
    private const double BackSpeed = 0.4;    // m/s, body.py's back-off speed
    private const double TimeScale = 4.0;    // the world runs this many times faster than life
    private const int TrailEvery = 10;       // a trail point every that many steps of motion
    private static readonly string[] Fleet = { "blue", "red", "green" };                             // docker-compose.yml
    private static readonly IReadOnlyDictionary<string, string> Followers = new Dictionary<string, string> { ["red"] = "blue" };
    private readonly FloorPlan plan;
    private readonly List<Box> crates = new();
    private readonly Dictionary<string, Golem> golems = new();
    private readonly InProcessBroker broker = new();
    private readonly List<WorldContact> contacts = new();
    private readonly List<WayDecided> ways = new();   // guarded by `contacts`, numbered with the same sequence
    private readonly List<ErrandSent> errands = new();
    private readonly LegTracker tracker = new();
    private Task watcher;
    private readonly SemaphoreSlim wake = new(0);
    private readonly CancellationTokenSource stop = new();
    private Task pump;
    private int sequence;
    private volatile bool held;   // the bodies take their orders but do not move: several set out together

    private sealed class Golem
    {
        public string Name;
        public GolemHost Host;
        public KinematicBody Body;
        public Task Clock;
    }

    public MockWorld() : this(FloorPlan.Load()) { }

    public MockWorld(FloorPlan plan)
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
        var settings = new GolemSettings(name, name, at, Capabilities.Parse(null), peers ?? Fleet.Where(n => n != name).ToList(),
                                         follower ?? (Followers.TryGetValue(name, out var f) ? f : null), DatabaseType.IN_MEMORY, "");
        var host = GolemHost.Build(settings, body, new TellsInMemory(broker), new PanelFeed());
        var golem = new Golem { Name = name, Host = host, Body = body };
        lock (golems) golems[name] = golem;
        pump ??= Task.Run(PumpAsync);
        watcher ??= Task.Run(WatchAsync);
        await host.ConnectAsync(stop.Token);
        golem.Clock = host.RunAsync(stop.Token);
        await Until(() => Read(name, "print g.KnowsWhereItStands 'v';").GetBoolean(), TimeSpan.FromSeconds(10));
    }

    public Task PlaceGolemAsync(string golem, (double X, double Y)? at = null) => AddGolemAsync(golem, at);

    public string Send(string golem, params (double X, double Y)[] stops)
    {
        var answer = Of(golem).Host.Embodiment.Displacer.Move(stops);
        if (!answer.Ok) throw new InvalidOperationException($"{golem} refused the errand: {answer.Refused}");
        string print = Compact(answer.Print);
        lock (contacts) errands.Add(new ErrandSent(golem, string.Join(" > ", stops.Select(s => $"({s.X}, {s.Y})")), print, ++sequence));
        NoteWay(golem);
        Observe(Of(golem));
        return print;
    }

    public IReadOnlyList<ErrandSent> Errands(string golem)
    {
        lock (contacts) return errands.Where(e => e.Golem == golem).ToList();
    }

    public IReadOnlyList<LegReport> Legs(string golem) => tracker.Of(golem);

    public async Task<LegReport> NextLegAsync(string golem, TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        while (DateTime.UtcNow < until)
        {
            var leg = tracker.Next(golem);
            if (leg != null) return leg;
            await Task.Delay(10);
        }
        throw new TimeoutException($"no leg of {golem} ended in {timeout}: {Outcome(golem)}");
    }

    // The legs, watched: every golem's route asked again and again while the bodies move (a leg lasts far longer than this beat).
    private async Task WatchAsync()
    {
        while (!stop.IsCancellationRequested)
        {
            List<Golem> all;
            lock (golems) all = golems.Values.ToList();
            foreach (var g in all) Observe(g);
            try { await Task.Delay(10, stop.Token); } catch (OperationCanceledException) { return; }
        }
    }

    private void Observe(Golem g)
    {
        try
        {
            var state = RouteState.Parse(g.Host.Performance.Actor.Using(RouteState.Query).PerformQuery());
            tracker.Observe(g.Name, state, (g.Body.X, g.Body.Y), Contacts(g.Name), () => { lock (contacts) return ++sequence; });
        }
        catch (Exception e) when (!stop.IsCancellationRequested) { Console.WriteLine($"[world] watching {g.Name}: {e.Message}"); }
        catch { }
    }

    private static string Compact(string json)
    {
        try { return JsonSerializer.Serialize(JsonDocument.Parse(json).RootElement); } catch (JsonException) { return json; }
    }

    public async Task SendTogetherAsync(params (string Golem, (double X, double Y) Stop)[] errands)
    {
        held = true;
        try
        {
            foreach (var (golem, stop) in errands) Send(golem, stop);
            await Until(() => errands.All(e => Of(e.Golem).Body.Doing != null), TimeSpan.FromSeconds(10));
        }
        finally { held = false; }
    }

    public async Task RunUntilSettledAsync(TimeSpan timeout)
    {
        var until = DateTime.UtcNow + timeout;
        var quietSince = DateTime.MaxValue;
        while (DateTime.UtcNow < until)
        {
            List<Golem> all;
            lock (golems) all = golems.Values.ToList();
            foreach (var g in all) NoteWay(g.Name);   // a way decided again by a peer's word, off the body's own reports
            bool busy = all.Any(g => g.Body.Busy || Read(g.Name, "print g.HasPendingMission() 'v';").GetBoolean());
            if (busy) quietSince = DateTime.MaxValue;
            else if (quietSince == DateTime.MaxValue) quietSince = DateTime.UtcNow;
            else if (DateTime.UtcNow - quietSince > TimeSpan.FromMilliseconds(400)) return;   // quiet a while: no push still in flight
            await Task.Delay(20);
        }
        throw new TimeoutException("the world did not settle in " + timeout + ": " + string.Join("; ", golems.Keys.Select(n => $"{n} {Outcome(n)}")));
    }

    public ErrandOutcome Outcome(string golem) => Read(golem, "print g.Routes().Count 'v';").GetInt32() == 0
        ? new("none", 0, Read(golem, "print collisions.MarkCount 'v';").GetInt32(), Read(golem, "print collisions.EncounterCount 'v';").GetInt32())
        : new(
        Read(golem, "print g.Newest().Status 'v';").GetString(),
        Read(golem, "print g.Newest().Bumps 'v';").GetInt32(),
        Read(golem, "print collisions.MarkCount 'v';").GetInt32(),
        Read(golem, "print collisions.EncounterCount 'v';").GetInt32());

    public IReadOnlyList<WorldContact> Contacts(string golem)
    {
        lock (contacts) return contacts.Where(c => c.Golem == golem).ToList();
    }

    public IReadOnlyList<WayDecided> Ways(string golem)
    {
        lock (contacts) return ways.Where(w => w.Golem == golem).ToList();
    }

    // The way the golem's newest route holds now, kept when it differs from the last one kept.
    private void NoteWay(string golem)
    {
        using var doc = JsonDocument.Parse(Of(golem).Host.Performance.Actor.Using("{ if (g.Routes().Count > 0) { route = g.Newest(); print route.Id 'route', route.AsPlan() 'plan'; } }").PerformQuery());
        if (!doc.RootElement.TryGetProperty("route", out var route)) return;
        int id = route.GetInt32();
        string plan = doc.RootElement.GetProperty("plan").GetString();
        lock (contacts)
        {
            var last = ways.LastOrDefault(w => w.Golem == golem);
            if (last != null && last.Route == id && last.Plan == plan) return;
            ways.Add(new WayDecided(golem, id, plan, ++sequence));
        }
    }

    public (double X, double Y, double Heading) TruePose(string golem)
    {
        var p = Of(golem).Body.LatestTruth;
        return (p.X, p.Y, p.Theta);
    }

    public IReadOnlyList<(double X, double Y)> Trail(string golem) => Of(golem).Body.TrailSoFar();

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
        if (watcher != null) try { await watcher; } catch (OperationCanceledException) { }
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

    // ---- the world's beat: every tick, each body does a little of its order; reports go out from the world's thread, never
    //      inside the engine's push (which would re-enter the actor) ----

    internal void OrderArrived(string body) => wake.Release();

    private async Task PumpAsync()
    {
        var clock = System.Diagnostics.Stopwatch.StartNew();
        double last = clock.Elapsed.TotalSeconds;
        while (!stop.IsCancellationRequested)
        {
            List<Golem> all;
            lock (golems) all = golems.Values.ToList();
            foreach (var g in all) g.Body.TakeNewOrder();
            double now = clock.Elapsed.TotalSeconds, dt = (now - last) * TimeScale;
            last = now;
            var moving = held ? new List<Golem>() : all.Where(g => g.Body.Doing != null).ToList();
            if (moving.Count == 0)
            {
                await wake.WaitAsync(TimeSpan.FromMilliseconds(20), stop.Token).ContinueWith(_ => { });
                continue;
            }
            foreach (var g in moving)
            {
                try { Beat(g, dt); }
                catch (Exception e) { Console.WriteLine($"[world] {g.Name}: {e.Message}"); g.Body.Done(); }
            }
            // a beat is measured by the clock, so sleeping between beats changes no distance; spinning here would starve the
            // engine's reaction threads, and the pushes — the next orders — would arrive late (23-sep-2026: a turn took a second)
            await Task.Delay(2);
        }
    }

    // As much of what the body is doing as `dt` seconds allow: a turn in place, or a run straight holding its heading, looked
    // at every centimetre, until the shell meets something.
    private void Beat(Golem golem, double dt)
    {
        var body = golem.Body;
        var doing = body.Doing;
        if (doing == null) return;
        if (doing.Action is "turnLeft" or "turnRight")
        {
            double turn = Math.Min(TurnSpeed * dt, doing.Left);
            body.Put(body.X, body.Y, Normalize(body.Theta + (doing.Action == "turnLeft" ? turn : -turn)));
            doing.Left -= turn;
            if (doing.Left <= 1e-9) { body.Done(); golem.Host.Embodiment.Displacer.Arrived(doing.Route); }
            return;
        }
        double direction = doing.Action == "advance" ? body.Theta : body.Theta + Math.PI;
        double run = Math.Min((doing.Action == "back" ? BackSpeed : doing.Speed) * dt, doing.Left);
        while (run > 1e-12)
        {
            double step = Math.Min(Step, run);
            double nx = body.X + Math.Cos(direction) * step, ny = body.Y + Math.Sin(direction) * step;
            var hit = FirstTouch(golem.Name, nx, ny);
            if (hit != null)
            {
                body.Done();                                                   // the bumper fired: the motors stop at once
                Bumped(golem, hit.Value.With, hit.Value.PointX, hit.Value.PointY);
                if (hit.Value.Other != null)                                   // the other body's bumper fires too, moving or standing
                {
                    hit.Value.Other.Body.Done();
                    Bumped(hit.Value.Other, golem.Name, hit.Value.PointX, hit.Value.PointY);
                }
                return;
            }
            body.Put(nx, ny, body.Theta);
            doing.Left -= step;
            run -= step;
        }
        if (doing.Left <= 1e-9) { body.Done(); golem.Host.Embodiment.Displacer.Arrived(doing.Route); }
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
        NoteWay(golem.Name);   // the route corrected its way inside the bump
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

    internal sealed class Doing
    {
        public string Action;
        public double Left;
        public double Speed;   // m/s: the cruise the journal declared for the body
        public int Route;
    }

    internal sealed class KinematicBody : IBodyWire
    {
        private readonly MockWorld world;
        private readonly string name;
        private readonly object gate = new();
        private readonly List<(double X, double Y)> trail = new();
        private string waiting;     // the newest order published, not yet taken
        private Doing doing;        // what the body is doing now
        private Doing held;         // what a stop left undone, for a continue
        private volatile Pose pose;
        private volatile Contact contact;
        private int ticks;

        public KinematicBody(MockWorld world, string name, double x, double y, double theta)
        {
            this.world = world;
            this.name = name;
            pose = new Pose(x, y, theta);
            trail.Add((x, y));
        }

        public double X => pose.X;
        public double Y => pose.Y;
        public double Theta => pose.Theta;
        public bool Busy { get { lock (gate) return waiting != null || doing != null; } }
        public Doing Doing { get { lock (gate) return doing; } }

        public PoseSource Source => PoseSource.World;
        public Pose LatestPose => pose;
        public Pose LatestTruth => pose;
        public Contact LatestContact => contact;
        public string OrderTopic => $"/golem/{name}/order";

        public Task ConnectAsync(CancellationToken ct) => Task.CompletedTask;
        public Task BindAsync(CancellationToken ct) => Task.CompletedTask;

        public Task PublishAsync(string topic, string json)
        {
            lock (gate) waiting = json;   // a different order replaces what the body was doing
            world.OrderArrived(name);
            return Task.CompletedTask;
        }

        public Task TeleportAsync(double x, double y, double theta, CancellationToken ct)
        {
            lock (gate) { doing = null; held = null; }
            Put(x, y, theta);
            return Task.CompletedTask;
        }

        public ValueTask DisposeAsync() => ValueTask.CompletedTask;

        // The order waiting, if any, becomes what the body does: stop keeps what was left, continue takes it up again.
        internal void TakeNewOrder()
        {
            string json;
            lock (gate) { json = waiting; waiting = null; }
            if (json == null) return;
            using var doc = JsonDocument.Parse(json);
            var o = doc.RootElement;
            string action = o.GetProperty("action").GetString();
            lock (gate)
            {
                if (action == "stop") { held = doing; doing = null; return; }
                if (action == "continue") { doing = held; held = null; return; }
                if (action is not ("advance" or "back" or "turnLeft" or "turnRight")) return;
                doing = new Doing
                {
                    Action = action,
                    Left = o.TryGetProperty("amount", out var a) ? a.GetDouble() : 0.0,
                    Route = o.TryGetProperty("route", out var r) && r.ValueKind == JsonValueKind.Number ? r.GetInt32() : 0,
                    Speed = o.TryGetProperty("body", out var b) && b.TryGetProperty("speed", out var v) ? v.GetDouble() : 1.0,
                };
                held = null;
            }
        }

        internal void Done() { lock (gate) doing = null; }

        internal void Put(double x, double y, double theta)
        {
            pose = new Pose(x, y, theta);
            lock (gate) if (++ticks % TrailEvery == 0) trail.Add((x, y));
        }

        internal IReadOnlyList<(double X, double Y)> TrailSoFar() { lock (gate) return trail.Concat(new[] { (pose.X, pose.Y) }).ToList(); }
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
