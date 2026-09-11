using System.Globalization;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Maps;
using GolemDomain.Robots;

namespace GolemDomain.Routes;

/// <summary>
/// A trajectory — Juan's RUTA / TRAYECTORIA: the ordered legs a body walks, one straight segment from each
/// leg to the next. A mission's road is a trajectory; an evasion <see cref="Maneuver"/> is a trajectory of a
/// special kind. It reads in one line — "kitchen/north@4,9.5 > north~center@5.5,8 > garage@9,1.5" — but the
/// journal writes it act by act — the road is an object the golem opens for a mission and the acts are its own
/// (Juan, 11-sep-2026: "route = g.Route(1); … route.Via(…)"): `route = g.Route(@id); route.Via(door1, at1);
/// route.Around(around2); route.Stop(stop3);` — never as text. The last stop decides it: the mission takes the road.
/// </summary>
internal class Trajectory
{
    private readonly List<Leg> legs;
    private readonly Mission mission;    // the mission this road is being decided for; null when it was born whole
    private readonly MapLayout layout;   // where its doors stand and its stops lie
    private bool decided;

    /// <summary>A trajectory born whole — the planner's, or a maneuver's. Nothing can be added to it.</summary>
    internal Trajectory(IEnumerable<Leg> legs)
    {
        if (legs == null) throw new GolemDomainException("a trajectory needs its legs, even none");
        this.legs = legs.ToList();
        decided = true;
    }

    /// <summary>The road being decided for a mission: empty, until its acts add the legs, the last stop last.</summary>
    internal Trajectory(MapLayout layout, Mission mission)
    {
        this.layout = layout ?? throw new GolemDomainException("a road is decided on a layout");
        this.mission = mission ?? throw new GolemDomainException("a road is decided for a mission");
        legs = new List<Leg>();
    }

    // ---- the acts that decide the road, each returning the road so they chain ----

    /// <summary>A leg: cross this passage at this point — a door where the layout stands it, an opening where the road
    /// meets it.</summary>
    internal Trajectory Via(Passage passage, Position at)
    {
        if (passage == null || at == null) throw new GolemDomainException("a road crosses a passage at a point");
        if (passage is Door door && layout.PointOf(door).DistanceTo(at) > 1e-6)
            throw new GolemDomainException($"the door {door.Name} stands at ({Fmt(layout.PointOf(door).X)}, {Fmt(layout.PointOf(door).Y)}), not at ({Fmt(at.X)}, {Fmt(at.Y)})");
        return Add(new Leg(at, passage.Name));
    }

    /// <summary>A leg: skirt a mark through this point.</summary>
    internal Trajectory Around(Position at) => Add(new Leg(at ?? throw new GolemDomainException("a detour needs its point"), Leg.Detour));

    /// <summary>A leg: step out of a peer's way to this point.</summary>
    internal Trajectory Aside(Position at) => Add(new Leg(at ?? throw new GolemDomainException("a courtesy step needs its point"), Leg.Courtesy));

    /// <summary>A leg: reach this stop (named by the zone it stands in). When every stop ahead has its leg the road is
    /// decided: doors gain their straight crossings and the mission takes it.</summary>
    internal Trajectory Stop(Position at)
    {
        if (at == null) throw new GolemDomainException("a stop needs its point");
        Add(new Leg(at, layout.ZoneAt(at).Name));
        if (StopCount == mission.StopsAhead.Count())
        {
            decided = true;
            mission.Route(layout.WithDoorCrossings(this));
        }
        return this;
    }

    private Trajectory Add(Leg leg)
    {
        if (mission == null) throw new GolemDomainException("this road was born whole: nothing can be added to it");
        if (decided) throw new GolemDomainException($"mission {mission.Id}'s road is decided: nothing can be added after its last stop");
        legs.Add(leg);
        return this;
    }

    /// <summary>Whether the road is decided — its last stop added and the mission holding it — or still being drafted.</summary>
    internal bool IsDecided => decided;

    internal IReadOnlyList<Leg> Legs() => legs;
    internal int Count => legs.Count;
    internal bool IsEmpty => legs.Count == 0;
    internal int StopCount => legs.Count(l => l.IsStop);

    internal Leg LegAt(int index)
    {
        if (index < 0 || index >= legs.Count) throw new GolemDomainException($"the trajectory has {legs.Count} legs, not a leg {index}");
        return legs[index];
    }

    internal Leg Last
    {
        get
        {
            if (legs.Count == 0) throw new GolemDomainException("an empty trajectory has no last leg");
            return legs[^1];
        }
    }

    /// <summary>The stops from a leg onward: the points of the stop legs, in order.</summary>
    internal IEnumerable<Position> StopsFrom(int index) => legs.Skip(index).Where(l => l.IsStop).Select(l => l.At);

    /// <summary>The segments a body walks, from where it starts: one straight run to each leg's point in turn.</summary>
    internal IReadOnlyList<Segment> Segments(Position from)
    {
        if (from == null) throw new GolemDomainException("a trajectory is walked from somewhere");
        var segments = new List<Segment>();
        Position here = from;
        foreach (var leg in legs)
        {
            segments.Add(new Segment(here, leg.At));
            here = leg.At;
        }
        return segments;
    }

    /// <summary>How long the walk is, from where it starts, leg to leg.</summary>
    internal double Length(Position from) => Segments(from).Sum(s => s.Length);

    /// <summary>The trajectory as the journal writes it: "name@x,y > name@x,y".</summary>
    internal string AsPlan() => string.Join(" > ", legs.Select(l => $"{l.Name}@{Fmt(l.At.X)},{Fmt(l.At.Y)}"));

    private static string Fmt(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
}
