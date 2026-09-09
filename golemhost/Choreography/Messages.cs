using System.Globalization;
using Choreography.Dispatch;

namespace GolemHost.Choreography;

// The golem's ops messages (consume-and-dispatch guide). Producers emit a bare
// payload plus a "kind" header; the InputRouting at the boundary prepends the
// TypeId tag — so the wire layout is decided in exactly one place. Deserialize
// parses raw[1..] (raw[0] is the tag).

// The golem decided the road of a mission: the plan as the journal will read it.
public sealed class MissionRouted : IDispatchMessage
{
    public static int TypeId => 'T';
    public int Id { get; private init; }
    public string Plan { get; private init; } = "";

    public static IDispatchMessage Deserialize(string raw)
    {
        int bar = raw.IndexOf('|');
        return new MissionRouted
        {
            Id = int.Parse(raw[1..bar], CultureInfo.InvariantCulture),
            Plan = raw[(bar + 1)..]
        };
    }

    public static string Payload(int id, string plan) => $"{id}|{plan}";
}

// The body crossed the next passage of a mission's road.
public sealed class PassageCrossed : IDispatchMessage
{
    public static int TypeId => 'P';
    public int Id { get; private init; }
    public string Passage { get; private init; } = "";

    public static IDispatchMessage Deserialize(string raw)
    {
        int bar = raw.IndexOf('|');
        return new PassageCrossed
        {
            Id = int.Parse(raw[1..bar], CultureInfo.InvariantCulture),
            Passage = raw[(bar + 1)..]
        };
    }

    public static string Payload(int id, string passage) => $"{id}|{passage}";
}

// The body reached the next stop of a mission's road (the last one completes the mission).
public sealed class StopReached : IDispatchMessage
{
    public static int TypeId => 'S';
    public int Id { get; private init; }
    public double X { get; private init; }
    public double Y { get; private init; }

    public static IDispatchMessage Deserialize(string raw)
    {
        var parts = raw[1..].Split('|');
        return new StopReached
        {
            Id = int.Parse(parts[0], CultureInfo.InvariantCulture),
            X = double.Parse(parts[1], CultureInfo.InvariantCulture),
            Y = double.Parse(parts[2], CultureInfo.InvariantCulture)
        };
    }

    public static string Payload(int id, double x, double y) =>
        $"{id}|{x.ToString("R", CultureInfo.InvariantCulture)}|{y.ToString("R", CultureInfo.InvariantCulture)}";
}

// The golem concluded a touch was a thing: a mark on the map of touches, with the heading of the touch (its normal).
public sealed class ObstacleMarked : IDispatchMessage
{
    public static int TypeId => 'M';
    public double X { get; private init; }
    public double Y { get; private init; }
    public double Heading { get; private init; }

    public static IDispatchMessage Deserialize(string raw)
    {
        var parts = raw[1..].Split('|');
        return new ObstacleMarked
        {
            X = double.Parse(parts[0], CultureInfo.InvariantCulture),
            Y = double.Parse(parts[1], CultureInfo.InvariantCulture),
            Heading = double.Parse(parts[2], CultureInfo.InvariantCulture)
        };
    }

    public static string Payload(double x, double y, double heading) =>
        $"{x.ToString("R", CultureInfo.InvariantCulture)}|{y.ToString("R", CultureInfo.InvariantCulture)}|{heading.ToString("R", CultureInfo.InvariantCulture)}";
}

// The golem concluded a touch was a peer: it met that body there. History, not geometry.
public sealed class PeerMet : IDispatchMessage
{
    public static int TypeId => 'E';
    public string Who { get; private init; } = "";
    public double X { get; private init; }
    public double Y { get; private init; }

    public static IDispatchMessage Deserialize(string raw)
    {
        var parts = raw[1..].Split('|');
        return new PeerMet
        {
            Who = parts[0],
            X = double.Parse(parts[1], CultureInfo.InvariantCulture),
            Y = double.Parse(parts[2], CultureInfo.InvariantCulture)
        };
    }

    public static string Payload(string who, double x, double y) =>
        $"{who}|{x.ToString("R", CultureInfo.InvariantCulture)}|{y.ToString("R", CultureInfo.InvariantCulture)}";
}

// The body grazed a wall the map knows, on a mission's road: its own execution error, counted against its patience.
public sealed class MissionGrazed : IDispatchMessage
{
    public static int TypeId => 'G';
    public int Id { get; private init; }
    public double X { get; private init; }
    public double Y { get; private init; }

    public static IDispatchMessage Deserialize(string raw)
    {
        var parts = raw[1..].Split('|');
        return new MissionGrazed
        {
            Id = int.Parse(parts[0], CultureInfo.InvariantCulture),
            X = double.Parse(parts[1], CultureInfo.InvariantCulture),
            Y = double.Parse(parts[2], CultureInfo.InvariantCulture)
        };
    }

    public static string Payload(int id, double x, double y) =>
        $"{id}|{x.ToString("R", CultureInfo.InvariantCulture)}|{y.ToString("R", CultureInfo.InvariantCulture)}";
}

// The body touched something the map does not hold, heading that way — on a mission's road (Id > 0), or while standing idle (Id 0).
public sealed class MissionBumped : IDispatchMessage
{
    public static int TypeId => 'B';
    public int Id { get; private init; }
    public double X { get; private init; }
    public double Y { get; private init; }
    public double Heading { get; private init; }

    public static IDispatchMessage Deserialize(string raw)
    {
        var parts = raw[1..].Split('|');
        return new MissionBumped
        {
            Id = int.Parse(parts[0], CultureInfo.InvariantCulture),
            X = double.Parse(parts[1], CultureInfo.InvariantCulture),
            Y = double.Parse(parts[2], CultureInfo.InvariantCulture),
            Heading = double.Parse(parts[3], CultureInfo.InvariantCulture)
        };
    }

    public static string Payload(int id, double x, double y, double heading) =>
        $"{id}|{x.ToString("R", CultureInfo.InvariantCulture)}|{y.ToString("R", CultureInfo.InvariantCulture)}|{heading.ToString("R", CultureInfo.InvariantCulture)}";
}

// The world said no: a collision, a stall, no road.
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

// The golem lets a mission go: a newer told point made it pointless.
public sealed class MissionAbandoned : IDispatchMessage
{
    public static int TypeId => 'U';
    public int Id { get; private init; }
    public string Reason { get; private init; } = "";

    public static IDispatchMessage Deserialize(string raw)
    {
        int bar = raw.IndexOf('|');
        return new MissionAbandoned
        {
            Id = int.Parse(raw[1..bar], CultureInfo.InvariantCulture),
            Reason = raw[(bar + 1)..]
        };
    }

    public static string Payload(int id, string reason) => $"{id}|{reason}";
}

// The operator asked the golem to let go of every pending mission.
public sealed class EverythingLetGo : IDispatchMessage
{
    public static int TypeId => 'X';
    public string Reason { get; private init; } = "";

    public static IDispatchMessage Deserialize(string raw) => new EverythingLetGo { Reason = raw[1..] };

    public static string Payload(string reason) => reason;
}
