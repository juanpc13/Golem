using System.Globalization;
using System.Linq;
using Choreography.Theater;
using GolemDomain;
using GolemDomain.Layouts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;

namespace GolemTest;

// End-to-end through the perform: a real actor, a journal (in memory), the same
// release chain the host runs, typed assertions via Out parameters.
//
// The floor plan the tests share with the host (11 x 11): a ring of rooms around two solid
// blocks, a wide shortcut through the middle (open boundaries) and two long narrow corridors.
//
//   kitchen (0,8 4x3) --door(4,9.5)-- north (4,8 3x3) --door(7,9.5)-- storage (7,8 4x3)
//     | door (0.75,8)                   ~ open ~                          | door (10.25,8)
//   west (0,3 1.5x5)    [solid]      center (4,3 3x5)     [solid]       east (9.5,3 1.5x5)
//     | door (0.75,3)                   ~ open ~                          | door (10.25,3)
//   living (0,0 4x3) --door(4,1.5)-- south (4,0 3x3) --door(7,1.5)-- garage (7,0 4x3)
[TestClass]
public class MissionAcceptanceTests
{
    private PerformanceV2 perf;
    private const double East = 0.0, North = 1.5708, West = 3.1416, South = -1.5708;   // headings of a touch

    [TestInitialize]
    public void AGolemIsBorn()
    {
        // The DSL renders numbers into the journal with the current culture: pin it.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        // One actor and one store per test: the in-memory store is shared by name.
        string name = "golem-under-test-" + Guid.NewGuid().ToString("N");
        perf = new PerformanceV2(name, DomainLibrary.Assembly);
        perf.ConfigureStorage(DatabaseType.IN_MEMORY, name);
        perf.Start();
        perf.Actor.Using(Releases).PerformCommand();
    }

    [TestCleanup]
    public void TheGolemRests() => perf.Dispose();

    // The release chain as the host carries it: the body (one object), the warehouse map from the catalog (the
    // concrete MapLayout: areas with their positions, doors, openings, in one train each) and the golem that
    // RECEIVES its modules — each a global of the actor in its own right. Constructor literals carry a decimal point (Fase 0, P7).
    private static string Releases =>
        "upgrade('body_v1') { body = Body(0.25, 2.0, 6.0); }\n"
        + Catalog.Warehouse().AsRelease()
        + "upgrade('init') { collisions = Collisions(map); g = Golem(body, map, collisions); }\n";

    // ---- the body ----

    [TestMethod]
    public void TheBody_IsReleasedIntoTheJournal_SizeSpeedAndLinger()
    {
        Assert.AreEqual(0.25, Double("g.Radius()"), 0.001, "the body's size");
        Assert.AreEqual(2.0, Double("g.Speed()"), 0.001, "its cruise speed");
        Assert.AreEqual(6.0, Double("g.LingerAfterTold()"), 0.001, "its linger at a told stop");
        // A constructor's refusal reaches the DSL as "Error while instantiating class 'Body'": the engine wraps the
        // domain's exception and its reason is lost on the way (Fase 0 follow-up, 10-sep-2026). The C# tests below
        // keep the reasons; here we can only assert that the body was refused.
        Refuses("b = Body(0.0, 2.0, 6.0);", "Error while instantiating class 'Body'");
        Refuses("b = Body(0.25, 0.0, 6.0);", "Error while instantiating class 'Body'");
        Refuses("b = Body(0.25, 2.0, -1.0);", "Error while instantiating class 'Body'");
        Assert.AreEqual("a body needs a radius above zero", Assert.ThrowsException<DomainException>(() => new GolemDomain.Robots.Body(0.0, 2.0, 6.0)).Message);
        Assert.AreEqual("a body needs a cruise speed above zero", Assert.ThrowsException<DomainException>(() => new GolemDomain.Robots.Body(0.25, 0.0, 6.0)).Message);
        Assert.AreEqual("a linger cannot be negative", Assert.ThrowsException<DomainException>(() => new GolemDomain.Robots.Body(0.25, 2.0, -1.0)).Message);
    }

    // ---- the map ----

    [TestMethod]
    public void TheMap_IsChartedFromTheReleaseChain_OneFluentChainPerPlace()
    {
        Assert.AreEqual(9, Int("g.PlaceCount()"));
        Assert.AreEqual(10, Int("g.PassageCount()"), "eight doors and two open boundaries");
        Assert.AreEqual("kitchen", Text("g.PlaceAt(2.0, 9.5).Name"));
        Assert.AreEqual("center", Text("g.PlaceAt(5.5, 5.5).Name"));
        Assert.AreEqual("west", Text("g.PlaceAt(0.75, 5.5).Name"));
        Assert.IsFalse(Bool("g.IsOnMap(3.0, 5.0)"), "the left block is solid: nowhere on the map");
        Assert.IsFalse(Bool("g.IsOnMap(11.5, 5.0)"), "beyond the east wall");
    }

    [TestMethod]
    public void TheMap_IsReadAsObjects_EachPlaceWithItsDoorsAndOpenings()
    {
        // the very query the panel runs: the golem's places, walked and printed, each with what it holds
        string json = perf.Actor.Using(@"
            foreach (places in g.Places()) {
                print places.Name 'name', places.X 'x', places.Y 'y', places.Width 'w', places.Height 'h', places.Center.X 'cx', places.Center.Y 'cy';
                foreach (doors in places.Doorways()) { print doors.To 'to', doors.At.X 'x', doors.At.Y 'y'; }
                foreach (opens in places.OpenSides()) { print opens.To 'to'; }
            }
        ").PerformQuery();

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var places = doc.RootElement.GetProperty("places");
        Assert.AreEqual(9, places.GetArrayLength(), "the engine renders each foreach as an array named after its variable");

        var kitchen = places[0];
        Assert.AreEqual("kitchen", kitchen.GetProperty("name").GetString());
        Assert.AreEqual(2.0, kitchen.GetProperty("cx").GetDouble(), 0.001, "a property chain: places.Center.X");
        var doors = kitchen.GetProperty("doors");
        Assert.AreEqual(2, doors.GetArrayLength(), "nested foreach: the place's doors, as the place sees them");
        Assert.AreEqual("north", doors[0].GetProperty("to").GetString());
        Assert.AreEqual(4.0, doors[0].GetProperty("x").GetDouble(), 0.001);
        Assert.IsFalse(kitchen.TryGetProperty("opens", out _), "a place with no open boundary simply lacks the key");

        Assert.AreEqual("center", places[1].GetProperty("opens")[0].GetProperty("to").GetString(), "north opens to the center");
        Assert.AreEqual("east", places[5].GetProperty("name").GetString());
        // what the bodies touched is NOT the map's to answer: it is the obstacles module, read flat with its zone

    }

    [TestMethod]
    public void TheMap_KnowsItsCornersWallsAndJambs_AsObjects()
    {
        // corners are locations (a Location IS a Position: X and Y are inherited, the engine binds them the same);
        // walls are segments (Length inherited too), each knowing the doors that pierce it; a door has two jambs
        string json = perf.Actor.Using(@"
            foreach (places in g.Places()) {
                print places.Name 'name';
                foreach (corners in places.Corners()) { print corners.Label 'label', corners.X 'x', corners.Y 'y'; }
                foreach (walls in places.Walls()) {
                    print walls.From.X 'x0', walls.From.Y 'y0', walls.To.X 'x1', walls.To.Y 'y1', walls.Length 'len', walls.Thickness 't';
                    foreach (doors in walls.Doors()) {
                        print doors.Name 'name', doors.At.X 'x', doors.At.Y 'y', doors.Width 'w', doors.Height 'h';
                        foreach (jambs in doors.Jambs()) { print jambs.Label 'label', jambs.X 'x', jambs.Y 'y'; }
                    }
                }
            }
        ").PerformQuery();

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var places = doc.RootElement.GetProperty("places");
        var kitchen = places[0];
        var corners = kitchen.GetProperty("corners");
        Assert.AreEqual(4, corners.GetArrayLength(), "a rectangle: four corner locations");
        Assert.AreEqual("kitchen ne", corners[2].GetProperty("label").GetString());
        Assert.AreEqual(4.0, corners[2].GetProperty("x").GetDouble(), 0.001, "X is inherited from Position");
        Assert.AreEqual(11.0, corners[2].GetProperty("y").GetDouble(), 0.001);

        var walls = kitchen.GetProperty("walls");
        Assert.AreEqual(4, walls.GetArrayLength(), "the kitchen has no open boundary: four walls");
        var east = walls[3];                                                     // south, north, west, east
        Assert.AreEqual(4.0, east.GetProperty("x0").GetDouble(), 0.001);
        Assert.AreEqual(3.0, east.GetProperty("len").GetDouble(), 0.001, "Length is inherited from Segment");
        Assert.AreEqual(0.0, east.GetProperty("t").GetDouble(), 0.001, "a wall is a line today");
        var door = east.GetProperty("doors")[0];
        Assert.AreEqual("kitchen/north", door.GetProperty("name").GetString(), "the east wall holds the door to the north hall");
        Assert.AreEqual(1.4, door.GetProperty("w").GetDouble(), 0.001, "the gap the world leaves");
        Assert.AreEqual(0.5, door.GetProperty("h").GetDouble(), 0.001, "as tall as the plan");
        var jambs = door.GetProperty("jambs");
        Assert.AreEqual(2, jambs.GetArrayLength(), "a door is two locations on its wall");
        Assert.AreEqual(8.8, jambs[0].GetProperty("y").GetDouble(), 0.001);
        Assert.AreEqual(10.2, jambs[1].GetProperty("y").GetDouble(), 0.001);
        Assert.AreEqual(4.0, jambs[1].GetProperty("x").GetDouble(), 0.001, "both on the wall's line");

        var north = places[1];
        Assert.AreEqual(3, north.GetProperty("walls").GetArrayLength(), "the north hall's south edge is open to the center: three walls");
        Assert.AreEqual(0, places[4].GetProperty("walls").EnumerateArray().Count(w => Math.Abs(w.GetProperty("y0").GetDouble() - 3.0) < 0.001 && Math.Abs(w.GetProperty("y1").GetDouble() - 3.0) < 0.001),
            "the center's south edge is open too: no wall along y = 3");
    }

    [TestMethod]
    public void AnEvasion_IsAManeuver_ATrajectoryOfItsOwn()
    {
        // the body at the middle of the center hall, heading east (0 rad), touched something: step right, run ahead
        string json = perf.Actor.Using(@"
            foreach (legs in g.Evasion(@x, @y, @heading, 'step-right').Legs()) { print legs.Name 'leg', legs.At.X 'x', legs.At.Y 'y'; }
            foreach (back in g.Evasion(@x, @y, @heading, 'back-off').Legs()) { print back.Name 'leg', back.At.X 'x', back.At.Y 'y'; }
        ")
        .WithParameters(p => {
            p["x",       typeof(double)] = 5.5;
            p["y",       typeof(double)] = 5.5;
            p["heading", typeof(double)] = 0.0;
        })
        .PerformQuery();

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var legs = doc.RootElement.GetProperty("legs");
        Assert.AreEqual(2, legs.GetArrayLength(), "aside, then ahead");
        Assert.AreEqual("aside", legs[0].GetProperty("leg").GetString());
        Assert.AreEqual(5.5, legs[0].GetProperty("x").GetDouble(), 0.001);
        Assert.AreEqual(5.0, legs[0].GetProperty("y").GetDouble(), 0.001, "right of an eastward lane is south: half a metre");
        Assert.AreEqual("ahead", legs[1].GetProperty("leg").GetString());
        Assert.AreEqual(6.7, legs[1].GetProperty("x").GetDouble(), 0.001, "then 1.2 ahead in the new lane");
        Assert.AreEqual(5.0, legs[1].GetProperty("y").GetDouble(), 0.001);
        var back = doc.RootElement.GetProperty("back");
        Assert.AreEqual(1, back.GetArrayLength());
        Assert.AreEqual("back", back[0].GetProperty("leg").GetString());
        Assert.AreEqual(4.6, back[0].GetProperty("x").GetDouble(), 0.001, "0.9 back along the lane");

        Refuses("g.Evasion(5.5, 5.5, 0.0, 'teleport');", "no evasion strategy named");
    }

    [TestMethod]
    public void TheShortestRoad_CutsThroughTheCenter_WhenThatIsShorter()
    {
        // kitchen (2,9.5) -> door (4,9.5) -> the open boundary north~center at its middle (5.5,8) -> the open
        // boundary center~south, crossed where the walked run to the door's approach point (6.4,1.5) meets it
        // (x ~ 6.19) -> door (7,1.5) -> garage (9,1.5)
        double cross = 5.5 + (6.4 - 5.5) * (5.0 / 6.5);
        double throughTheCenter = 2.0 + Math.Sqrt(4.5) + Math.Sqrt((cross - 5.5) * (cross - 5.5) + 25)
                                  + Math.Sqrt((7 - cross) * (7 - cross) + 2.25) + 2.0;      // ~12.87
        double aroundByTheWest = 2 * Math.Sqrt(3.8125) + 5.0 + Math.Sqrt(12.8125) + 3.0 + 2.0; // ~15.5
        double road = Double("g.Distance(map.Find('kitchen'), map.Find('garage'))");
        Assert.AreEqual(throughTheCenter, road, 0.01);
        Assert.IsTrue(road < aroundByTheWest, "the ring is the long way round");
    }

    [TestMethod]
    public void TheShortestRoad_TakesTheCorridor_WhenThatIsShorter()
    {
        // kitchen (2,9.5) -> door (0.75,8) -> west corridor -> door (0.75,3) -> living (2,1.5)
        double byTheWestCorridor = 2 * Math.Sqrt(3.8125) + 5.0;                  // ~8.9
        double throughTheCenter = 2.0 + Math.Sqrt(4.5) + 5.0 + Math.Sqrt(4.5) + 2.0; // ~13.2
        double road = Double("g.Distance(map.Find('kitchen'), map.Find('living'))");
        Assert.AreEqual(byTheWestCorridor, road, 0.01);
        Assert.IsTrue(road < throughTheCenter, "for this pair the corridor beats the shortcut");
    }

    [TestMethod]
    public void TheGolem_TellsAKnownWallFromAnythingElse_ByItsMap()
    {
        // a point the body touched: on a boundary the map holds as a wall, or not
        Assert.IsTrue(Bool("g.KnowsWallAt(0.05, 5.5)"), "the corridor's outer wall");
        Assert.IsTrue(Bool("g.KnowsWallAt(4.0, 8.5)"), "the kitchen's east wall, by the door's jamb");
        Assert.IsFalse(Bool("g.KnowsWallAt(4.1, 9.2)"), "the kitchen/north doorway: no wall there, whatever was touched is something else");
        Assert.IsTrue(Bool("g.KnowsWallAt(4.05, 8.0)"), "the corner of the left block, met through the walls that end there");
        Assert.IsFalse(Bool("g.KnowsWallAt(10.25, 5.9)"), "the middle of the east corridor: whatever stands there is not on the map");
        Assert.IsFalse(Bool("g.KnowsWallAt(5.5, 8.0)"), "the open boundary north~center is not a wall");
        Assert.IsFalse(Bool("g.KnowsWallAt(5.5, 5.5)"), "the middle of the center hall");
    }

    // ---- entrusting: MoveTo, Cover, Follow ----

    [TestMethod]
    public void MoveTo_APoint_IsPending_AndIsTheOperators()
    {
        Visit(1, 2.0, 1.5);

        Assert.AreEqual(1, Int("g.Pending()"));
        Assert.IsTrue(Bool("g.IsPending(1)"));
        Assert.AreEqual(2.0, Double("g.OrderX()"));
        Assert.IsFalse(Bool("g.IsFollowing(1)"), "the operator ordered it");
        Assert.AreEqual(1, Int("g.StopsLeft(1)"));
    }

    [TestMethod]
    public void MoveTo_APlace_HeadsForItsCenter()
    {
        Visit(1, "storage");

        Assert.AreEqual(9.0, Double("g.OrderX()"), 0.001);
        Assert.AreEqual(9.5, Double("g.OrderY()"), 0.001);
        Refuses("g.Visit(2, map.Find('attic'));", "unknown area 'attic'");
    }

    [TestMethod]
    public void MoveTo_SeveralStops_WalksThemInTheGivenOrder()
    {
        Visit(1, new[] { "garage", "kitchen", "storage" });
        Assert.AreEqual(3, Int("g.StopsLeft(1)"));

        // from the living room: garage first, back out through the same door, the center shortcut to the kitchen, then the storage
        string plan = Text("g.Plan(1, 2.0, 1.5)");
        StringAssert.StartsWith(plan, "living/south@4,1.5 > south/garage@7,1.5 > garage@9,1.5 > south/garage@7,1.5 > ");
        StringAssert.Contains(plan, "> kitchen@2,9.5 > kitchen/north@4,9.5 > north/storage@7,9.5 > storage@9,9.5");
        Assert.IsTrue(plan.IndexOf("garage@9,1.5") < plan.IndexOf("kitchen@2,9.5") && plan.IndexOf("kitchen@2,9.5") < plan.IndexOf("storage@9,9.5"), "the order is the operator's");
    }

    [TestMethod]
    public void Cover_SeveralStops_LetsTheGolemChooseTheShortestOrder()
    {
        Cover(1, new[] { "garage", "kitchen", "storage" });

        // from the living room the shortest round is garage (7), then storage by the east corridor (8.9), then kitchen (7);
        // the order given would cost 7 + 12.9 + 7
        string plan = Text("g.Plan(1, 2.0, 1.5)");
        Assert.IsTrue(plan.IndexOf("garage@9,1.5") < plan.IndexOf("storage@9,9.5") && plan.IndexOf("storage@9,9.5") < plan.IndexOf("kitchen@2,9.5"),
            "garage, storage, kitchen — not the order given: " + plan);
        StringAssert.EndsWith(plan, "> kitchen@2,9.5");
    }

    [TestMethod]
    public void AStop_IsAPlaceOrAPointOnTheMap_AnythingElseIsRefused()
    {
        Visit(1, new[] { "kitchen", "9,8" });
        Assert.AreEqual(2, Int("g.StopsLeft(1)"));

        Refuses("g.Visit(2, map.Find('attic'));", "unknown area 'attic'");
        Refuses("g.Visit(2, Position(3.0, 5.0));", "nowhere on the map");
        Assert.IsTrue(Bool("g.KnowsPlace('kitchen')"));
        Assert.IsFalse(Bool("g.KnowsPlace('attic')"));
        Assert.IsTrue(Bool("g.IsOnMap(9.0, 8.0)"), "a point in the north hall");
        Assert.AreEqual(1, Int("g.Total()"));
    }

    [TestMethod]
    public void Follow_TakesAPointAPeerReached_WithAHandleOfItsOwn()
    {
        Visit(1, 2.0, 1.5);

        Follow(8.5, 2.5);

        Assert.AreEqual(2, Int("g.Total()"));
        Assert.IsTrue(Bool("g.Knows(2)"), "the followed point got handle 2");
        Assert.IsTrue(Bool("g.IsFollowing(2)"), "a Follow mission remembers who it came from");
        Assert.AreEqual(3, Int("g.NextHandle()"));
    }

    // ---- the road: Route, Cross, Reach ----

    [TestMethod]
    public void APlan_ReadsLikeTheJournalWillWriteIt_AndNamesEveryPassageAndStop()
    {
        Visit(1, 2.0, 1.5);   // the living room
        Assert.AreEqual("kitchen/west@0.75,8 > west/living@0.75,3 > living@2,1.5", Text("g.Plan(1, 2.0, 9.5)"));

        Visit(2, 9.0, 1.5);   // the garage
        Abandon(1, "the test moves on");
        string plan = Text("g.Plan(2, 2.0, 9.5)");
        StringAssert.StartsWith(plan, "kitchen/north@4,9.5 > ");
        StringAssert.Contains(plan, "north~center@");
        StringAssert.Contains(plan, "center~south@");
        StringAssert.EndsWith(plan, "> south/garage@7,1.5 > garage@9,1.5");
    }

    [TestMethod]
    public void RoutingCrossingAndReaching_AdvanceTheLegs_AndTheLastStopCompletes()
    {
        Visit(1, 2.0, 1.5);
        Route(1, Text("g.Plan(1, 2.0, 9.5)"));

        Assert.IsTrue(Bool("g.IsRouted(1)"));
        Assert.AreEqual(3, Int("g.LegsLeft(1)"));
        Assert.AreEqual("kitchen/west", Text("g.OrderPassage(1)"));
        Assert.IsFalse(Bool("g.OrderIsStop(1)"));
        Assert.AreEqual(0.75, Double("g.OrderX()"), 0.001, "the body heads to the first door, not to the stop");

        Cross(1, "kitchen/west");
        Assert.AreEqual(2, Int("g.LegsLeft(1)"));
        Assert.AreEqual(3.0, Double("g.OrderY()"), 0.001);
        Refuses("g.Cross(1, map.FindDoor('kitchen', 'west'));", "is heading to 'west/living'");
        Refuses("g.Reach(1, 0.75, 3.0);", "cross it, no stop is next");

        Cross(1, "west/living");
        Assert.AreEqual(1, Int("g.LegsLeft(1)"));
        Assert.IsTrue(Bool("g.OrderIsStop(1)"));
        Assert.AreEqual("living", Text("g.OrderPassage(1)"), "the last leg is the stop, named by its place");
        Refuses("g.Cross(1, map.FindDoor('west', 'living'));", "a stop, not a passage");
        Refuses("g.Reach(1, 2.0, 2.0);", "next stop is (2, 1.5)");

        Reach(1, 2.0, 1.5);
        Assert.AreEqual("completed", Text("g.StatusOf(1)"), "reaching the last stop completes the mission");
        Assert.IsFalse(Bool("g.IsPending(1)"));
        Assert.AreEqual(0, Int("g.Pending()"));
        Refuses("g.Reach(1, 2.0, 1.5);", "already completed");
    }

    [TestMethod]
    public void AMissionWithSeveralStops_ReachesEachInTurn_AndCompletesOnTheLast()
    {
        Visit(1, new[] { "north", "south" });
        Route(1, Text("g.Plan(1, 5.5, 5.5)"));   // from the center: up to the north hall, back down through the center to the south hall

        Assert.AreEqual(2, Int("g.StopsLeft(1)"));
        Cross(1, "north~center");
        Reach(1, 5.5, 9.5);
        Assert.AreEqual("pending", Text("g.StatusOf(1)"), "one stop reached, one to go");
        Assert.AreEqual(1, Int("g.StopsLeft(1)"));

        Cross(1, "north~center");
        Cross(1, "center~south");
        Reach(1, 5.5, 1.5);
        Assert.AreEqual("completed", Text("g.StatusOf(1)"));
        Assert.AreEqual(0, Int("g.StopsLeft(1)"));
    }

    [TestMethod]
    public void ADoor_IsCrossedStraight_LiningUpOffTheWallOnBothSides()
    {
        // kitchen (2,9.5) -> door kitchen/west at (0.75,8) on a horizontal wall -> west corridor -> door (0.75,3) -> living
        Visit(1, 2.0, 1.5);
        Route(1, Text("g.Plan(1, 2.0, 9.5)"));

        Assert.AreEqual(0.75, Double("g.OrderX()"), 0.001, "the leg IS the door");
        Assert.AreEqual(8.0, Double("g.OrderY()"), 0.001);
        Assert.AreEqual(0.75, Double("g.OrderApproachX()"), 0.001, "the approach stands right in front of the door");
        Assert.AreEqual(8.6, Double("g.OrderApproachY()"), 0.001, "0.6 into the kitchen, the side the body comes from");
        Assert.AreEqual(0.75, Double("g.OrderExitX()"), 0.001);
        Assert.AreEqual(7.4, Double("g.OrderExitY()"), 0.001, "0.6 into the corridor, the side it goes to");

        Cross(1, "kitchen/west");
        Assert.AreEqual(3.6, Double("g.OrderApproachY()"), 0.001, "the next door, west/living at y=3, is approached from the corridor");
        Assert.AreEqual(2.4, Double("g.OrderExitY()"), 0.001);

        Cross(1, "west/living");
        Assert.AreEqual(2.0, Double("g.OrderApproachX()"), 0.001, "a stop has no wall to clear: approach, exit and point coincide");
        Assert.AreEqual(1.5, Double("g.OrderExitY()"), 0.001);
    }

    // ---- the ending: Fail, Abandon, Announce ----

    [TestMethod]
    public void AFailedMission_KeepsItsReason_AndIsNoLongerPending()
    {
        Visit(1, 2.0, 1.5);

        perf.Actor.Using(@"
            g.Fail(@id, @reason);
        ")
        .WithParameters(p => {
            p["id",     typeof(int)]    = 1;
            p["reason", typeof(string)] = "collided with crate at (10.3, 5.9): nothing on my map there";
        })
        .PerformCommand();

        Assert.IsFalse(Bool("g.IsPending(1)"));
        Assert.IsFalse(Bool("g.HasPendingMission()"));
        Assert.AreEqual("failed", Text("g.StatusOf(1)"));
        Refuses("g.Fail(1, 'again');", "already failed");
    }

    [TestMethod]
    public void AStaleFollowedPoint_IsAbandonedForANewerOne_AndTheOperatorsPointNeverIs()
    {
        Visit(1, 9.0, 1.5);                 // the operator: garage
        Follow(2.0, 9.5);                    // the leader was in the kitchen...
        Assert.IsFalse(Bool("g.HasNewerFollowing(2)"));

        Follow(9.0, 9.5);                    // ...and now says it is in the storage
        Assert.IsTrue(Bool("g.HasNewerFollowing(2)"));
        Assert.AreEqual(3, Int("g.NewestFollowingId()"));
        Assert.IsFalse(Bool("g.HasNewerFollowing(1)"), "the operator's mission is never superseded by the loop's rule");

        Abandon(2, "superseded by mission 3");

        Assert.AreEqual("abandoned", Text("g.StatusOf(2)"));
        Assert.IsFalse(Bool("g.IsPending(2)"));
        Assert.AreEqual(2, Int("g.Pending()"), "the operator's garage and the newest followed point remain");
        Assert.AreEqual(1, Int("g.FollowingCount()"));
    }

    [TestMethod]
    public void LettingGo_AbandonsEveryPendingMissionInOneCommand_ButNeverReusesAHandle()
    {
        Visit(1, 2.0, 1.5);
        Visit(2, 9.0, 1.5);
        long entriesBefore = perf.CurrentEntryId;

        perf.Actor.Using(@"
            foreach (id in g.PendingIds()) {
                g.Abandon(id, @reason);
            }
        ")
        .WithParameters(p => {
            p["reason", typeof(string)] = "the operator let go of everything";
        })
        .PerformCommand();

        Assert.IsTrue(perf.CurrentEntryId - entriesBefore <= 2,
            "one action for all the missions (plus the template of a shape seen for the first time), never one entry per mission");
        Assert.AreEqual(0, Int("g.Pending()"));
        Assert.AreEqual(2, Int("g.Total()"), "nothing is erased: the missions stay, abandoned");
        Assert.AreEqual("abandoned", Text("g.StatusOf(1)"));
        Assert.AreEqual(3, Int("g.NextHandle()"), "a spent handle is never minted again: the idempotency keys hang on it");
        Refuses("g.Abandon(1, 'again');", "already abandoned");
    }

    [TestMethod]
    public void AbandoningNeedsAReason()
    {
        Visit(1, 2.0, 1.5);
        Refuses("g.Abandon(1, '');", "needs a reason");
    }

    [TestMethod]
    public void AReachedStop_CanBeAnnounced_AndAMissionWithoutOneCannot()
    {
        Visit(1, 2.0, 9.5);
        Route(1, Text("g.Plan(1, 2.0, 9.5)"));   // already there: the stop alone
        Reach(1, 2.0, 9.5);
        Visit(2, 9.0, 1.5);

        perf.Actor.Using(@"
            g.Announce(@id);
        ")
        .WithParameters(p => {
            p["id", typeof(int)] = 1;
        })
        .PerformCommand();

        Assert.IsTrue(Bool("g.WasAnnounced(1)"));
        Assert.IsFalse(Bool("g.WasAnnounced(2)"));
        Refuses("g.Announce(2);", "only a reached stop is announced");
    }

    // ---- the road ahead ----

    [TestMethod]
    public void TheRoadLeft_RunsThroughEveryStopAhead_AlongThePassages_AndTheTimeCountsTheLingers()
    {
        Visit(1, 5.5, 9.5);       // north
        Follow(5.5, 5.5);          // center, a followed point: one linger
        Visit(3, 5.5, 1.5);       // south
        long entriesBefore = perf.CurrentEntryId;

        double route = 4.0 + 4.0;                       // north -> center -> south, straight across the open boundaries
        double firstLeg = 2.0 + 1.5;                    // from the kitchen center: door (4,9.5), then the north center

        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @route = g.RouteLength();
            @left  = g.DistanceLeft(@x, @y);
            @eta   = g.SecondsLeft(@x, @y);
        ")
        .WithParameters(rented, p => {
            p["x", typeof(double)]                      = 2.0;
            p["y", typeof(double)]                      = 9.5;
            p[Parameter.Out, "route", typeof(double)]   = default;
            p[Parameter.Out, "left",  typeof(double)]   = default;
            p[Parameter.Out, "eta",   typeof(double)]   = default;
        })
        .PerformQuery();

        Assert.AreEqual(route, rented["route"].GetValue<double>(), 0.01, "stop to stop through the passages");
        Assert.AreEqual(firstLeg + route, rented["left"].GetValue<double>(), 0.01, "plus the way from where the body stands");
        Assert.AreEqual((firstLeg + route) / 2.0 + 6.0, rented["eta"].GetValue<double>(), 0.01, "at the body's 2 units/s, plus its 6 s linger at the followed stop");
        Assert.AreEqual(entriesBefore, perf.CurrentEntryId, "a query leaves no entry: the pose never touches the journal");
    }

    [TestMethod]
    public void TheRoute_IsAnsweredWithNoParameters_AtTheBodysOwnSpeedAndLingers()
    {
        Visit(1, 5.5, 9.5);
        Follow(5.5, 5.5);
        Visit(3, 5.5, 1.5);

        Assert.AreEqual(8.0, Double("g.RouteLength()"), 0.01);
        Assert.AreEqual(8.0 / 2.0 + 6.0, Double("g.RouteSeconds()"), 0.01);
    }

    [TestMethod]
    public void WithNothingPending_TheRoadLeftIsZero()
    {
        Visit(1, 9.0, 1.5);
        Abandon(1, "the test is over");

        Assert.AreEqual(0.0, Double("g.DistanceLeft(2.0, 9.5)"), 0.001);
    }

    // ---- marks: what bodies touched that the plan does not hold ----

    [TestMethod]
    public void ABump_IsATouchToBeSettled_AndAMarkIsTheConclusion()
    {
        Visit(1, 9.0, 1.5);                       // the garage
        Route(1, Text("g.Plan(1, 9.0, 9.5)"));      // from the storage: down the east corridor
        Assert.AreEqual(0, Int("g.MarkCount()"));
        Assert.IsTrue(Bool("g.FitsAt(10.25, 5.5)"), "the corridor is clear as far as the map knows");

        Bump(1, 10.25, 5.85, South);                // the crate's face — or a peer: not known yet
        Assert.AreEqual(0, Int("g.MarkCount()"), "a bump is a touch, not yet a mark");
        Assert.AreEqual(1, Int("g.Bumps(1)"));
        Assert.IsTrue(Bool("g.HasBumpedSinceRoute(1)"));
        Assert.AreEqual("", Text("g.HeardBumpNear(10.25, 5.85, 0)"), "no peer said it bumped there");

        Assert.AreEqual(1, Int("g.Mark(10.25, 5.85, -1.5708)"), "nobody else bumped: the golem marks it, with the heading of the touch as the mark's normal");
        Assert.IsFalse(Bool("g.FitsAt(10.25, 5.5)"), "the body no longer fits where the mark reaches");
        Assert.IsTrue(Bool("g.HasRoomAt(10.25, 5.5)"), "the walls alone still leave room there: marks are what the body feels around");
        Assert.IsFalse(Bool("g.HasRoomAt(9.7, 5.5)"), "too close to the corridor's wall for the body");

        Assert.AreEqual(1, Int("g.LearnMark(10.25, 5.9, -1.5708)"), "a peer's touch within a tenth of a unit is the same mark");
        Assert.AreEqual(2, Int("g.LearnMark(10.3, 6.6, -1.5708)"), "a peer's touch further along is another mark");
    }

    [TestMethod]
    public void APeersBumpHeardThereAndThen_NamesWhoWasMet()
    {
        Visit(1, 9.0, 1.5);
        Route(1, Text("g.Plan(1, 9.0, 9.5)"));
        Assert.AreEqual(1, Int("g.HearBump('blue', 4.6, 9.5, 5.1, 9.5)"), "blue says it bumped in the kitchen's doorway, standing just past it");
        int heard = Int("g.HeardBumpCount()");

        Bump(1, 4.7, 9.5, East);                    // my own touch, right there
        Assert.AreEqual("blue", Text("g.HeardBumpNear(4.7, 9.5, 0)"), "blue's bump is within a body's diameter of mine: it was blue");
        Assert.AreEqual("", Text("g.HeardBumpNear(4.7, 9.5, " + heard + ")"), "nothing heard after that count");
        Assert.AreEqual("", Text("g.HeardBumpNear(10.25, 5.85, 0)"), "a bump far away is somebody else's business");
        Assert.AreEqual(0, Int("g.MarkCount()"), "meeting a body leaves no mark");

        Assert.AreEqual(1, Int("g.Bump(4.9, 9.5, 0.0)"), "a body standing idle that gets touched bumps too, without a mission");
        Assert.AreEqual(2, Int("g.Bump(4.9, 9.5, 0.0)"));
        Refuses("g.HearBump('', 1.0, 1.0, 1.0, 1.0);", "needs to say who");
    }

    [TestMethod]
    public void MarksCloseTogether_AreJoinedIntoOneObstacle_WhoseVerticesOutlineIt()
    {
        LearnMark(10.25, 5.85, South);    // the crate's north face
        LearnMark(10.25, 5.15, North);    // its south face
        LearnMark(9.9, 5.5, East);        // its west face
        LearnMark(8.0, 9.5, East);        // something else, far away in the storage room
        Assert.AreEqual(4, Int("g.MarkCount()"));
        Assert.AreEqual(2, Int("g.ObstacleCount()"), "three touches on one thing, one on another");

        string json = perf.Actor.Using(@"
            foreach (obstacles in g.Obstacles()) {
                print obstacles.Where 'zone', obstacles.Size 'size', obstacles.Shape 'shape', obstacles.Center.X 'cx';
                foreach (vertices in obstacles.Vertices()) { print vertices.At.X 'x', vertices.At.Y 'y'; }
            }
        ").PerformQuery();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var list = doc.RootElement.GetProperty("obstacles");
        Assert.AreEqual(2, list.GetArrayLength());
        var crate = list[0];
        Assert.AreEqual("east", crate.GetProperty("zone").GetString(), "the crate stands in the east corridor");
        Assert.AreEqual(3, crate.GetProperty("size").GetInt32(), "the three touches outline one obstacle");
        Assert.AreEqual(3, crate.GetProperty("vertices").GetArrayLength());
        Assert.AreEqual(10.13, crate.GetProperty("cx").GetDouble(), 0.01, "centred among its vertices");
        Assert.AreEqual("storage", list[1].GetProperty("zone").GetString());
        Assert.AreEqual("point", list[1].GetProperty("shape").GetString(), "one touch is a point, not a figure yet");
    }

    [TestMethod]
    public void AMarkInACorridorTooNarrowForTheBody_ClosesIt_AndTheRoadGoesRound()
    {
        Visit(1, 9.0, 1.5);                        // the garage, from the storage center
        string before = Text("g.Plan(1, 9.0, 9.5)");
        StringAssert.Contains(before, "storage/east@10.25,8 > east/garage@10.25,3", "the east corridor is the shortest road");

        LearnMark(10.25, 5.85, South);                   // a peer bumped into something in the middle of the corridor

        string after = Text("g.Plan(1, 9.0, 9.5)");
        Assert.IsFalse(after.Contains("east/garage"), "between the mark and the corridor's walls the body does not fit: " + after);
        StringAssert.Contains(after, "north~center@");
        StringAssert.EndsWith(after, "> south/garage@7,1.5 > garage@9,1.5");
    }

    [TestMethod]
    public void AMarkInAWideRoom_IsSkirted_WithAroundLegs()
    {
        Visit(1, 10.2, 9.5);                        // across the storage room
        LearnMark(9.0, 9.5, East);                       // something in the middle of it, touched heading east

        string plan = Text("g.Plan(1, 7.6, 9.5)");
        StringAssert.Contains(plan, "around@", "the body skirts the mark: " + plan);
        StringAssert.EndsWith(plan, "> storage@10.2,9.5");

        Route(1, plan);
        Assert.IsFalse(Bool("g.OrderIsStop(1)"));
        Assert.AreEqual("around", Text("g.OrderPassage(1)"));
        Pass(1, plan, "around");
        Assert.IsTrue(Int("g.LegsLeft(1)") >= 1);
    }

    [TestMethod]
    public void AfterABump_TheRoadIsDecidedAgain_AndOnlyThen()
    {
        Visit(1, 9.0, 1.5);
        Route(1, Text("g.Plan(1, 9.0, 9.5)"));
        Refuses("g.Route(1);", "already has its road");

        Bump(1, 10.25, 5.85, South);                 // the crate, met halfway down the corridor
        Mark(10.25, 5.85, South);                    // nobody else bumped: an obstacle
        string again = Text("g.Plan(1, 10.25, 6.3)");   // from where the body backed off to
        Assert.IsFalse(again.Contains("east/garage"), "the corridor is closed by the mark: " + again);
        StringAssert.Contains(again, "storage/east@10.25,8", "back out the way it came");
        Route(1, again);
        Assert.IsFalse(Bool("g.HasBumpedSinceRoute(1)"));
        Assert.AreEqual(1, Int("g.StopsLeft(1)"));
        Assert.AreEqual("pending", Text("g.StatusOf(1)"));
    }

    [TestMethod]
    public void ABodyStandingAmongMarks_CanStillLeave_ButNotThroughThem()
    {
        Visit(1, 9.0, 1.5);                        // the garage
        LearnMark(7.6, 9.3, South);                      // two touches on something right beside the body...
        LearnMark(8.2, 9.3, South);
        string plan = Text("g.Plan(1, 7.9, 9.55)");  // ...which stands between them, in the storage room
        StringAssert.EndsWith(plan, "> garage@9,1.5", "a road out exists: " + plan);

        LearnMark(10.25, 5.85, South);                   // the crate closes the corridor too
        // a body 0.15 north of that mark (it just backed off the crate; the touch was estimated a little short) leaves
        // the way it came and takes the long way round — never straight down through the mark (the 8-sep live run)
        string outNorth = Text("g.Plan(1, 10.25, 6.0)");
        StringAssert.StartsWith(outNorth, "storage/east@10.25,8 > ", "back out through the door it came in by: " + outNorth);
        Assert.IsFalse(outNorth.Contains("east/garage"), "not through the crate: " + outNorth);
    }

    [TestMethod]
    public void WhenNoRoadFits_TheProgressReadsStillAnswer_AsTheCrowFlies()
    {
        Visit(1, 2.0, 9.5);                          // the kitchen, from the storage
        Route(1, Text("g.Plan(1, 9.0, 9.5)"));
        LearnMark(4.0, 9.2, East);                        // marks close the kitchen/north doorway...
        LearnMark(4.0, 9.8, East);
        LearnMark(0.75, 8.2, South);                      // ...and the kitchen/west one
        LearnMark(0.75, 7.8, South);
        Refuses("g.Plan(1, 9.0, 9.5);", "no road");
        Assert.AreEqual(7.0, Double("g.DistanceLeft(9.0, 9.5)"), 0.001, "straight from (9, 9.5) to (2, 9.5): a read never refuses");
        Assert.IsTrue(Double("g.SecondsLeft(9.0, 9.5)") > 0);
    }

    [TestMethod]
    public void WhenNoRoadFitsTheBody_ThePlanSaysSo()
    {
        Visit(1, 9.0, 1.5);                        // the garage has two doors
        LearnMark(7.0, 1.5, East);                       // something in the south/garage door
        LearnMark(10.25, 3.0, North);                    // and something in the east/garage door
        Refuses("g.Plan(1, 2.0, 1.5);", "fits a body of radius 0.25 past 2 marks");
    }



    // ---- the order: the golem tells the host where to drive, one point at a time ----

    [TestMethod]
    public void TheOrder_IsThePointTheRoadHoldsNext_AndSkippingOneIsRefused()
    {
        Visit(1, 2.0, 1.5);                       // the living room, from the kitchen
        Route(1, Text("g.Plan(1, 2.0, 9.5)"));    // kitchen/west > west/living > living
        Assert.IsTrue(Bool("g.HasNextPoint(1)"));
        Assert.IsFalse(Bool("g.IsOrdered(1)"), "a road decided is not yet an order given");

        Refuses("g.MoveTo(1, Position(2.0, 1.5));", "heads to (0.75, 8), not to (2, 1.5)");   // the stop, skipping two doors

        Order(1, 0.75, 8.0);
        Assert.IsTrue(Bool("g.IsOrdered(1)"), "the host now knows where to drive");
        Order(1, 0.75, 8.0);                       // after a touch the same point may be ordered again
        Assert.IsTrue(Bool("g.IsOrdered(1)"));

        Cross(1, "kitchen/west");
        Assert.IsFalse(Bool("g.IsOrdered(1)"), "the order was carried out: the golem owes the next one");
        Assert.AreEqual(3.0, Double("g.OrderY()"), 0.001, "and the next point is the second door");
    }

    [TestMethod]
    public void AnErrandOneSegmentAway_IsWalkedWithoutDecidingARoad()
    {
        Visit(1, 2.0, 9.5);                        // a point of the kitchen, from the kitchen
        Assert.IsFalse(Bool("g.NeedsRoad(1, 3.0, 9.0)"), "same room, nothing in between: there is nothing to decide");
        Assert.IsFalse(Bool("g.IsRouted(1)"));
        Assert.IsTrue(Bool("g.HasNextPoint(1)"));

        Order(1, 2.0, 9.5);                        // the order goes straight out, with no Route
        Reach(1, 2.0, 9.5);
        Assert.AreEqual("completed", Text("g.StatusOf(1)"), "reaching the only stop completes it, road or no road");

        Visit(2, 9.0, 1.5);                        // the garage, from the kitchen: doors in between
        Assert.IsTrue(Bool("g.NeedsRoad(2, 2.0, 9.5)"), "another room: there is a road to decide");
    }

    [TestMethod]
    public void SeveralStopsWithoutARoad_AreWalkedOneOrderAtATime()
    {
        Visit(1, new[] { "2,9.5", "3,10.5" });     // two points of the kitchen
        Assert.AreEqual(2, Int("g.StopsLeft(1)"));

        Order(1, 2.0, 9.5);
        Reach(1, 2.0, 9.5);
        Assert.AreEqual("pending", Text("g.StatusOf(1)"), "one stop reached, one to go");
        Assert.IsFalse(Bool("g.IsOrdered(1)"));
        Assert.IsTrue(Bool("g.HasNextPoint(1)"));

        Order(1, 3.0, 10.5);
        Reach(1, 3.0, 10.5);
        Assert.AreEqual("completed", Text("g.StatusOf(1)"));
        Assert.IsFalse(Bool("g.HasNextPoint(1)"), "nothing left to head to: the pump stops here");
    }


    [TestMethod]
    public void ATouchVoidsTheStandingOrder_SoTheGolemMustSayAgainWhereItHeads()
    {
        Visit(1, 2.0, 1.5);
        Route(1, Text("g.Plan(1, 2.0, 9.5)"));
        Order(1, 0.75, 8.0);
        Assert.IsTrue(Bool("g.IsOrdered(1)"));

        Bump(1, 0.8, 8.2, South);
        Assert.IsFalse(Bool("g.IsOrdered(1)"), "what the body met voids the order: the conclusion hands it back");
        Order(1, 0.75, 8.0);                       // the conclusion re-issues the same point
        Assert.IsTrue(Bool("g.IsOrdered(1)"));

        Graze(1, 0.05, 8.5);
        Assert.IsFalse(Bool("g.IsOrdered(1)"), "a graze voids it too: retry or give up, and neither is the host's call");
        Assert.IsTrue(Bool("g.MayRetryLeg(1)"), "patience left: the order comes back");

        Graze(1, 0.05, 8.4);
        Graze(1, 0.05, 8.6);
        Assert.IsFalse(Bool("g.MayRetryLeg(1)"), "patience spent: no order comes back, the mission ends");
        Assert.IsFalse(Bool("g.IsOrdered(1)"));
    }


    [TestMethod]
    public void MeetingAPeer_TheRoadStepsOutOfItsWay_AndTheStepIsALegLikeAnyOther()
    {
        // red drives east across the kitchen toward the north hall; blue tells it bumped in the doorway and
        // stands just past it. Knowing where blue is, red's road starts by stepping out of the way.
        Visit(1, "north");
        Int("g.HearBump('blue', 4.6, 9.5, 5.1, 9.5)");

        string road = Text("g.PlanPast(1, 'blue', 4.6, 9.5, 0.0)");
        StringAssert.StartsWith(road, "aside@", "the first leg is the courtesy step: " + road);
        StringAssert.EndsWith(road, "> north@5.5,9.5", "and then the errand goes on");

        // to its own right, facing east, is south — and away from blue, who stands ahead
        var step = road.Split('>')[0].Trim();
        Assert.AreEqual("aside@4.6,9", step, "a body's width to its right, off the line it was on");

        // the step is a leg to cross, never a stop: reaching it must not complete the errand
        Route(1, road);
        Assert.IsFalse(Bool("g.OrderIsStop(1)"));
        Assert.AreEqual("aside", Text("g.OrderPassage(1)"));
        Order(1, 4.6, 9.0);
        Pass(1, road, "aside");
        Assert.AreEqual("pending", Text("g.StatusOf(1)"), "stepping aside is not arriving");
        Assert.AreEqual(1, Int("g.StopsLeft(1)"));
    }

    [TestMethod]
    public void WithNowhereToStepAside_TheRoadIsThePlainOne()
    {
        Visit(1, "north");
        Int("g.HearBump('blue', 4.6, 9.5, 5.1, 9.5)");
        // hemmed in against the kitchen's north wall: neither side leaves room for the body
        string road = Text("g.PlanPast(1, 'blue', 2.0, 10.9, 1.5708)");
        Assert.IsFalse(road.Contains("aside@"), "no room to be polite: the plain road, and the meeting is settled by waiting: " + road);
    }


    [TestMethod]
    public void TheObstacles_AreReadAsAFlatList_EachWithItsZoneAndItsVertices()
    {
        LearnMark(10.25, 5.85, South);      // three touches on the crate of the east corridor
        LearnMark(10.25, 5.15, North);
        LearnMark(9.9, 5.5, East);
        LearnMark(2.0, 9.5, East);          // something else, a room away
        Met("blue", 4.7, 9.5);              // and a body met in the north hall

        // the very query the panel's table runs: one row per obstacle, one per vertex under it
        string json = perf.Actor.Using(@"
            print g.ObstacleCount() 'total';
            foreach (obstacles in g.Obstacles()) {
                print obstacles.Kind 'kind', obstacles.Where 'zone', obstacles.Shape 'shape', obstacles.Size 'size', obstacles.Who 'who';
                foreach (vertices in obstacles.Vertices()) { print vertices.At.X 'x', vertices.At.Y 'y', vertices.Heading 'normal'; }
            }
        ").PerformQuery();

        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.AreEqual(3, doc.RootElement.GetProperty("total").GetInt32(), "two things and one peer");
        var list = doc.RootElement.GetProperty("obstacles");
        Assert.AreEqual(3, list.GetArrayLength());

        var crate = list[0];
        Assert.AreEqual("thing", crate.GetProperty("kind").GetString());
        Assert.AreEqual("east", crate.GetProperty("zone").GetString(), "the flat list says the zone: no need to walk the places");
        Assert.AreEqual("polygon", crate.GetProperty("shape").GetString());
        Assert.AreEqual(3, crate.GetProperty("vertices").GetArrayLength(), "its three touches, each with the normal");
        var normals = crate.GetProperty("vertices").EnumerateArray().Select(v => v.GetProperty("normal").GetDouble()).ToList();
        Assert.IsTrue(normals.Any(n => Math.Abs(n - South) < 0.001) && normals.Any(n => Math.Abs(n - North) < 0.001),
            "each vertex keeps the normal of ITS touch: the faces are told apart, whatever order they are drawn in");

        var other = list[1];
        Assert.AreEqual("kitchen", other.GetProperty("zone").GetString(), "another obstacle, other vertices, another zone");
        Assert.AreEqual("point", other.GetProperty("shape").GetString());
        Assert.AreEqual(1, other.GetProperty("vertices").GetArrayLength());

        var peer = list[2];
        Assert.AreEqual("peer", peer.GetProperty("kind").GetString());
        Assert.AreEqual("blue", peer.GetProperty("who").GetString());
        Assert.AreEqual("north", peer.GetProperty("zone").GetString());
        Assert.IsFalse(peer.TryGetProperty("vertices", out _), "a peer outlines nothing: bodies move on");
    }


    [TestMethod]
    public void WhenSomethingIsTakenAway_TheGolemForgetsItWithEveryVertex_AndANewTouchIsANewObstacle()
    {
        LearnMark(10.25, 5.85, South);        // three touches on the crate of the east corridor
        LearnMark(10.25, 5.15, North);
        LearnMark(9.9, 5.5, East);
        LearnMark(2.0, 9.5, East);            // and something else, a room away
        Assert.AreEqual(2, Int("g.ObstacleCount()"));
        Assert.AreEqual(4, Int("g.MarkCount()"));
        Assert.IsFalse(Bool("g.FitsAt(10.25, 5.5)"), "the crate is in the way");

        Assert.IsTrue(Bool("g.KnowsObstacleAt(10.13, 5.5)"), "asked by its centre");
        Assert.IsTrue(Bool("g.KnowsObstacleAt(9.9, 5.5)"), "or by one of its vertices");
        Assert.IsFalse(Bool("g.KnowsObstacleAt(5.5, 5.5)"), "and nothing stands in the middle of the centre hall");

        Assert.AreEqual(3, Int("g.Forget(10.13, 5.5)"), "the three marks that outlined it go at once");
        Assert.AreEqual(1, Int("g.ObstacleCount()"), "the other obstacle is untouched");
        Assert.AreEqual(1, Int("g.MarkCount()"));
        Assert.IsTrue(Bool("g.FitsAt(10.25, 5.5)"), "a body may pass there again");
        Assert.AreEqual(0, Int("g.Forget(10.13, 5.5)"), "forgetting nothing drops nothing: it is not a refusal");

        // whatever is touched there next is a NEW obstacle, outlined by new marks
        LearnMark(10.25, 5.5, South);
        Assert.AreEqual(2, Int("g.ObstacleCount()"));
        Assert.AreEqual(2, Int("g.ThingCount()"), "the new thing stands on its own, with nothing of the old one");
        Assert.AreEqual(2, Int("g.MarkCount()"), "one mark of the new thing, one of the untouched one");
    }

    [TestMethod]
    public void ForgettingAnEncounter_DropsThatPeer_AndLeavesTheThings()
    {
        Met("blue", 4.7, 9.5);
        LearnMark(10.25, 5.85, South);
        Assert.AreEqual(2, Int("g.ObstacleCount()"));

        Assert.AreEqual(1, Int("g.Forget(4.7, 9.5)"), "the encounter is dropped");
        Assert.AreEqual(0, Int("g.MetCount()"));
        Assert.AreEqual(1, Int("g.ThingCount()"), "and the thing stays");
        Assert.AreEqual(1, Int("g.MarkCount()"));
    }

    // ---- the second touch protocol: the domain suspects, the golem concludes ----

    [TestMethod]
    public void WhatWasTouched_IsSuspectedByTheDomain_WallPeerOrThing()
    {
        // the domain reasons over its own facts: the map, and what the peers said since the leg began
        string json = perf.Actor.Using(@"
            print g.Suspect(0.05, 5.5, 3.1416, 0).Kind 'wall', g.Suspect(0.05, 5.5, 3.1416, 0).Conclusion 'wallVerb';
            print g.Suspect(10.25, 5.9, -1.5708, 0).Kind 'thing', g.Suspect(10.25, 5.9, -1.5708, 0).Conclusion 'thingVerb', g.Suspect(10.25, 5.9, -1.5708, 0).Who 'nobody';
        ").PerformQuery();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.AreEqual("wall", doc.RootElement.GetProperty("wall").GetString(), "the corridor's outer wall");
        Assert.AreEqual("Graze", doc.RootElement.GetProperty("wallVerb").GetString(), "a wall I know: I grazed it");
        Assert.AreEqual("thing", doc.RootElement.GetProperty("thing").GetString(), "nothing on the map, nobody heard");
        Assert.AreEqual("Mark", doc.RootElement.GetProperty("thingVerb").GetString());
        Assert.AreEqual("", doc.RootElement.GetProperty("nobody").GetString());

        Int("g.HearBump('blue', 4.6, 9.5, 5.1, 9.5)");
        string peer = perf.Actor.Using(@"
            print g.Suspect(4.7, 9.5, 0.0, 0).Kind 'kind', g.Suspect(4.7, 9.5, 0.0, 0).Who 'who', g.Suspect(4.7, 9.5, 0.0, 0).Conclusion 'verb';
            print g.Suspect(4.7, 9.5, 0.0, 1).Kind 'later';
        ").PerformQuery();
        using var doc2 = System.Text.Json.JsonDocument.Parse(peer);
        Assert.AreEqual("peer", doc2.RootElement.GetProperty("kind").GetString(), "blue bumped within a meeting's reach: it was blue");
        Assert.AreEqual("blue", doc2.RootElement.GetProperty("who").GetString());
        Assert.AreEqual("Met", doc2.RootElement.GetProperty("verb").GetString());
        Assert.AreEqual("thing", doc2.RootElement.GetProperty("later").GetString(), "heard before the leg began: old news, not this touch");
    }

    [TestMethod]
    public void GrazingAKnownWall_IsAFact_ThatSpendsTheGolemsPatienceOnTheLeg()
    {
        Visit(1, 2.0, 1.5);
        Route(1, Text("g.Plan(1, 2.0, 9.5)"));   // kitchen/west > west/living > living
        Assert.IsTrue(Bool("g.MayRetryLeg(1)"));

        Graze(1, 0.05, 8.5);
        Graze(1, 0.05, 8.4);
        Assert.AreEqual(2, Int("g.Grazes(1)"));
        Assert.IsTrue(Bool("g.MayRetryLeg(1)"), "two grazes: still patient");
        Graze(1, 0.05, 8.6);
        Assert.IsFalse(Bool("g.MayRetryLeg(1)"), "three grazes on one leg: patience spent, the mission is given up");
        Assert.AreEqual(0, Int("g.MarkCount()"), "a graze leaves no mark: the wall was known");

        Cross(1, "kitchen/west");
        Assert.IsTrue(Bool("g.MayRetryLeg(1)"), "a new leg, fresh patience");
        Assert.AreEqual(3, Int("g.Grazes(1)"), "but the mission remembers every graze");
        Refuses("g.Graze(2, 1.0, 1.0);", "unknown mission");
    }

    [TestMethod]
    public void MeetingAPeer_IsHistory_AmongTheObstacles_NeverGeometry()
    {
        Met("blue", 4.7, 9.5);
        LearnMark(10.25, 5.85, South);

        Assert.AreEqual(1, Int("g.MetCount()"));
        Assert.AreEqual(2, Int("g.ObstacleCount()"), "a thing and a peer");
        Assert.AreEqual(1, Int("g.ThingCount()"), "only the thing is planned around");
        Assert.IsTrue(Bool("g.FitsAt(4.7, 9.5)"), "the peer moved on: the body fits where it was met");

        string json = perf.Actor.Using(@"
            foreach (obstacles in g.Obstacles()) { print obstacles.Kind 'kind', obstacles.Where 'zone', obstacles.Who 'who', obstacles.Shape 'shape'; }
        ").PerformQuery();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var list = doc.RootElement.GetProperty("obstacles");
        var thing = list[0];
        Assert.AreEqual("thing", thing.GetProperty("kind").GetString(), "the things come first");
        Assert.AreEqual("east", thing.GetProperty("zone").GetString());
        Assert.AreEqual("", thing.GetProperty("who").GetString());
        var peer = list[1];
        Assert.AreEqual("peer", peer.GetProperty("kind").GetString(), "and the peers after them, as history");
        Assert.AreEqual("blue", peer.GetProperty("who").GetString());
        Assert.AreEqual("north", peer.GetProperty("zone").GetString(), "the encounter stands in the north hall");
        Assert.AreEqual("point", peer.GetProperty("shape").GetString());
        Refuses("g.Met('', 1.0, 1.0);", "has a name");
    }

    [TestMethod]
    public void ABodyStandingInsideItsOwnMark_CanStillLeave_MovingAwayFromIt()
    {
        // From the 8-sep live run: red grazed the crate's north-west corner with its side while heading south; the touch
        // was estimated head-on, so the mark (4.94, 5.76) fell 0.19 from where the body then stood (4.8, 5.63) — inside
        // its own radius. A body cannot be inside a thing: the mark forbids walking further in, not leaving.
        Visit(1, 9.0, 1.5);                                                    // the garage
        Mark(4.93672793175996, 5.75978159057928, -1.37257826652825);
        Mark(5.14806941278059, 5.65085610890956, 0.0681849256515386);         // the second touch, stepping left into the crate's west face

        string plan = Text("g.Plan(1, 4.8, 5.63)");
        StringAssert.EndsWith(plan, "> garage@9,1.5", "a road out exists: " + plan);
    }

    [TestMethod]
    public void AMarkKnowsItsNormal_TheSideTheBodyCameFromIsFree()
    {
        LearnMark(5.5, 5.85, South);   // the crate's north face, touched by a body heading south: the thing lies south of the point

        Assert.IsFalse(Bool("g.FitsAt(5.5, 5.5)"), "beyond the mark, along its normal: inside the thing");
        Assert.IsFalse(Bool("g.FitsAt(5.0, 5.85)"), "half a metre along the surface: within the mark's reach");
        Assert.IsTrue(Bool("g.FitsAt(4.85, 5.85)"), "0.65 along the surface: past the reach");
        Assert.IsTrue(Bool("g.FitsAt(5.5, 6.25)"), "0.4 back the way the body came: a radius and a margin clear of the surface — free (the old disc said no)");
        Assert.IsFalse(Bool("g.FitsAt(5.5, 6.1)"), "0.25 back: the body would touch the surface again");
    }

    // ---- helpers: the same perform shapes the host uses ----

    private void Bump(int id, double x, double y, double heading) =>
        perf.Actor.Using(@"
            g.Bump(@id, @x, @y, @heading);
        ")
        .WithParameters(p => {
            p["id",      typeof(int)]    = id;
            p["x",       typeof(double)] = x;
            p["y",       typeof(double)] = y;
            p["heading", typeof(double)] = heading;
        })
        .PerformCommand();

    private void Graze(int id, double x, double y) =>
        perf.Actor.Using(@"
            g.Graze(@id, @x, @y);
        ")
        .WithParameters(p => {
            p["id", typeof(int)]    = id;
            p["x",  typeof(double)] = x;
            p["y",  typeof(double)] = y;
        })
        .PerformCommand();

    private void Mark(double x, double y, double heading) =>
        perf.Actor.Using(@"
            g.Mark(@x, @y, @heading);
        ")
        .WithParameters(p => {
            p["x",       typeof(double)] = x;
            p["y",       typeof(double)] = y;
            p["heading", typeof(double)] = heading;
        })
        .PerformCommand();

    private void LearnMark(double x, double y, double heading) =>
        perf.Actor.Using(@"
            g.LearnMark(@x, @y, @heading);
        ")
        .WithParameters(p => {
            p["x",       typeof(double)] = x;
            p["y",       typeof(double)] = y;
            p["heading", typeof(double)] = heading;
        })
        .PerformCommand();

    private void Met(string who, double x, double y) =>
        perf.Actor.Using(@"
            g.Met(@who, @x, @y);
        ")
        .WithParameters(p => {
            p["who", typeof(string)] = who;
            p["x",   typeof(double)] = x;
            p["y",   typeof(double)] = y;
        })
        .PerformCommand();

    private void Order(int id, double x, double y) =>
        perf.Actor.Using(@"
            { point = Position(@x, @y); g.MoveTo(@id, point); }
        ")
        .WithParameters(p => {
            p["id", typeof(int)]    = id;
            p["x",  typeof(double)] = x;
            p["y",  typeof(double)] = y;
        })
        .PerformCommand();

    private void Visit(int id, double x, double y) =>
        perf.Actor.Using(@"
            { point = Position(@x, @y); g.Visit(@id, point); }
        ")
        .WithParameters(p => {
            p["id", typeof(int)]    = id;
            p["x",  typeof(double)] = x;
            p["y",  typeof(double)] = y;
        })
        .PerformCommand();

    private void Visit(int id, string place) =>
        perf.Actor.Using(@"
            { point = map.Find(@area); g.Visit(@id, point); }
        ")
        .WithParameters(p => {
            p["id",   typeof(int)]    = id;
            p["area", typeof(string)] = place;
        })
        .PerformCommand();

    private void Visit(int id, string[] stops) => Errand("Visit", id, stops);
    private void Cover(int id, string[] stops) => Errand("Cover", id, stops);

    // Several stops are several acts in ONE command: an area by its name, a point as a Position — no list of
    // strings encoding positions any more (Juan, 10-sep-2026: objects, one per statement).
    private void Errand(string verb, int id, string[] stops)
    {
        // one stop is `point`, several are `point1`, `point2`… — and so are their @params
        var script = new System.Text.StringBuilder("{\n");
        for (int i = 0; i < stops.Length; i++)
        {
            string n = stops.Length == 1 ? "" : (i + 1).ToString();
            script.Append(IsPoint(stops[i]) ? $"point{n} = Position(@x{n}, @y{n}); g.{verb}(@id, point{n});\n" : $"point{n} = map.Find(@area{n}); g.{verb}(@id, point{n});\n");
        }
        script.Append("}\n");
        perf.Actor.Using(script.ToString())
        .WithParameters(p => {
            p["id", typeof(int)] = id;
            for (int i = 0; i < stops.Length; i++)
            {
                string n = stops.Length == 1 ? "" : (i + 1).ToString();
                if (IsPoint(stops[i]))
                {
                    var xy = stops[i].Split(',');
                    p[$"x{n}", typeof(double)] = double.Parse(xy[0], CultureInfo.InvariantCulture);
                    p[$"y{n}", typeof(double)] = double.Parse(xy[1], CultureInfo.InvariantCulture);
                }
                else p[$"area{n}", typeof(string)] = stops[i];
            }
        })
        .PerformCommand();
    }

    private static bool IsPoint(string token)
    {
        var xy = token.Split(',');
        return xy.Length == 2
            && double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out _)
            && double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out _);
    }

    private void Follow(double x, double y) =>
        perf.Actor.Using(@"
            { point = Position(@x, @y); g.Follow(point); }
        ")
        .WithParameters(p => {
            p["x", typeof(double)] = x;
            p["y", typeof(double)] = y;
        })
        .PerformCommand();

    // The road is decided act by act, all in one command — Route, then a Via / Around / Aside per leg and a Stop
    // per stop — from the one-line text g.Plan renders (the test reads the plan as a human does, the journal never
    // holds that text).
    private void Route(int id, string plan)
    {
        var legs = plan.Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries);
        var script = new System.Text.StringBuilder("{\ng.Route(@id);\n");
        for (int i = 0; i < legs.Length; i++)
        {
            int n = i + 1;   // legs read from 1
            string name = legs[i][..legs[i].LastIndexOf('@')];
            script.Append(
                name == "around" ? $"around{n} = Position(@x{n}, @y{n}); g.Around(@id, around{n});\n"
                : name == "aside" ? $"aside{n} = Position(@x{n}, @y{n}); g.Aside(@id, aside{n});\n"
                : name.Contains('/') ? $"door{n} = map.FindDoor(@a{n}, @b{n}); at{n} = Position(@x{n}, @y{n}); g.Via(@id, door{n}, at{n});\n"
                : name.Contains('~') ? $"opening{n} = map.FindOpening(@a{n}, @b{n}); at{n} = Position(@x{n}, @y{n}); g.Via(@id, opening{n}, at{n});\n"
                : $"stop{n} = Position(@x{n}, @y{n}); g.Stop(@id, stop{n});\n");
        }
        script.Append("}\n");
        perf.Actor.Using(script.ToString())
        .WithParameters(p => {
            p["id", typeof(int)] = id;
            for (int i = 0; i < legs.Length; i++)
            {
                int n = i + 1;
                int at = legs[i].LastIndexOf('@');
                string name = legs[i][..at];
                var xy = legs[i][(at + 1)..].Split(',');
                p[$"x{n}", typeof(double)] = double.Parse(xy[0], CultureInfo.InvariantCulture);
                p[$"y{n}", typeof(double)] = double.Parse(xy[1], CultureInfo.InvariantCulture);
                char sep = name.Contains('/') ? '/' : name.Contains('~') ? '~' : ' ';
                if (sep != ' ')
                {
                    var ab = name.Split(sep);
                    p[$"a{n}", typeof(string)] = ab[0];
                    p[$"b{n}", typeof(string)] = ab[1];
                }
            }
        })
        .PerformCommand();
    }

    // A passage is an object of the map: the door or the opening between two areas.
    private void Cross(int id, string passage)
    {
        bool door = passage.Contains('/');
        var ab = passage.Split(door ? '/' : '~');
        perf.Actor.Using(door
            ? "{ door = map.FindDoor(@a, @b); g.Cross(@id, door); }"
            : "{ opening = map.FindOpening(@a, @b); g.Cross(@id, opening); }")
        .WithParameters(p => {
            p["id", typeof(int)]    = id;
            p["a",  typeof(string)] = ab[0];
            p["b",  typeof(string)] = ab[1];
        })
        .PerformCommand();
    }

    // Passing the first leg of a kind (around, aside) named in a plan text: a point, not a passage.
    private void Pass(int id, string plan, string kind)
    {
        var leg = plan.Split('>', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries).First(l => l.StartsWith(kind + "@"));
        var xy = leg[(leg.LastIndexOf('@') + 1)..].Split(',');
        perf.Actor.Using(@"
            { point = Position(@x, @y); g.Pass(@id, point); }
        ")
        .WithParameters(p => {
            p["id", typeof(int)]    = id;
            p["x",  typeof(double)] = double.Parse(xy[0], CultureInfo.InvariantCulture);
            p["y",  typeof(double)] = double.Parse(xy[1], CultureInfo.InvariantCulture);
        })
        .PerformCommand();
    }

    private void Reach(int id, double x, double y) =>
        perf.Actor.Using(@"
            g.Reach(@id, @x, @y);
        ")
        .WithParameters(p => {
            p["id", typeof(int)]    = id;
            p["x",  typeof(double)] = x;
            p["y",  typeof(double)] = y;
        })
        .PerformCommand();

    private void Abandon(int id, string reason) =>
        perf.Actor.Using(@"
            g.Abandon(@id, @reason);
        ")
        .WithParameters(p => {
            p["id",     typeof(int)]    = id;
            p["reason", typeof(string)] = reason;
        })
        .PerformCommand();

    // A command the domain must refuse: the DomainException message surfaces through the perform.
    private void Refuses(string command, string because)
    {
        try
        {
            perf.Actor.Using(command).PerformCommand();
            Assert.Fail($"'{command}' should have been refused ({because})");
        }
        catch (AssertFailedException) { throw; }
        catch (Exception ex)
        {
            StringAssert.Contains(ex.ToString(), because);
        }
    }

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
