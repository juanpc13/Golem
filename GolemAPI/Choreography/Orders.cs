using System.Globalization;
using System.Text.Json;

namespace GolemAPI.Choreography;

// What the golem tells its body to do NOW — one thing at a time (Juan, 16-sep-2026: "un comando ejecutado produce un
// print del siguiente punto que debe alcanzar y una vez alcanzado pide el siguiente"). Every script that changes it
// (GolemController: the errand, a point reached, a bump, a hold…) ends with the same print, and the command RETURNS
// that print to whoever performed it, at write time. Four orders exist, one thing each: TURN in place to the next leg's
// heading, RUN to the next leg's point (kind door/opening/via/stop, its approach and exit), HOLD (the operator paused the
// route), or DECIDE (the route has no way, or a bump interrupted it: the route decides it again from where the body
// stands — `route.Decide(from)`). Nothing pending → no order.
public sealed record Order(int Route, string What, string Kind, string Name, double X, double Y, double AX, double AY, double EX, double EY,
                           bool HasHeading, double Heading, bool Following, int StopsLeft)
{
    public bool IsCrossedStraight => AX != EX || AY != EY;

    /// <summary>No order at all — what the driver is told when the operator lets go: drop what you are doing.</summary>
    public static readonly Order None = new(0, "none", "", "", 0, 0, 0, 0, 0, 0, false, 0, false, 0);

    /// <summary>The same order as another: the same route asked to do the same thing at the same point.</summary>
    public bool SameAs(Order other) =>
        other != null && Route == other.Route && What == other.What && Kind == other.Kind && X == other.X && Y == other.Y;

    // The labels the golem prints (GolemController.NextOrder): route, order (hold | decide | turn | run) and, for a leg,
    // kind name x y ax ay ex ey hasHeading heading following stopsLeft.
    public static Order Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var e = doc.RootElement;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("route", out var route) || !e.TryGetProperty("order", out var what)) return null;
            string w = what.GetString() ?? "";
            double D(string n, double d = 0) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : d;
            bool B(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
            string S(string n) => e.TryGetProperty(n, out var v) ? v.GetString() ?? "" : "";
            double x = D("x"), y = D("y");
            return new Order(route.GetInt32(), w, S("kind"), S("name"), x, y, D("ax", x), D("ay", y), D("ex", x), D("ey", y),
                             B("hasHeading"), D("heading"), B("following"), (int)D("stopsLeft"));
        }
        catch (JsonException) { return null; }
    }

    public string Describe() => What == "turn" ? $"turn to {Heading:0.00}" : What != "run" ? What
        : $"{(Kind == "door" || Kind == "opening" ? Name : Kind)}@{X.ToString("0.##", CultureInfo.InvariantCulture)},{Y.ToString("0.##", CultureInfo.InvariantCulture)}"
          + (HasHeading ? $" heading {Heading:0.00}" : "");
}

// What a script answered: the order it printed (null when the golem has nothing pending), or the refusal the Check
// gave (the domain's own words) — never both.
public readonly record struct Answer(Order Order, string Refused)
{
    public bool Ok => Refused == "";
    public static Answer Of(string performed)
    {
        // a refused Check comes back as {"EWI":[{"Error":"…"}]}; anything else is the print (or nothing)
        if (!string.IsNullOrWhiteSpace(performed) && performed.Contains("\"EWI\"", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(performed);
                var first = doc.RootElement.GetProperty("EWI").EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("Error", out var error)) return new Answer(null, error.GetString() ?? "refused");
            }
            catch (JsonException) { }
            return new Answer(null, performed);
        }
        return new Answer(Order.Parse(performed), "");
    }
}

// The mailbox between whoever performs a script and the body's driver: it keeps the LATEST order (a newer one
// supersedes an older one not yet taken) and wakes the driver. The operator's endpoints and the driver's own reports
// all leave their order here; the driver takes one at a time.
public sealed class Orders
{
    private readonly object gate = new();
    private Order latest;
    private TaskCompletionSource<bool> waiter = new(TaskCreationOptions.RunContinuationsAsynchronously);

    /// <summary>Whoever drives the body listens here to drop what it is doing when a different order comes.</summary>
    public event Action<Order> Arrived;

    public void Offer(Order order)
    {
        if (order == null) return;
        TaskCompletionSource<bool> wake;
        lock (gate)
        {
            latest = order;
            wake = waiter;
            waiter = new TaskCompletionSource<bool>(TaskCreationOptions.RunContinuationsAsynchronously);
        }
        wake.TrySetResult(true);
        Arrived?.Invoke(order);
    }

    /// <summary>Drop whatever is being done: nothing is left to take, but whoever drives is told to stop.</summary>
    public void Drop() => Arrived?.Invoke(Order.None);

    public bool HasPending { get { lock (gate) return latest != null; } }

    public Order Take()
    {
        lock (gate) { var o = latest; latest = null; return o; }
    }

    /// <summary>The next order, or null when none came within the span.</summary>
    public async Task<Order> WaitAsync(TimeSpan span, CancellationToken ct)
    {
        Task<bool> pending;
        lock (gate)
        {
            if (latest != null) { var o = latest; latest = null; return o; }
            pending = waiter.Task;
        }
        var done = await Task.WhenAny(pending, Task.Delay(span, ct));
        if (ct.IsCancellationRequested) return null;
        return done == pending ? Take() : null;
    }
}
