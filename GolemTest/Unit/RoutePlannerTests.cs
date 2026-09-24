using GolemDomain;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Routes;
using GolemDomain.Touches;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// THE PLANNER (Routes.RoutePlanner): the shortest road between two points of a layout, for a body of a given radius —
// Dijkstra over doors, the pivots of the openings and the corners of the things' figures; a straight run wherever the map
// lets one through. It consults the layout and the collisions module and owns neither.
[TestClass]
public class RoutePlannerTests
{
    // ---- the road over a clean floor ----

    [TestMethod]
    public void FromTheNorthHall_ToTheSouthHall_IsOneStraightLeg_TheOpeningsCrossedNotBentOn()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var planner = new RoutePlanner(map, collisions, 0.25);

        // Juan, 21-sep-2026: "¿por qué el dominio no une esos puntos y llega directo?"
        Assert.AreEqual("south@5.5,1.5", planner.Road(new Position(6.3, 10.4), new Position(5.5, 1.5)).AsPlan(), "one leg: the two open boundaries are crossed on the way");
    }

    [TestMethod]
    public void WhereTheStraightRun_WouldGrazeABlocksCorner_TheWayBendsOnTheOpeningsPivots()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var planner = new RoutePlanner(map, collisions, 0.25);

        // the straight run would cross north~center 0.46 m from the block's corner: the way bends on the pivots by the corners
        Assert.AreEqual("north~center@4.5,8 > center~south@6.5,3 > south@6.9,2.4", planner.Road(new Position(4.2, 8.6), new Position(6.9, 2.4)).AsPlan());
    }

    [TestMethod]
    public void TheShortestRoad_CutsThroughTheCentre_WhenThatIsShorter()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var planner = new RoutePlanner(map, collisions, 0.25);

        // kitchen (2,9.5) -> door (4,9.5) -> ONE straight run through the north hall, the centre and the south hall -> door (7,1.5) -> garage (9,1.5)
        var kitchen = map.Find("kitchen").Center; var garage = map.Find("garage").Center;
        Assert.AreEqual("kitchen/north@4,9.5 > south/garage@7,1.5 > garage@9,1.5", planner.Road(kitchen, garage).AsPlan());
        double throughTheCentre = 2.0 + Math.Sqrt(9.0 + 64.0) + 2.0;                       // ~12.54
        double aroundByTheWest = 2 * Math.Sqrt(3.8125) + 5.0 + Math.Sqrt(12.8125) + 3.0 + 2.0; // ~15.5
        Assert.AreEqual(throughTheCentre, planner.RoadLength(kitchen, garage), 0.01);
        Assert.IsTrue(planner.RoadLength(kitchen, garage) < aroundByTheWest, "the ring is the long way round");
    }

    [TestMethod]
    public void TheShortestRoad_TakesTheCorridor_WhenThatIsShorter()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var planner = new RoutePlanner(map, collisions, 0.25);

        // kitchen (2,9.5) -> door (0.75,8) -> west corridor -> door (0.75,3) -> living (2,1.5)
        var kitchen = map.Find("kitchen").Center; var living = map.Find("living").Center;
        Assert.AreEqual("kitchen/west@0.75,8 > west/living@0.75,3 > living@2,1.5", planner.Road(kitchen, living).AsPlan());
        Assert.AreEqual(2 * Math.Sqrt(3.8125) + 5.0, planner.RoadLength(kitchen, living), 0.01, "~8.9, against ~13.2 through the centre");
    }

    [TestMethod]
    public void AnErrandInTheSameRoom_HasAWayOfOneLeg_TheStopItself()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var planner = new RoutePlanner(map, collisions, 0.25);

        Assert.AreEqual("kitchen@2,9.5", planner.Road(new Position(3.0, 9.0), new Position(2.0, 9.5)).AsPlan(), "same room, nothing in between");
        Assert.AreEqual(1, planner.Road(new Position(3.0, 9.0), new Position(2.0, 9.5)).Count);
        Assert.IsTrue(planner.Road(new Position(3.0, 9.0), new Position(2.0, 9.5)).Last.IsStop);
    }

    [TestMethod]
    public void ARoadThroughSeveralStops_IsWalkedAsOne_InTheOrderGiven()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var planner = new RoutePlanner(map, collisions, 0.25);

        // from the living room: garage first, back out through the same door, the centre shortcut to the kitchen, then the storage
        string plan = planner.Road(map.Find("living").Center, new[] { map.Find("garage").Center, map.Find("kitchen").Center, map.Find("storage").Center }).AsPlan();
        StringAssert.StartsWith(plan, "living/south@4,1.5 > south/garage@7,1.5 > garage@9,1.5 > south/garage@7,1.5 > ");
        StringAssert.Contains(plan, "> kitchen@2,9.5 > kitchen/north@4,9.5 > north/storage@7,9.5 > storage@9,9.5");
        Assert.IsTrue(plan.IndexOf("garage@9,1.5") < plan.IndexOf("kitchen@2,9.5") && plan.IndexOf("kitchen@2,9.5") < plan.IndexOf("storage@9,9.5"), "the order is the caller's");
    }

    [TestMethod]
    public void TheBestOrder_MakesTheWholeRoadShortest()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var planner = new RoutePlanner(map, collisions, 0.25);

        // from the living room the shortest round is garage (7), then storage by the east corridor (8.9), then kitchen (7);
        // the order given would cost 7 + 12.9 + 7
        var stops = new[] { map.Find("garage").Center, map.Find("kitchen").Center, map.Find("storage").Center };
        var best = planner.BestOrder(map.Find("living").Center, stops);
        CollectionAssert.AreEqual(new[] { "garage", "storage", "kitchen" }, best.Select(s => map.ZoneAt(s).Name).ToList(), "garage, storage, kitchen — not the order given");
    }

    // ---- the road past what the bodies learned by touching ----

    [TestMethod]
    public void AMarkInACorridorTooNarrowForTheBody_ClosesIt_AndTheRoadGoesRound()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var planner = new RoutePlanner(map, collisions, 0.25);

        var storage = map.Find("storage").Center; var garage = map.Find("garage").Center;
        StringAssert.Contains(planner.Road(storage, garage).AsPlan(), "storage/east@10.25,8 > east/garage@10.25,3", "the east corridor is the shortest road");

        collisions.Mark(new Pose(10.25, 5.85, -1.5708));   // a peer bumped into something in the middle of the corridor

        string after = planner.Road(storage, garage).AsPlan();
        Assert.IsFalse(after.Contains("east/garage"), "between the mark and the corridor's walls the body does not fit: " + after);
        StringAssert.EndsWith(after, "> south/garage@7,1.5 > garage@9,1.5");
    }

    [TestMethod]
    public void AMarkInAWideRoom_IsSkirted_WithAroundLegs()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var planner = new RoutePlanner(map, collisions, 0.25);

        collisions.Mark(new Pose(9.0, 9.5, 0.0));   // something in the middle of the storage room, touched heading east
        var road = planner.Road(new Position(7.6, 9.5), new Position(10.2, 9.5));
        StringAssert.Contains(road.AsPlan(), "around@", "the body skirts the mark: " + road.AsPlan());
        StringAssert.EndsWith(road.AsPlan(), "> storage@10.2,9.5");
        Assert.AreEqual("around", road.LegAt(0).Name, "the detour is the first leg of the way: the route's own");
        Assert.IsFalse(road.LegAt(0).IsStop);
        Assert.IsTrue(road.Count >= 2);
    }

    [TestMethod]
    public void AfterABump_TheRoadFromTheRetreat_LeavesTheWayItCame_NeverThroughTheMark()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var planner = new RoutePlanner(map, collisions, 0.25);

        collisions.Mark(new Pose(10.25, 5.85, -1.5708));       // the crate, met halfway down the corridor
        string again = planner.Road(new Position(10.25, 6.3), map.Find("garage").Center).AsPlan();   // from where the body backed off to
        Assert.IsFalse(again.Contains("east/garage"), "the corridor is closed by the mark: " + again);
        StringAssert.StartsWith(again, "storage/east@10.25,8", "back out the way it came");
    }

    [TestMethod]
    public void ABodyStandingAmongMarks_CanStillLeave_ButNotThroughThem()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var planner = new RoutePlanner(map, collisions, 0.25);

        collisions.Mark(new Pose(8.2, 9.3, -1.5708));   // two touches on something right beside the body — a thing 0.6 wide
        collisions.Mark(new Pose(8.8, 9.3, -1.5708));
        StringAssert.EndsWith(planner.Road(new Position(8.5, 9.55), map.Find("garage").Center).AsPlan(), "> garage@9,1.5", "a road out exists");

        collisions.Mark(new Pose(10.25, 5.85, -1.5708));       // the crate closes the corridor too
        // a body 0.15 north of that mark (it just backed off the crate; the touch was estimated a little short) leaves the way it
        // came and takes the long way round — never straight down through the mark (the 8-sep live run)
        string outNorth = planner.Road(new Position(10.25, 6.0), map.Find("garage").Center).AsPlan();
        StringAssert.StartsWith(outNorth, "storage/east@10.25,8 > ", "back out through the door it came in by: " + outNorth);
        Assert.IsFalse(outNorth.Contains("east/garage"), "not through the crate: " + outNorth);
    }

    [TestMethod]
    public void ABodyStandingInsideItsOwnMark_CanStillLeave_MovingAwayFromIt()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var planner = new RoutePlanner(map, collisions, 0.25);

        // From the 8-sep live run: red grazed the crate's corner with its side; the touch was estimated head-on, so the mark fell
        // inside the body's own radius. A body cannot be inside a thing: the mark forbids walking further in, not leaving.
        collisions.Mark(new Pose(4.93672793175996, 5.75978159057928, -1.37257826652825));
        collisions.Mark(new Pose(5.14806941278059, 5.65085610890956, 0.0681849256515386));
        StringAssert.EndsWith(planner.Road(new Position(4.8, 5.63), map.Find("garage").Center).AsPlan(), "> garage@9,1.5", "a road out exists");
    }

    [TestMethod]
    public void WhenNoRoadFitsTheBody_ThePlannerSaysSo_CountingTheMarks()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var planner = new RoutePlanner(map, collisions, 0.25);

        collisions.Mark(new Pose(7.0, 1.5, 0.0));           // something in the south/garage door
        collisions.Mark(new Pose(10.25, 3.0, 1.5708));        // and something in the east/garage door
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => planner.Road(map.Find("garage").Center, map.Find("living").Center)).Message, "fits a body of radius 0.25 past 2 marks");
    }

    [TestMethod]
    public void WhenNoRoadExistsThroughTheMap_ThePlannerSaysThatInstead()
    {
        var island = new MapLayout("islands");
        var a = island.Area("a").At(new Position(0, 0)).Size(2, 2);
        island.Area("b").At(new Position(5, 5)).Size(2, 2);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new RoutePlanner(island, new Collisions(), 0.25).Road(a.Center, new Position(6, 6))).Message, "through the map");
    }

    [TestMethod]
    public void ThePlanner_RefusesNothingGiven_AndABodyOfNegativeRadius()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var planner = new RoutePlanner(map, collisions, 0.25);

        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new RoutePlanner(null, collisions, 0.25)).Message, "layout");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new RoutePlanner(map, null, 0.25)).Message, "learned");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new RoutePlanner(map, collisions, -0.1)).Message, "radius cannot be negative");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => planner.Road(null, new Position(1, 1))).Message, "not given");
    }
}
