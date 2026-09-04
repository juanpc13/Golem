using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace GolemHost.Membrane;

public sealed record Pose(double X, double Y, double Theta);

// What the body last touched, as the simulator's contact sensor reports it: the other
// model's name, and when. Telemetry — it lives in memory, never in the journal.
public sealed record Contact(string With, DateTime At);

// The golem's membrane to the ROS world: JSON over websocket against rosbridge. Behind it,
// ros_gz_bridge turns the body's Gazebo topics into ROS topics:
//   /model/<body>/cmd_vel   geometry_msgs/Twist        (in)  how the body is driven
//   /model/<body>/odometry  nav_msgs/Odometry          (out) where the body REALLY is (ground truth)
//   /model/<body>/contacts  ros_gz_interfaces/Contacts (out) what the body touches, by name
//   /sim/teleport           geometry_msgs/PoseStamped  (in)  the lab lever: put a body on a mark
// Everything arriving here is ephemeral telemetry; nothing of it reaches the journal.
public sealed class Rosbridge : IAsyncDisposable
{
    private ClientWebSocket ws = new();
    private readonly string url;
    private readonly string body;
    private readonly CancellationTokenSource readerCts = new();
    private Task reader;

    public volatile Pose LatestPose;
    public volatile Contact LatestContact;

    private string CmdVel => $"/model/{body}/cmd_vel";
    private string Odometry => $"/model/{body}/odometry";
    private string Contacts => $"/model/{body}/contacts";
    private const string Teleport = "/sim/teleport";

    public Rosbridge(string url, string body)
    {
        this.url = url;
        this.body = body;
    }

    public async Task ConnectAsync(CancellationToken ct)
    {
        for (int attempt = 1; ; attempt++)
        {
            try
            {
                await ws.ConnectAsync(new Uri(url), ct);
                break;
            }
            catch (Exception) when (attempt < 60)
            {
                // a ClientWebSocket that failed to connect is unusable — start over with a fresh one
                ws.Dispose();
                ws = new ClientWebSocket();
                Console.WriteLine($"[membrane] rosbridge not answering at {url}, retry {attempt}...");
                await Task.Delay(2000, ct);
            }
        }

        Console.WriteLine($"[membrane] connected to {url}");
    }

    // Declare what we publish and subscribe what we watch. Types are stated explicitly: a topic
    // whose publisher is not up yet cannot be inferred by rosbridge, and stating them costs nothing.
    public async Task BindAsync(CancellationToken ct)
    {
        await SendAsync(new { op = "advertise", topic = CmdVel, type = "geometry_msgs/Twist" }, ct);
        await SendAsync(new { op = "advertise", topic = Teleport, type = "geometry_msgs/PoseStamped" }, ct);
        await SendAsync(new { op = "subscribe", topic = Odometry, type = "nav_msgs/Odometry", throttle_rate = 50 }, ct);
        await SendAsync(new { op = "subscribe", topic = Contacts, type = "ros_gz_interfaces/Contacts", throttle_rate = 50 }, ct);
        reader = Task.Run(() => ReadLoopAsync(readerCts.Token), CancellationToken.None);
        Console.WriteLine($"[membrane] driving {CmdVel}; listening on {Odometry} and {Contacts}");
    }

    public Task DriveAsync(double linear, double angular, CancellationToken ct) =>
        SendAsync(new
        {
            op = "publish",
            topic = CmdVel,
            msg = new
            {
                linear = new { x = linear, y = 0.0, z = 0.0 },
                angular = new { x = 0.0, y = 0.0, z = angular }
            }
        }, ct);

    // Put the body on a mark (lab lever; see sim/bridge/teleport.py). The frame_id names the model.
    public Task TeleportAsync(double x, double y, double theta, CancellationToken ct) =>
        SendAsync(new
        {
            op = "publish",
            topic = Teleport,
            msg = new
            {
                header = new { frame_id = body },
                pose = new
                {
                    position = new { x, y, z = 0.02 },
                    orientation = new { x = 0.0, y = 0.0, z = Math.Sin(theta / 2), w = Math.Cos(theta / 2) }
                }
            }
        }, ct);

    private async Task ReadLoopAsync(CancellationToken ct)
    {
        var buffer = new byte[64 * 1024];
        var message = new StringBuilder();
        try
        {
            while (!ct.IsCancellationRequested)
            {
                message.Clear();
                WebSocketReceiveResult r;
                do
                {
                    r = await ws.ReceiveAsync(buffer, ct);
                    message.Append(Encoding.UTF8.GetString(buffer, 0, r.Count));
                } while (!r.EndOfMessage);

                using var doc = JsonDocument.Parse(message.ToString());
                if (!doc.RootElement.TryGetProperty("topic", out var topic)) continue;
                string name = topic.GetString();
                if (name == Odometry) ReadPose(doc.RootElement.GetProperty("msg"));
                else if (name == Contacts) ReadContacts(doc.RootElement.GetProperty("msg"));
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException e)
        {
            Console.WriteLine($"[membrane] connection lost: {e.Message}");
        }
    }

    // nav_msgs/Odometry: the planar pose is x, y and the yaw of the orientation quaternion.
    private void ReadPose(JsonElement msg)
    {
        var pose = msg.GetProperty("pose").GetProperty("pose");
        var p = pose.GetProperty("position");
        var q = pose.GetProperty("orientation");
        double qx = q.GetProperty("x").GetDouble(), qy = q.GetProperty("y").GetDouble();
        double qz = q.GetProperty("z").GetDouble(), qw = q.GetProperty("w").GetDouble();
        double yaw = Math.Atan2(2 * (qw * qz + qx * qy), 1 - 2 * (qy * qy + qz * qz));
        LatestPose = new Pose(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble(), yaw);
    }

    // ros_gz_interfaces/Contacts: each contact names both collisions as model::link::collision.
    // The other party's MODEL is what the golem cares about ("crate", "red", "wall_east_w").
    // Resting on the floor is not touching anything.
    private void ReadContacts(JsonElement msg)
    {
        foreach (var c in msg.GetProperty("contacts").EnumerateArray())
        {
            string a = c.GetProperty("collision1").GetProperty("name").GetString() ?? "";
            string b = c.GetProperty("collision2").GetProperty("name").GetString() ?? "";
            string other = a.StartsWith(body + "::", StringComparison.Ordinal) ? b : a;
            string model = other.Split("::")[0];
            if (model == "ground_plane" || model == body || model == "") continue;
            LatestContact = new Contact(model, DateTime.UtcNow);
            return;
        }
    }

    private Task SendAsync(object o, CancellationToken ct) =>
        ws.SendAsync(Encoding.UTF8.GetBytes(JsonSerializer.Serialize(o)),
                     WebSocketMessageType.Text, endOfMessage: true, ct);

    public async ValueTask DisposeAsync()
    {
        readerCts.Cancel();
        if (reader != null)
            try { await reader; } catch { }
        ws.Dispose();
    }
}
