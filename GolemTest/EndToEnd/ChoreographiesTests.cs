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
            "the place at (11.5, 11.5) of far is nowhere on the map");
    }

    // MORE GOLEMS THAN VERTICES, BY THE WARDEN (ajuste 101, 8-oct-2026; Juan: "hagamos lo mismo en el CLI"): the formation told for how many
    // golems it lays out — the corners first, the rest on the sides (ajuste 100) — and each golem told the NUMBER of its place
    [TestMethod]
    public void AFormationToldForMoreGolemsThanVertices_HasTheCornersAndPointsOfItsSides_TakenByNumber()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));

        var square = blue.Choreography.Form("square-1", "square", new Position(5.5, 5.5), new Meters(2.0), new Degrees(0.0), 6);
        Assert.AreEqual(6, square.PlaceCount, "laid out for six golems: six places");
        Assert.AreEqual(5.5, square.PlaceNumbered(1).X, 1e-9, "place 1: the middle of the north side");
        Assert.AreEqual(6.5, square.PlaceNumbered(1).Y, 1e-9);
        Assert.AreEqual(4.5, square.PlaceNumbered(2).X, 1e-9, "place 2: the north-west corner");
        Assert.AreEqual(6.5, square.PlaceNumbered(2).Y, 1e-9);
        Assert.AreEqual(4.5, square.Vertex(2).X, 1e-9, "the vertices keep their own numbers");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => square.PlaceNumbered(6)).Message, "square-1 has places 0 to 5: there is no place 6");
        var route = blue.Choreography.Take(new Pose(5.5, 2.0, 1.5708), square, square.PlaceNumbered(4));
        StringAssert.Contains(route.AsPlan(), "face@5.5,4.5", "place 4, the middle of the south side, faced at the end");

        var few = blue.Choreography.Form("square-2", "square", new Position(5.5, 5.5), new Meters(2.0), new Degrees(0.0), 2);
        Assert.AreEqual(4, few.PlaceCount, "fewer golems than vertices: the vertices, the rest free");
        var plain = blue.Choreography.Form("square-3", "square", new Position(5.5, 5.5), new Meters(2.0), new Degrees(0.0));
        Assert.AreEqual(4, plain.PlaceCount, "told for nobody in particular: its vertices");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Form("x", "square", new Position(5.5, 5.5), new Meters(2.0), new Degrees(0.0), 0)).Message,
            "a formation is laid out for 1 to 100 bodies");
    }

    // A RING TOLD (ajuste 102, 8-oct-2026; Juan, on the double ring sent as visits: "no con el contexto del doble anillo para decirle cuál es su
    // posición"): a circle told for how many golems ride it, its place taken by number
    [TestMethod]
    public void ARingTold_HasItsPlacesByNumber_AndItsPlaceIsTakenStraight()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var red = new Golem(body, "red");
        red.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));

        var ring = red.Choreography.Form("double-ring-1-inner", "circle", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0), 2);
        Assert.AreEqual(2, ring.PlaceCount, "a ring of two");
        Assert.AreEqual(6.5, ring.PlaceNumbered(0).X, 1e-9, "place 0 due east");
        Assert.AreEqual(4.5, ring.PlaceNumbered(1).X, 1e-9, "place 1 due west");
        Assert.AreEqual(1.0, ring.Measure.InMeters, 1e-9, "a circle's measure is its radius");

        // a step of a rotation is the place of the one ahead, taken STRAIGHT (ajuste 103; Juan: "la idea es que no sigan el arco… será un tramo
        // recto hacia la posición del otro"): no point between
        var plan = red.Choreography.Take(new Pose(6.5, 5.5, 3.1416), ring, ring.PlaceNumbered(1)).AsPlan();
        Assert.AreEqual("floor@4.5,5.5 > face@4.5,5.5", plan.Split('\n').First(l => l.Contains("floor@")).Trim().Replace("way: ", ""), "one straight run to the place, then facing the centre: " + plan);

        var turned = red.Choreography.Form("double-ring-1-outer", "circle", new Position(5.5, 5.5), new Meters(2.5), new Degrees(90.0), 2);
        Assert.AreEqual(8.0, turned.PlaceNumbered(0).Y, 1e-9, "a ring turned 90°: its first place due north");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => red.Choreography.Form("c", "circle", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0))).Message,
            "a circle's places depend on how many bodies take it");
    }
}
