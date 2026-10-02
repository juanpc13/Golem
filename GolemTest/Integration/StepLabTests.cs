using System.Globalization;
using Choreography.Theater;
using GolemAPI.Choreography;
using GolemDomain;
using GolemDomain.Layouts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;
using Puppeteer.EventSourcing.Interpreter.Formatters;

namespace GolemTest;

// LAB (1-oct-2026, ajuste 77): the uptake of a peer's `PlacedAt` failed in the mock with "Variable 'id' has not been defined" — a name of
// no script of its own. Statement by statement, on a real actor, to find which one the engine trips on.
[TestClass]
public class StepLabTests
{
    private PerformanceV2 perf;

    [TestInitialize]
    public void AGolemIsBorn()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string name = "step-lab-" + Guid.NewGuid().ToString("N");
        perf = new PerformanceV2(name, DomainLibrary.Assembly);
        perf.ConfigureStorage(DatabaseType.IN_MEMORY, name);
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
                    g = Golem(body, 'blue');
                    scenario = g.Stage(warehouse);
                    g.Enter(scenario);
                }
            ").PerformCommand();
        perf.Actor.Using("{ me = Pose(@px, @py, @ptheta); g.Wake(me); }")
            .WithParameters(p => { p["px", typeof(double)] = 6.5; p["py", typeof(double)] = 7.5; p["ptheta", typeof(double)] = 0.0; })
            .PerformCommand();
    }

    [TestCleanup]
    public void TheGolemRests() => perf.Dispose();

    private string Try(string script, Action<Parameters> parameters = null)
    {
        try
        {
            var invocation = perf.Actor.Using(script);
            if (parameters != null) invocation = invocation.WithParameters(parameters);
            return "ok: " + invocation.PerformCommand();
        }
        catch (Exception ex) { return "FAILED: " + GolemEmbodiment.Reason(ex); }
    }

    // Do the reactions on [_:Muster].Placed(_) — with and without an expose — fire on this golem's own Placed act when a peer's word
    // (an uptake, Placed without expose) was written just before it? In the mock the LAST golem placed told nobody.
    [TestMethod]
    public void TheReactionsOnPlaced_FireOnTheOwnAct_AfterAPeersWordWasTaken()
    {
        string name = "step-lab-reactions-" + Guid.NewGuid().ToString("N");
        var p2 = new PerformanceV2(name, DomainLibrary.Assembly);
        p2.ConfigureStorage(DatabaseType.IN_MEMORY, name);
        var sink = new RecordingSink();
        foreach (var (reaction, pattern) in new[] {
            ("probe-placed", "[_:Muster].Placed(_)"),
            ("probe-placed-expose", "[_:Muster].Placed(_) expose $who who, $call call;"),
            ("probe-find", "[_:Golem].Find($id)") })
            p2.Actor.Reactions.DefineReaction(reaction).Cue().Company().WithSharedHydration().Seek("Act").One().OnMatch(pattern)
                .Program.Emit("{ print g.Choreography.Current.PlacedCount 'placed'; }");
        p2.Start();
        p2.OutputTarget(sink, new JsonFormatter());
        p2.Actor.Using(
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
                    g = Golem(body, 'blue');
                    scenario = g.Stage(warehouse);
                    g.Enter(scenario);
                }
            ").PerformCommand();
        p2.Actor.Using("{ me = Pose(@px, @py, @ptheta); g.Wake(me); }")
            .WithParameters(p => { p["px", typeof(double)] = 6.5; p["py", typeof(double)] = 7.5; p["ptheta", typeof(double)] = 0.0; })
            .PerformCommand();
        p2.Actor.Using(@"
            {
                from = g.Destination;
                center = Position(@cx, @cy);
                side = Meters(@sideLength);
                formation = g.Choreography.Formation(@figure, center, side);
                fleet = Fleet(@names);
                me = fleet.Member(g);
                muster = g.Choreography.Muster(@callId, formation, fleet);
                route = muster.Join(from, me);
                print route.Id 'route', route.Order 'action';
            }").WithParameters(p => {
                p["cx", typeof(double)] = 5.5; p["cy", typeof(double)] = 5.5; p["sideLength", typeof(double)] = 2.0;
                p["figure", typeof(string)] = "square"; p["callId", typeof(string)] = "blue-1"; p["names", typeof(string)] = "blue,red";
            }).PerformCommand();
        // walk the route to its end, as the arrivals do
        for (int i = 0; i < 6; i++)
        {
            string state = p2.Actor.Using("{ print g.HasPendingMission() 'v'; }").PerformCommand();
            if (!state.Contains("true")) break;
            p2.Actor.Using("{ route = g.Underway(); leg = route.NextLeg; route.Arrive(leg); print route.Id 'route', route.Order 'action'; }").PerformCommand();
        }
        sink.Pushed.Clear();
        // the peer's word first (an uptake: Placed without expose), then the own act (Placed with expose)
        p2.Actor.Using(GolemEmbodiment.UptakePlacedAt).WithParameters(p => { p["call", typeof(string)] = "blue-1"; p["who", typeof(string)] = "red"; }).PerformCommand();
        var afterUptake = sink.Pushed.Select(x => x.Reaction).ToList();
        sink.Pushed.Clear();
        string own = p2.Actor.Using(@"
            {
                muster = g.Choreography.Current;
                me = muster.Me;
                route = muster.Placed(me);
                print route.Id 'route', route.Status 'ended';
                expose me.Name who, muster.Call call;
            }").PerformCommand();
        Thread.Sleep(500);
        var afterOwn = sink.Pushed.Select(x => x.Reaction + ":" + x.Document).ToList();
        p2.Dispose();
        Console.WriteLine("after uptake: " + string.Join(", ", afterUptake));
        Console.WriteLine("own print: " + own);
        Console.WriteLine("after own: " + string.Join(", ", afterOwn));
        Assert.IsTrue(afterOwn.Any(r => r.StartsWith("probe-placed-expose")), "the reaction with the expose fires on the own act — after uptake: " + string.Join(", ", afterUptake) + " | after own: " + string.Join(", ", afterOwn));
    }

    private sealed class RecordingSink : IOutputSink
    {
        public readonly List<(string Reaction, string Document)> Pushed = new();
        public void Push(in PushDocument document) { lock (Pushed) Pushed.Add((document.ReactionName, document.Document.ReplaceLineEndings(" "))); }
    }

    // The word that COMPLETES the round, taken by the uptake after the own Placed: is it recorded? (In the mock the last word never was.)
    [TestMethod]
    public void TheWordThatCompletesTheRound_IsTakenByTheUptake()
    {
        var log = new List<string>();
        log.Add("join: " + Try(@"
            {
                from = g.Destination;
                center = Position(@cx, @cy);
                side = Meters(@sideLength);
                formation = g.Choreography.Formation(@figure, center, side);
                fleet = Fleet(@names);
                me = fleet.Member(g);
                muster = g.Choreography.Muster(@callId, formation, fleet);
                route = muster.Join(from, me);
                print route.Id 'route', route.Order 'action';
            }", p => {
                p["cx", typeof(double)] = 5.5; p["cy", typeof(double)] = 5.5; p["sideLength", typeof(double)] = 2.0;
                p["figure", typeof(string)] = "square"; p["callId", typeof(string)] = "blue-1"; p["names", typeof(string)] = "blue,red";
            }));
        for (int i = 0; i < 6 && Try("{ print g.HasPendingMission() 'v'; }").Contains("true"); i++)
            log.Add("walk: " + Try("{ route = g.Underway(); leg = route.NextLeg; route.Arrive(leg); print route.Id 'route', route.Order 'action'; }"));
        log.Add("reached: " + Try("{ print g.Choreography.Current.Reached 'v'; }"));
        log.Add("own placed: " + Try(@"
            {
                muster = g.Choreography.Current;
                me = muster.Me;
                route = muster.Placed(me);
                print route.Id 'route', route.Status 'ended', muster.PlacedCount 'placed';
                expose me.Name who, muster.Call call;
            }"));
        log.Add("reached after: " + Try("{ print g.Choreography.Current.Reached 'v'; }"));
        log.Add("red's word (completes the round): " + Try(GolemEmbodiment.UptakePlacedAt, p => { p["call", typeof(string)] = "blue-1"; p["who", typeof(string)] = "red"; }));
        log.Add("placed count: " + Try("{ print g.Choreography.Current.PlacedCount 'v', g.Choreography.Current.CanStep 'can'; }"));
        log.Add("rotate: " + Try(GolemEmbodiment.UptakeRotateTo, p => { p["sense", typeof(string)] = "clockwise"; p["stepId", typeof(string)] = "s1"; p["call", typeof(string)] = "blue-1"; }));
        Console.WriteLine(string.Join("\n", log));
        Assert.IsFalse(log.Any(l => l.Contains("FAILED")), string.Join("\n", log));
        StringAssert.Contains(log.Last(l => l.StartsWith("placed count")), "\"v\":2", string.Join("\n", log));
    }

    [TestMethod]
    public void TheUptakeOfAPeersWord_StatementByStatement()
    {
        var log = new List<string>();
        log.Add("join: " + Try(@"
            {
                from = g.Destination;
                center = Position(@cx, @cy);
                side = Meters(@sideLength);
                formation = g.Choreography.Formation(@figure, center, side);
                fleet = Fleet(@names);
                me = fleet.Member(g);
                muster = g.Choreography.Muster(@callId, formation, fleet);
                route = muster.Join(from, me);
                print route.Id 'route', route.Order 'action';
            }", p => {
                p["cx", typeof(double)] = 5.5; p["cy", typeof(double)] = 5.5; p["sideLength", typeof(double)] = 2.0;
                p["figure", typeof(string)] = "square"; p["callId", typeof(string)] = "blue-1"; p["names", typeof(string)] = "blue,red";
            }));
        // an act with @id first — the arrival's shape — then the uptake: does a parameter named id linger on the table?
        log.Add("arrive: " + Try(@"
            {
                route = g.Underway();
                leg = route.NextLeg;
                route.Arrive(leg);
                print route.Id 'route', route.Order 'action';
            }
            expose @id rid, @stop reached, @sx x, @sy y;", p => { p["id", typeof(int)] = 1; p["stop", typeof(bool)] = false; p["sx", typeof(double)] = 6.5; p["sy", typeof(double)] = 6.5; }));
        log.Add("uptake after @id: " + Try(GolemEmbodiment.UptakePlacedAt, p => { p["call", typeof(string)] = "blue-1"; p["who", typeof(string)] = "red"; }));
        log.Add("knows: " + Try("{ print g.Choreography.Knows(@call) 'v'; }", p => p["call", typeof(string)] = "blue-1"));
        log.Add("muster(call): " + Try("{ muster = g.Choreography.Muster(@call); print muster.Call 'v'; }", p => p["call", typeof(string)] = "blue-1"));
        log.Add("fleet.member: " + Try("{ muster = g.Choreography.Muster(@call); peer = muster.Fleet.Member(@who); print peer.Name 'v'; }",
            p => { p["call", typeof(string)] = "blue-1"; p["who", typeof(string)] = "red"; }));
        log.Add("heard: " + Try("{ muster = g.Choreography.Muster(@call); peer = muster.Fleet.Member(@who); route = muster.Heard(peer); print route.Id 'v'; }",
            p => { p["call", typeof(string)] = "blue-1"; p["who", typeof(string)] = "red"; }));
        log.Add("uptake whole: " + Try(GolemEmbodiment.UptakePlacedAt, p => { p["call", typeof(string)] = "blue-1"; p["who", typeof(string)] = "red"; }));
        log.Add("rotate: " + Try("{ muster = g.Choreography.Current; move = muster.Formation.Rotate(@sense); muster.Queue(move, @stepId); print muster.Queued 'v'; }",
            p => { p["sense", typeof(string)] = "clockwise"; p["stepId", typeof(string)] = "s1"; }));
        log.Add("uptake rotate whole: " + Try(GolemEmbodiment.UptakeRotateTo, p => { p["sense", typeof(string)] = "clockwise"; p["stepId", typeof(string)] = "s2"; p["call", typeof(string)] = "blue-1"; }));
        Console.WriteLine(string.Join("\n", log));
        Assert.IsFalse(log.Any(l => l.Contains("FAILED")), string.Join("\n", log));
    }
}
