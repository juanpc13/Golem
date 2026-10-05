using GolemDomain;
using GolemDomain.Coordination;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Robots;
using GolemDomain.Scenarios;
using GolemDomain.Touches;
using GolemDomain.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest.EndToEnd;

// THE GOLEM OBEYS A PLACE (propuesta 88): it answers a call with where it stands, takes the place the warden's table gives it — the
// others as berths (ajuste 80), the route ending facing the centre (ajuste 79) — says when it stands on it, and yields when the warden
// moves it while an order is in the body (propuesta 74).
[TestClass]
public class PlacementTests
{
    [TestMethod]
    public void TheGolemTakesItsPlaceFromTheTable_TheOthersAsBerths_FacingTheCentre_AndSaysWhenItStandsOnIt()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        var collisions = new Collisions();
        blue.Enter(new Scenario(Catalog.OpenFloor(), collisions));
        blue.Wake(new Pose(6.5, 2.5, 1.5708));   // blue comes from the south, straight up x = 6.5
        var centre = new Position(5.5, 5.5);

        // the call answered once
        blue.Choreography.Stood("red-1", blue.Destination);
        Assert.IsTrue(blue.Choreography.Answered("red-1"));
        Assert.IsFalse(blue.Choreography.Answered("red-2"));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Stood("red-1", blue.Destination)).Message, "said where it stood for the call red-1 already");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Current).Message, "was given no place yet");

        // the table: yellow stands in the middle of blue's column, by the south-east corner, and goes north-west; blue goes north-east
        var given = new Assignments("blue@6.5,2.5>6.5,6.5;yellow@6.5,4.5>4.5,6.5");
        Assert.IsTrue(given.Has(blue));
        Assert.IsFalse(given.Has(new Golem(body, "green")), "a golem of no place in the table");
        var route = blue.Choreography.Take("red-1", 0, given, centre);
        Assert.AreEqual(2, collisions.PeersInTheWay().Count, "yellow where it stands and where it goes: bodies in blue's way while its route lasts");
        Assert.IsTrue(route.LegsAhead.Count > 2, "not the straight run up the column through yellow: the way bends around it — " + route.AsPlan());
        Assert.IsTrue(route.LegsAhead.All(l => Math.Max(Math.Abs(l.At.X - 6.5), Math.Abs(l.At.Y - 4.5)) > 0.6), "no leg ends inside yellow's berth — " + route.AsPlan());
        StringAssert.EndsWith(route.AsPlan(), "floor@6.5,6.5 > face@6.5,6.5", "blue's own corner, then facing the centre: " + route.AsPlan());
        Assert.AreEqual(0.0, route.FacesToward.DistanceTo(centre), 1e-9);
        var placement = blue.Choreography.Current;
        Assert.AreEqual("red-1", placement.Call);
        Assert.AreEqual(0, placement.Round);
        Assert.AreSame(route, placement.Route);
        Assert.IsFalse(placement.Reached, "not yet: the route is underway");
        Assert.IsFalse(blue.Choreography.Reached(route));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => placement.Placed()).Message, "does not stand on its place");

        // the same table again changes nothing
        Assert.AreSame(route, blue.Choreography.Take("red-1", 0, new Assignments(given.AsText()), centre));

        WalkToTheEnd(route);
        Assert.AreEqual("completed", route.Status);
        Assert.AreEqual(0, collisions.PeersInTheWay().Count, "the route ended: the peers have moved on, as bodies do");
        Assert.IsTrue(placement.Reached);
        Assert.IsTrue(blue.Choreography.Reached(route));
        placement.Placed();
        Assert.IsTrue(placement.Said);
        Assert.IsFalse(placement.Reached, "said: nothing to say again");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => placement.Placed()).Message, "said so already");

        // a golem with no place in a table is refused; a place off the map too
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Golem(body, "green").Choreography.Take("red-1", 0, given, centre)).Message, "'green' has no place in the call red-1");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Take("red-9", 0, new Assignments("blue@6.5,6.5>12,6.5"), centre)).Message, "is nowhere on the map");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Take("red-9", 0, null, centre)).Message, "'given' was not given");
    }

    [TestMethod]
    public void AnotherPlaceWhileAnOrderIsInTheBody_MakesTheRouteYield_AndHaltedOpensTheNextFromWhereTheBodyStopped()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        blue.Wake(new Pose(5.8, 5.8, 0.0));
        var centre = new Position(5.5, 5.5);
        var first = blue.Choreography.Take("red-1", 0, new Assignments("blue@5.8,5.8>4.5,6.5;red@7,7>6.5,6.5"), centre);
        StringAssert.Contains(first.AsPlan(), "floor@4.5,6.5");
        Assert.AreEqual(1, blue.Routes().Count);

        // the warden shares the round again with blue elsewhere — while blue's body carries an order: the route yields
        var moved = blue.Choreography.Take("red-1", 0, new Assignments("blue@5.8,5.8>6.5,6.5;red@7,7>4.5,6.5"), centre);
        Assert.AreSame(first, moved, "no route yet: the order in course was measured from a pose blue no longer knows");
        Assert.IsTrue(first.Yielding);
        Assert.AreEqual("pending", first.Status);
        Assert.AreEqual("stop", first.Order, "the route asks the body to stop and say where it stood");
        Assert.IsFalse(first.IsWalkable);
        Assert.AreSame(first, blue.Dash(first), "a route that yields is replaced, not improved");

        // the body stopped half-way, turned a little: the new route opens from THERE
        var next = blue.Choreography.Halted(new Pose(5.6, 5.6, 0.4));
        Assert.AreNotSame(first, next);
        Assert.AreEqual("abandoned", first.Status);
        StringAssert.Contains(first.Why, "the warden moved blue to (6.5, 6.5)");
        StringAssert.Contains(next.AsPlan(), "floor@6.5,6.5");
        Assert.AreEqual(0.0, next.Standing.DistanceTo(new Position(5.6, 5.6)), 1e-9, "measured from where the body stopped");
        Assert.AreEqual(0.4, next.Standing.Heading, 1e-9, "…facing the way it stopped");
        Assert.AreSame(next, blue.Choreography.Current.Route);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Halted(new Pose(5.6, 5.6, 0.4))).Message, "nothing to halt");

        // the route walked: a new place in the same round opens at once from the corner the last arrival left the body on
        WalkToTheEnd(next);
        Assert.AreEqual("completed", next.Status);
        var again = blue.Choreography.Take("red-1", 0, new Assignments("blue@5.8,5.8>4.5,4.5;red@7,7>6.5,6.5"), centre);
        Assert.AreNotSame(next, again, "nothing to stop: the route to the next place opens at once");
        Assert.AreEqual("completed", next.Status, "a route walked stays walked");
        Assert.AreEqual(0.0, again.Standing.DistanceTo(new Position(6.5, 6.5)), 1e-9);
        StringAssert.Contains(again.AsPlan(), "floor@4.5,4.5");

        // the next round is another placement: from where the last one left the body
        WalkToTheEnd(again);
        var stepped = blue.Choreography.Take("red-1", 1, new Assignments("blue@4.5,4.5>6.5,4.5;red@6.5,6.5>4.5,6.5"), centre);
        Assert.AreEqual(1, blue.Choreography.Current.Round);
        Assert.AreEqual(0.0, stepped.Standing.DistanceTo(new Position(4.5, 4.5)), 1e-9);
        StringAssert.Contains(stepped.AsPlan(), "floor@6.5,4.5 > face@6.5,4.5");
    }

    // THE FACING (ajuste 79): a route to a place ends with the body turning to face the centre, so every step costs every body the same
    // turn and the fleet moves in lockstep from the first step on.
    [TestMethod]
    public void ARouteToAPlace_EndsFacingTheCentre_AndAStepCostsTheSameTurn()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        blue.Wake(new Pose(6.5, 10.4, -1.5708));   // from the north, down x = 6.5, to the north-east corner: it arrives facing south
        var centre = new Position(5.5, 5.5);
        var route = blue.Choreography.Take("red-1", 0, new Assignments("blue@6.5,10.4>6.5,6.5"), centre);
        Assert.IsTrue(route.LegsAhead.Last().IsFacing);
        Assert.AreEqual(1, route.StopsLeft, "the facing is no stop");
        Assert.AreEqual("advance", route.Order, "already facing south: straight to the corner");
        route.Arrive(route.NextLeg);
        Assert.IsTrue(route.IsPending(), "on the corner, the route is not done: it faces the centre first");
        Assert.AreEqual("turnRight", route.Order, "from south (−90°) to the centre at −135°: a right turn");
        Assert.AreEqual(Math.PI / 4, route.Amount, 1e-3, "45 degrees");
        Assert.IsFalse(blue.Choreography.Current.Reached, "not placed until it faces the centre");
        route.Arrive(route.NextLeg);
        Assert.AreEqual("completed", route.Status, "the turn made, the route is complete: no advance asked");
        Assert.AreEqual(-3 * Math.PI / 4, route.Standing.Heading, 1e-3, "facing the centre");
        Assert.IsTrue(blue.Choreography.Current.Reached);

        // the step: clockwise from the north-east corner is south along the east side, 45° from facing the centre
        blue.Choreography.Current.Placed();
        var step = blue.Choreography.Take("red-1", 1, new Assignments("blue@6.5,6.5>6.5,4.5"), centre);
        Assert.AreEqual(Math.PI / 4, step.Amount, 1e-3, "45° from facing the centre to along the side: " + step.Order);
        StringAssert.EndsWith(step.AsPlan(), "floor@6.5,4.5 > face@6.5,4.5", "and the step ends facing the centre too");

        // a body already facing the centre when it reaches its place completes at once
        var green = new Golem(body, "green");
        green.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        green.Wake(new Pose(3.5, 7.5, -0.7854));   // from the north-west diagonal, facing the centre
        var toNorthWest = green.Choreography.Take("red-1", 0, new Assignments("green@3.5,7.5>4.5,6.5"), centre);
        toNorthWest.Arrive(toNorthWest.NextLeg);
        Assert.AreEqual("completed", toNorthWest.Status, "already facing the centre on arrival: nothing to turn");
    }

    private static void WalkToTheEnd(GolemDomain.Routes.Route route)
    {
        for (int i = 0; i < 50 && route.IsPending(); i++) route.Arrive(route.NextLeg);
    }
}
