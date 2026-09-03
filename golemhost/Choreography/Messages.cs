using System.Globalization;
using Choreography.Dispatch;

namespace GolemHost.Choreography;

// The golem's ops messages (consume-and-dispatch guide). Producers emit a bare
// payload plus a "kind" header; the InputRouting at the boundary prepends the
// TypeId tag — so the wire layout is decided in exactly one place. Deserialize
// parses raw[1..] (raw[0] is the tag).

public sealed class MissionSucceeded : IDispatchMessage
{
    public static int TypeId => 'S';
    public int Id { get; private init; }

    public static IDispatchMessage Deserialize(string raw) =>
        new MissionSucceeded { Id = int.Parse(raw[1..], CultureInfo.InvariantCulture) };

    public static string Payload(int id) => id.ToString(CultureInfo.InvariantCulture);
}

public sealed class MissionFailed : IDispatchMessage
{
    public static int TypeId => 'F';
    public int Id { get; private init; }
    public string Reason { get; private init; } = "";

    public static IDispatchMessage Deserialize(string raw)
    {
        int bar = raw.IndexOf('|');
        return new MissionFailed
        {
            Id = int.Parse(raw[1..bar], CultureInfo.InvariantCulture),
            Reason = raw[(bar + 1)..]
        };
    }

    public static string Payload(int id, string reason) => $"{id}|{reason}";
}

public sealed class MissionRerouted : IDispatchMessage
{
    public static int TypeId => 'R';
    public int Id { get; private init; }
    public double X { get; private init; }
    public double Y { get; private init; }
    public string Reason { get; private init; } = "";

    public static IDispatchMessage Deserialize(string raw)
    {
        int bar = raw.IndexOf('|');
        var head = raw[1..bar].Split(',');
        return new MissionRerouted
        {
            Id = int.Parse(head[0], CultureInfo.InvariantCulture),
            X = double.Parse(head[1], CultureInfo.InvariantCulture),
            Y = double.Parse(head[2], CultureInfo.InvariantCulture),
            Reason = raw[(bar + 1)..]
        };
    }

    public static string Payload(int id, double x, double y, string reason) =>
        $"{id},{x.ToString(CultureInfo.InvariantCulture)},{y.ToString(CultureInfo.InvariantCulture)}|{reason}";
}

// The operator asked the golem to let go of every mission.
public sealed class GolemRetired : IDispatchMessage
{
    public static int TypeId => 'X';
    public string Reason { get; private init; } = "";

    public static IDispatchMessage Deserialize(string raw) => new GolemRetired { Reason = raw[1..] };

    public static string Payload(string reason) => reason;
}
