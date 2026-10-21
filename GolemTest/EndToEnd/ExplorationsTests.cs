using GolemDomain;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Robots;
using GolemDomain.Routes;
using GolemDomain.Scenarios;
using GolemDomain.Touches;
using GolemDomain.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// THE EXPLORATION (propuesta 120, 20-oct-2026): the golem's module that keeps the trail of where its body went and deduces a map from
// the trail and the touches — through the golem, as the journal writes it: the routes walked, the bumps reported, the waking.
[TestClass]
public class ExplorationsTests
{
    // A WALK DOWN A CORRIDOR OF THE MAZE: every cell the body crossed is free in the deduced map, open to the next; the unknown next
    // to them is the frontier; the drawing shows the cells walked as dots in a field of question marks
    [TestMethod]
    public void AWalkDownTheMaze_DeducesTheCellsCrossed_OpenToEachOther_AndTheFrontierAroundThem()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body);
        g.Enter(new Scenario(Catalog.Maze(), new Collisions()));
        Assert.AreEqual(0, g.Exploration.StrideCount, "nothing walked yet");
        Assert.AreEqual(0, g.Exploration.Covered);

        // from the cradle (cell-9-5) east to cell-11-5, three cells down the corridor
        var route = g.Visit(new Pose(14.25, 8.25, 0.0), new Position(17.25, 8.25));
        WalkToTheEnd(route);
        Assert.AreEqual("completed", route.Status);

        Assert.IsTrue(g.Exploration.StrideCount >= 1, "the advance is a stride of the trail");
        Assert.AreEqual(3.0, g.Exploration.Distance, 0.01, "three metres walked, as the chassis counted them");
        Assert.AreEqual(3, g.Exploration.Covered, "cell-9-5, cell-10-5 and cell-11-5 are free");
        Assert.AreEqual(0, g.Exploration.WallCount, "nothing touched: no wall deduced");

        var deduced = g.Exploration.Deduced();
        Assert.AreEqual("explored-maze", deduced.Name);
        Assert.AreEqual(3, deduced.ZoneCount);
        Assert.IsTrue(deduced.Connects(deduced.Find("cell-9-5"), deduced.Find("cell-10-5")), "the trail crossed between them: open");
        Assert.IsTrue(deduced.Connects(deduced.Find("cell-10-5"), deduced.Find("cell-11-5")));
        Assert.AreEqual("cell-10-5", deduced.ZoneNameOf(new Position(15.75, 8.25)));

        // the frontier: the unknown next to the three — west of the first, east of the last, north and south of each
        var frontier = g.Exploration.Frontier();
        Assert.AreEqual(8, frontier.Count, string.Join(" ", frontier.Select(f => $"({f.X}, {f.Y})")));
        Assert.IsTrue(frontier.Any(f => Math.Abs(f.X - 12.75) < 1e-9 && Math.Abs(f.Y - 8.25) < 1e-9), "west of the cradle");
        Assert.IsTrue(frontier.Any(f => Math.Abs(f.X - 18.75) < 1e-9 && Math.Abs(f.Y - 8.25) < 1e-9), "east of the last cell");
        Assert.IsFalse(g.Exploration.IsComplete);

        // the drawing, in the maze's own text over the envelope of the map's zones — the free cells, columns 1..17 and rows 1..19 (the
        // outer ring of walls is no zone): 19 rows of 17, the walked cells dots on the row of the cradle (row 5, the 15th line from the north)
        var drawing = g.Exploration.Drawing();
        Assert.AreEqual(19, drawing.Count);
        Assert.AreEqual(17, drawing[0].Length);
        Assert.AreEqual("????????...??????", drawing[19 - 5]);
        Assert.IsTrue(drawing.Where((_, i) => i != 19 - 5).All(l => l == new string('?', 17)), "every other row unknown");
    }

    // A TOUCH DEDUCES A WALL: on the open floor (no wall known: every touch is a mark) the body walks north and touches something
    // at the boundary of its cell — the cell beyond the touch is a wall in the hypothesis, drawn '#', and that side is no frontier
    [TestMethod]
    public void ATouch_DeducesTheCellBeyondItAsAWall_AndTakesItOffTheFrontier()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body);
        g.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));

        // east along y = 5.5 from (5.0, 5.5) to (8.0, 5.5): cells (3,3), (4,3), (5,3) with a grain of 1.5
        var east = g.Visit(new Pose(5.0, 5.5, 0.0), new Position(8.0, 5.5));
        WalkToTheEnd(east);
        Assert.AreEqual(3, g.Exploration.Covered);

        // then north toward (8.0, 9.0): the body turns, advances, and touches on its nose standing at (8.0, 7.2) — the touch lands at
        // (8.0, 7.45), a hair short of the cell boundary at 7.5, pointing north into cell (5,5)
        var north = g.Visit(new Pose(8.0, 5.5, 0.0), new Position(8.0, 9.0));
        Step(north);   // the turn to face north
        Assert.AreEqual("advance", north.Order);
        g.Bump(new Pose(8.0, 7.2, Math.PI / 2), 0.0);
        Assert.AreEqual(1, g.Current.Collisions.MarkCount, "on the open floor every touch is a mark");

        Assert.AreEqual(1, g.Exploration.WallCount);
        var wall = g.Exploration.Cells().Single(c => !c.IsFree);
        Assert.AreEqual(5, wall.Col);
        Assert.AreEqual(5, wall.Row);
        Assert.IsTrue(g.Exploration.Cells().Any(c => c.IsFree && c.Col == 5 && c.Row == 4), "the body walked into cell (5,4) before the touch");
        Assert.IsFalse(g.Exploration.Frontier().Any(f => Math.Abs(f.X - 8.25) < 1e-9 && Math.Abs(f.Y - 8.25) < 1e-9), "the wall cell is no frontier");

        var drawing = g.Exploration.Drawing();   // the open floor is 11 × 11: 8 rows of 8 cells at a grain of 1.5
        Assert.AreEqual(8, drawing.Count);
        Assert.AreEqual('#', drawing[7 - 5][5], string.Join("\n", drawing));
        Assert.AreEqual('.', drawing[7 - 4][5]);
    }

    // CARRIED, NOT WALKED: woken somewhere else (a teleport, a rebirth) the trail goes on from there and draws no stride across the jump
    [TestMethod]
    public void AWaking_AnchorsTheTrail_WithoutAStrideAcrossTheJump()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body);
        g.Enter(new Scenario(Catalog.Maze(), new Collisions()));
        var route = g.Visit(new Pose(14.25, 8.25, 0.0), new Position(15.75, 8.25));
        WalkToTheEnd(route);
        int strides = g.Exploration.StrideCount;
        Assert.AreEqual(2, g.Exploration.Covered);

        g.Wake(new Pose(2.25, 29.25, 0.0));   // carried to the north-west corner of the maze
        Assert.AreEqual(strides, g.Exploration.StrideCount, "no stride across the jump");
        Assert.AreEqual(3, g.Exploration.Covered, "the corner is known free: the body stands in it (fase B) — but nothing joins it to the two cells walked");
        Assert.IsFalse(g.Exploration.Deduced().Connects(g.Exploration.Deduced().Find("cell-1-19"), g.Exploration.Deduced().Find("cell-9-5")));

        var on = g.Visit(new Pose(2.25, 29.25, 0.0), new Position(3.75, 29.25));
        WalkToTheEnd(on);
        Assert.AreEqual(strides + 1, g.Exploration.StrideCount, "the walk from where it woke is a stride");
        Assert.AreEqual(4, g.Exploration.Covered);
    }

    // ONE TRAIL PER SCENARIO: what was walked in the maze stays the maze's; the open floor starts empty
    [TestMethod]
    public void EachScenario_HasItsOwnTrail()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body);
        g.Enter(new Scenario(Catalog.Maze(), new Collisions()));
        WalkToTheEnd(g.Visit(new Pose(14.25, 8.25, 0.0), new Position(15.75, 8.25)));
        Assert.AreEqual(2, g.Exploration.Covered);

        g.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        Assert.AreEqual(1, g.Exploration.Covered, "the open floor's trail is born with the cell the body stands in, nothing walked");
        Assert.AreEqual(0, g.Exploration.StrideCount);

        g.Enter(g.Scenario("maze"));
        Assert.AreEqual(2, g.Exploration.Covered, "the maze's trail was kept");
    }

    // THE MODE (fase B; Juan: "uno inicia el modo exploración y él solo se queda ahí un tiempo indefinido hasta lograr explorar todo lo que
    // encuentre"): on a blind floor of four cells the golem probes one step into the unknown at a time — east first, then north, then west —
    // until nothing unknown is left next to what it knows, and the exploration finishes by itself
    [TestMethod]
    public void TheMode_ProbesIntoTheUnknown_StepByStep_UntilNothingUnknownIsLeft()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body);
        g.Enter(new Scenario(Catalog.Blind(3.0, 3.0), new Collisions()));
        g.Wake(new Pose(0.75, 0.75, 0.0));
        Assert.IsFalse(g.Exploration.Exploring);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Exploration.Probe(new Pose(0.75, 0.75, 0.0))).Message, "not exploring");

        g.Exploration.Start();
        Assert.IsTrue(g.Exploration.Exploring);
        Assert.IsTrue(g.Exploration.Probing, "standing free with the unknown all around");
        Assert.IsFalse(g.Exploration.Concluded);

        var east = g.Exploration.Probe(new Pose(0.75, 0.75, 0.0));
        Assert.IsTrue(east.IsProbe);
        Assert.AreEqual("unknown@2.25,0.75", east.AsPlan(), "one step east, into the cell it does not know");
        Assert.AreEqual("advance", east.Order, "facing east already: no turn");
        Assert.AreEqual(1.5, east.Amount, 0.01);
        Assert.IsFalse(g.Exploration.Probing, "a route pending: no probe opens");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Exploration.Probe(new Pose(0.75, 0.75, 0.0))).Message, "pending");
        WalkToTheEnd(east);
        Assert.AreEqual("completed", east.Status);
        Assert.IsFalse(east.EndedByTouch);
        Assert.AreEqual(2, g.Exploration.Covered);

        var north = g.Exploration.Probe(new Pose(2.25, 0.75, 0.0));
        Assert.AreEqual("unknown@2.25,2.25", north.AsPlan(), "east is the floor's edge: north next");
        Assert.AreEqual("turnLeft", north.Order);
        WalkToTheEnd(north);
        var west = g.Exploration.Probe(new Pose(2.25, 2.25, 1.5708));
        Assert.AreEqual("unknown@0.75,2.25", west.AsPlan());
        WalkToTheEnd(west);

        Assert.AreEqual(4, g.Exploration.Covered, "the four cells walked");
        Assert.AreEqual(0, g.Exploration.FrontierCount);
        Assert.IsTrue(g.Exploration.IsComplete);
        Assert.IsFalse(g.Exploration.Probing, "nothing unknown is left");
        Assert.IsTrue(g.Exploration.Concluded, "exploring and complete: the host writes the finish");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Exploration.Probe(new Pose(0.75, 2.25, 3.1416))).Message, "complete");
        Assert.AreEqual(3, g.Exploration.Probes);
        g.Exploration.Finish();
        Assert.IsFalse(g.Exploration.Exploring);
        Assert.IsTrue(g.Exploration.Finished);
        Assert.AreEqual(4, g.Exploration.Deduced().ZoneCount, "the map deduced: the whole floor, every cell open to the next the body crossed");
    }

    // A PROBE THAT TOUCHES: the step into the unknown meets something — the mark is learned where it landed, the cell beyond it is a wall,
    // the route asks the body BACK to where the step began and is complete there, and the next probe goes somewhere else
    [TestMethod]
    public void AProbeThatTouches_LearnsTheWall_BacksToWhereTheStepBegan_AndTheNextProbeGoesElsewhere()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body);
        g.Enter(new Scenario(Catalog.Blind(4.5, 3.0), new Collisions()));
        g.Wake(new Pose(0.75, 0.75, 0.0));
        g.Exploration.Start();

        var east = g.Exploration.Probe(new Pose(0.75, 0.75, 0.0));
        Assert.AreEqual("unknown@2.25,0.75", east.AsPlan());
        // halfway, the body is pressed on its nose standing at (1.25, 0.75): the touch lands at (1.5, 0.75), the cell boundary, pointing east
        g.Bump(new Pose(1.25, 0.75, 0.0), 0.0);
        Assert.AreEqual(1, g.Current.Collisions.MarkCount, "a probe learns what it touches: a mark, on a map that knows no wall");
        Assert.IsTrue(east.IsPending());
        Assert.AreEqual("via@0.75,0.75", east.AsPlan(), "back to where the step began, walked as any leg: turned toward and advanced");
        while (east.Order is "turnLeft" or "turnRight") Step(east);
        Assert.AreEqual("advance", east.Order);
        Assert.AreEqual(0.5, east.Amount, 0.01);
        east.Reach(new Pose(0.75, 0.75, Math.PI));
        Assert.AreEqual("completed", east.Status);
        Assert.IsTrue(east.EndedByTouch);

        Assert.AreEqual(1, g.Exploration.WallCount);
        var wall = g.Exploration.Cells().Single(c => !c.IsFree);
        Assert.AreEqual((1, 0), (wall.Col, wall.Row));
        Assert.AreEqual(1, g.Exploration.Covered, "nothing new walked");
        Assert.IsFalse(g.Exploration.Frontier().Any(f => Math.Abs(f.X - 2.25) < 1e-9 && Math.Abs(f.Y - 0.75) < 1e-9), "the wall is no frontier");
        CollectionAssert.AreEqual(new[] { "???", ".#?" }, g.Exploration.Drawing().ToList(), "3 x 2 cells: the cell stood in free, the one touched a wall, the rest unknown");

        var north = g.Exploration.Probe(new Pose(0.75, 0.75, 0.0));
        Assert.AreEqual("unknown@0.75,2.25", north.AsPlan(), "east is a wall now: north is the next unknown");
        Assert.AreEqual(2, g.Exploration.Probes);

        g.Exploration.Stop();
        Assert.IsFalse(g.Exploration.Exploring);
        Assert.IsTrue(north.IsPending(), "the probe underway ends on its own");
        Assert.IsFalse(g.Exploration.Probing);
    }

    // THE MAP EXPORTED, ENTERED AND DROPPED (propuesta 120; Juan: "exportar mapa y se pasa a mapas de ese golem… borrar algún mapa completo del golem"):
    // the deduced map becomes a scenario of the golem's, read in the frame it was deduced in; the golem may enter it and plan on it; it may be dropped
    // once the golem left it; a release's map never
    [TestMethod]
    public void TheDeducedMap_IsExportedAsAScenario_EnteredLikeAnyOther_AndDroppedWhenLeft()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body);
        g.Enter(new Scenario(Catalog.Blind(3.0, 3.0), new Collisions()));
        g.Wake(new Pose(0.75, 0.75, 0.0));
        g.Exploration.Start();
        WalkToTheEnd(g.Exploration.Probe(new Pose(0.75, 0.75, 0.0)));
        WalkToTheEnd(g.Exploration.Probe(new Pose(2.25, 0.75, 0.0)));
        g.Exploration.Stop();
        Assert.AreEqual(3, g.Exploration.Covered, "stopped: what was explored stays");

        var exported = g.Export(g.Exploration.Deduced("explored-blind-1"));
        Assert.AreEqual("explored-blind-1", exported.Name);
        Assert.IsTrue(exported.Exported);
        Assert.AreEqual("blind", exported.Frame, "read in the frame it was deduced in");
        Assert.AreEqual(2, g.Scenarios().Count);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Export(g.Exploration.Deduced("explored-blind-1"))).Message, "already knows");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Drop(g.Scenario("blind"))).Message, "release's map");

        g.Enter(exported);
        Assert.AreEqual("explored-blind-1", g.Current.Name);
        var route = g.Visit(new Pose(0.75, 0.75, 0.0), new Position(2.25, 2.25));
        Assert.AreEqual("cell-1-0:middle@2.25,0.75 > cell-1-1@2.25,2.25", route.AsPlan(), "the planner plans on the deduced map: through the cells walked, by their openings — square, by the middles of the narrow cells (propuesta 110)");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Drop(exported)).Message, "is in", "the golem is in it: it uses another map first");

        route.Abandon("the test leaves");
        g.Enter(g.Scenario("blind"));
        g.Drop(exported);
        Assert.AreEqual(1, g.Scenarios().Count);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Scenario("explored-blind-1")).Message, "explored-blind-1");
    }

    // WHAT WAS EXPLORED, CLEARED (Juan: "clear de lo actualmente explorado, como un reset"): the trail starts afresh from where the body stands, the
    // marks go with it; refused while exploring
    [TestMethod]
    public void TheExploration_IsCleared_TrailAndMarks_OnceStopped()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body);
        g.Enter(new Scenario(Catalog.Blind(4.5, 3.0), new Collisions()));
        g.Wake(new Pose(0.75, 0.75, 0.0));
        g.Exploration.Start();
        var east = g.Exploration.Probe(new Pose(0.75, 0.75, 0.0));
        g.Bump(new Pose(1.25, 0.75, 0.0), 0.0);
        WalkToTheEnd(east);
        WalkToTheEnd(g.Exploration.Probe(new Pose(0.75, 0.75, Math.PI)));
        Assert.AreEqual(2, g.Exploration.Covered);
        Assert.AreEqual(1, g.Exploration.WallCount);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Exploration.Clear()).Message, "stop first");

        g.Exploration.Stop();
        g.Exploration.Clear();
        Assert.AreEqual(0, g.Exploration.StrideCount);
        Assert.AreEqual(1, g.Exploration.Covered, "only the cell the body stands in");
        Assert.AreEqual(0, g.Exploration.WallCount, "the marks are forgotten");
        Assert.AreEqual(0, g.Current.Collisions.MarkCount);
        Assert.AreEqual(0, g.Exploration.Probes);
    }

    // THE BLINDFOLD (Juan: "quisiera poder lanzarlo sin mapa"): a scenario of the golem's own with no map at all, named after the one it was in and
    // read in its frame; nothing bounds the unknown but what it touches — touched on every side, one cell is a complete exploration
    [TestMethod]
    public void TheBlindfold_IsAScenarioWithNoMap_WhereOnlyTheTouchesBoundTheUnknown()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body);
        g.Enter(new Scenario(Catalog.Maze(), new Collisions()));
        g.Wake(new Pose(14.25, 8.25, 0.0));

        var blind = g.Exploration.Blindfold();
        Assert.AreEqual("blind-maze", blind.Name);
        Assert.IsTrue(blind.Exported, "the golem's own, droppable — never a release's");
        Assert.AreEqual("maze", blind.Frame, "read in the frame of the map it was in");
        Assert.AreEqual(0, blind.Map.ZoneCount, "no map at all");
        Assert.AreSame(blind, g.Exploration.Blindfold(), "the blindfold of this map, known already: taken up again");
        Assert.IsTrue(g.Exploration.Sighted, "in the maze the golem sees: exploring blindfolds it first");

        g.Enter(blind);
        Assert.IsFalse(g.Exploration.Sighted);
        g.Exploration.Start();
        Assert.AreEqual(4, g.Exploration.FrontierCount, "nothing bounds the unknown: all four sides");
        foreach (var (heading, touchX, touchY) in new[] { (0.0, 14.75, 8.25), (Math.PI / 2, 14.25, 8.75), (Math.PI, 13.75, 8.25), (-Math.PI / 2, 14.25, 7.75) })
        {
            var probe = g.Exploration.Probe(new Pose(14.25, 8.25, heading));
            while (probe.Order is "turnLeft" or "turnRight") Step(probe);
            Assert.AreEqual("advance", probe.Order);
            g.Bump(new Pose(touchX, touchY, heading), 0.0);   // pressed on its nose a body's length past the centre: the touch lands on the cell's boundary
            while (probe.Order is "turnLeft" or "turnRight") Step(probe);
            Assert.AreEqual("advance", probe.Order, "back to the centre, faced and walked");
            probe.Reach(new Pose(14.25, 8.25, heading + Math.PI));
            Assert.IsTrue(probe.EndedByTouch);
        }
        Assert.AreEqual(4, g.Exploration.WallCount);
        Assert.AreEqual(1, g.Exploration.Covered);
        Assert.IsTrue(g.Exploration.IsComplete, "walled in on every side: nothing unknown is left next to the known");
        Assert.IsTrue(g.Exploration.Concluded);
        CollectionAssert.AreEqual(new[] { "?#?", "#.#", "?#?" }, g.Exploration.Drawing().ToList(), "drawn over what is known, there being no map");
        g.Exploration.Finish();

        g.Enter(g.Scenario("maze"));
        g.Drop(blind);
        Assert.AreEqual(1, g.Scenarios().Count);
    }

    // A STALL ON A PROBE IS A TOUCH (lab 20-oct-2026: blue, pressed on a wall its contact sensor did not feel, stalled and probed the same cell a
    // thousand times): the body says it could not move on, where it stands — the mark is learned on the nose, the probe backs off and the next
    // one goes elsewhere; on any other route a stall fails it
    [TestMethod]
    public void AStall_OnAProbe_IsATouchOnTheNose_AndFailsAnyOtherRoute()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body);
        g.Enter(new Scenario(Catalog.Blind(4.5, 3.0), new Collisions()));
        g.Wake(new Pose(0.75, 0.75, 0.0));
        g.Exploration.Start();
        var east = g.Exploration.Probe(new Pose(0.75, 0.75, 0.0));
        Assert.AreEqual("advance", east.Order);
        var stalled = g.Stall(new Pose(1.25, 0.75, 0.0), "stalled: no progress for 3 s");
        Assert.AreSame(east, stalled);
        Assert.IsTrue(east.IsPending(), "a touch, not a failure: the probe goes back to where the step began");
        Assert.AreEqual("via@0.75,0.75", east.AsPlan());
        Assert.AreEqual(1, g.Current.Collisions.MarkCount, "the mark on the nose, where the body could not go on");
        while (east.Order is "turnLeft" or "turnRight") Step(east);
        east.Reach(new Pose(0.75, 0.75, Math.PI));
        Assert.IsTrue(east.EndedByTouch);
        Assert.AreEqual(1, g.Exploration.WallCount);
        Assert.AreEqual("unknown@0.75,2.25", g.Exploration.Probe(new Pose(0.75, 0.75, 0.0)).AsPlan(), "the wall learned: the next probe goes north");
        g.Exploration.Stop();
        g.Underway().Abandon("the test leaves");

        var errand = g.Visit(new Pose(0.75, 0.75, 0.0), new Position(0.75, 2.25));
        var failed = g.Stall(new Pose(0.75, 1.5, Math.PI / 2), "stalled: no progress for 3 s");
        Assert.AreSame(errand, failed);
        Assert.AreEqual("failed", errand.Status, "any other route: the world's no");
        Assert.AreEqual("stalled: no progress for 3 s", errand.Why);
        Assert.AreEqual(1, g.Current.Collisions.MarkCount, "no mark: a stall is no touch outside a probe");
    }

    // the body does what the route asks, as the robot would report it: a turn to the heading told, an advance to the point told
    private static void Step(Route route)
    {
        var target = route.Target;
        string order = route.Order;
        if (order == "turnLeft" || order == "turnRight") route.Turn(new Pose(route.Standing.X, route.Standing.Y, target.Heading));
        else if (order == "advance" || order == "back") route.Reach(new Pose(target.X, target.Y, target.Heading));
        else Assert.Fail($"route {route.Id} asks '{order}': nothing for the body to do");
    }

    private static void WalkToTheEnd(Route route)
    {
        for (int step = 0; step < 200 && route.IsPending(); step++) Step(route);
        Assert.IsFalse(route.IsPending(), $"route {route.Id}: too many steps without ending — {route.AsPlan()}");
    }
}
