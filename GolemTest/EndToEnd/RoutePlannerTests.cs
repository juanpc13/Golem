using GolemDomain;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Robots;
using GolemDomain.Touches;
using GolemDomain.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// THE PLANNER (Routes.RoutePlanner), REACHED THROUGH THE GOLEM (Juan, 24-sep-2026: "que los testcase le pasen los módulos que se
// están testeando al golem"): the golem builds it at query time over the map and the collisions module it received, and the route it
// hands out (g.Visit, g.Cover) decides its way with it — the shortest road between two points for a body of the golem's radius,
// Dijkstra over doors, the pivots of the openings and the corners of the things' figures; a straight run wherever the map lets one
// through. What the fleet learned by touching enters as the journal writes it (g.HearBump) and the next way skirts it, or goes
// round when the body would not fit past it. The planner consults the layout and the collisions module and owns neither.
[TestClass]
public class RoutePlannerTests
{
    // ---- the road over a clean floor ----

    [TestMethod]
    public void FromTheNorthHall_ToTheSouthHall_IsOneStraightLeg_TheOpeningsCrossedNotBentOn()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        // Juan, 21-sep-2026: "¿por qué el dominio no une esos puntos y llega directo?"
        Assert.AreEqual("south@5.5,1.5", g.Visit(new Position(6.3, 10.4), new Position(5.5, 1.5)).AsPlan(), "one leg: the two open boundaries are crossed on the way");
    }

    [TestMethod]
    public void WhereTheStraightRun_WouldGrazeABlocksCorner_TheWayBendsOnTheOpeningsPivots()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        // the straight run would cross north~center 0.46 m from the block's corner: the way bends on the pivots by the corners
        Assert.AreEqual("north~center@4.5,8 > center~south@6.5,3 > south@6.9,2.4", g.Visit(new Position(4.2, 8.6), new Position(6.9, 2.4)).AsPlan());
    }

    [TestMethod]
    public void TheShortestRoad_CutsThroughTheCentre_WhenThatIsShorter()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        // kitchen (2,9.5) -> door (4,9.5) -> ONE straight run through the north hall, the centre and the south hall -> door (7,1.5) -> garage (9,1.5)
        Assert.AreEqual("kitchen/north@4,9.5 > south/garage@7,1.5 > garage@9,1.5", g.Visit(map.Find("kitchen").Center, map.Find("garage")).AsPlan());
        double throughTheCentre = 2.0 + Math.Sqrt(9.0 + 64.0) + 2.0;                       // ~12.54
        double aroundByTheWest = 2 * Math.Sqrt(3.8125) + 5.0 + Math.Sqrt(12.8125) + 3.0 + 2.0; // ~15.5
        Assert.AreEqual(throughTheCentre, g.Distance(map.Find("kitchen"), map.Find("garage")), 0.01, "centre to centre through the passages, for this body");
        Assert.IsTrue(g.Distance(map.Find("kitchen"), map.Find("garage")) < aroundByTheWest, "the ring is the long way round");
    }

    [TestMethod]
    public void TheShortestRoad_TakesTheCorridor_WhenThatIsShorter()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        // kitchen (2,9.5) -> door (0.75,8) -> west corridor -> door (0.75,3) -> living (2,1.5)
        Assert.AreEqual("kitchen/west@0.75,8 > west/living@0.75,3 > living@2,1.5", g.Visit(map.Find("kitchen").Center, map.Find("living")).AsPlan());
        Assert.AreEqual(2 * Math.Sqrt(3.8125) + 5.0, g.Distance(map.Find("kitchen"), map.Find("living")), 0.01, "~8.9, against ~13.2 through the centre");
    }

    [TestMethod]
    public void AnErrandInTheSameRoom_HasAWayOfOneLeg_TheStopItself()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var route = g.Visit(new Position(3.0, 9.0), new Position(2.0, 9.5));
        Assert.AreEqual("kitchen@2,9.5", route.AsPlan(), "same room, nothing in between");
        Assert.AreEqual(1, route.LegsAhead.Count);
        Assert.IsTrue(route.NextLeg.IsStop);
    }

    [TestMethod]
    public void ARoadThroughSeveralStops_IsWalkedAsOne_InTheOrderGiven()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        // from the living room: garage first, back out through the same door, the centre shortcut to the kitchen, then the storage
        string plan = g.Visit(map.Find("living").Center, map.Find("garage")).Then(map.Find("kitchen")).Then(map.Find("storage")).AsPlan();
        StringAssert.StartsWith(plan, "living/south@4,1.5 > south/garage@7,1.5 > garage@9,1.5 > south/garage@7,1.5 > ");
        StringAssert.Contains(plan, "> kitchen@2,9.5 > kitchen/north@4,9.5 > north/storage@7,9.5 > storage@9,9.5");
        Assert.IsTrue(plan.IndexOf("garage@9,1.5") < plan.IndexOf("kitchen@2,9.5") && plan.IndexOf("kitchen@2,9.5") < plan.IndexOf("storage@9,9.5"), "the order is the caller's");
    }

    [TestMethod]
    public void TheBestOrder_MakesTheWholeRoadShortest()
    {
        var map = Catalog.Warehouse();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var chooses = new Golem(body, map, new Collisions());
        var obeys = new Golem(body, map, new Collisions());

        // from the living room the shortest round is garage (7), then storage by the east corridor (8.9), then kitchen (7);
        // the order given would cost 7 + 12.5 + 7
        var chosen = chooses.Cover(map.Find("living").Center, map.Find("garage")).Then(map.Find("kitchen")).Then(map.Find("storage"));
        var given = obeys.Visit(map.Find("living").Center, map.Find("garage")).Then(map.Find("kitchen")).Then(map.Find("storage"));
        CollectionAssert.AreEqual(new[] { "garage", "storage", "kitchen" }, chosen.StopsAhead.Select(s => map.ZoneAt(s).Name).ToList(), "garage, storage, kitchen — not the order given");
        CollectionAssert.AreEqual(new[] { "garage", "kitchen", "storage" }, given.StopsAhead.Select(s => map.ZoneAt(s).Name).ToList(), "a Visit keeps the operator's order");
        Assert.AreEqual(2 * Math.Sqrt(3.8125) + 5.0 + 7.0, chooses.RouteLength(), 0.05, "stop to stop: the east corridor to the storage, then west along the north to the kitchen");
        Assert.IsTrue(chooses.RouteLength() < obeys.RouteLength(), "the golem's order makes the whole road shorter: " + chooses.RouteLength() + " against " + obeys.RouteLength());
    }

    // ---- the road past what the bodies learned by touching ----

    [TestMethod]
    public void AMarkInACorridorTooNarrowForTheBody_ClosesIt_AndTheRoadGoesRound()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var before = g.Visit(map.Find("storage").Center, map.Find("garage"));
        StringAssert.Contains(before.AsPlan(), "storage/east@10.25,8 > east/garage@10.25,3", "the east corridor is the shortest road");
        before.Abandon("planned again once the corridor is known shut");

        g.HearBump("red", new Pose(10.25, 6.1, -1.5708), 0.0);   // a peer bumped into something in the middle of the corridor: (10.25, 5.85)

        string after = g.Visit(map.Find("storage").Center, map.Find("garage")).AsPlan();
        Assert.IsFalse(after.Contains("east/garage"), "between the mark and the corridor's walls the body does not fit: " + after);
        StringAssert.EndsWith(after, "> south/garage@7,1.5 > garage@9,1.5");
    }

    [TestMethod]
    public void AMarkInAWideRoom_IsSkirted_WithAroundLegs()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        g.HearBump("red", new Pose(8.75, 9.5, 0.0), 0.0);   // something in the middle of the storage room, touched heading east: (9.0, 9.5)
        var route = g.Visit(new Position(7.6, 9.5), new Position(10.2, 9.5));
        StringAssert.Contains(route.AsPlan(), "around@", "the body skirts the mark: " + route.AsPlan());
        StringAssert.EndsWith(route.AsPlan(), "> storage@10.2,9.5");
        Assert.AreEqual("around", route.NextLeg.Name, "the detour is the first leg of the way: the route's own");
        Assert.IsFalse(route.NextLeg.IsStop);
        Assert.IsTrue(route.LegsAhead.Count >= 2);
    }

    [TestMethod]
    public void AfterABump_TheWayFromTheRetreat_LeavesTheWayItCame_NeverThroughTheMark()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var route = g.Visit(new Pose(9.0, 9.5, -1.5708), map.Find("garage"));   // down the east corridor
        g.Bump(new Pose(10.25, 6.1, -1.5708), 0.0);                                // the crate, met halfway down
        string again = route.AsPlan();
        StringAssert.StartsWith(again, "back@", "back off first: " + again);
        Assert.IsFalse(again.Contains("east/garage"), "the corridor is closed by the mark: " + again);
        StringAssert.Contains(again, "storage/east@10.25,8", "back out the way it came");
    }

    [TestMethod]
    public void ABodyStandingAmongMarks_CanStillLeave_ButNotThroughThem()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        g.HearBump("red", new Pose(8.2, 9.55, -1.5708), 0.0);   // two touches on something right beside the body — a thing 0.6 wide: (8.2, 9.3)…
        g.HearBump("red", new Pose(8.8, 9.55, -1.5708), 0.0);   // …and (8.8, 9.3)
        StringAssert.EndsWith(g.Visit(new Position(8.5, 9.55), map.Find("garage")).AsPlan(), "> garage@9,1.5", "a road out exists");

        g.HearBump("red", new Pose(10.25, 6.1, -1.5708), 0.0);   // the crate closes the corridor too
        // a body 0.15 north of that mark (it just backed off the crate; the touch was estimated a little short) leaves the way it
        // came and takes the long way round — never straight down through the mark (the 8-sep live run)
        string outNorth = g.Visit(new Position(10.25, 6.0), map.Find("garage")).AsPlan();
        StringAssert.StartsWith(outNorth, "storage/east@10.25,8 > ", "back out through the door it came in by: " + outNorth);
        Assert.IsFalse(outNorth.Contains("east/garage"), "not through the crate: " + outNorth);
    }

    [TestMethod]
    public void ABodyStandingInsideItsOwnMark_CanStillLeave_MovingAwayFromIt()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        // From the 8-sep live run: red grazed the crate's corner with its side; the touch was estimated head-on, so the mark fell
        // inside the body's own radius. A body cannot be inside a thing: the mark forbids walking further in, not leaving.
        g.Visit(new Pose(5.5, 9.5, -1.5708), new Position(5.5, 1.5));
        g.Bump(new Pose(4.888, 6.005, -1.373), 0.0);   // the touch reckoned at (4.937, 5.760)
        g.Bump(new Pose(4.899, 5.634, 0.068), 0.0);    // and at (5.148, 5.651)
        StringAssert.EndsWith(g.Visit(new Position(4.8, 5.63), map.Find("garage")).AsPlan(), "> garage@9,1.5", "a road out exists");
    }

    [TestMethod]
    public void WhenNoRoadFitsTheBody_TheErrandIsRefused_CountingTheMarks()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        g.HearBump("red", new Pose(6.75, 1.5, 0.0), 0.0);        // something in the south/garage door: (7.0, 1.5)
        g.HearBump("red", new Pose(10.25, 2.75, 1.5708), 0.0);   // and something in the east/garage door: (10.25, 3.0)
        var refused = Assert.ThrowsException<GolemDomainException>(() => g.Visit(map.Find("garage").Center, map.Find("living")));
        StringAssert.Contains(refused.Message, "no road");
        StringAssert.Contains(refused.Message, "fits a body of radius 0.25 past 2 marks");
        Assert.AreEqual(0, g.Routes().Count, "nothing minted");
    }

    [TestMethod]
    public void WhenNoRoadExistsThroughTheMap_TheErrandSaysThatInstead()
    {
        var island = new MapLayout("islands");
        var a = island.Area("a").At(new Position(0, 0)).Size(2, 2);
        island.Area("b").At(new Position(5, 5)).Size(2, 2);
        var g = new Golem(new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6)), island, new Collisions());
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Visit(a.Center, new Position(6, 6))).Message, "through the map");
    }

    [TestMethod]
    public void TheRoad_RefusesNothingGiven()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var route = g.Visit(new Position(2.0, 9.5), new Position(2.0, 1.5));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Road(null, new Position(1, 1))).Message, "'route' was not given");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Road(route, null)).Message, "'from' was not given");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Visit(null, new Position(1, 1))).Message, "'from' was not given");
        Assert.AreEqual(route.AsPlan(), g.Road(route, new Position(2.0, 9.5)).AsPlan(), "the way the golem would walk, read as objects: the same the route decided");
    }
}
