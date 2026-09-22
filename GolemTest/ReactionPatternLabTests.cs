using System.Globalization;
using Choreography.Theater;
using GolemDomain;
using GolemDomain.Layouts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;
using Puppeteer.EventSourcing.Interpreter.Formatters;

namespace GolemTest;

// A LAB, not an acceptance test (16-sep-2026, Juan: "hay que quitar los expose… ¿se pueden quitar todos?"). It asks the
// engine which reaction patterns match the braced, object-speaking acts the journal writes — a statement on a local
// variable inside { }, an id that enters through g.Find(@id), a point built by Position(@x, @y) — and which values a
// reaction can capture from them without an expose. The findings are written to lab-patterns.txt beside the test binary.
[TestClass]
public sealed class ReactionPatternLabTests
{
    private sealed class Sink : IOutputSink
    {
        public readonly List<(string Reaction, string Document, string Bindings)> Pushed = new();
        public void Push(in PushDocument d) =>
            Pushed.Add((d.ReactionName, d.Document, string.Join(",", d.Bindings.Select(b => $"{b.Key}={b.Value}"))));
    }

    [TestInitialize]
    public void PinTheCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [TestMethod]
    public void WhichPatterns_MatchTheBracedActs_WithoutAnExpose()
    {
        var sink = new Sink();
        var perf = new PerformanceV2("pattern-lab-" + Guid.NewGuid().ToString("N"), DomainLibrary.Assembly);
        perf.ConfigureStorage(DatabaseType.IN_MEMORY, "pattern-lab");
        perf.OutputTarget(sink, new JsonFormatter());
        var findings = new List<string>();
        var patterns = new (string Name, string Pattern)[]
        {
            ("A-reach-wild",      "[_:Route].Reach(_)"),
            ("C-find-id",         "[_:Golem].Find($id)"),
            ("D-visit-wild",      "[_:Golem].Visit(_)"),
            ("D2-visit-capture",  "[_:Golem].Visit($p)"),
            ("E-decide-wild",     "[_:Route].Decide(_)"),
            ("E2-turn-parens",    "[_:Route].Turn(_)"),
            ("F-pause-parens",    "[_:Golem].Pause(_)"),
            ("F2-pause-bare",     "[_:Route].Pause"),
            ("F3-resume-parens",  "[_:Golem].Resume(_)"),
            ("I-reach-typed",     "[_:Route].Reach(_:Pose)"),
            ("J-bump-wild",       "[_:Route].Bump(_)"),
            ("K-fail-capture",    "[_:Route].Fail($why)"),
            ("L-then-wild",       "[_:Route].Then(_)"),
            ("D3-visit-typed",    "[_:Golem].Visit(_:Position)"),
            ("M-visit-2args",     "[_:Golem].Visit(_, _)"),
        };
        foreach (var (name, pattern) in patterns)
        {
            try
            {
                perf.Actor.Reactions.DefineReaction(name)
                    .Cue().Company().WithSharedHydration()
                    .Seek("Act").One()
                        .OnMatch(pattern)
                    .Program.Emit("print g.Routes().Count 'routes';");
                findings.Add($"{name}: defined ({pattern})");
            }
            catch (Exception e) { findings.Add($"{name}: DEFINITION REFUSED ({pattern}) -> {e.GetType().Name}: {e.Message}"); }
        }
        perf.Start();
        perf.Actor.Using(
            "upgrade('body_v1') { radius = Meters(0.25); speed = MetersPerSecond(2.0); linger = Seconds(6.0); retreat = Meters(0.6); body = Body(radius, speed, linger, retreat); }\n"
            + Catalog.Warehouse().AsRelease()
            + "upgrade('init') { collisions = Collisions(map); g = Golem(body, map, collisions); }\n")
        .PerformCommand();

        // the acts as the host writes them, one per command so each entry is its own case (a turn reports the heading the
        // route asked, 2.52 toward the door's approach: since 22-sep a turn that leaves the body facing elsewhere is asked again); a dummy first so the
        // first act is not the first entry after the releases
        perf.Actor.Using("{ warm = map.Find(@area); }").WithParameters(p => { p["area", typeof(string)] = "kitchen"; }).PerformCommand();
        perf.Actor.Using("{ from = Position(@fx, @fy); point1 = Position(@x, @y); route = g.Visit(from, point1); point2 = Position(@x2, @y2); route.Then(point2); }")
            .WithParameters(p => { p["fx", typeof(double)] = 2.0; p["fy", typeof(double)] = 1.5; p["x", typeof(double)] = 2.0; p["y", typeof(double)] = 9.5; p["x2", typeof(double)] = 9.0; p["y2", typeof(double)] = 1.5; }).PerformCommand();
        perf.Actor.Using("{ route = g.Find(@id); from = Position(@vx, @vy); route.Decide(from); }")
            .WithParameters(p => { p["id", typeof(int)] = 1; p["vx", typeof(double)] = 2.0; p["vy", typeof(double)] = 1.5; }).PerformCommand();
        perf.Actor.Using("{ route = g.Find(@id); me = Pose(@x, @y, @t); route.Turn(me); }")
            .WithParameters(p => { p["id", typeof(int)] = 1; p["x", typeof(double)] = 2.0; p["y", typeof(double)] = 1.5; p["t", typeof(double)] = 2.52; }).PerformCommand();
        perf.Actor.Using("{ route = g.Pause(Pose(2.0, 9.5, 0.0)); }")
            .WithParameters(p => { p["id", typeof(int)] = 1; }).PerformCommand();
        perf.Actor.Using("{ route = g.Resume(Pose(2.0, 9.5, 0.0)); }")
            .WithParameters(p => { p["id", typeof(int)] = 1; }).PerformCommand();
        perf.Actor.Using("{ route = g.Find(@id); me = Pose(@x, @y, @t); route.Turn(me); }")   // resumed: the turn is asked again
            .WithParameters(p => { p["id", typeof(int)] = 1; p["x", typeof(double)] = 2.0; p["y", typeof(double)] = 9.5; p["t", typeof(double)] = -1.745; }).PerformCommand();   // resumed from the kitchen: the door's approach lies south-south-west
        perf.Actor.Using("{ route = g.Find(@id); me = Pose(@x, @y, @t); route.Reach(me); }")
            .WithParameters(p => { p["id", typeof(int)] = 1; p["x", typeof(double)] = 1.2; p["y", typeof(double)] = 8.6; p["t", typeof(double)] = 2.52; }).PerformCommand();
        perf.Actor.Using("{ route = g.Find(@id); touch = Pose(@x, @y, @h); me = Pose(@px, @py, @h); route.Bump(touch, me); }")
            .WithParameters(p => { p["id", typeof(int)] = 1; p["x", typeof(double)] = 3.0; p["y", typeof(double)] = 9.5; p["h", typeof(double)] = 3.14; p["px", typeof(double)] = 3.25; p["py", typeof(double)] = 9.5; }).PerformCommand();
        perf.Actor.Using("{ route = g.Find(@id); route.Fail(@why); }")
            .WithParameters(p => { p["id", typeof(int)] = 1; p["why", typeof(string)] = "the lab says so"; }).PerformCommand();
        // a second errand, late in the journal, written as the host writes it: does its own Stop fire the pattern?
        perf.Actor.Using("{ from = Position(@fx, @fy); point = Position(@x, @y); route = g.Visit(from, point); }")
            .WithParameters(p => { p["fx", typeof(double)] = 2.0; p["fy", typeof(double)] = 9.5; p["x", typeof(double)] = 9.0; p["y", typeof(double)] = 9.5; }).PerformCommand();
        Thread.Sleep(3000);   // .Cue() reactions run after the entry lands

        findings.Add("--- pushes ---");
        foreach (var (reaction, document, bindings) in sink.Pushed)
            findings.Add($"{reaction}: {document.Replace("\n", " ")} bindings[{bindings}]");
        foreach (var (name, _) in patterns)
            findings.Add($"{name}: {(sink.Pushed.Any(p => p.Reaction == name) ? "FIRED" : "silent")}");
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "lab-patterns.txt"), findings);
        perf.Dispose();
        Assert.IsTrue(findings.Count > 0);
    }
}
