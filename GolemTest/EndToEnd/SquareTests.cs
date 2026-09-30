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

// THE SQUARE (29-sep-2026; Juan: "agrega la figura de cuadro… deja 4 golems"), said by its SIDE (ajuste 65, 30-sep-2026; Juan: "el
// cuadrado maneja radius, ¿no debería ser lateral/largo del cuadro?"): its sides square to the map, the first corner north-east; four
// bodies take the corners, eight the corners and the middles of the sides.
[TestClass]
public class SquareTests
{
    [TestMethod]
    public void FourBodies_TakeTheCorners_EightTheCornersAndTheMiddles_TheSidesSquareToTheMap()
    {
        var square = new Square(new Position(5.5, 5.5), new Meters(2.0));
        var v = square.Vertices();
        Assert.AreEqual(6.5, v[0].X, 0.001, "the first corner north-east, half the side from the centre each way");
        Assert.AreEqual(6.5, v[0].Y, 0.001);
        Assert.AreEqual(4.5, v[1].X, 0.001, "then north-west, counter-clockwise");
        Assert.AreEqual(6.5, v[1].Y, 0.001);
        Assert.AreEqual(2.0, v[0].DistanceTo(v[1]), 0.001, "the side, as it was said");
        Assert.AreEqual(Math.Sqrt(2), square.Circumradius, 0.001, "the circle through the corners is the square's own: the side over √2");
        Assert.AreEqual(v[0].Y, v[1].Y, 0.001, "the north side level: the sides are square to the map");

        var four = square.Places(4);
        for (int k = 0; k < 4; k++) Assert.AreEqual(0.0, four[k].DistanceTo(v[k]), 0.001, "four bodies: the corners");
        var eight = square.Places(8);
        Assert.AreEqual(0.0, eight[1].DistanceTo(new Position(5.5, 6.5)), 0.001, "eight: the middle of the north side between the first two corners");
        Assert.AreEqual(8, eight.Select(p => (Math.Round(p.X, 3), Math.Round(p.Y, 3))).Distinct().Count());
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Square(new Position(5.5, 5.5), new Meters(0.0))).Message, "a square needs a side greater than zero");
    }

    [TestMethod]
    public void AFleetOfFour_TakesTheSquare_ByRank_AndTurnsAlongItsSides()
    {
        var map = Catalog.OpenFloor();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body);
        g.Enter(new Scenario(map, collisions));
        var square = new Square(new Position(5.5, 5.5), new Meters(2.0));
        var fleet = new Fleet("blue,red,green,yellow");
        CollectionAssert.AreEqual(new[] { "blue", "green", "red", "yellow" }, fleet.Names.ToList());

        // by rank: the names sorted, the corners counter-clockwise from the north-east — wherever each body stands
        StringAssert.EndsWith(g.Choreography.Join(new Pose(2.5, 2.5, 0.0), square, fleet.Member("blue")).AsPlan(), "floor@6.5,6.5", "blue, first: the north-east corner");
        StringAssert.EndsWith(g.Choreography.Join(new Pose(2.5, 2.5, 0.0), square, fleet.Member("green")).AsPlan(), "floor@4.5,6.5", "green, second: north-west");
        StringAssert.EndsWith(g.Choreography.Join(new Pose(2.5, 2.5, 0.0), square, fleet.Member("red")).AsPlan(), "floor@4.5,4.5", "red, third: south-west");
        StringAssert.EndsWith(g.Choreography.Join(new Pose(2.5, 2.5, 0.0), square, fleet.Member("yellow")).AsPlan(), "floor@6.5,4.5", "yellow, fourth: the south-east corner");
        var turning = g.Choreography.Join(new Pose(2.5, 2.5, 0.0), square, fleet.Member("green"), new Rotation("clockwise", new Seconds(10.0)));
        Assert.AreEqual(11, turning.StopsLeft, "its corner and ten stages: a side of 2 m is 1 s at 2 m/s");
        StringAssert.StartsWith(turning.AsPlan(), "floor@4.5,6.5 > floor@6.5,6.5 > floor@6.5,4.5", "green from the north-west corner clockwise: north-east, then south-east");
    }

    // THE FORMATION BY NAME (ajuste 69; Juan: "el módulo asociado a coreografías… g.coreografia.formacion('cuadrado'), y ese creará el
    // objeto… que puede ser cualquier forma al final"): the golem's choreographies module makes it, with its natural measure; the golem
    // joins it whatever shape it is.
    [TestMethod]
    public void TheGolemsChoreographies_MakeTheFormationByName_WithItsMeasure_AndTheGolemJoinsIt()
    {
        var map = Catalog.OpenFloor();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body);
        g.Enter(new Scenario(map, collisions));
        var center = new Position(5.5, 5.5);
        var side = new Meters(2.0);

        var formation = g.Choreography.Formation("square", center, side);
        Assert.IsInstanceOfType(formation, typeof(Square), "square: a square");
        Assert.AreEqual(2.0, ((Square)formation).Side.InMeters, 1e-9, "said by its side");
        Assert.IsInstanceOfType(g.Choreography.Formation("Circle", center, new Meters(1.0)), typeof(Circle), "any case; a circle by its radius");
        Assert.IsInstanceOfType(g.Choreography.Formation("triangle", center, side), typeof(Triangle));
        CollectionAssert.AreEqual(new[] { "square", "triangle", "circle" }, g.Choreography.Names().ToList());
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Choreography.Formation("hexagon", center, side)).Message, "knows no formation named 'hexagon'");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Choreography.Formation("square", null, side)).Message, "'center' was not given");

        var fleet = new Fleet("blue,red,green,yellow");
        var route = g.Choreography.Join(new Pose(2.5, 2.5, 0.0), formation, fleet.Member("red"));
        StringAssert.EndsWith(route.AsPlan(), "floor@4.5,4.5", "red, third: the south-west corner of the square the module made");
    }
}

