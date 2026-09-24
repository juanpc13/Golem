using GolemDomain;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Robots;
using GolemDomain.Routes;
using GolemDomain.Touches;
using GolemDomain.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// THE GOLEM (GolemDomain.Golem): the mind that RECEIVES its body, its map and its collisions module, hands out routes (Visit,
// Cover, Follow), keeps where its body stands (Standing, Wake), takes what the body reports (Bump) and what the peers tell
// (HearBump, LearnForget), holds and releases itself (Pause, Resume), and answers over the fleet of its routes.
[TestClass]
public class GolemTests
{
    // ---- entrusting: Visit, Cover, Follow ----

    [TestMethod]
    public void Visit_APoint_HandsOutARouteThatIsPending_AndIsTheOperators()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var route = g.Visit(new Position(2.0, 9.5), new Position(2.0, 1.5));
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
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var route = g.Visit(new Position(2.0, 9.5), map.Find("storage"));
        StringAssert.EndsWith(route.AsPlan(), "storage@9,9.5", "the way ends at the area's centre");
        Assert.AreEqual(1, route.StopsLeft, "the place is the route's one stop");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => map.Find("attic")).Message, "attic");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Visit(new Position(2.0, 9.5), new Position(3.0, 5.0))).Message, "nowhere on the map");
        Assert.IsTrue(map.Knows("kitchen"));
        Assert.IsFalse(map.Knows("attic"));
        Assert.AreEqual(1, g.Routes().Count, "a refused errand mints nothing");
    }

    [TestMethod]
    public void Visit_WhenNoWayFits_IsRefusedBeforeAnythingIsMinted()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        collisions.Mark(new Pose(4.0, 9.2, 0.0));        // marks close the kitchen/north doorway…
        collisions.Mark(new Pose(4.0, 9.8, 0.0));
        collisions.Mark(new Pose(0.75, 8.2, -1.5708));      // …and the kitchen/west one
        collisions.Mark(new Pose(0.75, 7.8, -1.5708));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Visit(new Position(2.0, 9.5), new Position(9.0, 9.5))).Message, "no road");
        Assert.AreEqual(0, g.Routes().Count, "nothing minted, no handle spent");
    }

    [TestMethod]
    public void Cover_HandsOutARouteWhoseOrderOfStopsTheGolemChooses()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var route = g.Cover(new Position(2.0, 1.5), map.Find("garage"));
        Assert.IsTrue(route.ChoosesOrder);
        Assert.IsFalse(g.Visit(new Position(2.0, 1.5), map.Find("garage")).ChoosesOrder, "a Visit keeps the operator's order");
    }

    [TestMethod]
    public void Follow_TakesAPointAPeerReached_WithAHandleOfItsOwn_AndItFollows()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        g.Visit(new Position(2.0, 9.5), new Position(2.0, 1.5));
        var followed = g.Follow(new Position(8.5, 2.5));
        Assert.AreEqual(2, g.Routes().Count, "two routes, handles 1 and 2, minted inside");
        Assert.AreEqual(2, followed.Id, "the followed point got handle 2");
        Assert.IsTrue(followed.Following, "a followed route remembers where it came from");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => followed.Then(new Position(9.0, 1.5))).Message, "a told point is one route each");
    }

    // ---- where the golem knows its body stands (18-sep-2026) ----

    [TestMethod]
    public void TheGolem_LearnsWhereItsBodyStands_FromEveryActThatBringsThePose()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        Assert.IsFalse(g.KnowsWhereItStands, "born, it knows nothing of where its body is");
        var route = g.Visit(new Position(2.0, 9.5), new Position(2.0, 1.5));   // the errand opens from the kitchen's centre: where the body stands
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
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        g.Wake(new Pose(2.0, 9.5, 0.0));                           // the body woke in the kitchen, facing east
        var route = g.Follow(new Position(5.5, 9.5));                    // the leader says it reached the north hall
        Assert.IsTrue(route.IsRouted, "planned on arrival: " + route.AsPlan());
        StringAssert.EndsWith(route.AsPlan(), "north@5.5,9.5", "from the kitchen, through its door, to the told point");
        Assert.AreEqual("advance", route.Order, "facing east already, the first order is to the motors");
        Assert.IsTrue(route.Amount > 0.0);
    }

    [TestMethod]
    public void AToldPoint_WhileBusy_IsPlannedFromWhereTheWayUnderwayEnds()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        g.Visit(new Position(2.0, 9.5), new Position(2.0, 1.5));   // to the living room, from the kitchen
        var followed = g.Follow(new Position(9.0, 1.5));                 // the leader is in the garage
        Assert.IsTrue(followed.IsRouted);
        StringAssert.Contains(followed.AsPlan(), "@4,1.5", "from where route 1 ends — the living room — through its door to the south: " + followed.AsPlan());
        StringAssert.EndsWith(followed.AsPlan(), "garage@9,1.5");
    }

    [TestMethod]
    public void AToldPoint_BeforeAnyActBroughtThePose_IsRefused()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Follow(new Position(5.5, 9.5))).Message, "does not know where its body stands");
        Assert.AreEqual(0, g.Routes().Count, "nothing minted");
    }

    [TestMethod]
    public void Awake_TheGolemKeepsWhereItsBodyStands_AndAPlanUnderway_IsDecidedAgainFromThere()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var route = g.Visit(new Position(2.0, 9.5), new Position(9.0, 9.5));   // to the storage, from the kitchen: east through the north hall
        StringAssert.StartsWith(route.AsPlan(), "kitchen/");
        Assert.IsTrue(g.Wake(new Pose(2.0, 1.5, 1.5708)), "reborn: the body was carried to the living room meanwhile — the plan was decided again");
        Assert.AreEqual(2.0, g.Standing.X, 1e-9);
        Assert.AreEqual(1.5, g.Standing.Y, 1e-9);
        StringAssert.StartsWith(route.AsPlan(), "living/", "the way underway was decided again from the living room, inside: " + route.AsPlan());
        StringAssert.StartsWith(route.Order, "turn", "facing north, the way east asks a turn first");
    }

    [TestMethod]
    public void Awake_WithNothingUnderway_OnlyKeepsWhereItStands()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        Assert.IsFalse(g.Wake(new Pose(5.5, 5.5, 0.0)), "nothing to decide again");
        Assert.IsTrue(g.KnowsWhereItStands);
        Assert.AreEqual(0.0, g.Standing.Heading, 1e-9);
        Assert.IsFalse(g.HasPendingMission());
        Assert.AreEqual(0, g.Routes().Count);
    }

    [TestMethod]
    public void Awake_WhileHeld_TheWayUnderwayStaysAsItWas()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var route = g.Visit(new Position(2.0, 9.5), new Position(9.0, 9.5));
        string plan = route.AsPlan();
        g.Pause(new Pose(2.0, 9.5, 0.0));
        Assert.IsFalse(g.Wake(new Pose(2.0, 1.5, 1.5708)), "held: nothing is decided again");
        Assert.AreEqual(plan, route.AsPlan(), "the body stands where it is, the way keeps");
        Assert.AreEqual("stop", route.Order);
        Assert.AreEqual(1.5, g.Standing.Y, 1e-9, "but the golem knows where its body stands now");
    }

    // ---- the touches: the bump is the golem's, the peers' words are heard ----

    [TestMethod]
    public void TheBump_IsTheGolems_ItReckonsTheTouchFromTheBody_FindsItsRouteUnderway_AndTheRouteCorrectsInside()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var route = g.Visit(new Pose(9.0, 9.5, -1.5708), new Position(9.0, 1.5));  // down the east corridor
        // the body stood at (10.25, 6.1) facing south and was pressed on the nose: the golem reckons the touch one radius ahead
        Assert.IsTrue(g.Bump(new Pose(10.25, 6.1, -1.5708), 0.0), "the golem handed the touch to its route underway");
        Assert.AreEqual(1, route.Bumps);
        Assert.AreEqual(1, collisions.MarkCount);
        Assert.IsTrue(collisions.KnowsAt(new Position(10.25, 5.85)), "the mark where the touch landed: the body's radius ahead of where it stood");
        Assert.IsFalse(g.FitsAt(new Position(10.25, 5.6)), "the thing reaches beyond the mark, into the corridor");
        Assert.AreEqual("back", route.Order, "and the route corrected its way inside: the retreat first");
        Assert.AreEqual(10.25, g.Standing.X, 1e-9, "the bump brought where the body stood");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Bump(new Pose(1, 1, 0), double.NaN)).Message, "must be an angle");
    }

    [TestMethod]
    public void ATouchOnTheLeftFlank_LandsALeftOfTheBody()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var route = g.Visit(new Pose(5.5, 9.5, -1.5708), new Position(5.5, 1.5));
        g.Bump(new Pose(5.5, 6.0, -1.5708), Math.PI / 2);   // pressed on the left flank while facing south: the thing is to the east
        Assert.IsTrue(collisions.KnowsAt(new Position(5.75, 6.0)), "one radius to the body's left, which facing south is east");
    }

    [TestMethod]
    public void APeersBump_IsHeardAndLearnedAsAMark_UnlessItLiesOnAWallWeBothKnow()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        Assert.AreEqual(1, g.HearBump("blue", new Pose(5.1, 9.5, 3.1416), 0.0), "blue says it bumped in the kitchen's doorway, facing west: its touch reckoned at (4.85, 9.5)");
        Assert.AreEqual(1, collisions.MarkCount, "for now every touch is a thing: learned as a mark");
        Assert.AreEqual("blue", collisions.HeardNear(new Position(4.7, 9.5), 0), "and I know blue was there");
        Assert.AreEqual(2, g.HearBump("red", new Pose(1.22, 2.72, 0.79), 0.0), "red grazed the living room's north wall, as it did live (17-sep)");
        Assert.AreEqual(1, collisions.MarkCount, "heard, but no mark: a wall on the map is nothing learned");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.HearBump("", new Pose(1.0, 1.0, 0.0), 0.0)).Message, "who");
    }

    // ---- the meeting of two bodies, concluded by the peer's word (22-sep-2026, ajuste 46) ----

    [TestMethod]
    public void APeersTouchThatLandedOnMyBody_AnnulsMyLastBump_AndKeepsTheEncounter()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        // blue drives south down the centre hall and bumps red head-on: blue stood at (5.5, 6.0), its touch at (5.5, 5.75)
        var route = g.Visit(new Pose(5.5, 9.5, -1.5708), new Position(5.5, 1.5));
        g.Bump(new Pose(5.5, 6.0, -1.5708), 0.0);
        Assert.AreEqual(1, collisions.MarkCount, "at write time it is a thing: marked, told");
        Assert.AreEqual(1, route.Bumps);
        StringAssert.Contains(route.AsPlan(), "aside@", "and the way steps to the right of the thing: " + route.AsPlan());

        // red's word arrives: it stood at (5.5, 5.5) facing north and was pressed on the nose — its touch landed at (5.5, 5.75), ON blue's body
        g.HearBump("red", new Pose(5.5, 5.5, 1.5708), 0.0);
        Assert.AreEqual(0, collisions.MarkCount, "my mark is taken back and red's touch is not learned: it was red");
        Assert.AreEqual(1, collisions.EncounterCount, "the encounter is kept as history");
        Assert.AreEqual("red", collisions.All().Single().Who);
        Assert.AreEqual(0, route.Bumps, "the route annulled the bump: it counts no more against its patience");
        StringAssert.StartsWith(route.AsPlan(), "back@", "the retreat already inserted is kept…");
        StringAssert.Contains(route.AsPlan(), "> aside@", "…then the step to the body's own right (ajuste 47)…");
        Assert.AreEqual("back", route.Order);
        // and red, standing there, is IN THE WAY for the rest of this route (ajuste 50): a figure the way keeps a body from
        Assert.AreEqual(1, collisions.PeersInTheWay().Count);
        Assert.AreEqual(1, collisions.Figures(body.Radius.InMeters).Count, "no thing, but the peer's berth");
        Assert.IsTrue(collisions.Blocks(new Position(5.5, 5.5), body.Radius.InMeters), "where red stands the body does not fit");
        var red = new Position(5.5, 5.5);
        foreach (var run in route.LegsAhead.Skip(1).Zip(route.LegsAhead, (next, prev) => new Segment(prev.At, next.At)))
            Assert.IsTrue(run.DistanceTo(red) >= 2 * body.Radius.InMeters - 1e-9, "every run of the way keeps a body from red: " + route.AsPlan());
        WalkToTheEnd(route);
        Assert.AreEqual("completed", route.Status);
        Assert.AreEqual(0, collisions.PeersInTheWay().Count, "the route ended: red has moved on, as bodies do");
        Assert.AreEqual(1, collisions.EncounterCount, "but the encounter is history still");
        Assert.AreEqual(0, collisions.Figures(body.Radius.InMeters).Count);
    }

    [TestMethod]
    public void AnIdleGolem_SettingOutAfresh_PlansAroundNobodyItMetWhileStanding()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        g.Wake(new Pose(5.5, 9.5, 0.0));
        g.Bump(new Pose(5.5, 9.5, 0.0), Math.PI / 2);                  // standing, touched on the left flank…
        g.HearBump("blue", new Pose(5.5, 10.0, -1.5708), 0.0);   // …by blue, whose word lands on my body: met, and in my way
        Assert.AreEqual(1, collisions.PeersInTheWay().Count);
        var route = g.Visit(new Pose(5.5, 9.5, 0.0), new Position(9.0, 9.5));    // then the operator sends me east
        Assert.AreEqual(0, collisions.PeersInTheWay().Count, "setting out afresh: blue has moved on");
        Assert.AreEqual("storage@9,9.5", route.AsPlan().Split(" > ").Last());
    }

    [TestMethod]
    public void APeersTouchFarFromMyBody_IsLearnedAsAMark_AndMyBumpStands()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var route = g.Visit(new Pose(5.5, 9.5, -1.5708), new Position(5.5, 1.5));
        g.Bump(new Pose(5.5, 6.0, -1.5708), 0.0);
        g.HearBump("red", new Pose(10.25, 6.1, -1.5708), 0.0);   // red bumped the crate in the east corridor, a room away
        Assert.AreEqual(2, collisions.MarkCount, "two things: mine and red's");
        Assert.AreEqual(0, collisions.EncounterCount);
        Assert.AreEqual(1, route.Bumps, "my bump stands");
    }

    [TestMethod]
    public void OnlyTheLastBump_CanBeAnnulled_ByAWordThatLandsOnIt()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var route = g.Visit(new Pose(9.0, 9.5, -1.5708), new Position(9.0, 1.5));  // down the east corridor
        g.Bump(new Pose(10.25, 7.0, -1.5708), 0.0);                       // a first thing, high in the corridor
        Reach(route, route.NextLeg.At.X, route.NextLeg.At.Y);     // backed off
        g.Bump(new Pose(10.25, 6.1, -1.5708), 0.0);                       // and a second, lower down
        Assert.AreEqual(2, collisions.MarkCount);
        g.HearBump("red", new Pose(10.25, 6.5, 1.5708), 0.0);   // red's touch at (10.25, 6.75): on my body of the FIRST bump, not the last
        Assert.AreEqual(0, collisions.EncounterCount, "only the last bump can be annulled: red's word is learned as a mark like any other…");
        Assert.AreEqual(2, collisions.MarkCount, "…and that mark is the very one my first bump left, so nothing new");
        g.HearBump("red", new Pose(10.25, 5.6, 1.5708), 0.0);   // red's touch at (10.25, 5.85): on my body of the last bump (10.25, 6.1)
        Assert.AreEqual(1, collisions.MarkCount, "the last bump annulled: my last mark gone, red's word not learned; the first mark stays");
        Assert.AreEqual(1, collisions.EncounterCount);
        g.HearBump("red", new Pose(10.25, 5.6, 1.5708), 0.0);            // the wire says it again
        Assert.AreEqual(1, collisions.MarkCount, "heard once: nothing changes");
        g.HearBump("red", new Pose(10.25, 5.7, 1.5708), 0.0);   // and a word that would land on the annulled body again
        Assert.AreEqual(2, collisions.MarkCount, "annulled once: the next word is about something else, learned as a mark");
    }

    [TestMethod]
    public void TheSameWordHeardTwice_IsHeardOnce()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        g.HearBump("blue", new Pose(5.1, 9.5, 3.1416), 0.0);
        Assert.AreEqual(1, collisions.MarkCount);
        g.HearBump("blue", new Pose(5.1, 9.5, 3.1416), 0.0);   // the wire says it again (17-sep live: both journals heard the doorway bump twice)
        Assert.AreEqual(1, collisions.HeardCount, "heard once");
        Assert.AreEqual(1, collisions.MarkCount, "and nothing more is learned from the repeat");
        g.HearBump("blue", new Pose(5.11, 9.5, 3.1416), 0.0);   // blue bumped there AGAIN, a centimetre off: a new word (22-sep live, ajuste 50)
        Assert.AreEqual(2, collisions.HeardCount, "the wire's repeat is the same word to the last digit; a near one is a new one");
        Assert.AreEqual(1, collisions.MarkCount, "learned as the mark already standing there");
    }

    [TestMethod]
    public void AStandingBody_Touched_WritesItsBump_MarksNothing_AndTheMoversWordIsNoThingEither()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        // 22-sep-2026 live: blue drove into red standing in the north hall; red's touch was refused, so blue kept its mark
        g.Wake(new Pose(5.5, 9.5, 0.0));                               // red stands in the north hall, facing east
        Assert.IsFalse(g.Bump(new Pose(5.5, 9.5, 0.0), Math.PI / 2), "no route took it: the body stood — pressed on its left flank");
        Assert.AreEqual(1, g.IdleBumps);
        Assert.AreEqual(0, collisions.MarkCount, "no mark: things do not move, so it was a body");
        Assert.AreEqual(0, g.Routes().Count, "and no route was minted");
        // blue's word: it stood at (5.5, 10.0) facing south, pressed on the nose — its touch at (5.5, 9.75), on red's shell
        g.HearBump("blue", new Pose(5.5, 10.0, -1.5708), 0.0);
        Assert.AreEqual(0, collisions.MarkCount, "red learns no thing where blue touched it: that was red");
        Assert.AreEqual(1, collisions.EncounterCount, "the encounter is history");
        Assert.AreEqual("blue", collisions.All().Single().Who);
    }

    [TestMethod]
    public void TheMover_HearingTheStandingBodysWord_AnnulsItsMark()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var route = g.Visit(new Pose(6.3, 10.4, -1.5708), new Position(4.6, 9.2));  // blue drives across the north hall…
        g.Bump(new Pose(5.5, 10.0, -1.5708), 0.0);   // …and bumps into something at (5.5, 9.75): a thing, marked
        Assert.AreEqual(1, collisions.MarkCount);
        g.HearBump("red", new Pose(5.5, 9.5, 0.0), Math.PI / 2);   // red, standing at (5.5, 9.5) facing east, says it was pressed on its left flank: its touch (5.5, 9.75) landed on blue's body
        Assert.AreEqual(0, collisions.MarkCount, "blue takes its mark back: it bumped into red");
        Assert.AreEqual(1, collisions.EncounterCount);
        Assert.AreEqual(0, route.Bumps, "and the route annulled the bump");
    }

    [TestMethod]
    public void AThirdParty_HearingTwoWordsThatLandOnEachOther_LearnsNoThing()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        // green hears blue's bump, then red's: blue's touch landed on red's body and red's on blue's — they met each other
        g.HearBump("blue", new Pose(5.5, 10.0, -1.5708), 0.0);            // blue's touch at (5.5, 9.75)
        Assert.AreEqual(1, collisions.MarkCount, "on the first word alone, a thing is learned");
        g.HearBump("red", new Pose(5.5, 9.5, 0.0), Math.PI / 2);   // red's body at (5.5, 9.5): blue's touch landed on it
        Assert.AreEqual(0, collisions.MarkCount, "the second word says whose body it was: the mark learned from the first goes, and none is learned from the second");
        Assert.AreEqual(2, collisions.HeardCount, "both words are kept: I know where blue and red were");
        g.HearBump("blue", new Pose(10.25, 6.1, -1.5708), 0.0);   // and a bump of blue's far from anybody stays a thing
        Assert.AreEqual(1, collisions.MarkCount);
    }

    [TestMethod]
    public void MeetingAPeer_TakesTheMarksBack_AndForgettingDropsWhatStoodThere()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var route = g.Visit(new Pose(2.0, 9.5, 0.0), map.Find("north"));
        g.HearBump("blue", new Pose(5.1, 9.5, 3.1416), 0.0);
        route.Bump(new Pose(4.7, 9.5, 0.0), new Pose(4.45, 9.5, 0.0));   // my own touch, right there
        Assert.AreEqual(2, collisions.MarkCount, "two marks presumed: blue's bump, heard, and my own");
        Assert.AreEqual(1, g.Met("blue", new Position(4.7, 9.5)));
        Assert.AreEqual(0, collisions.MarkCount, "meeting a body takes both marks back: mine, and the one I learned from blue's bump there");
        Assert.AreEqual(0, g.LearnMet(new Position(4.6, 9.5)), "when blue tells it met a body too, nothing is left to take back");

        collisions.Mark(new Pose(10.25, 5.85, -1.5708));
        collisions.Mark(new Pose(10.25, 5.15, 1.5708));
        Assert.AreEqual(2, g.Forget(new Position(10.2, 5.5)), "the operator says the thing is gone: every mark that outlined it goes");
        Assert.AreEqual(0, g.LearnForget(new Position(10.2, 5.5)), "a peer says so too: nothing left");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Forget(null)).Message, "needs where");
    }

    // ---- the hold is the golem's ----

    [TestMethod]
    public void TheOperatorHoldsTheGolem_WhereItsBodyStands_AndLetsItGoOn()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var route = g.Visit(new Position(2.0, 9.5), new Position(2.0, 1.5));
        Assert.IsFalse(g.Held);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Resume(new Pose(2.0, 9.5, 0.0))).Message, "is not paused");
        Assert.AreSame(route, g.Pause(new Pose(2.6, 9.5, 1.5708)), "the route underway is held with the golem and handed back");
        Assert.IsTrue(g.Held, "the GOLEM is held, not just an errand");
        Assert.AreEqual(2.6, g.HeldAt.X, 1e-9, "where it was held is kept, by the golem…");
        Assert.AreEqual(2.6, route.HeldAt.X, 1e-9, "…and by the route it interrupted");
        Assert.AreEqual("stop", route.Order);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Pause(new Pose(2.6, 9.5, 0.0))).Message, "already paused");
        Assert.AreSame(route, g.Resume(new Pose(2.6, 9.5, 1.5708)));
        Assert.IsFalse(g.Held, "let go on");
        Assert.IsFalse(route.Paused);
        Reach(route, 2.0, 1.5);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Pause(new Pose(2.0, 1.5, 0.0))).Message, "no pending mission");
    }

    // ---- the fleet of routes ----

    [TestMethod]
    public void AStaleFollowedPoint_IsOvertakenByANewerOne_AndTheOperatorsPointNeverIs()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var operators = g.Visit(new Position(9.0, 9.5), new Position(9.0, 1.5));   // the operator: the garage, from the storage
        var older = g.Follow(new Position(2.0, 9.5));                      // the leader was in the kitchen…
        Assert.IsFalse(g.HasNewerFollowing(older));
        var newer = g.Follow(new Position(9.0, 9.5));                      // …and now says it is in the storage
        Assert.IsTrue(g.HasNewerFollowing(older));
        Assert.AreEqual(newer.Id, g.NewestFollowingId());
        Assert.IsFalse(g.HasNewerFollowing(operators), "the operator's route is never superseded by the followers' rule");
        older.Abandon("superseded by route 3");
        Assert.AreEqual(2, g.PendingRoutes().Count, "the operator's garage and the newest followed point remain");
    }

    [TestMethod]
    public void LettingGo_AbandonsEveryPendingRoute_AndAHandleIsNeverMintedAgain()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        g.Visit(new Position(2.0, 9.5), new Position(2.0, 1.5));
        g.Visit(new Position(2.0, 9.5), new Position(9.0, 1.5));
        foreach (var route in g.PendingRoutes()) route.Abandon("the operator let go of everything");
        Assert.AreEqual(0, g.PendingRoutes().Count);
        Assert.AreEqual(2, g.Routes().Count, "nothing is erased: the routes stay, abandoned");
        Assert.AreEqual("abandoned", g.Find(1).Status);
        Assert.AreEqual(3, g.Visit(new Position(2.0, 9.5), new Position(2.0, 1.5)).Id, "a spent handle is never minted again: the idempotency keys hang on it");
        Assert.AreEqual(3, g.Newest().Id, "the route handed out last");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Find(9)).Message, "unknown route 9");
    }

    [TestMethod]
    public void AQueuedRoute_MeasuresItsFirstOrder_FromWhereTheBodyReallyStands_NotFromThePlannedEnd()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        // 22-sep-2026 live: blue's second errand was planned from where the first was expected to end; the body ended half a
        // metre away facing elsewhere, and the first turn — measured from the planned pose — sent it the wrong way
        var first = g.Visit(new Pose(5.5, 9.5, -1.5708), new Position(5.5, 1.5));   // down through the centre to the south hall
        var queued = g.Visit(g.PlannedEnd(), new Position(2.0, 1.5));   // then to the living room, planned from (5.5, 1.5) facing south
        Assert.AreEqual(5.5, queued.Standing.X, 1e-9, "planned from the expected end");
        Assert.AreEqual("advance", first.Order, "facing south already: the first errand is one advance");
        first.Reach(new Pose(6.0, 2.5, 0.0));   // reported where the body REALLY ended: off the stop, facing east
        Assert.AreEqual("completed", first.Status);
        Assert.AreSame(queued, g.Underway());
        Assert.AreEqual(6.0, queued.Standing.X, 1e-9, "the queued route measures from the real pose");
        Assert.AreEqual(0.0, queued.Standing.Heading, 1e-9);
        Assert.AreEqual("turnRight", queued.Order, "facing east, the door to the living room lies west-south-west: turn right, the shorter way");
        Assert.AreEqual(new Pose(6.0, 2.5, 0.0).DistanceTo(queued.Target), new Position(6.0, 2.5).DistanceTo(queued.Target), 1e-9);
        Step(queued);
        Assert.AreEqual("advance", queued.Order);
        Assert.AreEqual(new Position(6.0, 2.5).DistanceTo(queued.Target), queued.Amount, 1e-9, "and the advance is measured from there too");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => queued.StandAt(new Pose(1, 1, 0))).Message, "already underway");
    }

    [TestMethod]
    public void ThePlannedEnd_IsWhereTheNextErrandStarts_WhileTheGolemIsBusy()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        g.Visit(new Pose(2.0, 9.5, 0.0), new Position(2.0, 1.5));
        Assert.AreEqual(2.0, g.PlannedEnd().X, 1e-9);
        Assert.AreEqual(1.5, g.PlannedEnd().Y, 1e-9);
        var next = g.Visit(g.PlannedEnd(), new Position(9.0, 1.5));       // the host opens the next errand from there
        StringAssert.StartsWith(next.AsPlan(), "living/south@4,1.5", "planned from the living room, where the first errand ends");
    }

    // ---- the reads where the body enters the answer ----

    [TestMethod]
    public void TheRoadLeft_RunsThroughEveryStopAhead_AlongThePassages_AndTheTimeCountsTheLingers()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        g.Visit(new Position(2.0, 9.5), new Position(5.5, 9.5));                     // north
        g.Follow(new Position(5.5, 5.5));                                  // centre, a followed point: one linger
        g.Visit(g.PlannedEnd(), new Position(5.5, 1.5));                   // south
        double road = 4.0 + 4.0;                                // north -> centre -> south, straight across the open boundaries
        double firstLeg = 2.0 + 1.5;                            // from the kitchen centre: door (4,9.5), then the north centre
        Assert.AreEqual(road, g.RouteLength(), 0.01, "stop to stop through the passages");
        Assert.AreEqual(firstLeg + road, g.DistanceLeft(new Position(2.0, 9.5)), 0.01, "plus the way from where the body stands");
        Assert.AreEqual((firstLeg + road) / 2.0 + 6.0, g.SecondsLeft(new Position(2.0, 9.5)), 0.01, "at the body's 2 m/s, plus its 6 s linger at the followed stop");
        Assert.AreEqual(road / 2.0 + 6.0, g.RouteSeconds(), 0.01, "answered with no parameters");
    }

    [TestMethod]
    public void WithNothingPending_TheRoadLeftIsZero_AndWithNoRoadItIsAsTheCrowFlies()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var route = g.Visit(new Position(9.0, 9.5), new Position(9.0, 1.5));
        route.Abandon("the test is over");
        Assert.AreEqual(0.0, g.DistanceLeft(new Position(2.0, 9.5)), 0.001);

        g.Visit(new Position(9.0, 9.5), new Position(2.0, 9.5));                     // the kitchen, from the storage
        collisions.Mark(new Pose(4.0, 9.2, 0.0));                  // marks close the kitchen/north doorway…
        collisions.Mark(new Pose(4.0, 9.8, 0.0));
        collisions.Mark(new Pose(0.75, 8.2, -1.5708));                // …and the kitchen/west one
        collisions.Mark(new Pose(0.75, 7.8, -1.5708));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Road(g.Underway(), new Position(9.0, 9.5))).Message, "no road");
        Assert.AreEqual(7.0, g.DistanceLeft(new Position(9.0, 9.5)), 0.001, "straight from (9, 9.5) to (2, 9.5): a read never refuses");
        Assert.IsTrue(g.SecondsLeft(new Position(9.0, 9.5)) > 0);
    }

    [TestMethod]
    public void TheGolem_AnswersForItsBody_WhereItFits_AndHowFarAreasLie()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        Assert.IsTrue(g.FitsAt(new Position(5.5, 5.5)), "the middle of the centre hall");
        Assert.IsFalse(g.HasRoomAt(new Position(9.7, 5.5)), "too close to the corridor's wall for the body");
        Assert.IsTrue(g.HasRoomAt(new Position(10.25, 5.5)));
        collisions.Mark(new Pose(10.25, 5.85, -1.5708));
        Assert.IsFalse(g.FitsAt(new Position(10.25, 5.5)), "the walls leave room, the mark does not");
        Assert.AreEqual(2.0 + Math.Sqrt(9.0 + 64.0) + 2.0, g.Distance(map.Find("kitchen"), map.Find("garage")), 0.01, "centre to centre through the passages");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Distance(map.Find("kitchen"), map.Find("kitchen"))).Message, "the same area");
        Assert.AreEqual(4.6, g.Aside(new Pose(4.6, 9.5, 0.0)).X, 1e-9, "the courtesy step: two radii to the right of where it faces…");
        Assert.AreEqual(9.0, g.Aside(new Pose(4.6, 9.5, 0.0)).Y, 1e-9, "…which facing east is south");
    }

    // ---- the guards ----

    [TestMethod]
    public void AGolem_ReceivesItsModules_AndRefusesNothingGiven()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Golem(null, map, collisions)).Message, "a golem needs a body to drive");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Golem(body, null, collisions)).Message, "a golem needs its map, laid out");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Golem(body, map, null)).Message, "even empty");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Visit(null, new Position(1, 1))).Message, "'from' was not given");
        var same = new Position(2.0, 9.5);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Visit(same, same)).Message, "the same position");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Wake(null)).Message, "'me' was not given");
    }

    // The body the test drives: ONE thing the route asks, done — a turn made (the body stands where it stood, facing the target's
    // heading) or a move made (the body stands at the target, facing that heading) — the act the robot's report writes.
    private static void Step(Route route)
    {
        var target = route.Target;
        string order = route.Order;
        if (order == "turnLeft" || order == "turnRight") route.Turn(new Pose(route.Standing.X, route.Standing.Y, target.Heading));
        else if (order == "advance" || order == "back") route.Reach(new Pose(target.X, target.Y, target.Heading));
        else Assert.Fail($"route {route.Id} asks '{order}': nothing for the body to do");
    }

    // The body walks the way until the leg at (x, y) — a stop, a door, a point — is behind it, one act per thing the route asks.
    private static void Reach(Route route, double x, double y)
    {
        Assert.IsTrue(route.IsRouted, $"route {route.Id} walks only once its way is decided");
        for (int step = 0; step < 80; step++)
        {
            if (!route.IsPending()) Assert.Fail($"route {route.Id} ended before reaching ({x}, {y})");
            bool atIt = Math.Abs(route.NextLeg.At.X - x) < 1e-6 && Math.Abs(route.NextLeg.At.Y - y) < 1e-6;
            Step(route);
            if (!atIt) continue;
            if (!route.IsPending()) return;
            if (Math.Abs(route.NextLeg.At.X - x) > 1e-6 || Math.Abs(route.NextLeg.At.Y - y) > 1e-6) return;
        }
        Assert.Fail($"route {route.Id}: too many steps without reaching ({x}, {y})");
    }

    // The body walks the whole way to the end.
    private static void WalkToTheEnd(Route route)
    {
        for (int step = 0; step < 200 && route.IsPending(); step++) Step(route);
        Assert.IsFalse(route.IsPending(), $"route {route.Id}: too many steps without ending — {route.AsPlan()}");
    }
}
