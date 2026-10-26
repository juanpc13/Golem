using GolemDomain;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Plays;
using GolemDomain.Robots;
using GolemDomain.Scenarios;
using GolemDomain.Touches;
using GolemDomain.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest.EndToEnd;

// THE TRAFFIC PLAY (propuesta 125, 23-oct-2026; Juan: "3 robots como en una carretera y el semáforo les habla por tell para poder pasar
// uno recto y otro girando a su izquierda o derecha"): the crossing derived from the map, a car's drive with its gate at the stop line, the
// light's law — one at a time, the first that stopped — and the end when every car cleared. Every test instantiates its own objects and goes
// through the golem, as the journal does.
[TestClass]
public class TrafficTests
{
    private static Golem Born(string name, Pose at)
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, name);
        g.Enter(new Scenario(Catalog.CrossCorridors(), new Collisions()));
        g.Wake(at);
        return g;
    }

    [TestMethod]
    public void TheCrossing_IsTheOneZoneOpenOnFourSidesToAisles_WithItsStopLines_FarEnds_AndTheExitOfEveryTurn()
    {
        var map = Catalog.CrossCorridors();
        var crossing = Crossing.In(map);
        Assert.AreEqual("crossing", crossing.Zone.Name);
        CollectionAssert.AreEqual(new[] { Direction.North, Direction.South, Direction.East, Direction.West }, crossing.Arms.ToList());
        Assert.AreEqual("south-aisle", crossing.Arm(Direction.South).Name);
        Assert.AreEqual("west-aisle", crossing.Arm(Direction.West).Name);
        // the stop line: on the arm's axis, 0.75 m from the crossing's edge (the body's nose half a metre clear); the far end, 0.75 m from the far side
        AssertAt(crossing.StopLine(Direction.South), 5.5, 4.0);
        AssertAt(crossing.StopLine(Direction.North), 5.5, 7.0);
        AssertAt(crossing.StopLine(Direction.West), 4.0, 5.5);
        AssertAt(crossing.StopLine(Direction.East), 7.0, 5.5);
        AssertAt(crossing.FarEnd(Direction.South), 5.5, 0.75);
        AssertAt(crossing.FarEnd(Direction.West), 0.75, 5.5);
        AssertAt(crossing.ExitPoint(Direction.West), 4.0, 5.5);
        // where a point stands: an arm, never a room
        Assert.AreEqual(Direction.South, crossing.ArmOf(new Position(5.5, 1.0)));
        Assert.AreEqual(Direction.East, crossing.ArmOf(new Position(9.0, 5.5)));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => crossing.ArmOf(new Position(2.0, 2.0))).Message, "stands on no arm");
        // the exit, in a driver's words: coming from the south one travels north — left is the west arm, right the east one, straight on the north
        Assert.AreEqual(Direction.North, Crossing.Exit(Direction.South, Turn.Straight));
        Assert.AreEqual(Direction.West, Crossing.Exit(Direction.South, Turn.Left));
        Assert.AreEqual(Direction.East, Crossing.Exit(Direction.South, Turn.Right));
        Assert.AreEqual(Direction.East, Crossing.Exit(Direction.North, Turn.Left), "coming from the north one travels south: its left is the east");
        Assert.AreEqual(Direction.South, Crossing.Exit(Direction.East, Turn.Left), "coming from the east one travels west: its left is the south");
        Assert.AreEqual(Direction.South, Crossing.Exit(Direction.West, Turn.Right), "coming from the west one travels east: its right is the south");
        // a map with no crossing
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => Crossing.In(Catalog.Warehouse())).Message, "has no crossing");
    }

    [TestMethod]
    public void AJourney_IsACarAndItsTurn_ReadFromText_AndTheCastAndTheJourneysMustAgree()
    {
        var journeys = new Journeys("Red:straight, blue:LEFT,green:right");
        Assert.AreEqual(3, journeys.Count);
        Assert.AreEqual("blue,green,red", journeys.Roster, "the cars sorted, like a cast's");
        Assert.AreEqual("blue:left,green:right,red:straight", journeys.Text);
        Assert.AreEqual(Turn.Left, journeys.Of("blue"));
        Assert.IsFalse(journeys.Knows("yellow"));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Journeys("red:back")).Message, "goes straight, left or right");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Journeys("red")).Message, "a car and its turn");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Journeys("red:left,red:right")).Message, "two journeys");
        // the play staged: the light leads the cast, the cars follow; every car has a journey and every journey a car
        var orange = Born("orange", new Pose(2.0, 2.0, 0.0));
        var cast = new Cast("orange", "blue,red,green");
        var play = (Traffic)orange.Dynamics.Stage("road-1", "traffic", cast, journeys, Part.Light, "call-1");
        Assert.AreEqual("traffic", play.Kind);
        Assert.AreEqual("orange", play.Light);
        CollectionAssert.AreEqual(new[] { "blue", "green", "red" }, play.Cars.ToList());
        Assert.AreEqual(Part.Light, orange.Dynamics.Me, "the lead of a traffic cast is the light, not pacman");
        Assert.IsTrue(orange.Dynamics.PlaysLight); Assert.IsFalse(orange.Dynamics.PlaysCar); Assert.IsFalse(orange.Dynamics.PlaysPacman);
        Assert.AreSame(play, orange.Dynamics.Traffic);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => orange.Dynamics.Pacman).Message, "is traffic, not pacman");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => orange.Dynamics.Stage("road-2", "traffic", cast, new Journeys("blue:left,red:right"), Part.Light, "call-2")).Message, "the car 'green' has no journey");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => orange.Dynamics.Stage("road-2", "traffic", cast, new Journeys("blue:left,red:right,green:left,yellow:left"), Part.Light, "call-2")).Message, "'yellow' has a journey but is no car");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => orange.Dynamics.Stage("road-2", "traffic", cast, journeys, Part.Car, "call-2")).Message, "'orange' is light in the cast, not car");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => orange.Dynamics.Stage("road-2", "traffic", cast, Part.Light, "call-2")).Message, "needs the journeys");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => orange.Dynamics.Stage("road-2", "pacman", cast, journeys, Part.Pacman, "call-2")).Message, "has no journeys");
    }

    [TestMethod]
    public void ACar_DrivesToItsStopLine_WaitsAtTheGate_GoesOnTheLightsWord_AndClearsTheCrossingByItsTurn()
    {
        // blue sets out from the south arm facing north, turning LEFT at the crossing: its stops are the south stop line (the gate), the west
        // exit point and the west far end — one route, decided whole
        var blue = Born("blue", new Pose(5.5, 1.0, Math.PI / 2));
        var cast = new Cast("orange", "blue,red");
        var play = (Traffic)blue.Dynamics.Stage("road-1", "traffic", cast, new Journeys("blue:left,red:straight"), Part.Car, "call-1");
        Assert.AreEqual(Part.Car, blue.Dynamics.Me);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Dynamics.Drive(blue.Destination, play)).Message, "start it first");
        play.Start();
        var route = blue.Dynamics.Drive(blue.Destination, play);
        Assert.AreSame(route, play.Route); Assert.IsTrue(route.HasGate);
        Assert.AreEqual(Direction.South, play.Arm); Assert.AreEqual(Direction.West, play.ExitArm);
        Assert.AreEqual(4, route.Stops.Count, "the line, the turn point, the exit point, the far end");
        AssertAt(route.Stops[0], 5.5, 4.0); AssertAt(route.Stops[1], 5.5, 5.5); AssertAt(route.Stops[2], 4.0, 5.5); AssertAt(route.Stops[3], 0.75, 5.5);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Dynamics.Drive(blue.Destination, play)).Message, "is driving already");
        // it walks to its line and STANDS: the gate — the route waits, the order is stop, and the car has its word to say
        WalkUntilItWaits(route);
        Assert.IsTrue(route.Waiting); Assert.AreEqual("stop", route.Order); Assert.IsFalse(route.IsWalkable);
        AssertAt(route.Standing, 5.5, 4.0);
        Assert.IsTrue(blue.Dynamics.AtTheLine); Assert.IsFalse(play.IsStopped);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Dynamics.Go(play)).Message, "has no green: the crossing is free");
        blue.Dynamics.Stopped(route.Standing, play);
        Assert.IsTrue(play.IsStopped); Assert.IsFalse(blue.Dynamics.AtTheLine, "said once");
        Assert.AreEqual("blue", play.QueueText, "its own word is in its own copy's queue too");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Dynamics.Stopped(route.Standing, play)).Message, "said it stopped already");
        // TOUCHED WHILE STANDING AT THE LINE (lab 23-oct-2026: blue, touched there, crossed with no green): it backs off and WAITS AT THE LINE AGAIN
        blue.Bump(route.Standing, 0.0);
        Assert.IsFalse(route.Waiting); Assert.AreEqual("back", route.Order, "the correction first");
        Assert.AreEqual(1, blue.Current.Collisions.MarkCount, "a thing marked where it was touched — on the line itself");
        // …the thing is gone (the warden's `forget`): the way to the line is clear again. (A car that touched it would be a PEER MET, whose berth
        // covers the line while the route lasts — the yield rule still pending; the light's law keeps cars out of each other's way instead.)
        blue.Forget(new Position(5.5, 4.25));
        Assert.AreEqual(0, blue.Current.Collisions.MarkCount, "forgotten");
        WalkUntilItWaits(route);
        Assert.IsTrue(route.Waiting); AssertAt(route.Standing, 5.5, 4.0); Assert.AreEqual(3, route.StopsLeft, "the gate's stop reached again; the turn point, the exit and the far end ahead");
        Assert.IsFalse(blue.Dynamics.AtTheLine, "it said it stopped already: one word");
        // the light's word: red first — the gate stays shut; then blue — it goes
        play.Granted("red");
        Assert.IsFalse(blue.Dynamics.HasGreen); Assert.AreEqual("red", play.Holder);
        play.Freed("red");
        Assert.IsTrue(play.IsFree); Assert.IsFalse(play.IsDone);
        play.Granted("blue");
        Assert.IsTrue(blue.Dynamics.HasGreen);
        var going = blue.Dynamics.Go(play);
        Assert.AreEqual("abandoned", route.Status, "the route that waited at the line is left");
        Assert.AreSame(going, play.Route); Assert.IsFalse(going.Waiting); Assert.IsTrue(going.IsPending()); Assert.IsFalse(going.HasGate);
        Assert.AreEqual(3, going.Stops.Count, "the way through: the turn point (one lane: the centre), the exit point and the parking place, decided at the green");
        AssertAt(going.Stops[0], 5.5, 5.5); AssertAt(going.Stops[1], 4.0, 5.5); AssertAt(going.Stops[2], 0.75, 5.5);
        route = going;
        Assert.IsFalse(blue.Dynamics.PastTheCrossing, "not yet: the exit point is ahead");
        // through the crossing to the west exit point: past the crossing, its second word; then on to the far end: the journey complete
        WalkUntil(route, r => r.StopsLeft <= 1);
        Assert.IsTrue(blue.Dynamics.PastTheCrossing);
        blue.Dynamics.Cleared(route.Standing, play);
        Assert.IsTrue(play.ClearedHere); Assert.IsFalse(blue.Dynamics.PastTheCrossing, "said once");
        Assert.AreEqual("blue,red", play.ClearedText, "red cleared before, by its word"); Assert.IsTrue(play.IsFree);
        Assert.IsTrue(route.IsPending(), "the far end is still ahead: the drive goes on");
        WalkUntil(route, r => !r.IsPending());
        Assert.AreEqual("completed", route.Status);
        AssertAt(route.Standing, 0.75, 5.5);
        Assert.IsTrue(play.IsDone, "red cleared by its word and blue by its own: every car through"); Assert.IsTrue(play.IsOver, "concluded in this copy too, like a catch on either side");
    }

    [TestMethod]
    public void TheLight_GrantsTheCrossingOneAtATime_InTheOrderItHeardTheCarsStop_AndThePlayEndsWhenEveryCarCleared()
    {
        var orange = Born("orange", new Pose(2.0, 2.0, 0.0));
        var cast = new Cast("orange", "blue,red,green");
        var play = (Traffic)orange.Dynamics.Stage("road-1", "traffic", cast, new Journeys("blue:left,red:straight,green:right"), Part.Light, "call-1");
        Assert.IsFalse(orange.Dynamics.MayGrant, "staged, not running");
        play.Start();
        Assert.IsFalse(orange.Dynamics.MayGrant, "nobody at a line yet");
        // the words come in the light's own order: red (from the north, going straight) first, then blue (from the south, turning left) — the law
        // is the journal's order, not the names'; BUT THE ROAD HAS ONE LANE (lab 23-oct-2026): red would leave by the south arm, where blue waits
        // at its line, so red is BLOCKED and blue, whose west arm is free, passes first
        play.Queued("red", Direction.North);
        Assert.IsTrue(orange.Dynamics.MayGrant, "red alone: the south arm is free");
        play.Queued("blue", Direction.South);
        Assert.AreEqual("red,blue", play.QueueText);
        Assert.AreEqual(Direction.South, play.ExitArmOf("red")); Assert.AreEqual(Direction.West, play.ExitArmOf("blue"));
        Assert.IsTrue(play.IsBlocked("red"), "blue waits at the line of the arm red leaves by"); Assert.IsFalse(play.IsBlocked("blue"));
        Assert.AreEqual("red", play.BlockedText);
        Assert.IsTrue(orange.Dynamics.MayGrant);
        var permit = orange.Dynamics.Grant(play);
        Assert.AreEqual("blue", permit.To, "the first that stopped whose exit arm is free"); Assert.AreEqual(1, permit.Number); Assert.AreEqual("road-1", permit.Play);
        Assert.AreEqual("blue", play.Holder); Assert.AreEqual("red", play.QueueText);
        Assert.IsFalse(orange.Dynamics.MayGrant, "the crossing is blue's until it clears");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => orange.Dynamics.Grant(play)).Message, "is blue's until it clears");
        play.Queued("green", Direction.East);
        play.Queued("blue", Direction.South);
        Assert.AreEqual("red,green", play.QueueText, "a word heard twice is heard once; the holder is not queued again");
        Assert.IsTrue(play.IsBlocked("green"), "green leaves by the north arm, where red waits");
        // blue clears into the west arm: red's way south is free now, green's way north still is not; red goes, then green; the third
        // clearing ends the play — concluded here, the clock announces it
        play.Freed("blue");
        Assert.IsTrue(play.IsFree); Assert.AreEqual(1, play.ClearedCount);
        Assert.IsFalse(play.IsBlocked("red")); Assert.IsTrue(play.IsBlocked("green"));
        Assert.AreEqual("red", orange.Dynamics.Grant(play).To);
        Assert.IsFalse(orange.Dynamics.MayGrant);
        play.Freed("red");
        Assert.IsFalse(play.IsBlocked("green"), "red left the north arm's line and parked in the south arm");
        Assert.AreEqual("green", orange.Dynamics.Grant(play).To);
        Assert.AreEqual(3, play.Permits);
        Assert.IsFalse(play.IsOver);
        // two cars that would park in the same arm: the second is blocked while the first stands at that far end
        var yellowCast = new Cast("orange", "blue,red");
        var jam = (Traffic)orange.Dynamics.Stage("road-2", "traffic", yellowCast, new Journeys("blue:straight,red:right"), Part.Light, "call-2");
        jam.Start();
        jam.Queued("blue", Direction.South); jam.Queued("red", Direction.East);
        Assert.AreEqual(Direction.North, jam.ExitArmOf("blue")); Assert.AreEqual(Direction.North, jam.ExitArmOf("red"), "from the east one travels west: its right is the north");
        Assert.AreEqual("blue", orange.Dynamics.Grant(jam).To);
        jam.Freed("blue");
        Assert.IsTrue(jam.IsBlocked("red"), "blue stands at the north arm's far end: red would park on it"); Assert.IsFalse(orange.Dynamics.MayGrant);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => jam.Grant()).Message, "every car waiting is blocked");
        jam.Over("warden");
        orange.Dynamics.Forget(jam);
        var road = (Traffic)orange.Dynamics.Stage("road-1", "traffic", cast, new Journeys("blue:left,red:straight,green:right"), Part.Light, "call-3");
        Assert.AreSame(road, orange.Dynamics.Traffic);
        play = (Traffic)orange.Dynamics.Find("road-1");
        play.Start();
        play.Queued("red", Direction.North); play.Queued("blue", Direction.South); play.Queued("green", Direction.East);
        Assert.AreEqual("blue", orange.Dynamics.Grant(play).To); play.Freed("blue");
        Assert.AreEqual("red", orange.Dynamics.Grant(play).To); play.Freed("red");
        Assert.AreEqual("green", orange.Dynamics.Grant(play).To);
        Assert.IsFalse(play.IsOver);
        play.Freed("green");
        Assert.IsTrue(play.IsDone); Assert.IsTrue(play.IsOver); Assert.AreEqual("crossing", play.Catcher); Assert.IsFalse(play.WasCaught);
        Assert.IsTrue(orange.Dynamics.Concluded, "the end concluded here, to announce");
        orange.Dynamics.Concluding.Announce();
        Assert.IsFalse(orange.Dynamics.Concluded);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => play.Grant()).Message, "is not running");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => play.Queued("yellow", Direction.North)).Message, "is no car of the play");
        // a car that hears the end while still driving to its far end FINISHES its journey: the end of a traffic play abandons nothing
        var green = Born("green", new Pose(9.0, 5.5, Math.PI));
        var copy = (Traffic)green.Dynamics.Stage("road-1", "traffic", cast, new Journeys("blue:left,red:straight,green:right"), Part.Car, "call-1");
        copy.Start();
        var drive = green.Dynamics.Drive(green.Destination, copy);
        Assert.AreEqual(Direction.East, copy.Arm); Assert.AreEqual(Direction.North, copy.ExitArm, "coming from the east one travels west: its right is the north");
        copy.HeardOver("crossing");
        Assert.IsTrue(copy.IsOver); Assert.IsTrue(drive.IsPending(), "the drive goes on to its far end");
    }

    // THE ROAD OF TWO LANES AND THE QUEUE (§12, 23-oct-2026; Juan: "debería ser como una cola de carros queriendo pasar… y de doble carril")
    [TestMethod]
    public void TheRoad_HasTwoLanesPerArm_TheQueuesPlacesOnTheInboundLane_AndTheParkingPlacesOnTheOutboundOne()
    {
        var road = Crossing.In(Catalog.Road());
        Assert.AreEqual("junction", road.Zone.Name, "found by its shape, whatever its name");
        Assert.AreEqual(2, road.Lanes(Direction.South)); Assert.AreEqual(0.75, road.LaneOffset(Direction.South), 1e-9);
        Assert.AreEqual(1, Crossing.In(Catalog.CrossCorridors()).Lanes(Direction.South), "an aisle of 1.5 m: one lane, on the axis");
        // coming from the south one travels north: the inbound lane is on its right, the east; the line 0.75 m from the crossing, the queue a pitch each
        AssertAt(road.StopLine(Direction.South), 8.25, 5.25);
        AssertAt(road.StopLine(Direction.South, 1), 8.25, 4.25);
        AssertAt(road.StopLine(Direction.South, 2), 8.25, 3.25);
        AssertAt(road.StopLine(Direction.North), 6.75, 9.75, "coming from the north one travels south: its right is the west");
        AssertAt(road.StopLine(Direction.East), 9.75, 8.25, "coming from the east one travels west: its right is the north");
        AssertAt(road.StopLine(Direction.West), 5.25, 6.75);
        // leaving by the south one travels south: the outbound lane is on its right, the west; the exit point 0.75 m in; the parking places from the far end back
        AssertAt(road.ExitPoint(Direction.South), 6.75, 5.25);
        AssertAt(road.FarEnd(Direction.South), 6.75, 0.75);
        AssertAt(road.FarEnd(Direction.South, 1), 6.75, 1.75);
        AssertAt(road.ExitPoint(Direction.North), 8.25, 9.75);
        AssertAt(road.FarEnd(Direction.North), 8.25, 14.25);
        Assert.AreEqual(5, road.SlotsOf(Direction.South), "six metres less the line and the far end, a pitch each");
        // THE TURN POINT (lab 23-oct-2026: orange cut the inside corner): where the inbound lane meets the outbound lane of the arm it leaves by
        AssertAt(road.TurnPoint(Direction.South, Direction.West), 8.25, 8.25, "from the south turning left: up its lane to the west arm's outbound lane");
        AssertAt(road.TurnPoint(Direction.North, Direction.West), 6.75, 8.25, "from the north turning right: a lane's width from both walls of the corner");
        AssertAt(road.TurnPoint(Direction.East, Direction.South), 6.75, 8.25, "from the east turning left");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => road.TurnPoint(Direction.South, Direction.North)).Message, "straight on");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => road.StopLine(Direction.South, -1)).Message, "0 or more");
    }

    [TestMethod]
    public void AQueue_IsTheCarsOfAnArmByName_EachAtItsPlace_AndItAdvancesWhenTheOneAheadGetsTheGreen()
    {
        var journeys = new Journeys("red:south:straight, blue:SOUTH:left, green:north:right");
        Assert.IsTrue(journeys.KnowArms);
        Assert.AreEqual("blue:south:left,green:north:right,red:south:straight", journeys.Text);
        Assert.AreEqual(Direction.South, journeys.ArmOf("red"));
        CollectionAssert.AreEqual(new[] { "blue", "red" }, journeys.OnArm(Direction.South).ToList(), "the south queue, by name: blue first");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Journeys("red:south:straight,blue:left")).Message, "every journey names the arm");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Journeys("red:up:straight")).Message, "comes from the north, south, east or west arm");
        // red is SECOND on the south arm: its drive ends at the second place, a pitch behind the line, and its word is not the line's
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var red = new Golem(body, "red");
        red.Enter(new Scenario(Catalog.Road(), new Collisions()));
        red.Wake(new Pose(8.25, 2.0, Math.PI / 2));
        var cast = new Cast("orange", "blue,red,green");
        var play = (Traffic)red.Dynamics.Stage("road-2", "traffic", cast, journeys, Part.Car, "call-1");
        play.Start();
        Assert.AreEqual(1, play.SlotOf("red")); Assert.AreEqual(0, play.SlotOf("blue")); Assert.AreEqual(0, play.SlotOf("green"));
        Assert.AreEqual(0, play.ParkingSlotOf("red"), "the only one leaving by the north arm");
        var route = red.Dynamics.Drive(red.Destination, play);
        Assert.AreEqual(1, play.Slot); Assert.IsFalse(play.IsFirst);
        AssertAt(route.Stops[0], 8.25, 4.25); AssertAt(route.Stops[1], 8.25, 9.75); AssertAt(route.Stops[2], 8.25, 14.25);
        Assert.AreEqual(3, route.Stops.Count, "straight on: no turn point");
        // SECOND IN THE QUEUE: it lines up and WAITS TO SET OUT until blue, ahead, says it stopped at its place (lab 23-oct-2026: red, facing the
        // junction already, drove into orange still turning at the place ahead)
        WalkUntilItWaits(route);
        Assert.IsTrue(route.WaitsToSetOut); Assert.IsFalse(route.WaitsAtGate); AssertAt(route.Standing, 8.25, 2.0);
        Assert.IsTrue(play.WaitsToSetOut); Assert.IsFalse(play.AtItsPlace); Assert.IsFalse(red.Dynamics.MaySetOut, "blue said nothing yet");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => red.Dynamics.Stopped(route.Standing, play)).Message, "waits to set out");
        Assert.AreEqual("blue", play.Ahead("red")); Assert.AreEqual("", play.Ahead("blue"));
        play.Queued("blue", Direction.South, 0);
        Assert.IsTrue(red.Dynamics.MaySetOut);
        var setOut = red.Dynamics.SetOut(play);
        Assert.AreSame(route, setOut); Assert.IsFalse(route.Waiting); Assert.IsTrue(route.HasGate, "the gate at its place stays ahead");
        WalkUntilItWaits(route);
        Assert.IsTrue(route.WaitsAtGate); AssertAt(route.Standing, 8.25, 4.25);
        Assert.IsTrue(play.AtItsPlace); Assert.IsFalse(red.Dynamics.AtTheLine, "second in the queue: its word is for the one behind, not the light");
        red.Dynamics.Stopped(route.Standing, play);
        Assert.IsTrue(play.IsStopped); Assert.AreEqual("blue", play.QueueText, "blue's word at the line is in every copy's queue; red's from the second place joins none");
        Assert.IsFalse(red.Dynamics.MayAdvance, "blue is still ahead");
        // the light gives blue the green (its word, in red's copy): blue is still on the arm, turning to leave — red stays (lab 23-oct-2026: advancing
        // at the green ran red into blue); once blue CLEARED the crossing the queue moves — red advances to the line, its old route abandoned
        play.Granted("blue");
        Assert.AreEqual(1, play.SlotOf("red")); Assert.IsFalse(red.Dynamics.MayAdvance, "blue has the green but is still on the arm");
        play.Freed("blue");
        Assert.AreEqual(0, play.SlotOf("red")); Assert.IsTrue(red.Dynamics.MayAdvance);
        var advanced = red.Dynamics.Advance(route.Standing, play);
        Assert.AreEqual("abandoned", route.Status); Assert.AreSame(advanced, play.Route); Assert.IsTrue(play.IsFirst);
        AssertAt(advanced.Stops[0], 8.25, 5.25);
        Assert.IsFalse(red.Dynamics.MayAdvance);
        Assert.IsFalse(advanced.WaitsToSetOut, "advancing, nobody ahead: no start together");
        WalkUntilItWaits(advanced);
        AssertAt(advanced.Standing, 8.25, 5.25);
        Assert.IsTrue(red.Dynamics.AtTheLine, "first now: its word to the light");
        red.Dynamics.Stopped(advanced.Standing, play);
        Assert.AreEqual("red", play.QueueText);
        // the journey must agree with where the car stands
        var green = new Golem(body, "green");
        green.Enter(new Scenario(Catalog.Road(), new Collisions()));
        green.Wake(new Pose(8.25, 2.0, Math.PI / 2));
        var copy = (Traffic)green.Dynamics.Stage("road-2", "traffic", cast, journeys, Part.Car, "call-1");
        copy.Start();
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => green.Dynamics.Drive(green.Destination, copy)).Message, "comes from the north arm by its journey but stands on the south one");
        // on a two-lane road nobody is blocked by an exit arm: the light's copy grants in the order it heard
        var orange = new Golem(body, "orange");
        orange.Enter(new Scenario(Catalog.Road(), new Collisions()));
        orange.Wake(new Pose(2.0, 2.0, 0.0));
        var light = (Traffic)orange.Dynamics.Stage("road-2", "traffic", cast, journeys, Part.Light, "call-1");
        light.Start();
        light.Queued("green", Direction.North); light.Queued("blue", Direction.South);
        Assert.IsFalse(light.IsBlocked("green"), "two lanes: the car it would meet is on the other lane");
        Assert.AreEqual("green", orange.Dynamics.Grant(light).To);
        // THE PARKING FOLLOWS THE GREENS (lab 23-oct-2026: by name, a car granted later drove into one parked ahead): green and blue both leave by
        // the west arm — green, granted first, parks farthest; blue, granted after it, a pitch short
        Assert.AreEqual(Direction.West, Crossing.Exit(Direction.North, Turn.Right)); Assert.AreEqual(Direction.West, Crossing.Exit(Direction.South, Turn.Left));
        Assert.AreEqual(0, light.ParkingSlotOf("green")); Assert.AreEqual(1, light.ParkingSlotOf("blue"), "green, granted, goes into the west arm ahead of it");
        light.Freed("green");
        Assert.AreEqual("blue", orange.Dynamics.Grant(light).To);
        Assert.AreEqual(1, light.ParkingSlotOf("blue"), "green went into the west arm before it"); Assert.AreEqual(0, light.ParkingSlotOf("green"));
    }

    private static void AssertAt(Position at, double x, double y, string why = "")
    {
        Assert.AreEqual(x, at.X, 1e-6, "x " + why); Assert.AreEqual(y, at.Y, 1e-6, "y " + why);
    }

    private static void WalkUntilItWaits(GolemDomain.Routes.Route route) => WalkUntil(route, r => r.Waiting);

    private static void WalkUntil(GolemDomain.Routes.Route route, Func<GolemDomain.Routes.Route, bool> done)
    {
        for (int i = 0; i < 60 && route.IsPending() && !done(route); i++)
        {
            if (route.Waiting) Assert.Fail("the route waits at its gate: nothing to walk until the light's word");
            route.Arrive(route.NextLeg);
        }
        Assert.IsTrue(done(route), $"the walk did not get there: {route.Status}, {route.AsPlan()}");
    }
}
