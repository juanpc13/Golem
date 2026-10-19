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

        var square = blue.Choreography.Form("Square-2", "square", new Position(5.5, 5.5), new Meters(2.0), new Degrees(0.0), fleet, Policy.Rank, "call-7");
        Assert.AreEqual("square-2", square.Name, "the name the warden gave it, lower case");
        Assert.AreEqual("call-7", square.Stamp, "the once of its call, the host's");
        Assert.AreEqual("square", square.Figure.Name);
        Assert.AreEqual(Policy.Rank, square.Policy);
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
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Form("h", "hexagon", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0), fleet, Policy.Rank)).Message, "knows no figure named 'hexagon'");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Form("s", "square", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0), fleet, (Policy)7)).Message, "by Rank, Distance or RockPaperScissors");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Form("d", "double ring", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0), fleet, Policy.Rank)).Message, "knows no figure named 'double ring'");
    }

    [TestMethod]
    public void ToldAgain_ItIsMadeAgain_TheOldOneDissolved_AndARouteAlreadyGivenIsLetGo()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,red");

        var turned = blue.Choreography.Form("square-1", "square", new Position(5.5, 5.5), new Meters(2.0), new Degrees(45.0), fleet, Policy.Rank);
        var corner = turned.Holders()[0];
        Assert.AreEqual(5.5, corner.X, 1e-9, "the north-east corner, turned 45°, stands due north");
        Assert.AreEqual(5.5 + Math.Sqrt(2), corner.Y, 1e-9);
        var route = turned.Join(new Pose(5.5, 9.0, -1.5708), fleet.Member(blue));
        StringAssert.Contains(route.AsPlan(), "floor@5.5,6.91", "blue's route to the turned corner: " + route.AsPlan());

        var moved = blue.Choreography.Form("square-1", "square", new Position(3.0, 3.0), new Meters(2.0), new Degrees(0.0), fleet, Policy.Rank);
        Assert.AreSame(moved, blue.Choreography.Find("square-1"), "told again by the same name: made again");
        Assert.IsTrue(turned.Dissolved, "the old one dissolved");
        Assert.AreEqual(1, blue.Choreography.Formations().Count, "one formation by that name in the list");
        Assert.AreEqual(4.0, moved.Holders()[0].X, 1e-9); Assert.AreEqual(4.0, moved.Holders()[0].Y, 1e-9);
        Assert.AreEqual("abandoned", route.Status, "the route to the old figure was let go with it");
        StringAssert.Contains(route.Why, "square-1 was dissolved");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => turned.Convene(new Pose(5.5, 9.0, -1.5708), fleet.Member(blue))).Message, "was dissolved");
    }

    [TestMethod]
    public void Dissolved_TheFormationLeavesTheList_ItsRouteIsLetGo_AndNothingMoreIsQueued()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,red");
        var square = blue.Choreography.Form("square-1", "square", new Position(5.5, 5.5), new Meters(2.0), new Degrees(0.0), fleet, Policy.Rank);
        var ring = blue.Choreography.Form("ring-1", "circle", new Position(5.5, 5.5), new Meters(1.0), new Degrees(0.0), fleet, Policy.Distance);
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

    // A WORD HEARD AHEAD (13-oct-2026; propuesta 112: the warden writes the same `form` on every tab, and red's came two seconds after blue's and
    // green's words of the round — they found no formation and were lost; red never completed its round and the fleet waited for ever): the
    // module keeps the word and the `form` of that name takes it in; a word from a member already heard is about the NEXT telling
    [TestMethod]
    public void AWordHeardBeforeTheFormationIsTold_IsKept_AndTheFormTakesItIn()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var red = new Golem(body, "red");
        red.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        Assert.IsFalse(red.Choreography.Awaits("tri", "blue"), "no formation of that name yet: the word is not for a copy in hand");
        red.Choreography.Overheard("tri", "blue", new Position(3.0, 7.0));
        red.Choreography.Overheard("tri", "green", new Position(1.5, 4.5));
        Assert.AreEqual(2, red.Choreography.OverheardCount("tri"));

        var fleet = new Fleet("blue,red,green");
        var tri = red.Choreography.Form("tri", "triangle", new Position(3.0, 5.5), new Meters(3.0), new Degrees(0.0), fleet, Policy.Rank, "s1");
        Assert.AreEqual(0, red.Choreography.OverheardCount("tri"), "taken in by the form");
        Assert.AreEqual(2, tri.StoodCount, "blue and green spoke before red was told");
        var route = tri.Convene(new Pose(4.5, 4.0, 0.0), fleet.Member(red));
        Assert.IsNotNull(route, "red's own word completes the round: its route opens");
        Assert.IsTrue(tri.IsComplete);

        // blue speaks again: a NEW telling of the name (the show run again) — kept for the next form, not mixed into this one
        Assert.IsFalse(red.Choreography.Awaits("tri", "blue"), "blue already spoke in this copy");
        red.Choreography.Overheard("tri", "blue", new Position(3.0, 7.2));
        var again = red.Choreography.Form("tri", "triangle", new Position(3.0, 5.5), new Meters(3.0), new Degrees(0.0), fleet, Policy.Rank, "s2");
        Assert.AreEqual(1, again.StoodCount, "the new telling starts with blue's new word");

        // a dissolution drops what was kept for its name
        red.Choreography.Overheard("tri", "green", new Position(1.5, 4.6));
        red.Choreography.Dissolve(again);
        Assert.AreEqual(0, red.Choreography.OverheardCount("tri"));
    }

    // THE PROPELLER (propuesta 114, 13-oct-2026; Juan: "quiero una nueva formación de hélice y que cambien de posición al rotar"… "la hélice tiene
    // que tener uno al centro también"): one golem at the centre, the others along blades out of it, the first north; six golems on three blades
    // are the centre and two rungs (one tip left free); a step takes every golem on a blade to the NEXT BLADE at its own distance, and leaves the
    // one at the centre where it stands, facing along the first blade
    [TestMethod]
    public void APropeller_HasOneAtTheCentre_AndAStepTurnsTheBladesRoundIt()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        var cyan = new Golem(body, "cyan");
        cyan.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var fleet = new Fleet("blue,cyan,green,orange,red,yellow");
        var propeller = cyan.Choreography.FormPropeller("prop", new Position(5.5, 5.5), new Meters(3.0), new Meters(1.0), 3, new Degrees(0.0), fleet, Policy.Rank, "s1");
        var places = propeller.Figure.Places(6);
        Assert.AreEqual(7, places.Count, "the centre and two rungs on three blades — one tip left free");
        Assert.AreEqual(5.5, places[0].X, 1e-9, "the first place, the centre");
        Assert.AreEqual(5.5, places[0].Y, 1e-9);
        Assert.AreEqual(6.5, places[1].Y, 1e-9, "the hub's rung, the north blade");
        Assert.AreEqual(8.5, places[4].Y, 1e-9, "the tips' rung, the north blade");
        Assert.AreEqual(5.5 + 3.0 * Math.Cos(Math.PI * 210 / 180), places[5].X, 1e-9, "the second blade, 120° on, counter-clockwise");
        Assert.AreEqual(3, propeller.Figure.Orbits(6).Count, "the centre, and every rung an orbit");
        Assert.AreEqual(1.0, propeller.Figure.InnerMeters, 1e-9);
        Assert.AreEqual(3, propeller.Figure.Arms);
        Assert.AreEqual(8.5, new Propeller(new Position(5.5, 5.5), new Meters(3.0), new Meters(1.0), 5, new Degrees(0.0)).Places(6)[1].Y, 1e-9, "one rung: at the tip");

        // cyan (rank 1) holds the hub of the north blade; one step clockwise takes it to the hub of the blade before, the south-east one (330°)
        var route = propeller.Join(new Pose(5.5, 6.4, 0.0), fleet.Member(cyan));
        for (int i = 0; i < 50 && route.IsPending(); i++) { if (route.Waiting) route.Go(); route.Arrive(route.NextLeg); }
        propeller.Placed(propeller.Me);
        foreach (var other in new[] { "blue", "green", "orange", "red", "yellow" }) propeller.Heard(fleet.Member(other));
        propeller.Queue(propeller.Figure.Rotate(Sense.Clockwise), "step-1");
        var step = propeller.Step();
        Assert.AreEqual(3, propeller.PlaceIndex, "cyan to the hub of the next blade clockwise");
        for (int i = 0; i < 50 && step.IsPending(); i++) { if (step.Waiting) step.Go(); step.Arrive(step.NextLeg); }
        Assert.AreEqual(5.5 + Math.Cos(Math.PI * 330 / 180), cyan.Standing.X, 1e-6, "on the south-east blade, still at the hub");
        Assert.AreEqual(5.5 + Math.Sin(Math.PI * 330 / 180), cyan.Standing.Y, 1e-6);
        Assert.AreEqual(1, propeller.Holders().Count(h => h.Index == 0 && h.Name == "blue"), "blue still holds the centre: no step moves it");

        // blue (rank 0) takes the centre and faces along the first blade, north
        var blue = new Golem(body, "blue");
        blue.Enter(new Scenario(Catalog.OpenFloor(), new Collisions()));
        var blues = blue.Choreography.FormPropeller("prop", new Position(5.5, 5.5), new Meters(3.0), new Meters(1.0), 3, new Degrees(0.0), fleet, Policy.Rank, "s1");
        var centre = blues.Join(new Pose(5.0, 5.0, 0.0), fleet.Member(blue));
        for (int i = 0; i < 50 && centre.IsPending(); i++) { if (centre.Waiting) centre.Go(); centre.Arrive(centre.NextLeg); }
        Assert.AreEqual(5.5, blue.Standing.X, 1e-6);
        Assert.AreEqual(Math.PI / 2, blue.Standing.Heading, 1e-6, "facing along the first blade");

        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => cyan.Choreography.FormPropeller("bad", new Position(5.5, 5.5), new Meters(3.0), new Meters(3.5), 3, new Degrees(0.0), fleet, Policy.Rank)).Message, "hub");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => cyan.Choreography.FormPropeller("bad", new Position(5.5, 5.5), new Meters(3.0), new Meters(1.0), 1, new Degrees(0.0), fleet, Policy.Rank)).Message, "from 2 to 12 blades");
    }
}
