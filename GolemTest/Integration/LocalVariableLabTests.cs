using System.Globalization;
using Choreography.Theater;
using GolemDomain;
using GolemDomain.Layouts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;
using Puppeteer.EventSourcing.Interpreter.Formatters;

namespace GolemTest;

// A LAB (21-sep-2026, Juan: "arreglarlo con una variable local el underway para no estar accediendo todo el tiempo"): may a
// script keep the route underway in a LOCAL variable — `route = g.Underway();` inside the `if` that guards it — in every
// place NextOrder is spoken: a QUERY (the clock's AskOrder), a reaction's EMIT (read-only: the next-order push), and a COMMAND
// (Wake, LetGo)? And does that local leak as an actor global? Findings in lab-local.txt beside the test binary.
[TestClass]
public sealed class LocalVariableLabTests
{
    private sealed class Sink : IOutputSink
    {
        public readonly List<(string Reaction, string Document)> Pushed = new();
        public void Push(in PushDocument d) { lock (Pushed) Pushed.Add((d.ReactionName, d.Document)); }
    }

    [TestInitialize]
    public void PinTheCulture() { CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture; CultureInfo.CurrentCulture = CultureInfo.InvariantCulture; }


    [TestMethod]
    public void MayTheRouteUnderway_LiveInALocalVariable_InAQuery_AnEmit_AndACommand()
    {
        var findings = new List<string>();
        var sink = new Sink();
        var perf = new PerformanceV2("local-lab-" + Guid.NewGuid().ToString("N"), DomainLibrary.Assembly);
        perf.ConfigureStorage(DatabaseType.IN_MEMORY, "local-lab");
        perf.OutputTarget(sink, new JsonFormatter());
        try
        {
            perf.Actor.Reactions.DefineReaction("next-order-visit").Cue().Company().WithSharedHydration()
                .Seek("Act").One().OnMatch("[_:Golem].Visit(_, _)").Program.Emit(@"
                    {
                        print g.HasPendingMission() 'pending', g.Held 'held';
                        if (g.HasPendingMission()) {
                            route = g.Underway();
                            print route.Id 'route', route.Order 'action', route.Amount 'amount';
                            if (route.IsWalkable) {
                                print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                      route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                      route.Following 'following', route.StopsLeft 'stopsLeft';
                            }
                        }
                    }
                ");
            findings.Add("C emit defined with the braced local");
        }
        catch (Exception e) { findings.Add($"C emit DEFINITION REFUSED -> {e.GetType().Name}: {e.Message}"); }
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

        // F — labels named like the @params: what the RETURNED print says (the journal's canonical render showed '6.3' for 'x')
        try
        {
            string f = perf.Actor.Using(@"
                {
                    me = Pose(@x, @y, @t);
                    g.Wake(me);
                    print g.Standing.X 'x', g.Standing.Y 'y';
                }
            ")
                .WithParameters(p => {
                    p["x", typeof(double)] = 6.3;
                    p["y", typeof(double)] = 10.4;
                    p["t", typeof(double)] = 0.0;
                }).PerformCommand();
            findings.Add("F wake with params x,y and labels 'x','y' -> returned print: " + f.Replace("\n", " "));
        }
        catch (Exception e) { findings.Add($"F FAILED -> {e.GetType().Name}: {e.Message}"); }

        // the errand: the emit (C) fires here
        perf.Actor.Using(@"
            {
                from = Pose(@fx, @fy, @ft);
                point = Position(@px, @py);
                route = g.Visit(from, point);
            }
        ")
            .WithParameters(p => {
                p["fx", typeof(double)] = 6.3;
                p["fy", typeof(double)] = 10.4;
                p["ft", typeof(double)] = 0.0;
                p["px", typeof(double)] = 2.0;
                p["py", typeof(double)] = 9.5;
            }).PerformCommand();
        Thread.Sleep(1500);
        lock (sink.Pushed) findings.Add($"C emit pushed {sink.Pushed.Count}: " + string.Join(" | ", sink.Pushed.Select(p => p.Document.Replace("\n", " "))));

        // A — a QUERY with the braced local
        try { findings.Add("A query braced local -> " + perf.Actor.Using(@"
            {
                print g.HasPendingMission() 'pending', g.Held 'held';
                if (g.HasPendingMission()) {
                    route = g.Underway();
                    print route.Id 'route', route.Order 'action', route.Amount 'amount';
                    if (route.IsWalkable) {
                        print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                              route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                              route.Following 'following', route.StopsLeft 'stopsLeft';
                    }
                }
            }
        ").PerformQuery().Replace("\n", " ")); }
        catch (Exception e) { findings.Add($"A query braced local FAILED -> {e.GetType().Name}: {Root(e).Message}"); }

        // B — a QUERY with the local inside the if, NO outer braces
        try { findings.Add("B query unbraced local -> " + perf.Actor.Using(@"
            if (g.HasPendingMission()) {
                route = g.Underway();
                print route.Id 'route', route.Order 'action';
            }
        ").PerformQuery().Replace("\n", " ")); }
        catch (Exception e) { findings.Add($"B query unbraced local FAILED -> {e.GetType().Name}: {Root(e).Message}"); }

        // D — a COMMAND (the waking) with the local inside the nested if
        try
        {
            string d = perf.Actor.Using(@"
                {
                    me = Pose(@px, @py, @ptheta);
                    g.Wake(me);
                }
                {
                    print g.HasPendingMission() 'pending', g.Held 'held';
                    if (g.HasPendingMission()) {
                        route = g.Underway();
                        print route.Id 'route', route.Order 'action', route.Amount 'amount';
                        if (route.IsWalkable) {
                            print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                  route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                  route.Following 'following', route.StopsLeft 'stopsLeft';
                        }
                    }
                }
            ")
                .WithParameters(p => {
                    p["px", typeof(double)] = 5.5;
                    p["py", typeof(double)] = 10.0;
                    p["ptheta", typeof(double)] = 3.1;
                }).PerformCommand();
            findings.Add("D command wake + braced local -> " + d.Replace("\n", " "));
        }
        catch (Exception e) { findings.Add($"D command FAILED -> {e.GetType().Name}: {Root(e).Message}"); }

        // E — did `route` leak as a global?
        try { findings.Add("E global 'route' after D -> LEAKED: " + perf.Actor.Using(@"
            print route.Id 'v';
        ").PerformQuery()); }
        catch (Exception e) { findings.Add($"E global 'route' after D -> not a global ({Root(e).Message})"); }

        File.WriteAllLines(Path.Combine(AppContext.BaseDirectory, "lab-local.txt"), findings);
        foreach (var f in findings) Console.WriteLine(f);
        perf.Dispose();
        Assert.IsTrue(findings.Count >= 6);
    }

    private static Exception Root(Exception e) { while (e.InnerException != null) e = e.InnerException; return e; }
}
