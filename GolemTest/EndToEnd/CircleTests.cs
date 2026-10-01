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

        var red = g.Choreography.Muster("call-red", circle, fleet).Join(new Pose(2.5, 2.5, 0.0), fleet.Member("red"));
        StringAssert.Contains(red.AsPlan(), "center@5,4.63", "red, third of three: the place at 240 degrees — from the living room, the door crossed on the way (Dash): " + red.AsPlan());
        red.Abandon("this golem plays the next member now");   // its route ended: the berths of the others move on (ajuste 80)
        var blue = g.Choreography.Muster("call-blue", circle, fleet).Join(new Pose(0.5, 0.5, 0.0), fleet.Member("blue"));
        StringAssert.Contains(blue.AsPlan(), "center@6.5,5.5", "blue, first: due east");
        blue.Abandon("this golem plays the next member now");
        var green = g.Choreography.Muster("call-green", circle, fleet).Join(new Pose(9.0, 9.5, 0.0), fleet.Member("green"));
        StringAssert.Contains(green.AsPlan(), "center@5,6.37", "green, second: at 120 degrees");
        green.Abandon("this golem plays the next member now");
        Assert.AreEqual("door by door", red.Navigation.Name, "a formation is joined by a route born door by door, like every route (ajuste 62)");
        g.Strategy.OnTheWay.Activate();
        var again = g.Choreography.Muster("call-red-2", circle, fleet).Join(new Pose(2.5, 2.5, 0.0), fleet.Member("red"));   // another call: a golem takes one place per call
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
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Choreography.Muster("tight", tight, fleet).Join(new Pose(2.5, 2.5, 0.0), fleet.Member("blue"))).Message,
            "place 1 of 2 of the circle at (1.75, 5.5)");
        // a circle whose place lies outside the map altogether
        var outside = new Circle(new Position(11.0, 5.5), new Meters(2.0));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Choreography.Muster("outside", outside, fleet).Join(new Pose(2.5, 2.5, 0.0), fleet.Member("blue"))).Message, "is nowhere on the map");
        Assert.AreEqual(0, g.Routes().Count, "nothing minted for a refused place");
    }
}
