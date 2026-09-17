using System.Globalization;
using Choreography.Theater;
using GolemDomain;
using GolemDomain.Layouts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;
using Puppeteer.EventSourcing.Interpreter.Formatters;

namespace GolemTest;

// A LAB, not an acceptance test (17-sep-2026, Juan: "esos print terminarán llamando a Push de RobotToRos automáticamente al
// terminar el script, sin necesidad de que nosotros lo disparemos"). A command's print is PULL (it returns to the caller);
// only a Reaction's Program.Emit is PUSHED to the OutputTarget. So the question is: which ONE reaction fires reliably on
// EVERY act the host writes, so that the next order reaches the body without the host dispatching it? The morning's lab
// (lab-patterns.txt) defined many reactions on one actor and saw them fire inconsistently. Here each candidate pattern
// gets an actor of its own, the acts are performed exactly as the host writes them, and every push is counted per act.
// Findings go to lab-push.txt beside the test binary.
[TestClass]
public sealed class OrderPushLabTests
{
    private sealed class Sink : IOutputSink
    {
        public readonly List<(string Reaction, string Document, string Bindings)> Pushed = new();
        public void Push(in PushDocument d)
        {
            lock (Pushed) Pushed.Add((d.ReactionName, d.Document, string.Join(",", d.Bindings.Select(b => $"{b.Key}={b.Value}"))));
        }
    }

    private const string NextOrder = @"
        if (g.HasPendingMission()) { print g.Underway().Id 'route', g.Underway().Order 'order'; }";

    [TestInitialize]
    public void PinTheCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [TestMethod]
    public void WhichSingleReaction_PushesTheNextOrder_AfterEveryAct()
    {
        var findings = new List<string>();
        var patterns = new (string Name, string Pattern)[]
        {
            ("find-id",        "[_:Golem].Find($id)"),
            ("visit-2",        "[_:Golem].Visit(_, _)"),
            ("then",           "[_:Route].Then(_)"),
            ("turn",           "[_:Route].Turn()"),
            ("reach",          "[_:Route].Reach(_)"),
            ("bump-2",         "[_:Route].Bump(_, _)"),
            ("decide",         "[_:Route].Decide(_)"),
            ("pause",          "[_:Golem].Pause(_)"),
            ("abandon-why",    "[_:Route].Abandon($why)"),
            ("pending-routes", "[_:Golem].PendingRoutes()"),
        };
        foreach (var (name, pattern) in patterns)
        {
            var sink = new Sink();
            var perf = new PerformanceV2("push-lab-" + Guid.NewGuid().ToString("N"), DomainLibrary.Assembly);
            perf.ConfigureStorage(DatabaseType.IN_MEMORY, "push-lab-" + name);
            perf.OutputTarget(sink, new JsonFormatter());
            try
            {
                perf.Actor.Reactions.DefineReaction("next-order")
                    .Cue().Company().WithSharedHydration()
                    .Seek("Act").One()
                        .OnMatch(pattern)
                    .Program.Emit(NextOrder);
            }
            catch (Exception e) { findings.Add($"{name} ({pattern}): DEFINITION REFUSED -> {e.Message}"); perf.Dispose(); continue; }
            perf.Start();
            perf.Actor.Using(
                "upgrade('body_v1') { radius = Meters(0.25); speed = MetersPerSecond(2.0); linger = Seconds(6.0); retreat = Meters(0.6); body = Body(radius, speed, linger, retreat); }\n"
                + Catalog.Warehouse().AsRelease()
                + "upgrade('init') { collisions = Collisions(map); g = Golem(body, map, collisions); }\n")
            .PerformCommand();

            var acts = new (string Act, string Script, Action<dynamic> Bind)[]
            {
                ("visit",   "{ from = Position(@fx, @fy); point = Position(@x, @y); route = g.Visit(from, point); }\n" + NextOrder,
                            p => { p["fx", typeof(double)] = 2.0; p["fy", typeof(double)] = 1.5; p["x", typeof(double)] = 2.0; p["y", typeof(double)] = 9.5; }),
                ("then",    "{ route = g.Find(@id); point = Position(@x, @y); route.Then(point); }\n" + NextOrder,
                            p => { p["id", typeof(int)] = 1; p["x", typeof(double)] = 9.0; p["y", typeof(double)] = 1.5; }),
                ("turn",    "{ route = g.Find(@id); route.Turn(); }\n" + NextOrder, p => { p["id", typeof(int)] = 1; }),
                ("reach",   "{ route = g.Find(@id); point = Position(@x, @y); route.Reach(point); }\n" + NextOrder,
                            p => { p["id", typeof(int)] = 1; p["x", typeof(double)] = 0.75; p["y", typeof(double)] = 3.0; }),
                ("pause",   "{ route = g.Pause(Pose(2.0, 9.5, 0.0)); }\n" + NextOrder, p => { p["id", typeof(int)] = 1; }),
                ("resume",  "{ route = g.Resume(Pose(2.0, 9.5, 0.0)); }\n" + NextOrder, p => { p["id", typeof(int)] = 1; }),
                ("bump",    "{ route = g.Find(@id); touch = Pose(@x, @y, @h); me = Pose(@px, @py, @h); route.Bump(touch, me); }\n" + NextOrder,
                            p => { p["id", typeof(int)] = 1; p["x", typeof(double)] = 0.75; p["y", typeof(double)] = 5.0; p["h", typeof(double)] = -1.57; p["px", typeof(double)] = 0.75; p["py", typeof(double)] = 5.25; }),
                ("decide",  "{ route = g.Find(@id); from = Position(@x, @y); route.Decide(from); }\n" + NextOrder,
                            p => { p["id", typeof(int)] = 1; p["x", typeof(double)] = 0.75; p["y", typeof(double)] = 6.0; }),
                ("abandon", "{ route = g.Find(@id); route.Abandon(@why); }\n" + NextOrder, p => { p["id", typeof(int)] = 1; p["why", typeof(string)] = "the lab says so"; }),
                ("visit-b", "{ from = Position(@fx, @fy); point = Position(@x, @y); route = g.Visit(from, point); }\n" + NextOrder,
                            p => { p["fx", typeof(double)] = 2.0; p["fy", typeof(double)] = 9.5; p["x", typeof(double)] = 9.0; p["y", typeof(double)] = 9.5; }),
                ("letgo",   "foreach (route in g.PendingRoutes()) { route.Abandon(@reason); }\n" + NextOrder, p => { p["reason", typeof(string)] = "let go"; }),
            };
            var line = new List<string>();
            foreach (var (act, script, bind) in acts)
            {
                int before; lock (sink.Pushed) before = sink.Pushed.Count;
                string returned;
                try { returned = perf.Actor.Using(script).WithParameters(bind).PerformCommand(); }
                catch (Exception e) { line.Add($"{act}:ERR({e.GetType().Name})"); continue; }
                Thread.Sleep(700);   // .Cue() reactions run after the entry lands
                int after; lock (sink.Pushed) after = sink.Pushed.Count;
                string pushed = after > before ? sink.Pushed[after - 1].Document.Replace("\n", " ") : "-";
                line.Add($"{act}:{(after - before)}×push{(after > before ? $"[{pushed}]" : "")} ret[{returned.Replace("\n", " ")}]");
            }
            Thread.Sleep(1500);
            int total; lock (sink.Pushed) total = sink.Pushed.Count;
            findings.Add($"{name} ({pattern}): {total} pushes — " + string.Join(" | ", line));
            perf.Dispose();
        }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "lab-push.txt"), findings);
        Assert.IsTrue(findings.Count > 0);
    }

    // The real configuration on ONE actor: the speech reactions (they match exposes) plus one next-order reaction per act
    // shape that opens or moves a route — Find($id) for every act on a route in hand, Visit/Cover(_, _) for the errand,
    // Follow(_) for a told point — emitting a print that ALWAYS says something (so the sink is pushed even when nothing
    // is pending and the body must stop). Does every act push exactly once, with the echo reactions around?
    [TestMethod]
    public void TheRealSet_OneNextOrderReactionPerActShape_PushesOncePerAct()
    {
        const string always = @"
            print g.HasPendingMission() 'pending';
            if (g.HasPendingMission()) { print g.Underway().Id 'route', g.Underway().Order 'order'; }";
        var sink = new Sink();
        var perf = new PerformanceV2("push-lab-real-" + Guid.NewGuid().ToString("N"), DomainLibrary.Assembly);
        perf.ConfigureStorage(DatabaseType.IN_MEMORY, "push-lab-real");
        perf.OutputTarget(sink, new JsonFormatter());
        // the speech, as GolemSpeech defines it (no peers to tell here: the reactions match and print instead)
        perf.Actor.Reactions.DefineReaction("echo-bumped").Cue().Company().WithSharedHydration().Seek("Bumped").One()
            .OnMatch("expose $x x, $y y, $heading heading, $who who, $px px, $py py;").Program.Emit("print @x 'bumpedAt';");
        perf.Actor.Reactions.DefineReaction("echo-reached").Cue().Company().WithSharedHydration().Seek("Reached").One()
            .OnMatch("expose $missionId rid, $x rx, $y ry;").Program.Emit("print @x 'reachedAt';");
        foreach (var (name, pattern) in new[] { ("next-order-find", "[_:Golem].Find($id)"), ("next-order-visit", "[_:Golem].Visit(_, _)"), ("next-order-cover", "[_:Golem].Cover(_, _)"), ("next-order-follow", "[_:Golem].Follow(_)") })
            perf.Actor.Reactions.DefineReaction(name).Cue().Company().WithSharedHydration().Seek("Act").One().OnMatch(pattern).Program.Emit(always);
        perf.Start();
        perf.Actor.Using(
            "upgrade('body_v1') { radius = Meters(0.25); speed = MetersPerSecond(2.0); linger = Seconds(6.0); retreat = Meters(0.6); body = Body(radius, speed, linger, retreat); }\n"
            + Catalog.Warehouse().AsRelease()
            + "upgrade('init') { collisions = Collisions(map); g = Golem(body, map, collisions); }\n")
        .PerformCommand();
        var acts = new (string Act, string Script, Action<dynamic> Bind)[]
        {
            ("visit",   "{ from = Position(@fx, @fy); point = Position(@x, @y); route = g.Visit(from, point); }\n" + always,
                        p => { p["fx", typeof(double)] = 2.0; p["fy", typeof(double)] = 1.5; p["x", typeof(double)] = 2.0; p["y", typeof(double)] = 9.5; }),
            ("then",    "{ route = g.Find(@id); point = Position(@x, @y); route.Then(point); }\n" + always,
                        p => { p["id", typeof(int)] = 1; p["x", typeof(double)] = 9.0; p["y", typeof(double)] = 1.5; }),
            ("turn",    "{ route = g.Find(@id); route.Turn(); }\n" + always, p => { p["id", typeof(int)] = 1; }),
            ("reach",   "{ route = g.Find(@id); point = Position(@x, @y); route.Reach(point); }\n" + always,
                        p => { p["id", typeof(int)] = 1; p["x", typeof(double)] = 0.75; p["y", typeof(double)] = 3.0; }),
            ("bump",    "{ route = g.Find(@id); touch = Pose(@x, @y, @h); me = Pose(@px, @py, @h); route.Bump(touch, me); }\nexpose @x x, @y y, @h heading, @name who, @px px, @py py;\n" + always,
                        p => { p["id", typeof(int)] = 1; p["x", typeof(double)] = 0.75; p["y", typeof(double)] = 5.0; p["h", typeof(double)] = -1.57; p["px", typeof(double)] = 0.75; p["py", typeof(double)] = 5.25; p["name", typeof(string)] = "lab"; }),
            ("pause",   "{ route = g.Pause(Pose(2.0, 9.5, 0.0)); }\n" + always, p => { p["id", typeof(int)] = 1; }),
            ("resume",  "{ route = g.Resume(Pose(2.0, 9.5, 0.0)); }\n" + always, p => { p["id", typeof(int)] = 1; }),
            ("follow",  "{ point = Position(@x, @y); g.Follow(point); }", p => { p["x", typeof(double)] = 5.5; p["y", typeof(double)] = 9.5; }),
            ("fail",    "{ route = g.Find(@id); route.Fail(@why); }\n" + always, p => { p["id", typeof(int)] = 1; p["why", typeof(string)] = "the lab says so"; }),
            ("decide2", "{ route = g.Find(@id); from = Position(@x, @y); route.Decide(from); }\n" + always,
                        p => { p["id", typeof(int)] = 2; p["x", typeof(double)] = 2.0; p["y", typeof(double)] = 9.5; }),
            ("cover",   "{ from = Position(@fx, @fy); point = Position(@x, @y); route = g.Cover(from, point); }\n" + always,
                        p => { p["fx", typeof(double)] = 2.0; p["fy", typeof(double)] = 9.5; p["x", typeof(double)] = 9.0; p["y", typeof(double)] = 9.5; }),
            ("reach-stop", "{ route = g.Find(@id); point = Position(@x, @y); route.Reach(point); }\nexpose @id rid, @x rx, @y ry;\n" + always,
                        p => { p["id", typeof(int)] = 2; p["x", typeof(double)] = 5.5; p["y", typeof(double)] = 9.5; }),
        };
        var findings = new List<string>();
        foreach (var (act, script, bind) in acts)
        {
            int before; lock (sink.Pushed) before = sink.Pushed.Count;
            string returned;
            try { returned = perf.Actor.Using(script).WithParameters(bind).PerformCommand(); }
            catch (Exception e) { findings.Add($"{act}: ERR {e.GetType().Name}: {e.Message}"); continue; }
            Thread.Sleep(900);
            List<(string Reaction, string Document, string Bindings)> mine;
            lock (sink.Pushed) mine = sink.Pushed.Skip(before).ToList();
            findings.Add($"{act}: {mine.Count} push(es) {string.Join(" ", mine.Select(m => $"{m.Reaction}[{m.Document.Replace("\n", " ")}]"))} ret[{returned.Replace("\n", " ")}]");
        }
        Thread.Sleep(1500);
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "lab-push-real.txt"), findings);
        perf.Dispose();
        Assert.IsTrue(findings.Count > 0);
    }
}
