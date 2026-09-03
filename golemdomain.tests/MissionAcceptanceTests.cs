using System.Globalization;
using Choreography.Theater;
using GolemHost.Domain;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;

namespace GolemHost.Domain.Tests;

// End-to-end through the perform: a real actor, a journal (in memory), the same
// release chain the host runs, typed assertions via Out parameters.
[TestClass]
public class MissionAcceptanceTests
{
    private PerformanceV2 perf;

    [TestInitialize]
    public void AGolemIsBornWithItsWorld()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string name = "golem-under-test-" + Guid.NewGuid().ToString("N");
        perf = new PerformanceV2(name, GolemDomain.Assembly);
        perf.ConfigureStorage(DatabaseType.IN_MEMORY, name);
        perf.Start();
        perf.Actor.Using(@"
            upgrade('init')     { g = Golem(); }
            upgrade('world_v1') { g.Inhabit(11.08, 0.6); }
            upgrade('rock_v1')  { g.PlaceRock(7.5, 4.5, 1.0); }
        ")
        .PerformCommand();
    }

    [TestCleanup]
    public void TheGolemRests() => perf.Dispose();

    [TestMethod]
    public void AnEntrustedMission_IsPending()
    {
        Assign(1, 3.0, 4.0);

        Assert.AreEqual(1, Int("g.Pending()"));
        Assert.IsTrue(Bool("g.IsPending(1)"));
        Assert.AreEqual(3.0, Double("g.NextX()"));
    }

    [TestMethod]
    public void CompletingAMission_SettlesIt_AndTheCheckRefusesASecondCompletion()
    {
        Assign(1, 3.0, 4.0);

        string first = Complete(1);
        long entriesAfterFirst = perf.CurrentEntryId;
        string second = Complete(1);

        Assert.AreEqual("", first, "the first completion is accepted");
        Assert.AreNotEqual("", second, "a settled mission refuses another completion");
        Assert.AreEqual(entriesAfterFirst, perf.CurrentEntryId, "a refused check leaves no journal entry");
        Assert.AreEqual(0, Int("g.Pending()"));
    }

    [TestMethod]
    public void AnAlternateRoute_KeepsTheOrderedPointOnRecord_AndAimsAtTheNewOne()
    {
        Assign(1, 15.0, 5.0);

        perf.Actor.Using(@"
            g.Reroute(@id, @x, @y, @reason);
        ")
        .WithParameters(p => {
            p["id",     typeof(int)]    = 1;
            p["x",      typeof(double)] = 10.48;
            p["y",      typeof(double)] = 5.0;
            p["reason", typeof(string)] = "target unreachable";
        })
        .PerformCommand();

        Assert.AreEqual(1, Int("g.Reroutes(1)"));
        Assert.AreEqual(10.48, Double("g.NextX()"), 0.001);
        Assert.IsTrue(Bool("g.IsPending(1)"), "a rerouted mission is still to be done");
    }

    [TestMethod]
    public void TheWorld_RefusesToStandInsideTheRock_AndOffersTheNearestPointOutside()
    {
        Assert.IsFalse(Bool("g.CanStandAt(7.5, 4.5)"));
        Assert.IsTrue(Bool("g.CanStandAt(2.0, 2.0)"));
        Assert.IsFalse(Bool("g.CanStandAt(15.0, 5.0)"), "outside the walls");

        double altX = Double("g.NearestStandableX(7.5, 4.5)");
        Assert.IsTrue(Bool($"g.CanStandAt({altX.ToString(CultureInfo.InvariantCulture)}, 4.5)"),
            "the nearest standable point is standable");
    }

    [TestMethod]
    public void RetiringLetsGoOfEveryMission_ButNeverReusesAHandle()
    {
        Assign(1, 3.0, 4.0);
        Assign(2, 5.0, 6.0);

        perf.Actor.Using(@"
            g.Retire(@reason);
        ")
        .WithParameters(p => {
            p["reason", typeof(string)] = "the test is over";
        })
        .PerformCommand();

        Assert.AreEqual(0, Int("g.Total()"));
        Assert.AreEqual(3, Int("g.NextHandle()"), "a spent handle is never minted again: the idempotency keys hang on it");
    }

    [TestMethod]
    public void APointToldByAPeer_IsTakenUp_WithItsOwnHandle()
    {
        Assign(1, 3.0, 4.0);

        perf.Actor.Using(@"
            g.Take(@x, @y);
        ")
        .WithParameters(p => {
            p["x", typeof(double)] = 8.5;
            p["y", typeof(double)] = 2.5;
        })
        .PerformCommand();

        Assert.AreEqual(2, Int("g.Total()"));
        Assert.IsTrue(Bool("g.Knows(2)"), "the taken point got handle 2");
        Assert.AreEqual(3, Int("g.NextHandle()"));
    }

    // ---- helpers: the same perform shapes the host uses ----

    private void Assign(int id, double x, double y) =>
        perf.Actor.Using(@"
            g.Assign(@id, @x, @y);
        ")
        .WithParameters(p => {
            p["id", typeof(int)]    = id;
            p["x",  typeof(double)] = x;
            p["y",  typeof(double)] = y;
        })
        .PerformCommand();

    private string Complete(int id) =>
        perf.Actor.Using(
            @"
                Check(g.Knows(@id) && g.IsPending(@id)) Error 'mission is not pending';
            ",
            @"
                g.Complete(@id);
            ")
        .WithParameters(p => {
            p["id", typeof(int)] = id;
        })
        .PerformCheckThenCommand();

    private int Int(string expression)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using($"@value = {expression};")
        .WithParameters(rented, p => {
            p[Parameter.Out, "value", typeof(int)] = default;
        })
        .PerformQuery();
        return rented["value"].GetValue<int>();
    }

    private double Double(string expression)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using($"@value = {expression};")
        .WithParameters(rented, p => {
            p[Parameter.Out, "value", typeof(double)] = default;
        })
        .PerformQuery();
        return rented["value"].GetValue<double>();
    }

    private bool Bool(string expression)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using($"@value = {expression};")
        .WithParameters(rented, p => {
            p[Parameter.Out, "value", typeof(bool)] = default;
        })
        .PerformQuery();
        return rented["value"].GetValue<bool>();
    }
}
