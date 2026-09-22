using GolemDomain;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Routes;
using GolemDomain.Touches;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static GolemTest.DomainFixture;

namespace GolemTest;

// THE ROUTE (Routes.Route): the errand and its way, one object, decided inside — the stops, the way planned from where the
// body stands, the cursor that hands out ONE thing at a time in the robot's words (an action and an amount measured from where
// the body last stood), the touches that correct the way inside, the hold, the ending. Handed out by the golem; here it is
// asked directly.
[TestClass]
public class RouteTests
{
    private Golem g;
    private MapLayout map;
    private Collisions collisions;

    [TestInitialize]
    public void AGolemIsBorn() => (g, map, collisions) = Born();

    // ---- the way, decided inside ----

    [TestMethod]
    public void TheErrand_DecidesItsWholeWayInside_AndAsksOnlyTheNextThing()
    {
        var route = g.Visit(At(2.0, 1.5, East), map.Find("kitchen"));   // from the living room, facing east
        Assert.IsTrue(route.IsRouted, "the errand decided the way");
        Assert.AreEqual("west/living@0.75,3 > kitchen/west@0.75,8 > kitchen@2,9.5", route.AsPlan(), "the whole way, held by the route");
        Assert.AreEqual("west/living", route.NextLeg.Name, "the first thing is the door out of the living room");
        Assert.AreEqual("door", route.NextLeg.Kind);
        Assert.IsTrue(route.NextLeg.HasHeading, "the first leg has its heading too: the start is known");
        // the robot's words (17-sep-2026): an action and an amount, measured from where the body stands and faces
        StringAssert.StartsWith(route.Order, "turn", "one thing at a time: turn first, to face the door's approach");
        Assert.IsTrue(route.Amount > 0.1, "how much to turn, in radians: " + route.Amount);
        double toApproach = P(2.0, 1.5).DistanceTo(route.Target);
        Step(route);
        Assert.AreEqual("advance", route.Order, "turned: now advance to line up in front of the door");
        Assert.AreEqual(toApproach, route.Amount, 0.001, "how far, in metres, from where the body stands");
        Refuses(() => route.Turn(At(2.0, 1.5, 0.0)), "asked no turn now");
        Step(route);
        Assert.AreEqual("west/living", route.NextLeg.Name, "lined up in front of the door: the door is still the leg");
        if (route.Order.StartsWith("turn")) Step(route);
        Assert.AreEqual("advance", route.Order, "straight through the door to its exit");
        Step(route);
        Assert.AreEqual("kitchen/west", route.NextLeg.Name, "the door is behind: the next leg");
        Assert.AreEqual("advance", route.Order, "out of the door the body already faces north up the corridor: no turn, an advance");
        Assert.IsTrue(route.Amount > 3.0, "the corridor's length to the next door's approach: " + route.Amount);
        Assert.AreEqual(1, route.StopsLeft);
    }

    [TestMethod]
    public void TheWay_ReadsAsOneLine_NamingEveryDoorAndStop()
    {
        Assert.AreEqual("kitchen/west@0.75,8 > west/living@0.75,3 > living@2,1.5", g.Visit(P(2.0, 9.5), P(2.0, 1.5)).AsPlan());
        Assert.AreEqual("kitchen/north@4,9.5 > south/garage@7,1.5 > garage@9,1.5", g.Visit(P(2.0, 9.5), P(9.0, 1.5)).AsPlan(),
            "door to door in one straight run through the centre: the openings are crossed, not bent on");
    }

    [TestMethod]
    public void OneMoreStop_IsToldToTheRoute_AndTheWayIsDecidedAgainThroughThemAll()
    {
        var route = g.Visit(P(2.0, 1.5), map.Find("garage"));
        route.Then(map.Find("kitchen")).Then(map.Find("storage"));
        Assert.AreEqual(3, route.StopsLeft);
        string plan = route.AsPlan();
        StringAssert.StartsWith(plan, "living/south@4,1.5 > south/garage@7,1.5 > garage@9,1.5 > south/garage@7,1.5 > ");
        StringAssert.EndsWith(plan, "> kitchen@2,9.5 > kitchen/north@4,9.5 > north/storage@7,9.5 > storage@9,9.5");
        Refuses(() => route.Then(P(3.0, 5.0)), "nowhere on the map");
        Step(route);
        Refuses(() => route.Then(map.Find("north")), "already underway: no stop can be added");
    }

    [TestMethod]
    public void ACoverRoute_LetsTheGolemChooseTheOrderOfItsStops_AndKeepsIt()
    {
        var route = g.Cover(P(2.0, 1.5), map.Find("garage"));
        route.Then(map.Find("kitchen")).Then(map.Find("storage"));
        Assert.IsTrue(route.ChoosesOrder);
        CollectionAssert.AreEqual(new[] { "garage", "storage", "kitchen" }, route.StopsAhead.Select(s => map.ZoneAt(s).Name).ToList(),
            "garage, storage, kitchen — not the order given: the shortest round from the living room");
        StringAssert.EndsWith(route.AsPlan(), "> kitchen@2,9.5");
    }

    // ---- the walk: one leg at a time, the pose reported ----

    [TestMethod]
    public void TheWay_IsWalkedOneLegAtATime_ATurnBeforeAMove_AndTheLastStopCompletes()
    {
        var route = g.Visit(At(2.0, 9.5, East), P(2.0, 1.5));
        Assert.AreEqual(3, route.LegsLeft, "two doors and the stop: the whole way");
        Assert.AreEqual("kitchen/west", route.NextLeg.Name);
        Assert.IsFalse(route.NextLeg.IsStop);
        Assert.AreEqual(0.75, route.Target.X, 0.001, "the body heads to the first door's approach, not to the stop");
        Assert.IsTrue(route.IsStopAhead(P(2.0, 1.5)));
        Assert.IsFalse(route.IsStopAhead(P(0.75, 3.0)), "a door is not a stop");

        Refuses(() => route.Reach(At(2.0, 2.0, 0.0)), "asked no move now");   // the route asks a turn first
        Reach(route, 0.75, 8.0);                                                 // the first door: lined up, crossed, reported
        Assert.AreEqual(2, route.LegsLeft);
        Assert.AreEqual("west/living", route.NextLeg.Name, "the route hands out the next leg");
        Assert.IsTrue(route.NextLeg.HasHeading, "with the heading to walk it, from the previous point");
        Assert.AreEqual(South, route.NextLeg.Target.Heading, 0.001, "straight south down the west corridor");

        Reach(route, 2.0, 1.5);                                                  // the stop: the second door walked on the way
        Assert.AreEqual(0, route.LegsLeft, "every leg walked: reaching the stop was the last");
        Assert.AreEqual("completed", route.Status, "reaching the last stop completes the route");
        Assert.IsFalse(route.IsPending());
        Refuses(() => route.Reach(At(2.0, 1.5, 0.0)), "already completed");
    }

    [TestMethod]
    public void TheArrival_IsOneAct_AndTheRouteKnowsWhetherItAskedATurnOrAMove()
    {
        var route = g.Visit(At(2.0, 9.5, East), P(2.0, 1.5));
        StringAssert.StartsWith(route.Order, "turn");
        double heading = route.Target.Heading;
        route.Arrive(At(2.0, 9.5, heading));
        Assert.AreEqual("advance", route.Order, "the arrival was the turn: now the move");
        var target = route.Target;
        route.Arrive(At(target.X, target.Y, heading));
        Assert.AreEqual(target.X, route.Standing.X, 1e-9, "the arrival was the move: the body stands where it went");
        Assert.AreEqual(target.Y, route.Standing.Y, 1e-9);
    }

    [TestMethod]
    public void ARouteWithSeveralStops_ReachesEachInTurn_AndCompletesOnTheLast()
    {
        var route = g.Visit(At(5.5, 5.5, North), map.Find("north")).Then(map.Find("south"));   // from the centre
        Assert.AreEqual(2, route.StopsLeft);
        Reach(route, 5.5, 9.5);
        Assert.AreEqual("pending", route.Status, "one stop reached, one to go");
        Assert.AreEqual(1, route.StopsLeft);
        Assert.AreEqual("south", route.NextLeg.Name, "the way goes on from the stop: straight down through both openings, no bend");
        Reach(route, 5.5, 1.5);
        Assert.AreEqual("completed", route.Status);
        Assert.AreEqual(0, route.StopsLeft);
    }

    [TestMethod]
    public void SeveralStopsInOneRoom_AreReachedInOrder_TheFirstWithAnAdvanceOfNothing()
    {
        var route = g.Visit(At(2.0, 9.5, East), P(2.0, 9.5)).Then(P(3.0, 10.5));   // two points of the kitchen, from the first of them
        Assert.AreEqual(2, route.StopsLeft);
        Assert.AreEqual("advance", route.Order, "standing on the first stop already: an advance of nothing");
        Assert.AreEqual(0.0, route.Amount, 1e-6);
        Reach(route, 2.0, 9.5);
        Assert.AreEqual("pending", route.Status, "one stop reached, one to go");
        Assert.AreEqual(3.0, route.NextLeg.Target.X, 0.001);
        Reach(route, 3.0, 10.5);
        Assert.AreEqual("completed", route.Status);
    }

    [TestMethod]
    public void ADoor_IsCrossedStraight_LiningUpOffTheWallOnBothSides()
    {
        // kitchen (2,9.5) -> door kitchen/west at (0.75,8) on a horizontal wall -> west corridor -> door (0.75,3) -> living
        var legs = g.Visit(P(2.0, 9.5), P(2.0, 1.5)).LegsAhead;
        Assert.AreEqual(3, legs.Count);
        Assert.AreEqual(0.75, legs[0].At.X, 0.001, "the leg IS the door");
        Assert.AreEqual(8.0, legs[0].At.Y, 0.001);
        Assert.AreEqual(0.75, legs[0].Approach.X, 0.001, "the approach stands right in front of the door");
        Assert.AreEqual(8.6, legs[0].Approach.Y, 0.001, "0.6 into the kitchen, the side the body comes from");
        Assert.AreEqual(7.4, legs[0].Exit.Y, 0.001, "0.6 into the corridor, the side it goes to");
        Assert.AreEqual(3.6, legs[1].Approach.Y, 0.001, "the next door, west/living at y=3, is approached from the corridor");
        Assert.AreEqual(2.4, legs[1].Exit.Y, 0.001);
        Assert.AreEqual(2.0, legs[2].Approach.X, 0.001, "a stop has no wall to clear: approach, exit and point coincide");
        Assert.AreEqual(1.5, legs[2].Exit.Y, 0.001);
    }

    [TestMethod]
    public void ADoor_FollowedByABendOnAnOpening_IsStillCrossedStraight()
    {
        // kitchen (3, 9.5) -> door kitchen/north at (4, 9.5) -> south (5.5, 1.5): the straight run from the door would cross
        // north~center too close to the block's corner, so the way bends on the opening's pivot (4.5, 8) — a leg ON the north
        // hall's boundary. 17-sep-2026 live: the door got no approach and no exit, the body turned in the doorway and grazed the jamb.
        var route = g.Visit(At(3.0, 9.5, East), P(5.5, 1.5));
        Assert.AreEqual("kitchen/north", route.NextLeg.Name);
        StringAssert.StartsWith(route.AsPlan(), "kitchen/north@4,9.5 > north~center@", "the door is followed by the bend: " + route.AsPlan());
        Assert.AreEqual(3.4, route.NextLeg.Approach.X, 0.001, "lined up inside the kitchen, off the wall");
        Assert.AreEqual(4.6, route.NextLeg.Exit.X, 0.001, "out into the north hall, off the wall: the turn toward the opening is made there, not in the doorway");
        Assert.AreEqual(3.4, route.Target.X, 0.001, "the first thing headed to is the approach");
    }

    [TestMethod]
    public void TheNextLegsPose_AndThePlannedEnd_AreReadFromTheRoute()
    {
        var route = g.Visit(At(2.0, 9.5, East), P(2.0, 1.5));
        Assert.AreEqual(0.75, route.NextLeg.Target.X, 1e-9);
        Assert.AreEqual(2.0, route.PlannedEnd.X, 1e-9, "where the way ends…");
        Assert.AreEqual(1.5, route.PlannedEnd.Y, 1e-9);
        Assert.AreEqual(P(0.75, 3.0).HeadingTo(P(2.0, 1.5)), route.PlannedEnd.Heading, 1e-9, "…facing the last leg's heading: from the living room's door to its centre");
    }

    // ---- the touches: the route concludes what it was and corrects its way inside ----

    [TestMethod]
    public void ATouchOnAWallItKnows_IsAGraze_ConcludedInside()
    {
        var route = g.Visit(At(2.0, 9.5, East), P(2.0, 1.5));   // kitchen/west > west/living > living
        Touched(route, 0.05, 8.5, West);                          // the west corridor's outer wall
        Assert.AreEqual(1, route.Grazes, "a wall I know: my own error, a graze");
        Assert.AreEqual(0, route.Bumps);
        Assert.AreEqual(0, collisions.MarkCount, "no mark: the wall was known");
        Assert.AreEqual("back", route.Order, "back off, then the same legs again");
    }

    [TestMethod]
    public void ATouchOnNothingCharted_IsABump_ConcludedInside()
    {
        var route = g.Visit(At(9.0, 9.5, South), P(9.0, 1.5));  // down the east corridor
        Touched(route, 10.25, 5.85, South);
        Assert.AreEqual(1, route.Bumps, "nothing on the map there: a thing");
        Assert.AreEqual(1, collisions.MarkCount, "presumed and marked at once");
        Assert.AreEqual("back", route.Order, "the retreat first, then the road around");
    }

    [TestMethod]
    public void ABump_IsATouchAndAMarkAtOnce_AndTheRouteBacksOffFirst()
    {
        var route = g.Visit(At(9.0, 9.5, South), P(9.0, 1.5));  // the garage, down the east corridor
        Assert.IsFalse(collisions.Blocks(P(10.25, 5.5), Radius), "the corridor is clear as far as the map knows");
        Bump(route, 10.25, 5.85, South);                          // the crate's face: presumed a thing, in one act
        Assert.AreEqual(1, collisions.MarkCount, "a bump is a touch AND a mark, with the heading of the touch as its normal");
        Assert.AreEqual(1, route.Bumps);
        Assert.AreEqual("back", route.Order, "the route corrected its way inside: back off first");
        StringAssert.StartsWith(route.AsPlan(), "back@", "the retreat is the first correction: " + route.AsPlan());
        Assert.IsTrue(collisions.Blocks(P(10.25, 5.5), Radius), "the body no longer fits where the mark reaches");
        Assert.IsTrue(map.HasRoom(P(10.25, 5.5), Radius), "the walls alone still leave room there: marks are what the body feels around");
        Assert.IsFalse(map.HasRoom(P(9.7, 5.5), Radius), "too close to the corridor's wall for the body");
    }

    [TestMethod]
    public void AfterABump_TheRetreatLeavesRoomToTurn_AndTheRoadGoesAroundIfTheBodyFits()
    {
        // north → south through the centre hall; the body meets the crate's north face head-on, halfway down
        var route = g.Visit(At(5.5, 9.5, South), P(5.5, 1.5));
        Bump(route, 5.5, 5.85, South);
        Assert.AreEqual("back", route.Order, "the first correction: back off");
        double backY = route.NextLeg.Target.Y;
        Assert.IsTrue(backY >= 5.85 + Radius + Retreat - 1e-6, "at least the body's retreat behind where it stood: " + backY);
        Assert.IsTrue(g.FitsAt(P(5.5, backY)), "and standing clear there");
        string plan = route.AsPlan();
        StringAssert.StartsWith(plan, "back@", plan);
        StringAssert.Contains(plan, "around@", "the body fits past the crate in the 3 m hall: it goes around, not another way — " + plan);
        StringAssert.EndsWith(plan, "> south@5.5,1.5");
    }

    [TestMethod]
    public void ATouch_CorrectsTheWayInside_AndGrazesSpendThePatienceOnTheLeg_UntilTheRouteFailsItself()
    {
        var route = g.Visit(At(2.0, 9.5, East), P(2.0, 1.5));
        Assert.IsFalse(route.BumpedSinceRoute);
        Bump(route, 2.0, 10.8, North);                            // something by the kitchen's north wall
        Assert.AreEqual("back", route.Order, "the touch corrected the way: back off first, then the road around");
        Assert.IsFalse(route.BumpedSinceRoute, "nothing left to decide: the route decided inside");
        Assert.AreEqual("pending", route.Status, "and the errand goes on");
        Assert.AreEqual(1, route.StopsLeft);

        Assert.IsTrue(route.MayRetryLeg);
        Graze(route, 0.05, 8.5);
        Graze(route, 0.05, 8.4);
        Assert.AreEqual(2, route.Grazes);
        Assert.IsTrue(route.MayRetryLeg, "two grazes: still patient");
        Graze(route, 0.05, 8.6);
        Assert.IsFalse(route.MayRetryLeg, "three grazes on one leg: patience spent");
        Assert.AreEqual("failed", route.Status, "the route gave itself up: the third graze ended it inside, no host decides that");
        Assert.AreEqual(1, collisions.MarkCount, "the bump's mark stays; a graze leaves none: the wall was known");
        Assert.AreEqual(3, route.Grazes, "the route remembers every graze");
    }

    [TestMethod]
    public void StrandedAfterARetreat_TheRouteDecidesItsWayAgainFromThere_ByItself()
    {
        // marks close the kitchen's doors except a sliver; a bump right by the kitchen/north door leaves only the retreat, and
        // once the body backed off, the route plans again from the real pose — or fails by itself when no road exists
        var route = g.Visit(At(2.0, 9.5, East), P(9.0, 9.5));    // to the storage through the north hall
        PlantMark(collisions, 0.75, 8.2, South);
        PlantMark(collisions, 0.75, 7.8, South);                   // the west door is closed
        Bump(route, 3.5, 9.5, East);                               // something right in front of the north door
        StringAssert.StartsWith(route.AsPlan(), "back@");
        int legs = route.LegsLeft;
        Reach(route, route.NextLeg.At.X, route.NextLeg.At.Y);      // the retreat reached: the route decided again from there
        Assert.IsTrue(route.IsPending() ? route.LegsLeft >= 1 : route.Status == "failed", "either a way from the retreat, or the route ended itself: " + route.AsPlan());
        Assert.IsTrue(legs >= 1);
    }

    // ---- the hold ----

    [TestMethod]
    public void AHeldRoute_KeepsItsWayAndItsPlace_AndResumesTowardItsNextLegFromWhereTheBodyStands()
    {
        var route = g.Visit(At(2.0, 9.5, East), P(2.0, 9.5));    // already there: the stop alone, west of where it will be held
        Assert.IsFalse(route.Paused);
        route.Pause(At(2.6, 9.5, North));
        Assert.IsTrue(route.Paused, "held");
        Assert.AreEqual("stop", route.Order, "held: the body stops");
        Assert.AreEqual(2.6, route.HeldAt.X, 1e-9, "where it was held is kept");
        Assert.IsTrue(route.IsPending(), "a hold is not an ending");
        Assert.IsTrue(route.IsRouted, "the way keeps");
        Assert.AreEqual(1, route.StopsLeft, "and so do the stops ahead");
        Refuses(() => route.Pause(At(2.6, 9.5, 0.0)), "already paused");

        route.Resume(At(2.6, 9.5, North));
        Assert.IsFalse(route.Paused);
        Assert.AreEqual("turnLeft", route.Order, "from where it was held, facing north, the stop lies due west: a quarter turn to the left");
        Assert.AreEqual(1.5708, route.Amount, 0.001, "how much: a quarter turn, in radians");
        Assert.AreEqual(3.1416, route.NextLeg.Target.Heading, 0.001, "the heading to the stop (2.0, 9.5) from (2.6, 9.5): due west");
        Refuses(() => route.Resume(At(2.6, 9.5, North)), "is not paused");
    }

    // ---- the ending ----

    [TestMethod]
    public void AFailedRoute_IsNoLongerPending_AndCannotFailAgain()
    {
        var route = g.Visit(P(2.0, 9.5), P(2.0, 1.5));
        route.Fail("collided with crate at (10.3, 5.9): nothing on my map there");
        Assert.IsFalse(route.IsPending());
        Assert.AreEqual("failed", route.Status);
        Refuses(() => route.Fail("again"), "already failed");
        Refuses(() => g.Visit(P(2.0, 9.5), P(9.0, 1.5)).Fail(""), "needs a reason");
    }

    [TestMethod]
    public void AnAbandonedRoute_StaysAbandoned_AndAbandoningNeedsAReason()
    {
        var route = g.Visit(P(2.0, 9.5), P(2.0, 1.5));
        Refuses(() => route.Abandon(""), "needs a reason");
        route.Abandon("the operator let go");
        Assert.AreEqual("abandoned", route.Status);
        Assert.IsFalse(route.IsPending());
        Refuses(() => route.Abandon("again"), "already abandoned");
    }

    [TestMethod]
    public void AReachedStop_CanBeAnnounced_AndARouteWithoutOneCannot()
    {
        var reached = g.Visit(At(2.0, 9.5, East), P(2.0, 9.5));
        Reach(reached, 2.0, 9.5);
        var fresh = g.Visit(P(2.0, 9.5), P(9.0, 1.5));
        reached.Announce();
        Assert.IsTrue(reached.Announced);
        Assert.IsFalse(fresh.Announced);
        Refuses(() => fresh.Announce(), "only a reached stop is announced");
    }

    // ---- the follower ----

    [TestMethod]
    public void TheFollowerStandoff_IsTwoBodiesAndAClearance_NotALooseNumber()
    {
        g.Wake(At(4.5, 9.5, East));                                // in the north hall, facing east
        var route = g.Follow(P(6.6, 9.5));                         // the leader's spot, 2.1 m ahead in the same hall
        Assert.AreEqual(2 * Radius + Route.FollowerClearance, route.FollowerStandoff, 1e-9, "two radii of the declared body and the clearance");
        Assert.AreEqual("advance", route.Order);
        Assert.AreEqual(2.1 - 1.0, route.Amount, 1e-6, "the advance to the leader's spot stops a standoff short");
    }

    [TestMethod]
    public void AFollowerOnItsLastStop_PullsOver_BeforeTheRouteCompletes()
    {
        g.Wake(At(5.5, 8.5, East));                                // just south of the north hall's centre, facing east
        var route = g.Follow(P(5.5, 9.5));                         // the leader's spot: planned from there at once
        Reach(route, 5.5, 9.5);                                    // the stop reached…
        Assert.AreEqual("pending", route.Status, "…is not the end for a follower");
        Assert.AreEqual(0, route.StopsLeft);
        Assert.AreEqual("aside", route.NextLeg.Name, "it pulls over first, off the leader's way — a leg of its own: " + route.AsPlan());
        Assert.IsTrue(route.NextLeg.IsCorrection);
        WalkToTheEnd(route);
        Assert.AreEqual("completed", route.Status, "walked aside: done");
    }

    // ---- the repertoire kept for the meeting protocol (taken out of the host on 18-sep-2026) ----

    [TestMethod]
    public void DecidingPastAPeer_StepsOutOfItsWayFirst_AndTheStepIsALegLikeAnyOther()
    {
        // red drives east across the kitchen toward the north hall; blue stands just past the doorway. Knowing where blue is,
        // red's way starts by stepping out of the way — to its own right, facing east, is south — and then the errand goes on
        var route = g.Visit(At(2.0, 9.5, East), map.Find("north"));
        g.HearBump("blue", At(5.1, 9.5, West), 0.0);              // blue's bump, learned as a mark for now…
        Bump(route, 4.7, 9.5, East);                               // …my own touch, right there…
        g.Met("blue", P(4.7, 9.5));                                // …and the golem names blue: both marks go, the way is clear to step aside
        route.DecidePast("blue", At(4.6, 9.5, East));
        string way = route.AsPlan();
        StringAssert.StartsWith(way, "aside@4.6,9", "a body's width to its right, off the line it was on: " + way);
        StringAssert.EndsWith(way, "> north@5.5,9.5", "and then the errand goes on");
        Assert.IsFalse(route.NextLeg.IsStop, "the step is a leg to cross, never a stop");
        Assert.AreEqual("aside", route.NextLeg.Name);
        Assert.AreEqual("pending", route.Status, "stepping aside is not arriving");
        Assert.AreEqual(1, route.StopsLeft);
    }

    [TestMethod]
    public void WithNowhereToStepAside_TheWayIsThePlainOne()
    {
        var route = g.Visit(At(2.0, 9.5, East), map.Find("north"));
        g.HearBump("blue", At(5.1, 9.5, West), 0.0);
        route.DecidePast("blue", At(2.0, 10.9, North));           // hemmed in against the kitchen's north wall
        Assert.IsFalse(route.AsPlan().Contains("aside@"), "no room to be polite: the plain way — " + route.AsPlan());
    }

    // ---- the guards ----

    [TestMethod]
    public void ARoute_RefusesNothingGiven_AndAStopNowhereOnTheMap()
    {
        Refuses(() => new Route(1, null, false, false, map, collisions, Radius, Retreat, _ => { }), "Route.Route: 'stop' was not given");
        Refuses(() => new Route(1, P(2, 9.5), false, false, map, collisions, Radius, Retreat, null), "'stood' was not given");
        Refuses(() => g.Visit(P(2.0, 9.5), P(3.0, 5.0)), "nowhere on the map");
        var route = g.Visit(P(2.0, 9.5), P(2.0, 1.5));
        Refuses(() => route.Arrive(null), "Route.Arrive: 'me' was not given");
        Refuses(() => route.Touched(At(1, 1, 0), null), "Route.Touched: 'me' was not given");
        var same = At(1.0, 1.0, 0.0);
        Refuses(() => route.Touched(same, same), "the same pose");
    }
}
