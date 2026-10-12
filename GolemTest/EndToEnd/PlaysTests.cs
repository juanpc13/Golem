using GolemDomain;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Plays;
using GolemDomain.Robots;
using GolemDomain.Scenarios;
using GolemDomain.Touches;
using GolemDomain.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// THE PLAYS ARE THE GOLEM'S (propuesta 108, fase 2, 12-oct-2026; Juan: "que tome el tramo de arriba/abajo/izquierda/derecha cuando entre en una
// intersección, si no seguir; en un camino sin salida gira siempre a la derecha"): the module keeps the play it was told — its cast, its part in
// it — and PACMAN'S RUNS are decided on the maze: straight down the corridor to the next decision cell, where the rule picks the way on. Read
// where the journal reads — the play's own properties and the route the golem opened.
[TestClass]
public class PlaysTests
{
    [TestMethod]
    public void APlayStaged_IsKeptWithItsCast_AndEveryGolemKnowsItsPart()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.Maze(), new Collisions()));
        var cast = new Cast("Blue", "red,green,yellow");
        Assert.AreEqual("blue", cast.Pacman);
        Assert.AreEqual("green,red,yellow", cast.Roster, "the ghosts' names sorted: every copy computes the same");
        Assert.AreEqual(4, cast.Count);

        Assert.IsFalse(blue.Dynamics.Knows, "no play told yet");
        var play = blue.Dynamics.Stage("Pacman", cast, "call-1");
        Assert.AreEqual("pacman", play.Name);
        Assert.AreEqual("call-1", play.Stamp, "the once of its call, the host's");
        Assert.AreEqual(PlayState.Staged, play.State);
        Assert.AreEqual(Part.Pacman, blue.Dynamics.Me, "blue is pacman, by the name it was born with");
        Assert.AreSame(play, blue.Dynamics.Current);
        Assert.AreSame(play, blue.Dynamics.Find("pacman"));
        Assert.AreEqual(Direction.East, play.Heading, "pacman heads east in the cradle");
        Assert.IsFalse(play.HasWish);

        var red = new Golem(body, "red");
        red.Enter(new Scenario(Catalog.Maze(), new Collisions()));
        Assert.AreEqual(Part.Ghost, red.Dynamics.Stage("pacman", cast, "call-1").Cast.PartOf(red));
        var orange = new Golem(body, "orange");
        orange.Enter(new Scenario(Catalog.Maze(), new Collisions()));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => orange.Dynamics.Stage("pacman", cast, "call-1")).Message, "'orange' is not in the cast");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Dynamics.Stage("chess", cast, "call-2")).Message, "the only play known is pacman");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Cast("blue", "red,blue")).Message, "cannot be pacman and a ghost at once");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Dynamics.Find("tetris")).Message, "no play named 'tetris'");
    }

    [TestMethod]
    public void PacmanRuns_StraightToTheNextDecisionCell_AndTheRuleTurnsIt_WishStraightRightLeftBack()
    {
        var map = Catalog.Maze();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(map, new Collisions()));
        var play = blue.Dynamics.Stage("pacman", new Cast("blue", "red"), "call-1");

        // released in the cradle (cell-9-5) heading east, no wish: straight on to cell-10-5 — the first intersection, three sides open
        var cradle = map.Find("cell-9-5") as Zone;
        Assert.AreEqual(Direction.East, play.Decide(map, cradle));
        var (first, heading) = play.NextRun(map, cradle);
        Assert.AreEqual("cell-10-5", first.Name);
        Assert.AreEqual(Direction.East, heading);
        Assert.AreEqual(3, map.Neighbours(first).Count, "an intersection: west, east and north open, the south closed");

        // the golem opens the route of the run: one square advance down the corridor, ending at the decision cell's middle
        var route = blue.Dynamics.Run(new Pose(14.25, 8.25, 0.0), play);
        Assert.AreEqual("pending", route.Status);
        Assert.AreEqual("cell-10-5@15.75,8.25", route.AsPlan().Trim(), "the run: to the middle of the decision cell, nothing in between");
        Assert.IsTrue(play.IsRunning);
        Assert.AreEqual(1, play.Runs);
        Assert.IsFalse(blue.Dynamics.Reached, "underway: not yet");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Dynamics.Run(new Pose(14.25, 8.25, 0.0), play)).Message, "already running");
        WalkToTheEnd(route);
        Assert.AreEqual("completed", route.Status);
        Assert.IsTrue(blue.Dynamics.Reached, "the run reached its decision cell: the next is to be decided");

        // the keyboard wishes north: open at cell-10-5, so the next run turns north up column 10 to cell-10-7 (where the east opens: three sides)
        play.Head(Direction.North);
        Assert.IsTrue(play.HasWish);
        var second = blue.Dynamics.Run(new Pose(15.75, 8.25, 0.0), play);
        Assert.AreEqual("cell-10-7@15.75,11.25", second.AsPlan().Trim(), "north, two cells, to the next intersection");
        Assert.AreEqual(Direction.North, play.Heading);
        Assert.IsFalse(play.HasWish, "the wish taken is consumed");
        Assert.AreEqual(2, play.Runs);
        WalkToTheEnd(second);

        // a wish that is closed at the decision cell is kept and pacman goes straight on; it is taken at the next cell where it opens
        var atTen7 = map.Find("cell-10-7") as Zone;
        play.Head(Direction.West);
        Assert.AreEqual(Direction.North, play.Decide(map, atTen7), "west is closed at cell-10-7 (cell-9-7 is a wall): straight on, north");
        Assert.IsTrue(play.HasWish, "kept for the next decision");

        // the rule at a corner, without a wish: straight closed → the right of the heading; right closed too → the left; all closed → back
        var fresh = new Golem(body, "blue");
        fresh.Enter(new Scenario(map, new Collisions()));
        var corner = map.Find("cell-1-15") as Zone;   // the north-west corner of the lower-left loop: east and north open, west and south walls
        Assert.AreEqual(2, map.Neighbours(corner).Count);
        var westward = fresh.Dynamics.Stage("pacman", new Cast("blue", "red"), "call-2");
        westward.Head(Direction.West);   // heading east at birth; a wish west, taken where it is open…
        var runWest = fresh.Dynamics.Run(new Pose(5.25, 23.25, 0.0), westward);   // from cell-3-15, west: cell-2-15, then cell-1-15 where straight closes
        Assert.AreEqual("cell-1-15@2.25,23.25", runWest.AsPlan().Trim());
        Assert.AreEqual(Direction.West, westward.Heading);
        Assert.AreEqual(Direction.North, westward.Decide(map, corner), "heading west at the corner: straight closed, the right of west is north, open");
        Assert.IsTrue(Pacman.IsDecision(map, corner, Direction.West));
        Assert.AreEqual(Direction.East, Pacman.Left(Direction.South));
        Assert.AreEqual(Direction.West, Pacman.Back(Direction.East));
        Assert.AreEqual(Direction.West, Pacman.Right(Direction.South));
    }

    private static void WalkToTheEnd(GolemDomain.Routes.Route route)
    {
        for (int i = 0; i < 50 && route.IsPending(); i++)
        {
            if (route.Waiting) route.Go();
            route.Arrive(route.NextLeg);
        }
    }
}
