using System.Globalization;
using Choreography.Dispatch;

namespace GolemAPI.Choreography;

// The golem's ops messages (consume-and-dispatch guide). Producers emit a bare
// payload plus a "kind" header; the InputRouting at the boundary prepends the
// TypeId tag — so the wire layout is decided in exactly one place. Deserialize
// parses raw[1..] (raw[0] is the tag).

// The order: the golem tells the host where to drive next — one point of its queue.
public sealed class MissionOrdered : IDispatchMessage
{
    public static int TypeId => 'O';
    public int Id { get; private init; }
    public double X { get; private init; }
    public double Y { get; private init; }

    public static IDispatchMessage Deserialize(string raw)
    {
        var parts = raw[1..].Split('|');
        return new MissionOrdered
        {
            Id = int.Parse(parts[0], CultureInfo.InvariantCulture),
            X = double.Parse(parts[1], CultureInfo.InvariantCulture),
            Y = double.Parse(parts[2], CultureInfo.InvariantCulture)
        };
    }

    public static string Payload(int id, double x, double y) =>
        $"{id}|{x.ToString("R", CultureInfo.InvariantCulture)}|{y.ToString("R", CultureInfo.InvariantCulture)}";
}

// The golem decided the road of a mission: its legs, as RoadLeg encodes them — written back as one act per leg.
public sealed class MissionRouted : IDispatchMessage
{
    public static int TypeId => 'T';
    public int Id { get; private init; }
    public string Road { get; private init; } = "";

    public static IDispatchMessage Deserialize(string raw)
    {
        int bar = raw.IndexOf('|');
        return new MissionRouted
        {
            Id = int.Parse(raw[1..bar], CultureInfo.InvariantCulture),
            Road = raw[(bar + 1)..]
        };
    }

    public static string Payload(int id, string road) => $"{id}|{road}";
}

// The body crossed the next leg of a mission's road that is not a stop: a passage (kitchen/north, north~center)
// or a point to pass (around, aside) — the point travels too, so the act can name it.
public sealed class PassageCrossed : IDispatchMessage
{
    public static int TypeId => 'P';
    public int Id { get; private init; }
    public string Passage { get; private init; } = "";
    public double X { get; private init; }
    public double Y { get; private init; }

    public static IDispatchMessage Deserialize(string raw)
    {
        var parts = raw[1..].Split('|');
        return new PassageCrossed
        {
            Id = int.Parse(parts[0], CultureInfo.InvariantCulture),
            Passage = parts[1],
            X = double.Parse(parts[2], CultureInfo.InvariantCulture),
            Y = double.Parse(parts[3], CultureInfo.InvariantCulture)
        };
    }

    public static string Payload(int id, string passage, double x, double y) =>
        $"{id}|{passage}|{x.ToString("R", CultureInfo.InvariantCulture)}|{y.ToString("R", CultureInfo.InvariantCulture)}";
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
    // Where the body itself stood when it touched: what a peer needs to step out of ITS way.
    public double PoseX { get; private init; }
    public double PoseY { get; private init; }

    public static IDispatchMessage Deserialize(string raw)
    {
        var parts = raw[1..].Split('|');
        return new MissionBumped
        {
            Id = int.Parse(parts[0], CultureInfo.InvariantCulture),
            X = double.Parse(parts[1], CultureInfo.InvariantCulture),
            Y = double.Parse(parts[2], CultureInfo.InvariantCulture),
            Heading = double.Parse(parts[3], CultureInfo.InvariantCulture),
            PoseX = double.Parse(parts[4], CultureInfo.InvariantCulture),
            PoseY = double.Parse(parts[5], CultureInfo.InvariantCulture)
        };
    }

    public static string Payload(int id, double x, double y, double heading, double poseX, double poseY) =>
        $"{id}|{x.ToString("R", CultureInfo.InvariantCulture)}|{y.ToString("R", CultureInfo.InvariantCulture)}|{heading.ToString("R", CultureInfo.InvariantCulture)}|{poseX.ToString("R", CultureInfo.InvariantCulture)}|{poseY.ToString("R", CultureInfo.InvariantCulture)}";
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
