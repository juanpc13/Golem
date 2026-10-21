using GolemDomain;
using GolemDomain.Formations;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Robots;
using GolemDomain.Scenarios;
using GolemDomain.Touches;
using GolemDomain.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// THE FORMATION (propuesta 104; the convocation of ajuste 73 with a name — PLAN-coreografia-disputa §13, §14): every golem its own copy of the call, fed by the same
// words; each sets out to the best place it knows, and a nearer peer's word interrupts its route and sends it to the next — with an
// order of the route in the body, the route YIELDS first: it asks the body to stop, and the next route opens from where the body says it
// stood (propuesta 74). A square of side 2 around (5.5, 5.5) for a fleet of two: the north-east corner (6.5, 6.5) and the south-west one
// (4.5, 4.5), half a perimeter apart.
[TestClass]
public class FormationTests
{
    [TestMethod]
    public void TheRoundOpensTheRoutes_WithTheSameTable_AndACorrectedWord_InterruptsMyRoute_WhileTheNearerOneKeepsIts()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var red = new Golem(body, "red");
        red.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,red");
        var center = new Position(5.5, 5.5);
        var side = new Meters(2.0);
        var blueCall = blue.Choreography.Form("red-1", blue.Choreography.Figure("square", center, side), fleet, Policy.Distance);
        var redCall = red.Choreography.Form("red-1", red.Choreography.Figure("square", center, side), fleet, Policy.Distance);
        Assert.AreEqual(Policy.Distance, blueCall.Policy);

        // each says where it stands: nobody sets out until the round is complete (ajuste 80)
        Assert.IsNull(blueCall.Convene(new Pose(5.8, 5.8, 0.0), fleet.Member(blue)), "blue spoke; red has not: no route yet");
        Assert.IsFalse(blueCall.IsComplete);
        Assert.IsNull(blueCall.Route);
        Assert.AreEqual(0, blue.Routes().Count, "nothing minted while the fleet speaks");
        Assert.IsNull(redCall.Convene(new Pose(7.0, 7.0, 0.0), fleet.Member(red)));

        // the words cross and complete each round: the routes open with the SAME table — red nearer to the north-east corner, blue to the next
        // nearest of the four (ajuste 86: the places are the corners, two stay free): north-west and south-east tie, the order breaks it
        var blueFirst = blueCall.Stood(fleet.Member(red), new Position(7.0, 7.0));
        Assert.IsNotNull(blueFirst, "red's word completes blue's round: blue's route opens");
        Assert.IsTrue(blueCall.IsComplete);
        StringAssert.Contains(blueFirst.AsPlan(), "floor@4.5,6.5", "red stands nearer to the north-east corner: blue takes the north-west one — " + blueFirst.AsPlan());
        var redRoute = redCall.Stood(fleet.Member(blue), new Position(5.8, 5.8));
        StringAssert.Contains(redRoute.AsPlan(), "floor@6.5,6.5", "and red the north-east one");
        Assert.AreEqual(0.0, blueCall.PlaceOf(fleet.Member(red)).DistanceTo(redCall.PlaceOf(fleet.Member(red))), 1e-9, "the two copies, the same table");
        Assert.AreEqual(0.0, blueCall.PlaceOf(fleet.Member(blue)).DistanceTo(redCall.PlaceOf(fleet.Member(blue))), 1e-9);
        Assert.AreEqual(2, blueFirst.Company.Count, "red, where it stood and where it goes, is the COMPANY blue's route plans around while it lasts (ajuste 80; propuesta 107)");
        Assert.AreEqual(0, blue.Current.Collisions.PeersInTheWay().Count, "nobody touched red: no peer met, nothing among the obstacles (propuesta 107)");

        // a CORRECTED word — red says it stands by the south-west corner after all — while blue's body carries an order of its route
        var blueYields = blueCall.Stood(fleet.Member(red), new Position(4.4, 4.4));
        Assert.AreSame(blueFirst, blueYields, "red's word opens no route yet: the order in course was measured from a pose blue no longer knows");
        Assert.IsTrue(blueFirst.Yielding);
        Assert.AreEqual("pending", blueFirst.Status);
        Assert.AreEqual("stop", blueFirst.Order, "the route asks the body to stop and say where it stood");
        Assert.IsFalse(blueFirst.IsWalkable);
        Assert.AreSame(blueFirst, blue.Dash(blueFirst), "a route that yields is replaced, not improved");
        Assert.AreSame(blueCall, blue.Choreography.FormationOf(blueFirst), "the body's word finds the formation by the route underway");

        // the body stopped half-way, turned a little: the new route opens from THERE, not from where the first one began
        var blueNow = blueCall.Halted(new Pose(5.6, 5.6, 0.4));
        Assert.AreNotSame(blueFirst, blueNow);
        Assert.AreEqual("abandoned", blueFirst.Status);
        StringAssert.Contains(blueFirst.Why, "red took this place by distance: blue goes to (6.5, 6.5)");
        StringAssert.Contains(blueNow.AsPlan(), "floor@6.5,6.5", "blue goes to the north-east corner now");
        Assert.AreEqual(0.0, blueNow.Standing.DistanceTo(new Position(5.6, 5.6)), 1e-9, "measured from where the body stopped");
        Assert.AreEqual(0.4, blueNow.Standing.Heading, 1e-9, "…facing the way it stopped");
        Assert.AreSame(blueNow, blueCall.Route);
        Assert.AreSame(blueCall, blue.Choreography.FormationOf(blueNow));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blueCall.Halted(new Pose(5.6, 5.6, 0.4))).Message, "nothing to halt");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.FormationOf(blueFirst)).Message, "was given by no formation");

        // the same word again changes nothing; a golem convenes once; the name is the formation's identity
        Assert.AreSame(blueNow, blueCall.Stood(fleet.Member(red), new Position(4.4, 4.4)));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blueCall.Convene(new Pose(5.8, 5.8, 0.0), fleet.Member(blue))).Message, "already took its place");
        Assert.AreSame(blueCall, blue.Choreography.Find("red-1"), "the formation by its name");
        Assert.AreNotSame(blueCall, blue.Choreography.Form("red-2", blue.Choreography.Figure("square", center, side), fleet, Policy.Distance), "another name, another formation");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blueCall.Stood(fleet.Member(blue), new Position(1.0, 1.0))).Message, "its own word is its Convene");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Form("red-3", blue.Choreography.Figure("square", center, side), fleet, (Policy)7)).Message, "by Rank, Distance or RockPaperScissors");
    }

    [TestMethod]
    public void AWordToARouteAlreadyWalked_OpensTheNextAtOnce_FromWhereTheLastArrivalLeftTheBody()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,red");
        var call = blue.Choreography.Form("red-1", blue.Choreography.Figure("square", new Position(5.5, 5.5), new Meters(2.0)), fleet, Policy.Distance);
        Assert.IsNull(call.Convene(new Pose(5.8, 5.8, 0.0), fleet.Member(blue)), "the round is not complete");
        var first = call.Stood(fleet.Member("red"), new Position(7.0, 7.0));
        StringAssert.Contains(first.AsPlan(), "floor@4.5,6.5", "red nearer to the north-east corner: blue takes the north-west one");
        WalkToTheEnd(first);
        Assert.AreEqual("completed", first.Status, "blue is on the north-west corner, facing the centre: no order in course");
        Assert.AreEqual(0, blue.Current.Collisions.PeersInTheWay().Count, "the route ended: red has moved on, nothing is planned around");

        // red corrects its word: it stands by the south-west corner after all — blue, already placed, sets out for the north-east one at once
        var next = call.Stood(fleet.Member("red"), new Position(4.4, 4.4));
        Assert.AreNotSame(first, next, "nothing to stop: the route to the next place opens at once");
        Assert.IsFalse(first.Yielding);
        Assert.AreEqual("completed", first.Status, "a route walked stays walked");
        Assert.AreEqual(0.0, next.Standing.DistanceTo(new Position(4.5, 6.5)), 1e-9, "from the corner the last arrival left the body on");
        StringAssert.Contains(next.AsPlan(), "floor@6.5,6.5");
    }

    [TestMethod]
    public void TwoWordsWhileTheBodyStops_OneStop_OneRouteToTheLastPlace()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var green = new Golem(body, "green");
        green.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,green,red");
        var call = green.Choreography.Form("red-1", green.Choreography.Figure("square", new Position(5.5, 5.5), new Meters(2.0)), fleet, Policy.Distance);
        // three bodies on a square: the round — blue and red far away, green by the north-east corner — gives green the nearest place
        Assert.IsNull(call.Convene(new Pose(7.0, 7.0, 0.0), fleet.Member(green)));
        Assert.IsNull(call.Stood(fleet.Member("blue"), new Position(0.5, 0.5)));
        var first = call.Stood(fleet.Member("red"), new Position(0.5, 9.5));
        Assert.IsNotNull(first, "the third word completes the round");
        var firstPlace = call.PlaceOf(fleet.Member(green));

        // two corrected words while green's body carries an order: one stop, the place kept moves twice
        Assert.AreSame(first, call.Stood(fleet.Member("blue"), new Position(firstPlace.X, firstPlace.Y)), "blue stands on green's place: green yields");
        Assert.IsTrue(first.Yielding);
        var afterBlue = call.PlaceOf(fleet.Member(green));
        Assert.AreSame(first, call.Stood(fleet.Member("red"), new Position(afterBlue.X, afterBlue.Y)), "red stands on green's next place: still the one stop");
        var last = call.PlaceOf(fleet.Member(green));
        Assert.IsTrue(last.DistanceTo(afterBlue) > 0.5 && last.DistanceTo(firstPlace) > 0.5, "red took that one too: green gets the third");

        var route = call.Halted(new Pose(6.8, 6.8, 0.2));
        Assert.AreEqual("abandoned", first.Status);
        StringAssert.Contains(first.Why, "red took this place by distance", "in the words of the last peer that took it");
        Assert.AreEqual(0.0, route.PlannedEnd.DistanceTo(last), 1e-9, "one route, to the last place");
        Assert.AreEqual(2, green.Routes().Count, "the first and the one that replaced it: no route in between");
    }

    // THE BERTHS (ajuste 80; Juan: "azul debería saber que ahí se encuentra amarillo y chocará en su trayecto"): the other members, where they
    // said they stand and where they go, are bodies in the way when the route to the place opens — the way goes around them from its first leg.
    [TestMethod]
    public void ThePeersKnownAtTheCall_AreBerthsTheWayGoesAround_UntilTheRouteEnds()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        var collisions = new Collisions();
        blue.Enter(new Scenario(Catalog.OpenFloor(), collisions));
        var fleet = new Fleet("blue,yellow");   // two on a square: blue the north-east corner (6.5, 6.5), yellow the north-west one; two corners free (ajuste 86)
        var square = blue.Choreography.Figure("square", new Position(5.5, 5.5), new Meters(2.0));
        var call = blue.Choreography.Form("red-1", square, fleet, Policy.Rank);
        Assert.AreEqual(Policy.Rank, call.Policy);

        // yellow stands in the middle of blue's column, by the south-east corner; blue comes from the south, straight up x = 6.5
        Assert.IsNull(call.Stood(fleet.Member("yellow"), new Position(6.5, 4.5)), "yellow's word, before blue joined: kept");
        var route = call.Join(new Pose(6.5, 2.5, 1.5708), fleet.Member(blue));
        var berths = call.Berths();
        Assert.AreEqual(2, berths.Count, "yellow where it stands and where it goes: " + string.Join(", ", berths.Select(b => b.Who + "@" + b.Center.X + "," + b.Center.Y)));
        Assert.AreEqual(2, route.Company.Count, "both the route's company while it lasts (propuesta 107)");
        Assert.AreEqual(0, collisions.PeersInTheWay().Count, "and no peer met: nobody touched yellow");
        Assert.AreEqual(0, collisions.Present().Count, "the obstacles read shows nothing of the fleet's words");
        Assert.IsTrue(route.LegsAhead.Count > 2, "not the straight run up the column through yellow: the way bends around it — " + route.AsPlan());
        Assert.IsTrue(route.LegsAhead.All(l => Math.Max(Math.Abs(l.At.X - 6.5), Math.Abs(l.At.Y - 4.5)) > 0.6), "no leg ends inside yellow's berth — " + route.AsPlan());
        StringAssert.Contains(route.AsPlan(), "floor@6.5,6.5", "and it still ends on blue's own corner");
        WalkToTheEnd(route);
        Assert.AreEqual("completed", route.Status);
        Assert.AreEqual(2, route.Company.Count, "the route ended: its company goes with it — the next route is told its own");

        // a peer standing on MY place is no berth (by rank a golem may stand on another's corner): the route still opens
        var other = new Golem(body, "yellow");
        other.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var otherCall = other.Choreography.Form("red-1", square, fleet, Policy.Rank);
        otherCall.Stood(fleet.Member("blue"), new Position(4.6, 6.4));   // blue stands almost on the north-west corner, yellow's place by rank
        var toNorthWest = otherCall.Join(new Pose(2.5, 8.5, 0.0), fleet.Member(other));
        StringAssert.Contains(toNorthWest.AsPlan(), "floor@4.5,6.5", "the place is taken even so: " + toNorthWest.AsPlan());
    }

    // THE STEPS (ajuste 77; Juan: "que la flota tome la posición del otro en el sentido de las agujas del reloj y antihorario… se pueden
    // encolar"): the convocation is the formation in place; every member says when it stands on its place; a step waits for everybody and
    // then takes each to the next place in its sense — clockwise is DOWN the order (the places run counter-clockwise from north-east).
    [TestMethod]
    public void AStep_WaitsForEverybodyPlaced_ThenTakesEachToTheNextPlace_AndStepsQueue()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,red");
        var square = blue.Choreography.Figure("square", new Position(5.5, 5.5), new Meters(2.0));
        var call = blue.Choreography.Form("red-1", square, fleet, Policy.Rank);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Current).Message, "stands in no formation");

        // blue joins by rank: first of two, the north-east corner (place 1 of 2 of the square: north-east, then south-west)
        var first = call.Join(new Pose(5.8, 5.8, 0.0), fleet.Member(blue));
        StringAssert.Contains(first.AsPlan(), "floor@6.5,6.5");
        Assert.AreEqual(0, call.PlaceIndex);
        Assert.AreSame(call, blue.Choreography.Current);
        Assert.IsTrue(blue.Choreography.Knows("red-1"));
        Assert.IsFalse(blue.Choreography.Knows("red-2"), "no place in a call it never heard of");
        Assert.IsFalse(blue.Choreography.Reached(first), "not yet: the route is underway");

        // a step is queued before anybody arrived: nothing moves — the MOVE is the figure's (ajuste 84), the queue the convocation's
        var clockwise = square.Rotate(Sense.Clockwise);
        Assert.AreEqual("one step clockwise", clockwise.Name);
        Assert.AreEqual(-1, clockwise.Shift, "the places run counter-clockwise: clockwise steps down the order");
        Assert.AreEqual(3, clockwise.Next(0, 4), "from the first corner of four, around to the last");
        Assert.AreEqual(1, square.Rotate(Sense.Counterclockwise).Next(0, 4));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => clockwise.Next(4, 4)).Message, "is none of the 4 places");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => call.Queue(null, "step-0")).Message, "'move' was not given");
        Assert.AreEqual(1, call.Queue(clockwise, "step-1"));
        Assert.IsFalse(call.CanStep);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => call.Step()).Message, "stand on their places");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => call.Queue(call.Figure.Rotate(Sense.Clockwise), "step-1")).Message, "already queued");

        // red says it stands on its place; blue is not there yet: the word is kept
        Assert.AreSame(first, call.Heard(fleet.Member("red")), "red is placed, blue is still walking: the route in place comes back");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => call.Placed(fleet.Member("red"))).Message, "a peer's word is Heard");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => call.Heard(call.Me)).Message, "its own word is Placed");
        Assert.AreEqual(1, call.PlacedCount);
        Assert.IsFalse(call.CanStep);

        // blue arrives: it reached its place, says so — and with that everybody is placed: the step opens at once
        WalkToTheEnd(first);
        Assert.IsTrue(blue.Choreography.Reached(first));
        Assert.AreSame(call, blue.Choreography.FormationOf(first));
        Assert.AreSame(fleet.Member(blue).Name, call.Me.Name);
        var second = call.Placed(call.Me);
        Assert.AreNotSame(first, second, "everybody placed and a step queued: the step opens");
        Assert.AreEqual(0, call.Queued);
        Assert.AreEqual(3, call.PlaceIndex, "clockwise from the first of FOUR corners (two of them free, ajuste 86): the last, south-east");
        StringAssert.Contains(second.AsPlan(), "floor@6.5,4.5", "blue goes to the south-east corner, a free one");
        Assert.AreEqual(0.0, second.Standing.DistanceTo(new Position(6.5, 6.5)), 1e-9, "from the corner it stood on");
        Assert.AreSame(second, call.Route);
        Assert.AreEqual(0, call.PlacedCount, "a new round: nobody is placed yet");
        Assert.IsFalse(blue.Choreography.Reached(first), "the first route is no longer the one to its place");

        // two more steps queue while they walk; the first of them opens only when both arrived again
        call.Queue(call.Figure.Rotate(Sense.Counterclockwise), "step-2");
        Assert.AreEqual(2, call.Queue(call.Figure.Rotate(Sense.Counterclockwise), "step-3"));
        // THE START TOGETHER (propuesta 106; Juan: "que todos se sincronicen en posición de salida y avancen a la vez"): the step's route turns the
        // body to face its next place and then WAITS; blue says it is lined up, and with red's word both set out at once
        Assert.IsFalse(second.Waiting, "not lined up yet: the turn is asked first");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => call.Aligned(call.Me)).Message, "is not waiting for the fleet's start");
        second.Arrive(second.NextLeg);
        Assert.IsTrue(second.Waiting, "the turn done, the route waits for the fleet's start");
        Assert.AreEqual("stop", second.Order);
        Assert.IsFalse(second.IsWalkable);
        Assert.IsTrue(call.AwaitsMyWord);
        Assert.AreSame(second, call.Aligned(call.Me));
        Assert.IsFalse(call.AwaitsMyWord);
        Assert.AreEqual(1, call.AlignedCount);
        Assert.IsTrue(second.Waiting, "red is not lined up yet");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => call.HeardAligned(call.Me)).Message, "its own word is Aligned");
        Assert.AreSame(second, call.HeardAligned(fleet.Member("red")));
        Assert.IsFalse(second.Waiting, "everybody lined up: the fleet starts");
        Assert.AreEqual("advance", second.Order);
        Assert.AreEqual(2, call.AlignedCount, "the words of this round stay until the formation leaves it: a route opened again in the round starts at once");
        WalkToTheEnd(second);
        Assert.AreSame(second, call.Placed(call.Me), "blue stands, red does not yet: the step waits");
        var third = call.Heard(fleet.Member("red"));
        Assert.AreNotSame(second, third);
        Assert.AreEqual(1, call.Queued, "one step left in the queue");
        Assert.AreEqual(0, call.PlaceIndex, "counter-clockwise from the south-east corner: back to the first, north-east");
        StringAssert.Contains(third.AsPlan(), "floor@6.5,6.5");
        Assert.AreEqual(3, blue.Routes().Count, "one route per step, nothing in between");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => call.Heard(new Fleet("blue,green,red").Member("green"))).Message, "is not a member of the fleet");
        Assert.AreSame(third, call.Stood(fleet.Member("red"), new Position(7.0, 7.0)), "by rank a word moves nobody: the place is the rank's");
    }

    [TestMethod]
    public void AStep_FollowsAConvocationByDistance_FromThePlaceTheTableGave()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,red");
        var call = blue.Choreography.Form("red-1", blue.Choreography.Figure("square", new Position(5.5, 5.5), new Meters(2.0)), fleet, Policy.Distance);
        Assert.IsNull(call.Heard(fleet.Member("red")), "a word before blue has a route is only kept");
        Assert.IsNull(call.Stood(fleet.Member("red"), new Position(7.0, 7.0)));
        var first = call.Convene(new Pose(5.8, 5.8, 0.0), fleet.Member(blue));
        StringAssert.Contains(first.AsPlan(), "floor@4.5,6.5", "red is nearer to the north-east corner: blue takes the north-west one (ajuste 86: the corners, two free)");
        Assert.AreEqual(1, call.PlaceIndex, "the index of the place the table gave");
        call.Queue(call.Figure.Rotate(Sense.Clockwise), "step-1");
        WalkToTheEnd(first);
        var step = call.Placed(call.Me);
        Assert.AreNotSame(first, step, "red's word came first; blue's completes the round: the step opens");
        Assert.AreEqual(0, call.PlaceIndex);
        StringAssert.Contains(step.AsPlan(), "floor@6.5,6.5", "clockwise from the north-west corner: the north-east one");
    }

    // THE FACING (ajuste 79; resolves the pendiente 15): a route to a place ends with the body turning to face the centre, so every step
    // costs every body the same turn and the fleet moves in lockstep from the first step on.
    [TestMethod]
    public void ARouteToAPlace_EndsFacingTheCentre_AndTheStepCostsEveryBodyTheSameTurn()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,green,purple,yellow");
        var square = blue.Choreography.Figure("square", new Position(5.5, 5.5), new Meters(2.0));
        var call = blue.Choreography.Form("red-1", square, fleet, Policy.Rank);

        // blue comes from the north, down x = 6.5, to the north-east corner: it arrives facing south, and the route then has it face the centre
        var route = call.Join(new Pose(6.5, 10.4, -1.5708), fleet.Member(blue));
        Assert.AreEqual(0.0, route.FacesToward.DistanceTo(new Position(5.5, 5.5)), 1e-9, "the route faces the centre");
        StringAssert.EndsWith(route.AsPlan(), "floor@6.5,6.5 > face@6.5,6.5", "the way ends with the facing leg, on the place: " + route.AsPlan());
        Assert.IsTrue(route.LegsAhead.Last().IsFacing);
        Assert.IsFalse(route.LegsAhead.Last().IsCorrection);
        Assert.IsFalse(route.LegsAhead.Last().IsStop);
        Assert.AreEqual(1, route.StopsLeft, "the facing is no stop");
        Assert.AreEqual("advance", route.Order, "already facing south: straight to the corner");
        route.Arrive(route.NextLeg);
        Assert.IsTrue(route.IsPending(), "on the corner, the route is not done: it faces the centre first");
        Assert.AreEqual(0, route.StopsLeft);
        Assert.AreEqual("turnRight", route.Order, "from south (−90°) to the centre at −135°: a right turn");
        Assert.AreEqual(Math.PI / 4, route.Amount, 1e-3, "45 degrees");
        Assert.IsFalse(call.Reached, "not placed until it faces the centre");
        route.Arrive(route.NextLeg);
        Assert.AreEqual("completed", route.Status, "the turn made, the route is complete: no advance asked");
        Assert.AreEqual(-3 * Math.PI / 4, route.Standing.Heading, 1e-3, "facing the centre");
        Assert.IsTrue(call.Reached);

        // every step from here costs 45 degrees: from facing the centre to along the side, whichever sense
        call.Placed(call.Me); foreach (var name in new[] { "green", "purple", "yellow" }) call.Heard(fleet.Member(name));
        call.Queue(call.Figure.Rotate(Sense.Clockwise), "s1");
        Assert.AreEqual(0, call.Round);
        var step = call.Step();
        Assert.AreEqual(1, call.Round, "one step opened: the second round — its words are not the first's");
        Assert.AreEqual(Math.PI / 4, step.Amount, 1e-3, "clockwise from the north-east corner: south along the east side, 45° from facing the centre: " + step.Order);
        StringAssert.EndsWith(step.AsPlan(), "floor@6.5,4.5 > face@6.5,4.5", "and the step ends facing the centre too");

        // a body that already faces the centre when it reaches its place completes at once
        var green = new Golem(body, "green");
        green.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var greens = green.Choreography.Form("red-1", square, fleet, Policy.Rank);
        var toNorthWest = greens.Join(new Pose(3.5, 7.5, -0.7854), fleet.Member(green));   // from the north-west diagonal, facing the centre
        toNorthWest.Arrive(toNorthWest.NextLeg);
        Assert.AreEqual("completed", toNorthWest.Status, "already facing the centre on arrival: nothing to turn — " + toNorthWest.AsPlan());
    }

    // A GRAZE ON THE WAY TO A PLACE (lab 8-oct-2026: yellow touched a wall on its way to its place in the double ring, the golem refused its own
    // bump — "route 1's way must end at a stop, not at 'face'" — and the body stood forever): the legs left are walked again from the retreat,
    // and the facing leg is appended once, by Take, not copied too.
    [TestMethod]
    public void AGrazeOnTheWayToAPlace_KeepsTheWay_AndOneFacingLeg()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,red");
        var square = blue.Choreography.Form("sq", "square", new Position(5.5, 5.5), new Meters(2.0), new Degrees(0.0), fleet, Policy.Rank);
        var route = square.Join(new Pose(0.5, 6.5, 0.0), fleet.Member(blue));   // from the west edge, straight east to the north-east corner
        StringAssert.EndsWith(route.AsPlan(), "floor@6.5,6.5 > face@6.5,6.5", route.AsPlan());
        // the body touches the perimeter wall it brushed along, a graze: the way is kept, the retreat first, the facing once at the end
        route.Graze(new Position(1.0, 7.0), new Pose(1.0, 6.75, 0.0));
        Assert.AreEqual("pending", route.Status);
        Assert.AreEqual(1, route.LegsAhead.Count(l => l.IsFacing), "one facing leg, appended by Take — not two");
        StringAssert.EndsWith(route.AsPlan(), "floor@6.5,6.5 > face@6.5,6.5", route.AsPlan());
        Assert.AreEqual("back", route.Order, "the retreat first");
    }

    // The body's walk: the leg the route asks, done as asked — reported the way the robot does (ajuste 68).
    // A BODY ALREADY ON ITS PLACE, TOLD TO TAKE IT (13-oct-2026; lab: green, carried onto its place, was asked turnLeft 1.32, advance 0.01 and
    // turnRight 1.29 — a line-up toward a point a centimetre away, pure noise — and faced one way while its golem believed another; every step
    // after went a radian off): the stop within a hair is reached at birth and the route asks the facing turn alone, from where the body faces
    [TestMethod]
    public void ABodyOnItsPlaceAlready_IsAskedTheFacingTurnAlone_NoLineUpTowardACentimetre()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var copy = blue.Choreography.Form("tri", "triangle", new Position(3.0, 5.5), new Meters(3.0), new Degrees(0.0), new Fleet("blue"), Policy.Rank, "call-1");
        var place = copy.Figure.Places(1)[0];   // the apex, north of the centre: (3, 7.23)
        var route = copy.Convene(new Pose(place.X + 0.008, place.Y - 0.006, -0.08), copy.Fleet.Member(blue));   // standing on it, a centimetre off, facing east-ish
        Assert.IsNotNull(route);
        Assert.AreEqual(1, route.LegsAhead.Count, "the stop was reached at birth: the facing alone is ahead — " + route.AsPlan());
        Assert.IsTrue(route.NextLeg.IsFacing);
        Assert.AreEqual("turnRight", route.Order, "the centre lies south of the apex: a right turn from facing east, measured from where the body really faces");
        Assert.AreEqual(Math.PI / 2 - 0.08, route.Amount, 0.02, "down to facing south, nothing else");
        route.Arrive(route.NextLeg);
        Assert.AreEqual("completed", route.Status, "faced: on its place");
        Assert.IsTrue(copy.Reached);

        // on its place AND facing the centre already: nothing to walk, nothing to turn — the route is complete at birth (13-oct-2026 lab: blue was
        // asked `advance 0.02` on its facing leg), and the formation knows the golem stands on its place
        var red = new Golem(body, "red");
        red.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var redCopy = red.Choreography.Form("tri", "triangle", new Position(3.0, 5.5), new Meters(3.0), new Degrees(0.0), new Fleet("red"), Policy.Rank, "call-1");
        var apex = redCopy.Figure.Places(1)[0];
        var born = redCopy.Convene(new Pose(apex.X + 0.01, apex.Y - 0.01, -Math.PI / 2 + 0.01), redCopy.Fleet.Member(red));   // within the 0.02 rad the golem lets pass (ajuste 119; was 0.05)
        Assert.AreEqual("completed", born.Status, born.AsPlan());
        Assert.IsTrue(redCopy.Reached, "the host says Placed with no arrival");
    }

    // A MEMBER ALREADY ON ITS PLACE (13-oct-2026: a take again after a failure, one member standing where it must; the others waited for its
    // line-up word for ever): it is no mover of the round, and placed implies lined up — the others set out without a word from it
    [TestMethod]
    public void AMemberStandingOnItsPlace_IsNoMover_AndPlacedCountsAsLinedUp_SoTheOthersSetOut()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var fleet = new Fleet("blue,red");
        var red = new Golem(body, "red");
        red.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var copy = red.Choreography.Form("pair", "square", new Position(5.5, 5.5), new Meters(2.0), new Degrees(0.0), fleet, Policy.Rank, "call-1");
        var blue = fleet.Member("blue");
        var me = fleet.Member("red");
        // blue said it stands on the north-east corner, its place (6.5, 6.5), near enough; red stands far away
        Assert.IsNull(copy.Stood(blue, new Position(6.4, 6.6)));
        var route = copy.Convene(new Pose(2.0, 2.0, 0.0), me);
        Assert.IsNotNull(route, "the round is complete: red's route opens");
        route.Arrive(route.NextLeg);   // red's line-up turn
        Assert.IsTrue(route.Waiting, "lined up, red waits for the movers");
        copy.Aligned(me);
        Assert.IsTrue(!route.Waiting, "blue is no mover — it stands on its place — so red's own line-up is the whole start: " + route.Order);

        // and a member heard as PLACED counts as lined up: a round where the other stood away but arrived before lining up (its word of being there)
        var later = red.Choreography.Form("pair-2", "square", new Position(5.5, 5.5), new Meters(2.0), new Degrees(0.0), fleet, Policy.Rank, "call-2");
        Assert.IsNull(later.Stood(blue, new Position(9.0, 9.0)));
        var again = later.Convene(new Pose(2.0, 2.0, 0.0), me);
        again.Arrive(again.NextLeg);
        later.Aligned(me);
        Assert.IsTrue(again.Waiting, "blue moves and said nothing yet");
        later.Heard(blue);
        Assert.IsFalse(again.Waiting, "blue placed: as good as lined up — red sets out");
    }

    // THE RETAKE (propuesta 113, 13-oct-2026): a member's route to its place ended short — the formation opens it again from where the body
    // stands, once; a second failure leaves it stranded for that golem, said in the read, and placed once the retake reaches, it is forgotten
    [TestMethod]
    public void ARouteToAPlaceThatEndedShort_IsTakenAgainFromWhereTheBodyStands_Once_ThenTheFormationIsStranded()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var formation = blue.Choreography.Form("tri-1", "triangle", new Position(5.5, 5.5), new Meters(2.0), new Degrees(0.0), new Fleet("blue"), Policy.Rank, "call-1");
        var route = formation.Convene(new Pose(2.0, 2.0, 0.0), formation.Fleet.Member(blue));
        Assert.IsNotNull(route, "a fleet of one: the round is complete at once");
        Assert.IsFalse(formation.NeedsRetake, "underway: nothing ended short");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => formation.Retake(new Pose(2.5, 2.5, 0.0))).Message, "nothing to take again");

        route.Fail("stalled: no progress for 3 s");
        Assert.IsTrue(formation.NeedsRetake, "the route to the place failed");
        var again = formation.Retake(new Pose(3.0, 3.0, 0.0));
        Assert.AreNotSame(route, again, "a new route, from where the body stands");
        Assert.IsTrue(again.IsPending());
        StringAssert.EndsWith(again.AsPlan().Trim(), "face@5.5,6.65", "to the same place, ending facing the centre: " + again.AsPlan());
        Assert.IsFalse(formation.Stranded);
        Assert.IsFalse(formation.NeedsRetake, "underway again");

        again.Fail("stalled: no progress for 3 s");
        Assert.IsTrue(formation.NeedsRetake);
        var same = formation.Retake(new Pose(3.2, 3.1, 0.0));
        Assert.AreSame(again, same, "a second failure: no third route");
        Assert.IsTrue(formation.Stranded, "the formation says it: the warden decides");
        StringAssert.Contains(formation.StrandedWhy, "blue could not reach (5.5, 6.65) twice");
        Assert.IsFalse(formation.NeedsRetake, "stranded: nothing more by itself");

        // placed at last (the warden took it again and the body reached): the stranding is forgotten and a retake is to be had again
        formation.Placed(formation.Fleet.Member(blue));
        Assert.IsFalse(formation.Stranded, "placed: forgotten");
        Assert.AreEqual("", formation.StrandedWhy);
    }

    // THE ROUTE BORN FAILED (19-oct-2026; lab 14-oct, the march by rps: green's word completed orange's round, the take found "no road … past 1
    // marks" and the exception refused the whole uptake — orange without a route and not stranded, the five others waiting for its line-up for
    // ever): a peer's word that completes the round and finds no take possible is not refused — the route is born failed with the reason, so the
    // word is journaled, the print says it ended short, and the retake answers it once before the formation is stranded. A place off the map
    // stays a refusal: nothing to take again from anywhere.
    [TestMethod]
    public void AWordThatCompletesTheRound_WithNoTakePossible_IsNotRefused_TheRouteIsBornFailed_AndTheRetakeAnswersIt()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,red");
        // a square against the north wall: its north corners (y 10.8) are on the map but leave no room for the body (the inset is 0.35 m)
        var formation = blue.Choreography.Form("sq-1", blue.Choreography.Figure("square", new Position(5.5, 9.8), new Meters(2.0)), fleet, Policy.Distance);
        Assert.IsNull(formation.Convene(new Pose(6.5, 10.0, 0.0), fleet.Member(blue)), "one of two spoke: the round is open");

        var route = formation.Stood(fleet.Member("red"), new Position(4.0, 8.5));   // red nearest the south-west corner: blue gets the north-east one
        Assert.IsNotNull(route, "the word completed the round and was not refused");
        Assert.AreSame(route, formation.Route);
        Assert.IsFalse(route.IsPending(), "born failed");
        Assert.IsTrue(route.EndedShort);
        StringAssert.Contains(route.Why, "leaves no room for a body");
        Assert.AreEqual(1, blue.Routes().Count, "minted and held like any other route: the journal keeps the fact");
        Assert.IsTrue(formation.NeedsRetake, "the retake answers it — the clock asks, since no arrival will");
        Assert.IsFalse(formation.Stranded);

        var again = formation.Retake(new Pose(6.5, 10.0, 0.0));
        Assert.AreSame(route, again, "no take possible from there either: no new route");
        Assert.IsTrue(formation.Stranded, "once: the formation says it, the warden decides");
        StringAssert.Contains(formation.StrandedWhy, "blue could not reach (6.5, 10.8) again");
        Assert.IsFalse(formation.NeedsRetake);

        // a place off the map is refused as ever: the word is not taken
        var off = blue.Choreography.Form("sq-2", blue.Choreography.Figure("square", new Position(5.5, 10.5), new Meters(2.0)), fleet, Policy.Distance);
        off.Convene(new Pose(6.5, 10.9, 0.0), fleet.Member(blue));   // nearest the north-east corner (6.5, 11.5), off the floor
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => off.Stood(fleet.Member("red"), new Position(4.0, 8.5))).Message, "nowhere on the map");
    }

    // THE CUE BETWEEN DANCERS (propuesta 117, 19-oct-2026; Juan: "dos grupos van a bailar… ya conocen su coreografía completa… no habrá un director…
    // el último del primer grupo le hace la seña a alguno del otro grupo… uno sólo le dice el nombre del golem… a él sólo le dicen 'continúas tú' y
    // éste le avisa a su equipo"; evening: "que el último en salir de la coreografía sea el que diga realmente a yellow"): every member finishes its
    // own part — a finish asked before the part is done waits for it — and THE COPY THAT COUNTS THE FLEET'S LAST FINISH cues ONE golem by name; the
    // right formation, born on cue, refuses a take, keeps its steps queued, and is taken by the cued one — whose word lets its team convene; a cue
    // heard before the formation is kept for it
    [TestMethod]
    public void TheLastToLeaveAFinishedFleet_CuesOneGolemByName_WhoTakesTheFormationHeWaitsIn_AndHisTeamFollows()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        Golem Born(string name) { var g = new Golem(body, name); g.Enter(new Scenario(Catalog.OpenFloor(), new Collisions())); return g; }
        var blue = Born("blue"); var red = Born("red");
        var left = new Fleet("blue,red,green");
        var centre = new Position(3.0, 5.5);
        var blueLeft = blue.Choreography.Form("tri-left", blue.Choreography.Figure("triangle", centre, new Meters(2.0)), left, Policy.Rank);
        var redLeft = red.Choreography.Form("tri-left", red.Choreography.Figure("triangle", centre, new Meters(2.0)), left, Policy.Rank);
        Assert.AreEqual("", blueLeft.CueTo); Assert.IsFalse(blueLeft.OnCue); Assert.IsTrue(blueLeft.IsCued, "a formation that does not start on cue is as good as cued");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blueLeft.Cues("green")).Message, "it is of this very fleet");
        blueLeft.Cues("Cyan"); redLeft.Cues("cyan");
        Assert.AreEqual("cyan", redLeft.CueTo);

        // blue asks to finish BEFORE its part is done: the word waits (finishing), nothing counted (a step may still come after it: it is walked first)
        var places = blueLeft.Figure.Places(3);
        Pose OnPlace(int index) { var at = places[index]; return new Pose(at.X, at.Y, Math.Atan2(centre.Y - at.Y, centre.X - at.X)); }
        Assert.IsNull(blueLeft.Convene(OnPlace(0), left.Member(blue)), "one of three spoke");
        Assert.AreSame(blueLeft.Route, blueLeft.Finish(left.Member(blue)));
        Assert.IsTrue(blueLeft.Finishing); Assert.IsFalse(blueLeft.JustFinished); Assert.IsFalse(blueLeft.FinishDue); Assert.AreEqual(0, blueLeft.FinishedCount);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blueLeft.Finish(left.Member("red"))).Message, "a peer's word is HeardFinished");
        // the round completes, blue's route is complete at birth (on its place, facing the centre), it is placed: the finish is due, and counts
        blueLeft.Stood(left.Member("green"), places[1]);
        Assert.IsNotNull(blueLeft.Stood(left.Member("red"), places[2]));
        Assert.AreEqual("completed", blueLeft.Route.Status);
        blueLeft.Placed(left.Member(blue));
        Assert.IsTrue(blueLeft.IsDone); Assert.IsTrue(blueLeft.FinishDue); Assert.IsTrue(blue.Choreography.Finishing); Assert.AreSame(blueLeft, blue.Choreography.FinishingFormation);
        blueLeft.Finish(left.Member(blue));
        Assert.IsTrue(blueLeft.JustFinished); Assert.AreEqual(1, blueLeft.FinishedCount); Assert.IsFalse(blueLeft.Finishing); Assert.IsFalse(blue.Choreography.Finishing);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blueLeft.Finish(left.Member(blue))).Message, "already finished");
        Assert.IsFalse(blueLeft.Cueing, "two still dancing");
        // the last to leave, as blue's copy hears it: with the fleet's last finish counted, blue has the cue to give — whoever that last one was
        blueLeft.HeardFinished(left.Member("green")); blueLeft.HeardFinished(left.Member("red"));
        Assert.IsTrue(blueLeft.IsFinished); Assert.IsTrue(blueLeft.Cueing); Assert.IsTrue(blue.Choreography.Cueing); Assert.AreSame(blueLeft, blue.Choreography.CueingFormation);
        blueLeft.Cue();
        Assert.IsTrue(blueLeft.CueGiven); Assert.IsFalse(blueLeft.Cueing, "given once"); Assert.IsFalse(blue.Choreography.Cueing);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blueLeft.Cue()).Message, "no cue to give here");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blueLeft.Cues("orange")).Message, "already gave its cue");

        // red's copy counts the same words in its own order: the words carry no clock, so every copy that hears the last one may cue — the cued
        // golem takes the first word and drops the rest
        redLeft.Stood(left.Member("blue"), places[0]); redLeft.Stood(left.Member("green"), places[1]);
        Assert.IsNotNull(redLeft.Convene(OnPlace(2), left.Member(red)));
        redLeft.Placed(left.Member(red));
        redLeft.HeardFinished(left.Member("blue")); redLeft.HeardFinished(left.Member("green"));
        Assert.IsFalse(redLeft.Cueing, "red has not finished itself");
        redLeft.Finish(left.Member(red));
        Assert.IsTrue(redLeft.JustFinished); Assert.IsTrue(redLeft.IsFinished); Assert.IsTrue(redLeft.Cueing);

        // A FORMATION THAT FINISHED STAYS, AND STILL MOVES (19-oct-2026; Juan: "creo que quizás no debería borrarse, pero creo que siempre
        // debería permitir moverse"): listed and found as before, a step queued on it opens; the finish counted stays counted, the cue is its
        Assert.AreEqual(1, red.Choreography.Formations().Count, "finished: still in the list");
        Assert.AreSame(redLeft, red.Choreography.Find("tri-left")); Assert.AreSame(redLeft, red.Choreography.Current);
        Assert.AreEqual(1, redLeft.Queue(redLeft.Figure.Rotate(Sense.Counterclockwise), "after-finish"));
        Assert.IsTrue(redLeft.IsFinished, "the finish counted stays counted"); Assert.IsTrue(redLeft.Cueing);

    // THE RIGHT, born on cue: a take is refused, the steps queue meanwhile, and the cued one takes it
        var cyan = Born("cyan"); var orange = Born("orange");
        var right = new Fleet("cyan,orange,yellow");
        var cyanRight = cyan.Choreography.Form("tri-right", cyan.Choreography.Figure("triangle", new Position(8.0, 5.5), new Meters(2.0)), right, Policy.Rank);
        cyanRight.StartsOnCue();
        Assert.IsTrue(cyanRight.OnCue); Assert.IsFalse(cyanRight.IsCued);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => cyanRight.Convene(new Pose(7.0, 5.5, 0.0), right.Member(cyan))).Message, "starts on cue");
        Assert.AreEqual(1, cyanRight.Queue(cyanRight.Figure.Rotate(Sense.Clockwise), "step-1"), "its choreography is known before it dances: the step waits");
        Assert.IsFalse(cyan.Choreography.AwaitsCue("orange"), "the cue names another golem: nobody's here");
        Assert.IsTrue(cyan.Choreography.AwaitsCue("Cyan"));
        Assert.AreSame(cyanRight, cyan.Choreography.CuedFormation);
        Assert.IsNull(cyanRight.TakeCue(new Pose(7.0, 5.5, 0.0), right.Member(cyan)), "taken by the cue: one of three spoke");
        Assert.IsTrue(cyanRight.IsCued);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => cyanRight.TakeCue(new Pose(7.0, 5.5, 0.0), right.Member(cyan))).Message, "was cued already");
        Assert.IsFalse(cyan.Choreography.AwaitsCue("cyan"), "taken: it waits no more");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => cyanRight.StartsOnCue()).Message, "starts on cue no more");
        Assert.AreEqual(1, cyanRight.Queued, "the step still waits for the round");
        // orange hears cyan's word: the team follows — its own take is no longer refused
        var orangeRight = orange.Choreography.Form("tri-right", orange.Choreography.Figure("triangle", new Position(8.0, 5.5), new Meters(2.0)), right, Policy.Rank);
        orangeRight.StartsOnCue();
        orangeRight.Stood(right.Member("cyan"), new Position(7.0, 5.5));
        Assert.IsTrue(orangeRight.IsCued, "a member spoke: the cued one took it");
        Assert.IsNull(orangeRight.Convene(new Pose(9.0, 5.5, 0.0), right.Member(orange)));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blueLeft.TakeCue(new Pose(1.0, 1.0, 0.0), left.Member(blue))).Message, "does not start on cue");
        // a cued take whose place is off the map is refused BEFORE anything is marked (lab 19-oct: cyan in another scenario): the cue stays takeable
        var far = Born("purple");
        var farFleet = new Fleet("purple,pink,lime");
        var farRight = far.Choreography.Form("far", far.Choreography.Figure("triangle", new Position(10.9, 10.9), new Meters(2.0)), farFleet, Policy.Rank);
        farRight.StartsOnCue();
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => farRight.TakeCue(new Pose(9.0, 9.0, 0.0), farFleet.Member(far))).Message, "nowhere on the map");
        Assert.IsFalse(farRight.IsCued, "nothing marked: the cue may be taken once the golem stands on the right map");

        // A CUE HEARD AHEAD: yellow is named before its form came — kept, and the formation told later is cued by the clock's take
        var yellow = Born("yellow");
        yellow.Choreography.OverheardCue("orange");
        Assert.IsFalse(yellow.Choreography.HasPendingCue, "a cue to another golem is nobody's here");
        yellow.Choreography.OverheardCue("yellow");
        Assert.IsFalse(yellow.Choreography.HasPendingCue, "named, but it waits in no formation yet");
        var yellowRight = yellow.Choreography.Form("tri-right", yellow.Choreography.Figure("triangle", new Position(8.0, 5.5), new Meters(2.0)), right, Policy.Rank);
        yellowRight.StartsOnCue();
        Assert.IsTrue(yellow.Choreography.HasPendingCue, "now it does: the clock takes it");
        Assert.AreSame(yellowRight, yellow.Choreography.CuedFormation);
        Assert.IsFalse(yellow.Choreography.HasPendingCue, "taken once");
    }

    // THE PULL (propuesta 118, 20-oct-2026; Juan: "quiero ver que yellow esté pendiente de consultarle a alguno si ya terminó… por ask me suena bien"):
    // the right formation starts after the left by ASKING — its first golem by rank COMMITS ITSELF (propuesta 121, 21-oct-2026; paper 0B's lab: a
    // commitment to self, pending until desisted, kept by the attention when due) to ask the golem named every 5 s whether the left finished; the
    // asked one answers from its own copy; "not yet" is counted and the recurrence goes on, "yes" is the cue (TakeCue) and the recurrence's while closes it
    [TestMethod]
    public void AFormationThatStartsAfterAnotherByAsking_ArmsTheAskersRecurrence_AndTakesTheAnswerYesAsItsCue()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        Golem Born(string name) { var g = new Golem(body, name); g.Enter(new Scenario(Catalog.OpenFloor(), new Collisions())); return g; }
        var cyan = Born("cyan"); var orange = Born("orange"); var red = Born("red");
        var right = new Fleet("cyan,orange,yellow");
        var cyanRight = cyan.Choreography.Form("tri-right", cyan.Choreography.Figure("triangle", new Position(8.0, 5.5), new Meters(2.0)), right, Policy.Rank);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => cyanRight.Follows("tri-right", "red")).Message, "cannot start after itself");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => cyanRight.Follows("tri-left", "orange")).Message, "it is of this very fleet");
        cyanRight.Follows("Tri-Left", "Red");
        Assert.AreEqual("tri-left", cyanRight.After); Assert.AreEqual("red", cyanRight.AskTo); Assert.AreEqual("cyan", cyanRight.Asker, "the first by rank asks");
        Assert.IsTrue(cyanRight.OnCue, "born waiting, like one on cue"); Assert.IsFalse(cyanRight.IsCued);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => cyanRight.Convene(new Pose(7.0, 5.5, 0.0), right.Member(cyan))).Message, "starts on cue");
        Assert.AreEqual(1, cyanRight.Queue(cyanRight.Figure.Rotate(Sense.Clockwise), "s1"), "its steps wait");

        // THE RECURRENCE (propuesta 122): the asker's golem arms the engine's `remember` inside its Follows act — the domain says who asks, how often
        // (5 s unless the warden says), whether it STILL asks (the recurrence's while), and counts the questions the activations write
        Assert.IsTrue(cyanRight.IsAsker, "the first by rank"); Assert.IsTrue(cyanRight.MayAsk); Assert.IsTrue(cyanRight.StillAsks);
        Assert.AreEqual(5.0, cyanRight.EverySeconds, "every 5 s unless the warden says");
        Assert.IsTrue(cyan.Choreography.StillAsks("Tri-Right", cyanRight.Stamp));
        Assert.IsFalse(cyan.Choreography.StillAsks("tri-right", "another-stamp"), "told again under the name: the old recurrence stops by itself");
        Assert.IsFalse(cyan.Choreography.StillAsks("square-9", cyanRight.Stamp), "never told");
        cyanRight.Ask(); cyanRight.Ask();
        Assert.AreEqual(2, cyanRight.Asked, "every activation asks");
        // a member that is not the asker arms nothing and never asks
        var orangeRight = orange.Choreography.Form("tri-right", orange.Choreography.Figure("triangle", new Position(8.0, 5.5), new Meters(2.0)), right, Policy.Rank);
        orangeRight.Follows("tri-left", "red");
        Assert.IsFalse(orangeRight.IsAsker, "orange is not the asker"); Assert.IsFalse(orangeRight.MayAsk); Assert.IsFalse(orangeRight.StillAsks);
        Assert.IsFalse(orange.Choreography.StillAsks("tri-right", orangeRight.Stamp));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => orangeRight.Ask()).Message, "cyan asks, not orange");

        // red is asked: the question kept, answered on its tick from its copy — not yet, then yes once tri-left finished there
        Assert.IsTrue(red.Choreography.AskedOf("Red")); Assert.IsFalse(red.Choreography.AskedOf("cyan"));
        Assert.IsFalse(red.Choreography.Answering);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => red.Choreography.Answer()).Message, "nobody asked");
        red.Choreography.AskedAbout("tri-left", "cyan");
        Assert.IsTrue(red.Choreography.Answering);
        var notYet = red.Choreography.Answer();
        Assert.AreEqual("tri-left", notYet.Call); Assert.AreEqual("cyan", notYet.To); Assert.IsFalse(notYet.Done, "red knows no finished tri-left"); Assert.AreEqual(1, notYet.Number);
        Assert.IsFalse(red.Choreography.Answering, "answered once");
        var left = new Fleet("blue,red");
        var redLeft = red.Choreography.Form("tri-left", red.Choreography.Figure("triangle", new Position(3.0, 5.5), new Meters(2.0)), left, Policy.Rank);
        var places = redLeft.Figure.Places(2);
        redLeft.Stood(left.Member("blue"), places[0]);
        Assert.IsNotNull(redLeft.Convene(new Pose(places[1].X, places[1].Y, Math.Atan2(5.5 - places[1].Y, 3.0 - places[1].X)), left.Member(red)));
        redLeft.Placed(left.Member(red));
        redLeft.Finish(left.Member(red)); redLeft.HeardFinished(left.Member("blue"));
        Assert.IsTrue(redLeft.IsFinished);
        red.Choreography.AskedAbout("tri-left", "cyan");
        var yes = red.Choreography.Answer();
        Assert.IsTrue(yes.Done); Assert.AreEqual(2, yes.Number);

        // cyan hears the answers: 'not yet' counted, 'yes' taken as the cue — the usual take by cue, the team then following its word
        Assert.IsTrue(cyan.Choreography.AwaitsAnswer("cyan", "tri-left")); Assert.IsFalse(cyan.Choreography.AwaitsAnswer("orange", "tri-left"), "the answer names cyan");
        Assert.IsFalse(cyan.Choreography.AwaitsAnswer("cyan", "square-9"));
        Assert.AreSame(cyanRight, cyan.Choreography.FormationAfter("tri-left"));
        cyanRight.HeardNotYet();
        Assert.AreEqual(1, cyanRight.NotYet); Assert.IsTrue(cyanRight.MayAsk, "not yet: the recurrence goes on asking"); Assert.IsTrue(cyanRight.StillAsks);
        Assert.IsNull(cyanRight.TakeCue(new Pose(7.0, 5.5, 0.0), right.Member(cyan)), "yes: taken, one of three spoke");
        Assert.IsTrue(cyanRight.IsCued); Assert.IsFalse(cyanRight.MayAsk, "cued: nothing to ask"); Assert.IsFalse(cyan.Choreography.AwaitsAnswer("cyan", "tri-left"));
        Assert.IsFalse(cyanRight.StillAsks, "the answer came: the recurrence's while no longer holds"); Assert.IsFalse(cyan.Choreography.StillAsks("tri-right", cyanRight.Stamp));
        // a formation dissolved asks no more; a period of zero is refused
        var dissolved = cyan.Choreography.Form("tri-later", cyan.Choreography.Figure("triangle", new Position(8.0, 5.5), new Meters(2.0)), right, Policy.Rank);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => dissolved.Follows("tri-left", "red", new Seconds(0.0))).Message, "zero is no period");
        dissolved.Follows("tri-left", "red", new Seconds(2.0));
        Assert.AreEqual(2.0, dissolved.EverySeconds); Assert.IsTrue(dissolved.StillAsks); Assert.IsTrue(cyan.Choreography.StillAsks("tri-later", dissolved.Stamp));
        cyan.Choreography.Dissolve(dissolved);
        Assert.IsFalse(dissolved.StillAsks); Assert.IsFalse(cyan.Choreography.StillAsks("tri-later", dissolved.Stamp), "dissolved: the recurrence's while no longer holds");
        orangeRight.Stood(right.Member("cyan"), new Position(7.0, 5.5));
        Assert.IsTrue(orangeRight.IsCued, "the asker spoke: the team follows");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => cyanRight.Follows("other", "blue")).Message, "already taken");
    }

    // A GOLEM SENT ELSEWHERE, THEN THE STEP (13-oct-2026; Juan: "si le digo visit a blue estando en una posición de formación, cuando le doy
    // rotate no se posiciona donde tiene que estar… me parece un error que no vuelva a su posición"): the step is planned from where the body IS,
    // not from the vertex the formation had left it on — so the golem comes back to the figure, to the place the step gives it
    [TestMethod]
    public void AGolemSentElsewhere_ComesBackToItsNextPlace_WhenTheFormationSteps()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,red");
        var square = blue.Choreography.Form("sq", blue.Choreography.Figure("square", new Position(5.5, 5.5), new Meters(2.0)), fleet, Policy.Rank);
        var first = square.Join(new Pose(5.8, 5.8, 0.0), fleet.Member(blue));
        WalkToTheEnd(first);
        square.Placed(square.Me);
        Assert.AreEqual(6.5, blue.Standing.X, 1e-6, "blue on its place, the north-east corner");

        var errand = blue.Visit(blue.Destination, new Position(2.0, 2.0));   // the operator sends it away from the figure
        WalkToTheEnd(errand);
        Assert.AreEqual(2.0, blue.Standing.X, 1e-6);

        square.Queue(square.Figure.Rotate(Sense.Clockwise), "step-1");
        var step = square.Heard(fleet.Member("red"));
        Assert.IsNotNull(step);
        Assert.AreEqual(2.0, step.Standing.X, 1e-6, "the step starts where the body is, not on the corner it left");
        Assert.AreEqual(2.0, step.Standing.Y, 1e-6);
        step.Arrive(step.NextLeg);   // the line-up turn, toward the south-east corner from (2, 2)
        step.Go();
        Assert.AreEqual("advance", step.Order);
        Assert.AreEqual(Math.Sqrt(4.5 * 4.5 + 2.5 * 2.5), step.Amount, 1e-6, "the run from (2, 2) to the south-east corner (6.5, 4.5) — not the 2 m from the corner it had left");
        WalkToTheEnd(step);
        Assert.AreEqual(6.5, blue.Standing.X, 1e-6, "back on the figure, on the place the step gives it");
        Assert.AreEqual(4.5, blue.Standing.Y, 1e-6);
    }

    // EVERY COPY RECKONS ON THE SAME NUMBERS (13-oct-2026; lab: by distance, green's own copy held its word at full precision, the others as the
    // tell carried it, to the millimetre; on a near tie green's copy gave green the place the others gave red): green and red stand a fraction of a
    // millimetre apart in x, each nearer the east place at full precision — so, unrounded, each copy would give its own golem the east place
    [TestMethod]
    public void TwoCopiesByDistance_GiveThePlacesAlike_WhenTheWordsTravelToTheMillimetre()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var green = new Golem(body, "green");
        green.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var red = new Golem(body, "red");
        red.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("green,red");
        var greens = green.Choreography.Form("pair", "circle", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0), fleet, Policy.Distance, "g1");
        var reds = red.Choreography.Form("pair", "circle", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0), fleet, Policy.Distance, "r1");

        greens.Convene(new Pose(5.5004, 8.0, 0.0), fleet.Member(green));
        greens.Stood(fleet.Member(red), new Position(5.5, 8.0));        // red's word, as the tell carries it
        reds.Convene(new Pose(5.5002, 8.0, 0.0), fleet.Member(red));
        reds.Stood(fleet.Member(green), new Position(5.5, 8.0));        // green's word, as the tell carries it

        string Table(Formation f) => string.Join(" ", f.Holders().OrderBy(h => h.Name).Select(h => $"{h.Name}@{h.Index}"));
        Assert.AreEqual(Table(greens), Table(reds), "both copies share the places alike");
        Assert.AreEqual("green@0 red@1", Table(greens), "a tie at the millimetre: by name, green first, the east place");
    }

    // THE DISPUTES BY DISTANCE (14-oct-2026; Juan: "las disputas que ha tenido el golem para resolver una posición con otro golem y el resultado de
    // si éste cedió o la ganó él"): blue and red both stand nearest the east place; red is nearer, so the law gives it to red — in blue's copy blue
    // YIELDED it to red, in red's copy red WON it against blue; by rank there is no dispute
    [TestMethod]
    public void ByDistance_TheGolemNearestWinsThePlace_AndEachCopySaysWhoYieldedAndWhoWon()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var red = new Golem(body, "red");
        red.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,red");
        var blues = blue.Choreography.Form("pair", "circle", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0), fleet, Policy.Distance, "b1");
        var reds = red.Choreography.Form("pair", "circle", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0), fleet, Policy.Distance, "r1");
        Assert.AreEqual(0, blue.Choreography.Disputes().Count, "nobody spoke: nothing disputed yet");

        blues.Convene(new Pose(6.0, 5.5, 0.0), fleet.Member(blue));    // 0.5 m from the east place
        blues.Stood(fleet.Member(red), new Position(6.2, 5.5));         // red: 0.3 m from it
        reds.Convene(new Pose(6.2, 5.5, 0.0), fleet.Member(red));
        reds.Stood(fleet.Member(blue), new Position(6.0, 5.5));

        var lost = blue.Choreography.Disputes().Single();
        Assert.IsFalse(lost.Won, "blue yielded the east place");
        Assert.AreEqual("red", lost.Rival);
        Assert.AreEqual(0, lost.Place);
        Assert.AreEqual(6.5, lost.At.X, 1e-9);
        Assert.AreEqual(0.5, lost.Mine, 1e-9);
        Assert.AreEqual(0.3, lost.Theirs, 1e-9);
        var won = red.Choreography.Disputes().Single();
        Assert.IsTrue(won.Won, "red won it");
        Assert.AreEqual("blue", won.Rival);

        // forgotten (14-oct-2026): this copy's disputes go and it settles no more; a formation told again brings its own
        blue.Choreography.ForgetDisputes();
        Assert.AreEqual(0, blue.Choreography.Disputes().Count, "every dispute forgotten");
        blues.Stood(fleet.Member(red), new Position(6.25, 5.5));        // a corrected word re-tables the copy: still forgotten
        Assert.AreEqual(0, blue.Choreography.Disputes().Count);
        var again = blue.Choreography.Form("pair", "circle", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0), fleet, Policy.Distance, "b3");
        again.Convene(new Pose(6.0, 5.5, 0.0), fleet.Member(blue));
        again.Stood(fleet.Member(red), new Position(6.2, 5.5));
        Assert.AreEqual(1, blue.Choreography.Disputes().Count, "the formation told again: a new copy, its dispute shown");

        // by rank nobody disputes: the names sorted take the places
        var ranked = blue.Choreography.Form("ranked", "circle", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0), fleet, Policy.Rank, "b2");
        ranked.Convene(new Pose(6.0, 5.5, 0.0), fleet.Member(blue));
        ranked.Stood(fleet.Member(red), new Position(6.2, 5.5));
        Assert.AreEqual(1, blue.Choreography.Disputes().Count, "still the one of the pair told again: by rank there are none");
    }

    // ROCK, PAPER, SCISSORS (propuesta 115, 14-oct-2026; Juan: "que jueguen rock scissors paper para ver quién se queda la posición"): both want the
    // east place; whoever wins the game keeps it — every copy plays the same hands, drawn from each golem's stamp, so both copies agree on the
    // winner, and the disputes say who won and how
    [TestMethod]
    public void ByTheGame_BothCopiesPlayTheSameHands_AndTheDisputesSayWhoWonAndHow()
    {
        var hand = Throws.Of("blue-1", "blue");
        Assert.AreEqual(Throws.Length, hand.Length);
        Assert.IsTrue(hand.All(c => c is 'R' or 'P' or 'S'));
        Assert.AreEqual(hand, Throws.Of("blue-1", "blue"), "the same stamp and name, the same hand");
        Assert.IsTrue(Throws.Beats('R', 'S') && Throws.Beats('S', 'P') && Throws.Beats('P', 'R') && !Throws.Beats('S', 'R'));

        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var red = new Golem(body, "red");
        red.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,red");
        var blues = blue.Choreography.Form("pair", "circle", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0), fleet, Policy.RockPaperScissors, "blue-1");
        var reds = red.Choreography.Form("pair", "circle", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0), fleet, Policy.RockPaperScissors, "red-7");
        Assert.AreEqual(Policy.RockPaperScissors, blues.Policy);

        blues.Convene(new Pose(6.0, 5.5, 0.0), fleet.Member(blue));    // both nearest the east place
        blues.Stood(fleet.Member(red), new Position(6.2, 5.5), "red-7");
        reds.Convene(new Pose(6.2, 5.5, 0.0), fleet.Member(red));
        reds.Stood(fleet.Member(blue), new Position(6.0, 5.5), "blue-1");

        string Table(Formation f) => string.Join(" ", f.Holders().OrderBy(h => h.Name).Select(h => $"{h.Name}@{h.Index}"));
        Assert.AreEqual(Table(blues), Table(reds), "both copies played the same game");
        var bluesGame = blue.Choreography.Disputes().Single();
        var redsGame = red.Choreography.Disputes().Single();
        Assert.AreEqual(0, bluesGame.Place, "the east place was played");
        Assert.AreNotEqual(bluesGame.Won, redsGame.Won, "one won it, the other yielded it");
        Assert.AreEqual(bluesGame.Game, redsGame.Game, "the same throws decided it in both copies");
        StringAssert.Contains(bluesGame.Game, "beats");
        string winner = bluesGame.Won ? "blue" : "red";
        Assert.AreEqual($"{winner}@0", Table(blues).Split(' ').Single(t => t.StartsWith(winner)), "the winner holds the east place");
    }

    private static void WalkToTheEnd(GolemDomain.Routes.Route route)
    {
        for (int i = 0; i < 50 && route.IsPending(); i++)
        {
            if (route.Waiting) route.Go();   // the fleet's start, given here by hand: the words that give it are FormationTests' business (propuesta 106)
            route.Arrive(route.NextLeg);
        }
    }

    // A PEER'S LINE-UP HEARD EARLY (propuesta 106): its word may come before this copy opened the step (its last placed word still on the way);
    // it is kept, and this golem's own line-up then starts the fleet at once. And a touch while waiting sets the route out by itself.
    [TestMethod]
    public void AWordOfLineUpHeardBeforeTheStep_IsKept_AndATouchWhileWaiting_SetsTheRouteOut()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,red");
        var call = blue.Choreography.Form("sq", blue.Choreography.Figure("square", new Position(5.5, 5.5), new Meters(2.0)), fleet, Policy.Rank);
        var first = call.Join(new Pose(5.8, 5.8, 0.0), fleet.Member(blue));
        WalkToTheEnd(first);
        call.Placed(call.Me);
        call.Queue(call.Figure.Rotate(Sense.Clockwise), "step-1");
        Assert.AreSame(first, call.HeardAligned(fleet.Member("red"), 1), "red is lined up for round 1, a step blue has not opened yet: the word is kept in its round, nothing moves");
        Assert.AreEqual(1, call.AlignedCount, "of round 0 only blue's own: placed implies lined up (13-oct-2026); red's word is round 1's");
        var step = call.Heard(fleet.Member("red"));   // red's placed word arrives after its line-up: the step opens
        Assert.AreNotSame(first, step);
        Assert.AreEqual(1, call.Round);
        Assert.AreEqual(1, call.AlignedCount, "red's early word, in this round");
        step.Arrive(step.NextLeg);
        Assert.IsTrue(step.Waiting);
        call.Aligned(call.Me);
        Assert.IsFalse(step.Waiting, "red's early word counted: blue's own line-up starts the fleet");
        Assert.AreEqual("advance", step.Order);

        // a second step; waiting on it, a touch from behind: the correction goes first, the start is not waited for again
        WalkToTheEnd(step);
        call.Placed(call.Me);
        call.Queue(call.Figure.Rotate(Sense.Clockwise), "step-2");
        var next = call.Heard(fleet.Member("red"));
        next.Arrive(next.NextLeg);
        Assert.IsTrue(next.Waiting);
        var standing = next.Standing;
        blue.Bump(standing, Math.PI);   // pressed on its back while standing lined up
        Assert.IsFalse(next.Waiting, "touched: the route clears away from the touch by itself");
        Assert.AreEqual("advance", next.Order, "touched from behind: the clearance ahead (ajuste 75)");
        Assert.IsTrue(next.NextLeg.IsCorrection);
    }

    [TestMethod]
    public void AWordHeardBeforeConvening_IsKept_AndCountsWhenTheGolemConvenes()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,red");
        var call = blue.Choreography.Form("red-1", blue.Choreography.Figure("square", new Position(5.5, 5.5), new Meters(2.0)), fleet, Policy.Distance);

        Assert.IsNull(call.Stood(fleet.Member("red"), new Position(7.0, 7.0)), "blue has not convened: the word is only kept");
        Assert.IsFalse(call.Knows(fleet.Member(blue)));
        Assert.IsTrue(call.Knows(fleet.Member("red")));
        var route = call.Convene(new Pose(5.8, 5.8, 0.0), fleet.Member(blue));
        StringAssert.Contains(route.AsPlan(), "floor@4.5,6.5", "blue convenes knowing red is nearer to the north-east corner: straight to the north-west");
        Assert.IsTrue(call.IsComplete);
    }
}

