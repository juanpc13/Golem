using GolemCli.Choreography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemCliTests;

[TestClass]
public class ChoreographyTests
{
    [TestMethod]
    public void ASquare_HasItsCornersFromTheNorthEast_CounterClockwise_SaidByItsSide()
    {
        var square = new Square(new Spot(5.5, 5.5), 2.0);
        var v = square.Vertices();
        Assert.AreEqual(4, v.Count);
        Assert.AreEqual(new Spot(6.5, 6.5), v[0], "north-east first");
        Assert.AreEqual(new Spot(4.5, 6.5), v[1], "then north-west");
        Assert.AreEqual(new Spot(4.5, 4.5), v[2], "south-west");
        Assert.AreEqual(new Spot(6.5, 4.5), v[3], "south-east");
        Assert.AreEqual(2.0, v[0].DistanceTo(v[1]), 1e-9, "the side");
        StringAssert.Contains(Assert.ThrowsException<ArgumentException>(() => square.Places(5)).Message, "4 places");
        StringAssert.Contains(Assert.ThrowsException<ArgumentException>(() => new Square(new Spot(0, 0), 0)).Message, "side above zero");
        Assert.AreEqual("pentagon", Figure.Named("Pentagon", new Spot(5.5, 5.5), 2.0).Name);
        Assert.AreEqual(5, Figure.Named("pentagon", new Spot(5.5, 5.5), 2.0).Places(4).Count, "four on a pentagon: the five vertices, one free");
        Assert.AreEqual(3, new Circle(new Spot(5.5, 5.5), 1.0).Places(3).Count);
    }

    [TestMethod]
    public void ByRank_SharesThePlacesInTheNamesOrder_WhateverTheMarks()
    {
        var fleet = new Fleet(new[] { new Member("yellow", new Spot(3, 3)), new Member("blue", new Spot(6, 6.2)), new Member("red", null), new Member("green", null) });
        CollectionAssert.AreEqual(new[] { "blue", "green", "red", "yellow" }, fleet.Names.ToList(), "sorted: the rank");
        var shared = new ByRank().Share(fleet, new Square(new Spot(5.5, 5.5), 2.0).Places(4));
        Assert.AreEqual(0, shared["blue"]); Assert.AreEqual(1, shared["green"]); Assert.AreEqual(2, shared["red"]); Assert.AreEqual(3, shared["yellow"]);
    }

    [TestMethod]
    public void ByDistance_GivesEachTheNearestPlace_ThePairsShortestFirst_AndNeedsEverybodysPosition()
    {
        // the lab's dispute: blue and red both nearest the north-east corner; blue wins it by the shorter pair, red takes the next
        var fleet = new Fleet(new[] { new Member("blue", new Spot(6, 6.2)), new Member("red", new Spot(7, 6.3)), new Member("green", new Spot(3.5, 7.5)), new Member("yellow", new Spot(3, 3)) });
        var places = new Square(new Spot(5.5, 5.5), 2.0).Places(4);
        var shared = new ByDistance().Share(fleet, places);
        Assert.AreEqual(0, shared["red"], "red at (7, 6.3) is nearest the north-east corner (6.5, 6.5)");
        Assert.AreEqual(1, shared["green"], "green: north-west");
        Assert.AreEqual(2, shared["yellow"], "yellow: south-west");
        Assert.AreEqual(3, shared["blue"], "blue lost the north-east to red and takes the south-east");
        var blind = new Fleet(new[] { new Member("blue", null), new Member("red", new Spot(1, 1)) });
        StringAssert.Contains(Assert.ThrowsException<ArgumentException>(() => new ByDistance().Share(blind, places)).Message, "unknown: blue");
    }

    [TestMethod]
    public void AChoreography_WritesEveryGolemsScript_ItsPlace_ThenASyncAndTheNextPlacePerStep()
    {
        var fleet = new Fleet(new[] { new Member("blue", null), new Member("red", null), new Member("green", null), new Member("yellow", null) });
        var square = new Choreography(new Square(new Spot(5.5, 5.5), 2.0), fleet, new ByRank(), 2, clockwise: true);
        // one errand (Juan: "con visit de todos los puntos calculados cubre parte de completar una coreografía"): one line per golem
        var errands = square.Scripts(Pace.OneErrand);
        Assert.AreEqual(4, errands.Count);
        Assert.IsTrue(errands["blue"][0].StartsWith("# square at 5.5,5.5, by rank, 2 step(s) clockwise, 4 golem(s), one errand"), errands["blue"][0]);
        Assert.AreEqual("visit 6.5,6.5 6.5,4.5 4.5,4.5", errands["blue"][1], "blue: north-east, then clockwise down the order: south-east, south-west");
        Assert.AreEqual("visit 4.5,6.5 6.5,6.5 6.5,4.5", errands["green"][1], "green, second: north-west, then north-east");
        Assert.AreEqual(2, errands["blue"].Count, "a note and one visit");

        // rounds: a visit per place, a @sync between
        var rounds = square.Scripts(Pace.Rounds);
        CollectionAssert.AreEqual(new[] { "visit 6.5,6.5", "@sync", "visit 6.5,4.5", "@sync", "visit 4.5,4.5" }, rounds["blue"].Skip(1).ToList());
        Assert.AreEqual(8, rounds.Values.Sum(s => s.Count(l => l == Choreography.Sync)), "two syncs per golem");

        var counter = new Choreography(new Square(new Spot(5.5, 5.5), 2.0), fleet, new ByRank(), 1, clockwise: false);
        Assert.AreEqual("visit 6.5,6.5 4.5,6.5", counter.Scripts()["blue"][1], "counter-clockwise: blue from the north-east to the north-west");

        // four on a pentagon: the free vertex moves with the fleet
        var pentagon = new Choreography(new Pentagon(new Spot(5.5, 5.5), 2.0), fleet, new ByRank(), 1, clockwise: true);
        Assert.AreEqual(pentagon.Places[4], pentagon.PlaceAt("blue", 1), "blue from the north vertex into the free north-east one");
        StringAssert.Contains(Assert.ThrowsException<ArgumentException>(() => new Choreography(square.Figure, fleet, new ByRank(), 201, true)).Message, "0 to 200");
    }
}
