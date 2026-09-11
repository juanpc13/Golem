using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace GolemAPI.Membrane;

public sealed record Pose(double X, double Y, double Theta);

// What the body last touched, as the simulator's contact sensor reports it: the other
// model's name, and when. Telemetry — it lives in memory, never in the journal.
/// <summary>A touch on the body's shell: what it touched (the world's name for it), when, and WHERE ON THE SHELL —
/// the bearing of the touch relative to the body's heading (0 = the nose, +π/2 = the left flank, -π/2 = the right).
/// A bumper knows no more than that; where the touch lands on the plane follows from the pose the golem believes.</summary>
public sealed record Contact(string With, DateTime At, double Bearing)
{
    /// <summary>The touch on the plane, as the golem reckons it: a radius from the centre of the body it believes at
    /// <paramref name="pose"/>, in the direction of the bearing. The heading returned points from the body INTO what
    /// it touched (the mark's normal).</summary>
    public (double X, double Y, double Heading) On(Pose pose, double radius)
    {
        double heading = Math.Atan2(Math.Sin(pose.Theta + Bearing), Math.Cos(pose.Theta + Bearing));
        return (pose.X + radius * Math.Cos(heading), pose.Y + radius * Math.Sin(heading), heading);
    }
}

// Where the golem's idea of its own position comes from.
//   World  — the simulator's ground truth (a gift no real robot gets).
//   Wheels — dead reckoning: the pose the wheels believe, integrated by the drive plugin from
//            wheel speeds and anchored ONCE to the true pose when the golem binds (the operator
//            telling the robot where it stands at start-up). It lies whenever the wheels slip or
//            the body is pushed — exactly as a real robot without a sensor on the world would.
public enum PoseSource { World, Wheels }

// The golem's membrane to the ROS world: JSON over websocket against rosbridge. Behind it,
// ros_gz_bridge turns the body's Gazebo topics into ROS topics:
//   /model/<body>/cmd_vel         geometry_msgs/Twist        (in)  how the body is driven
//   /model/<body>/odometry        nav_msgs/Odometry          (out) where the body REALLY is (ground truth)
//   /model/<body>/wheel_odometry  nav_msgs/Odometry          (out) where the wheels BELIEVE it is
//   /model/<body>/contacts        ros_gz_interfaces/Contacts (out) what the body touches, by name, and where on its shell
//   /sim/teleport                 geometry_msgs/PoseStamped  (in)  the lab lever: put a body on a mark
// Everything arriving here is ephemeral telemetry; nothing of it reaches the journal.
public sealed class Rosbridge : IAsyncDisposable
{
    private ClientWebSocket ws = new();
    private readonly string url;
    private readonly string body;
    private readonly CancellationTokenSource readerCts = new();
    private Task reader;

    // Dead reckoning's anchor: the wheels' pose and the true pose at the moment of calibration.
    private Pose odometryAtStart, truthAtStart;
    private volatile bool calibrate = true;

    public PoseSource Source { get; }

    /// <summary>Where the golem believes its body is: the truth, or dead reckoning. THE pose the golem acts on.</summary>
    public volatile Pose LatestPose;
    /// <summary>Where the body really is, for the operator's eyes only (the panel's ghost). Never for the golem.</summary>
    public volatile Pose LatestTruth;
    public volatile Contact LatestContact;

    private string CmdVel => $"/model/{body}/cmd_vel";
    private string Odometry => $"/model/{body}/odometry";
    private string WheelOdometry => $"/model/{body}/wheel_odometry";
    private string Contacts => $"/model/{body}/contacts";
    private const string Teleport = "/sim/teleport";

    public Rosbridge(string url, string body, PoseSource source)
    {
        this.url = url;
        this.body = body;
        Source = source;
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
        if (Source == PoseSource.Wheels)
            await SendAsync(new { op = "subscribe", topic = WheelOdometry, type = "nav_msgs/Odometry", throttle_rate = 50 }, ct);
        await SendAsync(new { op = "subscribe", topic = Contacts, type = "ros_gz_interfaces/Contacts", throttle_rate = 50 }, ct);
        reader = Task.Run(() => ReadLoopAsync(readerCts.Token), CancellationToken.None);
        Console.WriteLine($"[membrane] driving {CmdVel}; pose from {(Source == PoseSource.Wheels ? WheelOdometry + " (dead reckoning)" : Odometry + " (the world's truth)")}; contacts on {Contacts}");
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
    // A body carried somewhere is told where it stands: dead reckoning re-anchors once it has settled.
    public async Task TeleportAsync(double x, double y, double theta, CancellationToken ct)
    {
        await SendAsync(new
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
        if (Source == PoseSource.Wheels)
            _ = Task.Run(async () => { await Task.Delay(1500); calibrate = true; });
    }

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
                if (name == Odometry) ReadTruth(doc.RootElement.GetProperty("msg"));
                else if (name == WheelOdometry) ReadWheels(doc.RootElement.GetProperty("msg"));
                else if (name == Contacts) ReadContacts(doc.RootElement.GetProperty("msg"));
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException e)
        {
            Console.WriteLine($"[membrane] connection lost: {e.Message}");
        }
    }

    private void ReadTruth(JsonElement msg)
    {
        var truth = ParsePose(msg);
        LatestTruth = truth;
        if (Source == PoseSource.World) LatestPose = truth;
    }

    // Dead reckoning: the wheels' pose, expressed in the world through the anchor taken at calibration.
    private void ReadWheels(JsonElement msg)
    {
        var odometry = ParsePose(msg);
        var truth = LatestTruth;
        if (truth == null) return;   // nothing to anchor to yet
        if (calibrate)
        {
            odometryAtStart = odometry;
            truthAtStart = truth;
            calibrate = false;
            Console.WriteLine($"[membrane] dead reckoning anchored: the body is told it stands at ({truth.X:0.00}, {truth.Y:0.00})");
        }
        double dx = odometry.X - odometryAtStart.X, dy = odometry.Y - odometryAtStart.Y;
        double turn = truthAtStart.Theta - odometryAtStart.Theta;
        LatestPose = new Pose(
            truthAtStart.X + dx * Math.Cos(turn) - dy * Math.Sin(turn),
            truthAtStart.Y + dx * Math.Sin(turn) + dy * Math.Cos(turn),
            Normalize(truthAtStart.Theta + (odometry.Theta - odometryAtStart.Theta)));
    }

    // nav_msgs/Odometry: the planar pose is x, y and the yaw of the orientation quaternion.
    private static Pose ParsePose(JsonElement msg)
    {
        var pose = msg.GetProperty("pose").GetProperty("pose");
        var p = pose.GetProperty("position");
        var q = pose.GetProperty("orientation");
        double qx = q.GetProperty("x").GetDouble(), qy = q.GetProperty("y").GetDouble();
        double qz = q.GetProperty("z").GetDouble(), qw = q.GetProperty("w").GetDouble();
        double yaw = Math.Atan2(2 * (qw * qz + qx * qy), 1 - 2 * (qy * qy + qz * qz));
        return new Pose(p.GetProperty("x").GetDouble(), p.GetProperty("y").GetDouble(), yaw);
    }

    // ros_gz_interfaces/Contacts: each contact names both collisions as model::link::collision.
    // The other party's MODEL is what the golem cares about ("crate", "red", "wall_east_w"), and WHERE ON THE SHELL
    // it touched: the sensor reports the contact points in the world's frame, so the bearing is taken against the
    // body's TRUE pose (a bumper knows which part of the shell was pressed, whatever the body believes about where
    // it stands) — the world's coordinates never leave this method. Without positions, or before the first truth,
    // the touch is taken head-on (bearing 0), as it always was. Resting on the floor is not touching anything.
    private void ReadContacts(JsonElement msg)
    {
        foreach (var c in msg.GetProperty("contacts").EnumerateArray())
        {
            string a = c.GetProperty("collision1").GetProperty("name").GetString() ?? "";
            string b = c.GetProperty("collision2").GetProperty("name").GetString() ?? "";
            string other = a.StartsWith(body + "::", StringComparison.Ordinal) ? b : a;
            string model = other.Split("::")[0];
            if (model == "ground_plane" || model == body || model == "") continue;
            LatestContact = new Contact(model, DateTime.UtcNow, BearingOf(c));
            return;
        }
    }

    private double BearingOf(JsonElement contact)
    {
        var truth = LatestTruth;
        if (truth == null || !contact.TryGetProperty("positions", out var positions) || positions.GetArrayLength() == 0) return 0;
        double sx = 0, sy = 0; int n = 0;
        foreach (var p in positions.EnumerateArray()) { sx += p.GetProperty("x").GetDouble(); sy += p.GetProperty("y").GetDouble(); n++; }
        return Normalize(Math.Atan2(sy / n - truth.Y, sx / n - truth.X) - truth.Theta);
    }

    private static double Normalize(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
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
