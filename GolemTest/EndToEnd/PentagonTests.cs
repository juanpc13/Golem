using GolemDomain;
using GolemDomain.Formations;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Robots;
using GolemDomain.Scenarios;
using GolemDomain.Touches;
using GolemDomain.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest.EndToEnd;

// THE PENTAGON (ajuste 85, 2-oct-2026; Juan: "agrega el pentágono como figura… quiero ver cómo se forman las figuras de pentágono con 4
// golems"): the regular pentagon of that side, its first vertex north; the fleet spread along its perimeter, so four bodies stand on the
// north vertex and three points of the sides, and a step is the next place along the perimeter, whatever its count.
[TestClass]
public class PentagonTests
{
    [TestMethod]
    public void FourBodies_TakeFourVerticesOfAPentagon_LeaveTheFifthFree_AndAStepMovesTheHoleWithThem()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var g = new Golem(body, "blue");
        g.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var center = new Position(5.5, 5.5);
        var pentagon = (Pentagon)g.Choreography.Formation("pentagon", center, new Meters(2.0));
        Assert.AreEqual("pentagon", pentagon.Name);
        Assert.AreEqual(2.0 / (2 * Math.Sin(Math.PI / 5)), pentagon.Circumradius, 1e-9, "the side over twice the sine of 36°: 1.70 m");

        // the vertices: north first, then counter-clockwise
        var v = pentagon.Vertices();
        Assert.AreEqual(5, v.Count);
        Assert.AreEqual(0.0, v[0].DistanceTo(new Position(5.5, 7.2013)), 1e-3, "the apex due north");
        Assert.AreEqual(0.0, v[1].DistanceTo(new Position(3.8820, 6.0257)), 1e-3, "north-west");
        Assert.AreEqual(0.0, v[2].DistanceTo(new Position(4.5, 4.1236)), 1e-3, "south-west");
        Assert.AreEqual(0.0, v[3].DistanceTo(new Position(6.5, 4.1236)), 1e-3, "south-east");
        Assert.AreEqual(0.0, v[4].DistanceTo(new Position(7.1180, 6.0257)), 1e-3, "north-east");
        Assert.AreEqual(2.0, v[0].DistanceTo(v[1]), 1e-9, "a side of two metres");

        // five take the vertices; four take the first four BY RANK and leave the north-east one FREE (ajuste 86): the figure still reads as a pentagon
        var five = pentagon.Places(5);
        for (int i = 0; i < 5; i++) Assert.AreEqual(0.0, five[i].DistanceTo(v[i]), 1e-9, $"five bodies: vertex {i + 1}");
        var four = pentagon.Places(4);
        Assert.AreEqual(5, four.Count, "four bodies, five places: the one nobody takes stays free");
        for (int i = 0; i < 5; i++) Assert.AreEqual(0.0, four[i].DistanceTo(v[i]), 1e-9, "the places are the vertices, in their order");
        Assert.AreEqual(6, pentagon.Places(6).Count, "more bodies than vertices: spread along the perimeter (ajuste 65)");

        // the fleet of four takes it by rank, every route ending facing the centre; a step moves everybody one vertex — and the hole with them
        var fleet = new Fleet("blue,green,red,yellow");
        var call = g.Choreography.Muster("red-1", pentagon, fleet);
        var route = call.Join(new Pose(5.5, 9.0, -1.5708), fleet.Member(g));
        StringAssert.Contains(route.AsPlan(), "floor@5.5,7.2 > face@5.5,7.2", "blue, first by rank: the north vertex, then facing the centre: " + route.AsPlan());
        Assert.AreEqual(0, call.PlaceIndex);
        Assert.AreEqual(0.0, pentagon.Place(fleet.Member("yellow")).DistanceTo(v[3]), 1e-9, "yellow, last of four: the south-east vertex; the north-east stays free");
        Assert.AreEqual(4, pentagon.Rotate("clockwise").Next(0, 5), "clockwise from the north vertex: the north-east one — the free place, taken");
        Assert.AreEqual(1, pentagon.Rotate("counterclockwise").Next(0, 5), "counter-clockwise: the north-west one");
        call.Placed(call.Me); foreach (var name in new[] { "green", "red", "yellow" }) call.Heard(fleet.Member(name));   // one golem plays all: everybody placed
        call.Queue(pentagon.Rotate("clockwise"), "s1");
        var step = call.Step();
        Assert.AreEqual(4, call.PlaceIndex, "blue steps into the free vertex; its own, the north, is the free one now");
        StringAssert.Contains(step.AsPlan(), "floor@7.12,6.03", "the north-east vertex: " + step.AsPlan());
    }
}
