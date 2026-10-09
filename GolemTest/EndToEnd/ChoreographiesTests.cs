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

// THE FORMATIONS ARE THE GOLEM'S (propuesta 104, 8-oct-2026; Juan: "las formaciones le pertenecerán al golem… a los otros golems
// involucrados se les proporcionará la misma formación… el CLI sólo servirá como interfaz"): the module keeps every formation by NAME — a
// figure with its fleet, its policy and the place every member holds — made, made again, found, listed and dissolved; who holds what is
// the golem's to resolve, never the warden's.
[TestClass]
public class ChoreographiesTests
{
    [TestMethod]
    public void AFormationTold_IsKeptByItsName_WithItsFleet_AndEveryPlaceHeldByRank()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,red,green,yellow");

        var square = blue.Choreography.Form("Square-2", "square", new Position(5.5, 5.5), new Meters(2.0), new Degrees(0.0), fleet, "rank", "call-7");
        Assert.AreEqual("square-2", square.Name, "the name the warden gave it, lower case");
        Assert.AreEqual("call-7", square.Stamp, "the once of its call, the host's");
        Assert.AreEqual("square", square.Figure.Name);
        Assert.AreEqual("rank", square.Policy);
        Assert.AreSame(fleet, square.Fleet);
        Assert.AreSame(square, blue.Choreography.Find("square-2"), "found again by its name");
        Assert.IsTrue(blue.Choreography.HasFormation("SQUARE-2"));
        Assert.IsFalse(blue.Choreography.Knows("square-2"), "told, not taken: this golem has no place in it yet");
        Assert.AreEqual(1, blue.Choreography.Formations().Count);

        // by rank every place is known from birth: the names sorted take the corners counter-clockwise from the north-east
        var holders = square.Holders();
        CollectionAssert.AreEqual(new[] { "blue", "green", "red", "yellow" }, holders.Select(h => h.Name).ToList());
        CollectionAssert.AreEqual(new[] { 0, 1, 2, 3 }, holders.Select(h => h.Index).ToList());
        Assert.AreEqual(6.5, holders[0].X, 1e-9); Assert.AreEqual(6.5, holders[0].Y, 1e-9);   // blue: the north-east corner
        Assert.AreEqual(4.5, holders[2].X, 1e-9); Assert.AreEqual(4.5, holders[2].Y, 1e-9);   // red: the south-west one
        Assert.IsTrue(holders.All(h => h.Orbit == 0), "a square has one orbit");

        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Find("pentagon-7")).Message, "the golem was told no formation named 'pentagon-7': form it first");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Form("h", "hexagon", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0), fleet, "rank")).Message, "knows no figure named 'hexagon'");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Form("s", "square", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0), fleet, "luck")).Message, "by 'rank' or by 'distance'");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Form("d", "double ring", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0), fleet, "rank")).Message, "two rings of golems: FormRings");
    }

    [TestMethod]
    public void ToldAgain_ItIsMadeAgain_TheOldOneDissolved_AndARouteAlreadyGivenIsLetGo()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,red");

        var turned = blue.Choreography.Form("square-1", "square", new Position(5.5, 5.5), new Meters(2.0), new Degrees(45.0), fleet, "rank");
        var corner = turned.Holders()[0];
        Assert.AreEqual(5.5, corner.X, 1e-9, "the north-east corner, turned 45°, stands due north");
        Assert.AreEqual(5.5 + Math.Sqrt(2), corner.Y, 1e-9);
        var route = turned.Join(new Pose(5.5, 9.0, -1.5708), fleet.Member(blue));
        StringAssert.Contains(route.AsPlan(), "floor@5.5,6.91", "blue's route to the turned corner: " + route.AsPlan());

        var moved = blue.Choreography.Form("square-1", "square", new Position(3.0, 3.0), new Meters(2.0), new Degrees(0.0), fleet, "rank");
        Assert.AreSame(moved, blue.Choreography.Find("square-1"), "told again by the same name: made again");
        Assert.IsTrue(turned.Dissolved, "the old one dissolved");
        Assert.AreEqual(1, blue.Choreography.Formations().Count, "one formation by that name in the list");
        Assert.AreEqual(4.0, moved.Holders()[0].X, 1e-9); Assert.AreEqual(4.0, moved.Holders()[0].Y, 1e-9);
        Assert.AreEqual("abandoned", route.Status, "the route to the old figure was let go with it");
        StringAssert.Contains(route.Why, "square-1 was dissolved");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => turned.Convene(new Pose(5.5, 9.0, -1.5708), fleet.Member(blue))).Message, "was dissolved");
    }

    [TestMethod]
    public void ADoubleRing_IsToldWithItsTwoRingsOfGolems_AndEachRingTurnsAlone_OrBoth()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var red = new Golem(body, "red");
        red.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,cyan,green", "orange,red,yellow");
        Assert.IsTrue(fleet.IsDivided);
        Assert.AreEqual(3, fleet.Division);
        CollectionAssert.AreEqual(new[] { "blue", "cyan", "green", "orange", "red", "yellow" }, fleet.Names.ToList(), "the outer ring's names sorted, then the inner ring's");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Fleet("blue,red", "red,yellow")).Message, "a golem rides one ring: red on both");

        var rings = red.Choreography.FormRings("double-ring-1", new Position(5.5, 5.5), new Meters(3.0), new Meters(1.2), new Degrees(0.0), fleet, "rank");
        Assert.AreEqual("double ring", rings.Figure.Name);
        Assert.AreEqual(6, rings.PlaceCount);
        var holders = rings.Holders();
        Assert.AreEqual(0, holders.Single(h => h.Name == "blue").Orbit, "blue on the outer ring");
        Assert.AreEqual(1, holders.Single(h => h.Name == "red").Orbit, "red on the inner one");
        Assert.AreEqual(8.5, holders.Single(h => h.Name == "blue").X, 1e-9, "the outer ring's first place due east, radius 3");
        Assert.AreEqual(6.7, holders.Single(h => h.Name == "orange").X, 1e-9, "the inner ring's first place due east, radius 1.2");
        Assert.AreEqual(4, holders.Single(h => h.Name == "red").Index, "red: the second place of the inner ring");

        // red takes its place; everybody placed; a step of the INNER ring alone moves the inner golems and leaves the outer ones standing
        var route = rings.Join(new Pose(2.5, 2.5, 0.0), fleet.Member(red));
        StringAssert.Contains(route.AsPlan(), "floor@4.9,6.54", "red's place: 120° round the inner ring — " + route.AsPlan());
        WalkToTheEnd(route);   // on its place: the berths of the others have moved on (ajuste 80)
        rings.Placed(rings.Me); foreach (var name in new[] { "blue", "cyan", "green", "orange", "yellow" }) rings.Heard(fleet.Member(name));
        var inner = rings.Figure.Rotate(Sense.Clockwise, Ring.Inner);
        Assert.AreEqual(1, inner.Orbit);
        Assert.IsTrue(inner.Moves(1)); Assert.IsFalse(inner.Moves(0));
        rings.Queue(inner, "s1");
        var step = rings.Step();
        Assert.AreNotSame(route, step, "red moves: the inner ring turned");
        Assert.AreEqual(3, rings.PlaceIndex, "clockwise is down the order: from the second place of the inner ring to its first");
        Assert.AreEqual(0, rings.Holders().Single(h => h.Name == "blue").Index, "blue did not move");
        Assert.AreEqual(3, rings.PlacedCount, "the outer ring is still in place; the inner one walks");

        // the inner ring STARTS TOGETHER (propuesta 106): red turns to face its next place and waits for orange and yellow to be lined up too
        step.Arrive(step.NextLeg);
        Assert.IsTrue(step.Waiting, "the turn done, the route waits for the fleet's start");
        Assert.AreEqual("stop", step.Order);
        rings.Aligned(rings.Me);
        rings.HeardAligned(fleet.Member("orange"));
        Assert.IsTrue(step.Waiting, "yellow is not lined up yet");
        Assert.AreSame(step, rings.HeardAligned(fleet.Member("blue")), "a word of the outer ring, which does not move, changes nothing");
        rings.HeardAligned(fleet.Member("yellow"));
        Assert.IsFalse(step.Waiting, "the three movers lined up: the inner ring sets out at once");
        Assert.AreEqual("advance", step.Order);
        // the outer ring alone: red stays, its route in place stands
        WalkToTheEnd(step);
        rings.Placed(rings.Me); rings.Heard(fleet.Member("orange")); rings.Heard(fleet.Member("yellow"));
        rings.Queue(rings.Figure.Rotate(Sense.Counterclockwise, Ring.Outer), "s2");
        var same = rings.Step();
        Assert.AreSame(step, same, "red does not move on the outer ring's step");
        Assert.AreEqual(1, rings.Holders().Single(h => h.Name == "blue").Index, "blue went counter-clockwise, one place up the order");
        Assert.AreEqual(3, rings.PlacedCount, "now the inner ring is the one still in place");

        // both rings at once; a figure of one orbit refuses a ring
        rings.Placed(rings.Me); foreach (var name in new[] { "blue", "cyan", "green", "orange", "yellow" }) rings.Heard(fleet.Member(name));
        rings.Queue(rings.Figure.Rotate(Sense.Clockwise, Ring.Whole), "s3");
        var both = rings.Step();
        Assert.AreNotSame(same, both);
        Assert.AreEqual(0, rings.Holders().Single(h => h.Name == "blue").Index, "everybody one place down the order");
        Assert.AreEqual(5, rings.PlaceIndex);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Square(new Position(5.5, 5.5), new Meters(2.0)).Rotate(Sense.Clockwise, Ring.Inner)).Message, "has no rings: it turns whole");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => red.Choreography.FormRings("r", new Position(5.5, 5.5), new Meters(3.0), new Meters(1.2), new Degrees(0.0), new Fleet("blue,red"), "rank")).Message, "needs its fleet in two rings");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => red.Choreography.Form("r", "circle", new Position(5.5, 5.5), new Meters(3.0), new Degrees(0.0), fleet, "rank")).Message, "a fleet in two rings takes a double ring");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => red.Choreography.FormRings("r", new Position(5.5, 5.5), new Meters(1.0), new Meters(1.2), new Degrees(0.0), fleet, "rank")).Message, "wider than the inner one");
    }

    [TestMethod]
    public void Dissolved_TheFormationLeavesTheList_ItsRouteIsLetGo_AndNothingMoreIsQueued()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,red");
        var square = blue.Choreography.Form("square-1", "square", new Position(5.5, 5.5), new Meters(2.0), new Degrees(0.0), fleet, "rank");
        var ring = blue.Choreography.Form("ring-1", "circle", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0), fleet, "distance");
        Assert.AreEqual(2, blue.Choreography.Formations().Count);
        Assert.AreEqual(0, ring.Holders().Count, "by distance nobody holds a place until it speaks");

        var route = square.Join(new Pose(2.5, 2.5, 0.0), fleet.Member(blue));
        Assert.IsTrue(blue.Choreography.Knows("square-1"));
        Assert.AreSame(square, blue.Choreography.Current);
        blue.Choreography.Dissolve(square);
        Assert.IsTrue(square.Dissolved);
        Assert.AreEqual("abandoned", route.Status);
        Assert.IsFalse(blue.Choreography.HasFormation("square-1"));
        Assert.IsFalse(blue.Choreography.Knows("square-1"));
        CollectionAssert.AreEqual(new[] { "ring-1" }, blue.Choreography.Formations().Select(f => f.Name).ToList());
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => square.Queue(square.Figure.Rotate(Sense.Clockwise), "s1")).Message, "was dissolved");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Current).Message, "stands in no formation");
        blue.Choreography.Dissolve(square);   // twice: nothing more happens
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Dissolve(null)).Message, "'formation' was not given");
    }

    // The body's walk: the leg the route asks, done as asked — reported the way the robot does (ajuste 68).
    private static void WalkToTheEnd(GolemDomain.Routes.Route route)
    {
        for (int i = 0; i < 50 && route.IsPending(); i++)
        {
            if (route.Waiting) route.Go();   // the fleet's start, given here by hand: the words that give it are FormationTests' business (propuesta 106)
            route.Arrive(route.NextLeg);
        }
    }
}
