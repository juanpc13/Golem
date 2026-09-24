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
    // Within DoorGap of a door's point there is no wall: the passage itself. The world cuts a gap DoorWidth wide but the
    // walls' thickness eats into it (~1.25 m clear), so the JAMBS stand about 0.62 m from the door's point — a touch there
    // is a wall (16-sep-2026 lab: two touches on the jambs of living/south, 0.6 m from the point, were taken for things
    // and marked while the gap was DoorWidth / 2 + 0.1). Half the width minus the touch's estimation error.
    internal const double DoorGap = DoorWidth / 2 - 0.15;
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
    internal Zone Of(Area area)
    {
        if (area == null) throw new GolemDomainException("an area is needed, not nothing");
        return area as Zone ?? throw new GolemDomainException($"area '{area.Name}' is not of this map");
    }

    internal IEnumerable<Zone> Zones => areas.Cast<Zone>().Where(z => z.IsLaidOut);
    internal int ZoneCount => Zones.Count();

    /// <summary>Whether an area of this map has been told where it stands.</summary>
    internal bool IsLaidOut(Area area)
    {
        if (area == null) throw new GolemDomainException("MapLayout.IsLaidOut: 'area' was not given");
        return area != null && Of(area).IsLaidOut;
    }

    /// <summary>Whether two areas actually share an edge on the plane. Two areas may CONNECT (a passage joins them,
    /// information) without TOUCHING here (not aligned, not laid out yet): the map answers the first, the layout the second.</summary>
    internal bool Touches(Area a, Area b)
    {
        if (a == null) throw new GolemDomainException("MapLayout.Touches: 'a' was not given");
        if (b == null) throw new GolemDomainException("MapLayout.Touches: 'b' was not given");
        if (ReferenceEquals(a, b)) throw new GolemDomainException("MapLayout.Touches: 'a' and 'b' are the same area");
        return IsLaidOut(a) && IsLaidOut(b) && Of(a).Touches(Of(b));
    }

    /// <summary>The zone with this name, laid out. Refuses an unknown or an unplaced area.</summary>
    internal Zone ZoneNamed(string name)
    {
        var zone = Find(name);
        if (!zone.IsLaidOut) throw new GolemDomainException($"area '{name}' is not laid out");
        return zone;
    }

    // ---- the doors, placed ----

    /// <summary>Where the door between two areas stands: a point on the wall they share. Chainable.</summary>
    internal MapLayout DoorAt(Area a, Area b, Position at)
    {
        if (a == null) throw new GolemDomainException("MapLayout.DoorAt: 'a' was not given");
        if (b == null) throw new GolemDomainException("MapLayout.DoorAt: 'b' was not given");
        if (at == null) throw new GolemDomainException("MapLayout.DoorAt: 'at' was not given");
        if (ReferenceEquals(a, b)) throw new GolemDomainException("MapLayout.DoorAt: 'a' and 'b' are the same area");
        return DoorAt(a?.Name, b?.Name, at);
    }

    // Where the door between two areas, named, stands — declares the door if the map did not dispose it yet. The names are
    // the layout's own business: the journal hands in the areas as objects (21-sep-2026).
    private MapLayout DoorAt(string a, string b, Position at)
    {
        if (at == null) throw new GolemDomainException($"the door {a}/{b} needs a point on the shared wall");
        var door = Door(a, b);
        if (doorPoints.TryGetValue(door.Name, out var placed) && placed.DistanceTo(at) > 1e-9)
            throw new GolemDomainException($"the door {door.Name} already stands at ({Fmt(placed.X)}, {Fmt(placed.Y)})");
        doorPoints[door.Name] = at;
        return this;
    }

    /// <summary>The doors that stand somewhere: each with its point and its jambs.</summary>
    internal IEnumerable<PlacedDoor> PlacedDoors => Doors.Where(d => doorPoints.ContainsKey(d.Name)).Select(d => new PlacedDoor(d, doorPoints[d.Name], this));

    internal bool IsPlaced(Door door)
    {
        if (door == null) throw new GolemDomainException("MapLayout.IsPlaced: 'door' was not given");
        return door != null && doorPoints.ContainsKey(door.Name);
    }

    /// <summary>Where a door stands. Consult IsPlaced first.</summary>
    internal Position PointOf(Door door)
    {
        if (door == null) throw new GolemDomainException("MapLayout.PointOf: 'door' was not given");
        return door != null && doorPoints.TryGetValue(door.Name, out var at) ? at : throw new GolemDomainException($"the door {door?.Name} stands nowhere yet");
    }

    /// <summary>A door, realized: its point, its jambs, its width.</summary>
    internal PlacedDoor Placed(Door door)
    {
        if (door == null) throw new GolemDomainException("MapLayout.Placed: 'door' was not given");
        return new(door, PointOf(door), this);
    }

    // ---- where things stand ----

    internal bool IsOnMap(Position at)
    {
        if (at == null) throw new GolemDomainException("MapLayout.IsOnMap: 'at' was not given");
        return Zones.Any(z => z.Contains(at));
    }

    /// <summary>The zone a point stands in, or null when the map holds nothing there (a solid block, off the floor):
    /// what a table needs to name the zone without refusing.</summary>
    internal Zone ZoneOf(Position at)
    {
        if (at == null) throw new GolemDomainException("MapLayout.ZoneOf: 'at' was not given");
        foreach (var z in Zones) if (z.Contains(at)) return z;
        return null;
    }

    /// <summary>The NAME of the zone a point stands in, or "" when the map holds nothing there: what a table prints beside an
    /// obstacle's centre (ajuste 54, 24-sep-2026: the zone is the map's answer, not the obstacle's — and a query cannot ask a
    /// null zone its name).</summary>
    internal string ZoneNameOf(Position at)
    {
        if (at == null) throw new GolemDomainException("MapLayout.ZoneNameOf: 'at' was not given");
        return ZoneOf(at)?.Name ?? "";
    }

    /// <summary>The zone a point stands in. A point on a shared wall belongs to the first laid out.</summary>
    internal Zone ZoneAt(Position at)
    {
        if (at == null) throw new GolemDomainException("MapLayout.ZoneAt: 'at' was not given");
        return ZoneOf(at) ?? throw new GolemDomainException($"the point ({Fmt(at.X)}, {Fmt(at.Y)}) is nowhere on the map");
    }

    /// <summary>Every zone a point stands in (two, on a shared wall).</summary>
    internal IReadOnlyList<Zone> ZonesOf(Position at)
    {
        if (at == null) throw new GolemDomainException("MapLayout.ZonesOf: 'at' was not given");
        var zones = Zones.Where(z => z.Contains(at)).ToList();
        if (zones.Count == 0) throw new GolemDomainException($"the point ({Fmt(at.X)}, {Fmt(at.Y)}) is nowhere on the map");
        return zones;
    }

    /// <summary>Whether the WALLS leave room for a body of this radius at a point: inside a zone and off every wall by
    /// its radius plus a margin. What was learned by touching is the collisions module's answer, not this one's.</summary>
    internal bool HasRoom(Position at, double radius)
    {
        if (at == null) throw new GolemDomainException("MapLayout.HasRoom: 'at' was not given");
        return Zones.Any(z => z.ContainsInset(at, radius + BodyMargin));
    }

    /// <summary>Whether a point lies on a wall the map knows: within tolerance of a wall of some zone and not in one
    /// of that wall's doorways. The corner of a solid block is known through the perpendicular walls that meet at it.</summary>
    internal bool IsWallAt(Position at, double tolerance)
    {
        if (at == null) throw new GolemDomainException("MapLayout.IsWallAt: 'at' was not given");
        return Zones.Any(z => z.Walls().Any(w => w.Holds(at, tolerance)));
    }

    // ---- the passages, in positions ----

    /// <summary>Whether both areas of an opening are laid out and actually share an edge.</summary>
    internal bool Touches(Opening opening)
    {
        if (opening == null) throw new GolemDomainException("MapLayout.Touches: 'opening' was not given");
        return opening != null && Knows(opening.A) && Knows(opening.B) && Touches(opening.AreaA, opening.AreaB);
    }

    /// <summary>The edge an opening frees. Consult Touches first.</summary>
    internal Segment EdgeOf(Opening opening)
    {
        if (opening == null) throw new GolemDomainException("MapLayout.EdgeOf: 'opening' was not given");
        return Of(opening.AreaA).SharedEdgeWith(Of(opening.AreaB));
    }

    /// <summary>Where the straight run from one point to another CROSSES the openings on its way — one point per boundary
    /// it passes — for a body of this radius; null when a wall cuts it. The run leaves each zone through an open boundary,
    /// never through a wall nor through a corner where two walls meet, and crosses that boundary OpeningMargin away from the
    /// corners of the blocks (a narrow boundary: with room for the body). Empty when both points share a zone. This is the
    /// calculation the planner asks (Juan, 21-sep-2026: "si tiene paso libre, que llegue directo"): a run through the north
    /// hall, the centre and the south hall is one straight run, not three.</summary>
    internal IReadOnlyList<Position> Crossings(Position from, Position to, double radius)
    {
        if (from == null) throw new GolemDomainException("MapLayout.Crossings: 'from' was not given");
        if (to == null) throw new GolemDomainException("MapLayout.Crossings: 'to' was not given");
        if (radius < 0) throw new GolemDomainException("MapLayout.Crossings: a body's radius cannot be negative");
        if (from.DistanceTo(to) < 1e-9) return Array.Empty<Position>();
        foreach (var start in ZonesOf(from))
        {
            var crossings = Walk(start, from, to, radius);
            if (crossings != null) return crossings;
        }
        return null;
    }

    // The run from p to `to`, zone by zone: where it leaves the zone it is in, whether an open boundary is there (away from the
    // corners), and on into the next zone — until the zone holds the end. Null: a wall, a corner, or the run leaves at once
    // through the side p stands on (then p's other zone is the one to walk from).
    private List<Position> Walk(Zone zone, Position p, Position to, double radius)
    {
        var crossings = new List<Position>();
        for (int hops = 0; hops <= ZoneCount; hops++)
        {
            if (zone.Contains(to)) return crossings;
            double dx = to.X - p.X, dy = to.Y - p.Y;
            var r = zone.Rect;
            double tx = dx > 1e-12 ? (r.X + r.Width - p.X) / dx : dx < -1e-12 ? (r.X - p.X) / dx : double.PositiveInfinity;
            double ty = dy > 1e-12 ? (r.Y + r.Height - p.Y) / dy : dy < -1e-12 ? (r.Y - p.Y) / dy : double.PositiveInfinity;
            double t = Math.Min(tx, ty);
            if (t <= 1e-9) return null;                        // leaves at once through the side it stands on
            if (Math.Abs(tx - ty) < 1e-9) return null;         // through a corner: two walls meet there
            if (t >= 1 - 1e-9) return crossings;               // the end lies on this zone's boundary
            var exit = new Position(p.X + t * dx, p.Y + t * dy);
            bool vertical = tx < ty;
            Opening through = null;
            foreach (var o in OpeningsOf(zone))
            {
                if (!Touches(o)) continue;
                var edge = EdgeOf(o);
                if (edge.IsVertical != vertical) continue;
                if (vertical ? Math.Abs(edge.From.X - exit.X) > 1e-6 : Math.Abs(edge.From.Y - exit.Y) > 1e-6) continue;
                double along = vertical ? exit.Y : exit.X;
                double lo = vertical ? Math.Min(edge.From.Y, edge.To.Y) : Math.Min(edge.From.X, edge.To.X);
                double hi = vertical ? Math.Max(edge.From.Y, edge.To.Y) : Math.Max(edge.From.X, edge.To.X);
                double clearance = Clearance(edge, radius);
                if (along < lo + clearance - 1e-9 || along > hi - clearance + 1e-9) continue;   // by the block's corner: a wall
                through = o;
                break;
            }
            if (through == null) return null;
            crossings.Add(exit);
            zone = Of(through.OtherSide(zone));
            p = exit;
        }
        return null;
    }

    /// <summary>Where a body may TURN on an open boundary when its way is not straight: the two ends of the boundary, inset by
    /// the clearance from the blocks' corners, and its middle — the middle alone when the boundary is narrow. The planner's
    /// nodes on an opening (21-sep-2026; the middle was the only one before, so every crossing bent through it).</summary>
    internal IReadOnlyList<Position> Pivots(Opening opening, double radius)
    {
        if (opening == null) throw new GolemDomainException("MapLayout.Pivots: 'opening' was not given");
        if (radius < 0) throw new GolemDomainException("MapLayout.Pivots: a body's radius cannot be negative");
        var edge = EdgeOf(opening);
        if (edge.Length <= 2 * OpeningMargin + 1e-9) return new[] { edge.Midpoint };   // narrow: nowhere to turn but the middle
        double clearance = Clearance(edge, radius);
        if (edge.IsVertical)
        {
            double x = edge.From.X, lo = Math.Min(edge.From.Y, edge.To.Y), hi = Math.Max(edge.From.Y, edge.To.Y);
            return new[] { new Position(x, lo + clearance), edge.Midpoint, new Position(x, hi - clearance) };
        }
        double y = edge.From.Y, x0 = Math.Min(edge.From.X, edge.To.X), x1 = Math.Max(edge.From.X, edge.To.X);
        return new[] { new Position(x0 + clearance, y), edge.Midpoint, new Position(x1 - clearance, y) };
    }

    // How far from the corners of an open boundary a body keeps: OpeningMargin, where the walls of the blocks stand; on a
    // boundary too narrow for that, whatever leaves room for the body itself.
    private static double Clearance(Segment edge, double radius) =>
        edge.Length > 2 * OpeningMargin ? OpeningMargin : Math.Max(0.0, edge.Length / 2 - radius);

    /// <summary>A unit step through a door into one of its two areas, from the other.</summary>
    internal Position StepInto(Door door, Area side)
    {
        if (side == null) throw new GolemDomainException("MapLayout.StepInto: 'side' was not given");
        if (door == null || !door.Joins(side)) throw new GolemDomainException($"the door {door?.Name} does not open into '{side?.Name}'");
        return Of(door.OtherSide(side)).StepInto(Of(side));
    }

    // ---- the road, as the body walks it ----

    /// <summary>
    /// Every door on a road is crossed straight: its leg gains an approach point in front of the door
    /// (DoorClearance into the area the body comes from) and an exit point behind it (into the area it
    /// goes to). The side it is crossed TO is the door's area the road does not come from — the previous
    /// point's side, or where the road starts for its first leg (17-sep-2026 lab: with the next point in a
    /// third zone beyond an opening neither side held it, the door got no crossing, and the body turned in
    /// the doorway and grazed the jamb); when the previous point tells nothing, the next one is consulted.
    /// Derived from the map alone, so a road decided act by act gets the same crossings.
    /// </summary>
    internal Trajectory WithDoorCrossings(Position from, Trajectory road)
    {
        if (from == null) throw new GolemDomainException("MapLayout.WithDoorCrossings: 'from' was not given");
        if (road == null) throw new GolemDomainException("MapLayout.WithDoorCrossings: 'road' was not given");
        var legs = road.Legs();
        var result = new List<Leg>();
        for (int i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            var door = PlacedDoors.FirstOrDefault(d => d.Name == leg.Name && d.At.DistanceTo(leg.At) < 1e-6);
            Area toSide = door == null ? null : SideAwayFrom(door.Door, i == 0 ? from : legs[i - 1].Exit);
            if (door != null && toSide == null && i + 1 < legs.Count) toSide = SideOf(door.Door, legs[i + 1]);
            if (toSide == null || !Touches(door.Door.OtherSide(toSide), toSide)) { result.Add(leg); continue; }
            var step = StepInto(door.Door, toSide);
            result.Add(new Leg(leg.At, leg.Name,
                new Position(leg.At.X - step.X * DoorClearance, leg.At.Y - step.Y * DoorClearance),
                new Position(leg.At.X + step.X * DoorClearance, leg.At.Y + step.Y * DoorClearance)));
        }
        return new Trajectory(result);
    }

    // The side of a door the road continues on: the door's area that holds the next leg's point. When both hold
    // it (the next point sits on a wall they share, e.g. another door) OR NEITHER DOES (the next point sits on the
    // area's boundary — an opening's crossing point, 17-sep-2026 lab: the body left the kitchen's door without a crossing,
    // turned in the doorway and grazed the jamb), the next leg's own passage decides: the door's side it joins.
    // The side of a door the road crosses TO, told by where it comes from: the door's other area. Null when the point
    // it comes from lies in both areas or in neither (on their shared wall, or somewhere else).
    private Area SideAwayFrom(Door door, Position previous)
    {
        Area a = door.AreaA, b = door.AreaB;
        bool inA = Of(a).Contains(previous), inB = Of(b).Contains(previous);
        if (inA && !inB) return b;
        if (inB && !inA) return a;
        return null;
    }

    private Area SideOf(Door door, Leg next)
    {
        Area a = door.AreaA, b = door.AreaB;
        bool inA = Of(a).Contains(next.At), inB = Of(b).Contains(next.At);
        if (inA && !inB) return a;
        if (inB && !inA) return b;
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
    /// <summary>The release that builds this map in a journal, in TWO MOVEMENTS (Juan, 21-sep-2026: "primero crear las
    /// variables de las áreas con el size y la posición, luego para el DoorAt pasar el objeto del área"): every area is born
    /// as a variable — its name, where it stands, how big it is — and then, all of them existing, the doors are placed and
    /// the boundaries opened BETWEEN OBJECTS. The areas live in a block of their own, so only <c>map</c> becomes a global of
    /// the actor; a name with a hyphen becomes a camelCase identifier (<c>north-aisle</c> → <c>northAisle</c>).</summary>
    internal string AsRelease()
    {
        var text = new StringBuilder();
        text.Append("upgrade('").Append(Name).Append("_v1') {\n");
        text.Append("    map = MapLayout('").Append(Name).Append("');\n");
        text.Append("    {\n");
        foreach (var z in areas.Cast<Zone>())
        {
            text.Append("        ").Append(Identifier(z.Name)).Append(" = map.Area('").Append(z.Name).Append("')");
            if (z.IsLaidOut)
                text.Append(".At(Position(").Append(Lit(z.X)).Append(", ").Append(Lit(z.Y)).Append(")).Size(").Append(Lit(z.Width)).Append(", ").Append(Lit(z.Height)).Append(')');
            text.Append(";\n");
        }
        foreach (var z in areas.Cast<Zone>())
            foreach (var p in passages.Where(q => q.A == z.Name))
            {
                text.Append("        ").Append(Identifier(z.Name));
                if (p is Door d && IsPlaced(d))
                {
                    var at = PointOf(d);
                    text.Append(".DoorAt(").Append(Identifier(d.B)).Append(", Position(").Append(Lit(at.X)).Append(", ").Append(Lit(at.Y)).Append("));\n");
                }
                else if (p is Door) text.Append(".DoorTo(").Append(Identifier(p.B)).Append(");\n");
                else text.Append(".OpenTo(").Append(Identifier(p.B)).Append(");\n");
            }
        text.Append("    }\n");
        text.Append("}\n");
        return text.ToString();
    }

    // The variable an area is held in while the release builds the map: its name, a hyphen turning the next letter upper.
    private static string Identifier(string name)
    {
        var parts = name.Split('-');
        return parts[0] + string.Concat(parts.Skip(1).Select(p => p.Length == 0 ? "" : char.ToUpperInvariant(p[0]) + p[1..]));
    }

    // A double literal for the DSL: always with a decimal point.
    private static string Lit(double d) => d == Math.Floor(d) ? d.ToString("0.0", CultureInfo.InvariantCulture) : d.ToString("0.###", CultureInfo.InvariantCulture);

    internal static string Fmt(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
}
