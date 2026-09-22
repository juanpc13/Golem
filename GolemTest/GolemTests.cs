using GolemDomain;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Touches;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static GolemTest.DomainFixture;

namespace GolemTest;

// THE GOLEM (GolemDomain.Golem): the mind that RECEIVES its body, its map and its collisions module, hands out routes (Visit,
// Cover, Follow), keeps where its body stands (Standing, Wake), takes what the body reports (Bump) and what the peers tell
// (HearBump, LearnForget), holds and releases itself (Pause, Resume), and answers over the fleet of its routes.
[TestClass]
public class GolemTests
{
    private Golem g;
    private MapLayout map;
    private Collisions collisions;

    [TestInitialize]
    public void AGolemIsBorn() => (g, map, collisions) = Born();

    // ---- entrusting: Visit, Cover, Follow ----

    [TestMethod]
    public void Visit_APoint_HandsOutARouteThatIsPending_AndIsTheOperators()
    {
        var route = g.Visit(P(2.0, 9.5), P(2.0, 1.5));
        Assert.AreEqual(1, route.Id, "the handle is minted inside");
        Assert.AreEqual(1, g.PendingRoutes().Count);
        Assert.IsTrue(g.Knows(1));
        Assert.AreSame(route, g.Find(1), "and found again by it");
        Assert.AreSame(route, g.Underway(), "the first pending route is the one underway");
        Assert.IsTrue(route.IsPending());
        StringAssert.EndsWith(route.AsPlan(), "living@2,1.5", "the way, decided inside, ends at the point");
        Assert.AreEqual(0.75, g.Underway().NextLeg.Target.X, "the first thing is the first leg of the way: the door out of the kitchen");
        Assert.IsFalse(route.Following, "the operator ordered it");
        Assert.AreEqual(1, route.StopsLeft);
    }

    [TestMethod]
    public void Visit_APlace_HeadsForItsCentre_AndAnUnknownPlaceOrAPointOffTheMapIsRefused()
    {
        var route = g.Visit(P(2.0, 9.5), map.Find("storage"));
        StringAssert.EndsWith(route.AsPlan(), "storage@9,9.5", "the way ends at the area's centre");
        Assert.AreEqual(1, route.StopsLeft, "the place is the route's one stop");
        Refuses(() => map.Find("attic"), "attic");
        Refuses(() => g.Visit(P(2.0, 9.5), P(3.0, 5.0)), "nowhere on the map");
        Assert.IsTrue(map.Knows("kitchen"));
        Assert.IsFalse(map.Knows("attic"));
        Assert.AreEqual(1, g.Routes().Count, "a refused errand mints nothing");
    }

    [TestMethod]
    public void Visit_WhenNoWayFits_IsRefusedBeforeAnythingIsMinted()
    {
        PlantMark(collisions, 4.0, 9.2, East);        // marks close the kitchen/north doorway…
        PlantMark(collisions, 4.0, 9.8, East);
        PlantMark(collisions, 0.75, 8.2, South);      // …and the kitchen/west one
        PlantMark(collisions, 0.75, 7.8, South);
        Refuses(() => g.Visit(P(2.0, 9.5), P(9.0, 9.5)), "no road");
        Assert.AreEqual(0, g.Routes().Count, "nothing minted, no handle spent");
    }

    [TestMethod]
    public void Cover_HandsOutARouteWhoseOrderOfStopsTheGolemChooses()
    {
        var route = g.Cover(P(2.0, 1.5), map.Find("garage"));
        Assert.IsTrue(route.ChoosesOrder);
        Assert.IsFalse(g.Visit(P(2.0, 1.5), map.Find("garage")).ChoosesOrder, "a Visit keeps the operator's order");
    }

    [TestMethod]
    public void Follow_TakesAPointAPeerReached_WithAHandleOfItsOwn_AndItFollows()
    {
        g.Visit(P(2.0, 9.5), P(2.0, 1.5));
        var followed = g.Follow(P(8.5, 2.5));
        Assert.AreEqual(2, g.Routes().Count, "two routes, handles 1 and 2, minted inside");
        Assert.AreEqual(2, followed.Id, "the followed point got handle 2");
        Assert.IsTrue(followed.Following, "a followed route remembers where it came from");
        Refuses(() => followed.Then(P(9.0, 1.5)), "a told point is one route each");
    }

    // ---- where the golem knows its body stands (18-sep-2026) ----

    [TestMethod]
    public void TheGolem_LearnsWhereItsBodyStands_FromEveryActThatBringsThePose()
    {
        Assert.IsFalse(g.KnowsWhereItStands, "born, it knows nothing of where its body is");
        var route = g.Visit(P(2.0, 9.5), P(2.0, 1.5));       // the errand opens from the kitchen's centre: where the body stands
        Assert.IsTrue(g.KnowsWhereItStands);
        Assert.AreEqual(2.0, g.Standing.X, 1e-9);
        Assert.AreEqual(9.5, g.Standing.Y, 1e-9);
        Step(route);                                          // the body did the first thing asked…
        Step(route);                                          // …and the next: it reported where it stood each time
        Assert.AreEqual(route.Standing.X, g.Standing.X, 1e-9, "the route told its golem where the body stood");
        Assert.AreEqual(route.Standing.Y, g.Standing.Y, 1e-9);
        Assert.AreEqual(route.Standing.Heading, g.Standing.Heading, 1e-9);
    }

    [TestMethod]
    public void AToldPoint_IsPlannedAtOnce_FromWhereTheGolemKnowsItsBodyStands()
    {
        g.Wake(At(2.0, 9.5, East));                           // the body woke in the kitchen, facing east
        var route = g.Follow(P(5.5, 9.5));                    // the leader says it reached the north hall
        Assert.IsTrue(route.IsRouted, "planned on arrival: " + route.AsPlan());
        StringAssert.EndsWith(route.AsPlan(), "north@5.5,9.5", "from the kitchen, through its door, to the told point");
        Assert.AreEqual("advance", route.Order, "facing east already, the first order is to the motors");
        Assert.IsTrue(route.Amount > 0.0);
    }

    [TestMethod]
    public void AToldPoint_WhileBusy_IsPlannedFromWhereTheWayUnderwayEnds()
    {
        g.Visit(P(2.0, 9.5), P(2.0, 1.5));                    // to the living room, from the kitchen
        var followed = g.Follow(P(9.0, 1.5));                 // the leader is in the garage
        Assert.IsTrue(followed.IsRouted);
        StringAssert.Contains(followed.AsPlan(), "@4,1.5", "from where route 1 ends — the living room — through its door to the south: " + followed.AsPlan());
        StringAssert.EndsWith(followed.AsPlan(), "garage@9,1.5");
    }

    [TestMethod]
    public void AToldPoint_BeforeAnyActBroughtThePose_IsRefused()
    {
        Refuses(() => g.Follow(P(5.5, 9.5)), "does not know where its body stands");
        Assert.AreEqual(0, g.Routes().Count, "nothing minted");
    }

    [TestMethod]
    public void Awake_TheGolemKeepsWhereItsBodyStands_AndAPlanUnderway_IsDecidedAgainFromThere()
    {
        var route = g.Visit(P(2.0, 9.5), P(9.0, 9.5));        // to the storage, from the kitchen: east through the north hall
        StringAssert.StartsWith(route.AsPlan(), "kitchen/");
        Assert.IsTrue(g.Wake(At(2.0, 1.5, North)), "reborn: the body was carried to the living room meanwhile — the plan was decided again");
        Assert.AreEqual(2.0, g.Standing.X, 1e-9);
        Assert.AreEqual(1.5, g.Standing.Y, 1e-9);
        StringAssert.StartsWith(route.AsPlan(), "living/", "the way underway was decided again from the living room, inside: " + route.AsPlan());
        StringAssert.StartsWith(route.Order, "turn", "facing north, the way east asks a turn first");
    }

    [TestMethod]
    public void Awake_WithNothingUnderway_OnlyKeepsWhereItStands()
    {
        Assert.IsFalse(g.Wake(At(5.5, 5.5, East)), "nothing to decide again");
        Assert.IsTrue(g.KnowsWhereItStands);
        Assert.AreEqual(East, g.Standing.Heading, 1e-9);
        Assert.IsFalse(g.HasPendingMission());
        Assert.AreEqual(0, g.Routes().Count);
    }

    [TestMethod]
    public void Awake_WhileHeld_TheWayUnderwayStaysAsItWas()
    {
        var route = g.Visit(P(2.0, 9.5), P(9.0, 9.5));
        string plan = route.AsPlan();
        g.Pause(At(2.0, 9.5, East));
        Assert.IsFalse(g.Wake(At(2.0, 1.5, North)), "held: nothing is decided again");
        Assert.AreEqual(plan, route.AsPlan(), "the body stands where it is, the way keeps");
        Assert.AreEqual("stop", route.Order);
        Assert.AreEqual(1.5, g.Standing.Y, 1e-9, "but the golem knows where its body stands now");
    }

    // ---- the touches: the bump is the golem's, the peers' words are heard ----

    [TestMethod]
    public void TheBump_IsTheGolems_ItReckonsTheTouchFromTheBody_FindsItsRouteUnderway_AndTheRouteCorrectsInside()
    {
        Refuses(() => g.Bump(At(10.25, 6.1, South), 0.0), "nothing underway");
        var route = g.Visit(At(9.0, 9.5, South), P(9.0, 1.5));  // down the east corridor
        // the body stood at (10.25, 6.1) facing south and was pressed on the nose: the golem reckons the touch one radius ahead
        Assert.AreSame(route, g.Bump(At(10.25, 6.1, South), 0.0), "the golem handed the touch to its route underway and hands it back");
        Assert.AreEqual(1, route.Bumps);
        Assert.AreEqual(1, collisions.MarkCount);
        Assert.IsTrue(collisions.KnowsAt(P(10.25, 5.85)), "the mark where the touch landed: the body's radius ahead of where it stood");
        Assert.IsFalse(g.FitsAt(P(10.25, 5.6)), "the thing reaches beyond the mark, into the corridor");
        Assert.AreEqual("back", route.Order, "and the route corrected its way inside: the retreat first");
        Assert.AreEqual(10.25, g.Standing.X, 1e-9, "the bump brought where the body stood");
        Refuses(() => g.Bump(At(1, 1, 0), double.NaN), "must be an angle");
    }

    [TestMethod]
    public void ATouchOnTheLeftFlank_LandsALeftOfTheBody()
    {
        var route = g.Visit(At(5.5, 9.5, South), P(5.5, 1.5));
        g.Bump(At(5.5, 6.0, South), Math.PI / 2);              // pressed on the left flank while facing south: the thing is to the east
        Assert.IsTrue(collisions.KnowsAt(P(5.75, 6.0)), "one radius to the body's left, which facing south is east");
    }

    [TestMethod]
    public void APeersBump_IsHeardAndLearnedAsAMark_UnlessItLiesOnAWallWeBothKnow()
    {
        Assert.AreEqual(1, g.HearBump("blue", At(5.1, 9.5, West), 0.0), "blue says it bumped in the kitchen's doorway, facing west: its touch reckoned at (4.85, 9.5)");
        Assert.AreEqual(1, collisions.MarkCount, "for now every touch is a thing: learned as a mark");
        Assert.AreEqual("blue", collisions.HeardNear(P(4.7, 9.5), 0), "and I know blue was there");
        Assert.AreEqual(2, g.HearBump("red", At(1.22, 2.72, 0.79), 0.0), "red grazed the living room's north wall, as it did live (17-sep)");
        Assert.AreEqual(1, collisions.MarkCount, "heard, but no mark: a wall on the map is nothing learned");
        Refuses(() => g.HearBump("", At(1.0, 1.0, 0.0), 0.0), "who");
    }

    [TestMethod]
    public void TheSameWordHeardTwice_IsHeardOnce()
    {
        g.HearBump("blue", At(5.1, 9.5, West), 0.0);
        Assert.AreEqual(1, collisions.MarkCount);
        g.HearBump("blue", At(5.1, 9.5, West), 0.0);           // the wire says it again (17-sep live: both journals heard the doorway bump twice)
        Assert.AreEqual(1, collisions.HeardCount, "heard once");
        Assert.AreEqual(1, collisions.MarkCount, "and nothing more is learned from the repeat");
    }

    [TestMethod]
    public void ABodyStandingIdle_ThatGetsTouched_CountsItsBumps_WithoutARoute()
    {
        Assert.AreEqual(1, g.Bump(At(4.9, 9.5, 0.0)));
        Assert.AreEqual(2, g.Bump(At(4.9, 9.5, 0.0)));
        Assert.AreEqual(0, collisions.MarkCount, "no mark: things do not move, so it was a body");
    }

    [TestMethod]
    public void MeetingAPeer_TakesTheMarksBack_AndForgettingDropsWhatStoodThere()
    {
        var route = g.Visit(At(2.0, 9.5, East), map.Find("north"));
        g.HearBump("blue", At(5.1, 9.5, West), 0.0);
        Bump(route, 4.7, 9.5, East);                             // my own touch, right there
        Assert.AreEqual(2, collisions.MarkCount, "two marks presumed: blue's bump, heard, and my own");
        Assert.AreEqual(1, g.Met("blue", P(4.7, 9.5)));
        Assert.AreEqual(0, collisions.MarkCount, "meeting a body takes both marks back: mine, and the one I learned from blue's bump there");
        Assert.AreEqual(0, g.LearnMet(P(4.6, 9.5)), "when blue tells it met a body too, nothing is left to take back");

        PlantMark(collisions, 10.25, 5.85, South);
        PlantMark(collisions, 10.25, 5.15, North);
        Assert.AreEqual(2, g.Forget(P(10.2, 5.5)), "the operator says the thing is gone: every mark that outlined it goes");
        Assert.AreEqual(0, g.LearnForget(P(10.2, 5.5)), "a peer says so too: nothing left");
        Refuses(() => g.Forget(null), "needs where");
    }

    // ---- the hold is the golem's ----

    [TestMethod]
    public void TheOperatorHoldsTheGolem_WhereItsBodyStands_AndLetsItGoOn()
    {
        var route = g.Visit(P(2.0, 9.5), P(2.0, 1.5));
        Assert.IsFalse(g.Held);
        Refuses(() => g.Resume(At(2.0, 9.5, 0.0)), "is not paused");
        Assert.AreSame(route, g.Pause(At(2.6, 9.5, North)), "the route underway is held with the golem and handed back");
        Assert.IsTrue(g.Held, "the GOLEM is held, not just an errand");
        Assert.AreEqual(2.6, g.HeldAt.X, 1e-9, "where it was held is kept, by the golem…");
        Assert.AreEqual(2.6, route.HeldAt.X, 1e-9, "…and by the route it interrupted");
        Assert.AreEqual("stop", route.Order);
        Refuses(() => g.Pause(At(2.6, 9.5, 0.0)), "already paused");
        Assert.AreSame(route, g.Resume(At(2.6, 9.5, North)));
        Assert.IsFalse(g.Held, "let go on");
        Assert.IsFalse(route.Paused);
        Reach(route, 2.0, 1.5);
        Refuses(() => g.Pause(At(2.0, 1.5, 0.0)), "no pending mission");
    }

    // ---- the fleet of routes ----

    [TestMethod]
    public void AStaleFollowedPoint_IsOvertakenByANewerOne_AndTheOperatorsPointNeverIs()
    {
        var operators = g.Visit(P(9.0, 9.5), P(9.0, 1.5));     // the operator: the garage, from the storage
        var older = g.Follow(P(2.0, 9.5));                      // the leader was in the kitchen…
        Assert.IsFalse(g.HasNewerFollowing(older));
        var newer = g.Follow(P(9.0, 9.5));                      // …and now says it is in the storage
        Assert.IsTrue(g.HasNewerFollowing(older));
        Assert.AreEqual(newer.Id, g.NewestFollowingId());
        Assert.IsFalse(g.HasNewerFollowing(operators), "the operator's route is never superseded by the followers' rule");
        older.Abandon("superseded by route 3");
        Assert.AreEqual(2, g.PendingRoutes().Count, "the operator's garage and the newest followed point remain");
    }

    [TestMethod]
    public void LettingGo_AbandonsEveryPendingRoute_AndAHandleIsNeverMintedAgain()
    {
        g.Visit(P(2.0, 9.5), P(2.0, 1.5));
        g.Visit(P(2.0, 9.5), P(9.0, 1.5));
        foreach (var route in g.PendingRoutes()) route.Abandon("the operator let go of everything");
        Assert.AreEqual(0, g.PendingRoutes().Count);
        Assert.AreEqual(2, g.Routes().Count, "nothing is erased: the routes stay, abandoned");
        Assert.AreEqual("abandoned", g.Find(1).Status);
        Assert.AreEqual(3, g.Visit(P(2.0, 9.5), P(2.0, 1.5)).Id, "a spent handle is never minted again: the idempotency keys hang on it");
        Assert.AreEqual(3, g.Newest().Id, "the route handed out last");
        Refuses(() => g.Find(9), "unknown route 9");
    }

    [TestMethod]
    public void ThePlannedEnd_IsWhereTheNextErrandStarts_WhileTheGolemIsBusy()
    {
        g.Visit(At(2.0, 9.5, East), P(2.0, 1.5));
        Assert.AreEqual(2.0, g.PlannedEnd().X, 1e-9);
        Assert.AreEqual(1.5, g.PlannedEnd().Y, 1e-9);
        var next = g.Visit(g.PlannedEnd(), P(9.0, 1.5));       // the host opens the next errand from there
        StringAssert.StartsWith(next.AsPlan(), "living/south@4,1.5", "planned from the living room, where the first errand ends");
    }

    // ---- the reads where the body enters the answer ----

    [TestMethod]
    public void TheRoadLeft_RunsThroughEveryStopAhead_AlongThePassages_AndTheTimeCountsTheLingers()
    {
        g.Visit(P(2.0, 9.5), P(5.5, 9.5));                     // north
        g.Follow(P(5.5, 5.5));                                  // centre, a followed point: one linger
        g.Visit(g.PlannedEnd(), P(5.5, 1.5));                   // south
        double road = 4.0 + 4.0;                                // north -> centre -> south, straight across the open boundaries
        double firstLeg = 2.0 + 1.5;                            // from the kitchen centre: door (4,9.5), then the north centre
        Assert.AreEqual(road, g.RouteLength(), 0.01, "stop to stop through the passages");
        Assert.AreEqual(firstLeg + road, g.DistanceLeft(P(2.0, 9.5)), 0.01, "plus the way from where the body stands");
        Assert.AreEqual((firstLeg + road) / 2.0 + 6.0, g.SecondsLeft(P(2.0, 9.5)), 0.01, "at the body's 2 m/s, plus its 6 s linger at the followed stop");
        Assert.AreEqual(road / 2.0 + 6.0, g.RouteSeconds(), 0.01, "answered with no parameters");
    }

    [TestMethod]
    public void WithNothingPending_TheRoadLeftIsZero_AndWithNoRoadItIsAsTheCrowFlies()
    {
        var route = g.Visit(P(9.0, 9.5), P(9.0, 1.5));
        route.Abandon("the test is over");
        Assert.AreEqual(0.0, g.DistanceLeft(P(2.0, 9.5)), 0.001);

        g.Visit(P(9.0, 9.5), P(2.0, 9.5));                     // the kitchen, from the storage
        PlantMark(collisions, 4.0, 9.2, East);                  // marks close the kitchen/north doorway…
        PlantMark(collisions, 4.0, 9.8, East);
        PlantMark(collisions, 0.75, 8.2, South);                // …and the kitchen/west one
        PlantMark(collisions, 0.75, 7.8, South);
        Refuses(() => g.Road(g.Underway(), P(9.0, 9.5)), "no road");
        Assert.AreEqual(7.0, g.DistanceLeft(P(9.0, 9.5)), 0.001, "straight from (9, 9.5) to (2, 9.5): a read never refuses");
        Assert.IsTrue(g.SecondsLeft(P(9.0, 9.5)) > 0);
    }

    [TestMethod]
    public void TheGolem_AnswersForItsBody_WhereItFits_AndHowFarAreasLie()
    {
        Assert.IsTrue(g.FitsAt(P(5.5, 5.5)), "the middle of the centre hall");
        Assert.IsFalse(g.HasRoomAt(P(9.7, 5.5)), "too close to the corridor's wall for the body");
        Assert.IsTrue(g.HasRoomAt(P(10.25, 5.5)));
        PlantMark(collisions, 10.25, 5.85, South);
        Assert.IsFalse(g.FitsAt(P(10.25, 5.5)), "the walls leave room, the mark does not");
        Assert.AreEqual(2.0 + Math.Sqrt(9.0 + 64.0) + 2.0, g.Distance(map.Find("kitchen"), map.Find("garage")), 0.01, "centre to centre through the passages");
        Refuses(() => g.Distance(map.Find("kitchen"), map.Find("kitchen")), "the same area");
        Assert.AreEqual(4.6, g.Aside(At(4.6, 9.5, East)).X, 1e-9, "the courtesy step: two radii to the right of where it faces…");
        Assert.AreEqual(9.0, g.Aside(At(4.6, 9.5, East)).Y, 1e-9, "…which facing east is south");
    }

    // ---- the guards ----

    [TestMethod]
    public void AGolem_ReceivesItsModules_AndRefusesNothingGiven()
    {
        Refuses(() => new Golem(null, map, collisions), "a golem needs a body to drive");
        Refuses(() => new Golem(Body(), null, collisions), "a golem needs its map, laid out");
        Refuses(() => new Golem(Body(), map, null), "even empty");
        Refuses(() => new Golem(Body(), Catalog.RingCorridor(), collisions), "over the golem's own layout");
        Refuses(() => g.Visit(null, P(1, 1)), "'from' was not given");
        var same = P(2.0, 9.5);
        Refuses(() => g.Visit(same, same), "the same position");
        Refuses(() => g.Wake(null), "'me' was not given");
    }
}
