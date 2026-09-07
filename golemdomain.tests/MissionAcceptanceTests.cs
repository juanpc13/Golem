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
                g.Embody(2.0);
                g.Pace(6);
            }
            upgrade('map_v1') {
                g.AddPlace('kitchen', 0, 8, 4, 3).Door('north', 4, 9.5).Door('west', 0.75, 8);
                g.AddPlace('north',   4, 8, 3, 3).Door('storage', 7, 9.5).Open('center');
                g.AddPlace('storage', 7, 8, 4, 3).Door('east', 10.25, 8);
                g.AddPlace('west',    0, 3, 1.5, 5).Door('living', 0.75, 3);
                g.AddPlace('center',  4, 3, 3, 5).Open('south');
                g.AddPlace('east',    9.5, 3, 1.5, 5).Door('garage', 10.25, 3);
                g.AddPlace('living',  0, 0, 4, 3).Door('south', 4, 1.5);
                g.AddPlace('south',   4, 0, 3, 3).Door('garage', 7, 1.5);
                g.AddPlace('garage',  7, 0, 4, 3);
            }
            upgrade('size_v1') {
                g.Measure(0.25);
            }
        ")
        .PerformCommand();
    }

    [TestCleanup]
    public void TheGolemRests() => perf.Dispose();

    // ---- the map ----

    [TestMethod]
    public void TheMap_IsLearnedFromTheReleaseChain_OneFluentChainPerPlace()
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
    public void APlan_ReadsLikeTheJournalWillWriteIt_AndNamesEveryPassage()
    {
        Assign(1, 2.0, 1.5);   // the living room
        Assert.AreEqual("kitchen/west@0.75,8 > west/living@0.75,3 > living@2,1.5", Text("g.Plan(1, 2.0, 9.5)"));

        Assign(2, 9.0, 1.5);   // the garage
        Complete(1);
        string plan = Text("g.Plan(2, 2.0, 9.5)");
        StringAssert.StartsWith(plan, "kitchen/north@4,9.5 > ");
        StringAssert.Contains(plan, "north~center@");
        StringAssert.Contains(plan, "center~south@");
        StringAssert.EndsWith(plan, "> south/garage@7,1.5 > garage@9,1.5");
    }

    [TestMethod]
    public void RoutingAndPassing_AdvanceTheLegs_AndAWrongPassageIsRefused()
    {
        Assign(1, 2.0, 1.5);
        Route(1, Text("g.Plan(1, 2.0, 9.5)"));

        Assert.IsTrue(Bool("g.IsRouted(1)"));
        Assert.AreEqual(3, Int("g.LegsLeft(1)"));
        Assert.AreEqual("kitchen/west", Text("g.NextPassage(1)"));
        Assert.AreEqual(0.75, Double("g.NextX()"), 0.001, "the body heads to the first door, not to the goal");

        Pass(1, "kitchen/west");
        Assert.AreEqual(2, Int("g.LegsLeft(1)"));
        Assert.AreEqual(3.0, Double("g.NextY()"), 0.001);

        Refuses("g.Pass(1, 'kitchen/west');", "is heading to 'west/living'");

        Pass(1, "west/living");
        Assert.AreEqual(1, Int("g.LegsLeft(1)"));
        Assert.AreEqual(2.0, Double("g.NextX()"), 0.001, "the last leg is the goal");
        Refuses("g.Pass(1, 'living');", "only the goal left");

        Assert.AreEqual("", Complete(1));
    }

    [TestMethod]
    public void ADoor_IsCrossedStraight_LiningUpOffTheWallOnBothSides()
    {
        // kitchen (2,9.5) -> door kitchen/west at (0.75,8) on a horizontal wall -> west corridor -> door (0.75,3) -> living
        Assign(1, 2.0, 1.5);
        Route(1, Text("g.Plan(1, 2.0, 9.5)"));

        Assert.AreEqual(0.75, Double("g.NextX()"), 0.001, "the leg IS the door");
        Assert.AreEqual(8.0, Double("g.NextY()"), 0.001);
        Assert.AreEqual(0.75, Double("g.NextApproachX()"), 0.001, "the approach stands right in front of the door");
        Assert.AreEqual(8.6, Double("g.NextApproachY()"), 0.001, "0.6 into the kitchen, the side the body comes from");
        Assert.AreEqual(0.75, Double("g.NextExitX()"), 0.001);
        Assert.AreEqual(7.4, Double("g.NextExitY()"), 0.001, "0.6 into the corridor, the side it goes to");

        Pass(1, "kitchen/west");
        Assert.AreEqual(3.6, Double("g.NextApproachY()"), 0.001, "the next door, west/living at y=3, is approached from the corridor");
        Assert.AreEqual(2.4, Double("g.NextExitY()"), 0.001);

        Pass(1, "west/living");
        Assert.AreEqual(2.0, Double("g.NextApproachX()"), 0.001, "the goal has no wall to clear: approach, exit and point coincide");
        Assert.AreEqual(1.5, Double("g.NextExitY()"), 0.001);
    }

    [TestMethod]
    public void AMissionToAPlace_HeadsForItsCenter()
    {
        perf.Actor.Using(@"
            g.AssignPlace(@id, @place);
        ")
        .WithParameters(p => {
            p["id",    typeof(int)]    = 1;
            p["place", typeof(string)] = "storage";
        })
        .PerformCommand();

        Assert.AreEqual(9.0, Double("g.NextX()"), 0.001);
        Assert.AreEqual(9.5, Double("g.NextY()"), 0.001);
        Refuses("g.AssignPlace(2, 'attic');", "unknown place");
    }

    [TestMethod]
    public void APlaceToldByAPeer_IsAssignedTold_ToItsCenter()
    {
        perf.Actor.Using(@"
            g.AssignToldPlace(@place);
        ")
        .WithParameters(p => {
            p["place", typeof(string)] = "kitchen";
        })
        .PerformCommand();

        Assert.IsTrue(Bool("g.WasTold(1)"));
        Assert.AreEqual(2.0, Double("g.NextX()"), 0.001);
        Assert.AreEqual(9.5, Double("g.NextY()"), 0.001);
    }

    [TestMethod]
    public void AStaleToldPoint_IsSupersededByANewerOne_ButAnOperatorPointNeverIs()
    {
        Assign(1, 9.0, 1.5);                 // the operator: garage
        AssignTold(2.0, 9.5);                // the leader was in the kitchen...
        Assert.IsFalse(Bool("g.HasNewerTold(2)"));

        AssignTold(9.0, 9.5);                // ...and now says it is in the storage
        Assert.IsTrue(Bool("g.HasNewerTold(2)"));
        Assert.AreEqual(3, Int("g.NewestToldId()"));

        perf.Actor.Using(@"
            g.Supersede(@id, @by);
        ")
        .WithParameters(p => {
            p["id", typeof(int)] = 2;
            p["by", typeof(int)] = 3;
        })
        .PerformCommand();

        Assert.AreEqual("superseded", Text("g.StatusOf(2)"));
        Assert.IsFalse(Bool("g.IsPending(2)"));
        Assert.AreEqual(2, Int("g.Pending()"), "the operator's garage and the newest told point remain");
        Refuses("g.Supersede(1, 3);", "only told points are superseded");
        Refuses("g.Supersede(3, 2);", "is not a newer pending told point");
    }

    [TestMethod]
    public void APointInsideASolidBlock_IsRefused()
    {
        Refuses("g.Assign(1, 3.0, 5.0);", "nowhere on the map");
        Assert.AreEqual(0, Int("g.Total()"));
    }

    // ---- missions ----

    [TestMethod]
    public void AnEntrustedMission_IsPending()
    {
        Assign(1, 2.0, 1.5);

        Assert.AreEqual(1, Int("g.Pending()"));
        Assert.IsTrue(Bool("g.IsPending(1)"));
        Assert.AreEqual(2.0, Double("g.NextX()"));
        Assert.IsFalse(Bool("g.WasTold(1)"), "the operator ordered it");
    }

    [TestMethod]
    public void CompletingAMission_SettlesIt_AndTheCheckRefusesASecondCompletion()
    {
        Assign(1, 2.0, 1.5);

        string first = Complete(1);
        long entriesAfterFirst = perf.CurrentEntryId;
        string second = Complete(1);

        Assert.AreEqual("", first, "the first completion is accepted");
        Assert.AreNotEqual("", second, "a settled mission refuses another completion");
        Assert.AreEqual(entriesAfterFirst, perf.CurrentEntryId, "a refused check leaves no journal entry");
        Assert.AreEqual(0, Int("g.Pending()"));
    }

    [TestMethod]
    public void AFailedMission_KeepsItsReason_AndIsNoLongerPending()
    {
        Assign(1, 2.0, 1.5);

        perf.Actor.Using(@"
            g.Fail(@id, @reason);
        ")
        .WithParameters(p => {
            p["id",     typeof(int)]    = 1;
            p["reason", typeof(string)] = "stuck against a wall";
        })
        .PerformCommand();

        Assert.IsFalse(Bool("g.IsPending(1)"));
        Assert.IsFalse(Bool("g.HasPendingMission()"));
        Assert.AreEqual("failed", Text("g.StatusOf(1)"));
    }

    [TestMethod]
    public void APointToldByAPeer_IsAssignedTold_WithItsOwnHandle()
    {
        Assign(1, 2.0, 1.5);

        AssignTold(8.5, 2.5);

        Assert.AreEqual(2, Int("g.Total()"));
        Assert.IsTrue(Bool("g.Knows(2)"), "the told point got handle 2");
        Assert.IsTrue(Bool("g.WasTold(2)"), "an AssignTold mission remembers it was told");
        Assert.AreEqual(3, Int("g.NextHandle()"));
    }

    [TestMethod]
    public void TheRoadLeft_RunsThroughEveryPendingPoint_AlongThePassages_AndTheTimeCountsTheHolds()
    {
        Assign(1, 5.5, 9.5);       // north
        AssignTold(5.5, 5.5);      // center, a told point: one pause
        Assign(3, 5.5, 1.5);       // south
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

        Assert.AreEqual(route, rented["route"].GetValue<double>(), 0.01, "point to point through the passages");
        Assert.AreEqual(firstLeg + route, rented["left"].GetValue<double>(), 0.01, "plus the way from where the body stands");
        Assert.AreEqual((firstLeg + route) / 2.0 + 6.0, rented["eta"].GetValue<double>(), 0.01, "at the body's 2 units/s, plus its 6 s pause at the told point");
        Assert.AreEqual(entriesBefore, perf.CurrentEntryId, "a query leaves no entry: the pose never touches the journal");
    }

    [TestMethod]
    public void WithNothingPending_TheRoadLeftIsZero()
    {
        Assign(1, 9.0, 1.5);
        Complete(1);

        Assert.AreEqual(0.0, Double("g.DistanceLeft(2.0, 9.5)"), 0.001);
    }

    [TestMethod]
    public void TheRoute_IsAnsweredWithNoParameters_AtTheBodysOwnSpeedAndPauses()
    {
        Assign(1, 5.5, 9.5);
        AssignTold(5.5, 5.5);
        Assign(3, 5.5, 1.5);

        Assert.AreEqual(2.0, Double("g.Speed()"), 0.001, "the body release");
        Assert.AreEqual(6.0, Double("g.HoldAfterTold()"), 0.001, "the pace release");
        Assert.AreEqual(8.0, Double("g.RouteLength()"), 0.01);
        Assert.AreEqual(8.0 / 2.0 + 6.0, Double("g.RouteSeconds()"), 0.01);
    }

    [TestMethod]
    public void ABodyWithoutSpeed_IsRefused() => Refuses("g.Embody(0.0);", "speed above zero");

    [TestMethod]
    public void TheGolem_KnowsItsSize_AndTellsAKnownWallFromAnythingElse()
    {
        Assert.AreEqual(0.25, Double("g.Radius()"), 0.001, "the size release");
        Refuses("g.Measure(0.0);", "radius above zero");

        // a point the body touched: on a boundary the map holds as a wall, or not
        Assert.IsTrue(Bool("g.KnowsWallAt(0.05, 5.5)"), "the corridor's outer wall");
        Assert.IsTrue(Bool("g.KnowsWallAt(4.0, 8.5)"), "the kitchen's east wall, by the door's jamb");
        Assert.IsTrue(Bool("g.KnowsWallAt(4.05, 8.0)"), "the corner of the left block, met through the walls that end there");
        Assert.IsFalse(Bool("g.KnowsWallAt(10.25, 5.9)"), "the middle of the east corridor: whatever stands there is not on the map");
        Assert.IsFalse(Bool("g.KnowsWallAt(5.5, 8.0)"), "the open boundary north~center is not a wall");
        Assert.IsFalse(Bool("g.KnowsWallAt(5.5, 5.5)"), "the middle of the center hall");
    }

    [TestMethod]
    public void ACompletedPoint_CanBeAnnounced_AndAPendingOneCannot()
    {
        Assign(1, 2.0, 1.5);
        Assign(2, 9.0, 1.5);
        Complete(1);

        perf.Actor.Using(@"
            g.Announce(@id);
        ")
        .WithParameters(p => {
            p["id", typeof(int)] = 1;
        })
        .PerformCommand();

        Assert.IsTrue(Bool("g.WasAnnounced(1)"));
        Assert.IsFalse(Bool("g.WasAnnounced(2)"));
        Refuses("g.Announce(2);", "only a completed point is announced");
    }

    [TestMethod]
    public void RetiringLetsGoOfEveryMission_ButNeverReusesAHandle()
    {
        Assign(1, 2.0, 1.5);
        Assign(2, 9.0, 1.5);

        perf.Actor.Using(@"
            g.Retire(@reason);
        ")
        .WithParameters(p => {
            p["reason", typeof(string)] = "the test is over";
        })
        .PerformCommand();

        Assert.AreEqual(0, Int("g.Total()"));
        Assert.AreEqual(3, Int("g.NextHandle()"), "a spent handle is never minted again: the idempotency keys hang on it");
    }

    // ---- helpers: the same perform shapes the host uses ----

    private void Assign(int id, double x, double y) =>
        perf.Actor.Using(@"
            g.Assign(@id, @x, @y);
        ")
        .WithParameters(p => {
            p["id", typeof(int)]    = id;
            p["x",  typeof(double)] = x;
            p["y",  typeof(double)] = y;
        })
        .PerformCommand();

    private void AssignTold(double x, double y) =>
        perf.Actor.Using(@"
            g.AssignTold(@x, @y);
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

    private void Pass(int id, string passage) =>
        perf.Actor.Using(@"
            g.Pass(@id, @passage);
        ")
        .WithParameters(p => {
            p["id",      typeof(int)]    = id;
            p["passage", typeof(string)] = passage;
        })
        .PerformCommand();

    private string Complete(int id) =>
        perf.Actor.Using(
            @"
                Check(g.Knows(@id) && g.IsPending(@id)) Error 'mission is not pending';
            ",
            @"
                g.Complete(@id);
            ")
        .WithParameters(p => {
            p["id", typeof(int)] = id;
        })
        .PerformCheckThenCommand();

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
