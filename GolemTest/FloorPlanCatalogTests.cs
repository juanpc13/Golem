using System.Globalization;
using Choreography.Theater;
using GolemDomain;
using GolemDomain.Layouts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;

namespace GolemTest;

// The catalog of named maps (Catalog): each one is built with the domain's own class and reaches a golem's
// journal as the release it renders — so the journal, not the code, keeps the map. Every test
// charts one plan into a fresh actor through the perform and asks the golem about it.
[TestClass]
public class FloorPlanCatalogTests
{
    private PerformanceV2 perf;

    [TestInitialize]
    public void PinTheCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [TestCleanup]
    public void TheGolemRests() => perf?.Dispose();

    [TestMethod]
    public void TheWarehouse_RendersTheReleaseTheHostCarries_EachAreaFoundOnceAndToldInOneTrain()
    {
        string release = Catalog.Warehouse().AsRelease();

        // one concrete map builds everything: the area, where it stands, how big it is, its doors and what it opens to
        StringAssert.StartsWith(release, "upgrade('warehouse_v1') {\n    map = MapLayout('warehouse');");
        StringAssert.Contains(release, "    map.Area('kitchen').At(Position(0.0, 8.0)).Size(4.0, 3.0).DoorAt('north', Position(4.0, 9.5)).DoorAt('west', Position(0.75, 8.0));\n");
        StringAssert.Contains(release, "    map.Area('north').At(Position(4.0, 8.0)).Size(3.0, 3.0).DoorAt('storage', Position(7.0, 9.5)).OpenTo('center');\n");
        StringAssert.Contains(release, "    map.Area('west').At(Position(0.0, 3.0)).Size(1.5, 5.0).DoorAt('living', Position(0.75, 3.0));\n");
        StringAssert.Contains(release, "    map.Area('garage').At(Position(7.0, 0.0)).Size(4.0, 3.0);\n");
        Assert.IsFalse(release.Contains("g."), "the golem writes nothing here: the map builds itself, and the golem receives it");

        Born(release);
        Assert.AreEqual(9, Int("map.ZoneCount"));
        Assert.AreEqual(10, Int("map.PassageCount"), "eight doors and two openings, as the host's warehouse");
        Assert.AreEqual("center", Text("map.ZoneAt(Position(5.5, 5.5)).Name"));
        // and the map answers on its own, without the golem — as a maquette and as a layout, because it is both
        Assert.AreEqual(9, Int("map.AreaCount"), "what the maquette disposes");
        Assert.AreEqual(2, Int("map.Find('kitchen').Neighbours().Count"), "the kitchen connects to two areas");
        Assert.IsTrue(Bool("map.Connects(map.Find('north'), map.Find('center'))"), "connected by an opening: information");
        Assert.IsTrue(Bool("map.Touches(map.Find('north'), map.Find('center'))"), "and actually sharing an edge on the plane: geometry");
        Assert.IsFalse(Bool("map.Connects(map.Find('kitchen'), map.Find('garage'))"));
        Assert.AreEqual(9, Int("map.ZoneCount"), "every area laid out");
        Assert.AreEqual("north", Text("map.ZoneOf(Position(5.5, 9.5)).Name"));
        Assert.AreEqual(4.0, Double("map.Find('kitchen').Width"), 1e-9, "found once, read as the zone it is");
        Assert.AreEqual(0.25, Double("body.Radius.InMeters"), 1e-9, "and the body: a magnitude, read in its base unit");
    }

    [TestMethod]
    public void CrossCorridors_FourRoomsInTheCorners_TwoAislesThatCross()
    {
        Born(Catalog.Named("cross-corridors").AsRelease());

        Assert.AreEqual(9, Int("map.ZoneCount"), "four rooms, four aisles, the crossing");
        Assert.AreEqual(12, Int("map.PassageCount"), "eight doors, four open boundaries around the crossing");
        Assert.IsFalse(Bool("map.IsOnMap(Position(4.0, 5.5))") && Bool("map.IsOnMap(Position(-1.0, 5.5))"), "the floor is 11 x 11");
        Assert.AreEqual("crossing", Text("map.ZoneAt(Position(5.5, 5.5)).Name"));

        // from the northwest room to the southeast one: out a door into an aisle, through the crossing (two open
        // boundaries), along the other aisle and in through a door — never through a wall. The two ways round
        // (north then east, west then south) are the same length: the planner may take either.
        Visit(1, "southeast", 2.375, 8.625);
        string plan = Text("g.Road(g.Find(1), Position(2.375, 8.625)).AsPlan()");
        StringAssert.StartsWith(plan, "northwest/");
        Assert.AreEqual(2, plan.Split("crossing~").Length - 1, "in through one aisle, out through the other: " + plan);
        StringAssert.EndsWith(plan, "> southeast@8.63,2.38");
        Assert.AreEqual(2, plan.Split('/').Length - 1, "exactly two doors: " + plan);
        // the two aisles a room opens to: a corner room reaches its diagonal opposite only through the crossing
        double diagonal = Double("g.Distance(map.Find('northwest'), map.Find('southeast'))");
        double straight = Math.Sqrt(6.25 * 6.25 * 2);   // ~8.84, through the crossing's corner: not a road
        Assert.IsTrue(diagonal > straight + 1.5 && diagonal < 11.5, "door, aisle, crossing, aisle, door — a bit over ten metres: " + diagonal);
    }

    [TestMethod]
    public void RingCorridor_FourRoomsInTheMiddle_OneCorridorAllTheWayRound()
    {
        Born(Catalog.Named("ring-corridor").AsRelease());

        Assert.AreEqual(8, Int("map.ZoneCount"), "four rooms, four stretches of corridor");
        Assert.AreEqual(16, Int("map.PassageCount"), "eight doors to the corridor, four between the rooms, four open stretches");
        Assert.AreEqual("west-corridor", Text("map.ZoneAt(Position(0.75, 5.5)).Name"));

        // neighbouring rooms are joined directly: center to center through the door they share
        Assert.AreEqual(4.0, Double("g.Distance(map.Find('northwest'), map.Find('northeast'))"), 0.01, "two metres to the door, two more to the neighbour's center");
        // the corridor runs all the way round: from one stretch into the next through an open boundary, no door
        Visit(1, new[] { "9,10.25" }, 0.75, 10.25);                    // the east end of the north corridor
        string plan = Text("g.Road(g.Find(1), Position(0.75, 10.25)).AsPlan()");       // from the northwest corner of the ring
        StringAssert.StartsWith(plan, "west-corridor~north-corridor@1.5,10.2");
        StringAssert.EndsWith(plan, "> north-corridor@9,10.25");
        Assert.IsFalse(plan.Contains('/'), "along the corridor, through no door: " + plan);
        // and cutting through a room beats going round when it is shorter: the planner takes the rooms' doors
        Visit(2, "east-corridor", 0.75, 10.25);
        string across = Text("g.Road(g.Find(2), Position(0.75, 10.25)).AsPlan()");
        StringAssert.Contains(across, "northeast/north-corridor@7.5,9.5 > northeast/east-corridor@9.5,7.5", "through the northeast room: " + across);
    }

    [TestMethod]
    public void TheCatalog_NamesItsPlans_AndRefusesAnUnknownOne()
    {
        CollectionAssert.AreEqual(new[] { "warehouse", "cross-corridors", "ring-corridor" }, Catalog.Names());
        var refused = Assert.ThrowsException<GolemDomainException>(() => Catalog.Named("attic"));
        StringAssert.Contains(refused.Message, "no map named 'attic'");
    }

    // ---- helpers ----

    private void Born(string mapRelease)
    {
        string name = "golem-under-test-" + Guid.NewGuid().ToString("N");
        perf = new PerformanceV2(name, DomainLibrary.Assembly);
        perf.ConfigureStorage(DatabaseType.IN_MEMORY, name);
        perf.Start();
        perf.Actor.Using(
            "upgrade('body_v1') { radius = Meters(0.25); speed = MetersPerSecond(2.0); linger = Seconds(6.0); retreat = Meters(0.6); body = Body(radius, speed, linger, retreat); }\n"
            + mapRelease
            + "upgrade('init') { collisions = Collisions(map); g = Golem(body, map, collisions); }\n")
        .PerformCommand();
    }

    [TestMethod]
    public void AnObjectNotGiven_IsRefusedByTheDomain_BeforeAnythingRuns()
    {
        // Juan, 14-sep-2026: every method that takes an object checks it first, with an if, and refuses in the domain's voice
        var map = Catalog.Warehouse();
        var kitchen = map.Find("kitchen");
        Assert.AreEqual("Map.Connects: 'a' was not given", Assert.ThrowsException<GolemDomainException>(() => map.Connects(null, kitchen)).Message);
        Assert.AreEqual("Zone.Contains: 'at' was not given", Assert.ThrowsException<GolemDomainException>(() => kitchen.Contains(null)).Message);
        Assert.AreEqual("a golem needs a body to drive", Assert.ThrowsException<GolemDomainException>(() => new GolemDomain.Golem(null, map, new GolemDomain.Touches.Collisions(map))).Message);
        Assert.AreEqual("Route.Route: 'stop' was not given", Assert.ThrowsException<GolemDomainException>(() => new GolemDomain.Routes.Route(1, null, false, false, map, new GolemDomain.Touches.Collisions(map), 0.25, 0.6, _ => { })).Message);
    }

    [TestMethod]
    public void TwoObjectsOfOneKind_MustBeTwoDifferentObjects()
    {
        // Juan, 14-sep-2026: an act that takes two areas (two points, two ends) refuses the same object twice
        var map = Catalog.Warehouse();
        var kitchen = map.Find("kitchen");
        var p = new GolemDomain.Geometry.Position(1.0, 1.0);
        Assert.AreEqual("Map.Connects: 'a' and 'b' are the same area", Assert.ThrowsException<GolemDomainException>(() => map.Connects(kitchen, kitchen)).Message);
        Assert.AreEqual("Map.DoorBetween: 'a' and 'b' are the same area", Assert.ThrowsException<GolemDomainException>(() => map.DoorBetween(kitchen, kitchen)).Message);
        Assert.AreEqual("MapLayout.Touches: 'a' and 'b' are the same area", Assert.ThrowsException<GolemDomainException>(() => map.Touches(kitchen, kitchen)).Message);
        Assert.AreEqual("area 'kitchen' has no door to itself", Assert.ThrowsException<GolemDomainException>(() => map.Door("kitchen", "kitchen")).Message);
        Assert.AreEqual("Segment.Segment: 'from' and 'to' are the same point", Assert.ThrowsException<GolemDomainException>(() => new GolemDomain.Geometry.Segment(p, p)).Message);
        Assert.IsTrue(map.Connects(kitchen, map.Find("north")), "two different areas answer as before");
    }

    [TestMethod]
    public void APosition_LivesOnTheFloorUnlessToldItsHeight()
    {
        // Juan, 14-sep-2026: two coordinates are a point on the floor (z = 0); the third dimension waits for the day it is needed
        Born(Catalog.Warehouse().AsRelease());
        Assert.AreEqual(0.0, Double("Position(4.0, 9.5).Z"), 1e-12, "on the floor");
        Assert.AreEqual(1.2, Double("Position(4.0, 9.5, 1.2).Z"), 1e-12, "above it");
        Assert.AreEqual(5.0, Double("Position(0.0, 0.0, 0.0).DistanceTo(Position(3.0, 4.0))"), 1e-9, "the plane's distance");
        Assert.AreEqual(13.0, Double("Position(0.0, 0.0, 0.0).DistanceTo(Position(3.0, 4.0, 12.0))"), 1e-9, "distance in space");
        Assert.AreEqual(1.2, Double("Position(1.0, 1.0, 1.2).Along(0.0, 2.0).Z"), 1e-12, "a run along a heading keeps the height");
    }

    private void Visit(int id, string[] stops, double fromX, double fromY)
    {
        for (int i = 0; i < stops.Length; i++)
        {
            string stop = stops[i];
            bool first = i == 0;
            string script = stop.Contains(',')
                ? (first ? "{ from = Position(@fx, @fy); point = Position(@x, @y); route = g.Visit(from, point); }" : "{ route = g.Find(@id); point = Position(@x, @y); route.Then(point); }")
                : (first ? "{ from = Position(@fx, @fy); point = map.Find(@area); route = g.Visit(from, point); }" : "{ route = g.Find(@id); point = map.Find(@area); route.Then(point); }");
            perf.Actor.Using(script)
            .WithParameters(p => {
                if (first) { p["fx", typeof(double)] = fromX; p["fy", typeof(double)] = fromY; }
                else p["id", typeof(int)] = id;
                if (stop.Contains(','))
                {
                    var xy = stop.Split(',');
                    p["x", typeof(double)] = double.Parse(xy[0], CultureInfo.InvariantCulture);
                    p["y", typeof(double)] = double.Parse(xy[1], CultureInfo.InvariantCulture);
                }
                else p["area", typeof(string)] = stop;
            })
            .PerformCommand();
        }
    }

    private void Visit(int id, string place, double fromX, double fromY) =>
        perf.Actor.Using(@"
            { from = Position(@fx, @fy); point = map.Find(@area); route = g.Visit(from, point); }
        ")
        .WithParameters(p => {
            p["fx", typeof(double)] = fromX; p["fy", typeof(double)] = fromY;
            p["area", typeof(string)] = place;
        })
        .PerformCommand();

    private int Int(string expression)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using($"@value = {expression};")
        .WithParameters(rented, p => {
            p[Parameter.Out, "value", typeof(int)] = default;
        })
        .PerformQuery();
        return rented["value"].GetValue<int>();
    }

    private double Double(string expression)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using($"@value = {expression};")
        .WithParameters(rented, p => {
            p[Parameter.Out, "value", typeof(double)] = default;
        })
        .PerformQuery();
        return rented["value"].GetValue<double>();
    }

    private bool Bool(string expression)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using($"@value = {expression};")
        .WithParameters(rented, p => {
            p[Parameter.Out, "value", typeof(bool)] = default;
        })
        .PerformQuery();
        return rented["value"].GetValue<bool>();
    }

    private string Text(string expression)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using($"@value = {expression};")
        .WithParameters(rented, p => {
            p[Parameter.Out, "value", typeof(string)] = default;
        })
        .PerformQuery();
        return rented["value"].GetValue<string>();
    }
}
