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
    public void AMove_TakesTheFigureElsewhere_EveryoneKeepsItsVertex_AndTheShotWritesTheVisitToItThere()
    {
        var fleet = new Fleet(new[] { new Member("blue", null), new Member("red", null), new Member("green", null), new Member("yellow", null) });
        var square = new Square(new Spot(5.5, 5.5), 2.0);
        var formation = new Choreography(square, fleet, new ByRank(), 0, clockwise: true).Outcome(1);
        formation.Rotate(Sense.Clockwise);
        Assert.AreEqual("green NE · red NW · yellow SW · blue SE", formation.Holders);

        Assert.IsTrue(formation.Unshot, "a step is a draft until the shot (8-oct-2026)");
        formation.Shot();
        Assert.IsFalse(formation.Unshot, "laid out, stepped and shot: its lines are on the tabs");
        formation.Move(new Spot(4.0, 5.0));
        Assert.IsTrue(formation.Unshot, "a move is a draft until the shot");
        var lines = formation.Shot();
        Assert.IsFalse(formation.Unshot, "the shot wrote it");
        Assert.AreEqual(new Spot(4.0, 5.0), formation.Figure.Center);
        Assert.AreEqual(2.0, ((Square)formation.Figure).Side, "the same square, elsewhere");
        Assert.AreEqual("visit 5,4", lines["blue"], "blue keeps the south-east corner, now at 5,4");
        Assert.AreEqual("visit 5,6", lines["green"], "green keeps the north-east");
        Assert.AreEqual("green NE · red NW · yellow SW · blue SE", formation.Holders, "nobody changes vertex");
        Assert.AreEqual(-1, formation.Turned, "the rotations counted stay");
        Assert.AreEqual(1.5, ((Circle)new Circle(new Spot(5.5, 5.5), 1.5).At(new Spot(2.0, 2.0))).Radius, "a circle moves with its radius");
    }

    [TestMethod]
    public void AFigure_TurnsAndResizes_EveryoneKeepsItsVertex_AndTheCompassFollows()
    {
        var fleet = new Fleet(new[] { new Member("blue", null), new Member("red", null), new Member("green", null), new Member("yellow", null) });
        var square = new Square(new Spot(5.5, 5.5), 2.0);
        var formation = new Choreography(square, fleet, new ByRank(), 0, clockwise: true).Outcome(1);
        Assert.AreEqual("blue NE · green NW · red SW · yellow SE", formation.Holders);

        formation.Orient(45);
        var turned = formation.Shot();
        Assert.AreEqual("visit 5.5,6.914", turned["blue"], "blue's corner, the north-east, turned 45° stands due north");
        Assert.AreEqual("blue N · green W · red S · yellow E", formation.Holders, "nobody changes vertex; the compass follows the turn");
        Assert.AreEqual(45.0, formation.Figure.Angle);
        Assert.AreEqual(0, formation.Turned, "a turn of the figure is no step of the fleet");

        formation.Resize(4.0);
        var sized = formation.Shot();
        Assert.AreEqual("visit 5.5,8.328", sized["blue"], "a side of 4: the corner 2.83 m from the centre");
        Assert.AreEqual(4.0, ((Square)formation.Figure).Side);
        Assert.AreEqual(45.0, formation.Figure.Angle, "a resize keeps the orientation");
        StringAssert.Contains(Assert.ThrowsException<ArgumentException>(() => formation.Resize(0)).Message, "side above zero");
        formation.Orient(90);
        formation.Rotate(Sense.Clockwise);
        // a DRAFT (8-oct-2026): the steps wait for the shot, which writes them in order, each with the place it leaves everybody on
        Assert.IsTrue(formation.Unshot, "turned, then a step drafted: both wait for the shot");
        Assert.AreEqual(1, formation.Drafted.Count);
        Assert.AreEqual("  · 1 step(s) to shoot, reshaped", formation.DraftText);
        formation.Shot();
        Assert.IsFalse(formation.Unshot, "shot: nothing waits");
        Assert.AreEqual(0, formation.Drafted.Count);

        Assert.AreEqual(2.0, new Square(new Spot(0, 0), 2.0).MeasureFor(Math.Sqrt(2)), 1e-9, "the side that puts the corners that far");
        Assert.AreEqual(0.0, new Square(new Spot(0, 0), 2.0, 360).Angle, "an orientation is kept in [0, 360)");
        Assert.AreEqual(new Spot(5.5, 6.5), new Circle(new Spot(5.5, 5.5), 1.0, 90).Places(4)[0], "a circle's first place turns too");
    }

    [TestMethod]
    public void AFormation_IsToldToTheGolems_ByItsName_AndEachOneItsVertexByNumber_NeverACoordinate()
    {
        var fleet = new Fleet(new[] { new Member("blue", null), new Member("red", null), new Member("green", null), new Member("yellow", null) });
        var laidOut = new Choreography(new Square(new Spot(5.5, 5.5), 2.0), fleet, new ByRank(), 1, clockwise: true);
        var formation = laidOut.Outcome(1);
        Assert.IsTrue(formation.CanForm);
        Assert.AreEqual("square-1", formation.WireName);
        Assert.AreEqual("form square-1 square --center 5.5,5.5 --side 2 --angle 0", formation.FormLine());
        Assert.AreEqual("take square-1 --place 3", formation.TakeLine("blue"), "blue, one step clockwise from the north-east: place 3");

        var scripts = laidOut.TakeScripts(formation.WireName, formation.FormLine(), Pace.Rounds);
        CollectionAssert.AreEqual(new[] { "form square-1 square --center 5.5,5.5 --side 2 --angle 0", "take square-1 --place 0", Choreography.Sync, "take square-1 --place 3" },
                                  scripts["blue"].ToList(), "told the formation, then its vertex round by round: no coordinate");
        Assert.IsFalse(scripts.Values.SelectMany(l => l).Any(l => l.StartsWith("visit")));

        formation.Orient(45);
        Assert.AreEqual("form square-1 square --center 5.5,5.5 --side 2 --angle 45", formation.FormLine(), "a turn is told as the figure's orientation");
        var circle = new Choreography(new Circle(new Spot(5.5, 5.5), 1.0), fleet, new ByRank(), 0, clockwise: true).Outcome(2);
        Assert.IsTrue(circle.CanForm, "a circle is told too, for how many ride it (ajuste 102)");
        Assert.AreEqual("form circle-2 circle --center 5.5,5.5 --radius 1 --angle 0 --places 4", circle.FormLine());
    }

    // MORE GOLEMS THAN VERTICES (ajuste 101, 8-oct-2026; Juan: "hagamos lo mismo en el CLI"): the console lays them out by the golem's own
    // rule (ajuste 100) — the corners first, the rest on the sides — tells the golems how many, and each one the number of its place
    [TestMethod]
    public void FourOnATriangle_TakeTheCornersAndTheMiddleOfTheFirstSide_AndTheGolemsAreToldHowMany()
    {
        var fleet = new Fleet(new[] { new Member("blue", null), new Member("red", null), new Member("green", null), new Member("yellow", null) });
        var triangle = new Triangle(new Spot(5.5, 5.5), 2.0);
        var formation = new Choreography(triangle, fleet, new ByRank(), 0, clockwise: true).Outcome(1);
        var places = formation.Places;
        Assert.AreEqual(4, places.Count, "three corners and one more");
        var v = triangle.Vertices();
        Assert.AreEqual(v[0], places[0], "the apex first");
        Assert.AreEqual(new Spot(Math.Round((v[0].X + v[1].X) / 2, 3), Math.Round((v[0].Y + v[1].Y) / 2, 3)), places[1], "then the middle of the first side");
        Assert.AreEqual(v[1], places[2]); Assert.AreEqual(v[2], places[3], "and every corner taken");
        Assert.AreEqual("blue N · green NW · red SW · yellow SE", formation.Holders, "the middle of the north-west side, between the apex and the next corner");
        Assert.AreEqual("form triangle-1 triangle --center 5.5,5.5 --side 2 --angle 0 --places 4", formation.FormLine(), "the golems are told for how many");
        Assert.AreEqual("take triangle-1 --place 1", formation.TakeLine("green"), "green the place of a side, by its number");

        formation.Rotate(Sense.Counterclockwise);
        Assert.AreEqual("yellow N · blue NW · green SW · red SE", formation.Holders, "a step moves everybody one place round the same set");

        var six = new Square(new Spot(5.5, 5.5), 2.0).Places(6);
        CollectionAssert.AreEqual(new[] { new Spot(6.5, 6.5), new Spot(5.5, 6.5), new Spot(4.5, 6.5), new Spot(4.5, 4.5), new Spot(5.5, 4.5), new Spot(6.5, 4.5) }, six.ToList(),
            "six on a square: the corners and the middles of the north and south sides, as the golem's module lays them");
        var square = new Choreography(new Square(new Spot(5.5, 5.5), 2.0), fleet, new ByRank(), 0, clockwise: true).Outcome(2);
        Assert.AreEqual("form square-2 square --center 5.5,5.5 --side 2 --angle 0", square.FormLine(), "no more golems than vertices: nothing more to say");
    }

    // THE DOUBLE RING (8-oct-2026; Juan: "una formación que pregunte por los diámetros de los dos anillos y que se pueda rotar el superior y el
    // inferior… seleccionar cuáles serían los golems del anillo inferior y superior"): two rings round one centre, each turning on its own
    [TestMethod]
    public void ADoubleRing_HasTheGolemsOnTheirRings_AndEachRingTurnsAlone_Straight()
    {
        var rings = Formation.DoubleRing(1, new Spot(5.5, 5.5), 5.0, 2.0, new[] { "green", "blue" }, new[] { "yellow", "red" });
        Assert.AreEqual("double ring #1", rings.Name);
        Assert.IsTrue(rings.IsDoubleRing);
        Assert.IsTrue(rings.CanForm, "told ring by ring, each a circle of the golem's (ajuste 102)");
        Assert.AreEqual("outer: blue E · green W — inner: red E · yellow W", rings.Holders, "on each ring the names sorted from due east");
        Assert.AreEqual(Ring.Inner, rings.RingOf("red"));
        Assert.AreEqual("visit 8,5.5", rings.Shot()["blue"], "the outer ring, its diameter 5");
        Assert.AreEqual("visit 4.5,5.5", rings.Shot()["yellow"], "the inner ring, its diameter 2");

        // told by context (ajuste 102): each golem its own ring, a circle of the golem's, and the number of its place on it
        Assert.AreEqual("form double-ring-1-outer circle --center 5.5,5.5 --radius 2.5 --angle 0 --places 2", rings.FormLine("green"));
        Assert.AreEqual("form double-ring-1-inner circle --center 5.5,5.5 --radius 1 --angle 0 --places 2", rings.FormLine("red"));
        Assert.AreEqual("take double-ring-1-inner --place 1", rings.TakeLine("yellow"), "yellow the second place of the inner ring");
        var inner = rings.Rotate(Sense.Clockwise, 1, Ring.Inner);
        CollectionAssert.AreEquivalent(new[] { "red", "yellow" }, inner.Keys.ToList(), "the inner ring alone: the outer golems get no line");
        Assert.AreEqual("visit 4.5,5.5", inner["red"], "straight to the place of the one ahead (ajuste 103)");
        Assert.AreEqual("visit 6.5,5.5", inner["yellow"]);
        var outer = rings.Rotate(Sense.Counterclockwise, 1, Ring.Outer);
        Assert.AreEqual("visit 3,5.5", outer["blue"], "the outer ring the other way round, straight too");
        Assert.AreEqual("outer: green E · blue W — inner: yellow E · red W", rings.Holders);
        Assert.AreEqual("  outer 1 · inner -1", rings.TurnedText);
        Assert.AreEqual("take double-ring-1-inner --place 1", rings.TakeLine("red"), "a step: the place on its ring, no sense, straight (ajuste 103)");

        StringAssert.Contains(Assert.ThrowsException<ArgumentException>(() => Formation.DoubleRing(2, new Spot(5.5, 5.5), 5.0, 2.0, new[] { "blue" }, new[] { "blue" })).Message, "a golem rides one ring");
        StringAssert.Contains(Assert.ThrowsException<ArgumentException>(() => Formation.DoubleRing(2, new Spot(5.5, 5.5), 2.0, 5.0, new[] { "blue" }, new[] { "red" })).Message, "wider than the inner one");
        var square = new Choreography(new Square(new Spot(5.5, 5.5), 2.0), new Fleet(new[] { new Member("blue", null) }), new ByRank(), 0, clockwise: true).Outcome(3);
        StringAssert.Contains(Assert.ThrowsException<ArgumentException>(() => square.Rotate(Sense.Clockwise, 1, Ring.Inner)).Message, "has no rings");
        // the rings turn TOGETHER (8-oct-2026; Juan: "que giraran al mismo tiempo"; "retoma lo del giro en sync"): two steps of each ring, clicked
        // in any order, are two rounds with a step of each — the n-th of the inner ring and the n-th of the outer one set out at once
        rings.Shot();
        rings.Rotate(Sense.Clockwise, 1, Ring.Inner);
        rings.Rotate(Sense.Clockwise, 1, Ring.Inner);
        rings.Rotate(Sense.Counterclockwise, 1, Ring.Outer);
        rings.Rotate(Sense.Counterclockwise, 1, Ring.Outer);
        var rounds = rings.Rounds();
        Assert.AreEqual(2, rounds.Count, "four steps, two rounds");
        CollectionAssert.AreEquivalent(new Ring?[] { Ring.Inner, Ring.Outer }, rounds[0].Select(s => s.Ring).ToList(), "each round turns both rings");
        CollectionAssert.AreEquivalent(new Ring?[] { Ring.Inner, Ring.Outer }, rounds[1].Select(s => s.Ring).ToList());
        rings.Rotate(Sense.Counterclockwise);
        Assert.AreEqual(3, rings.Rounds().Count, "a step of the whole figure takes a round of its own, after the rings' steps");
        rings.Shot();
        Assert.IsFalse(rings.Unshot, "shot: the steps went to the tabs");
        rings.Resize(2.0);   // its measure is the outer radius: a diameter of 4
        Assert.AreEqual(1.6, ((DoubleRing)rings.Figure).InnerDiameter, 1e-9, "resized, the inner ring keeps its proportion");
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
