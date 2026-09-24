using GolemDomain;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Maps;
using GolemDomain.Robots;
using GolemDomain.Touches;
using GolemDomain.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// THE MAP (Maps.Map, Layouts.MapLayout), THE MODULE THE GOLEM RECEIVES (Juan, 24-sep-2026: "que los testcase le pasen los módulos
// que se están testeando al golem"): a layout is built with the domain's own classes — the areas first, then the passages between
// objects, as warehouse_v1 builds it — and handed to a golem; what the map disposes shows in the WAYS the golem decides over it: a
// straight run where the map lets one through, a bend on an opening's pivots where it does not, a door crossed, no way at all between
// what is not joined. The map's own reads stay the map's, as the journal reads them (map.Find, map.Connects, map.ZoneAt); the release
// a map renders is checked as text, because rendering it is the layout's own operation — that the engine applies it is the acceptance
// tests' business.
[TestClass]
public class MapLayoutTests
{
    // ---- the warehouse: built in two movements, read as a maquette and as a layout, walked by a golem ----

    [TestMethod]
    public void TheWarehouse_IsBuiltInTwoMovements_TheAreasFirst_ThenThePassagesBetweenObjects()
    {
        var map = new MapLayout("warehouse");
        var kitchen = map.Area("kitchen").At(new Position(0, 8)).Size(4, 3);
        var north = map.Area("north").At(new Position(4, 8)).Size(3, 3);
        var center = map.Area("center").At(new Position(4, 3)).Size(3, 5);
        var garage = map.Area("garage").At(new Position(7, 0)).Size(4, 3);
        kitchen.DoorAt(north, new Position(4, 9.5));
        north.OpenTo(center);
        var g = new Golem(new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6)), map, new Collisions());

        Assert.AreEqual(4, map.ZoneCount, "every area laid out");
        Assert.AreEqual(2, map.PassageCount, "one door, one open boundary");
        Assert.IsTrue(map.Connects(kitchen, north), "joined by a door: information");
        Assert.IsTrue(map.Connects(north, center), "joined by an open boundary: information");
        Assert.IsTrue(map.Touches(north, center), "and actually sharing an edge on the plane: geometry");
        Assert.IsFalse(map.Connects(kitchen, garage));
        Assert.AreEqual(4.0, map.PointOf(map.DoorBetween(kitchen, north)).X, 1e-9, "the door stands where it was told");
        Assert.AreEqual(9.5, map.PointOf(map.DoorBetween(kitchen, north)).Y, 1e-9);
        CollectionAssert.AreEquivalent(new[] { "north" }, kitchen.Neighbours().Select(a => a.Name).ToList());
        Assert.AreEqual(4.0, kitchen.Width, 1e-9, "found once, read as the zone it is");
        Assert.AreEqual("center", map.ZoneAt(new Position(5.5, 5.5)).Name);
        Assert.AreEqual("north", map.ZoneOf(new Position(5.5, 9.5)).Name);

        // what the map disposes, walked: out of the kitchen by its door, across the open boundary, to the centre — and nowhere to the garage
        Assert.AreEqual("kitchen/north@4,9.5 > center@5.5,5.5", g.Visit(new Position(2.0, 9.5), center).AsPlan());
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Visit(new Position(2.0, 9.5), garage)).Message, "through the map");
    }

    [TestMethod]
    public void TheCatalogsWarehouse_IsTheOneTheWorldBuilds_NineAreasAroundTwoBlocks()
    {
        var map = Catalog.Warehouse();
        var g = new Golem(new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6)), map, new Collisions());

        Assert.AreEqual(9, map.AreaCount, "what the maquette disposes");
        Assert.AreEqual(9, map.ZoneCount, "every area laid out");
        Assert.AreEqual(10, map.PassageCount, "eight doors and two open boundaries");
        Assert.AreEqual(8, map.Doors.Count());
        Assert.AreEqual(2, map.Openings.Count());
        Assert.AreEqual(2, map.Find("kitchen").Neighbours().Count, "the kitchen connects to two areas");
        Assert.IsTrue(map.Connects(map.Find("north"), map.Find("center")));
        Assert.IsTrue(map.Touches(map.Find("north"), map.Find("center")));
        Assert.IsFalse(map.Connects(map.Find("kitchen"), map.Find("garage")));
        Assert.IsTrue(map.IsOnMap(new Position(5.5, 5.5)));
        Assert.IsFalse(map.IsOnMap(new Position(-1.0, 5.5)), "the floor is 11 x 11");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Visit(new Position(5.5, 5.5), new Position(-1.0, 5.5))).Message, "nowhere on the map");
        Assert.AreEqual(9, map.Find("kitchen").Neighbours().Count + map.Find("garage").Neighbours().Count + 5, "the kitchen and the garage have two neighbours each");
    }

    [TestMethod]
    public void TheWarehouse_RendersItsReleaseInTwoMovements_NoNeighbourEntersByName()
    {
        string release = Catalog.Warehouse().AsRelease();

        StringAssert.StartsWith(release, "upgrade('warehouse_v1') {\n    map = MapLayout('warehouse');\n    {\n");
        StringAssert.Contains(release, "        kitchen = map.Area('kitchen').At(Position(0.0, 8.0)).Size(4.0, 3.0);\n");
        StringAssert.Contains(release, "        garage = map.Area('garage').At(Position(7.0, 0.0)).Size(4.0, 3.0);\n");
        StringAssert.Contains(release, "        kitchen.DoorAt(north, Position(4.0, 9.5));\n        kitchen.DoorAt(west, Position(0.75, 8.0));\n");
        StringAssert.Contains(release, "        north.DoorAt(storage, Position(7.0, 9.5));\n        north.OpenTo(center);\n");
        StringAssert.Contains(release, "        south.DoorAt(garage, Position(7.0, 1.5));\n    }\n}\n");
        Assert.IsTrue(release.IndexOf("garage = map.Area") < release.IndexOf("kitchen.DoorAt"), "every area exists before the first passage");
        Assert.IsFalse(release.Contains("DoorAt('") || release.Contains("DoorTo('") || release.Contains("OpenTo('"), "no neighbour enters by name: the passages are opened between objects");
        Assert.IsFalse(System.Text.RegularExpressions.Regex.IsMatch(release, @"\bg\."), "the golem writes nothing here: the map builds itself, and the golem receives it");
    }

    // ---- the straight run and the pivots: what the layout lets through shows in the way the golem decides ----

    [TestMethod]
    public void AStraightRun_IsOneLeg_WhereTheMapLetsItThrough_ItsOpenBoundariesCrossedOnTheWay()
    {
        var map = Catalog.Warehouse();
        var g = new Golem(new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6)), map, new Collisions());

        Assert.AreEqual("north@6.5,10.5", g.Visit(new Position(4.5, 9.0), new Position(6.5, 10.5)).AsPlan(), "both points in the north hall: one leg, nothing crossed");
        Assert.AreEqual("south@5.5,1.5", g.Visit(new Position(6.3, 10.4), new Position(5.5, 1.5)).AsPlan(),
            "north hall, centre, south hall: the two open boundaries are crossed on the way, away from the blocks' corners, not bent on (ajuste 45)");
    }

    [TestMethod]
    public void AStraightRun_IsCutByAWall_ByADoor_AndByABlock_SoTheWayGoesByThePassages()
    {
        var map = Catalog.Warehouse();
        var g = new Golem(new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6)), map, new Collisions());

        StringAssert.StartsWith(g.Visit(new Position(3.0, 9.5), new Position(5.5, 1.5)).AsPlan(), "kitchen/north@4,9.5 > ",
            "out of the kitchen through its east wall there is no straight run: a door is not an open boundary, it is crossed");
        Assert.AreEqual("kitchen/west@0.75,8 > west/living@0.75,3 > living@2,1.5", g.Visit(new Position(2.0, 9.5), new Position(2.0, 1.5)).AsPlan(),
            "kitchen to living room straight down: two blocks and a corridor's walls in between — round by the corridor's doors");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Visit(new Position(5.5, 9.5), new Position(7.5, 7.5))).Message, "nowhere on the map",
            "the corner where two walls meet is block: no stop there");
    }

    [TestMethod]
    public void WhereTheWayBends_ItBendsOnAnOpeningsPivots_ItsEndsInsetByTheMargin_OrItsMiddle()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));

        // the warehouse: the straight run would cross north~center 0.46 m from the block's corner, so the way bends on the pivots by
        // the corners — the ends of the 3 m boundaries, inset by the margin: (4.5, 8) and (6.5, 3)
        var warehouse = new Golem(body, Catalog.Warehouse(), new Collisions());
        Assert.AreEqual("north~center@4.5,8 > center~south@6.5,3 > south@6.9,2.4", warehouse.Visit(new Position(4.2, 8.6), new Position(6.9, 2.4)).AsPlan());

        // the ring corridor: up the west corridor and round the corner into the north one — the corner is an open boundary 1.5 m long,
        // and the margin still leaves three places to turn on it
        var ring = new Golem(body, Catalog.RingCorridor(), new Collisions());
        string corner = ring.Visit(new Position(0.75, 9.0), new Position(3.0, 10.25)).AsPlan();
        StringAssert.Contains(corner, "~");
        StringAssert.Contains(corner, "@1.5,10", "the way bends on a pivot of the corner opening, at x = 1.5: " + corner);
        StringAssert.EndsWith(corner, "> north-corridor@3,10.25");

        // a boundary narrower than twice the margin has one pivot, its middle
        var tight = new MapLayout("tight");
        var a = tight.Area("a").At(new Position(0, 0)).Size(0.8, 2);
        var b = tight.Area("b").At(new Position(0, 2)).Size(0.8, 2);
        a.OpenTo(b);
        var narrow = new Golem(body, tight, new Collisions());
        Assert.AreEqual("b@0.2,3", narrow.Visit(new Position(0.2, 1.0), new Position(0.2, 3.0)).AsPlan(), "a straight run still passes wherever the body fits");
        StringAssert.StartsWith(narrow.Visit(new Position(0.05, 1.0), new Position(0.05, 3.0)).AsPlan(), "a~b@0.4,2 > ", "but not hugging the wall: the way bends on the middle");
    }

    // ---- the way the golem decides over the other maps of the catalog ----

    [TestMethod]
    public void CrossCorridors_FourRoomsInTheCorners_TwoAislesThatCross()
    {
        var map = Catalog.Named("cross-corridors");
        var g = new Golem(new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6)), map, new Collisions());

        Assert.AreEqual(9, map.ZoneCount, "four rooms, four aisles, one crossing");
        Assert.AreEqual(12, map.PassageCount, "eight doors, four open boundaries around the crossing");
        Assert.AreEqual("crossing", map.ZoneAt(new Position(5.5, 5.5)).Name);

        // from the northwest room to the southeast one: out a door into an aisle, bending on the crossing's two open
        // boundaries (the straight run would graze the crossing's corner), along the other aisle and in through a door
        string plan = g.Visit(map.Find("northwest").Center, map.Find("southeast")).AsPlan();
        StringAssert.StartsWith(plan, "northwest/");
        Assert.AreEqual(2, plan.Split("crossing~").Length - 1, "in through one aisle, out through the other: " + plan);
        StringAssert.EndsWith(plan, "> southeast@8.63,2.38");
        Assert.AreEqual(2, plan.Split('/').Length - 1, "exactly two doors: " + plan);
        double diagonal = g.Distance(map.Find("northwest"), map.Find("southeast"));
        double straight = Math.Sqrt(6.25 * 6.25 * 2);   // ~8.84, through the crossing's corner: not a road
        Assert.IsTrue(diagonal > straight + 1.5 && diagonal < 11.5, "door, aisle, crossing, aisle, door — a bit over ten metres: " + diagonal);
    }

    [TestMethod]
    public void RingCorridor_FourRoomsInTheMiddle_OneCorridorAllTheWayRound()
    {
        var map = Catalog.Named("ring-corridor");
        var g = new Golem(new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6)), map, new Collisions());

        Assert.AreEqual(8, map.ZoneCount, "four rooms, four stretches of corridor");
        Assert.AreEqual(16, map.PassageCount, "eight doors to the corridor, four between the rooms, four open stretches");
        Assert.AreEqual("west-corridor", map.ZoneAt(new Position(0.75, 5.5)).Name);

        // neighbouring rooms are joined directly: centre to centre through the door they share
        Assert.AreEqual(4.0, g.Distance(map.Find("northwest"), map.Find("northeast")), 0.01, "two metres to the door, two more to the neighbour's centre");
        // the corridor runs all the way round: from one stretch into the next through an open boundary, in one straight run
        Assert.AreEqual("north-corridor@9,10.25", g.Visit(new Position(0.75, 10.25), new Position(9.0, 10.25)).AsPlan(), "the open stretch is crossed on the way, not bent on");
        // and cutting through a room beats going round when it is shorter: the way takes the rooms' doors
        string across = g.Visit(new Position(0.75, 10.25), map.Find("east-corridor")).AsPlan();
        StringAssert.Contains(across, "northeast/north-corridor@7.5,9.5 > northeast/east-corridor@9.5,7.5", "through the northeast room: " + across);
    }

    [TestMethod]
    public void TheCatalog_NamesItsPlans_AndRefusesAnUnknownOne()
    {
        CollectionAssert.AreEqual(new[] { "warehouse", "cross-corridors", "ring-corridor" }, Catalog.Names());
        var refused = Assert.ThrowsException<GolemDomainException>(() => Catalog.Named("attic"));
        StringAssert.Contains(refused.Message, "no map named 'attic'");
    }

    // ---- the guards every method keeps ----

    [TestMethod]
    public void AnObjectNotGiven_IsRefusedByTheDomain_BeforeAnythingRuns()
    {
        // Juan, 14-sep-2026: every method that takes an object checks it first, with an if, and refuses in the domain's voice
        var map = Catalog.Warehouse();
        var kitchen = map.Find("kitchen");
        var g = new Golem(new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6)), map, new Collisions());
        Assert.AreEqual("Map.Connects: 'a' was not given", Assert.ThrowsException<GolemDomainException>(() => map.Connects(null, kitchen)).Message);
        Assert.AreEqual("Zone.Contains: 'at' was not given", Assert.ThrowsException<GolemDomainException>(() => kitchen.Contains(null)).Message);
        Assert.AreEqual("MapLayout.ZoneNameOf: 'at' was not given", Assert.ThrowsException<GolemDomainException>(() => map.ZoneNameOf(null)).Message);
        Assert.AreEqual("Golem.Visit: 'area' was not given", Assert.ThrowsException<GolemDomainException>(() => g.Visit(new Position(2.0, 9.5), (Area)null)).Message);
        Assert.AreEqual("Golem.Distance: 'to' was not given", Assert.ThrowsException<GolemDomainException>(() => g.Distance(kitchen, null)).Message);
        Assert.AreEqual("a golem needs its map, laid out", Assert.ThrowsException<GolemDomainException>(() => new Golem(new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6)), null, new Collisions())).Message);
    }

    [TestMethod]
    public void TwoObjectsOfOneKind_MustBeTwoDifferentObjects()
    {
        // Juan, 14-sep-2026: an act that takes two areas (two points, two ends) refuses the same object twice
        var map = Catalog.Warehouse();
        var kitchen = map.Find("kitchen");
        var g = new Golem(new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6)), map, new Collisions());
        var p = new Position(1.0, 1.0);
        Assert.AreEqual("Map.Connects: 'a' and 'b' are the same area", Assert.ThrowsException<GolemDomainException>(() => map.Connects(kitchen, kitchen)).Message);
        Assert.AreEqual("Map.DoorBetween: 'a' and 'b' are the same area", Assert.ThrowsException<GolemDomainException>(() => map.DoorBetween(kitchen, kitchen)).Message);
        Assert.AreEqual("MapLayout.Touches: 'a' and 'b' are the same area", Assert.ThrowsException<GolemDomainException>(() => map.Touches(kitchen, kitchen)).Message);
        Assert.AreEqual("area 'kitchen' has no door to itself", Assert.ThrowsException<GolemDomainException>(() => map.Door("kitchen", "kitchen")).Message);
        Assert.AreEqual("Golem.Distance: 'from' and 'to' are the same area", Assert.ThrowsException<GolemDomainException>(() => g.Distance(kitchen, kitchen)).Message);
        Assert.AreEqual("Golem.Visit: 'from' and 'stop' are the same position", Assert.ThrowsException<GolemDomainException>(() => g.Visit(p, p)).Message);
        Assert.AreEqual("Segment.Segment: 'from' and 'to' are the same point", Assert.ThrowsException<GolemDomainException>(() => new Segment(p, p)).Message);
        Assert.IsTrue(map.Connects(kitchen, map.Find("north")), "two different areas answer as before");
    }

    [TestMethod]
    public void APosition_LivesOnTheFloorUnlessToldItsHeight()
    {
        // Juan, 14-sep-2026: two coordinates are a point on the floor (z = 0); the third dimension waits for the day it is needed
        Assert.AreEqual(0.0, new Position(4.0, 9.5).Z, 1e-12, "on the floor");
        Assert.AreEqual(1.2, new Position(4.0, 9.5, 1.2).Z, 1e-12, "above it");
        Assert.AreEqual(5.0, new Position(0.0, 0.0, 0.0).DistanceTo(new Position(3.0, 4.0)), 1e-9, "the plane's distance");
        Assert.AreEqual(13.0, new Position(0.0, 0.0, 0.0).DistanceTo(new Position(3.0, 4.0, 12.0)), 1e-9, "distance in space");
        Assert.AreEqual(1.2, new Position(1.0, 1.0, 1.2).Along(0.0, 2.0).Z, 1e-12, "a run along a heading keeps the height");
    }

    [TestMethod]
    public void TheZoneOfAPoint_IsNamedByTheMap_AndEmptyWhereItHoldsNothing()
    {
        // ajuste 54 (24-sep-2026): an obstacle no longer says its zone; a table asks the map where the obstacle's centre stands
        var map = Catalog.Warehouse();
        Assert.AreEqual("east", map.ZoneNameOf(new Position(10.13, 5.5)), "the crate's centre, in the east corridor");
        Assert.AreEqual("storage", map.ZoneNameOf(new Position(8.0, 9.5)));
        Assert.AreEqual("north", map.ZoneNameOf(new Position(4.7, 9.5)), "where a peer was met, in the north hall");
        Assert.AreEqual("", map.ZoneNameOf(new Position(2.0, 5.5)), "a solid block: nowhere the map holds, and no refusal — a table prints it");
    }
}
