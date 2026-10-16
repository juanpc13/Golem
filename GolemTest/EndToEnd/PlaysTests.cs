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

    // THE GHOSTS (fase 3, 15-oct-2026): a ghost's copy hears where pacman runs, leaves the den staggered by pacman's runs — the k-th ghost at run
    // 1 + 2k, no word of its own — and chases the cell pacman was last seen running to, one chase at a time, the next decided when it is idle
    [TestMethod]
    public void TheGhosts_HearWherePacmanRuns_LeaveTheDenStaggered_AndChaseTheCellLastSeen()
    {
        var map = Catalog.Maze();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var cast = new Cast("blue", "red,green");
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(map, new Collisions()));
        var pacman = blue.Dynamics.Stage("pacman", cast, "call-1");
        var green = new Golem(body, "green");   // the first ghost of the cast's order (the names sorted)
        green.Enter(new Scenario(map, new Collisions()));
        var greens = green.Dynamics.Stage("pacman", cast, "call-1");
        var red = new Golem(body, "red");       // the second
        red.Enter(new Scenario(map, new Collisions()));
        var reds = red.Dynamics.Stage("pacman", cast, "call-1");
        Assert.AreEqual(0, cast.RankOf(green)); Assert.AreEqual(1, cast.RankOf(red));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => cast.RankOf(blue)).Message, "is pacman");
        Assert.IsTrue(green.Dynamics.Plays("pacman", "call-1"), "the same play, by its title and stamp");
        Assert.IsFalse(green.Dynamics.Plays("pacman", "call-9"));

        // pacman runs from the cradle: its copy keeps the cell the run goes to — what the tell carries
        blue.Dynamics.Run(new Pose(14.25, 8.25, 0.0), pacman);
        Assert.AreEqual(15.75, pacman.TargetX, 1e-9); Assert.AreEqual(8.25, pacman.TargetY, 1e-9);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Dynamics.Chase(new Pose(14.25, 8.25, 0.0), pacman)).Message, "plays pacman");

        // nothing heard yet: no ghost chases
        Assert.IsFalse(greens.HasSeen);
        Assert.IsFalse(green.Dynamics.MayChase);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => green.Dynamics.Chase(new Pose(12.75, 18.75, 0.0), greens)).Message, "not been seen");

        // pacman's first run heard: the first ghost is released, the second waits for run 3
        var den = new Pose(12.75, 18.75, 0.0);   // cell-8-12, in the den
        greens.Seen(new Position(15.75, 8.25), 1);
        reds.Seen(new Position(15.75, 8.25), 1);
        Assert.IsTrue(greens.IsRunning, "a ghost's copy runs with pacman's first word");
        Assert.AreEqual(1, greens.RunsSeen);
        Assert.IsTrue(green.Dynamics.Released); Assert.IsFalse(red.Dynamics.Released);
        Assert.IsTrue(green.Dynamics.MayChase); Assert.IsFalse(red.Dynamics.MayChase);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => red.Dynamics.Chase(new Pose(14.25, 18.75, 0.0), reds)).Message, "still in the den: it leaves at pacman's run 3, 1 heard");

        // the first chase is the way out of the den, found by the planner: north through a door, round to cell-10-5
        var chase = green.Dynamics.Chase(den, greens);
        Assert.AreEqual("pending", chase.Status);
        StringAssert.EndsWith(chase.AsPlan().Trim(), "cell-10-5@15.75,8.25", "to the cell pacman was seen running to");
        Assert.IsTrue(chase.AsPlan().Split('>').Length >= 6, "out of the den by a north door and round the block: the doors open to the north alone — " + chase.AsPlan());
        Assert.AreEqual(1, greens.Chases);
        Assert.IsFalse(green.Dynamics.MayChase, "underway: the next word is kept for the arrival");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => green.Dynamics.Chase(den, greens)).Message, "already chasing");

        // a word heard underway is kept; at the arrival the ghost wants the new cell
        greens.Seen(new Position(15.75, 11.25), 2);
        Assert.IsFalse(green.Dynamics.MayChase);
        WalkToTheEnd(chase);
        Assert.AreEqual("completed", chase.Status);
        Assert.IsTrue(green.Dynamics.MayChase, "arrived, and pacman seen elsewhere since");
        var second = green.Dynamics.Chase(new Pose(15.75, 8.25, 1.5708), greens);
        Assert.AreEqual("cell-10-7@15.75,11.25", second.AsPlan().Trim(), "straight up column 10 after pacman");
        Assert.AreEqual(2, greens.Chases);
        WalkToTheEnd(second);
        Assert.IsFalse(green.Dynamics.MayChase, "pacman not seen anywhere new: the ghost stands and waits for the next word");
        greens.Seen(new Position(15.75, 11.25), 2);
        Assert.IsFalse(green.Dynamics.MayChase, "the same word again changes nothing");
        greens.Seen(new Position(15.75, 8.25), 1);
        Assert.AreEqual(2, greens.RunsSeen, "an older word arrived late is ignored");

        // pacman's third run releases the second ghost
        reds.Seen(new Position(18.75, 11.25), 3);
        Assert.IsTrue(red.Dynamics.Released);
        var redsChase = red.Dynamics.Chase(new Pose(14.25, 18.75, 0.0), reds);
        Assert.AreEqual("pending", redsChase.Status);
        StringAssert.EndsWith(redsChase.AsPlan().Trim(), "cell-12-7@18.75,11.25");
        Assert.AreEqual(1, reds.Chases);
    }

    // THE LAW (fase 4, 15-oct-2026; Juan: "cuando un fantasma toque al golem principal… deben decir que un golem fantasma tocó al golem pacman y
    // terminó el juego"): the golem concludes inside HearBump that a peer's touch landed on its body — met — and, in a play, a ghost meeting
    // pacman ENDS it, on whichever side hears it: the route underway abandoned, the end to be announced once; a peer's word closes the other
    // copies the same way; a ghost meeting a ghost is nothing; the warden may end it too.
    [TestMethod]
    public void AGhostMeetingPacman_EndsThePlay_OnEitherSide_AnnouncedOnce_AndHeardByTheRest()
    {
        var map = Catalog.Maze();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var cast = new Cast("blue", "red,green");
        Assert.AreEqual(Part.Ghost, cast.PartOf("green")); Assert.AreEqual(Part.Pacman, cast.PartOf("blue"));
        Assert.IsTrue(cast.Knows("red")); Assert.IsFalse(cast.Knows("orange"));
        var blue = new Golem(body, "blue"); blue.Enter(new Scenario(map, new Collisions()));
        var green = new Golem(body, "green"); green.Enter(new Scenario(map, new Collisions()));
        var red = new Golem(body, "red"); red.Enter(new Scenario(map, new Collisions()));
        var pacman = blue.Dynamics.Stage("pacman", cast, "call-1");
        var greens = green.Dynamics.Stage("pacman", cast, "call-1");
        var reds = red.Dynamics.Stage("pacman", cast, "call-1");
        Assert.AreEqual("", pacman.Catcher); Assert.IsFalse(pacman.WasCaught); Assert.IsFalse(blue.Dynamics.Concluded);

        // pacman runs, green chases it along row 5; nose to nose in the corridor, green bumps and blue bumps back
        var run = blue.Dynamics.Run(new Pose(14.25, 8.25, 0.0), pacman);
        greens.Seen(new Position(15.75, 8.25), 1);
        var chase = green.Dynamics.Chase(new Pose(18.75, 8.25, 3.1416), greens);   // from cell-12-5, heading west toward pacman
        Assert.IsTrue(chase.IsPending());
        green.Bump(new Pose(16.0, 8.25, 3.1416), 0.0);                              // green's nose pressed at (15.75, 8.25)
        Assert.IsTrue(greens.IsRunning, "a bump alone is a thing, as far as green knows");
        green.HearBump("blue", new Pose(15.5, 8.25, 0.0), 0.0);                     // blue's word: its nose pressed at (15.75, 8.25) — on green's body: they met
        Assert.IsTrue(greens.IsOver, "a ghost met pacman: the play is over in green's copy");
        Assert.AreEqual("green", greens.Catcher, "the ghost that caught pacman — green itself");
        Assert.IsTrue(greens.WasCaught);
        Assert.AreEqual("abandoned", chase.Status, "the chase underway is abandoned: the body stops");
        StringAssert.Contains(chase.Why, "game over: green caught blue");
        Assert.IsTrue(green.Dynamics.Concluded, "concluded here, to be announced by the clock");
        Assert.IsFalse(green.Dynamics.MayChase);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => green.Dynamics.Chase(new Pose(16.0, 8.25, 3.1416), greens)).Message, "is over");
        greens.Announce();
        Assert.IsFalse(green.Dynamics.Concluded, "announced once");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => greens.Announce()).Message, "announced already");

        // blue's side: its own bump and green's word conclude the same — whichever word arrives first, the end is the same
        blue.Bump(new Pose(15.5, 8.25, 0.0), 0.0);
        Assert.IsTrue(pacman.IsRunning);
        blue.HearBump("green", new Pose(16.0, 8.25, 3.1416), 0.0);
        Assert.IsTrue(pacman.IsOver); Assert.AreEqual("green", pacman.Catcher); Assert.IsTrue(pacman.WasCaught);
        Assert.IsFalse(run.IsPending(), "pacman's run abandoned");
        Assert.IsTrue(blue.Dynamics.Concluded, "blue concluded it too: a lost word does not keep the game alive");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Dynamics.Run(new Pose(15.5, 8.25, 0.0), pacman)).Message, "is over");
        Assert.IsFalse(blue.Dynamics.Reached);

        // red, the other ghost, hears the word: its copy closes the same way, nothing to announce; heard again, nothing changes
        reds.Seen(new Position(15.75, 8.25), 1);
        reds.HeardOver("green");
        Assert.IsTrue(reds.IsOver); Assert.AreEqual("green", reds.Catcher); Assert.IsTrue(reds.WasCaught);
        Assert.IsFalse(red.Dynamics.Concluded, "told, not concluded here: nothing to announce");
        reds.HeardOver("red");
        Assert.AreEqual("green", reds.Catcher, "the first word stands");
        reds.Over("warden");
        Assert.AreEqual("green", reds.Catcher, "over already: nothing more");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => reds.Seen(new Position(1.0, 1.0), 9)).Message, "is over");

        // a ghost meeting a ghost is an encounter and nothing more; a golem outside the cast too; and the warden may end the play
        var yellow = new Golem(body, "yellow"); yellow.Enter(new Scenario(map, new Collisions()));
        var cast2 = new Cast("blue", "red,yellow");
        var yellows = yellow.Dynamics.Stage("pacman", cast2, "call-2");
        yellows.Seen(new Position(15.75, 8.25), 1);
        yellow.Bump(new Pose(16.0, 8.25, 3.1416), 0.0);
        yellow.HearBump("red", new Pose(15.5, 8.25, 0.0), 0.0);
        Assert.IsTrue(yellows.IsRunning, "red and yellow, two ghosts, met: the play goes on");
        yellow.Bump(new Pose(16.0, 8.25, 3.1416), 0.0);
        yellow.HearBump("orange", new Pose(15.5, 8.25, 0.0), 0.0);
        Assert.IsTrue(yellows.IsRunning, "orange is in no play");
        yellows.Over("warden");
        Assert.IsTrue(yellows.IsOver); Assert.AreEqual("warden", yellows.Catcher); Assert.IsFalse(yellows.WasCaught, "the warden's end is no catch");
        Assert.IsTrue(yellow.Dynamics.Concluded, "the warden's end is announced like a catch");
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
