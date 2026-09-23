using System.Net.Http.Json;
using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace GolemTest.World;

/// <summary>The fleet or the simulator did not answer: a scenario against Gazebo cannot say anything, neither yes nor no.</summary>
public sealed class WorldUnavailableException : Exception
{
    public WorldUnavailableException(string message, Exception inner = null) : base(message, inner) { }
}

// THE WORLD IN GAZEBO, SEEN FROM OUTSIDE (propuesta 52, fase 2, 23-sep-2026): the fleet as `docker compose` deployed it — each
// golem is asked by its own endpoints (`/move`, `/state`, `/query`, `/reset`, `/obstacles`, `/forget`), exactly as an operator
// would — and the simulator is watched through rosbridge for what only IT can say: where each body really is
// (`/model/<body>/odometry`) and what it touched (`/model/<body>/contacts`, with the model's name: `crate_center`,
// `wall_center_w`, `red`…); the kiosk's crate lever (`/sim/crate`, `/sim/crates`) puts crates in the running world. The same
// face as the world in memory, so a scenario is written once. Contacts stream while two bodies stay pressed: a CONTACT is a
// touch of one model after the shell was quiet on it for a moment — the same rule the bumper follows (body.py).
public sealed class GazeboWorld : ILabWorld
{
    private static readonly TimeSpan Released = TimeSpan.FromMilliseconds(400);   // body.py: quiet this long, the bumper is released
    private readonly Uri rosbridge;
    private readonly IReadOnlyDictionary<string, Uri> golems;
    private readonly HttpClient http = new() { Timeout = TimeSpan.FromSeconds(10) };
    private readonly ClientWebSocket ws = new();
    private readonly SemaphoreSlim sending = new(1, 1);
    private readonly CancellationTokenSource stop = new();
    private readonly object gate = new();
    private readonly Dictionary<string, (double X, double Y, double Heading)> truth = new();
    private readonly Dictionary<string, (string With, DateTime Last)> pressed = new();
    private readonly List<WorldContact> contacts = new();
    private readonly HashSet<string> placed = new();
    private string crates = "";
    private Task reader;
    private int sequence;

    private GazeboWorld(Uri rosbridge, IReadOnlyDictionary<string, Uri> golems)
    {
        this.rosbridge = rosbridge;
        this.golems = golems;
    }

    /// <summary>Connects to rosbridge and to every golem, and leaves the world clean: no crates, every golem let go (its body back
    /// on its mark, awake there) and nothing it learned by touching left in its collisions. Unreachable: WorldUnavailableException.</summary>
    public static async Task<GazeboWorld> OpenAsync(Uri rosbridge, IReadOnlyDictionary<string, Uri> golems, TimeSpan patience)
    {
        var world = new GazeboWorld(rosbridge, golems);
        try
        {
            using (var cts = new CancellationTokenSource(TimeSpan.FromSeconds(5))) await world.ws.ConnectAsync(rosbridge, cts.Token);
            foreach (var (name, url) in golems) (await world.http.GetAsync(new Uri(url, "state"))).EnsureSuccessStatusCode();
        }
        catch (Exception e)
        {
            await world.DisposeAsync();
            throw new WorldUnavailableException($"the simulator ({rosbridge}) or a golem ({string.Join(", ", golems.Values)}) does not answer", e);
        }
        world.reader = Task.Run(world.ReadAsync);
        await world.SendAsync(new { op = "advertise", topic = "/sim/crate", type = "std_msgs/String" });
        await world.SendAsync(new { op = "subscribe", topic = "/sim/crates", type = "std_msgs/String" });
        foreach (var name in golems.Keys)
        {
            await world.SendAsync(new { op = "subscribe", topic = $"/model/{name}/odometry", type = "nav_msgs/Odometry", throttle_rate = 100 });
            await world.SendAsync(new { op = "subscribe", topic = $"/model/{name}/contacts", type = "ros_gz_interfaces/Contacts", throttle_rate = 20 });
        }
        await world.ResetAsync(patience);
        return world;
    }

    /// <summary>The world clean again: the crates cleared, every golem let go, every obstacle it holds forgotten, the bodies quiet.</summary>
    public async Task ResetAsync(TimeSpan patience)
    {
        await SendAsync(new { op = "publish", topic = "/sim/crate", msg = new { data = "clear" } });
        await UntilAsync(() => { lock (gate) return crates == ""; }, TimeSpan.FromSeconds(10), "the crates to clear");
        foreach (var url in golems.Values) await PostAsync(url, "reset", new { });
        foreach (var (name, url) in golems)
            for (int round = 0; round < 20; round++)
            {
                using var doc = JsonDocument.Parse(await http.GetStringAsync(new Uri(url, "obstacles")));
                if (!doc.RootElement.TryGetProperty("obstacles", out var obstacles)) break;
                var thing = obstacles.EnumerateArray().FirstOrDefault(o => o.GetProperty("kind").GetString() == "thing");
                if (thing.ValueKind == JsonValueKind.Undefined) break;
                var mark = thing.GetProperty("vertices")[0];
                await PostAsync(url, "forget", new { x = mark.GetProperty("x").GetDouble(), y = mark.GetProperty("y").GetDouble() });
            }
        lock (gate) placed.Clear();
        await SettleAsync(golems.Keys, patience);
        lock (gate) contacts.Clear();
    }

    public void PlaceCrate(string spot)
    {
        if (!FloorPlan.Crates.ContainsKey(spot)) throw new ArgumentException($"the kiosk has no crate spot '{spot}'", nameof(spot));
        SendAsync(new { op = "publish", topic = "/sim/crate", msg = new { data = spot } }).GetAwaiter().GetResult();
        UntilAsync(() => { lock (gate) return crates.Split(',').Contains(spot); }, TimeSpan.FromSeconds(10), $"the crate '{spot}' to stand")
            .GetAwaiter().GetResult();
    }

    /// <summary>The golem takes part in the scenario. With a place to start from, it is sent there first — an errand like any
    /// other, the world still clean — and the scenario begins once every body is quiet, with no contact counted yet.</summary>
    public async Task PlaceGolemAsync(string golem, (double X, double Y)? at = null)
    {
        if (!golems.ContainsKey(golem)) throw new ArgumentException($"no golem '{golem}' in this fleet");
        lock (gate) placed.Add(golem);
        if (at == null) return;
        Send(golem, at.Value);
        await SettleAsync(golems.Keys, TimeSpan.FromSeconds(120));   // the followers too: nobody still driving into the scenario
        lock (gate) contacts.RemoveAll(c => c.Golem == golem);
    }

    public void Send(string golem, params (double X, double Y)[] stops)
    {
        var answer = http.PostAsJsonAsync(new Uri(golems[golem], "move"), new { stops = stops.Select(s => new { x = s.X, y = s.Y }) })
            .GetAwaiter().GetResult();
        string body = answer.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!answer.IsSuccessStatusCode || body.Contains("\"EWI\"")) throw new InvalidOperationException($"{golem} refused the errand: {body}");
    }

    public Task RunUntilSettledAsync(TimeSpan timeout)
    {
        List<string> who;
        lock (gate) who = placed.ToList();
        return SettleAsync(who, timeout);
    }

    public ErrandOutcome Outcome(string golem)
    {
        var answer = http.PostAsJsonAsync(new Uri(golems[golem], "query"), new
        {
            // a full block runs verbatim at /query (a bare expression would be wrapped as one print)
            script = "{ route = g.Newest(); print route.Status 'status', route.Bumps 'bumps', collisions.MarkCount 'marks', collisions.EncounterCount 'met'; }"
        }).GetAwaiter().GetResult();
        string body = answer.Content.ReadAsStringAsync().GetAwaiter().GetResult();
        if (!answer.IsSuccessStatusCode) throw new InvalidOperationException($"{golem} did not answer the question: {body}");
        using var doc = JsonDocument.Parse(body);
        var e = doc.RootElement;
        return new ErrandOutcome(e.GetProperty("status").GetString(), e.GetProperty("bumps").GetInt32(),
                                 e.GetProperty("marks").GetInt32(), e.GetProperty("met").GetInt32());
    }

    public IReadOnlyList<WorldContact> Contacts(string golem)
    {
        lock (gate) return contacts.Where(c => c.Golem == golem).ToList();
    }

    public (double X, double Y, double Heading) TruePose(string golem)
    {
        lock (gate) return truth.TryGetValue(golem, out var p) ? p : throw new InvalidOperationException($"no word from {golem}'s odometry yet");
    }

    public async ValueTask DisposeAsync()
    {
        stop.Cancel();
        try { if (ws.State == WebSocketState.Open) await ws.CloseAsync(WebSocketCloseStatus.NormalClosure, "", CancellationToken.None); } catch { }
        if (reader != null) try { await reader; } catch { }
        ws.Dispose();
        http.Dispose();
    }

    // ---- quiet: nothing pending in the journals asked, and the bodies no longer moving ----

    private async Task SettleAsync(IEnumerable<string> who, TimeSpan timeout)
    {
        var names = who.ToList();
        var until = DateTime.UtcNow + timeout;
        var still = DateTime.MaxValue;
        Dictionary<string, (double X, double Y, double Heading)> before = null;
        while (DateTime.UtcNow < until)
        {
            bool pending = false;
            foreach (var name in names)
            {
                using var doc = JsonDocument.Parse(await http.GetStringAsync(new Uri(golems[name], "state")));
                if (doc.RootElement.GetProperty("pending").GetInt32() > 0) pending = true;
            }
            Dictionary<string, (double X, double Y, double Heading)> now;
            lock (gate) now = names.Where(truth.ContainsKey).ToDictionary(n => n, n => truth[n]);
            bool moving = before == null || now.Any(p => !before.TryGetValue(p.Key, out var b)
                                                         || Math.Abs(p.Value.X - b.X) + Math.Abs(p.Value.Y - b.Y) > 0.01
                                                         || Math.Abs(p.Value.Heading - b.Heading) > 0.02);
            before = now;
            if (pending || moving) still = DateTime.MaxValue;
            else if (still == DateTime.MaxValue) still = DateTime.UtcNow;
            else if (DateTime.UtcNow - still > TimeSpan.FromSeconds(1.5)) return;
            await Task.Delay(500);
        }
        throw new TimeoutException($"the world did not settle in {timeout}: {string.Join("; ", names.Select(n => $"{n} {Outcome(n)}"))}");
    }

    // ---- the wire to the simulator ----

    private async Task SendAsync(object frame)
    {
        var bytes = JsonSerializer.SerializeToUtf8Bytes(frame);
        await sending.WaitAsync();
        try { await ws.SendAsync(bytes, WebSocketMessageType.Text, true, stop.Token); }
        finally { sending.Release(); }
    }

    private async Task PostAsync(Uri golem, string path, object body)
    {
        var answer = await http.PostAsJsonAsync(new Uri(golem, path), body);
        if (!answer.IsSuccessStatusCode) Console.WriteLine($"[gazebo] {golem}{path} answered {(int)answer.StatusCode}: {await answer.Content.ReadAsStringAsync()}");
    }

    private async Task ReadAsync()
    {
        var buffer = new byte[256 * 1024];
        var message = new StringBuilder();
        while (!stop.IsCancellationRequested && ws.State == WebSocketState.Open)
        {
            WebSocketReceiveResult r;
            try { r = await ws.ReceiveAsync(buffer, stop.Token); }
            catch { return; }
            message.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
            if (!r.EndOfMessage) continue;
            try { Heard(message.ToString()); } catch (Exception e) { Console.WriteLine($"[gazebo] unreadable frame: {e.Message}"); }
            message.Clear();
        }
    }

    private void Heard(string frame)
    {
        using var doc = JsonDocument.Parse(frame);
        var root = doc.RootElement;
        if (!root.TryGetProperty("topic", out var t) || !root.TryGetProperty("msg", out var msg)) return;
        string topic = t.GetString();
        if (topic == "/sim/crates") { lock (gate) crates = msg.GetProperty("data").GetString() ?? ""; return; }
        var parts = topic.Split('/');   // "", "model", <body>, odometry|contacts
        if (parts.Length != 4) return;
        string body = parts[2];
        if (parts[3] == "odometry")
        {
            var pose = msg.GetProperty("pose").GetProperty("pose");
            var p = pose.GetProperty("position");
            var q = pose.GetProperty("orientation");
            double qx = q.GetProperty("x").GetDouble(), qy = q.GetProperty("y").GetDouble(), qz = q.GetProperty("z").GetDouble(), qw = q.GetProperty("w").GetDouble();
            double yaw = Math.Atan2(2 * (qw * qz + qx * qy), 1 - 2 * (qy * qy + qz * qz));
            lock (gate) truth[body] = (p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble(), yaw);
        }
        else if (parts[3] == "contacts")
            foreach (var c in msg.GetProperty("contacts").EnumerateArray())
            {
                string a = c.GetProperty("collision1").GetProperty("name").GetString() ?? "";
                string b = c.GetProperty("collision2").GetProperty("name").GetString() ?? "";
                string model = (a.StartsWith(body + "::", StringComparison.Ordinal) ? b : a).Split("::")[0];
                if (model is "ground_plane" or "" || model == body) continue;
                Touched(body, model, c);
                break;
            }
    }

    // One contact per touch: a model pressed again after the shell was quiet on it for a moment is a new contact.
    private void Touched(string body, string model, JsonElement contact)
    {
        var now = DateTime.UtcNow;
        lock (gate)
        {
            bool fresh = !pressed.TryGetValue(body, out var last) || last.With != model || now - last.Last > Released;
            pressed[body] = (model, now);
            if (!fresh || !truth.TryGetValue(body, out var at)) return;
            double bearing = 0;
            if (contact.TryGetProperty("positions", out var positions) && positions.GetArrayLength() > 0)
            {
                double sx = 0, sy = 0; int n = 0;
                foreach (var p in positions.EnumerateArray()) { sx += p.GetProperty("x").GetDouble(); sy += p.GetProperty("y").GetDouble(); n++; }
                bearing = Math.Atan2(Math.Sin(Math.Atan2(sy / n - at.Y, sx / n - at.X) - at.Heading), Math.Cos(Math.Atan2(sy / n - at.Y, sx / n - at.X) - at.Heading));
            }
            contacts.Add(new WorldContact(body, model, at.X, at.Y, at.Heading, bearing, ++sequence));
        }
    }

    private static async Task UntilAsync(Func<bool> condition, TimeSpan timeout, string what)
    {
        var until = DateTime.UtcNow + timeout;
        while (!condition())
        {
            if (DateTime.UtcNow > until) throw new TimeoutException($"waited {timeout} for {what}");
            await Task.Delay(100);
        }
    }
}
