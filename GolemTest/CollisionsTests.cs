using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Touches;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using static GolemTest.DomainFixture;

namespace GolemTest;

// THE COLLISIONS MODULE (Touches.Collisions): what the bodies LEARNED by touching — the marks, the bumps heard, the peers
// met — and what it INTERPRETS from them: obstacles as figures (a thing's marks joined by closeness), who was near, what
// blocks a body. Facts are kept; hypotheses are derived, never stored.
[TestClass]
public class CollisionsTests
{
    private MapLayout map;
    private Collisions collisions;

    [TestInitialize]
    public void OverTheWarehouse() { map = Catalog.Warehouse(); collisions = new Collisions(map); }

    [TestMethod]
    public void AMark_IsATouchWithItsNormal_AndATouchWithinATenthIsTheSameMark()
    {
        Assert.AreEqual(1, collisions.Mark(At(10.25, 5.85, South)), "the crate's north face, touched heading south");
        Assert.AreEqual(1, collisions.Mark(At(10.25, 5.9, South)), "a touch within a tenth of a metre is the same mark");
        Assert.AreEqual(2, collisions.Mark(At(10.3, 6.6, South)), "a touch further along is another mark");
        Assert.AreEqual(South, collisions.Marks[0].Heading, 1e-9, "the mark keeps the heading of its touch as its normal");
    }

    [TestMethod]
    public void MarksCloseTogether_AreJoinedIntoOneObstacle_WhoseVerticesOutlineIt()
    {
        PlantMark(collisions, 10.25, 5.85, South);    // the crate's north face
        PlantMark(collisions, 10.25, 5.15, North);    // its south face
        PlantMark(collisions, 9.9, 5.5, East);        // its west face
        PlantMark(collisions, 8.0, 9.5, East);        // something else, far away in the storage room

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
        Assert.IsTrue(normals.Any(n => Math.Abs(n - South) < 0.001) && normals.Any(n => Math.Abs(n - North) < 0.001),
            "each vertex keeps the normal of ITS touch: the faces are told apart");
        Assert.AreEqual("storage", obstacles[1].Where);
        Assert.AreEqual("point", obstacles[1].Shape, "one touch is a point, not a figure yet");
    }

    [TestMethod]
    public void AThingIsAFigure_TheBoxAroundItsMarks_GrownByAMarksReachAndAMargin_ThenByTheBody()
    {
        // 14-sep-2026: one figure per thing, so the second way around a crate takes the width the crate showed
        PlantMark(collisions, 5.5, 5.85, South);   // the crate's north face, touched by a body heading south
        Assert.IsTrue(collisions.Blocks(P(5.5, 5.5), Radius), "beyond the mark: inside the thing");
        Assert.IsTrue(collisions.Blocks(P(5.0, 5.85), Radius), "half a metre along the surface: within the berth");
        Assert.IsFalse(collisions.Blocks(P(4.85, 5.85), Radius), "0.65 along the surface: past the berth");
        Assert.IsTrue(collisions.Blocks(P(5.5, 6.25), Radius), "0.4 back: still within the berth");
        Assert.IsFalse(collisions.Blocks(P(5.5, 6.7), Radius), "0.85 back — the touch point plus the retreat — stands clear");
        PlantMark(collisions, 5.8, 5.85, South);   // a second touch 0.3 further along: one figure for both
        Assert.IsTrue(collisions.Blocks(P(6.3, 5.85), Radius), "half a metre past the second mark: within the one figure's berth");
        Assert.IsFalse(collisions.Blocks(P(6.45, 5.85), Radius), "0.65 past it: clear");
        Assert.AreEqual(1, collisions.Figures(Radius).Count, "one figure, the width the thing showed");
    }

    [TestMethod]
    public void APeersBumpHeard_IsKeptWithWhereThePeerStood_AndNamesWhoWasNear()
    {
        Assert.AreEqual(1, collisions.Hear("blue", P(4.85, 9.5), P(5.1, 9.5)), "blue's touch, and where blue stood");
        int heard = collisions.HeardCount;
        Assert.AreEqual("blue", collisions.HeardNear(P(4.7, 9.5), 0), "a bump within a body's diameter of mine: it was blue");
        Assert.AreEqual("", collisions.HeardNear(P(4.7, 9.5), heard), "nothing heard after that count");
        Assert.AreEqual("", collisions.HeardNear(P(10.25, 5.85), 0), "a bump far away is somebody else's business");
        Assert.AreEqual(5.1, collisions.LastKnownPositionOf("blue").X, 1e-9, "where blue was last heard");
        Assert.IsTrue(collisions.HeardAlready("blue", P(4.85, 9.5), P(5.1, 9.5)), "the same word again is already heard");
        Assert.IsFalse(collisions.HeardAlready("blue", P(6.0, 9.5), P(6.2, 9.5)), "a bump somewhere else is news");
        Refuses(() => collisions.Hear("", P(1, 1), P(1.2, 1)), "who");
    }

    [TestMethod]
    public void MeetingAPeer_TakesTheMarksBack_AndKeepsTheEncounterAsHistoryNeverGeometry()
    {
        collisions.Hear("blue", P(4.85, 9.5), P(5.1, 9.5));
        PlantMark(collisions, 4.85, 9.5, East);                // what blue's bump was learned as
        PlantMark(collisions, 4.7, 9.5, East);                 // my own touch, right there
        PlantMark(collisions, 10.25, 5.85, South);             // and a thing elsewhere
        Assert.AreEqual(3, collisions.MarkCount);

        Assert.AreEqual(1, collisions.Meet("blue", P(4.7, 9.5)));
        collisions.Unmark(P(4.7, 9.5));
        collisions.UnmarkHeardFrom("blue", P(4.7, 9.5));
        Assert.AreEqual(1, collisions.MarkCount, "meeting a body takes both marks back: mine, and the one learned from blue's bump there");
        Assert.AreEqual(1, collisions.EncounterCount);
        Assert.AreEqual(2, collisions.All().Count, "a thing and a peer");
        Assert.AreEqual(1, collisions.Things().Count, "only the thing is planned around");
        Assert.IsFalse(collisions.Blocks(P(4.7, 9.5), Radius), "the peer moved on: a body fits where it was met");

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
        PlantMark(collisions, 10.25, 5.85, South);   // three touches on the crate of the east corridor
        PlantMark(collisions, 10.25, 5.15, North);
        PlantMark(collisions, 9.9, 5.5, East);
        PlantMark(collisions, 2.0, 9.5, East);       // and something else, a room away
        Assert.AreEqual(2, collisions.All().Count);
        Assert.IsTrue(collisions.KnowsAt(P(10.13, 5.5)), "asked by its centre");
        Assert.IsTrue(collisions.KnowsAt(P(9.9, 5.5)), "or by one of its vertices");
        Assert.IsFalse(collisions.KnowsAt(P(5.5, 5.5)), "and nothing stands in the middle of the centre hall");

        Assert.AreEqual(3, collisions.Forget(P(10.13, 5.5)), "the three marks that outlined it go at once");
        Assert.AreEqual(1, collisions.All().Count, "the other obstacle is untouched");
        Assert.AreEqual(1, collisions.MarkCount);
        Assert.IsFalse(collisions.Blocks(P(10.25, 5.5), Radius), "a body may pass there again");
        Assert.AreEqual(0, collisions.Forget(P(10.13, 5.5)), "forgetting nothing drops nothing: it is not a refusal");

        PlantMark(collisions, 10.25, 5.5, South);    // whatever is touched there next is a NEW obstacle
        Assert.AreEqual(2, collisions.Things().Count, "the new thing stands on its own, with nothing of the old one");
        Assert.AreEqual(2, collisions.MarkCount, "one mark of the new thing, one of the untouched one");
    }

    [TestMethod]
    public void ForgettingAnEncounter_DropsThatPeer_AndLeavesTheThings()
    {
        collisions.Meet("blue", P(4.7, 9.5));
        PlantMark(collisions, 10.25, 5.85, South);
        Assert.AreEqual(2, collisions.All().Count);

        Assert.AreEqual(1, collisions.Forget(P(4.7, 9.5)), "the encounter is dropped");
        Assert.AreEqual(0, collisions.EncounterCount);
        Assert.AreEqual(1, collisions.Things().Count, "and the thing stays");
        Assert.AreEqual(1, collisions.MarkCount);
    }

    [TestMethod]
    public void TheObstaclesOfAZone_AreReadThroughTheZone()
    {
        PlantMark(collisions, 10.25, 5.85, South);
        PlantMark(collisions, 2.0, 9.5, East);
        Assert.AreEqual(1, collisions.In(map.Find("east")).Count);
        Assert.AreEqual(1, collisions.MarksIn(map.Find("kitchen")).Count);
        Assert.AreEqual(0, collisions.In(map.Find("garage")).Count);
    }

    [TestMethod]
    public void TheModule_IsMeasuredOverALayout_AndRefusesNothingGiven()
    {
        Refuses(() => new Collisions(null), "layout");
        Refuses(() => collisions.Mark(null), "needs the pose of the touch");
        Refuses(() => collisions.KnowsAt(null), "not given");
    }
}
