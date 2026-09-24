using GolemDomain;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Robots;
using GolemDomain.Touches;
using GolemDomain.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// THE COLLISIONS MODULE (Touches.Collisions), FILLED THROUGH THE GOLEM THAT RECEIVES IT (Juan, 24-sep-2026: "que los testcase le
// pasen los módulos que se están testeando al golem"): what the bodies LEARNED by touching enters the way the journal writes it — my
// body's bump (g.Bump: where it stood and where on its shell), a peer's bump heard (g.HearBump), a body met (g.Met), a thing taken away
// (g.Forget) — and what the module holds and INTERPRETS is read as the journal reads it: the marks, the bumps heard, the peers met,
// the obstacles as figures (a thing's marks joined by closeness), who was near, where the body fits (g.FitsAt). Facts are kept;
// hypotheses are derived, never stored. The module holds no map (ajuste 54): the zone an obstacle stands in is the map's answer.
[TestClass]
public class CollisionsTests
{
    [TestMethod]
    public void AMark_IsATouchWithItsNormal_OneRadiusAheadOfTheBody_AndATouchWithinATenthIsTheSameMark()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        g.Visit(new Pose(9.0, 9.5, -1.5708), new Position(9.0, 1.5));   // the garage, down the east corridor
        g.Bump(new Pose(10.25, 6.1, -1.5708), 0.0);                       // standing there facing south, pressed on the nose
        Assert.AreEqual(1, collisions.MarkCount, "the crate's north face, touched heading south: one mark");
        Assert.AreEqual(10.25, collisions.Marks[0].At.X, 1e-6);
        Assert.AreEqual(5.85, collisions.Marks[0].At.Y, 1e-6, "one radius ahead of where the body stood");
        Assert.AreEqual(-1.5708, collisions.Marks[0].Heading, 1e-3, "the mark keeps the heading of its touch as its normal");
        g.Bump(new Pose(10.25, 6.15, -1.5708), 0.0);                      // pressed again, five centimetres farther back
        Assert.AreEqual(1, collisions.MarkCount, "a touch within a tenth of a metre is the same mark");
        g.Bump(new Pose(10.3, 6.85, -1.5708), 0.0);                       // and farther along
        Assert.AreEqual(2, collisions.MarkCount, "a touch farther along is another mark");
    }

    [TestMethod]
    public void MarksCloseTogether_AreJoinedIntoOneObstacle_WhoseVerticesOutlineIt()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        // red felt its way around the crate in the east corridor (the 8-sep-2026 run) and told every bump: three faces
        g.HearBump("red", new Pose(10.25, 6.1, -1.5708), 0.0);    // its north face, touched heading south: the touch at (10.25, 5.85)
        g.HearBump("red", new Pose(10.25, 4.9, 1.5708), 0.0);     // its south face, heading north: (10.25, 5.15)
        g.HearBump("red", new Pose(9.65, 5.5, 0.0), 0.0);         // its west face, heading east: (9.9, 5.5)
        g.HearBump("red", new Pose(7.75, 9.5, 0.0), 0.0);         // and something else, far away in the storage room: (8.0, 9.5)

        Assert.AreEqual(4, collisions.MarkCount, "every touch heard, learned as a mark");
        var obstacles = collisions.All();
        Assert.AreEqual(2, obstacles.Count, "three touches on one thing, one on another");
        var crate = obstacles[0];
        Assert.AreEqual("thing", crate.Kind);
        Assert.AreEqual(3, crate.Size, "the three touches outline one obstacle");
        Assert.AreEqual("polygon", crate.Shape);
        Assert.AreEqual(3, crate.Vertices().Count);
        Assert.AreEqual(10.13, crate.Center.X, 0.01, "centred among its vertices");
        Assert.AreEqual("east", map.ZoneNameOf(crate.Center), "where it stands is the map's to say (ajuste 54)");
        var normals = crate.Vertices().Select(v => v.Heading).ToList();
        Assert.IsTrue(normals.Any(n => Math.Abs(n - -1.5708) < 0.001) && normals.Any(n => Math.Abs(n - 1.5708) < 0.001),
            "each vertex keeps the normal of ITS touch: the faces are told apart");
        Assert.AreEqual("point", obstacles[1].Shape, "one touch is a point, not a figure yet");
    }

    [TestMethod]
    public void AThingIsAFigure_TheBoxAroundItsMarks_GrownByAMarksReachAndAMargin_ThenByTheBody()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        // 14-sep-2026: one figure per thing, so the second way around a crate takes the width the crate showed between the touches
        g.Visit(new Pose(5.5, 9.5, -1.5708), new Position(5.5, 1.5));   // south, down the centre hall
        g.Bump(new Pose(5.5, 6.1, -1.5708), 0.0);                        // the crate's north face, head-on: the mark at (5.5, 5.85)
        Assert.IsFalse(g.FitsAt(new Position(5.5, 5.5)), "beyond the mark: inside the thing");
        Assert.IsFalse(g.FitsAt(new Position(5.0, 5.85)), "half a metre along the surface: within the berth");
        Assert.IsTrue(g.FitsAt(new Position(4.85, 5.85)), "0.65 along the surface: past the berth");
        Assert.IsFalse(g.FitsAt(new Position(5.5, 6.25)), "0.4 back: still within the berth");
        Assert.IsTrue(g.FitsAt(new Position(5.5, 6.7)), "0.85 back — the touch point plus the retreat — stands clear");
        g.Bump(new Pose(5.8, 6.1, -1.5708), 0.0);                        // a second touch 0.3 farther along: one figure for both
        Assert.IsFalse(g.FitsAt(new Position(6.3, 5.85)), "half a metre past the second mark: within the one figure's berth");
        Assert.IsTrue(g.FitsAt(new Position(6.45, 5.85)), "0.65 past it: clear");
        Assert.AreEqual(1, collisions.Figures(body.Radius.InMeters).Count, "one figure, the width the thing showed");
    }

    [TestMethod]
    public void APeersBumpHeard_IsKeptWithWhereThePeerStood_AndNamesWhoWasNear()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        Assert.AreEqual(1, g.HearBump("blue", new Pose(5.1, 9.5, 3.1416), 0.0), "blue says it bumped just past the kitchen's doorway, facing west, pressed on the nose: its touch reckoned at (4.85, 9.5)");
        int heard = collisions.HeardCount;
        Assert.AreEqual("blue", collisions.HeardNear(new Position(4.7, 9.5), 0), "a bump within a body's diameter of mine: it was blue");
        Assert.AreEqual("", collisions.HeardNear(new Position(4.7, 9.5), heard), "nothing heard after that count");
        Assert.AreEqual("", collisions.HeardNear(new Position(10.25, 5.85), 0), "a bump far away is somebody else's business");
        Assert.AreEqual(5.1, collisions.LastKnownPositionOf("blue").X, 1e-9, "where blue was last heard");
        Assert.AreEqual(1, g.HearBump("blue", new Pose(5.1, 9.5, 3.1416), 0.0), "the same word again is already heard: heard once");
        Assert.AreEqual(2, g.HearBump("blue", new Pose(6.2, 9.5, 3.1416), 0.0), "a bump somewhere else is news");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.HearBump("", new Pose(1, 1, 0), 0.0)).Message, "who");
    }

    [TestMethod]
    public void MeetingAPeer_TakesTheMarksBack_AndKeepsThePeerInTheWay_UntilTheRouteEnds()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        var route = g.Visit(new Pose(2.0, 9.5, 0.0), map.Find("north"));   // east across the kitchen, out of its door
        g.HearBump("blue", new Pose(5.1, 9.5, 3.1416), 0.0);    // blue says it bumped just past the doorway, facing west: learned as a mark at (4.85, 9.5)
        g.Bump(new Pose(4.45, 9.5, 0.0), 0.0);                  // my own touch, right there: a mark at (4.7, 9.5)
        g.HearBump("red", new Pose(10.25, 6.1, -1.5708), 0.0);  // and a thing elsewhere: the crate in the east corridor
        Assert.AreEqual(3, collisions.MarkCount);

        Assert.AreEqual(1, g.Met("blue", new Position(4.7, 9.5)), "the golem concludes what it touched was blue");
        Assert.AreEqual(1, collisions.MarkCount, "meeting a body takes both marks back: mine, and the one learned from blue's bump there");
        Assert.AreEqual(1, collisions.EncounterCount);
        Assert.AreEqual(2, collisions.All().Count, "a thing and a peer");
        Assert.AreEqual(1, collisions.Things().Count, "one thing…");
        Assert.AreEqual(1, collisions.PeersInTheWay().Count, "…and a peer still in the way (ajuste 50)");
        Assert.AreEqual(2, collisions.Figures(body.Radius.InMeters).Count, "both are figures the planner skirts");
        Assert.IsFalse(g.FitsAt(new Position(4.7, 9.5)), "no body fits where the peer stands…");
        Assert.IsFalse(g.FitsAt(new Position(4.7 + 2 * 0.25 + Collisions.MarkMargin - 0.01, 9.5)), "…nor within two radii and a margin of it");
        Assert.IsTrue(g.FitsAt(new Position(4.7 + 2 * 0.25 + Collisions.MarkMargin + 0.01, 9.5)), "just past the berth it does");

        route.Abandon("the operator let go");
        Assert.AreEqual(0, collisions.PeersInTheWay().Count, "the route ended: the peer has moved on, as bodies do");
        Assert.AreEqual(1, collisions.Figures(body.Radius.InMeters).Count, "only the thing is a figure now");
        Assert.IsTrue(g.FitsAt(new Position(4.7, 9.5)), "a body fits where the peer was met");
        Assert.AreEqual(1, collisions.EncounterCount, "the encounter stays as history");

        var peer = collisions.All().Single(o => o.Kind == "peer");
        Assert.AreEqual("blue", peer.Who);
        Assert.AreEqual("point", peer.Shape);
        Assert.AreEqual(0, peer.Vertices().Count, "a peer outlines nothing: bodies move on");
        Assert.AreEqual("thing", collisions.All()[0].Kind, "the things come first, the peers after them, as history");
    }

    [TestMethod]
    public void ForgettingAThing_DropsEveryMarkThatOutlinedIt_AndANewTouchThereIsANewObstacle()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        g.HearBump("red", new Pose(10.25, 6.1, -1.5708), 0.0);   // red's three touches on the crate of the east corridor…
        g.HearBump("red", new Pose(10.25, 4.9, 1.5708), 0.0);
        g.HearBump("red", new Pose(9.65, 5.5, 0.0), 0.0);
        g.HearBump("red", new Pose(1.75, 9.5, 0.0), 0.0);        // …and something else, a room away, in the kitchen: (2.0, 9.5)
        Assert.AreEqual(2, collisions.All().Count);
        Assert.IsTrue(collisions.KnowsAt(new Position(10.13, 5.5)), "asked by its centre: what the operator consults before saying it is gone");
        Assert.IsTrue(collisions.KnowsAt(new Position(9.9, 5.5)), "or by one of its vertices");
        Assert.IsFalse(collisions.KnowsAt(new Position(5.5, 5.5)), "and nothing stands in the middle of the centre hall");

        Assert.AreEqual(3, g.Forget(new Position(10.13, 5.5)), "somebody took it away: the three marks that outlined it go at once");
        Assert.AreEqual(1, collisions.All().Count, "the other obstacle is untouched");
        Assert.AreEqual(1, collisions.MarkCount);
        Assert.IsTrue(g.FitsAt(new Position(10.25, 5.5)), "a body may pass there again");
        Assert.AreEqual(0, g.Forget(new Position(10.13, 5.5)), "forgetting nothing drops nothing: it is not a refusal");

        g.HearBump("red", new Pose(10.25, 5.75, -1.5708), 0.0);   // whatever is touched there next is a NEW obstacle: (10.25, 5.5)
        Assert.AreEqual(2, collisions.Things().Count, "the new thing stands on its own, with nothing of the old one");
        Assert.AreEqual(2, collisions.MarkCount, "one mark of the new thing, one of the untouched one");
    }

    [TestMethod]
    public void ForgettingAnEncounter_DropsThatPeer_AndLeavesTheThings()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        g.Met("blue", new Position(4.7, 9.5));
        g.HearBump("red", new Pose(10.25, 6.1, -1.5708), 0.0);
        Assert.AreEqual(2, collisions.All().Count);

        Assert.AreEqual(1, g.Forget(new Position(4.7, 9.5)), "the encounter is dropped");
        Assert.AreEqual(0, collisions.EncounterCount);
        Assert.AreEqual(1, collisions.Things().Count, "and the thing stays");
        Assert.AreEqual(1, collisions.MarkCount);
    }

    [TestMethod]
    public void TheObstaclesOfAZone_AreReadThroughTheZone()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        g.HearBump("red", new Pose(10.25, 6.1, -1.5708), 0.0);   // the crate in the east corridor
        g.HearBump("red", new Pose(1.75, 9.5, 0.0), 0.0);        // something in the kitchen
        Assert.AreEqual(1, collisions.In(map.Find("east")).Count);
        Assert.AreEqual(1, collisions.MarksIn(map.Find("kitchen")).Count);
        Assert.AreEqual(0, collisions.In(map.Find("garage")).Count);
    }

    [TestMethod]
    public void TheModule_RefusesNothingGiven()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, map, collisions);

        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Bump(null, 0.0)).Message, "'me' was not given");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.HearBump("blue", null, 0.0)).Message, "'peerAt' was not given");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Forget(null)).Message, "needs where");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => collisions.KnowsAt(null)).Message, "not given");
    }
}
