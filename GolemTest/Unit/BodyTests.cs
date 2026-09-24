using GolemDomain;
using GolemDomain.Robots;
using GolemDomain.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// THE BODY (Robots.Body) AND ITS MAGNITUDES (Units): a body is built from magnitudes that say what they are, reads them in
// their base units whatever unit wrote them, and refuses nonsense in its own words (Juan, 11-sep-2026).
[TestClass]
public class BodyTests
{
    [TestMethod]
    public void TheBody_ReadsItsRadiusSpeedLingerAndRetreat_InBaseUnits()
    {
        var body = new Body(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
        Assert.AreEqual(0.25, body.Radius.InMeters, 1e-9, "the body's size");
        Assert.AreEqual(2.0, body.Speed.InMetersPerSecond, 1e-9, "its cruise speed");
        Assert.AreEqual(6.0, body.LingerAfterTold.InSeconds, 1e-9, "its linger at a told stop");
        Assert.AreEqual(0.6, body.Retreat.InMeters, 1e-9, "how far it backs off after a touch");
    }

    [TestMethod]
    public void TheBody_RefusesARadiusOrASpeedOfZero_InItsOwnWords()
    {
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Body(new Meters(0.0), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6))).Message, "a body needs a radius above zero");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Body(new Meters(0.25), new MetersPerSecond(0.0), new Seconds(6.0), new Meters(0.6))).Message, "a body needs a cruise speed above zero");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Body(null, new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6))).Message, "a body needs a radius");
    }

    [TestMethod]
    public void AMagnitude_ReadsInItsBaseUnit_WhateverUnitWroteIt_AndIsNeverNegative()
    {
        Assert.AreEqual(0.25, new Centimeters(25.0).InMeters, 1e-9, "a length reads in metres whatever unit wrote it");
        Assert.AreEqual(90.0, new Minutes(1.5).InSeconds, 1e-9, "a duration reads in seconds whatever unit wrote it");
        Assert.AreEqual(4.0, new MetersPerSecond(2.0).TimeFor(new Meters(8.0)).InSeconds, 1e-9, "a speed knows how long a length takes");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Seconds(-1.0)).Message, "a duration cannot be negative");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => new Meters(-0.1)).Message, "cannot be negative");
    }
}
