using GolemDomain;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Touches;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// THE COLLISIONS MODULE (Touches.Collisions): what the bodies LEARNED by touching — the marks, the bumps heard, the peers
// met — and what it INTERPRETS from them: obstacles as figures (a thing's marks joined by closeness), who was near, what
// blocks a body. Facts are kept; hypotheses are derived, never stored.
[TestClass]
public class CollisionsTests
{
    [TestMethod]
    public void AMark_IsATouchWithItsNormal_AndATouchWithinATenthIsTheSameMark()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions(map);

        Assert.AreEqual(1, collisions.Mark(new Pose(10.25, 5.85, -1.5708)), "the crate's north face, touched heading south");
        Assert.AreEqual(1, collisions.Mark(new Pose(10.25, 5.9, -1.5708)), "a touch within a tenth of a metre is the same mark");
        Assert.AreEqual(2, collisions.Mark(new Pose(10.3, 6.6, -1.5708)), "a touch further along is another mark");
        Assert.AreEqual(-1.5708, collisions.Marks[0].Heading, 1e-9, "the mark keeps the heading of its touch as its normal");
    }

    [TestMethod]
    public void MarksCloseTogether_AreJoinedIntoOneObstacle_WhoseVerticesOutlineIt()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions(map);

        collisions.Mark(new Pose(10.25, 5.85, -1.5708));    // the crate's north face
        collisions.Mark(new Pose(10.25, 5.15, 1.5708));    // its south face
        collisions.Mark(new Pose(9.9, 5.5, 0.0));        // its west face
        collisions.Mark(new Pose(8.0, 9.5, 0.0));        // something else, far away in the storage room

        Assert.AreEqual(4, collisions.MarkCount);
        var obstacles = collisions.All();
        Assert.AreEqual(2, obstacles.Count, "three touches on one thing, one on another");
        var crate = obstacles[0];
        Assert.AreEqual("thing", crate.Kind);
        Assert.AreEqual("east", crate.Where, "the crate stands in the east corridor");
        Assert.AreEqual(3, crate.Size, "the three touches outline one obstacle");
        Assert.AreEqual("polygon", crate.Shape);
        Assert.AreEqual(3, crate.Vertices().Count);
        Assert.AreEqual(10.13, crate.Center.X, 0.01, "centred among its vertices");
        var normals = crate.Vertices().Select(v => v.Heading).ToList();
        Assert.IsTrue(normals.Any(n => Math.Abs(n - -1.5708) < 0.001) && normals.Any(n => Math.Abs(n - 1.5708) < 0.001),
            "each vertex keeps the normal of ITS touch: the faces are told apart");
        Assert.AreEqual("storage", obstacles[1].Where);
        Assert.AreEqual("point", obstacles[1].Shape, "one touch is a point, not a figure yet");
    }

    [TestMethod]
    public void AThingIsAFigure_TheBoxAroundItsMarks_GrownByAMarksReachAndAMargin_ThenByTheBody()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions(map);

        // 14-sep-2026: one figure per thing, so the second way around a crate takes the width the crate showed
        collisions.Mark(new Pose(5.5, 5.85, -1.5708));   // the crate's north face, touched by a body heading south
        Assert.IsTrue(collisions.Blocks(new Position(5.5, 5.5), 0.25), "beyond the mark: inside the thing");
        Assert.IsTrue(collisions.Blocks(new Position(5.0, 5.85), 0.25), "half a metre along the surface: within the berth");
        Assert.IsFalse(collisions.Blocks(new Position(4.85, 5.85), 0.25), "0.65 along the surface: past the berth");
        Assert.IsTrue(collisions.Blocks(new Position(5.5, 6.25), 0.25), "0.4 back: still within the berth");
        Assert.IsFalse(collisions.Blocks(new Position(5.5, 6.7), 0.25), "0.85 back — the touch point plus the retreat — stands clear");
        collisions.Mark(new Pose(5.8, 5.85, -1.5708));   // a second touch 0.3 further along: one figure for both
        Assert.IsTrue(collisions.Blocks(new Position(6.3, 5.85), 0.25), "half a metre past the second mark: within the one figure's berth");
        Assert.IsFalse(collisions.Blocks(new Position(6.45, 5.85), 0.25), "0.65 past it: clear");
        Assert.AreEqual(1, collisions.Figures(0.25).Count, "one figure, the width the thing showed");
    }

    [TestMethod]
    public void APeersBumpHeard_IsKeptWithWhereThePeerStood_AndNamesWhoWasNear()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions(map);

        Assert.AreEqual(1, collisions.Hear("blue", new Position(4.85, 9.5), new Position(5.1, 9.5)), "blue's touch, and where blue stood");
        int heard = collisions.HeardCount;
        Assert.AreEqual("blue", collisions.HeardNear(new Position(4.7, 9.5), 0), "a bump within a body's diameter of mine: it was blue");
        Assert.AreEqual("", collisions.HeardNear(new Position(4.7, 9.5), heard), "nothing heard after that count");
        Assert.AreEqual("", collisions.HeardNear(new Position(10.25, 5.85), 0), "a bump far away is somebody else's business");
        Assert.AreEqual(5.1, collisions.LastKnownPositionOf("blue").X, 1e-9, "where blue was last heard");
        Assert.IsTrue(collisions.HeardAlready("blue", new Position(4.85, 9.5), new Position(5.1, 9.5)), "the same word again is already heard");
        Assert.IsFalse(collisions.HeardAlready("blue", new Position(6.0, 9.5), new Position(6.2, 9.5)), "a bump somewhere else is news");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => collisions.Hear("", new Position(1, 1), new Position(1.2, 1))).Message, "who");
    }

    [TestMethod]
    public void MeetingAPeer_TakesTheMarksBack_AndKeepsThePeerInTheWay_UntilThePeersMoveOn()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions(map);

        collisions.Hear("blue", new Position(4.85, 9.5), new Position(5.1, 9.5));
        collisions.Mark(new Pose(4.85, 9.5, 0.0));                // what blue's bump was learned as
        collisions.Mark(new Pose(4.7, 9.5, 0.0));                 // my own touch, right there
        collisions.Mark(new Pose(10.25, 5.85, -1.5708));             // and a thing elsewhere
        Assert.AreEqual(3, collisions.MarkCount);

        Assert.AreEqual(1, collisions.Meet("blue", new Position(4.7, 9.5)));
        collisions.Unmark(new Position(4.7, 9.5));
        collisions.UnmarkHeardFrom("blue", new Position(4.7, 9.5));
        Assert.AreEqual(1, collisions.MarkCount, "meeting a body takes both marks back: mine, and the one learned from blue's bump there");
        Assert.AreEqual(1, collisions.EncounterCount);
        Assert.AreEqual(2, collisions.All().Count, "a thing and a peer");
        Assert.AreEqual(1, collisions.Things().Count, "one thing…");
        Assert.AreEqual(1, collisions.PeersInTheWay().Count, "…and a peer still in the way (ajuste 50)");
        Assert.AreEqual(2, collisions.Figures(0.25).Count, "both are figures the planner skirts");
        Assert.IsTrue(collisions.Blocks(new Position(4.7, 9.5), 0.25), "no body fits where the peer stands…");
        Assert.IsTrue(collisions.Blocks(new Position(4.7 + 2 * 0.25 + Collisions.MarkMargin - 0.01, 9.5), 0.25), "…nor within two radii and a margin of it");
        Assert.IsFalse(collisions.Blocks(new Position(4.7 + 2 * 0.25 + Collisions.MarkMargin + 0.01, 9.5), 0.25), "just past the berth it does");
        Assert.AreEqual(1, collisions.PeersMovedOn(), "the route ended: the peer moved on…");
        Assert.AreEqual(0, collisions.PeersInTheWay().Count);
        Assert.AreEqual(1, collisions.Figures(0.25).Count, "…only the thing is a figure now");
        Assert.IsFalse(collisions.Blocks(new Position(4.7, 9.5), 0.25), "a body fits where the peer was met");
        Assert.AreEqual(1, collisions.EncounterCount, "the encounter stays as history");
        Assert.AreEqual(0, collisions.PeersMovedOn(), "nobody left to move on");

        var peer = collisions.All().Single(o => o.Kind == "peer");
        Assert.AreEqual("blue", peer.Who);
        Assert.AreEqual("north", peer.Where, "the encounter stands in the north hall");
        Assert.AreEqual("point", peer.Shape);
        Assert.AreEqual(0, peer.Vertices().Count, "a peer outlines nothing: bodies move on");
        Assert.AreEqual("thing", collisions.All()[0].Kind, "the things come first, the peers after them, as history");
    }

    [TestMethod]
    public void ForgettingAThing_DropsEveryMarkThatOutlinedIt_AndANewTouchThereIsANewObstacle()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions(map);

        collisions.Mark(new Pose(10.25, 5.85, -1.5708));   // three touches on the crate of the east corridor
        collisions.Mark(new Pose(10.25, 5.15, 1.5708));
        collisions.Mark(new Pose(9.9, 5.5, 0.0));
        collisions.Mark(new Pose(2.0, 9.5, 0.0));       // and something else, a room away
        Assert.AreEqual(2, collisions.All().Count);
        Assert.IsTrue(collisions.KnowsAt(new Position(10.13, 5.5)), "asked by its centre");
        Assert.IsTrue(collisions.KnowsAt(new Position(9.9, 5.5)), "or by one of its vertices");
        Assert.IsFalse(collisions.KnowsAt(new Position(5.5, 5.5)), "and nothing stands in the middle of the centre hall");

        Assert.AreEqual(3, collisions.Forget(new Position(10.13, 5.5)), "the three marks that outlined it go at once");
        Assert.AreEqual(1, collisions.All().Count, "the other obstacle is untouched");
        Assert.AreEqual(1, collisions.MarkCount);
        Assert.IsFalse(collisions.Blocks(new Position(10.25, 5.5), 0.25), "a body may pass there again");
        Assert.AreEqual(0, collisions.Forget(new Position(10.13, 5.5)), "forgetting nothing drops nothing: it is not a refusal");

        collisions.Mark(new Pose(10.25, 5.5, -1.5708));    // whatever is touched there next is a NEW obstacle
        Assert.AreEqual(2, collisions.Things().Count, "the new thing stands on its own, with nothing of the old one");
        Assert.AreEqual(2, collisions.MarkCount, "one mark of the new thing, one of the untouched one");
    }

    [TestMethod]
    public void ForgettingAnEncounter_DropsThatPeer_AndLeavesTheThings()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions(map);

        collisions.Meet("blue", new Position(4.7, 9.5));
        collisions.Mark(new Pose(10.25, 5.85, -1.5708));
        Assert.AreEqual(2, collisions.All().Count);

        Assert.AreEqual(1, collisions.Forget(new Position(4.7, 9.5)), "the encounter is dropped");
        Assert.AreEqual(0, collisions.EncounterCount);
        Assert.AreEqual(1, collisions.Things().Count, "and the thing stays");
        Assert.AreEqual(1, collisions.MarkCount);
    }

    [TestMethod]
    public void TheObstaclesOfAZone_AreReadThroughTheZone()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions(map);

        collisions.Mark(new Pose(10.25, 5.85, -1.5708));
        collisions.Mark(new Pose(2.0, 9.5, 0.0));
        Assert.AreEqual(1, collisions.In(map.Find("east")).Count);
        Assert.AreEqual(1, collisions.MarksIn(map.Find("kitchen")).Count);
        Assert.AreEqual(0, collisions.In(map.Find("garage")).Count);
    }

    [TestMethod]
    public void TheModule_IsMeasuredOverALayout_AndRefusesNothingGiven()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions(map);

        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Collisions(null)).Message, "layout");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => collisions.Mark(null)).Message, "needs the pose of the touch");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => collisions.KnowsAt(null)).Message, "not given");
    }
}
