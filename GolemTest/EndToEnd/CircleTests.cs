using GolemDomain;
using GolemDomain.Formations;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Robots;
using GolemDomain.Routes;
using GolemDomain.Scenarios;
using GolemDomain.Touches;
using GolemDomain.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// THE FORMATION THE FLEET TAKES (propuesta 59, paso 1, 28-sep-2026; Juan: "comandar una coreografía… un círculo… cada uno decide el vértice
// que quiera tomar"): a circle of places around a centre, the fleet's names sorted into ranks, and the golem's Join — its place by its
// rank, a route to it decided inside. Deterministic: every golem alone computes the same places and ranks.
[TestClass]
public class CircleTests
{
    [TestMethod]
    public void ThreeBodies_TakeATriangle_EachItsOwnPlace_ByItsRankAmongTheNames()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body);
        g.Enter(new Scenario(map, collisions));
        var circle = new Circle(new Position(5.5, 5.5), new Meters(1.0));
        var fleet = new Fleet("red,blue,green");

        Assert.AreEqual(3, fleet.Count);
        CollectionAssert.AreEqual(new[] { "blue", "green", "red" }, fleet.Names.ToList(), "the names sorted: the ranks every golem computes alone");
        Assert.AreEqual(0, fleet.Member("blue").Rank);
        Assert.AreEqual(2, fleet.Member("Red").Rank, "a name in any case");
        Assert.AreEqual(3, fleet.Member("red").Of);

        var places = circle.Places(3);
        Assert.AreEqual(6.5, places[0].X, 0.001, "the first place due east of the centre");
        Assert.AreEqual(5.5, places[0].Y, 0.001);
        Assert.AreEqual(places[0].DistanceTo(places[1]), places[1].DistanceTo(places[2]), 0.001, "three places evenly spaced: a triangle");
        Assert.AreEqual(places[1].DistanceTo(places[2]), places[2].DistanceTo(places[0]), 0.001);

        var red = g.Choreography.Join(new Pose(2.5, 2.5, 0.0), circle, fleet.Member("red"));
        StringAssert.EndsWith(red.AsPlan(), "center@5,4.63", "red, third of three: the place at 240 degrees — from the living room, the door crossed on the way (Dash): " + red.AsPlan());
        var blue = g.Choreography.Join(new Pose(0.5, 0.5, 0.0), circle, fleet.Member("blue"));
        StringAssert.EndsWith(blue.AsPlan(), "center@6.5,5.5", "blue, first: due east");
        var green = g.Choreography.Join(new Pose(9.0, 9.5, 0.0), circle, fleet.Member("green"));
        StringAssert.EndsWith(green.AsPlan(), "center@5,6.37", "green, second: at 120 degrees");
        Assert.AreEqual("door by door", red.Navigation.Name, "a formation is joined by a route born door by door, like every route (ajuste 62)");
        g.Strategy.OnTheWay.Activate();
        var again = g.Choreography.Join(new Pose(2.5, 2.5, 0.0), circle, fleet.Member("red"));
        Assert.AreEqual("door by door", again.Navigation.Name, "born door by door whatever the golem adopted: the script asks the strategy and improves it");
        Assert.IsTrue(g.Strategy.OnTheWay.IsActive, "what the script's if asks");
        Assert.AreEqual("on the way", g.Dash(again).Navigation.Name, "the route improved with a dash, as the script does it");
    }

    [TestMethod]
    public void APlaceOffTheMap_OrWithoutRoom_ANameOutsideTheFleet_AndAnEmptyCircle_AreRefused()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body);
        g.Enter(new Scenario(map, collisions));

        var fleet = new Fleet("red,blue");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => fleet.Member("green")).Message, "'green' is not in the fleet (blue, red)");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Circle(new Position(5.5, 5.5), new Meters(0.0))).Message, "a radius greater than zero");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Fleet(" , ")).Message, "at least one golem's name");

        // a circle in the west corridor (1.5 m wide) with a radius of 1 m: its places fall in the walls or the blocks
        var tight = new Circle(new Position(0.75, 5.5), new Meters(1.0));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Choreography.Join(new Pose(2.5, 2.5, 0.0), tight, fleet.Member("blue"))).Message,
            "place 1 of 2 of the circle at (1.75, 5.5)");
        // a circle whose place lies outside the map altogether
        var outside = new Circle(new Position(11.0, 5.5), new Meters(2.0));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Choreography.Join(new Pose(2.5, 2.5, 0.0), outside, fleet.Member("blue"))).Message, "is nowhere on the map");
        Assert.AreEqual(0, g.Routes().Count, "nothing minted for a refused place");
    }

    // THE ROTATION (paso 3): the effect's stages are the next places in the sense of the turn, as many as fit in its duration at the
    // body's cruise speed; a join with a turn is one route — the place, then every stage.
    [TestMethod]
    public void ARotation_TurnsTheFleetAroundTheCircle_OnePlaceAtATime_ForAsLongAsItLasts()
    {
        var map = Catalog.OpenFloor();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body);
        g.Enter(new Scenario(map, collisions));
        var circle = new Circle(new Position(5.5, 5.5), new Meters(1.0));
        var fleet = new Fleet("red,blue,green");
        var red = fleet.Member("red");                                                // rank 2 of 3: the place at 240 degrees

        var clockwise = new Rotation("clockwise", new Seconds(10.0));
        var stages = circle.Stages(red, clockwise, 2.0);
        Assert.AreEqual(11, stages.Count, "the run between neighbouring places is 1.73 m, 0.87 s at 2 m/s: eleven fit in ten seconds");
        Assert.AreEqual(5.0, stages[0].X, 0.01, "clockwise from 240 degrees: 120 degrees first — the ranks step down");
        Assert.AreEqual(6.37, stages[0].Y, 0.01);
        Assert.AreEqual(6.5, stages[1].X, 0.01, "then due east");
        Assert.AreEqual(5.0, stages[2].X, 0.01, "then back to its own place at 240 degrees");
        Assert.AreEqual(4.63, stages[2].Y, 0.01);
        var counter = circle.Stages(red, new Rotation("counter-clockwise", new Seconds(1.0)), 2.0);
        Assert.AreEqual(1, counter.Count, "one stage at least, however short the turn");
        Assert.AreEqual(6.5, counter[0].X, 0.01, "counter-clockwise from 240 degrees: due east first — the ranks step up");
        Assert.AreEqual(0, circle.Stages(new Fleet("red").Member("red"), clockwise, 2.0).Count, "a fleet of one has nowhere to turn to");

        var route = g.Choreography.Join(new Pose(2.5, 2.5, 0.0), circle, red, clockwise);
        Assert.AreEqual(12, route.StopsLeft, "its own place, then the eleven stages, one route");
        StringAssert.StartsWith(route.AsPlan(), "floor@5,4.63 > floor@5,6.37 > floor@6.5,5.5 > floor@5,4.63", "around and around");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Rotation("sideways", new Seconds(10.0))).Message, "turns 'clockwise' or 'counterclockwise', not 'sideways'");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Rotation("clockwise", new Seconds(0.0))).Message, "lasts longer than nothing");
    }
}
