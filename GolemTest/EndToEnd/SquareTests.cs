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
    public void AFleetOfFour_TakesTheSquare_ByRank()
    {
        var map = Catalog.OpenFloor();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body);
        g.Enter(new Scenario(map, collisions));
        var square = new Square(new Position(5.5, 5.5), new Meters(2.0));
        var fleet = new Fleet("blue,red,green,yellow");
        CollectionAssert.AreEqual(new[] { "blue", "green", "red", "yellow" }, fleet.Names.ToList());

        // by rank: the names sorted, the corners counter-clockwise from the north-east — wherever each body stands (one convocation per
        // member here: a golem takes one place per call)
        GolemDomain.Routes.Route AsMember(string name)
        {
            var taken = g.Choreography.Muster("call-" + name, square, fleet).Join(new Pose(2.5, 2.5, 0.0), fleet.Member(name));
            taken.Abandon("this golem plays the next member now");   // its route ended: the berths of the others move on (ajuste 80)
            return taken;
        }
        StringAssert.Contains(AsMember("blue").AsPlan(), "floor@6.5,6.5", "blue, first: the north-east corner");
        StringAssert.Contains(AsMember("green").AsPlan(), "floor@4.5,6.5", "green, second: north-west");
        StringAssert.Contains(AsMember("red").AsPlan(), "floor@4.5,4.5", "red, third: south-west");
        StringAssert.Contains(AsMember("yellow").AsPlan(), "floor@6.5,4.5", "yellow, fourth: the south-east corner");
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
        Assert.IsInstanceOfType(g.Choreography.Formation("pentagon", center, side), typeof(Pentagon), "the pentagon (ajuste 85)");
        CollectionAssert.AreEqual(new[] { "square", "pentagon", "triangle", "circle" }, g.Choreography.Names().ToList());
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Choreography.Formation("hexagon", center, side)).Message, "knows no formation named 'hexagon'");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => g.Choreography.Formation("square", null, side)).Message, "'center' was not given");

        var fleet = new Fleet("blue,red,green,yellow");
        var red = new Golem(body, "Red");                                          // born with its name (ajuste 72)
        red.Enter(new Scenario(map, collisions));
        Assert.AreEqual("red", red.Name, "lower case, like a fleet's names");
        Assert.IsTrue(fleet.Has(red), "the golem is in the fleet: it joins when it hears a call");
        Assert.IsFalse(fleet.Has(new Golem(body, "purple")), "a golem of another fleet");
        Assert.IsFalse(fleet.Has(g), "a golem born without a name is in no fleet");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => fleet.Member(g)).Message, "a golem without a name is in no fleet");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Golem(body, " ")).Message, "a golem's name must be a name");
        Assert.AreEqual(2, fleet.Member(red).Rank, "its member, by the name it was born with: blue, green, red, yellow");
        var route = red.Choreography.Muster("red-1", formation, fleet).Join(new Pose(2.5, 2.5, 0.0), fleet.Member(red));
        StringAssert.Contains(route.AsPlan(), "floor@4.5,4.5", "red, third: the south-west corner of the square the module made");
    }
}

