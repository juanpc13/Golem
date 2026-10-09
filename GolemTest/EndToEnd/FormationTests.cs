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
        var blueCall = blue.Choreography.Form("red-1", blue.Choreography.Figure("square", center, side), fleet, "distance");
        var redCall = red.Choreography.Form("red-1", red.Choreography.Figure("square", center, side), fleet, "distance");
        Assert.AreEqual("distance", blueCall.Policy);

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
        Assert.AreEqual(2, blue.Current.Collisions.PeersInTheWay().Count, "red, where it stood and where it goes, is a body in blue's way while blue's route lasts (ajuste 80)");

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
        Assert.AreNotSame(blueCall, blue.Choreography.Form("red-2", blue.Choreography.Figure("square", center, side), fleet, "distance"), "another name, another formation");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blueCall.Stood(fleet.Member(blue), new Position(1.0, 1.0))).Message, "its own word is its Convene");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Form("red-3", blue.Choreography.Figure("square", center, side), fleet, "luck")).Message, "by 'rank' or by 'distance'");
    }

    [TestMethod]
    public void AWordToARouteAlreadyWalked_OpensTheNextAtOnce_FromWhereTheLastArrivalLeftTheBody()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,red");
        var call = blue.Choreography.Form("red-1", blue.Choreography.Figure("square", new Position(5.5, 5.5), new Meters(2.0)), fleet, "distance");
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
        var call = green.Choreography.Form("red-1", green.Choreography.Figure("square", new Position(5.5, 5.5), new Meters(2.0)), fleet, "distance");
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
        var call = blue.Choreography.Form("red-1", square, fleet, "rank");
        Assert.AreEqual("rank", call.Policy);

        // yellow stands in the middle of blue's column, by the south-east corner; blue comes from the south, straight up x = 6.5
        Assert.IsNull(call.Stood(fleet.Member("yellow"), new Position(6.5, 4.5)), "yellow's word, before blue joined: kept");
        var route = call.Join(new Pose(6.5, 2.5, 1.5708), fleet.Member(blue));
        var berths = call.Berths();
        Assert.AreEqual(2, berths.Count, "yellow where it stands and where it goes: " + string.Join(", ", berths.Select(b => b.Who + "@" + b.Center.X + "," + b.Center.Y)));
        Assert.AreEqual(2, collisions.PeersInTheWay().Count, "both in blue's way while its route lasts");
        Assert.IsTrue(route.LegsAhead.Count > 2, "not the straight run up the column through yellow: the way bends around it — " + route.AsPlan());
        Assert.IsTrue(route.LegsAhead.All(l => Math.Max(Math.Abs(l.At.X - 6.5), Math.Abs(l.At.Y - 4.5)) > 0.6), "no leg ends inside yellow's berth — " + route.AsPlan());
        StringAssert.Contains(route.AsPlan(), "floor@6.5,6.5", "and it still ends on blue's own corner");
        WalkToTheEnd(route);
        Assert.AreEqual("completed", route.Status);
        Assert.AreEqual(0, collisions.PeersInTheWay().Count, "the route ended: the peers have moved on, as bodies do");

        // a peer standing on MY place is no berth (by rank a golem may stand on another's corner): the route still opens
        var other = new Golem(body, "yellow");
        other.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var otherCall = other.Choreography.Form("red-1", square, fleet, "rank");
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
        var call = blue.Choreography.Form("red-1", square, fleet, "rank");
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
        var call = blue.Choreography.Form("red-1", blue.Choreography.Figure("square", new Position(5.5, 5.5), new Meters(2.0)), fleet, "distance");
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
        var call = blue.Choreography.Form("red-1", square, fleet, "rank");

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
        var greens = green.Choreography.Form("red-1", square, fleet, "rank");
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
        var square = blue.Choreography.Form("sq", "square", new Position(5.5, 5.5), new Meters(2.0), new Degrees(0.0), fleet, "rank");
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
    private static void WalkToTheEnd(GolemDomain.Routes.Route route)
    {
        for (int i = 0; i < 50 && route.IsPending(); i++) route.Arrive(route.NextLeg);
    }

    [TestMethod]
    public void AWordHeardBeforeConvening_IsKept_AndCountsWhenTheGolemConvenes()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,red");
        var call = blue.Choreography.Form("red-1", blue.Choreography.Figure("square", new Position(5.5, 5.5), new Meters(2.0)), fleet, "distance");

        Assert.IsNull(call.Stood(fleet.Member("red"), new Position(7.0, 7.0)), "blue has not convened: the word is only kept");
        Assert.IsFalse(call.Knows(fleet.Member(blue)));
        Assert.IsTrue(call.Knows(fleet.Member("red")));
        var route = call.Convene(new Pose(5.8, 5.8, 0.0), fleet.Member(blue));
        StringAssert.Contains(route.AsPlan(), "floor@4.5,6.5", "blue convenes knowing red is nearer to the north-east corner: straight to the north-west");
        Assert.IsTrue(call.IsComplete);
    }
}

