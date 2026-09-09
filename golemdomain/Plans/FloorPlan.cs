using System.Globalization;
using System.Text;
using GolemHost.Domain.Geometry;
using GolemHost.Domain.Routes;

namespace GolemHost.Domain.Plans;

/// <summary>
/// A floor plan — Juan's MAPA / PLANO: one plane (no levels yet, no layers yet) holding the places the golem
/// knows, the passages between them (doors and open boundaries) and the marks where bodies touched what the
/// plan does not hold. It answers where a point stands, whether a body fits there, whether a touched point
/// is a wall it knows — and lends its geometry to the <see cref="RoutePlanner"/>, which finds the roads.
/// Rooms are rectangles, so a straight line inside a room never crosses a wall. The plan also writes itself
/// as the release the journal charts it with (<see cref="AsRelease"/>): the journal stays the only truth.
/// <para>Origins: a map of named places joined by passages is a topological map (Kuipers &amp; Byun, Robotics and
/// Autonomous Systems 1991), not a metric grid; the metric lives inside each place (a rectangle) and in the
/// door points. Robotics keeps both as layers of one map (metric under topological); here the second layer is
/// future work, declared in the PLAN, not modelled by halves.</para>
/// </summary>
internal sealed class FloorPlan
{
    // The plane: walls as tall as the arena's, as thin as a line (the real 0.15 is absorbed by WallTolerance).
    internal const double Height = 0.5;
    internal const double WallThickness = 0;
    // A door: the gap the world leaves in a wall, centered on the door point. Within DoorGap of the point there
    // is no wall (half the width plus a tenth for the pose's error).
    internal const double DoorWidth = 1.4;
    internal const double DoorGap = DoorWidth / 2 + 0.1;
    // The body's size, as the map accounts for it: how far from a door the body lines up and is clear of the
    // wall, and how far from the corners of an open boundary it crosses.
    internal const double DoorClearance = 0.6;
    internal const double OpeningMargin = 0.5;
    // How far from a wall's line a touched point may fall and still be that wall (its thickness, the pose's error).
    internal const double WallTolerance = 0.3;
    // A mark is a point where a body touched something: whatever stands there is taken to reach at least
    // this far around the point; a body passes it at its own radius plus a margin.
    internal const double MarkReach = 0.25;
    internal const double MarkMargin = 0.1;
    // Two marks this close were made on the same thing: joined, they become vertices of one obstacle.
    internal const double JoinWithin = 1.0;

    private readonly List<Place> places = new();
    private readonly List<Passage> passages = new();
    private readonly List<Mark> marks = new();        // facts: where bodies touched what the plan does not hold
    private readonly List<Peer> encounters = new();   // facts: where a body met another body (history, never planned around)

    // ---- charting ----

    internal Place AddPlace(string name, double x, double y, double width, double height)
    {
        if (places.Any(p => p.Name == name)) throw new DomainException($"place '{name}' already exists");
        var place = new Place(name, x, y, width, height, this);
        places.Add(place);
        return place;
    }

    internal void AddDoor(string a, string b, Position at)
    {
        if (passages.OfType<Door>().Any(d => d.Joins(a, b) && d.At.DistanceTo(at) < 1e-9)) return;   // declared from the other side already
        passages.Add(new Door(this, a, b, at));
    }

    internal void AddOpening(string a, string b)
    {
        if (passages.OfType<OpenBoundary>().Any(o => o.Joins(a, b))) return;
        passages.Add(new OpenBoundary(this, a, b));
    }

    /// <summary>A point where a body touched something the plan does not hold, with the heading of the touch (the
    /// mark's normal). Two touches within a tenth of a unit are one mark. Returns how many marks the plan holds.</summary>
    internal int AddMark(Pose touch)
    {
        if (!marks.Any(m => m.DistanceTo(touch) < 0.1)) marks.Add(new Mark(touch.X, touch.Y, touch.Heading));
        return marks.Count;
    }

    /// <summary>A body met another body at a point: history, kept as a Peer among the obstacles, never planned around.
    /// Returns how many encounters the plan holds.</summary>
    internal int AddEncounter(string who, Position at)
    {
        encounters.Add(new Peer(who, at));
        return encounters.Count;
    }

    // ---- the plan, read as objects ----

    internal int PlaceCount => places.Count;
    internal int PassageCount => passages.Count;
    internal int MarkCount => marks.Count;
    internal int EncounterCount => encounters.Count;
    internal bool Knows(string place) => places.Any(p => p.Name == place);

    internal IReadOnlyList<Place> Places => places;
    internal IEnumerable<Door> Doors => passages.OfType<Door>();
    internal IEnumerable<OpenBoundary> Openings => passages.OfType<OpenBoundary>();
    internal IReadOnlyList<Mark> Marks => marks;

    internal IEnumerable<Door> DoorsJoining(string place) => Doors.Where(d => d.Joins(place));
    internal IEnumerable<OpenBoundary> OpeningsJoining(string place) => Openings.Where(o => o.Joins(place));

    /// <summary>The doors of a place as that place sees them: the place across, and where the door stands.</summary>
    internal IReadOnlyList<Doorway> DoorsOf(Place place) =>
        DoorsJoining(place.Name).Select(d => new Doorway(d.OtherSide(place.Name), d.At, d.Width)).ToList();

    /// <summary>The open boundaries of a place: the place across each one.</summary>
    internal IReadOnlyList<Opening> OpeningsOf(Place place) =>
        OpeningsJoining(place.Name).Select(o => new Opening(o.OtherSide(place.Name))).ToList();

    /// <summary>The marks standing in a place (a mark on a shared wall stands in both).</summary>
    internal IReadOnlyList<Mark> MarksIn(Place place) => marks.Where(place.Contains).ToList();

    /// <summary>The obstacles the golem hypothesizes: the things the marks outline — marks within JoinWithin of one
    /// another (directly or through others) are vertices of one thing, ordered around its center so they can be
    /// joined into a figure — followed by the peers it met. Derived every time from the facts; never stored.</summary>
    internal IReadOnlyList<Obstacle> Obstacles()
    {
        int n = marks.Count;
        var parent = Enumerable.Range(0, n).ToArray();
        int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (marks[i].DistanceTo(marks[j]) <= JoinWithin) parent[Root(i)] = Root(j);

        var obstacles = new List<Obstacle>();
        foreach (var group in Enumerable.Range(0, n).GroupBy(Root).OrderBy(g => g.Min()))
        {
            var members = group.Select(i => marks[i]).ToList();
            var center = new Position(members.Average(m => m.X), members.Average(m => m.Y));
            var ordered = members.OrderBy(m => Math.Atan2(m.Y - center.Y, m.X - center.X)).ToList();
            obstacles.Add(new Thing(ordered, center));
        }
        obstacles.AddRange(encounters);
        return obstacles;
    }

    /// <summary>The things alone: the obstacles the roads avoid.</summary>
    internal IReadOnlyList<Thing> Things() => Obstacles().OfType<Thing>().ToList();

    /// <summary>The obstacles whose center stands in a place.</summary>
    internal IReadOnlyList<Obstacle> ObstaclesIn(Place place) => Obstacles().Where(o => place.Contains(o.Center)).ToList();

    internal Place PlaceNamed(string name)
    {
        foreach (var p in places) if (p.Name == name) return p;
        throw new DomainException($"unknown place '{name}'");
    }

    internal bool HasOpeningBetween(string a, string b) => Openings.Any(o => o.Joins(a, b));
    internal OpenBoundary OpeningBetween(string a, string b) =>
        Openings.FirstOrDefault(o => o.Joins(a, b)) ?? throw new DomainException($"no open boundary between '{a}' and '{b}'");

    // ---- where things stand ----

    internal bool IsOnMap(Position at) => places.Any(p => p.Contains(at));

    /// <summary>The place a point stands in. A point on a shared wall belongs to the first declared.</summary>
    internal Place PlaceAt(Position at)
    {
        foreach (var p in places) if (p.Contains(at)) return p;
        throw new DomainException($"the point ({Fmt(at.X)}, {Fmt(at.Y)}) is nowhere on the map");
    }

    /// <summary>The names of every place a point stands in (two, on a shared wall).</summary>
    internal string[] PlacesOf(Position at)
    {
        var names = places.Where(p => p.Contains(at)).Select(p => p.Name).ToArray();
        if (names.Length == 0) throw new DomainException($"the point ({Fmt(at.X)}, {Fmt(at.Y)}) is nowhere on the map");
        return names;
    }

    /// <summary>Whether a body of this radius stands clear at a point: inside a place, off every wall by its
    /// radius plus a margin, and clear of every mark — off it by the mark's reach plus its radius plus a margin,
    /// except on the side the touching body came from, which is free at a radius and a margin (the mark's normal).</summary>
    internal bool Fits(Position at, double radius)
    {
        if (!HasRoom(at, radius)) return false;
        return !marks.Any(m => m.Blocks(at, radius));
    }

    /// <summary>Whether the walls alone leave room for a body of this radius at a point — marks not counted:
    /// the question a body asks while feeling its way around one.</summary>
    internal bool HasRoom(Position at, double radius) => places.Any(p => p.ContainsInset(at, radius + MarkMargin));

    /// <summary>Whether a point lies on a wall the plan knows: within tolerance of a wall of some place and not in
    /// one of that wall's doorways. The corner of a solid block is known through the perpendicular walls that meet at it.</summary>
    internal bool IsWallAt(Position at, double tolerance) => places.Any(p => p.Walls().Any(w => w.Holds(at, tolerance)));

    // ---- the road, as the body walks it ----

    /// <summary>
    /// Every door on a road is crossed straight: its leg gains an approach point in front of the door
    /// (DoorClearance into the place the body comes from) and an exit point behind it (into the place it
    /// goes to). Derived from the plan alone, so a road parsed back from the journal gets the same crossings.
    /// </summary>
    internal Trajectory WithDoorCrossings(Trajectory road)
    {
        var legs = road.Legs();
        var result = new List<Leg>();
        for (int i = 0; i < legs.Count; i++)
        {
            var leg = legs[i];
            var door = Doors.FirstOrDefault(d => d.Name == leg.Name && d.At.DistanceTo(leg.At) < 1e-6);
            string toSide = door == null || i + 1 >= legs.Count ? null : SideOf(door, legs[i + 1]);
            if (toSide == null || !PlaceNamed(door.OtherSide(toSide)).Touches(PlaceNamed(toSide))) { result.Add(leg); continue; }
            var step = door.StepInto(toSide);
            result.Add(new Leg(leg.At, leg.Name,
                new Position(leg.At.X - step.X * DoorClearance, leg.At.Y - step.Y * DoorClearance),
                new Position(leg.At.X + step.X * DoorClearance, leg.At.Y + step.Y * DoorClearance)));
        }
        return new Trajectory(result);
    }

    // The side of a door the road continues on: the door's place that holds the next leg's point. When
    // both hold it (the next point sits on a wall they share, e.g. another door), the next leg's own
    // passage decides; when neither does, the crossing cannot be told.
    private string SideOf(Door door, Leg next)
    {
        bool inA = PlaceNamed(door.A).Contains(next.At), inB = PlaceNamed(door.B).Contains(next.At);
        if (inA && !inB) return door.A;
        if (inB && !inA) return door.B;
        if (!inA) return null;
        var onward = passages.FirstOrDefault(p => p.Name == next.Name);
        if (onward == null) return null;
        if (onward.Joins(door.A) && !onward.Joins(door.B)) return door.A;
        if (onward.Joins(door.B) && !onward.Joins(door.A)) return door.B;
        return null;
    }

    // ---- the plan as the journal charts it ----

    /// <summary>The release that charts this plan, one fluent chain per place, as the golem's journal writes it:
    /// <c>upgrade('map_v1') { g.Chart('kitchen', 0, 8, 4, 3).DoorTo('north', 4, 9.5); … }</c>. A named plan of
    /// the catalog reaches the journal through this text — the code never becomes the map's truth.</summary>
    internal string AsRelease(string upgrade)
    {
        if (string.IsNullOrWhiteSpace(upgrade)) throw new DomainException("a release needs a name");
        var text = new StringBuilder();
        text.Append("upgrade('").Append(upgrade).Append("') {\n");
        foreach (var p in places)
        {
            text.Append("    g.Chart('").Append(p.Name).Append("', ")
                .Append(Fmt(p.X)).Append(", ").Append(Fmt(p.Y)).Append(", ")
                .Append(Fmt(p.Width)).Append(", ").Append(Fmt(p.Height)).Append(')');
            foreach (var passage in passages.Where(q => q.A == p.Name))
            {
                if (passage is Door door)
                    text.Append(".DoorTo('").Append(door.B).Append("', ").Append(Fmt(door.At.X)).Append(", ").Append(Fmt(door.At.Y)).Append(')');
                else
                    text.Append(".OpenTo('").Append(passage.B).Append("')");
            }
            text.Append(";\n");
        }
        text.Append("}\n");
        return text.ToString();
    }

    private static string Fmt(double d) => d.ToString("0.###", CultureInfo.InvariantCulture);
}
