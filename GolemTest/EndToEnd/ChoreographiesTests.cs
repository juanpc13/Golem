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

// THE FORMATIONS THE WARDEN NAMES (propuesta 99, 8-oct-2026; Juan: "darle contexto al golem pero nunca darle la coordenada exacta de su
// visit"): the golem is told a formation — its name, figure, centre, side and orientation — and which VERTEX it takes; where that vertex
// stands, it resolves by itself, on its own map, and it arrives facing the centre.
[TestClass]
public class ChoreographiesTests
{
    [TestMethod]
    public void AFormationTold_IsKeptByItsName_AndItsVertexByNumber_StandsWhereTheFigureSays()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));

        var square = blue.Choreography.Form("Square-1", "square", new Position(5.5, 5.5), new Meters(2.0), new Degrees(0.0));
        Assert.AreEqual("square-1", square.Called, "the name the warden gave it, lower case");
        Assert.AreSame(square, blue.Choreography.Find("square-1"), "found again by its name");
        Assert.IsTrue(blue.Choreography.HasFormation("SQUARE-1"));
        Assert.AreEqual(6.5, square.Vertex(0).X, 1e-9); Assert.AreEqual(6.5, square.Vertex(0).Y, 1e-9);   // vertex 0: the north-east corner
        Assert.AreEqual(4.5, square.Vertex(2).X, 1e-9); Assert.AreEqual(4.5, square.Vertex(2).Y, 1e-9);   // vertex 2: the south-west one
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => square.Vertex(4)).Message, "a square has vertices 0 to 3: there is no vertex 4");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Find("pentagon-7")).Message, "the golem was told no formation named 'pentagon-7': form it first");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Form("c", "circle", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0))).Message, "a circle's places depend on how many bodies take it");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Form("h", "hexagon", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0))).Message, "square, pentagon or triangle");
    }

    [TestMethod]
    public void AFormationTurned_KeepsTheOrderOfItsVertices_AndToldAgain_IsMadeAgain()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));

        var turned = blue.Choreography.Form("square-1", "square", new Position(5.5, 5.5), new Meters(2.0), new Degrees(45.0));
        Assert.AreEqual(5.5, turned.Vertex(0).X, 1e-9, "the north-east corner, turned 45°, stands due north");
        Assert.AreEqual(5.5 + Math.Sqrt(2), turned.Vertex(0).Y, 1e-9);
        Assert.AreEqual(5.5 - Math.Sqrt(2), turned.Vertex(1).X, 1e-9, "the next one, due west: the order unchanged");

        var moved = blue.Choreography.Form("square-1", "square", new Position(3.0, 3.0), new Meters(2.0), new Degrees(0.0));
        Assert.AreSame(moved, blue.Choreography.Find("square-1"), "told again by the same name: made again, the old one gone");
        Assert.AreEqual(1, blue.Choreography.Formations().Count);
        Assert.AreEqual(4.0, moved.Vertex(0).X, 1e-9); Assert.AreEqual(4.0, moved.Vertex(0).Y, 1e-9);
        Assert.AreEqual(5.5 + Math.Sqrt(2), turned.Vertex(0).Y, 1e-9, "a route already given keeps the formation it was given by");
    }

    [TestMethod]
    public void TheVertexTaken_IsAnErrandOfTheGolem_EndingFacingTheCentre_AndRefusedOffTheMap()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));

        var square = blue.Choreography.Form("square-1", "square", new Position(5.5, 5.5), new Meters(2.0), new Degrees(0.0));
        var route = blue.Choreography.Take(new Pose(5.5, 2.0, 1.5708), square, square.Vertex(3));
        StringAssert.Contains(route.AsPlan(), "floor@6.5,4.5", "vertex 3, the south-east corner: the golem resolved where it stands");
        StringAssert.Contains(route.AsPlan(), "face@6.5,4.5", "and arrives facing the centre, as in its own choreography");

        var far = blue.Choreography.Form("far", "square", new Position(10.5, 10.5), new Meters(2.0), new Degrees(0.0));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Take(new Pose(5.5, 2.0, 1.5708), far, far.Vertex(0))).Message,
            "the vertex at (11.5, 11.5) of far is nowhere on the map");
    }
}
