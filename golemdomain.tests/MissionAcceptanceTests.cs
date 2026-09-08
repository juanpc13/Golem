using System.Globalization;
using Choreography.Theater;
using GolemHost.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;

namespace GolemHost.Domain.Tests;

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

    [TestInitialize]
    public void AGolemIsBorn()
    {
        // The DSL renders numbers into the journal with the current culture: pin it.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        // One actor and one store per test: the in-memory store is shared by name.
        string name = "golem-under-test-" + Guid.NewGuid().ToString("N");
        perf = new PerformanceV2(name, GolemDomain.Assembly);
        perf.ConfigureStorage(DatabaseType.IN_MEMORY, name);
        perf.Start();
        perf.Actor.Using(@"
            upgrade('init') {
                g = Golem();
                g.Embody(0.25);
                g.Cruise(2.0);
                g.Linger(6);
            }
            upgrade('map_v1') {
                g.Chart('kitchen', 0, 8, 4, 3).DoorTo('north', 4, 9.5).DoorTo('west', 0.75, 8);
                g.Chart('north',   4, 8, 3, 3).DoorTo('storage', 7, 9.5).OpenTo('center');
                g.Chart('storage', 7, 8, 4, 3).DoorTo('east', 10.25, 8);
                g.Chart('west',    0, 3, 1.5, 5).DoorTo('living', 0.75, 3);
                g.Chart('center',  4, 3, 3, 5).OpenTo('south');
                g.Chart('east',    9.5, 3, 1.5, 5).DoorTo('garage', 10.25, 3);
                g.Chart('living',  0, 0, 4, 3).DoorTo('south', 4, 1.5);
                g.Chart('south',   4, 0, 3, 3).DoorTo('garage', 7, 1.5);
                g.Chart('garage',  7, 0, 4, 3);
            }
        ")
        .PerformCommand();
    }

    [TestCleanup]
    public void TheGolemRests() => perf.Dispose();

    // ---- the body ----

    [TestMethod]
    public void TheBody_IsReleasedIntoTheJournal_SizeSpeedAndLinger()
    {
        Assert.AreEqual(0.25, Double("g.Radius()"), 0.001, "the body's size");
        Assert.AreEqual(2.0, Double("g.Speed()"), 0.001, "its cruise speed");
        Assert.AreEqual(6.0, Double("g.LingerAfterTold()"), 0.001, "its linger at a told stop");
        Refuses("g.Embody(0.0);", "radius above zero");
        Refuses("g.Cruise(0.0);", "cruise speed above zero");
        Refuses("g.Linger(-1.0);", "cannot be negative");
    }

    // ---- the map ----

    [TestMethod]
    public void TheMap_IsChartedFromTheReleaseChain_OneFluentChainPerPlace()
    {
        Assert.AreEqual(9, Int("g.Places()"));
        Assert.AreEqual(10, Int("g.Passages()"), "eight doors and two open boundaries");
        Assert.AreEqual("kitchen", Text("g.PlaceAt(2.0, 9.5)"));
        Assert.AreEqual("center", Text("g.PlaceAt(5.5, 5.5)"));
        Assert.AreEqual("west", Text("g.PlaceAt(0.75, 5.5)"));
        Assert.IsFalse(Bool("g.IsOnMap(3.0, 5.0)"), "the left block is solid: nowhere on the map");
        Assert.IsFalse(Bool("g.IsOnMap(11.5, 5.0)"), "beyond the east wall");
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
        double road = Double("g.Distance('kitchen', 'garage')");
        Assert.AreEqual(throughTheCenter, road, 0.01);
        Assert.IsTrue(road < aroundByTheWest, "the ring is the long way round");
    }

    [TestMethod]
    public void TheShortestRoad_TakesTheCorridor_WhenThatIsShorter()
    {
        // kitchen (2,9.5) -> door (0.75,8) -> west corridor -> door (0.75,3) -> living (2,1.5)
        double byTheWestCorridor = 2 * Math.Sqrt(3.8125) + 5.0;                  // ~8.9
        double throughTheCenter = 2.0 + Math.Sqrt(4.5) + 5.0 + Math.Sqrt(4.5) + 2.0; // ~13.2
        double road = Double("g.Distance('kitchen', 'living')");
        Assert.AreEqual(byTheWestCorridor, road, 0.01);
        Assert.IsTrue(road < throughTheCenter, "for this pair the corridor beats the shortcut");
    }

    [TestMethod]
    public void TheGolem_TellsAKnownWallFromAnythingElse_ByItsMap()
    {
        // a point the body touched: on a boundary the map holds as a wall, or not
        Assert.IsTrue(Bool("g.KnowsWallAt(0.05, 5.5)"), "the corridor's outer wall");
        Assert.IsTrue(Bool("g.KnowsWallAt(4.0, 8.5)"), "the kitchen's east wall, by the door's jamb");
        Assert.IsTrue(Bool("g.KnowsWallAt(4.05, 8.0)"), "the corner of the left block, met through the walls that end there");
        Assert.IsFalse(Bool("g.KnowsWallAt(10.25, 5.9)"), "the middle of the east corridor: whatever stands there is not on the map");
        Assert.IsFalse(Bool("g.KnowsWallAt(5.5, 8.0)"), "the open boundary north~center is not a wall");
        Assert.IsFalse(Bool("g.KnowsWallAt(5.5, 5.5)"), "the middle of the center hall");
    }

    // ---- entrusting: MoveTo, Cover, Follow ----

    [TestMethod]
    public void MoveTo_APoint_IsPending_AndIsTheOperators()
    {
        MoveTo(1, 2.0, 1.5);

        Assert.AreEqual(1, Int("g.Pending()"));
        Assert.IsTrue(Bool("g.IsPending(1)"));
        Assert.AreEqual(2.0, Double("g.NextX()"));
        Assert.IsFalse(Bool("g.IsFollowing(1)"), "the operator ordered it");
        Assert.AreEqual(1, Int("g.StopsLeft(1)"));
    }

    [TestMethod]
    public void MoveTo_APlace_HeadsForItsCenter()
    {
        MoveTo(1, "storage");

        Assert.AreEqual(9.0, Double("g.NextX()"), 0.001);
        Assert.AreEqual(9.5, Double("g.NextY()"), 0.001);
        Refuses("g.MoveTo(2, 'attic');", "unknown place");
    }

    [TestMethod]
    public void MoveTo_SeveralStops_WalksThemInTheGivenOrder()
    {
        MoveTo(1, new[] { "garage", "kitchen", "storage" });
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
        MoveTo(1, new[] { "kitchen", "9,8" });
        Assert.AreEqual(2, Int("g.StopsLeft(1)"));

        Refuses("g.MoveTo(2, {'attic'});", "neither a place nor a point");
        Refuses("g.MoveTo(2, {'3,5'});", "neither a place nor a point x,y on the map");
        Refuses("g.MoveTo(2, 3.0, 5.0);", "nowhere on the map");
        Assert.IsFalse(Bool("g.AreStops({'kitchen', 'attic'})"));
        Assert.IsTrue(Bool("g.AreStops({'kitchen', '9,8'})"));
        Assert.AreEqual(1, Int("g.Total()"));
    }

    [TestMethod]
    public void Follow_TakesAPointAPeerReached_WithAHandleOfItsOwn()
    {
        MoveTo(1, 2.0, 1.5);

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
        MoveTo(1, 2.0, 1.5);   // the living room
        Assert.AreEqual("kitchen/west@0.75,8 > west/living@0.75,3 > living@2,1.5", Text("g.Plan(1, 2.0, 9.5)"));

        MoveTo(2, 9.0, 1.5);   // the garage
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
        MoveTo(1, 2.0, 1.5);
        Route(1, Text("g.Plan(1, 2.0, 9.5)"));

        Assert.IsTrue(Bool("g.IsRouted(1)"));
        Assert.AreEqual(3, Int("g.LegsLeft(1)"));
        Assert.AreEqual("kitchen/west", Text("g.NextPassage(1)"));
        Assert.IsFalse(Bool("g.NextIsStop(1)"));
        Assert.AreEqual(0.75, Double("g.NextX()"), 0.001, "the body heads to the first door, not to the stop");

        Cross(1, "kitchen/west");
        Assert.AreEqual(2, Int("g.LegsLeft(1)"));
        Assert.AreEqual(3.0, Double("g.NextY()"), 0.001);
        Refuses("g.Cross(1, 'kitchen/west');", "is heading to 'west/living'");
        Refuses("g.Reach(1, 0.75, 3.0);", "cross it, no stop is next");

        Cross(1, "west/living");
        Assert.AreEqual(1, Int("g.LegsLeft(1)"));
        Assert.IsTrue(Bool("g.NextIsStop(1)"));
        Assert.AreEqual("living", Text("g.NextPassage(1)"), "the last leg is the stop, named by its place");
        Refuses("g.Cross(1, 'living');", "a stop, not a passage");
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
        MoveTo(1, new[] { "north", "south" });
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
        MoveTo(1, 2.0, 1.5);
        Route(1, Text("g.Plan(1, 2.0, 9.5)"));

        Assert.AreEqual(0.75, Double("g.NextX()"), 0.001, "the leg IS the door");
        Assert.AreEqual(8.0, Double("g.NextY()"), 0.001);
        Assert.AreEqual(0.75, Double("g.NextApproachX()"), 0.001, "the approach stands right in front of the door");
        Assert.AreEqual(8.6, Double("g.NextApproachY()"), 0.001, "0.6 into the kitchen, the side the body comes from");
        Assert.AreEqual(0.75, Double("g.NextExitX()"), 0.001);
        Assert.AreEqual(7.4, Double("g.NextExitY()"), 0.001, "0.6 into the corridor, the side it goes to");

        Cross(1, "kitchen/west");
        Assert.AreEqual(3.6, Double("g.NextApproachY()"), 0.001, "the next door, west/living at y=3, is approached from the corridor");
        Assert.AreEqual(2.4, Double("g.NextExitY()"), 0.001);

        Cross(1, "west/living");
        Assert.AreEqual(2.0, Double("g.NextApproachX()"), 0.001, "a stop has no wall to clear: approach, exit and point coincide");
        Assert.AreEqual(1.5, Double("g.NextExitY()"), 0.001);
    }

    // ---- the ending: Fail, Abandon, Announce ----

    [TestMethod]
    public void AFailedMission_KeepsItsReason_AndIsNoLongerPending()
    {
        MoveTo(1, 2.0, 1.5);

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
        MoveTo(1, 9.0, 1.5);                 // the operator: garage
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
        MoveTo(1, 2.0, 1.5);
        MoveTo(2, 9.0, 1.5);
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
        MoveTo(1, 2.0, 1.5);
        Refuses("g.Abandon(1, '');", "needs a reason");
    }

    [TestMethod]
    public void AReachedStop_CanBeAnnounced_AndAMissionWithoutOneCannot()
    {
        MoveTo(1, 2.0, 9.5);
        Route(1, Text("g.Plan(1, 2.0, 9.5)"));   // already there: the stop alone
        Reach(1, 2.0, 9.5);
        MoveTo(2, 9.0, 1.5);

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
        MoveTo(1, 5.5, 9.5);       // north
        Follow(5.5, 5.5);          // center, a followed point: one linger
        MoveTo(3, 5.5, 1.5);       // south
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
        MoveTo(1, 5.5, 9.5);
        Follow(5.5, 5.5);
        MoveTo(3, 5.5, 1.5);

        Assert.AreEqual(8.0, Double("g.RouteLength()"), 0.01);
        Assert.AreEqual(8.0 / 2.0 + 6.0, Double("g.RouteSeconds()"), 0.01);
    }

    [TestMethod]
    public void WithNothingPending_TheRoadLeftIsZero()
    {
        MoveTo(1, 9.0, 1.5);
        Abandon(1, "the test is over");

        Assert.AreEqual(0.0, Double("g.DistanceLeft(2.0, 9.5)"), 0.001);
    }

    // ---- helpers: the same perform shapes the host uses ----

    private void MoveTo(int id, double x, double y) =>
        perf.Actor.Using(@"
            g.MoveTo(@id, @x, @y);
        ")
        .WithParameters(p => {
            p["id", typeof(int)]    = id;
            p["x",  typeof(double)] = x;
            p["y",  typeof(double)] = y;
        })
        .PerformCommand();

    private void MoveTo(int id, string place) =>
        perf.Actor.Using(@"
            g.MoveTo(@id, @place);
        ")
        .WithParameters(p => {
            p["id",    typeof(int)]    = id;
            p["place", typeof(string)] = place;
        })
        .PerformCommand();

    private void MoveTo(int id, string[] stops) =>
        perf.Actor.Using(@"
            g.MoveTo(@id, @stops);
        ")
        .WithParameters(p => {
            p["id",    typeof(int)]      = id;
            p["stops", typeof(string[])] = stops;
        })
        .PerformCommand();

    private void Cover(int id, string[] stops) =>
        perf.Actor.Using(@"
            g.Cover(@id, @stops);
        ")
        .WithParameters(p => {
            p["id",    typeof(int)]      = id;
            p["stops", typeof(string[])] = stops;
        })
        .PerformCommand();

    private void Follow(double x, double y) =>
        perf.Actor.Using(@"
            g.Follow(@x, @y);
        ")
        .WithParameters(p => {
            p["x", typeof(double)] = x;
            p["y", typeof(double)] = y;
        })
        .PerformCommand();

    private void Route(int id, string plan) =>
        perf.Actor.Using(@"
            g.Route(@id, @plan);
        ")
        .WithParameters(p => {
            p["id",   typeof(int)]    = id;
            p["plan", typeof(string)] = plan;
        })
        .PerformCommand();

    private void Cross(int id, string passage) =>
        perf.Actor.Using(@"
            g.Cross(@id, @passage);
        ")
        .WithParameters(p => {
            p["id",      typeof(int)]    = id;
            p["passage", typeof(string)] = passage;
        })
        .PerformCommand();

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
