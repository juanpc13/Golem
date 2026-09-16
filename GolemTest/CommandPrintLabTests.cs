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
            "upgrade('body_v1') { radius = Meters(0.25); speed = MetersPerSecond(2.0); linger = Seconds(6.0); retreat = Meters(0.6); body = Body(radius, speed, linger, retreat); }\n"
            + Catalog.Warehouse().AsRelease()
            + "upgrade('init') { collisions = Collisions(map); g = Golem(body, map, collisions); }\n").PerformCommand();
        const string next = @"
            if (g.HasPendingMission()) { print g.Next().Id 'route', g.Next().Order 'order'; }
            if (g.HasPendingMission() && g.Next().IsWalkable) { print g.Next().NextLeg.Kind 'kind', g.Next().NextLeg.At.X 'x', g.Next().NextLeg.At.Y 'y', g.Next().NextLeg.HasHeading 'hasHeading', g.Next().NextLeg.Heading 'heading'; }";
        var findings = new List<string>();
        string r1 = perf.Actor.Using("{ from = Position(@fx, @fy); point = Position(@x, @y); route = g.Visit(from, point); }\n" + next)
            .WithParameters(p => { p["fx", typeof(double)] = 2.0; p["fy", typeof(double)] = 9.5; p["x", typeof(double)] = 2.0; p["y", typeof(double)] = 1.5; }).PerformCommand();
        findings.Add("1 errand+print (PerformCommand): " + r1);
        string r2 = perf.Actor.Using("Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'not pending';", "{ route = g.Find(@id); point = Position(@x, @y); route.Reach(point); }\nexpose @id rid, @x rx, @y ry;\n" + next)
            .WithParameters(p => { p["id", typeof(int)] = 1; p["x", typeof(double)] = 0.75; p["y", typeof(double)] = 8.0; }).PerformCheckThenCommand();
        findings.Add("2 reach+expose+print (CheckThenCommand, passes): " + r2);
        string r3 = perf.Actor.Using("Check(g.Knows(@id) && g.Find(@id).Paused) Error 'not paused';", "{ route = g.Find(@id); route.Resume(); }\n" + next)
            .WithParameters(p => { p["id", typeof(int)] = 1; }).PerformCheckThenCommand();
        findings.Add("3 refused Check: " + r3);
        string r4 = perf.Actor.Using("{ route = g.Find(@id); route.Pause(); }\n" + next).WithParameters(p => { p["id", typeof(int)] = 1; }).PerformCommand();
        findings.Add("4 pause+print: " + r4);
        string r5 = perf.Actor.Using("{ route = g.Find(@id); point = Position(@x, @y); route.Reach(point); }\n" + next)
            .WithParameters(p => { p["id", typeof(int)] = 1; p["x", typeof(double)] = 2.0; p["y", typeof(double)] = 1.5; }).PerformCommand();
        findings.Add("5 last stop reached (nothing pending) + print: '" + r5 + "'");
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "lab-print.txt"), findings);
        perf.Dispose();
        Assert.IsTrue(findings.Count == 5);
    }
}
