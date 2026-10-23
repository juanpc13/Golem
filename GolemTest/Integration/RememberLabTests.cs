using System.Globalization;
using System.Text.Json;
using Choreography.Theater;
using GolemAPI.Membrane;
using GolemDomain;
using GolemDomain.Layouts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;
using Puppeteer.EventSourcing.Interpreter.Formatters;

namespace GolemTest;

// A LAB (21-oct-2026, propuesta 122 — the pull by the engine's own `remember`, paper 0B): does the asker's recurrence, armed INSIDE the Follows act
// (`if (formation.IsAsker) { remember (@firstAt, @everyTicks) { … formation.Ask(); expose …; } while (g.Choreography.StillAsks(…)); }`), ask every
// period through the host's AttentionClock, does a Cue on `[_:Formation].Ask()` see every activation with its expose, does the `while` close it when
// the formation is dissolved, and does a pending recurrence survive a restart (the guide's caution: the @Now rehydration fix is not merged)?
// FINDING (first run): `remember (start, every) { … } while (cond)` with no times count fired ONCE — RememberStatement.EvaluateTimes returns 1 when the
// count is omitted, whatever the condition — so the count is given (an hour of questions) and the while closes it first. Findings in lab-remember.txt.
[TestClass]
public sealed class RememberLabTests
{
    private sealed class Sink : IOutputSink
    {
        public readonly List<(string Reaction, string Document)> Pushed = new();
        public void Push(in PushDocument d) { lock (Pushed) Pushed.Add((d.ReactionName, d.Document)); }
        public int Count { get { lock (Pushed) return Pushed.Count; } }
    }

    private const string Releases = @"
        upgrade('body_v1') {
            radius = Meters(0.25);
            speed = MetersPerSecond(2.0);
            linger = Seconds(6.0);
            retreat = Meters(0.6);
            body = Body(radius, speed, linger, retreat);
        }
    ";

    // The role's own Follows act (ChoreographerRol.Sequenced), verbatim: the Form, then the Follows with the recurrence inside
    private const string FormAndFollow = @"
        {
            center = Position(5.5, 5.5);
            measure = Meters(2.0);
            turn = Degrees(0.0);
            fleet = Fleet(@names);
            formation = g.Choreography.Form(@formationName, @figure, center, measure, turn, fleet, @by, @stamp);
            period = Seconds(@everySeconds);
            formation.Follows(@after, period);
            if (formation.IsAsker) {
                remember (@firstAt, @everyTicks, @timesAtMost) {
                    asking = g.Choreography.Find(@formationName);
                    asking.Ask();
                    print asking.Name 'called', asking.Asked 'asked', @attempt 'attempt';
                    expose asking.After call, asking.Stamp nonce, asking.Asker who, asking.Asked asked;
                }
                while (g.Choreography.StillAsks(@formationName, @stamp));
            }
            print formation.Name 'called', formation.After 'after', formation.Asker 'asker', formation.EverySeconds 'every', formation.IsAsker 'asks';
        }
    ";

    [TestInitialize]
    public void PinTheCulture() { CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; }

    private static PerformanceV2 Build(string name, DatabaseType storage, string where, Sink sink, List<string> findings)
    {
        var perf = new PerformanceV2(name, DomainLibrary.Assembly);
        perf.ConfigureStorage(storage, where);
        perf.OutputTarget(sink, new JsonFormatter());
        try
        {
            perf.Actor.Reactions.DefineReaction("echo-ask-lab").Cue().Company().WithSharedHydration()
                .Seek("Asked").One().OnMatch("[_:Formation].Ask() expose $call call, $nonce nonce, $who who, $asked asked;")
                .Program.Emit("{ print g.Name 'who'; }");
            findings.Add("A reaction on [_:Formation].Ask() with an expose: defined");
        }
        catch (Exception e) { findings.Add($"A reaction DEFINITION REFUSED -> {e.GetType().Name}: {e.Message}"); throw; }
        return perf;
    }

    private static void Release(PerformanceV2 perf, string golem) =>
        perf.Actor.Using(Releases + Catalog.OpenFloor().AsRelease() + "upgrade('init') { g = Golem(body, @golemName); scenario = g.Stage(openFloor); g.Enter(scenario); }")
            .WithParameters(p => { p["golemName", typeof(string)] = golem; })
            .PerformCommand();

    private static string Follow(PerformanceV2 perf, double everySeconds, string stamp) =>
        perf.Actor.Using(FormAndFollow).WithParameters(p =>
        {
            p["names", typeof(string)] = "cyan,orange";
            p["formationName", typeof(string)] = "tri-right";
            p["figure", typeof(string)] = "triangle";
            p["by", typeof(string)] = "Rank";
            p["stamp", typeof(string)] = stamp;
            p["everySeconds", typeof(double)] = everySeconds;
            p["after", typeof(string)] = "tri-left";
            p["firstAt", typeof(DateTime)] = DateTime.Now.AddSeconds(everySeconds);
            p["everyTicks", typeof(long)] = TimeSpan.FromSeconds(everySeconds).Ticks;
            p["timesAtMost", typeof(int)] = Math.Max(1, (int)Math.Ceiling(3600.0 / everySeconds));   // the engine takes a times count omitted as ONE (finding): the bound must be said
        }).PerformCommand();

    private static int Asked(PerformanceV2 perf) =>
        JsonDocument.Parse(perf.Actor.Using("{ print g.Choreography.Find('tri-right').Asked 'asked'; }").PerformQuery()).RootElement.GetProperty("asked").GetInt32();

    [TestMethod]
    public void TheAskersRecurrence_ArmedInsideTheFollowsAct_AsksEveryPeriod_IsSeenByTheCue_AndStopsWhenDissolved()
    {
        var findings = new List<string>();
        var sink = new Sink();
        var perf = Build("remember-lab-" + Guid.NewGuid().ToString("N"), DatabaseType.IN_MEMORY, "remember-lab", sink, findings);
        perf.Start();
        var clock = new AttentionClock();
        perf.Actor.UseAttentionClock(clock);
        Release(perf, "cyan");
        try
        {
            // B — the Follows act with the recurrence inside, every 0.3 s (the lab's pace); cyan is the asker
            string b;
            try { b = Follow(perf, 0.3, "stamp-1"); findings.Add("B follows + remember -> " + b); }
            catch (Exception e) { findings.Add($"B follows + remember REFUSED -> {e.GetType().Name}: {e.Message}"); throw; }
            StringAssert.Contains(b, "\"asks\":true");
            Assert.AreEqual(0, Asked(perf), "nothing asked at arming: the first question comes one period later");

            // C — the clock knocks, the actor asks: three or more questions in 1.4 s, every one journaled and seen by the Cue
            Thread.Sleep(1400);
            int asked = Asked(perf);
            findings.Add($"C after 1.4 s -> asked {asked}, knocks {clock.Knocks}, cue pushes {sink.Count}");
            Assert.IsTrue(asked >= 3, $"asked {asked}: the recurrence should have asked at least three times");
            Assert.IsTrue(sink.Count >= 3, $"the cue saw {sink.Count} of {asked} activations");

            // D — a member that is not the asker arms nothing (the `if` keeps the remember out)
            var orangeSink = new Sink();
            var orangeFindings = new List<string>();
            var orange = Build("remember-lab-orange-" + Guid.NewGuid().ToString("N"), DatabaseType.IN_MEMORY, "remember-lab-orange", orangeSink, orangeFindings);
            orange.Start();
            var orangeClock = new AttentionClock();
            orange.Actor.UseAttentionClock(orangeClock);
            Release(orange, "orange");
            string d = Follow(orange, 0.3, "stamp-1");
            findings.Add("D orange follows -> " + d);
            StringAssert.Contains(d, "\"asks\":false");
            Thread.Sleep(700);
            Assert.AreEqual(0, Asked(orange), "orange never asks"); Assert.AreEqual(0, orangeClock.Knocks, "nothing armed in orange");
            orange.Dispose();

            // E — dissolved: the while no longer holds, the recurrence settles at the next knock and the questions stop
            perf.Actor.Using("{ formation = g.Choreography.Find(@formationName); g.Choreography.Dissolve(formation); }")
                .WithParameters(p => { p["formationName", typeof(string)] = "tri-right"; }).PerformCommand();
            Thread.Sleep(500);
            int afterDissolve = JsonDocument.Parse(perf.Actor.Using("{ print g.Choreography.Formations().Count 'n'; }").PerformQuery()).RootElement.GetProperty("n").GetInt32();
            int knocksThen = clock.Knocks;
            Thread.Sleep(800);
            findings.Add($"E dissolved -> formations in force {afterDissolve}, knocks {knocksThen} then {clock.Knocks}, cue pushes {sink.Count}");
            Assert.AreEqual(knocksThen, clock.Knocks, "settled: the clock knocks no more");
            Assert.IsTrue(sink.Count >= asked, $"the cue saw {sink.Count}, the actor asked at least {asked}");
        }
        finally
        {
            File.WriteAllLines("lab-remember.txt", findings);
            perf.Dispose();
        }
    }

    [TestMethod]
    public void APendingRecurrence_SurvivesARestart_AndGoesOnAsking()
    {
        var findings = new List<string>();
        string folder = Path.Combine(Path.GetTempPath(), "remember-lab-" + Guid.NewGuid().ToString("N")[..8]);
        Directory.CreateDirectory(folder);
        string name = "remember-restart";
        int before;
        {
            var sink = new Sink();
            var perf = Build(name, DatabaseType.FileSystem, $"path={folder}", sink, findings);
            perf.Start();
            perf.Actor.UseAttentionClock(new AttentionClock());
            Release(perf, "cyan");
            Follow(perf, 0.3, "stamp-1");
            Thread.Sleep(800);
            before = Asked(perf);
            findings.Add($"F before the restart -> asked {before}");
            Assert.IsTrue(before >= 1, "asked at least once before the restart");
            perf.Dispose();   // the process ends: the agenda in memory is gone, the journal keeps the arming and the activations
        }
        {
            var sink = new Sink();
            var perf = Build(name, DatabaseType.FileSystem, $"path={folder}", sink, findings);
            try
            {
                perf.Start();   // rehydration: the Follows act, the activations; the pending recurrence is rebuilt from the journal alone
                int replayed = Asked(perf);
                findings.Add($"G after the restart, before the clock -> asked {replayed} (the journal's count)");
                Assert.AreEqual(before, replayed, "the replay re-applied every activation the journal holds");
                var clock = new AttentionClock();
                perf.Actor.UseAttentionClock(clock);   // configured after rehydration, as a host does: it arms what is pending
                Thread.Sleep(1200);
                int after = Asked(perf);
                findings.Add($"H 1.2 s after the clock -> asked {after}, knocks {clock.Knocks}, cue pushes {sink.Count}");
                Assert.IsTrue(after > replayed, $"asked {after} after the restart, {replayed} before: the recurrence should go on");
            }
            finally
            {
                File.WriteAllLines("lab-remember-restart.txt", findings);
                perf.Dispose();
            }
        }
    }
}
