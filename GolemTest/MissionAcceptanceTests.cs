using System.Globalization;
using System.Linq;
using Choreography.Theater;
using GolemDomain;
using GolemDomain.Units;
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
        "upgrade('body_v1') { radius = Meters(0.25); speed = MetersPerSecond(2.0); linger = Seconds(6.0); retreat = Meters(0.6); body = Body(radius, speed, linger, retreat); }\n"
        + Catalog.Warehouse().AsRelease()
        + "upgrade('init') { collisions = Collisions(map); g = Golem(body, map, collisions); }\n";

    // ---- the body ----

    [TestMethod]
    public void TheBody_IsReleasedIntoTheJournal_SizeSpeedAndLinger()
    {
        Assert.AreEqual(0.25, Double("body.Radius.InMeters"), 0.001, "the body's size");
        Assert.AreEqual(2.0, Double("body.Speed.InMetersPerSecond"), 0.001, "its cruise speed");
        Assert.AreEqual(6.0, Double("body.LingerAfterTold.InSeconds"), 0.001, "its linger at a told stop");
        // A constructor's refusal reaches the DSL as "Error while instantiating class 'Body'": the engine wraps the
        // domain's exception and its reason is lost on the way (Fase 0 follow-up, 10-sep-2026). The C# tests below
        // keep the reasons; here we can only assert that the body was refused.
        Refuses("b = Body(Meters(0.0), MetersPerSecond(2.0), Seconds(6.0), Meters(0.6));", "Error while instantiating class 'Body'");
        Refuses("b = Body(Meters(0.25), MetersPerSecond(0.0), Seconds(6.0), Meters(0.6));", "Error while instantiating class 'Body'");
        Refuses("b = Body(Meters(0.25), MetersPerSecond(2.0), Seconds(-1.0), Meters(0.6));", "Error while instantiating class 'Seconds'");
        // the magnitudes say what they are: a duration where a length goes is refused by type, not taken as a number
        Refuses("b = Body(Seconds(0.25), MetersPerSecond(2.0), Seconds(6.0), Meters(0.6));", "a value of type 'Length' is expected");
        Assert.AreEqual("a body needs a radius above zero", Assert.ThrowsException<GolemDomainException>(() => new GolemDomain.Robots.Body(new Meters(0.0), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6))).Message);
        Assert.AreEqual("a body needs a cruise speed above zero", Assert.ThrowsException<GolemDomainException>(() => new GolemDomain.Robots.Body(new Meters(0.25), new MetersPerSecond(0.0), new Seconds(6.0), new Meters(0.6))).Message);
        Assert.AreEqual("a duration cannot be negative", Assert.ThrowsException<GolemDomainException>(() => new Seconds(-1.0)).Message);
        Assert.AreEqual(0.25, new Centimeters(25.0).InMeters, 1e-9, "a length reads in metres whatever unit wrote it");
        Assert.AreEqual(90.0, new Minutes(1.5).InSeconds, 1e-9, "a duration reads in seconds whatever unit wrote it");
        Assert.AreEqual(4.0, new MetersPerSecond(2.0).TimeFor(new Meters(8.0)).InSeconds, 1e-9, "a speed knows how long a length takes");
    }

    // ---- the map ----

    [TestMethod]
    public void TheMap_IsChartedFromTheReleaseChain_OneFluentChainPerPlace()
    {
        Assert.AreEqual(9, Int("map.ZoneCount"));
        Assert.AreEqual(10, Int("map.PassageCount"), "eight doors and two open boundaries");
        Assert.AreEqual("kitchen", Text("map.ZoneAt(Position(2.0, 9.5)).Name"));
        Assert.AreEqual("center", Text("map.ZoneAt(Position(5.5, 5.5)).Name"));
        Assert.AreEqual("west", Text("map.ZoneAt(Position(0.75, 5.5)).Name"));
        Assert.IsFalse(Bool("map.IsOnMap(Position(3.0, 5.0))"), "the left block is solid: nowhere on the map");
        Assert.IsFalse(Bool("map.IsOnMap(Position(11.5, 5.0))"), "beyond the east wall");
    }

    [TestMethod]
    public void TheMap_IsReadAsObjects_EachPlaceWithItsDoorsAndOpenings()
    {
        // the very query the panel runs: the golem's places, walked and printed, each with what it holds
        string json = perf.Actor.Using(@"
            foreach (places in map.Zones) {
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
            foreach (places in map.Zones) {
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
        Assert.IsTrue(Bool("map.IsWallAt(Position(0.05, 5.5), 0.3)"), "the corridor's outer wall");
        Assert.IsTrue(Bool("map.IsWallAt(Position(4.0, 8.5), 0.3)"), "the kitchen's east wall, by the door's jamb");
        Assert.IsFalse(Bool("map.IsWallAt(Position(4.1, 9.2), 0.3)"), "the kitchen/north doorway: no wall there, whatever was touched is something else");
        Assert.IsTrue(Bool("map.IsWallAt(Position(4.05, 8.0), 0.3)"), "the corner of the left block, met through the walls that end there");
        Assert.IsFalse(Bool("map.IsWallAt(Position(10.25, 5.9), 0.3)"), "the middle of the east corridor: whatever stands there is not on the map");
        Assert.IsFalse(Bool("map.IsWallAt(Position(5.5, 8.0), 0.3)"), "the open boundary north~center is not a wall");
        Assert.IsFalse(Bool("map.IsWallAt(Position(5.5, 5.5), 0.3)"), "the middle of the center hall");
    }

    // ---- entrusting: MoveTo, Cover, Follow ----

    [TestMethod]
    public void MoveTo_APoint_IsPending_AndIsTheOperators()
    {
        Visit(1, 2.0, 1.5);

        Assert.AreEqual(1, Int("g.PendingRoutes().Count"));
        Assert.IsTrue(Bool("g.Find(1).IsPending()"));
        StringAssert.EndsWith(Text("g.Find(1).AsPlan()"), "living@2,1.5", "the way, decided inside, ends at the point");
        Assert.AreEqual(0.75, Double("g.Next().NextLeg.At.X"), "the first thing is the first leg of the way: the door out of the kitchen");
        Assert.IsFalse(Bool("g.Find(1).Following"), "the operator ordered it");
        Assert.AreEqual(1, Int("g.Find(1).StopsLeft"));
    }

    [TestMethod]
    public void MoveTo_APlace_HeadsForItsCenter()
    {
        Visit(1, "storage");

        StringAssert.EndsWith(Text("g.Find(1).AsPlan()"), "storage@9,9.5", "the way ends at the area's centre");
        Assert.AreEqual(9.0, Double("g.Find(1).StopsAhead.Count") == 1 ? 9.0 : 0.0, 0.001);
        Refuses("g.Visit(Position(2.0, 9.5), map.Find('attic'));", "unknown area 'attic'");
    }

    [TestMethod]
    public void MoveTo_SeveralStops_WalksThemInTheGivenOrder()
    {
        Visit(1, new[] { "garage", "kitchen", "storage" });
        Assert.AreEqual(3, Int("g.Find(1).StopsLeft"));

        // from the living room: garage first, back out through the same door, the center shortcut to the kitchen, then the storage
        string plan = Text("g.Road(g.Find(1), Position(2.0, 1.5)).AsPlan()");
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
        string plan = Text("g.Road(g.Find(1), Position(2.0, 1.5)).AsPlan()");
        Assert.IsTrue(plan.IndexOf("garage@9,1.5") < plan.IndexOf("storage@9,9.5") && plan.IndexOf("storage@9,9.5") < plan.IndexOf("kitchen@2,9.5"),
            "garage, storage, kitchen — not the order given: " + plan);
        StringAssert.EndsWith(plan, "> kitchen@2,9.5");
    }

    [TestMethod]
    public void AStop_IsAPlaceOrAPointOnTheMap_AnythingElseIsRefused()
    {
        Visit(1, new[] { "kitchen", "9,8" });
        Assert.AreEqual(2, Int("g.Find(1).StopsLeft"));

        Refuses("g.Visit(Position(2.0, 9.5), map.Find('attic'));", "unknown area 'attic'");
        Refuses("g.Visit(Position(2.0, 9.5), Position(3.0, 5.0));", "nowhere on the map");
        Assert.IsTrue(Bool("map.Knows('kitchen')"));
        Assert.IsFalse(Bool("map.Knows('attic')"));
        Assert.IsTrue(Bool("map.IsOnMap(Position(9.0, 8.0))"), "a point in the north hall");
        Assert.AreEqual(1, Int("g.Routes().Count"));
    }

    [TestMethod]
    public void Follow_TakesAPointAPeerReached_WithAHandleOfItsOwn()
    {
        Visit(1, 2.0, 1.5);

        Follow(8.5, 2.5);

        Assert.AreEqual(2, Int("g.Routes().Count"));
        Assert.IsTrue(Bool("g.Knows(2)"), "the followed point got handle 2");
        Assert.IsTrue(Bool("g.Find(2).Following"), "a Follow mission remembers who it came from");
        Assert.AreEqual(2, Int("g.Routes().Count"), "two routes, handles 1 and 2, minted inside");
    }

    // ---- the road: Route, Cross, Reach ----

    [TestMethod]
    public void APlan_ReadsLikeTheJournalWillWriteIt_AndNamesEveryPassageAndStop()
    {
        Visit(1, 2.0, 1.5);   // the living room
        Assert.AreEqual("kitchen/west@0.75,8 > west/living@0.75,3 > living@2,1.5", Text("g.Road(g.Find(1), Position(2.0, 9.5)).AsPlan()"));

        Visit(2, 9.0, 1.5);   // the garage
        Abandon(1, "the test moves on");
        string plan = Text("g.Road(g.Find(2), Position(2.0, 9.5)).AsPlan()");
        StringAssert.StartsWith(plan, "kitchen/north@4,9.5 > ");
        StringAssert.Contains(plan, "north~center@");
        StringAssert.Contains(plan, "center~south@");
        StringAssert.EndsWith(plan, "> south/garage@7,1.5 > garage@9,1.5");
    }

    [TestMethod]
    public void RoutingAndReaching_ThePlanIsWalkedInSilence_AndAStopReachedImpliesTheLegsBeforeIt()
    {
        Visit(1, 2.0, 1.5);
        Decide(1, 2.0, 9.5);

        Assert.IsTrue(Bool("g.Find(1).IsRouted"));
        Assert.AreEqual(3, Int("g.Find(1).LegsLeft"), "two doors and the stop: the whole plan, in one entry");
        Assert.AreEqual("kitchen/west", Text("g.Find(1).NextLeg.Name"));
        Assert.IsFalse(Bool("g.Find(1).NextLeg.IsStop"));
        Assert.AreEqual(0.75, Double("g.Next().NextLeg.At.X"), 0.001, "the body heads to the first door, not to the stop");

        // one point at a time (16-sep): the body reports each point of the way it reaches and the route hands out the
        // next; a point that is not on the way ahead is refused; a stop reached implies the legs before it were walked
        Refuses("{ route = g.Find(1); route.Reach(Position(2.0, 2.0)); }", "next point is (0.75, 8)");
        Assert.IsTrue(Bool("g.Find(1).IsLegAhead(Position(0.75, 8.0))"), "the first door is ahead");
        Assert.IsTrue(Bool("g.Find(1).IsStopAhead(Position(2.0, 1.5))"));
        Assert.IsFalse(Bool("g.Find(1).IsStopAhead(Position(0.75, 3.0))"), "a door is not a stop");
        Reach(1, 0.75, 8.0);                                                   // the first door, reported
        Assert.AreEqual(2, Int("g.Find(1).LegsLeft"));
        Assert.AreEqual("west/living", Text("g.Find(1).NextLeg.Name"), "the route hands out the next point");
        Assert.IsTrue(Bool("g.Find(1).NextLeg.HasHeading"), "and the heading to walk it with, from the previous point");
        Assert.AreEqual(-1.5708, Double("g.Find(1).NextLeg.Heading"), 0.001, "straight south down the west corridor");
        Assert.IsFalse(Bool("g.Find(1).IsLegAhead(Position(0.75, 8.0))"), "a point reached is behind");

        Reach(1, 2.0, 1.5);                                                    // the stop, skipping the second door: it implies the door was walked
        Assert.AreEqual(0, Int("g.Find(1).LegsLeft"), "the second door was walked: reaching the stop says so");
        Assert.AreEqual("completed", Text("g.Find(1).Status"), "reaching the last stop completes the mission");
        Assert.IsFalse(Bool("g.Find(1).IsPending()"));
        Assert.AreEqual(0, Int("g.PendingRoutes().Count"));
        Refuses("{ route = g.Find(1); route.Reach(Position(2.0, 1.5)); }", "already completed");
    }

    [TestMethod]
    public void AMissionWithSeveralStops_ReachesEachInTurn_AndCompletesOnTheLast()
    {
        Visit(1, new[] { "north", "south" });
        Decide(1, 5.5, 5.5);   // from the center: up to the north hall, back down through the center to the south hall

        Assert.AreEqual(2, Int("g.Find(1).StopsLeft"));
        Reach(1, 5.5, 9.5);
        Assert.AreEqual("pending", Text("g.Find(1).Status"), "one stop reached, one to go");
        Assert.AreEqual(1, Int("g.Find(1).StopsLeft"));
        Assert.AreEqual("north~center", Text("g.Find(1).NextLeg.Name"), "the plan goes on from the stop: back through the opening");

        Reach(1, 5.5, 1.5);
        Assert.AreEqual("completed", Text("g.Find(1).Status"));
        Assert.AreEqual(0, Int("g.Find(1).StopsLeft"));
    }

    [TestMethod]
    public void ADoor_IsCrossedStraight_LiningUpOffTheWallOnBothSides()
    {
        // kitchen (2,9.5) -> door kitchen/west at (0.75,8) on a horizontal wall -> west corridor -> door (0.75,3) -> living
        Visit(1, 2.0, 1.5);
        Decide(1, 2.0, 9.5);

        // the plan ahead, as the host takes it to walk: each leg with its approach and its exit
        string json = perf.Actor.Using(@"
            foreach (legs in g.Find(1).LegsAhead) { print legs.Name 'name', legs.At.X 'x', legs.At.Y 'y', legs.Approach.X 'ax', legs.Approach.Y 'ay', legs.Exit.X 'ex', legs.Exit.Y 'ey'; }
        ").PerformQuery();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var legs = doc.RootElement.GetProperty("legs");
        Assert.AreEqual(3, legs.GetArrayLength());
        Assert.AreEqual(0.75, legs[0].GetProperty("x").GetDouble(), 0.001, "the leg IS the door");
        Assert.AreEqual(8.0, legs[0].GetProperty("y").GetDouble(), 0.001);
        Assert.AreEqual(0.75, legs[0].GetProperty("ax").GetDouble(), 0.001, "the approach stands right in front of the door");
        Assert.AreEqual(8.6, legs[0].GetProperty("ay").GetDouble(), 0.001, "0.6 into the kitchen, the side the body comes from");
        Assert.AreEqual(0.75, legs[0].GetProperty("ex").GetDouble(), 0.001);
        Assert.AreEqual(7.4, legs[0].GetProperty("ey").GetDouble(), 0.001, "0.6 into the corridor, the side it goes to");
        Assert.AreEqual(3.6, legs[1].GetProperty("ay").GetDouble(), 0.001, "the next door, west/living at y=3, is approached from the corridor");
        Assert.AreEqual(2.4, legs[1].GetProperty("ey").GetDouble(), 0.001);
        Assert.AreEqual(2.0, legs[2].GetProperty("ax").GetDouble(), 0.001, "a stop has no wall to clear: approach, exit and point coincide");
        Assert.AreEqual(1.5, legs[2].GetProperty("ey").GetDouble(), 0.001);
    }

    [TestMethod]
    public void TheErrand_DecidesItsWholeWayInside_AndPrintsOnlyTheNextThing()
    {
        // exactly what the host writes: where it starts and the area found — nothing else; the route holds the points
        perf.Actor.Using(@"
            { from = Position(@fx, @fy); point = map.Find(@area); route = g.Visit(from, point); }
        ")
        .WithParameters(p => {
            p["fx", typeof(double)] = 2.0; p["fy", typeof(double)] = 1.5;   // from the living room
            p["area", typeof(string)] = "kitchen";
        })
        .PerformCommand();

        Assert.IsTrue(Bool("g.Find(1).IsRouted"), "the errand decided the way");
        Assert.AreEqual("west/living@0.75,3 > kitchen/west@0.75,8 > kitchen@2,9.5", Text("g.Find(1).AsPlan()"), "the whole way, held by the route");
        Assert.AreEqual("west/living", Text("g.Find(1).NextLeg.Name"), "the first thing is the door out of the living room");
        Assert.AreEqual("door", Text("g.Find(1).NextLeg.Kind"));
        Assert.IsTrue(Bool("g.Find(1).NextLeg.HasHeading"), "the first leg has its heading too: the start is written");
        Assert.AreEqual("turn", Text("g.Find(1).Order"), "one thing at a time: turn first");
        perf.Actor.Using("{ route = g.Find(@id); route.Turn(); }").WithParameters(p => { p["id", typeof(int)] = 1; }).PerformCommand();
        Assert.AreEqual("run", Text("g.Find(1).Order"), "turned: now run to the door");
        Refuses("{ route = g.Find(1); route.Turn(); }", "asked no turn now");
        perf.Actor.Using("{ route = g.Find(@id); point = Position(@x, @y); route.Reach(point); }")
            .WithParameters(p => { p["id", typeof(int)] = 1; p["x", typeof(double)] = 0.75; p["y", typeof(double)] = 3.0; }).PerformCommand();
        Assert.AreEqual("turn", Text("g.Find(1).Order"), "the next leg asks its turn again");
        Assert.AreEqual(1, Int("g.Find(1).StopsLeft"));
    }

    // ---- the hold: Pause, Resume ----

    [TestMethod]
    public void APausedMission_KeepsItsPlanAndItsPlace_UntilResumed()
    {
        Visit(1, 2.0, 9.5);
        Decide(1, 2.0, 9.5);
        Assert.IsFalse(Bool("g.Find(1).Paused"));

        perf.Actor.Using("{ route = g.Find(@id); route.Pause(); }").WithParameters(p => { p["id", typeof(int)] = 1; }).PerformCommand();
        Assert.IsTrue(Bool("g.Find(1).Paused"), "held");
        Assert.IsTrue(Bool("g.Find(1).IsPending()"), "a hold is not an ending");
        Assert.IsTrue(Bool("g.Find(1).IsRouted"), "the plan keeps");
        Assert.AreEqual(1, Int("g.Find(1).StopsLeft"), "and so do the stops ahead");
        Refuses("{ route = g.Find(1); route.Pause(); }", "already paused");

        perf.Actor.Using("{ route = g.Find(@id); route.Resume(); }").WithParameters(p => { p["id", typeof(int)] = 1; }).PerformCommand();
        Assert.IsFalse(Bool("g.Find(1).Paused"), "let go on");
        Refuses("{ route = g.Find(1); route.Resume(); }", "is not paused");

        Reach(1, 2.0, 9.5);
        Refuses("{ route = g.Find(1); route.Pause(); }", "already completed");
    }

    // ---- the ending: Fail, Abandon, Announce ----

    [TestMethod]
    public void AFailedMission_KeepsItsReason_AndIsNoLongerPending()
    {
        Visit(1, 2.0, 1.5);

        perf.Actor.Using(@"
            { route = g.Find(@id); route.Fail(@reason); }
        ")
        .WithParameters(p => {
            p["id",     typeof(int)]    = 1;
            p["reason", typeof(string)] = "collided with crate at (10.3, 5.9): nothing on my map there";
        })
        .PerformCommand();

        Assert.IsFalse(Bool("g.Find(1).IsPending()"));
        Assert.IsFalse(Bool("g.HasPendingMission()"));
        Assert.AreEqual("failed", Text("g.Find(1).Status"));
        Refuses("{ route = g.Find(1); route.Fail('again'); }", "already failed");
    }

    [TestMethod]
    public void AStaleFollowedPoint_IsAbandonedForANewerOne_AndTheOperatorsPointNeverIs()
    {
        Visit(1, 9.0, 1.5);                 // the operator: garage
        Follow(2.0, 9.5);                    // the leader was in the kitchen...
        Assert.IsFalse(Bool("g.HasNewerFollowing(g.Find(2))"));

        Follow(9.0, 9.5);                    // ...and now says it is in the storage
        Assert.IsTrue(Bool("g.HasNewerFollowing(g.Find(2))"));
        Assert.AreEqual(3, Int("g.NewestFollowingId()"));
        Assert.IsFalse(Bool("g.HasNewerFollowing(g.Find(1))"), "the operator's mission is never superseded by the loop's rule");

        Abandon(2, "superseded by mission 3");

        Assert.AreEqual("abandoned", Text("g.Find(2).Status"));
        Assert.IsFalse(Bool("g.Find(2).IsPending()"));
        Assert.AreEqual(2, Int("g.PendingRoutes().Count"), "the operator's garage and the newest followed point remain");
        Assert.AreEqual(1, Int("g.FollowingCount()"));
    }

    [TestMethod]
    public void LettingGo_AbandonsEveryPendingMissionInOneCommand_ButNeverReusesAHandle()
    {
        Visit(1, 2.0, 1.5);
        Visit(2, 9.0, 1.5);
        long entriesBefore = perf.CurrentEntryId;

        perf.Actor.Using(@"
            foreach (route in g.PendingRoutes()) {
                route.Abandon(@reason);
            }
        ")
        .WithParameters(p => {
            p["reason", typeof(string)] = "the operator let go of everything";
        })
        .PerformCommand();

        Assert.IsTrue(perf.CurrentEntryId - entriesBefore <= 2,
            "one action for all the missions (plus the template of a shape seen for the first time), never one entry per mission");
        Assert.AreEqual(0, Int("g.PendingRoutes().Count"));
        Assert.AreEqual(2, Int("g.Routes().Count"), "nothing is erased: the missions stay, abandoned");
        Assert.AreEqual("abandoned", Text("g.Find(1).Status"));
        Assert.AreEqual(2, Int("g.Routes().Count"), "a spent handle is never minted again: the idempotency keys hang on it");
        Refuses("{ route = g.Find(1); route.Abandon('again'); }", "already abandoned");
    }

    [TestMethod]
    public void AbandoningNeedsAReason()
    {
        Visit(1, 2.0, 1.5);
        Refuses("{ route = g.Find(1); route.Abandon(''); }", "needs a reason");
    }

    [TestMethod]
    public void AReachedStop_CanBeAnnounced_AndAMissionWithoutOneCannot()
    {
        Visit(1, 2.0, 9.5);
        Decide(1, 2.0, 9.5);   // already there: the stop alone
        Reach(1, 2.0, 9.5);
        Visit(2, 9.0, 1.5);

        perf.Actor.Using(@"
            { route = g.Find(@id); route.Announce(); }
        ")
        .WithParameters(p => {
            p["id", typeof(int)] = 1;
        })
        .PerformCommand();

        Assert.IsTrue(Bool("g.Find(1).Announced"));
        Assert.IsFalse(Bool("g.Find(2).Announced"));
        Refuses("{ route = g.Find(2); route.Announce(); }", "only a reached stop is announced");
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
            @left  = g.DistanceLeft(Position(@x, @y));
            @eta   = g.SecondsLeft(Position(@x, @y));
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

        Assert.AreEqual(0.0, Double("g.DistanceLeft(Position(2.0, 9.5))"), 0.001);
    }

    // ---- marks: what bodies touched that the plan does not hold ----

    [TestMethod]
    public void ABump_IsATouchAndAMarkAtOnce_UntilAPeerSpeaks()
    {
        Visit(1, 9.0, 1.5);                       // the garage
        Decide(1, 9.0, 9.5);      // from the storage: down the east corridor
        Assert.AreEqual(0, Int("collisions.MarkCount"));
        Assert.IsTrue(Bool("g.FitsAt(Position(10.25, 5.5))"), "the corridor is clear as far as the map knows");

        Bump(1, 10.25, 5.85, South);                // the crate's face — or a peer: presumed a thing, in one act
        Assert.AreEqual(1, Int("collisions.MarkCount"), "a bump is a touch AND a mark, with the heading of the touch as its normal");
        Assert.AreEqual(1, Int("g.Find(1).Bumps"));
        Assert.IsTrue(Bool("g.Find(1).BumpedSinceRoute"));
        Assert.AreEqual("", Text("collisions.HeardNear(Position(10.25, 5.85), 0)"), "no peer said it bumped there: the mark stands");
        Assert.IsFalse(Bool("g.FitsAt(Position(10.25, 5.5))"), "the body no longer fits where the mark reaches");
        Assert.IsTrue(Bool("g.HasRoomAt(Position(10.25, 5.5))"), "the walls alone still leave room there: marks are what the body feels around");
        Assert.IsFalse(Bool("g.HasRoomAt(Position(9.7, 5.5))"), "too close to the corridor's wall for the body");

        Assert.AreEqual(1, Int("collisions.Mark(Pose(10.25, 5.9, -1.5708))"), "a touch within a tenth of a unit is the same mark");
        Assert.AreEqual(2, Int("collisions.Mark(Pose(10.3, 6.6, -1.5708))"), "a touch further along is another mark");
    }

    [TestMethod]
    public void APeersBumpHeardThereAndThen_NamesWhoWasMet()
    {
        Visit(1, 9.0, 1.5);
        Decide(1, 9.0, 9.5);
        Assert.AreEqual(1, Int("g.HearBump('blue', Pose(4.6, 9.5, 0.0), Position(5.1, 9.5))"), "blue says it bumped in the kitchen's doorway, standing just past it");
        int heard = Int("collisions.HeardCount");

        Bump(1, 4.7, 9.5, East);                    // my own touch, right there
        Assert.AreEqual("blue", Text("collisions.HeardNear(Position(4.7, 9.5), 0)"), "blue's bump is within a body's diameter of mine: it was blue");
        Assert.AreEqual("", Text("collisions.HeardNear(Position(4.7, 9.5), " + heard + ")"), "nothing heard after that count");
        Assert.AreEqual("", Text("collisions.HeardNear(Position(10.25, 5.85), 0)"), "a bump far away is somebody else's business");
        Assert.AreEqual(2, Int("collisions.MarkCount"), "two marks presumed: blue's bump, heard, and my own");
        Met("blue", 4.7, 9.5);
        Assert.AreEqual(0, Int("collisions.MarkCount"), "meeting a body takes both marks back: mine, and the one I learned from blue's bump there");
        Assert.AreEqual(0, Int("g.LearnMet(Position(4.6, 9.5))"), "when blue tells it met a body too, nothing is left to take back");
        Assert.AreEqual(0, Int("collisions.MarkCount"), "a meeting leaves no mark on either side");

        Assert.AreEqual(1, Int("g.Bump(Pose(4.9, 9.5, 0.0))"), "a body standing idle that gets touched bumps too, without a mission");
        Assert.AreEqual(2, Int("g.Bump(Pose(4.9, 9.5, 0.0))"));
        Refuses("g.HearBump('', Pose(1.0, 1.0, 0.0), Position(1.0, 1.0));", "needs to say who");
        Assert.AreEqual(2, Int("g.HearTouch('red', Position(4.9, 9.5), Position(5.1, 9.5))"), "a standing peer touched: heard (the second bump heard)…");
        Assert.AreEqual(0, Int("collisions.MarkCount"), "…and no mark: what touches a standing body is a body");
    }

    [TestMethod]
    public void MarksCloseTogether_AreJoinedIntoOneObstacle_WhoseVerticesOutlineIt()
    {
        PlantMark(10.25, 5.85, South);    // the crate's north face
        PlantMark(10.25, 5.15, North);    // its south face
        PlantMark(9.9, 5.5, East);        // its west face
        PlantMark(8.0, 9.5, East);        // something else, far away in the storage room
        Assert.AreEqual(4, Int("collisions.MarkCount"));
        Assert.AreEqual(2, Int("collisions.All().Count"), "three touches on one thing, one on another");

        string json = perf.Actor.Using(@"
            foreach (obstacles in collisions.All()) {
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
        string before = Text("g.Road(g.Find(1), Position(9.0, 9.5)).AsPlan()");
        StringAssert.Contains(before, "storage/east@10.25,8 > east/garage@10.25,3", "the east corridor is the shortest road");

        PlantMark(10.25, 5.85, South);                   // a peer bumped into something in the middle of the corridor

        string after = Text("g.Road(g.Find(1), Position(9.0, 9.5)).AsPlan()");
        Assert.IsFalse(after.Contains("east/garage"), "between the mark and the corridor's walls the body does not fit: " + after);
        StringAssert.Contains(after, "north~center@");
        StringAssert.EndsWith(after, "> south/garage@7,1.5 > garage@9,1.5");
    }

    [TestMethod]
    public void AMarkInAWideRoom_IsSkirted_WithAroundLegs()
    {
        Visit(1, 10.2, 9.5);                        // across the storage room
        PlantMark(9.0, 9.5, East);                       // something in the middle of it, touched heading east

        string plan = Text("g.Road(g.Find(1), Position(7.6, 9.5)).AsPlan()");
        StringAssert.Contains(plan, "around@", "the body skirts the mark: " + plan);
        StringAssert.EndsWith(plan, "> storage@10.2,9.5");

        Decide(1, 7.6, 9.5);
        Assert.IsFalse(Bool("g.Find(1).NextLeg.IsStop"));
        Assert.AreEqual("around", Text("g.Find(1).NextLeg.Name"), "the emergency point is the first leg of the plan: the route's own detour");
        Assert.IsTrue(Int("g.Find(1).LegsLeft") >= 2);
    }

    [TestMethod]
    public void AfterABump_TheRoadIsDecidedAgain()
    {
        Visit(1, 9.0, 1.5);
        Decide(1, 9.0, 9.5);

        Bump(1, 10.25, 5.85, South);                 // the crate, met halfway down the corridor: bumped and marked at once
        string again = Text("g.Road(g.Find(1), Position(10.25, 6.3)).AsPlan()");   // from where the body backed off to
        Assert.IsFalse(again.Contains("east/garage"), "the corridor is closed by the mark: " + again);
        StringAssert.Contains(again, "storage/east@10.25,8", "back out the way it came");
        Decide(1, 10.25, 6.3);
        Assert.IsFalse(Bool("g.Find(1).BumpedSinceRoute"));
        Assert.AreEqual(1, Int("g.Find(1).StopsLeft"));
        Assert.AreEqual("pending", Text("g.Find(1).Status"));
    }

    [TestMethod]
    public void ABodyStandingAmongMarks_CanStillLeave_ButNotThroughThem()
    {
        Visit(1, 9.0, 1.5);                        // the garage
        PlantMark(8.2, 9.3, South);                      // two touches on something right beside the body — a thing 0.6 wide, a body's
        PlantMark(8.8, 9.3, South);                      // width and a hand clear of the north/storage door (14-sep: one FIGURE, its berth is a box)
        string plan = Text("g.Road(g.Find(1), Position(8.5, 9.55)).AsPlan()");  // ...which stands between them, in the storage room
        StringAssert.EndsWith(plan, "> garage@9,1.5", "a road out exists: " + plan);

        PlantMark(10.25, 5.85, South);                   // the crate closes the corridor too
        // a body 0.15 north of that mark (it just backed off the crate; the touch was estimated a little short) leaves
        // the way it came and takes the long way round — never straight down through the mark (the 8-sep live run)
        string outNorth = Text("g.Road(g.Find(1), Position(10.25, 6.0)).AsPlan()");
        StringAssert.StartsWith(outNorth, "storage/east@10.25,8 > ", "back out through the door it came in by: " + outNorth);
        Assert.IsFalse(outNorth.Contains("east/garage"), "not through the crate: " + outNorth);
    }

    [TestMethod]
    public void WhenNoRoadFits_TheProgressReadsStillAnswer_AsTheCrowFlies()
    {
        Visit(1, 2.0, 9.5);                          // the kitchen, from the storage
        Decide(1, 9.0, 9.5);
        PlantMark(4.0, 9.2, East);                        // marks close the kitchen/north doorway...
        PlantMark(4.0, 9.8, East);
        PlantMark(0.75, 8.2, South);                      // ...and the kitchen/west one
        PlantMark(0.75, 7.8, South);
        Refuses("g.Road(g.Find(1), Position(9.0, 9.5)).AsPlan();", "no road");
        Assert.AreEqual(7.0, Double("g.DistanceLeft(Position(9.0, 9.5))"), 0.001, "straight from (9, 9.5) to (2, 9.5): a read never refuses");
        Assert.IsTrue(Double("g.SecondsLeft(Position(9.0, 9.5))") > 0);
    }

    [TestMethod]
    public void WhenNoRoadFitsTheBody_ThePlanSaysSo()
    {
        Visit(1, 9.0, 1.5);                        // the garage has two doors
        PlantMark(7.0, 1.5, East);                       // something in the south/garage door
        PlantMark(10.25, 3.0, North);                    // and something in the east/garage door
        Refuses("g.Road(g.Find(1), Position(2.0, 1.5)).AsPlan();", "fits a body of radius 0.25 past 2 marks");
    }



    // ---- the plan: written whole, walked in silence; a stop reached is what the journal hears ----

    [TestMethod]
    public void AStopReached_ImpliesEveryLegBeforeIt_AndANonStopIsRefused()
    {
        Visit(1, 2.0, 1.5);                       // the living room, from the kitchen
        Decide(1, 2.0, 9.5);    // kitchen/west > west/living > living
        Assert.AreEqual(3, Int("g.Find(1).LegsLeft"));

        Refuses("{ route = g.Find(1); route.Reach(Position(3.0, 3.0)); }", "next point is (0.75, 8)");   // not a point of the way
        Reach(1, 2.0, 1.5);                                            // the stop: the two doors were walked
        Assert.AreEqual("completed", Text("g.Find(1).Status"));
        Assert.AreEqual(0, Int("g.Find(1).LegsLeft"));
    }

    [TestMethod]
    public void AnErrandOneSegmentAway_HasAPlanOfOneLeg_TheStopItself()
    {
        Visit(1, 2.0, 9.5);                        // a point of the kitchen, from the kitchen
        string plan = Text("g.Road(g.Find(1), Position(3.0, 9.0)).AsPlan()");
        Assert.AreEqual("kitchen@2,9.5", plan, "same room, nothing in between: the plan is the stop alone");
        Decide(1, 3.0, 9.0);
        Assert.AreEqual(1, Int("g.Find(1).LegsLeft"));
        Assert.IsTrue(Bool("g.Find(1).NextLeg.IsStop"));

        Reach(1, 2.0, 9.5);
        Assert.AreEqual("completed", Text("g.Find(1).Status"), "reaching the only stop completes it");
    }

    [TestMethod]
    public void SeveralStopsWithoutARoad_AreReachedInOrder()
    {
        Visit(1, new[] { "2,9.5", "3,10.5" });     // two points of the kitchen
        Assert.AreEqual(2, Int("g.Find(1).StopsLeft"));

        Refuses("{ route = g.Find(1); route.Reach(Position(3.0, 10.5)); }", "next stop is (2, 9.5)");
        Reach(1, 2.0, 9.5);
        Assert.AreEqual("pending", Text("g.Find(1).Status"), "one stop reached, one to go");
        Assert.AreEqual(3.0, Double("g.Next().NextLeg.At.X"), 0.001);

        Reach(1, 3.0, 10.5);
        Assert.AreEqual("completed", Text("g.Find(1).Status"));
    }

    [TestMethod]
    public void ATouch_InterruptsThePlan_AndAnotherRoadReplacesWhatWasLeft()
    {
        Visit(1, 2.0, 1.5);
        Decide(1, 2.0, 9.5);
        Assert.IsFalse(Bool("g.Find(1).BumpedSinceRoute"));

        Bump(1, 2.0, 10.8, North);                // something by the kitchen's north wall
        Assert.IsTrue(Bool("g.Find(1).BumpedSinceRoute"), "the plan is interrupted: the golem decides another road");
        Assert.AreEqual("pending", Text("g.Find(1).Status"), "but the mission goes on");

        Decide(1, 2.0, 10.4);   // from where the body stands, the same stop ahead
        Assert.IsFalse(Bool("g.Find(1).BumpedSinceRoute"));
        Assert.AreEqual(1, Int("g.Find(1).StopsLeft"));

        Graze(1, 0.05, 8.5);
        Assert.IsTrue(Bool("g.Find(1).MayRetryLeg"), "patience left: the plan is retried");
        Graze(1, 0.05, 8.4);
        Graze(1, 0.05, 8.6);
        Assert.IsFalse(Bool("g.Find(1).MayRetryLeg"), "patience spent: the mission ends");
    }

    [TestMethod]
    public void MeetingAPeer_TheRoadStepsOutOfItsWay_AndTheStepIsALegLikeAnyOther()
    {
        // red drives east across the kitchen toward the north hall; blue tells it bumped in the doorway and
        // stands just past it. Knowing where blue is, red's road starts by stepping out of the way.
        Visit(1, "north");
        Int("g.HearBump('blue', Pose(4.6, 9.5, 0.0), Position(5.1, 9.5))");
        Bump(1, 4.7, 9.5, East);                      // my own touch, right there: presumed a thing…
        Met("blue", 4.7, 9.5);                        // …until the domain names blue: both marks go, the way is clear to step aside

        // the route decides its way out of blue's way itself: the courtesy step first, then the errand goes on
        perf.Actor.Using("{ route = g.Find(@id); me = Pose(@x, @y, @heading); route.DecidePast(@who, me); }")
            .WithParameters(p => { p["id", typeof(int)] = 1; p["who", typeof(string)] = "blue"; p["x", typeof(double)] = 4.6; p["y", typeof(double)] = 9.5; p["heading", typeof(double)] = 0.0; })
            .PerformCommand();
        string road = Text("g.Find(1).AsPlan()");
        StringAssert.StartsWith(road, "aside@", "the first leg is the courtesy step: " + road);
        StringAssert.EndsWith(road, "> north@5.5,9.5", "and then the errand goes on");

        // to its own right, facing east, is south — and away from blue, who stands ahead
        var step = road.Split('>')[0].Trim();
        Assert.AreEqual("aside@4.6,9", step, "a body's width to its right, off the line it was on");

        // the step is a leg to cross, never a stop: reaching it must not complete the errand
        Assert.IsFalse(Bool("g.Find(1).NextLeg.IsStop"));
        Assert.AreEqual("aside", Text("g.Find(1).NextLeg.Name"), "the courtesy step is the route's own leg");
        Assert.AreEqual("pending", Text("g.Find(1).Status"), "stepping aside is not arriving");
        Assert.AreEqual(1, Int("g.Find(1).StopsLeft"));
    }

    [TestMethod]
    public void WithNowhereToStepAside_TheRoadIsThePlainOne()
    {
        Visit(1, "north");
        Int("g.HearBump('blue', Pose(4.6, 9.5, 0.0), Position(5.1, 9.5))");
        // hemmed in against the kitchen's north wall: neither side leaves room for the body
        perf.Actor.Using("{ route = g.Find(@id); me = Pose(@x, @y, @heading); route.DecidePast(@who, me); }")
            .WithParameters(p => { p["id", typeof(int)] = 1; p["who", typeof(string)] = "blue"; p["x", typeof(double)] = 2.0; p["y", typeof(double)] = 10.9; p["heading", typeof(double)] = 1.5708; })
            .PerformCommand();
        string road = Text("g.Find(1).AsPlan()");
        Assert.IsFalse(road.Contains("aside@"), "no room to be polite: the plain road, and the meeting is settled by waiting: " + road);
    }


    [TestMethod]
    public void TheObstacles_AreReadAsAFlatList_EachWithItsZoneAndItsVertices()
    {
        PlantMark(10.25, 5.85, South);      // three touches on the crate of the east corridor
        PlantMark(10.25, 5.15, North);
        PlantMark(9.9, 5.5, East);
        PlantMark(2.0, 9.5, East);          // something else, a room away
        Met("blue", 4.7, 9.5);              // and a body met in the north hall

        // the very query the panel's table runs: one row per obstacle, one per vertex under it
        string json = perf.Actor.Using(@"
            print collisions.All().Count 'total';
            foreach (obstacles in collisions.All()) {
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
        PlantMark(10.25, 5.85, South);        // three touches on the crate of the east corridor
        PlantMark(10.25, 5.15, North);
        PlantMark(9.9, 5.5, East);
        PlantMark(2.0, 9.5, East);            // and something else, a room away
        Assert.AreEqual(2, Int("collisions.All().Count"));
        Assert.AreEqual(4, Int("collisions.MarkCount"));
        Assert.IsFalse(Bool("g.FitsAt(Position(10.25, 5.5))"), "the crate is in the way");

        Assert.IsTrue(Bool("collisions.KnowsAt(Position(10.13, 5.5))"), "asked by its centre");
        Assert.IsTrue(Bool("collisions.KnowsAt(Position(9.9, 5.5))"), "or by one of its vertices");
        Assert.IsFalse(Bool("collisions.KnowsAt(Position(5.5, 5.5))"), "and nothing stands in the middle of the centre hall");

        Assert.AreEqual(3, Int("g.Forget(Position(10.13, 5.5))"), "the three marks that outlined it go at once");
        Assert.AreEqual(1, Int("collisions.All().Count"), "the other obstacle is untouched");
        Assert.AreEqual(1, Int("collisions.MarkCount"));
        Assert.IsTrue(Bool("g.FitsAt(Position(10.25, 5.5))"), "a body may pass there again");
        Assert.AreEqual(0, Int("g.Forget(Position(10.13, 5.5))"), "forgetting nothing drops nothing: it is not a refusal");

        // whatever is touched there next is a NEW obstacle, outlined by new marks
        PlantMark(10.25, 5.5, South);
        Assert.AreEqual(2, Int("collisions.All().Count"));
        Assert.AreEqual(2, Int("collisions.Things().Count"), "the new thing stands on its own, with nothing of the old one");
        Assert.AreEqual(2, Int("collisions.MarkCount"), "one mark of the new thing, one of the untouched one");
    }

    [TestMethod]
    public void ForgettingAnEncounter_DropsThatPeer_AndLeavesTheThings()
    {
        Met("blue", 4.7, 9.5);
        PlantMark(10.25, 5.85, South);
        Assert.AreEqual(2, Int("collisions.All().Count"));

        Assert.AreEqual(1, Int("g.Forget(Position(4.7, 9.5))"), "the encounter is dropped");
        Assert.AreEqual(0, Int("collisions.EncounterCount"));
        Assert.AreEqual(1, Int("collisions.Things().Count"), "and the thing stays");
        Assert.AreEqual(1, Int("collisions.MarkCount"));
    }

    // ---- the second touch protocol: the domain suspects, the golem concludes ----

    [TestMethod]
    public void WhatWasTouched_IsSuspectedByTheDomain_WallPeerOrThing()
    {
        // the domain reasons over its own facts: the map, and what the peers said since the leg began
        string json = perf.Actor.Using(@"
            print g.Suspect(Pose(0.05, 5.5, 3.1416), 0).Kind 'wall', g.Suspect(Pose(0.05, 5.5, 3.1416), 0).Conclusion 'wallVerb';
            print g.Suspect(Pose(10.25, 5.9, -1.5708), 0).Kind 'thing', g.Suspect(Pose(10.25, 5.9, -1.5708), 0).Conclusion 'thingVerb', g.Suspect(Pose(10.25, 5.9, -1.5708), 0).Who 'nobody';
        ").PerformQuery();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.AreEqual("wall", doc.RootElement.GetProperty("wall").GetString(), "the corridor's outer wall");
        Assert.AreEqual("Graze", doc.RootElement.GetProperty("wallVerb").GetString(), "a wall I know: I grazed it");
        Assert.AreEqual("thing", doc.RootElement.GetProperty("thing").GetString(), "nothing on the map, nobody heard");
        Assert.AreEqual("Bump", doc.RootElement.GetProperty("thingVerb").GetString(), "nothing more to write: the bump's mark stands");
        Assert.AreEqual("", doc.RootElement.GetProperty("nobody").GetString());

        Int("g.HearBump('blue', Pose(4.6, 9.5, 0.0), Position(5.1, 9.5))");
        string peer = perf.Actor.Using(@"
            print g.Suspect(Pose(4.7, 9.5, 0.0), 0).Kind 'kind', g.Suspect(Pose(4.7, 9.5, 0.0), 0).Who 'who', g.Suspect(Pose(4.7, 9.5, 0.0), 0).Conclusion 'verb';
            print g.Suspect(Pose(4.7, 9.5, 0.0), 1).Kind 'later';
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
        Decide(1, 2.0, 9.5);   // kitchen/west > west/living > living
        Assert.IsTrue(Bool("g.Find(1).MayRetryLeg"));

        Graze(1, 0.05, 8.5);
        Graze(1, 0.05, 8.4);
        Assert.AreEqual(2, Int("g.Find(1).Grazes"));
        Assert.IsTrue(Bool("g.Find(1).MayRetryLeg"), "two grazes: still patient");
        Graze(1, 0.05, 8.6);
        Assert.IsFalse(Bool("g.Find(1).MayRetryLeg"), "three grazes on one leg: patience spent, the mission is given up");
        Assert.AreEqual(0, Int("collisions.MarkCount"), "a graze leaves no mark: the wall was known");

        Reach(1, 2.0, 1.5);
        Assert.AreEqual(3, Int("g.Find(1).Grazes"), "the mission remembers every graze");
        Assert.AreEqual("completed", Text("g.Find(1).Status"), "the golem chose to go on and got there");
        Refuses("{ route = g.Find(2); route.Graze(Position(1.0, 1.0)); }", "unknown route");
    }

    [TestMethod]
    public void MeetingAPeer_IsHistory_AmongTheObstacles_NeverGeometry()
    {
        Met("blue", 4.7, 9.5);
        PlantMark(10.25, 5.85, South);

        Assert.AreEqual(1, Int("collisions.EncounterCount"));
        Assert.AreEqual(2, Int("collisions.All().Count"), "a thing and a peer");
        Assert.AreEqual(1, Int("collisions.Things().Count"), "only the thing is planned around");
        Assert.IsTrue(Bool("g.FitsAt(Position(4.7, 9.5))"), "the peer moved on: the body fits where it was met");

        string json = perf.Actor.Using(@"
            foreach (obstacles in collisions.All()) { print obstacles.Kind 'kind', obstacles.Where 'zone', obstacles.Who 'who', obstacles.Shape 'shape'; }
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
        Refuses("g.Met('', Position(1.0, 1.0));", "has a name");
    }

    [TestMethod]
    public void ABodyStandingInsideItsOwnMark_CanStillLeave_MovingAwayFromIt()
    {
        // From the 8-sep live run: red grazed the crate's north-west corner with its side while heading south; the touch
        // was estimated head-on, so the mark (4.94, 5.76) fell 0.19 from where the body then stood (4.8, 5.63) — inside
        // its own radius. A body cannot be inside a thing: the mark forbids walking further in, not leaving.
        Visit(1, 9.0, 1.5);                                                    // the garage
        PlantMark(4.93672793175996, 5.75978159057928, -1.37257826652825);
        PlantMark(5.14806941278059, 5.65085610890956, 0.0681849256515386);    // the second touch, stepping left into the crate's west face

        string plan = Text("g.Road(g.Find(1), Position(4.8, 5.63)).AsPlan()");
        StringAssert.EndsWith(plan, "> garage@9,1.5", "a road out exists: " + plan);
    }

    [TestMethod]
    public void AMarkIsAFigure_TheBodyKeepsABerthAroundIt_AndTheRetreatStandsClear()
    {
        // 14-sep-2026: a thing's figure is the box around its marks, grown by a mark's reach, a margin and the body;
        // the body backs off its retreat (0.6) after a touch, which lands it outside that berth on the side it came from
        PlantMark(5.5, 5.85, South);   // the crate's north face, touched by a body heading south
        Assert.IsFalse(Bool("g.FitsAt(Position(5.5, 5.5))"), "beyond the mark: inside the thing");
        Assert.IsFalse(Bool("g.FitsAt(Position(5.0, 5.85))"), "half a metre along the surface: within the berth");
        Assert.IsTrue(Bool("g.FitsAt(Position(4.85, 5.85))"), "0.65 along the surface: past the berth");
        Assert.IsFalse(Bool("g.FitsAt(Position(5.5, 6.25))"), "0.4 back: still within the berth (the old normal model said free — that is where the next bump came from)");
        Assert.IsTrue(Bool("g.FitsAt(Position(5.5, 6.7))"), "0.85 back — the touch point plus the retreat — stands clear");
        PlantMark(5.8, 5.85, South);   // a second touch 0.3 further along: one figure for both, the width the thing showed
        Assert.IsFalse(Bool("g.FitsAt(Position(6.3, 5.85))"), "half a metre past the second mark: within the one figure's berth");
        Assert.IsTrue(Bool("g.FitsAt(Position(6.45, 5.85))"), "0.65 past it: clear");
    }

    // ---- helpers: the same perform shapes the host uses ----

    private void Bump(int id, double x, double y, double heading) =>
        perf.Actor.Using(@"
            { route = g.Find(@id); touch = Pose(@x, @y, @heading); route.Bump(touch); }
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
            { route = g.Find(@id); at = Position(@x, @y); route.Graze(at); }
        ")
        .WithParameters(p => {
            p["id", typeof(int)]    = id;
            p["x",  typeof(double)] = x;
            p["y",  typeof(double)] = y;
        })
        .PerformCommand();

    // The lab plants a mark straight on the collisions module — the global, as a query may calculate with a module alone.
    private void PlantMark(double x, double y, double heading) =>
        perf.Actor.Using(@"
            { touch = Pose(@x, @y, @heading); collisions.Mark(touch); }
        ")
        .WithParameters(p => {
            p["x",       typeof(double)] = x;
            p["y",       typeof(double)] = y;
            p["heading", typeof(double)] = heading;
        })
        .PerformCommand();

    private void Met(string who, double x, double y) =>
        perf.Actor.Using(@"
            { at = Position(@x, @y); g.Met(@who, at); }
        ")
        .WithParameters(p => {
            p["who", typeof(string)] = who;
            p["x",   typeof(double)] = x;
            p["y",   typeof(double)] = y;
        })
        .PerformCommand();

    // The errand as the host writes it: from where the body stands (the kitchen's centre unless the test says), the
    // route decides its way inside and holds the points — the journal never lists them (Juan, 16-sep-2026).
    private void Visit(int id, double x, double y, double fromX = 2.0, double fromY = 9.5) =>
        perf.Actor.Using(@"
            { from = Position(@fx, @fy); point = Position(@x, @y); route = g.Visit(from, point); }
        ")
        .WithParameters(p => {
            p["fx", typeof(double)] = fromX; p["fy", typeof(double)] = fromY;
            p["x",  typeof(double)] = x;
            p["y",  typeof(double)] = y;
        })
        .PerformCommand();

    private void Visit(int id, string place, double fromX = 2.0, double fromY = 9.5) =>
        perf.Actor.Using(@"
            { from = Position(@fx, @fy); point = map.Find(@area); route = g.Visit(from, point); }
        ")
        .WithParameters(p => {
            p["fx", typeof(double)] = fromX; p["fy", typeof(double)] = fromY;
            p["area", typeof(string)] = place;
        })
        .PerformCommand();

    private void Visit(int id, string[] stops, double fromX = 2.0, double fromY = 9.5) => Errand("Visit", id, stops, fromX, fromY);
    private void Cover(int id, string[] stops, double fromX = 2.0, double fromY = 9.5) => Errand("Cover", id, stops, fromX, fromY);

    // The way decided again from where the body stands — after a bump, or awake with a plan underway.
    private void Decide(int id, double x, double y) =>
        perf.Actor.Using(@"
            { route = g.Find(@id); from = Position(@x, @y); route.Decide(from); }
        ")
        .WithParameters(p => { p["id", typeof(int)] = id; p["x", typeof(double)] = x; p["y", typeof(double)] = y; })
        .PerformCommand();

    // Several stops: the first opens the route from the start, each further one is told to it (route.Then), one
    // fixed template per stop — the route decides its way again through them all.
    private void Errand(string verb, int id, string[] stops, double fromX, double fromY)
    {
        for (int i = 0; i < stops.Length; i++)
        {
            string stop = stops[i];
            bool first = i == 0;
            string script = IsPoint(stop)
                ? (first ? $"{{ from = Position(@fx, @fy); point = Position(@x, @y); route = g.{verb}(from, point); }}" : "{ route = g.Find(@id); point = Position(@x, @y); route.Then(point); }")
                : (first ? $"{{ from = Position(@fx, @fy); point = map.Find(@area); route = g.{verb}(from, point); }}" : "{ route = g.Find(@id); point = map.Find(@area); route.Then(point); }");
            perf.Actor.Using(script)
            .WithParameters(p => {
                if (first) { p["fx", typeof(double)] = fromX; p["fy", typeof(double)] = fromY; }
                else p["id", typeof(int)] = id;
                if (IsPoint(stop))
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

    private void Reach(int id, double x, double y) =>
        perf.Actor.Using(@"
            { route = g.Find(@id); point = Position(@x, @y); route.Reach(point); }
        ")
        .WithParameters(p => {
            p["id", typeof(int)]    = id;
            p["x",  typeof(double)] = x;
            p["y",  typeof(double)] = y;
        })
        .PerformCommand();

    private void Abandon(int id, string reason) =>
        perf.Actor.Using(@"
            { route = g.Find(@id); route.Abandon(@reason); }
        ")
        .WithParameters(p => {
            p["id",     typeof(int)]    = id;
            p["reason", typeof(string)] = reason;
        })
        .PerformCommand();

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
