using System.Net.WebSockets;
using System.Text;
using System.Text.Json;

namespace GolemHost.Membrane;

public sealed record Pose(double X, double Y, double Theta);

// The golem's membrane to the ROS world: JSON over websocket against rosbridge.
// The pose arriving here is ephemeral telemetry — it lives in memory, never in the journal.
public sealed class Rosbridge : IAsyncDisposable
{
    private ClientWebSocket ws = new();
    private readonly string url;
    private readonly string turtle;
    private readonly CancellationTokenSource readerCts = new();
    private Task reader;

    public volatile Pose LatestPose;

    public Rosbridge(string url, string turtle)
    {
        this.url = url;
        this.turtle = turtle;
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
            catch (Exception) when (attempt < 30)
            {
                // a ClientWebSocket that failed to connect is unusable — start over with a fresh one
                ws.Dispose();
                ws = new ClientWebSocket();
                Console.WriteLine($"[membrane] rosbridge not answering at {url}, retry {attempt}...");
                await Task.Delay(2000, ct);
            }
        }

        await SendAsync(new { op = "advertise", topic = $"/{turtle}/cmd_vel", type = "geometry_msgs/Twist" }, ct);
        await SendAsync(new { op = "subscribe", topic = $"/{turtle}/pose", throttle_rate = 100 }, ct);
        reader = Task.Run(() => ReadLoopAsync(readerCts.Token), CancellationToken.None);
        Console.WriteLine($"[membrane] connected to {url}, listening on /{turtle}/pose");
    }

    public Task CallServiceAsync(string service, object args, CancellationToken ct) =>
        SendAsync(args == null
            ? new { op = "call_service", service }
            : (object)new { op = "call_service", service, args }, ct);

    public Task DriveAsync(double linear, double angular, CancellationToken ct) =>
        SendAsync(new
        {
            op = "publish",
            topic = $"/{turtle}/cmd_vel",
            msg = new
            {
                linear = new { x = linear, y = 0.0, z = 0.0 },
                angular = new { x = 0.0, y = 0.0, z = angular }
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
                if (doc.RootElement.TryGetProperty("topic", out var topic) &&
                    topic.GetString() == $"/{turtle}/pose")
                {
                    var msg = doc.RootElement.GetProperty("msg");
                    LatestPose = new Pose(
                        msg.GetProperty("x").GetDouble(),
                        msg.GetProperty("y").GetDouble(),
                        msg.GetProperty("theta").GetDouble());
                }
            }
        }
        catch (OperationCanceledException) { }
        catch (WebSocketException e)
        {
            Console.WriteLine($"[membrane] connection lost: {e.Message}");
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
