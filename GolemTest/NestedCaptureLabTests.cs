using System.Globalization;
using Choreography.Theater;
using GolemDomain;
using GolemDomain.Layouts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;
using Puppeteer.EventSourcing.Interpreter.Formatters;

namespace GolemTest;

// A LAB, not an acceptance test (18-sep-2026, Juan: "tengo entendido que podemos omitir los expose de los scripts…
// nos comentan que no son necesarios"). The engine's source (Ncubo.Puppeteer 2.0.1-beta.10017, built 21-aug-2026) carries
// NestedCallParameterNode (12-jun-2026: "casar argumento-llamada anidado y capturar sus $vars internos") and
// ScriptConstructorCall.ArgumentValues (11-jul-2026: "constructor positions correlate $x like method positions"). So the
// question is empirical: can a reaction capture the values our tells need STRAIGHT FROM THE ACT — the pose inside
// `Pose(@bodyX, @bodyY, @bodyHeading)`, the point inside `Position(@x, @y)` — without an `expose`? Each pattern gets its
// own actor and its own journal (one reaction per actor: many reactions on one actor fired unreliably, lab-patterns.txt),
// the same acts are written to each, and what every reaction pushed — with its bindings — is written to lab-nested.txt.
[TestClass]
public sealed class NestedCaptureLabTests
{
    private sealed class Sink : IOutputSink
    {
        public readonly List<(string Reaction, string Document, string Bindings)> Pushed = new();
        public void Push(in PushDocument d)
        {
            lock (Pushed) Pushed.Add((d.ReactionName, d.Document, string.Join(",", d.Bindings.Select(b => $"{b.Key}={b.Value}"))));
        }
    }

    [TestInitialize]
    public void PinTheCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    private const string Releases =
        "upgrade('body_v1') { radius = Meters(0.25); speed = MetersPerSecond(2.0); linger = Seconds(6.0); retreat = Meters(0.6); body = Body(radius, speed, linger, retreat); }\n"
        + "upgrade('init') { collisions = Collisions(map); g = Golem(body, map, collisions); }\n";

    [TestMethod]
    public void CanAReaction_CaptureTheValuesInsideANestedConstructor_WithoutAnExpose()
    {
        var cases = new (string Name, string Pattern, string Emit)[]
        {
            // the bump written INLINE — the pose built in the argument itself
            ("N1-bump-inline-ctor",   "[_:Golem].Bump(Pose($bx, $by, $bh), $bearing)", "print @bx 'bx', @by 'by', @bh 'bh', @bearing 'bearing';"),
            ("N2-bump-bearing-only",  "[_:Golem].Bump(_, $bearing)",                    "print @bearing 'bearing';"),
            // the bump as the host writes it TODAY — the pose in a local variable `me` on the line before
            ("N3-bump-var-capture",   "[_:Golem].Bump($me, $bearing)",                  "print @bearing 'bearing';"),
            ("N4-bump-var-ctor",      "[_:Golem].Bump(Pose($bx, $by, $bh), $bearing)", "print @bx 'bx', @bearing 'bearing';"),
            // a bare constructor pattern beside the act: does the pattern list see the `me = Pose(…)` statement?
            ("N5-ctor-then-bump",     "Pose($bx, $by, $bh) [_:Golem].Bump(_, $bearing)", "print @bx 'bx', @by 'by', @bh 'bh', @bearing 'bearing';"),
            ("N6-ctor-alone",         "Pose($bx, $by, $bh)",                             "print @bx 'bx', @by 'by', @bh 'bh';"),
            // the forget, inline and two-line
            ("N7-forget-inline",      "[_:Golem].Forget(Position($x, $y))",              "print @x 'x', @y 'y';"),
            ("N8-forget-var",         "[_:Golem].Forget(Position($x, $y))",              "print @x 'x', @y 'y';"),
            // the arrival, inline: the pose the body reports
            ("N9-arrive-inline",      "[_:Route].Arrive(Pose($x, $y, $h))",              "print @x 'x', @y 'y', @h 'h';"),
            // MIXED: the constructor beside the act PLUS a reduced expose — what the speech would need (ajuste 42)
            ("N10-bump-mixed",        "Pose($bx, $by, $bh) [_:Golem].Bump(_, $bearing) expose $who who;", "print @bx 'bx', @by 'by', @bh 'bh', @bearing 'bearing', @who 'who';"),
            ("N11-arrive-mixed",      "Pose($x, $y, _) [_:Route].Arrive(_) expose $rid rid, true reached;", "print @x 'x', @y 'y', @rid 'rid';"),
            ("N12-arrive-mixed-noStop", "Pose($x, $y, _) [_:Route].Arrive(_) expose $rid rid, true reached;", "print @x 'x', @y 'y', @rid 'rid';"),
            ("N13-forget-ctor-beside", "Position($x, $y) [_:Golem].Forget(_)",           "print @x 'x', @y 'y';"),
        };
        var findings = new List<string>();
        foreach (var (name, pattern, emit) in cases)
        {
            var sink = new Sink();
            var perf = new PerformanceV2("nested-lab-" + Guid.NewGuid().ToString("N"), DomainLibrary.Assembly);
            perf.ConfigureStorage(DatabaseType.IN_MEMORY, "nested-lab-" + name);
            perf.OutputTarget(sink, new JsonFormatter());
            try
            {
                perf.Actor.Reactions.DefineReaction(name).Cue().Company().WithSharedHydration()
                    .Seek("Act").One().OnMatch(pattern)
                    .Program.Emit(emit);
            }
            catch (Exception e) { findings.Add($"{name} ({pattern}): DEFINITION REFUSED -> {e.GetType().Name}: {e.Message}"); perf.Dispose(); continue; }
            perf.Start();
            perf.Actor.Using(Releases.Replace("upgrade('init')", Catalog.Warehouse().AsRelease() + "upgrade('init')")).PerformCommand();
            perf.Actor.Using("{ from = Pose(@fx, @fy, @ft); point = Position(@x, @y); route = g.Visit(from, point); }")
                .WithParameters(p => { p["fx", typeof(double)] = 2.0; p["fy", typeof(double)] = 9.5; p["ft", typeof(double)] = 0.0; p["x", typeof(double)] = 9.0; p["y", typeof(double)] = 9.5; }).PerformCommand();
            string act = name switch
            {
                "N1-bump-inline-ctor" or "N2-bump-bearing-only" => "{ route = g.Bump(Pose(@bodyX, @bodyY, @bodyHeading), @bearing); }",
                "N3-bump-var-capture" or "N4-bump-var-ctor" or "N5-ctor-then-bump" or "N6-ctor-alone" => "{ me = Pose(@bodyX, @bodyY, @bodyHeading); route = g.Bump(me, @bearing); }",
                "N7-forget-inline" => "{ g.Forget(Position(@bodyX, @bodyY)); }",
                "N8-forget-var" or "N13-forget-ctor-beside" => "{ at = Position(@bodyX, @bodyY); g.Forget(at); }",
                "N10-bump-mixed" => "{ me = Pose(@bodyX, @bodyY, @bodyHeading); route = g.Bump(me, @bearing); } expose @name who;",
                "N11-arrive-mixed" => "{ route = g.Underway(); me = Pose(@bodyX, @bodyY, @bodyHeading); route.Arrive(me); } expose @rid rid, @stop reached;",
                "N12-arrive-mixed-noStop" => "{ route = g.Underway(); me = Pose(@bodyX, @bodyY, @bodyHeading); route.Arrive(me); } expose @rid rid, @nostop reached;",
                _ => "{ route = g.Underway(); route.Arrive(Pose(@bodyX, @bodyY, @bodyHeading)); }",
            };
            if (name.StartsWith("N7") || name.StartsWith("N8") || name.StartsWith("N13"))   // something to forget: a bump first
                perf.Actor.Using("{ route = g.Bump(Pose(@bodyX, @bodyY, @bodyHeading), @bearing); }")
                    .WithParameters(p => { p["bodyX", typeof(double)] = 3.0; p["bodyY", typeof(double)] = 9.5; p["bodyHeading", typeof(double)] = 0.0; p["bearing", typeof(double)] = 0.0; }).PerformCommand();
            string returned, outcome;
            try
            {
                returned = perf.Actor.Using(act)
                    .WithParameters(p =>
                    {
                        p["bodyX", typeof(double)] = name.StartsWith("N7") || name.StartsWith("N8") || name.StartsWith("N13") ? 3.25 : name.StartsWith("N11") || name.StartsWith("N12") ? 3.4 : 3.0;
                        p["bodyY", typeof(double)] = 9.5;
                        p["bodyHeading", typeof(double)] = 0.0;
                        p["bearing", typeof(double)] = 0.1;
                        p["name", typeof(string)] = "lab";
                        p["rid", typeof(int)] = 1;
                        p["stop", typeof(bool)] = true;
                        p["nostop", typeof(bool)] = false;
                    })
                    .PerformCommand();
                outcome = "written";
            }
            catch (Exception e) { returned = ""; outcome = $"ACT FAILED -> {e.GetType().Name}: {Root(e).Message}"; }
            Thread.Sleep(1500);   // .Cue() reactions run after the entry lands
            List<(string Reaction, string Document, string Bindings)> pushed;
            lock (sink.Pushed) pushed = sink.Pushed.ToList();
            findings.Add($"{name} ({pattern}) on {act.Replace("\n", " ")} :: {outcome}; {pushed.Count} push(es) "
                + string.Join(" | ", pushed.Select(p => $"{p.Document.Replace("\n", " ")} bindings[{p.Bindings}]")));
            perf.Dispose();
        }
        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "lab-nested.txt"), findings);
        foreach (var f in findings) Console.WriteLine(f);
        Assert.IsTrue(findings.Count == cases.Length);
    }

    private static Exception Root(Exception e) { while (e.InnerException != null) e = e.InnerException; return e; }
}
