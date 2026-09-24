using GolemDomain;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Robots;
using GolemDomain.Routes;
using GolemDomain.Touches;
using GolemDomain.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// THE BODY (Robots.Body) AND ITS MAGNITUDES (Units), SEEN THROUGH THE GOLEM THAT DRIVES IT (Juan, 24-sep-2026: "que los testcase le
// pasen los módulos que se están testeando al golem"): a body is built from magnitudes that say what they are — as body_v1 builds it —
// and handed to a golem, and what each magnitude IS shows in what the golem does with it: the radius in where the body fits and how far
// a follower stops short of its leader, the retreat in how far it backs off after a touch, the speed and the linger in how long the road
// ahead takes. A magnitude reads in its base unit whatever unit wrote it, so the golem drives the same body; nonsense is refused at the
// body's birth, in its own words.
[TestClass]
public class BodyTests
{
    [TestMethod]
    public void TheRadius_IsWhereTheBodyFits_AndHowFarAFollowerStopsShortOfItsLeader()
    {
        var map = Catalog.Warehouse();
        var slim = new Golem(new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6)), map, new Collisions());
        var wide = new Golem(new Body(new Meters(0.8), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6)), map, new Collisions());

        Assert.IsTrue(slim.FitsAt(new Position(10.25, 5.5)), "the east corridor is 1.5 m wide: a body of radius 0.25 fits in the middle of it");
        Assert.IsFalse(wide.FitsAt(new Position(10.25, 5.5)), "a body of radius 0.8 does not: its shell would be in the walls");
        Assert.IsTrue(wide.FitsAt(new Position(5.5, 5.5)), "in the middle of the centre hall, 3 m wide, both fit");

        // a follower meets its leader's spot a standoff short: two radii of ITS body and a clearance
        var stout = new Golem(new Body(new Meters(0.35), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6)), map, new Collisions());
        slim.Wake(new Pose(5.0, 9.5, 0.0));                                   // both in the north hall, facing east…
        stout.Wake(new Pose(5.0, 9.5, 0.0));
        Assert.AreEqual(1.6 - (2 * 0.25 + Route.FollowerClearance), slim.Follow(new Position(6.6, 9.5)).Amount, 1e-6,
            "…the leader's spot 1.6 m ahead: the advance stops two radii of 0.25 and the clearance short of it");
        Assert.AreEqual(1.6 - (2 * 0.35 + Route.FollowerClearance), stout.Follow(new Position(6.6, 9.5)).Amount, 1e-6, "a stouter body stops farther short");
    }

    [TestMethod]
    public void TheRetreat_IsHowFarTheBodyBacksOff_AfterATouch()
    {
        var map = Catalog.Warehouse();
        var bold = new Golem(new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6)), map, new Collisions());
        var timid = new Golem(new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(1.0)), map, new Collisions());

        // both drive south down the centre hall and are pressed on the nose halfway, standing at (5.5, 6.1)
        var boldRoute = bold.Visit(new Pose(5.5, 9.5, -1.5708), new Position(5.5, 1.5));
        var timidRoute = timid.Visit(new Pose(5.5, 9.5, -1.5708), new Position(5.5, 1.5));
        bold.Bump(new Pose(5.5, 6.1, -1.5708), 0.0);
        timid.Bump(new Pose(5.5, 6.1, -1.5708), 0.0);
        Assert.AreEqual("back", boldRoute.Order, "the first correction after a touch: back off");
        Assert.AreEqual("back", timidRoute.Order);
        Assert.IsTrue(boldRoute.Amount >= 0.6 - 1e-6, "at least the body's own retreat, in metres: " + boldRoute.Amount);
        Assert.IsTrue(timidRoute.Amount >= 1.0 - 1e-6, "a body declared with a longer retreat backs off farther: " + timidRoute.Amount);
        Assert.IsTrue(timidRoute.Amount > boldRoute.Amount);
    }

    [TestMethod]
    public void TheSpeedAndTheLinger_AreHowLongTheRoadAheadTakes()
    {
        var map = Catalog.Warehouse();
        var quick = new Golem(new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6)), map, new Collisions());
        var slow = new Golem(new Body(new Meters(0.25), new MetersPerSecond(1.0), new Seconds(3.0), new Meters(0.6)), map, new Collisions());
        foreach (var g in new[] { quick, slow })
        {
            g.Visit(new Position(2.0, 9.5), new Position(5.5, 9.5));   // the north hall, from the kitchen
            g.Follow(new Position(5.5, 5.5));                          // the centre, a followed point: one linger there
            g.Visit(g.PlannedEnd(), new Position(5.5, 1.5));           // the south hall
        }
        Assert.AreEqual(8.0, quick.RouteLength(), 0.01, "north -> centre -> south, straight across the open boundaries: the same road for both");
        Assert.AreEqual(8.0, slow.RouteLength(), 0.01);
        Assert.AreEqual(8.0 / 2.0 + 6.0, quick.RouteSeconds(), 0.01, "at 2 m/s, plus a 6 s linger at the followed stop");
        Assert.AreEqual(8.0 / 1.0 + 3.0, slow.RouteSeconds(), 0.01, "at 1 m/s, plus a 3 s linger");
    }

    [TestMethod]
    public void AMagnitude_ReadsInItsBaseUnit_WhateverUnitWroteIt_SoTheGolemDrivesTheSameBody()
    {
        var map = Catalog.Warehouse();
        var inMetres = new Golem(new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6)), map, new Collisions());
        var inOtherUnits = new Golem(new Body(new Centimeters(25.0), new MetersPerSecond(2.0), new Minutes(0.1), new Centimeters(60.0)), map, new Collisions());
        foreach (var g in new[] { inMetres, inOtherUnits })
        {
            g.Visit(new Position(2.0, 9.5), new Position(5.5, 9.5));
            g.Follow(new Position(5.5, 5.5));
        }
        Assert.AreEqual(inMetres.RouteSeconds(), inOtherUnits.RouteSeconds(), 1e-9, "a tenth of a minute is the same linger as six seconds");
        Assert.AreEqual(inMetres.Underway().FollowerStandoff, inOtherUnits.Underway().FollowerStandoff, 1e-9, "twenty-five centimetres is the same radius as a quarter of a metre");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Seconds(-1.0)).Message, "a duration cannot be negative");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Meters(-0.1)).Message, "cannot be negative");
    }

    [TestMethod]
    public void ABody_RefusesARadiusOrASpeedOfZero_InItsOwnWords_AndAGolemNeedsOneToDrive()
    {
        var map = Catalog.Warehouse();
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Body(new Meters(0.0), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6))).Message, "a body needs a radius above zero");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Body(new Meters(0.25), new MetersPerSecond(0.0), new Seconds(6.0), new Meters(0.6))).Message, "a body needs a cruise speed above zero");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Body(null, new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6))).Message, "a body needs a radius");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Golem(null, map, new Collisions())).Message, "a golem needs a body to drive");
    }
}
