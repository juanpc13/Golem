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
public sealed class MissionPassed : IDispatchMessage
{
    public static int TypeId => 'P';
    public int Id { get; private init; }
    public string Passage { get; private init; } = "";

    public static IDispatchMessage Deserialize(string raw)
    {
        int bar = raw.IndexOf('|');
        return new MissionPassed
        {
            Id = int.Parse(raw[1..bar], CultureInfo.InvariantCulture),
            Passage = raw[(bar + 1)..]
        };
    }

    public static string Payload(int id, string passage) => $"{id}|{passage}";
}

// A told point was made obsolete by a newer one: the follower lets it go.
public sealed class MissionSuperseded : IDispatchMessage
{
    public static int TypeId => 'U';
    public int Id { get; private init; }
    public int By { get; private init; }

    public static IDispatchMessage Deserialize(string raw)
    {
        int bar = raw.IndexOf('|');
        return new MissionSuperseded
        {
            Id = int.Parse(raw[1..bar], CultureInfo.InvariantCulture),
            By = int.Parse(raw[(bar + 1)..], CultureInfo.InvariantCulture)
        };
    }

    public static string Payload(int id, int by) => $"{id}|{by}";
}

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

// The operator asked the golem to let go of every mission.
public sealed class GolemRetired : IDispatchMessage
{
    public static int TypeId => 'X';
    public string Reason { get; private init; } = "";

    public static IDispatchMessage Deserialize(string raw) => new GolemRetired { Reason = raw[1..] };

    public static string Payload(string reason) => reason;
}
