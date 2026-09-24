using System.Globalization;
using Choreography.Theater;
using GolemDomain;
using GolemDomain.Layouts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;

namespace GolemTest;

// A LAB (16-sep-2026, Juan: "al momento justo que se escribe el comando este retorna con print"): what a COMMAND that
// ends with a print returns to its caller — plain, with an expose beside the act, and behind a Check that passes or refuses.
[TestClass]
public sealed class CommandPrintLabTests
{
    [TestInitialize]
    public void PinTheCulture() { CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; }

    [TestMethod]
    public void WhatACommandThatPrints_Returns()
    {
        var perf = new PerformanceV2("print-lab-" + Guid.NewGuid().ToString("N"), DomainLibrary.Assembly);
        perf.ConfigureStorage(DatabaseType.IN_MEMORY, "print-lab");
        perf.Start();
        perf.Actor.Using(
            @"
                upgrade('body_v1') {
                    radius = Meters(0.25);
                    speed = MetersPerSecond(2.0);
                    linger = Seconds(6.0);
                    retreat = Meters(0.6);
                    body = Body(radius, speed, linger, retreat);
                }
            "
            + Catalog.Warehouse().AsRelease()
            + @"
                upgrade('init') {
                    collisions = Collisions(map);
                    g = Golem(body, map, collisions);
                }
            ").PerformCommand();
        var findings = new List<string>();
        string r1 = perf.Actor.Using(@"
            {
                from = Position(@fx, @fy);
                point = Position(@x, @y);
                route = g.Visit(from, point);
            }
            if (g.HasPendingMission()) {
                print g.Underway().Id 'route', g.Underway().Order 'action', g.Underway().Amount 'amount';
            }
            if (g.HasPendingMission() && g.Underway().IsWalkable) {
                print g.Underway().NextLeg.Kind 'kind', g.Underway().Target.X 'x', g.Underway().Target.Y 'y', g.Underway().Target.Heading 'heading';
            }
        ")
            .WithParameters(p => {
                p["fx", typeof(double)] = 2.0;
                p["fy", typeof(double)] = 9.5;
                p["x", typeof(double)] = 2.0;
                p["y", typeof(double)] = 1.5;
            }).PerformCommand();
        findings.Add("1 errand+print (PerformCommand): " + r1);
        string r2 = perf.Actor.Using(@"
            Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'not pending';
        ", @"
            {
                route = g.Find(@id);
                me = Pose(@x, @y, @t);
                route.Turn(me);
            }
            expose @id rid, @x rx, @y ry;
            if (g.HasPendingMission()) {
                print g.Underway().Id 'route', g.Underway().Order 'action', g.Underway().Amount 'amount';
            }
            if (g.HasPendingMission() && g.Underway().IsWalkable) {
                print g.Underway().NextLeg.Kind 'kind', g.Underway().Target.X 'x', g.Underway().Target.Y 'y', g.Underway().Target.Heading 'heading';
            }
        ")
            .WithParameters(p => {
                p["id", typeof(int)] = 1;
                p["x", typeof(double)] = 2.0;
                p["y", typeof(double)] = 9.5;
                p["t", typeof(double)] = -2.0;
            }).PerformCheckThenCommand();
        findings.Add("2 turn+expose+print (CheckThenCommand, passes): " + r2);
        string r3 = perf.Actor.Using(@"
            Check(g.Knows(@id) && g.Find(@id).Paused) Error 'not paused';
        ", @"
            {
                route = g.Resume(Pose(2.0, 9.5, 0.0));
            }
            if (g.HasPendingMission()) {
                print g.Underway().Id 'route', g.Underway().Order 'action', g.Underway().Amount 'amount';
            }
            if (g.HasPendingMission() && g.Underway().IsWalkable) {
                print g.Underway().NextLeg.Kind 'kind', g.Underway().Target.X 'x', g.Underway().Target.Y 'y', g.Underway().Target.Heading 'heading';
            }
        ")
            .WithParameters(p => {
                p["id", typeof(int)] = 1;
            }).PerformCheckThenCommand();
        findings.Add("3 refused Check: " + r3);
        string r4 = perf.Actor.Using(@"
            {
                route = g.Pause(Pose(2.0, 9.5, 0.0));
            }
            if (g.HasPendingMission()) {
                print g.Underway().Id 'route', g.Underway().Order 'action', g.Underway().Amount 'amount';
            }
            if (g.HasPendingMission() && g.Underway().IsWalkable) {
                print g.Underway().NextLeg.Kind 'kind', g.Underway().Target.X 'x', g.Underway().Target.Y 'y', g.Underway().Target.Heading 'heading';
            }
        ").WithParameters(p => {
            p["id", typeof(int)] = 1;
        }).PerformCommand();
        findings.Add("4 pause+print: " + r4);
        string r5 = perf.Actor.Using(@"
            {
                route = g.Find(@id);
                route.Abandon(@why);
            }
            if (g.HasPendingMission()) {
                print g.Underway().Id 'route', g.Underway().Order 'action', g.Underway().Amount 'amount';
            }
            if (g.HasPendingMission() && g.Underway().IsWalkable) {
                print g.Underway().NextLeg.Kind 'kind', g.Underway().Target.X 'x', g.Underway().Target.Y 'y', g.Underway().Target.Heading 'heading';
            }
        ")
            .WithParameters(p => {
                p["id", typeof(int)] = 1;
                p["why", typeof(string)] = "the lab is over";
            }).PerformCommand();
        findings.Add("5 route abandoned (nothing pending) + print: '" + r5 + "'");
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "lab-print.txt"), findings);
        perf.Dispose();
        Assert.IsTrue(findings.Count == 5);
    }
}
