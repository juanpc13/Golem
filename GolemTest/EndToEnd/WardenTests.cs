using GolemDomain;
using GolemDomain.Coordination;
using GolemDomain.Formations;
using GolemDomain.Geometry;
using GolemDomain.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest.EndToEnd;

// THE WARDEN (propuesta 88, 2-oct-2026; Juan: "centraliza la ley, flota aprendida, berths por tells"): a subject without a body that learns
// the fleet from the golems' words, convenes a formation, applies the law ONCE when everybody said where it stands, and shares the whole
// table as one word; then counts who stands on its place and opens the steps. It opens no route: the golems do (PlacementTests).
[TestClass]
public class WardenTests
{
    [TestMethod]
    public void TheWarden_IsBornWithItsName_LearnsTheFleetFromTheirWords_AndMakesFormations()
    {
        var warden = new Warden("Warden");
        Assert.AreEqual("warden", warden.Name, "lower case, like a golem's");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Warden(" ")).Message, "a warden's name must be a name");

        // the fleet is LEARNED: nobody until somebody speaks
        Assert.AreEqual(0, warden.Roster.Count);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => warden.Roster.Fleet()).Message, "heard from no golem yet");
        Assert.IsFalse(warden.Roster.Knows("blue"));
        var blue = warden.Roster.Heard("Blue", new Position(6.3, 10.4), "warehouse");
        Assert.AreEqual("blue", blue.Name);
        Assert.AreEqual(0.0, blue.Standing.DistanceTo(new Position(6.3, 10.4)), 1e-9, "where it said it stood");
        Assert.AreEqual("warehouse", blue.Scenario);
        Assert.AreEqual(1, blue.Words);
        Assert.AreSame(blue, warden.Roster.Of("blue"));
        warden.Roster.Heard("red", new Position(2.5, 2.5), "warehouse");
        warden.Roster.Heard("blue", new Position(5.5, 5.5), "open-floor");
        Assert.AreEqual(2, warden.Roster.Count, "a second word of blue's is no second golem");
        Assert.AreEqual(2, blue.Words);
        Assert.AreEqual("open-floor", blue.Scenario, "the latest word is what is known");
        CollectionAssert.AreEqual(new[] { "blue", "red" }, warden.Roster.Names().ToList());
        CollectionAssert.AreEqual(new[] { "blue", "red" }, warden.Roster.Fleet().Names.ToList(), "the fleet of everybody that spoke");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => warden.Roster.Of("green")).Message, "never heard from 'green'");
        warden.Roster.Stands("blue", new Position(6.5, 6.5));
        Assert.AreEqual(0.0, blue.Standing.DistanceTo(new Position(6.5, 6.5)), 1e-9, "a word about where it stands alone: on its place");
        Assert.AreEqual("open-floor", blue.Scenario, "the scenario kept");
        Assert.AreEqual(3, blue.Words);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => warden.Roster.Stands("green", new Position(1.0, 1.0))).Message, "never heard from 'green'");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => warden.Roster.Heard("green", null, "x")).Message, "'at' was not given");

        // the formations, by name, like the golem's module makes them
        Assert.IsInstanceOfType(warden.Formation("Square", new Position(5.5, 5.5), new Meters(2.0)), typeof(Square));
        Assert.IsInstanceOfType(warden.Formation("pentagon", new Position(5.5, 5.5), new Meters(2.0)), typeof(Pentagon));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => warden.Formation("hexagon", new Position(5.5, 5.5), new Meters(2.0))).Message, "knows no formation named 'hexagon'");

        // no convocation yet
        Assert.IsFalse(warden.Knows("red-1"));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => warden.Muster("red-1")).Message, "knows no call red-1");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => warden.Current).Message, "stands in no formation");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => warden.Muster("red-1", warden.Formation("square", new Position(5.5, 5.5), new Meters(2.0)), warden.Roster.Fleet(), "luck")).Message, "by 'rank' or by 'distance'");
    }

    // THE LAW ONCE, BY DISTANCE: the lab's marks (ajuste 87) — red and blue both nearest the north-east corner; red keeps it, blue's second
    // choice is taken by green first, blue ends south-east, yellow south-west — resolved in the warden's one copy, and the whole table
    // travels as one word.
    [TestMethod]
    public void ARoundOfWords_SharesThePlacesByDistance_TheLawOnce_AndTheWholeTableTravelsAsOneWord()
    {
        var warden = new Warden("warden");
        var square = warden.Formation("square", new Position(5.5, 5.5), new Meters(2.0));
        var fleet = new Fleet("blue,green,red,yellow");
        var muster = warden.Muster("red-1", square, fleet, "distance");
        Assert.AreSame(muster, warden.Muster("red-1", square, fleet, "distance"), "the same call, the same convocation");
        Assert.AreEqual("distance", muster.Policy);
        Assert.IsFalse(muster.Called);
        muster.Convene();
        Assert.IsTrue(muster.Called);
        Assert.IsTrue(warden.Knows("red-1"));
        Assert.AreSame(muster, warden.Current);
        Assert.AreSame(muster, warden.Muster("red-1"));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => muster.Convene()).Message, "was made already");

        muster.Stood(fleet.Member("red"), new Position(7.0, 6.3));
        muster.Stood(fleet.Member("blue"), new Position(6.0, 6.2));
        muster.Stood(fleet.Member("green"), new Position(3.5, 7.5));
        Assert.AreEqual(3, muster.StoodCount);
        Assert.IsFalse(muster.IsComplete, "yellow has not spoken");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => muster.Share()).Message, "3 of 4 said where they stand — the places wait for everybody");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => muster.Table).Message, "not shared yet");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => muster.Stood(fleet.Member("blue"), new Position(1.0, 1.0))).Message, "said where it stood already");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => muster.Stood(new Fleet("blue,purple").Member("purple"), new Position(1.0, 1.0))).Message, "is not a member of the fleet");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => muster.Placed(fleet.Member("red"))).Message, "not shared yet: nobody can stand on one");
        muster.Stood(fleet.Member("yellow"), new Position(3.0, 3.0));
        Assert.IsTrue(muster.IsComplete);
        Assert.IsFalse(muster.IsShared);

        var given = muster.Share();
        Assert.IsTrue(muster.IsShared);
        Assert.AreEqual(0, muster.Round);
        Assert.AreEqual(0.0, muster.PlaceOf(fleet.Member("red")).DistanceTo(new Position(6.5, 6.5)), 1e-9, "red, nearest to the north-east corner, keeps it");
        Assert.AreEqual(0.0, muster.PlaceOf(fleet.Member("green")).DistanceTo(new Position(4.5, 6.5)), 1e-9, "green takes the north-west corner before blue, nearer to it");
        Assert.AreEqual(0.0, muster.PlaceOf(fleet.Member("blue")).DistanceTo(new Position(6.5, 4.5)), 1e-9, "blue lost twice: the south-east corner");
        Assert.AreEqual(0.0, muster.PlaceOf(fleet.Member("yellow")).DistanceTo(new Position(4.5, 4.5)), 1e-9, "yellow the south-west one");
        Assert.AreEqual(3, muster.PlaceIndex(fleet.Member("blue")), "the index in the figure's order: north-east, north-west, south-west, south-east");
        Assert.AreEqual("blue@6,6.2>6.5,4.5;green@3.5,7.5>4.5,6.5;red@7,6.3>6.5,6.5;yellow@3,3>4.5,4.5", muster.Table, "the whole table as one word, the members in the fleet's order");
        Assert.AreEqual(4, given.Count);
        Assert.AreEqual(0.0, given.Of("red").From.DistanceTo(new Position(7.0, 6.3)), 1e-9, "where red said it stood…");
        Assert.AreEqual(0.0, given.Of("red").To.DistanceTo(new Position(6.5, 6.5)), 1e-9, "…and the place it goes to");

        // the word builds the same table back where it lands
        var back = new Assignments(muster.Table);
        Assert.AreEqual(muster.Table, back.AsText());
        Assert.AreEqual(0.0, back.Of("blue").To.DistanceTo(new Position(6.5, 4.5)), 1e-9);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Assignments("blue@6,6.2")).Message, "reads who@x,y>x,y");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => back.Of("purple")).Message, "has no place in these assignments");

        // shared: a late word changes nothing; sharing twice is refused
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => muster.Stood(fleet.Member("red"), new Position(1.0, 1.0))).Message, "shared already: 'red' speaks too late");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => muster.Share()).Message, "shared already");
    }

    [TestMethod]
    public void ByRank_ThePlacesAreTheNamesOrder_WhateverTheMarks()
    {
        var warden = new Warden("warden");
        var fleet = new Fleet("blue,green,red,yellow");
        var muster = warden.Muster("red-2", warden.Formation("square", new Position(5.5, 5.5), new Meters(2.0)), fleet, "rank");
        muster.Convene();
        muster.Stood(fleet.Member("red"), new Position(7.0, 6.3));
        muster.Stood(fleet.Member("blue"), new Position(6.0, 6.2));
        muster.Stood(fleet.Member("green"), new Position(3.5, 7.5));
        muster.Stood(fleet.Member("yellow"), new Position(3.0, 3.0));
        muster.Share();
        Assert.AreEqual("blue@6,6.2>6.5,6.5;green@3.5,7.5>4.5,6.5;red@7,6.3>4.5,4.5;yellow@3,3>6.5,4.5", muster.Table,
            "blue first: north-east; green north-west; red south-west, crossing the whole square; yellow south-east");
    }

    // THE STEPS (ajustes 77, 84, 86; propuesta 88: the warden counts who is placed and opens them): four on a pentagon by rank take four
    // vertices and leave the north-east one free; a step moves everybody one vertex and the hole with them; steps queue.
    [TestMethod]
    public void AStep_WaitsForEverybodyPlaced_MovesTheHoleOfAFreeVertexWithTheFleet_AndStepsQueue()
    {
        var warden = new Warden("warden");
        var pentagon = warden.Formation("pentagon", new Position(5.5, 5.5), new Meters(2.0));
        var fleet = new Fleet("blue,green,red,yellow");
        var muster = warden.Muster("red-3", pentagon, fleet, "rank");
        muster.Convene();
        var marks = new Dictionary<string, Position> { ["blue"] = new(5.5, 9.0), ["green"] = new(2.0, 7.0), ["red"] = new(3.0, 3.0), ["yellow"] = new(8.0, 3.0) };
        foreach (var name in fleet.Names) muster.Stood(fleet.Member(name), marks[name]);
        var first = muster.Share();
        var v = ((Pentagon)pentagon).Vertices();
        Assert.AreEqual(0.0, first.Of("blue").To.DistanceTo(v[0]), 1e-9, "blue, first: the north vertex");
        Assert.AreEqual(0.0, first.Of("yellow").To.DistanceTo(v[3]), 1e-9, "yellow, fourth: the south-east vertex; the north-east stays free");

        // a step queued before anybody is placed waits
        Assert.AreEqual(1, muster.Queue(pentagon.Rotate("clockwise"), "s1"));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => muster.Queue(pentagon.Rotate("clockwise"), "s1")).Message, "already queued");
        Assert.IsFalse(muster.CanStep);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => muster.Step()).Message, "0 of 4 stand on their places");
        muster.Placed(fleet.Member("blue")); muster.Placed(fleet.Member("green")); muster.Placed(fleet.Member("red"));
        Assert.AreEqual(3, muster.PlacedCount);
        Assert.IsFalse(muster.AllPlaced);
        Assert.IsFalse(muster.CanStep, "yellow is not placed yet");
        muster.Placed(fleet.Member("yellow"));
        Assert.IsTrue(muster.AllPlaced);
        Assert.IsTrue(muster.CanStep);

        var second = muster.Step();
        Assert.AreEqual(1, muster.Round, "a new round");
        Assert.AreEqual(0, muster.Queued);
        Assert.AreEqual(0, muster.PlacedCount, "nobody is placed in the new round yet");
        Assert.IsFalse(muster.CanStep);
        Assert.AreEqual(4, muster.PlaceIndex(fleet.Member("blue")), "clockwise from the north vertex: into the free north-east one");
        Assert.AreEqual(0, muster.PlaceIndex(fleet.Member("green")), "green into the north one blue left");
        Assert.AreEqual(2, muster.PlaceIndex(fleet.Member("yellow")), "yellow from south-east to south-west; the south-east is the hole now");
        Assert.AreEqual(0.0, second.Of("blue").From.DistanceTo(v[0]), 1e-9, "everybody stands on the place it reached…");
        Assert.AreEqual(0.0, second.Of("blue").To.DistanceTo(v[4]), 1e-9, "…and heads to the next");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => muster.Step()).Message, "no step is queued");

        // two more queue while they walk
        muster.Queue(pentagon.Rotate("counterclockwise"), "s2");
        Assert.AreEqual(2, muster.Queue(pentagon.Rotate("counterclockwise"), "s3"));
        foreach (var name in fleet.Names) muster.Placed(fleet.Member(name));
        muster.Step();
        Assert.AreEqual(2, muster.Round);
        Assert.AreEqual(1, muster.Queued, "one step left in the queue");
        Assert.AreEqual(0, muster.PlaceIndex(fleet.Member("blue")), "counter-clockwise: back to the north vertex");
        Assert.AreEqual(1, warden.Musters().Count, "one convocation called");
    }
}
