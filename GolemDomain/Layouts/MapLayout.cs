using System.Globalization;
using System.Text;
using GolemDomain.Geometry;
using GolemDomain.Maps;
using GolemDomain.Routes;

namespace GolemDomain.Layouts;

/// <summary>
/// The map laid out — Juan's MAPA with its DISTRIBUCIÓN: the CONCRETE map, the one class that builds everything.
/// It IS a <see cref="Map"/> (the same ways of creating areas and passages, everything the maquette disposes) and
/// extends them with dimensions and positions: its areas are <see cref="Zone"/>s — areas that also occupy a
/// rectangle — and every door has a point on the shared wall. From those it derives the walls as segments, the
/// corners, the shared edges, and answers what the plane can answer — where a point stands, whether two areas
/// actually touch (they may connect without being aligned), whether the walls leave room for a body, whether a
/// touched point is a wall it knows, how a door is crossed straight. The road planner consults it; the
/// collisions module measures against it.
/// <para>Built by creating each object once and telling it what it is, in one train:
/// <c>map = MapLayout('warehouse'); map.Area('kitchen').At(Position(0.0, 8.0)).Size(4.0, 3.0).DoorAt('north', Position(4.0, 9.5)).DoorAt('west', Position(0.75, 8.0));</c>
/// and later <c>map.Find('kitchen')</c> to read it or keep telling it. Every other method takes the objects:
/// <c>Touches(kitchen, north)</c>, <c>PointOf(door)</c>, <c>StepInto(door, north)</c>.</para>
/// </summary>
internal sealed class MapLayout : Map
{
    // The body's size, as the layout accounts for it: how far from a door the body lines up and is clear of the
    // wall, how far from the corners of an opening it crosses, the margin it keeps from a wall beyond its radius.
    internal const double DoorClearance = 0.6;
    internal const double OpeningMargin = 0.5;
    internal const double BodyMargin = 0.1;
    // Within DoorGap of a door's point there is no wall (half the width plus a tenth for the pose's error).
    internal const double DoorGap = DoorWidth / 2 + 0.1;
    // How far from a wall's line a touched point may fall and still be that wall (its thickness, the pose's error).
    internal const double WallTolerance = 0.3;

    private readonly Dictionary<string, Position> doorPoints = new();   // by the door's name

    /// <summary>A map with nothing on it yet, to be told its zones one by one.</summary>
    internal MapLayout(string name) : base(name) { }

    // ---- the areas of a laid-out map are zones ----

    protected override Area NewArea(string name) => new Zone(name, this);

    /// <summary>A new zone, by name, ready to be told where it stands: <c>map.Area('bay').At(…).Size(…)</c>.</summary>
    internal override Zone Area(string name) => (Zone)base.Area(name);

    /// <summary>The zone with this name, to tell it where it stands or to read it.</summary>
    internal override Zone Find(string name) => (Zone)base.Find(name);

    /// <summary>An area of this map, as the zone it is (every area of a laid-out map is a zone).</summary>
    internal Zone Of(Area area) => area as Zone ?? throw new DomainException(area == null ? "an area is needed, not nothing" : $"area '{area.Name}' is not of this map");

    internal IEnumerable<Zone> Zones => areas.Cast<Zone>().Where(z => z.IsLaidOut);
    internal int ZoneCount => Zones.Count();

    /// <summary>Whether an area of this map has been told where it stands.</summary>
    internal bool IsLaidOut(Area area) => area != null && Of(area).IsLaidOut;

    /// <summary>Whether two areas actually share an edge on the plane. Two areas may CONNECT (a passage joins them,
    /// information) without TOUCHING here (not aligned, not laid out yet): the map answers the first, the layout the second.</summary>
    internal bool Touches(Area a, Area b) => IsLaidOut(a) && IsLaidOut(b) && Of(a).Touches(Of(b));

    /// <summary>The zone with this name, laid out. Refuses an unknown or an unplaced area.</summary>
    internal Zone ZoneNamed(string name)
    {
        var zone = Find(name);
        if (!zone.IsLaidOut) throw new DomainException($"area '{name}' is not laid out");
        return zone;
    }

    // ---- the doors, placed ----

    /// <summary>Where the door between two areas stands: a point on the wall they share. Chainable.</summary>
    internal MapLayout DoorAt(Area a, Area b, Position at) => DoorAt(a?.Name, b?.Name, at);

    /// <summary>Where the door between two areas, named (the second may not be created yet), stands. Declares the door
    /// if the map did not dispose it yet. Chainable.</summary>
    internal MapLayout DoorAt(string a, string b, Position at)
    {
        if (at == null) throw new DomainException($"the door {a}/{b} needs a point on the shared wall");
        var door = Door(a, b);
        if (doorPoints.TryGetValue(door.Name, out var placed) && placed.DistanceTo(at) > 1e-9)
            throw new DomainException($"the door {door.Name} already stands at ({Fmt(placed.X)}, {Fmt(placed.Y)})");
        doorPoints[door.Name] = at;
        return this;
    }

    /// <summary>The doors that stand somewhere: each with its point and its jambs.</summary>
    internal IEnumerable<PlacedDoor> PlacedDoors => Doors.Where(d => doorPoints.ContainsKey(d.Name)).Select(d => new PlacedDoor(d, doorPoints[d.Name], this));

    internal bool IsPlaced(Door door) => door != null && doorPoints.ContainsKey(door.Name);

    /// <summary>Where a door stands. Consult IsPlaced first.</summary>
    internal Position PointOf(Door door) =>
        door != null && doorPoints.TryGetValue(door.Name, out var at) ? at : throw new DomainException($"the door {door?.Name} stands nowhere yet");

    /// <summary>A door, realized: its point, its jambs, its width.</summary>
    internal PlacedDoor Placed(Door door) => new(door, PointOf(door), this);

    // ---- where things stand ----

    internal bool IsOnMap(Position at) => Zones.Any(z => z.Contains(at));

    /// <summary>The zone a point stands in, or null when the map holds nothing there (a solid block, off the floor):
    /// what a table needs to name the zone without refusing.</summary>
    internal Zone ZoneOf(Position at)
    {
        foreach (var z in Zones) if (z.Contains(at)) return z;
        return null;
    }

    /// <summary>The zone a point stands in. A point on a shared wall belongs to the first laid out.</summary>
    internal Zone ZoneAt(Position at) =>
        ZoneOf(at) ?? throw new DomainException($"the point ({Fmt(at.X)}, {Fmt(at.Y)}) is nowhere on the map");

    /// <summary>Every zone a point stands in (two, on a shared wall).</summary>
    internal IReadOnlyList<Zone> ZonesOf(Position at)
    {
        var zones = Zones.Where(z => z.Contains(at)).ToList();
        if (zones.Count == 0) throw new DomainException($"the point ({Fmt(at.X)}, {Fmt(at.Y)}) is nowhere on the map");
        return zones;
    }

    /// <summary>Whether the WALLS leave room for a body of this radius at a point: inside a zone and off every wall by
    /// its radius plus a margin. What was learned by touching is the collisions module's answer, not this one's.</summary>
    internal bool HasRoom(Position at, double radius) => Zones.Any(z => z.ContainsInset(at, radius + BodyMargin));

    /// <summary>Whether a point lies on a wall the map knows: within tolerance of a wall of some zone and not in one
    /// of that wall's doorways. The corner of a solid block is known through the perpendicular walls that meet at it.</summary>
    internal bool IsWallAt(Position at, double tolerance) => Zones.Any(z => z.Walls().Any(w => w.Holds(at, tolerance)));

    // ---- the passages, in positions ----

    /// <summary>Whether both areas of an opening are laid out and actually share an edge.</summary>
    internal bool Touches(Opening opening) =>
        opening != null && Knows(opening.A) && Knows(opening.B) && Touches(opening.AreaA, opening.AreaB);

    /// <summary>The edge an opening frees. Consult Touches first.</summary>
    internal Segment EdgeOf(Opening opening) => Of(opening.AreaA).SharedEdgeWith(Of(opening.AreaB));
    internal Position MidpointOf(Opening opening) => EdgeOf(opening).Midpoint;

    /// <summary>Whether the straight run u→v crosses the edge an opening frees.</summary>
    internal bool IsCrossed(Opening opening, Position u, Position v)
    {
        var edge = EdgeOf(opening);
        if (edge.IsVertical)
        {
            double x = edge.From.X, y0 = Math.Min(edge.From.Y, edge.To.Y), y1 = Math.Max(edge.From.Y, edge.To.Y);
            if ((u.X - x) * (v.X - x) > 0) return false;      // both on the same side: no crossing
            if (Math.Abs(v.X - u.X) < 1e-9) return false;
            double t = (x - u.X) / (v.X - u.X);
            double y = u.Y + t * (v.Y - u.Y);
            return y >= y0 - 1e-9 && y <= y1 + 1e-9;
        }
        else
        {
            double y = edge.From.Y, x0 = Math.Min(edge.From.X, edge.To.X), x1 = Math.Max(edge.From.X, edge.To.X);
            if ((u.Y - y) * (v.Y - y) > 0) return false;
            if (Math.Abs(v.Y - u.Y) < 1e-9) return false;
            double t = (y - u.Y) / (v.Y - u.Y);
            double x = u.X + t * (v.X - u.X);
            return x >= x0 - 1e-9 && x <= x1 + 1e-9;
        }
    }

    /// <summary>Where the straight run u→v meets the edge an opening frees — kept OpeningMargin away from the corners,
    /// where the walls of the solid blocks stand; a narrow opening is crossed through its middle.</summary>
    internal Position CrossingPoint(Opening opening, Position u, Position v)
    {
        var edge = EdgeOf(opening);
        if (edge.IsVertical)
        {
            double x = edge.From.X, t = (x - u.X) / (v.X - u.X);
            double y0 = Math.Min(edge.From.Y, edge.To.Y), y1 = Math.Max(edge.From.Y, edge.To.Y);
            return new Position(x, AwayFromCorners(u.Y + t * (v.Y - u.Y), y0, y1));
        }
        else
        {
            double y = edge.From.Y, t = (y - u.Y) / (v.Y - u.Y);
            double x0 = Math.Min(edge.From.X, edge.To.X), x1 = Math.Max(edge.From.X, edge.To.X);
            return new Position(AwayFromCorners(u.X + t * (v.X - u.X), x0, x1), y);
        }
    }

    private static double AwayFromCorners(double along, double from, double to)
    {
        if (to - from <= 2 * OpeningMargin) return (from + to) / 2;   // a narrow opening: straight through the middle
        return Math.Clamp(along, from + OpeningMargin, to - OpeningMargin);
    }

    /// <summary>A unit step through a door into one of its two areas, from the other.</summary>
    internal Position StepInto(Door door, Area side)
    {
        if (door == null || !door.Joins(side)) throw new DomainException($"the door {door?.Name} does not open into '{side?.Name}'");
        return Of(door.OtherSide(side)).StepInto(Of(side));
    }

    // ---- the road, as the body walks it ----

    /// <summary>
    /// Every door on a road is crossed straight: its leg gains an approach point in front of the door
    /// (DoorClearance into the area the body comes from) and an exit point behind it (into the area it
    /// goes to). Derived from the map alone, so a road decided act by act gets the same crossings.
    /// </summary>
    internal Trajectory WithDoorCrossings(Trajectory road)
    {
        var legs = road.Legs();
        var result = new List<Leg>();
        for (int i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            var door = PlacedDoors.FirstOrDefault(d => d.Name == leg.Name && d.At.DistanceTo(leg.At) < 1e-6);
            Area toSide = door == null || i + 1 >= legs.Count ? null : SideOf(door.Door, legs[i + 1]);
            if (toSide == null || !Touches(door.Door.OtherSide(toSide), toSide)) { result.Add(leg); continue; }
            var step = StepInto(door.Door, toSide);
            result.Add(new Leg(leg.At, leg.Name,
                new Position(leg.At.X - step.X * DoorClearance, leg.At.Y - step.Y * DoorClearance),
                new Position(leg.At.X + step.X * DoorClearance, leg.At.Y + step.Y * DoorClearance)));
        }
        return new Trajectory(result);
    }

    // The side of a door the road continues on: the door's area that holds the next leg's point. When both hold
    // it (the next point sits on a wall they share, e.g. another door), the next leg's own passage decides; when
    // neither does, the crossing cannot be told.
    private Area SideOf(Door door, Leg next)
    {
        Area a = door.AreaA, b = door.AreaB;
        bool inA = Of(a).Contains(next.At), inB = Of(b).Contains(next.At);
        if (inA && !inB) return a;
        if (inB && !inA) return b;
        if (!inA) return null;
        if (!KnowsPassage(next.Name)) return null;
        var onward = FindPassage(next.Name);
        if (onward.Joins(a) && !onward.Joins(b)) return a;
        if (onward.Joins(b) && !onward.Joins(a)) return b;
        return null;
    }

    // ---- the map as the journal builds it ----

    /// <summary>The release that builds this map, as the golem's journal writes it — each area created once and told
    /// what it is in one train: where it stands, how big it is, where its doors are, what it opens to:
    /// <c>upgrade('warehouse_v1') { map = MapLayout('warehouse'); map.Area('kitchen').At(Position(0.0, 8.0)).Size(4.0, 3.0).DoorAt('north', Position(4.0, 9.5)).DoorAt('west', Position(0.75, 8.0)); … }</c>.
    /// Constructor literals carry a decimal point: the engine does not coerce an integer literal into a double
    /// parameter (Fase 0, P7). A named map of the catalog reaches the journal through this text — the code never
    /// becomes the map's truth.</summary>
    internal string AsRelease()
    {
        var text = new StringBuilder();
        text.Append("upgrade('").Append(Name).Append("_v1') {\n");
        text.Append("    map = MapLayout('").Append(Name).Append("');\n");
        foreach (var z in areas.Cast<Zone>())
        {
            text.Append("    map.Area('").Append(z.Name).Append("')");
            if (z.IsLaidOut)
                text.Append(".At(Position(").Append(Lit(z.X)).Append(", ").Append(Lit(z.Y)).Append(")).Size(").Append(Lit(z.Width)).Append(", ").Append(Lit(z.Height)).Append(')');
            foreach (var p in passages.Where(q => q.A == z.Name))
            {
                if (p is Door d && IsPlaced(d))
                {
                    var at = PointOf(d);
                    text.Append(".DoorAt('").Append(d.B).Append("', Position(").Append(Lit(at.X)).Append(", ").Append(Lit(at.Y)).Append("))");
                }
                else if (p is Door) text.Append(".DoorTo('").Append(p.B).Append("')");
                else text.Append(".OpenTo('").Append(p.B).Append("')");
            }
            text.Append(";\n");
        }
        text.Append("}\n");
        return text.ToString();
    }

    // A double literal for the DSL: always with a decimal point.
    private static string Lit(double d) => d == Math.Floor(d) ? d.ToString("0.0", CultureInfo.InvariantCulture) : d.ToString("0.###", CultureInfo.InvariantCulture);

    internal static string Fmt(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
}
