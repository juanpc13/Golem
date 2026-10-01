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

// THE TRIANGLE (propuesta 59, 29-sep-2026; Juan: "la coreografía para un triángulo también… en total 5 golems"): an equilateral
// triangle said by its side (ajuste 65), its apex north, and the fleet spread evenly along its perimeter — three bodies take the
// vertices, five stand a fifth of the perimeter apart. Joined and turned like the circle: the formation's own places. Set aside from
// the command line for now (ajuste 65: the square alone); kept in the repertoire. A side of 2√3 is the triangle whose corners stand 2 m
// from the centre.
[TestClass]
public class TriangleTests
{
    [TestMethod]
    public void ThreeBodies_TakeTheVertices_FiveSpreadAlongTheSides_EachOnThePerimeter()
    {
        var triangle = new Triangle(new Position(5.5, 5.5), new Meters(2.0 * Math.Sqrt(3)));
        var v = triangle.Vertices();
        Assert.AreEqual(2.0, triangle.Circumradius, 0.001, "the circle through the vertices: the side over √3");
        Assert.AreEqual(5.5, v[0].X, 0.001, "the apex due north of the centre");
        Assert.AreEqual(7.5, v[0].Y, 0.001);
        Assert.AreEqual(v[0].DistanceTo(v[1]), v[1].DistanceTo(v[2]), 0.001, "equilateral");
        Assert.AreEqual(2.0 * Math.Sqrt(3), v[0].DistanceTo(v[1]), 0.001, "the side, as it was said");

        var three = triangle.Places(3);
        Assert.AreEqual(3, three.Count);
        for (int k = 0; k < 3; k++) Assert.AreEqual(0.0, three[k].DistanceTo(v[k]), 0.001, "three bodies: the vertices, the apex first, counter-clockwise");

        var five = triangle.Places(5);
        Assert.AreEqual(5, five.Count);
        Assert.AreEqual(0.0, five[0].DistanceTo(v[0]), 0.001, "the first at the apex");
        double spacing = 3 * v[0].DistanceTo(v[1]) / 5;
        Assert.AreEqual(spacing, five[0].DistanceTo(five[1]), 0.001, "the second a fifth of the perimeter down the first side");
        foreach (var place in five)
        {
            double d = place.DistanceTo(triangle.Center);
            Assert.IsTrue(d <= 2.0 + 1e-9 && d >= 1.0 - 1e-9, "every place on the perimeter: between the inradius (1) and the radius (2), found " + d);
        }
        Assert.AreEqual(5, five.Select(p => (Math.Round(p.X, 3), Math.Round(p.Y, 3))).Distinct().Count(), "five different places");
    }

    [TestMethod]
    public void AFleetOfFive_JoinsTheTriangle_EachAtItsOwnPlace_AndTurnsAlongItsSides()
    {
        var map = Catalog.OpenFloor();
        var collisions = new Collisions();
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body);
        g.Enter(new Scenario(map, collisions));
        var triangle = new Triangle(new Position(5.5, 5.5), new Meters(2.0 * Math.Sqrt(3)));
        var fleet = new Fleet("blue,red,green,yellow,purple");
        CollectionAssert.AreEqual(new[] { "blue", "green", "purple", "red", "yellow" }, fleet.Names.ToList());

        var places = new HashSet<string>();
        foreach (var name in fleet.Names)
            places.Add(g.Choreography.Join(new Pose(2.5, 2.5, 0.0), g.Choreography.Muster("call-" + name, triangle, fleet), fleet.Member(name)).AsPlan());
        Assert.AreEqual(5, places.Count, "five golems, five places of the triangle");
        StringAssert.Contains(g.Choreography.Join(new Pose(2.5, 2.5, 0.0), g.Choreography.Muster("again-blue", triangle, fleet), fleet.Member("blue")).AsPlan(), "floor@5.5,7.5", "blue, first: the apex");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Triangle(new Position(5.5, 5.5), new Meters(0.0))).Message, "a side greater than zero");
    }
}
