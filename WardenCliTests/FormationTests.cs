using WardenCli.Formations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WardenCliTests;

[TestClass]
public class FormationTests
{
    [TestMethod]
    public void AFormation_IsBornWhereTheChoreographyLeavesEverybody_AndNamesEachVertexByItsCompassPoint()
    {
        var fleet = new Fleet(new[] { new Member("blue", null), new Member("red", null), new Member("green", null), new Member("yellow", null) });
        var square = new Square(new Spot(5.5, 5.5), 2.0);
        var laidOut = new Choreography(square, fleet, new ByRank(), 1, clockwise: true);
        var formation = laidOut.Outcome(1);
        Assert.AreEqual("square #1", formation.Name);
        // by rank blue NE(0), green NW(1), red SW(2), yellow SE(3); one step clockwise is one place DOWN the order
        Assert.AreEqual(3, formation.IndexOf("blue")); Assert.AreEqual("SE", square.Label(formation.IndexOf("blue"), 4));
        Assert.AreEqual(0, formation.IndexOf("green")); Assert.AreEqual("NE", square.Label(0, 4));
        Assert.AreEqual(1, formation.IndexOf("red")); Assert.AreEqual(2, formation.IndexOf("yellow"));
        Assert.AreEqual(new Spot(6.5, 4.5), formation.PlaceOf("blue"));
        Assert.AreEqual("green NE · red NW · yellow SW · blue SE", formation.Holders);
        Assert.AreEqual(0, formation.Turned, "a formation starts counting its own rotations");
        Assert.AreEqual("N", new Pentagon(new Spot(5.5, 5.5), 2.0).Label(0, 5));
        Assert.AreEqual("E", new Pentagon(new Spot(5.5, 5.5), 2.0).Label(4, 5), "18 degrees is nearer due east than the north-east");
        Assert.AreEqual("W", new Circle(new Spot(5.5, 5.5), 1.0).Label(2, 4));
    }

    [TestMethod]
    public void ARotation_MovesEveryoneOneVertexInTheSense_WritesEachVisit_AndKeepsTheVerticesHeld()
    {
        var fleet = new Fleet(new[] { new Member("blue", null), new Member("red", null), new Member("green", null), new Member("yellow", null) });
        var square = new Square(new Spot(5.5, 5.5), 2.0);
        var formation = new Choreography(square, fleet, new ByRank(), 0, clockwise: true).Outcome(1);
        Assert.AreEqual("blue NE · green NW · red SW · yellow SE", formation.Holders);

        var lines = formation.Rotate(Sense.Clockwise);
        Assert.AreEqual("visit 6.5,4.5", lines["blue"], "blue from NE to SE: down the order");
        Assert.AreEqual("visit 6.5,6.5", lines["green"], "green from NW to NE");
        Assert.AreEqual("green NE · red NW · yellow SW · blue SE", formation.Holders, "everybody one vertex down the order");
        Assert.AreEqual(-1, formation.Turned);

        var back = formation.Rotate(Sense.Counterclockwise, 2);
        Assert.AreEqual("visit 6.5,6.5 4.5,6.5", back["blue"], "two steps up the order, the way through both vertices");
        Assert.AreEqual(1, formation.IndexOf("blue"));
        Assert.AreEqual(1, formation.Turned);
        StringAssert.Contains(Assert.ThrowsException<ArgumentException>(() => formation.Rotate(Sense.Clockwise, 0)).Message, "1 to 200");
    }

    [TestMethod]
    public void AMove_TakesTheFigureElsewhere_EveryoneKeepsItsVertex_AndGetsTheVisitToItThere()
    {
        var fleet = new Fleet(new[] { new Member("blue", null), new Member("red", null), new Member("green", null), new Member("yellow", null) });
        var square = new Square(new Spot(5.5, 5.5), 2.0);
        var formation = new Choreography(square, fleet, new ByRank(), 0, clockwise: true).Outcome(1);
        formation.Rotate(Sense.Clockwise);
        Assert.AreEqual("green NE · red NW · yellow SW · blue SE", formation.Holders);

        var lines = formation.Move(new Spot(4.0, 5.0));
        Assert.AreEqual(new Spot(4.0, 5.0), formation.Figure.Center);
        Assert.AreEqual(2.0, ((Square)formation.Figure).Side, "the same square, elsewhere");
        Assert.AreEqual("visit 5,4", lines["blue"], "blue keeps the south-east corner, now at 5,4");
        Assert.AreEqual("visit 5,6", lines["green"], "green keeps the north-east");
        Assert.AreEqual("green NE · red NW · yellow SW · blue SE", formation.Holders, "nobody changes vertex");
        Assert.AreEqual(-1, formation.Turned, "the rotations counted stay");
        Assert.AreEqual(1.5, ((Circle)new Circle(new Spot(5.5, 5.5), 1.5).At(new Spot(2.0, 2.0))).Radius, "a circle moves with its radius");
    }

    [TestMethod]
    public void FourOnAPentagon_TheFreeVertexMovesWithTheFleet()
    {
        var fleet = new Fleet(new[] { new Member("blue", null), new Member("red", null), new Member("green", null), new Member("yellow", null) });
        var pentagon = new Pentagon(new Spot(5.5, 5.5), 2.0);
        var formation = new Choreography(pentagon, fleet, new ByRank(), 0, clockwise: true).Outcome(2);
        Assert.AreEqual("pentagon #2", formation.Name);
        Assert.IsNull(formation.HolderOf(4), "the north-east vertex is free by rank");
        formation.Rotate(Sense.Counterclockwise);
        Assert.IsNull(formation.HolderOf(0), "the hole moved one vertex up the order with everybody");
        Assert.AreEqual("yellow", formation.HolderOf(4));
    }

    [TestMethod]
    public void AFormation_RefusesTwoGolemsOnOneVertex_AndAVertexTheFigureDoesNotHave()
    {
        var fleet = new Fleet(new[] { new Member("blue", null), new Member("red", null) });
        var square = new Square(new Spot(5.5, 5.5), 2.0);
        StringAssert.Contains(Assert.ThrowsException<ArgumentException>(() => new Formation(1, square, fleet, new Dictionary<string, int> { ["blue"] = 0, ["red"] = 0 })).Message, "same vertex");
        StringAssert.Contains(Assert.ThrowsException<ArgumentException>(() => new Formation(1, square, fleet, new Dictionary<string, int> { ["blue"] = 0, ["red"] = 4 })).Message, "has 4");
        StringAssert.Contains(Assert.ThrowsException<ArgumentException>(() => new Formation(1, square, fleet, new Dictionary<string, int> { ["blue"] = 0 })).Message, "holds no vertex");
    }
}
