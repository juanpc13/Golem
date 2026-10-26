using GolemDomain.Geometry;
using GolemDomain.Layouts;

namespace GolemDomain.Plays;

/// <summary>THE CROSSING of the traffic play (propuesta 125, 23-oct-2026; Juan: "3 robots como en una carretera y el semáforo les habla por tell
/// para poder pasar uno recto y otro girando a su izquierda o derecha"): the zone of the map where two bodies do not fit — found by its shape,
/// the one zone open on FOUR sides to narrow zones — and its ARMS, the four aisles that meet in it, by the compass point they come from.
/// DERIVED from the map every time, never written: on every arm the STOP LINE (the arm's axis, <see cref="StopBack"/> from the crossing's edge:
/// a body stopped there keeps its nose half a metre clear of the crossing), the EXIT POINT (the same point of the arm it leaves by) and the
/// FAR END (the arm's axis, <see cref="StopBack"/> from its far side). Where a car goes from the arm it comes from is the arm's business: a
/// <see cref="Turn"/> says it in a driver's words.</summary>
internal sealed class Crossing
{
    /// <summary>How far from the crossing's edge a body stops and clears: its centre 0.75 m from the edge — a 0.25 body with the opening's margin.</summary>
    internal const double StopBack = 0.75;
    /// <summary>THE QUEUE (§12): one place behind the other along the lane, a body's length and a gap apart.</summary>
    internal const double SlotPitch = 1.0;
    /// <summary>An arm this wide or wider has TWO LANES (§12): the inbound one on the right of a car coming to the crossing, the outbound one
    /// on the right of a car leaving it. Narrower, one lane on the axis (cross-corridors).</summary>
    internal const double TwoLanesFrom = 3.0;

    private readonly Dictionary<Direction, Zone> arms = new();

    private Crossing(MapLayout map, Zone zone)
    {
        Map = map;
        Zone = zone;
        foreach (var side in zone.OpenSides())
        {
            var arm = map.ZoneNamed(side.To);
            double dx = arm.Center.X - zone.Center.X, dy = arm.Center.Y - zone.Center.Y;
            var from = Math.Abs(dx) > Math.Abs(dy) ? (dx > 0 ? Direction.East : Direction.West) : (dy > 0 ? Direction.North : Direction.South);
            if (arms.ContainsKey(from)) throw new GolemDomainException($"the crossing '{zone.Name}' has two arms to the {from.ToString().ToLowerInvariant()}");
            arms[from] = arm;
        }
        if (arms.Count != 4) throw new GolemDomainException($"the crossing '{zone.Name}' needs four arms, it has {arms.Count}");
    }

    /// <summary>The crossing IN a map: the one zone open on four sides to narrow zones. None, or more than one, refused.</summary>
    internal static Crossing In(MapLayout map)
    {
        if (map == null) throw new GolemDomainException("Crossing.In: 'map' was not given");
        var candidates = map.Zones.Where(z => z.OpenSides().Count == 4 && z.OpenSides().All(s => IsAisle(map.ZoneNamed(s.To), z))).ToList();
        if (candidates.Count == 0) throw new GolemDomainException($"the map '{map.Name}' has no crossing: no zone open on four sides to narrow zones");
        if (candidates.Count > 1) throw new GolemDomainException($"the map '{map.Name}' has {candidates.Count} crossings: the play needs one");
        return new Crossing(map, candidates[0]);
    }

    internal MapLayout Map { get; }
    internal Zone Zone { get; }
    internal IReadOnlyList<Direction> Arms => arms.Keys.OrderBy(d => d).ToList();

    internal Zone Arm(Direction from) => arms.TryGetValue(from, out var arm) ? arm : throw new GolemDomainException($"the crossing has no arm to the {from.ToString().ToLowerInvariant()}");

    /// <summary>The arm a point stands in — where a car comes from. Refused off every arm (a car sets out from an arm, never from a room).</summary>
    internal Direction ArmOf(Position at)
    {
        if (at == null) throw new GolemDomainException("Crossing.ArmOf: 'at' was not given");
        foreach (var (from, arm) in arms) if (arm.Contains(at)) return from;
        throw new GolemDomainException($"the point ({Fmt(at.X)}, {Fmt(at.Y)}) stands on no arm of the crossing: a car sets out from an arm");
    }

    /// <summary>The arm a car LEAVES by, from the arm it comes from and its turn — in a driver's words: coming from the south it travels north,
    /// its left is the west arm, its right the east one; straight on is the opposite arm.</summary>
    internal static Direction Exit(Direction from, Turn turn)
    {
        var travel = Opposite(from);
        return turn switch
        {
            Turn.Straight => travel,
            Turn.Left => travel switch { Direction.North => Direction.West, Direction.West => Direction.South, Direction.South => Direction.East, _ => Direction.North },
            Turn.Right => travel switch { Direction.North => Direction.East, Direction.East => Direction.South, Direction.South => Direction.West, _ => Direction.North },
            _ => throw new GolemDomainException($"a car goes straight, left or right, not '{turn}'"),
        };
    }

    internal static Direction Opposite(Direction d) => d switch { Direction.North => Direction.South, Direction.South => Direction.North, Direction.East => Direction.West, _ => Direction.East };

    /// <summary>How many lanes an arm has: two from <see cref="TwoLanesFrom"/> wide, else one.</summary>
    internal int Lanes(Direction arm) => Math.Min(Arm(arm).Width, Arm(arm).Height) >= TwoLanesFrom - 1e-9 ? 2 : 1;

    /// <summary>How far from the arm's axis its lanes run: a quarter of its breadth with two lanes, nothing with one.</summary>
    internal double LaneOffset(Direction arm) => Lanes(arm) == 2 ? Math.Min(Arm(arm).Width, Arm(arm).Height) / 4 : 0.0;

    /// <summary>The STOP LINE of an arm, for the k-th place of its queue (0 the line itself): on the INBOUND lane — the right of a car coming to
    /// the crossing, with two lanes; the axis with one — <see cref="StopBack"/> into the arm from the crossing's edge, plus k pitches back.</summary>
    internal Position StopLine(Direction arm, int slot = 0)
    {
        if (slot < 0) throw new GolemDomainException("Crossing.StopLine: a place of the queue is 0 or more");
        return OnLane(arm, inbound: true).Along(Inward(arm), StopBack + slot * SlotPitch);
    }

    /// <summary>The EXIT POINT of an arm: on its OUTBOUND lane, <see cref="StopBack"/> into the arm — reached, the body's tail is clear of the crossing.</summary>
    internal Position ExitPoint(Direction arm) => OnLane(arm, inbound: false).Along(Inward(arm), StopBack);

    /// <summary>The FAR END of an arm, for the k-th car to park there (0 the farthest): on its outbound lane, <see cref="StopBack"/> short of its far
    /// side, plus k pitches back toward the crossing.</summary>
    internal Position FarEnd(Direction arm, int slot = 0)
    {
        if (slot < 0) throw new GolemDomainException("Crossing.FarEnd: a place of the queue is 0 or more");
        var zone = Arm(arm);
        double length = Math.Max(zone.Width, zone.Height);
        return OnLane(arm, inbound: false).Along(Inward(arm), length - StopBack - slot * SlotPitch);
    }

    /// <summary>THE TURN POINT of a way through (lab 23-oct-2026: orange, turning right, cut the inside corner 2 cm from the wall and touched it every
    /// time — the planner's pivots stand 0.5 m from the corner but the run between them passes 0.35 m from it): a car crosses the junction ALONG
    /// ITS LANES, square — straight on its inbound lane to where it meets the outbound lane of the arm it leaves by, the turn there, straight to
    /// the exit point — so it keeps a lane's width from every wall. Straight on, no turn point.</summary>
    internal Position TurnPoint(Direction from, Direction exit)
    {
        if (exit == Opposite(from)) throw new GolemDomainException("straight on: no turn point");
        if (exit == from) throw new GolemDomainException("a car does not turn back at the crossing");
        var inbound = OnLane(from, inbound: true);     // a point of the inbound lane's line: the lane runs along the arm's axis through it
        var outbound = OnLane(exit, inbound: false);
        bool fromRunsNorthSouth = from is Direction.North or Direction.South;
        return fromRunsNorthSouth ? new Position(inbound.X, outbound.Y) : new Position(outbound.X, inbound.Y);
    }

    /// <summary>How many cars fit queued on an arm's lane: from the stop line to the far end, a pitch each.</summary>
    internal int SlotsOf(Direction arm) => (int)Math.Floor((Math.Max(Arm(arm).Width, Arm(arm).Height) - 2 * StopBack) / SlotPitch) + 1;

    // the point of the crossing's edge where a lane meets it: the midpoint shifted to the right of the car that travels the lane — coming
    // in, it travels toward the crossing (inward reversed); going out, away from it (inward)
    private Position OnLane(Direction arm, bool inbound)
    {
        double offset = LaneOffset(arm);
        if (offset == 0.0) return Edge(arm).Midpoint;
        double travel = inbound ? Inward(arm) + Math.PI : Inward(arm);
        return Edge(arm).Midpoint.Along(travel - Math.PI / 2, offset);   // the right of the travel: a quarter turn clockwise
    }

    /// <summary>An ARM is an aisle: a zone no wider than the crossing on its narrow side.</summary>
    private static bool IsAisle(Zone arm, Zone crossing) => Math.Min(arm.Width, arm.Height) <= Math.Min(crossing.Width, crossing.Height) + 1e-6;
    private Segment Edge(Direction arm) => Arm(arm).SharedEdgeWith(Zone);
    private double Inward(Direction arm) => Edge(arm).Midpoint.HeadingTo(Arm(arm).Center);
    private static string Fmt(double v) => v.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
