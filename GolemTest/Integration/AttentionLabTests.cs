using System.Globalization;
using Choreography.Theater;
using GolemDomain;
using GolemDomain.Layouts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;
using Puppeteer.EventSourcing.Interpreter.Formatters;

namespace GolemTest;

// A LAB (21-oct-2026, propuesta 121 — the commitment to self, paper 0B): may the engine's deterministic clock `@Now` enter a Check and a
// command as a DateTime argument of the domain (`g.Commitments.IsDue(@Now)`, `commitment.Attend(@Now)`), and does a reaction on
// `[_:Commitment].Attend(_)` see the activation with its expose? Findings in lab-attention.txt beside the test binary.
[TestClass]
public sealed class AttentionLabTests
{
    private sealed class Sink : IOutputSink
    {
        public readonly List<(string Reaction, string Document)> Pushed = new();
        public void Push(in PushDocument d) { lock (Pushed) Pushed.Add((d.ReactionName, d.Document)); }
    }

    [TestInitialize]
    public void PinTheCulture() { CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; }

    [TestMethod]
    public void TheAttention_KeepsACommitmentWhenDue_WithTheEnginesNow_AndAReactionSeesIt()
    {
        var findings = new List<string>();
        var sink = new Sink();
        var perf = new PerformanceV2("attention-lab-" + Guid.NewGuid().ToString("N"), DomainLibrary.Assembly);
        perf.ConfigureStorage(DatabaseType.IN_MEMORY, "attention-lab");
        perf.OutputTarget(sink, new JsonFormatter());
        try
        {
            perf.Actor.Reactions.DefineReaction("echo-ask-lab").Cue().Company().WithSharedHydration()
                .Seek("Asked").One().OnMatch("[_:Commitment].Attend(_) expose $call call, $to to, $asked asked;")
                .Program.Emit("{ print g.Name 'who'; }");
            findings.Add("A reaction on [_:Commitment].Attend(_) with an expose: defined");
        }
        catch (Exception e) { findings.Add($"A reaction DEFINITION REFUSED -> {e.GetType().Name}: {e.Message}"); }
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
            + Catalog.OpenFloor().AsRelease()
            + @"
                upgrade('init') {
                    g = Golem(body, 'cyan');
                    scenario = g.Stage(openFloor);
                    g.Enter(scenario);
                }
            ").PerformCommand();

        // B — the formation that starts after another arms the inquiry: committed in cyan, the asker
        try
        {
            string b = perf.Actor.Using(@"
                {
                    center = Position(5.5, 5.5);
                    measure = Meters(2.0);
                    turn = Degrees(0.0);
                    fleet = Fleet(@names);
                    formation = g.Choreography.Form(@formationName, @figure, center, measure, turn, fleet, @by, @stamp);
                    every = Seconds(@everySeconds);
                    formation.Follows(@after, @askTo, every);
                    print formation.Name 'called', formation.IsCommitted 'committed', formation.EverySeconds 'every', g.Commitments.PendingCount 'pending';
                }
            ").WithParameters(p =>
            {
                p["names", typeof(string)] = "cyan,orange";
                p["formationName", typeof(string)] = "tri-right";
                p["figure", typeof(string)] = "triangle";
                p["by", typeof(string)] = "Rank";
                p["stamp", typeof(string)] = "stamp-1";
                p["everySeconds", typeof(double)] = 5.0;
                p["after", typeof(string)] = "tri-left";
                p["askTo", typeof(string)] = "red";
            }).PerformCommand();
            findings.Add("B follows -> " + b);
            StringAssert.Contains(b, "\"committed\":true");
        }
        catch (Exception e) { findings.Add($"B follows REFUSED -> {e.GetType().Name}: {e.Message}"); throw; }

        // C — @Now in a query: is it due? (never kept: at once)
        try
        {
            string c = perf.Actor.Using("{ print g.Commitments.IsDue(@Now) 'due'; }").PerformQuery();
            findings.Add("C IsDue(@Now) in a query -> " + c);
            StringAssert.Contains(c, "\"due\":true");
        }
        catch (Exception e) { findings.Add($"C IsDue(@Now) in a query REFUSED -> {e.GetType().Name}: {e.Message}"); throw; }

        // D — the attention: Check with @Now, then the activation with @Now; the expose for the tell
        string attend = @"
            {
                commitment = g.Commitments.Due(@Now);
                formation = g.Choreography.Find(commitment.About);
                commitment.Attend(@Now);
                print formation.Name 'called', formation.Asked 'asked', commitment.Activations 'kept', commitment.Every.InSeconds 'every';
                expose formation.After call, formation.AskTo to, formation.Asked asked;
            }
        ";
        try
        {
            string d = perf.Actor.Using("Check(g.Commitments.IsDue(@Now)) Error 'nothing is due';", attend).PerformCheckThenCommand();
            findings.Add("D Attend(@Now) as CheckThenCommand -> " + d);
            StringAssert.Contains(d, "\"kept\":1");
        }
        catch (Exception e) { findings.Add($"D Attend(@Now) REFUSED -> {e.GetType().Name}: {e.Message}"); throw; }

        // E — at once again: not due (kept a moment ago) — the Check refuses, nothing written
        try
        {
            string e = perf.Actor.Using("Check(g.Commitments.IsDue(@Now)) Error 'nothing is due';", attend).PerformCheckThenCommand();
            findings.Add("E Attend again at once -> " + e);
            StringAssert.Contains(e, "nothing is due");
        }
        catch (Exception ex) { findings.Add($"E Attend again at once -> {ex.GetType().Name}: {ex.Message}"); }

        // F — the reaction fired on the activation?
        Thread.Sleep(500);
        lock (sink.Pushed) findings.Add($"F pushed by the reaction: {sink.Pushed.Count} -> {string.Join(" | ", sink.Pushed.Select(x => x.Reaction + ": " + x.Document))}");
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "lab-attention.txt"), findings);
        Assert.IsTrue(sink.Pushed.Count >= 1, "the reaction on [_:Commitment].Attend(_) fired: " + string.Join(" | ", findings));
    }
}
