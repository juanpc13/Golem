using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Routes;
using GolemDomain.Touches;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static GolemTest.DomainFixture;

namespace GolemTest;

// THE PLANNER (Routes.RoutePlanner): the shortest road between two points of a layout, for a body of a given radius —
// Dijkstra over doors, the pivots of the openings and the corners of the things' figures; a straight run wherever the map
// lets one through. It consults the layout and the collisions module and owns neither.
[TestClass]
public class RoutePlannerTests
{
    private MapLayout map;
    private Collisions collisions;
    private RoutePlanner planner;

    [TestInitialize]
    public void OverTheWarehouse()
    {
        map = Catalog.Warehouse();
        collisions = new Collisions(map);
        planner = new RoutePlanner(map, collisions, Radius);
    }

    // ---- the road over a clean floor ----

    [TestMethod]
    public void FromTheNorthHall_ToTheSouthHall_IsOneStraightLeg_TheOpeningsCrossedNotBentOn()
    {
        // Juan, 21-sep-2026: "¿por qué el dominio no une esos puntos y llega directo?"
        Assert.AreEqual("south@5.5,1.5", planner.Road(P(6.3, 10.4), P(5.5, 1.5)).AsPlan(), "one leg: the two open boundaries are crossed on the way");
    }

    [TestMethod]
    public void WhereTheStraightRun_WouldGrazeABlocksCorner_TheWayBendsOnTheOpeningsPivots()
    {
        // the straight run would cross north~center 0.46 m from the block's corner: the way bends on the pivots by the corners
        Assert.AreEqual("north~center@4.5,8 > center~south@6.5,3 > south@6.9,2.4", planner.Road(P(4.2, 8.6), P(6.9, 2.4)).AsPlan());
    }

    [TestMethod]
    public void TheShortestRoad_CutsThroughTheCentre_WhenThatIsShorter()
    {
        // kitchen (2,9.5) -> door (4,9.5) -> ONE straight run through the north hall, the centre and the south hall -> door (7,1.5) -> garage (9,1.5)
        var kitchen = Centre(map, "kitchen"); var garage = Centre(map, "garage");
        Assert.AreEqual("kitchen/north@4,9.5 > south/garage@7,1.5 > garage@9,1.5", planner.Road(kitchen, garage).AsPlan());
        double throughTheCentre = 2.0 + Math.Sqrt(9.0 + 64.0) + 2.0;                       // ~12.54
        double aroundByTheWest = 2 * Math.Sqrt(3.8125) + 5.0 + Math.Sqrt(12.8125) + 3.0 + 2.0; // ~15.5
        Assert.AreEqual(throughTheCentre, planner.RoadLength(kitchen, garage), 0.01);
        Assert.IsTrue(planner.RoadLength(kitchen, garage) < aroundByTheWest, "the ring is the long way round");
    }

    [TestMethod]
    public void TheShortestRoad_TakesTheCorridor_WhenThatIsShorter()
    {
        // kitchen (2,9.5) -> door (0.75,8) -> west corridor -> door (0.75,3) -> living (2,1.5)
        var kitchen = Centre(map, "kitchen"); var living = Centre(map, "living");
        Assert.AreEqual("kitchen/west@0.75,8 > west/living@0.75,3 > living@2,1.5", planner.Road(kitchen, living).AsPlan());
        Assert.AreEqual(2 * Math.Sqrt(3.8125) + 5.0, planner.RoadLength(kitchen, living), 0.01, "~8.9, against ~13.2 through the centre");
    }

    [TestMethod]
    public void AnErrandInTheSameRoom_HasAWayOfOneLeg_TheStopItself()
    {
        Assert.AreEqual("kitchen@2,9.5", planner.Road(P(3.0, 9.0), P(2.0, 9.5)).AsPlan(), "same room, nothing in between");
        Assert.AreEqual(1, planner.Road(P(3.0, 9.0), P(2.0, 9.5)).Count);
        Assert.IsTrue(planner.Road(P(3.0, 9.0), P(2.0, 9.5)).Last.IsStop);
    }

    [TestMethod]
    public void ARoadThroughSeveralStops_IsWalkedAsOne_InTheOrderGiven()
    {
        // from the living room: garage first, back out through the same door, the centre shortcut to the kitchen, then the storage
        string plan = planner.Road(Centre(map, "living"), new[] { Centre(map, "garage"), Centre(map, "kitchen"), Centre(map, "storage") }).AsPlan();
        StringAssert.StartsWith(plan, "living/south@4,1.5 > south/garage@7,1.5 > garage@9,1.5 > south/garage@7,1.5 > ");
        StringAssert.Contains(plan, "> kitchen@2,9.5 > kitchen/north@4,9.5 > north/storage@7,9.5 > storage@9,9.5");
        Assert.IsTrue(plan.IndexOf("garage@9,1.5") < plan.IndexOf("kitchen@2,9.5") && plan.IndexOf("kitchen@2,9.5") < plan.IndexOf("storage@9,9.5"), "the order is the caller's");
    }

    [TestMethod]
    public void TheBestOrder_MakesTheWholeRoadShortest()
    {
        // from the living room the shortest round is garage (7), then storage by the east corridor (8.9), then kitchen (7);
        // the order given would cost 7 + 12.9 + 7
        var stops = new[] { Centre(map, "garage"), Centre(map, "kitchen"), Centre(map, "storage") };
        var best = planner.BestOrder(Centre(map, "living"), stops);
        CollectionAssert.AreEqual(new[] { "garage", "storage", "kitchen" }, best.Select(s => map.ZoneAt(s).Name).ToList(), "garage, storage, kitchen — not the order given");
    }

    // ---- the road past what the bodies learned by touching ----

    [TestMethod]
    public void AMarkInACorridorTooNarrowForTheBody_ClosesIt_AndTheRoadGoesRound()
    {
        var storage = Centre(map, "storage"); var garage = Centre(map, "garage");
        StringAssert.Contains(planner.Road(storage, garage).AsPlan(), "storage/east@10.25,8 > east/garage@10.25,3", "the east corridor is the shortest road");

        PlantMark(collisions, 10.25, 5.85, South);       // a peer bumped into something in the middle of the corridor

        string after = planner.Road(storage, garage).AsPlan();
        Assert.IsFalse(after.Contains("east/garage"), "between the mark and the corridor's walls the body does not fit: " + after);
        StringAssert.EndsWith(after, "> south/garage@7,1.5 > garage@9,1.5");
    }

    [TestMethod]
    public void AMarkInAWideRoom_IsSkirted_WithAroundLegs()
    {
        PlantMark(collisions, 9.0, 9.5, East);           // something in the middle of the storage room, touched heading east
        var road = planner.Road(P(7.6, 9.5), P(10.2, 9.5));
        StringAssert.Contains(road.AsPlan(), "around@", "the body skirts the mark: " + road.AsPlan());
        StringAssert.EndsWith(road.AsPlan(), "> storage@10.2,9.5");
        Assert.AreEqual("around", road.LegAt(0).Name, "the detour is the first leg of the way: the route's own");
        Assert.IsFalse(road.LegAt(0).IsStop);
        Assert.IsTrue(road.Count >= 2);
    }

    [TestMethod]
    public void AfterABump_TheRoadFromTheRetreat_LeavesTheWayItCame_NeverThroughTheMark()
    {
        PlantMark(collisions, 10.25, 5.85, South);       // the crate, met halfway down the corridor
        string again = planner.Road(P(10.25, 6.3), Centre(map, "garage")).AsPlan();   // from where the body backed off to
        Assert.IsFalse(again.Contains("east/garage"), "the corridor is closed by the mark: " + again);
        StringAssert.StartsWith(again, "storage/east@10.25,8", "back out the way it came");
    }

    [TestMethod]
    public void ABodyStandingAmongMarks_CanStillLeave_ButNotThroughThem()
    {
        PlantMark(collisions, 8.2, 9.3, South);          // two touches on something right beside the body — a thing 0.6 wide
        PlantMark(collisions, 8.8, 9.3, South);
        StringAssert.EndsWith(planner.Road(P(8.5, 9.55), Centre(map, "garage")).AsPlan(), "> garage@9,1.5", "a road out exists");

        PlantMark(collisions, 10.25, 5.85, South);       // the crate closes the corridor too
        // a body 0.15 north of that mark (it just backed off the crate; the touch was estimated a little short) leaves the way it
        // came and takes the long way round — never straight down through the mark (the 8-sep live run)
        string outNorth = planner.Road(P(10.25, 6.0), Centre(map, "garage")).AsPlan();
        StringAssert.StartsWith(outNorth, "storage/east@10.25,8 > ", "back out through the door it came in by: " + outNorth);
        Assert.IsFalse(outNorth.Contains("east/garage"), "not through the crate: " + outNorth);
    }

    [TestMethod]
    public void ABodyStandingInsideItsOwnMark_CanStillLeave_MovingAwayFromIt()
    {
        // From the 8-sep live run: red grazed the crate's corner with its side; the touch was estimated head-on, so the mark fell
        // inside the body's own radius. A body cannot be inside a thing: the mark forbids walking further in, not leaving.
        PlantMark(collisions, 4.93672793175996, 5.75978159057928, -1.37257826652825);
        PlantMark(collisions, 5.14806941278059, 5.65085610890956, 0.0681849256515386);
        StringAssert.EndsWith(planner.Road(P(4.8, 5.63), Centre(map, "garage")).AsPlan(), "> garage@9,1.5", "a road out exists");
    }

    [TestMethod]
    public void WhenNoRoadFitsTheBody_ThePlannerSaysSo_CountingTheMarks()
    {
        PlantMark(collisions, 7.0, 1.5, East);           // something in the south/garage door
        PlantMark(collisions, 10.25, 3.0, North);        // and something in the east/garage door
        Refuses(() => planner.Road(Centre(map, "garage"), Centre(map, "living")), "fits a body of radius 0.25 past 2 marks");
    }

    [TestMethod]
    public void WhenNoRoadExistsThroughTheMap_ThePlannerSaysThatInstead()
    {
        var island = new MapLayout("islands");
        var a = island.Area("a").At(P(0, 0)).Size(2, 2);
        island.Area("b").At(P(5, 5)).Size(2, 2);
        Refuses(() => new RoutePlanner(island, new Collisions(island), Radius).Road(a.Center, P(6, 6)), "through the map");
    }

    [TestMethod]
    public void ThePlanner_RefusesNothingGiven_AndABodyOfNegativeRadius()
    {
        Refuses(() => new RoutePlanner(null, collisions, Radius), "layout");
        Refuses(() => new RoutePlanner(map, null, Radius), "learned");
        Refuses(() => new RoutePlanner(map, collisions, -0.1), "radius cannot be negative");
        Refuses(() => planner.Road(null, P(1, 1)), "not given");
    }
}
