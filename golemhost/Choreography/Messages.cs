using System.Globalization;
using Choreography.Dispatch;

namespace GolemHost.Choreography;

// The golem's ops wire (consume-and-dispatch guide): raw[0] carries the TypeId
// tag, the rest is the payload. The tag and the layout are OURS — the framework
// only assumes "a message with some arguments". Wire() builds what Deserialize parses.

public sealed class MissionOrdered : IDispatchMessage
{
    public static int TypeId => 'O';
    public double X { get; private init; }
    public double Y { get; private init; }

    public static IDispatchMessage Deserialize(string raw)
    {
        var xy = raw[1..].Split(',');
        return new MissionOrdered
        {
            X = double.Parse(xy[0], CultureInfo.InvariantCulture),
            Y = double.Parse(xy[1], CultureInfo.InvariantCulture)
        };
    }

    public static string Wire(double x, double y) =>
        $"{(char)TypeId}{x.ToString(CultureInfo.InvariantCulture)},{y.ToString(CultureInfo.InvariantCulture)}";
}

public sealed class MissionSucceeded : IDispatchMessage
{
    public static int TypeId => 'S';
    public int Id { get; private init; }

    public static IDispatchMessage Deserialize(string raw) =>
        new MissionSucceeded { Id = int.Parse(raw[1..], CultureInfo.InvariantCulture) };

    public static string Wire(int id) => $"{(char)TypeId}{id}";
}

public sealed class MissionFailed : IDispatchMessage
{
    public static int TypeId => 'F';
    public int Id { get; private init; }
    public string Reason { get; private init; } = "";

    public static IDispatchMessage Deserialize(string raw)
    {
        int sep = raw.IndexOf('|');
        return new MissionFailed
        {
            Id = int.Parse(raw[1..sep], CultureInfo.InvariantCulture),
            Reason = raw[(sep + 1)..]
        };
    }

    public static string Wire(int id, string reason) => $"{(char)TypeId}{id}|{reason}";
}
