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
    public void AGolemIsBorn()
    {
        // The DSL renders numbers into the journal with the current culture: pin it.
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        // One actor and one store per test: the in-memory store is shared by name.
        string name = "golem-under-test-" + Guid.NewGuid().ToString("N");
        perf = new PerformanceV2(name, GolemDomain.Assembly);
        perf.ConfigureStorage(DatabaseType.IN_MEMORY, name);
        perf.Start();
        perf.Actor.Using(@"
            upgrade('init')    { g = Golem(); }
            upgrade('body_v1') { g.Embody(2.0); }
            upgrade('pace_v1') { g.Pace(6); }
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
        Assert.IsFalse(Bool("g.WasTold(1)"), "the operator ordered it");
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
    public void AFailedMission_KeepsItsReason_AndIsNoLongerPending()
    {
        Assign(1, 3.0, 4.0);

        perf.Actor.Using(@"
            g.Fail(@id, @reason);
        ")
        .WithParameters(p => {
            p["id",     typeof(int)]    = 1;
            p["reason", typeof(string)] = "stuck against a wall";
        })
        .PerformCommand();

        Assert.IsFalse(Bool("g.IsPending(1)"));
        Assert.IsFalse(Bool("g.HasPendingMission()"));
        Assert.AreEqual("failed", Text("g.StatusOf(1)"));
    }

    [TestMethod]
    public void APointToldByAPeer_IsAssignedTold_WithItsOwnHandle()
    {
        Assign(1, 3.0, 4.0);

        AssignTold(8.5, 2.5);

        Assert.AreEqual(2, Int("g.Total()"));
        Assert.IsTrue(Bool("g.Knows(2)"), "the told point got handle 2");
        Assert.IsTrue(Bool("g.WasTold(2)"), "an AssignTold mission remembers it was told");
        Assert.AreEqual(3, Int("g.NextHandle()"));
    }

    [TestMethod]
    public void TheRoadLeft_RunsThroughEveryPendingPoint_InOrder_AndTheTimeCountsTheHolds()
    {
        Assign(1, 8.0, 5.0);   // 6 units from (2,5)
        AssignTold(8.0, 9.0);        // then 4 more, and a told point: one hold
        Assign(3, 2.0, 9.0);   // then 6 more
        long entriesBefore = perf.CurrentEntryId;

        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using(@"
            @left = g.DistanceLeft(@x, @y);
            @eta  = g.SecondsLeft(@x, @y);
        ")
        .WithParameters(rented, p => {
            p["x",     typeof(double)]                  = 2.0;
            p["y",     typeof(double)]                  = 5.0;
            p[Parameter.Out, "left", typeof(double)]    = default;
            p[Parameter.Out, "eta",  typeof(double)]    = default;
        })
        .PerformQuery();

        Assert.AreEqual(16.0, rented["left"].GetValue<double>(), 0.001, "6 + 4 + 6 through the three points");
        Assert.AreEqual(14.0, rented["eta"].GetValue<double>(), 0.001, "16 units at the body's 2 units/s, plus its 6 s pause at the told point");
        Assert.AreEqual(entriesBefore, perf.CurrentEntryId, "a query leaves no entry: the pose never touches the journal");
    }

    [TestMethod]
    public void WithNothingPending_TheRoadLeftIsZero()
    {
        Assign(1, 8.0, 5.0);
        Complete(1);

        Assert.AreEqual(0.0, Double("g.DistanceLeft(2.0, 5.0)"), 0.001);
    }

    [TestMethod]
    public void TheRoute_IsAnsweredWithNoParameters_AtTheBodysOwnSpeedAndPauses()
    {
        Assign(1, 8.0, 5.0);
        AssignTold(8.0, 9.0);        // 4 units after the first point, and a told point: one pause
        Assign(3, 2.0, 9.0);   // 6 more

        Assert.AreEqual(2.0, Double("g.Speed()"), 0.001, "the body release");
        Assert.AreEqual(6.0, Double("g.HoldAfterTold()"), 0.001, "the pace release");
        Assert.AreEqual(10.0, Double("g.RouteLength()"), 0.001, "point to point through the pending route");
        Assert.AreEqual(11.0, Double("g.RouteSeconds()"), 0.001, "10 units at 2 units/s, plus one 6 s pause");
    }

    [TestMethod]
    public void ABodyWithoutSpeed_IsRefused()
    {
        try
        {
            perf.Actor.Using(@"
                g.Embody(@speed);
            ")
            .WithParameters(p => {
                p["speed", typeof(double)] = 0.0;
            })
            .PerformCommand();
            Assert.Fail("a speed of zero must be refused");
        }
        catch (AssertFailedException) { throw; }
        catch (Exception ex)
        {
            StringAssert.Contains(ex.ToString(), "speed above zero");
        }
    }

    [TestMethod]
    public void ACompletedPoint_CanBeAnnounced_AndAPendingOneCannot()
    {
        Assign(1, 3.0, 4.0);
        Assign(2, 5.0, 6.0);
        Complete(1);

        perf.Actor.Using(@"
            g.Announce(@id);
        ")
        .WithParameters(p => {
            p["id", typeof(int)] = 1;
        })
        .PerformCommand();

        Assert.IsTrue(Bool("g.WasAnnounced(1)"));
        Assert.IsFalse(Bool("g.WasAnnounced(2)"));
        try
        {
            perf.Actor.Using(@"
                g.Announce(@id);
            ")
            .WithParameters(p => {
                p["id", typeof(int)] = 2;
            })
            .PerformCommand();
            Assert.Fail("a pending point has nothing to announce yet");
        }
        catch (AssertFailedException) { throw; }
        catch (Exception ex)
        {
            StringAssert.Contains(ex.ToString(), "only a completed point is announced");
        }
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

    private void AssignTold(double x, double y) =>
        perf.Actor.Using(@"
            g.AssignTold(@x, @y);
        ")
        .WithParameters(p => {
            p["x", typeof(double)] = x;
            p["y", typeof(double)] = y;
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

    private string Text(string expression)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using($"@value = {expression};")
        .WithParameters(rented, p => {
            p[Parameter.Out, "value", typeof(string)] = default;
        })
        .PerformQuery();
        return rented["value"].GetValue<string>();
    }
}
