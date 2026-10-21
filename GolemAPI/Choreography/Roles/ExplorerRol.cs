using GolemAPI.Membrane;
using GolemDomain.Geometry;
using Puppeteer;

namespace GolemAPI.Choreography.Roles;

/// <summary>THE EXPLORER'S ROLE (propuesta 120, fase B, 20-oct-2026; Juan: "uno inicia el modo exploración y él solo se queda ahí un tiempo
/// indefinido hasta lograr explorar todo lo que encuentre"): the scripts of the exploration module — the mode on and off, and THE PROBE the
/// host asks for every time the golem stands free: at the arrival that ends a probe (the displacer asks here, as it asks the player after a
/// run) and from the clock (a probe refused, a body stuck, the first one after `explore`). A probe is a route the golem lays itself, no
/// planner, that a touch ends: the bumper writes the touch as always, the route backs the body to where the step began, and that arrival
/// asks the next probe. Nothing unknown left next to what the golem knows, the clock writes the finish and the mode is off. The capability
/// `explorer` needs the motors and the bumper.</summary>
public sealed class ExplorerRol
{
    private readonly GolemEmbodiment robot;

    internal ExplorerRol(GolemEmbodiment robot) => this.robot = robot ?? throw new ArgumentNullException(nameof(robot));

    /// <summary>THE MODE ON — <c>explore</c>: <c>g.Exploration.Start()</c> on the map the golem is in; the first probe opens with the next tick, or
    /// at once from an arrival. BLIND — <c>explore blind</c> (Juan, 20-oct-2026: "quisiera poder lanzarlo sin mapa"): the golem blindfolds itself
    /// first — a scenario of its own with no map at all, `blind-&lt;scenario&gt;`, entered where its body stands — and explores that.</summary>
    public Answer Explore()
    {
        try
        {
            return Answer.Of(robot.Actor.Using(
                @"
                    {
                        if (g.Exploration.Sighted) {
                            blind = g.Exploration.Blindfold();
                            g.Enter(blind);
                        }
                        g.Exploration.Start();
                        print g.Current.Name 'scenario', g.Exploration.Exploring 'exploring', g.Exploration.Policy 'policy', g.Exploration.Covered 'covered',
                              g.Exploration.FrontierCount 'frontier', g.Exploration.IsComplete 'complete';
                    }
                ")
                .PerformCommand());
        }
        catch (Exception ex) { return Answer.Refusal("explore: " + GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>THE MODE OFF — <c>explore stop</c>: <c>g.Exploration.Stop()</c>; a probe underway ends on its own.</summary>
    public Answer StopExploring()
    {
        try
        {
            return Answer.Of(robot.Actor.Using(
                @"
                    {
                        g.Exploration.Stop();
                        print g.Exploration.Exploring 'exploring', g.Exploration.Probes 'probes', g.Exploration.Covered 'covered',
                              g.Exploration.FrontierCount 'frontier';
                    }
                ")
                .PerformCommand());
        }
        catch (Exception ex) { return Answer.Refusal("explore stop: " + GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>THE MAP EXPORTED — <c>export map &lt;name&gt;</c>: the map deduced here becomes a scenario of the golem's, under that name, to be entered like
    /// any other; a name already known is refused.</summary>
    public Answer ExportMap(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Answer.Refusal("a map needs a name");
        try
        {
            return Answer.Of(robot.Actor.Using(
                @"
                    {
                        explored = g.Exploration.Deduced(@mapName);
                        exploredScenario = g.Export(explored);
                        print exploredScenario.Name 'map', explored.ZoneCount 'zones', exploredScenario.Frame 'frame', g.Scenarios().Count 'known';
                    }
                ")
                .WithParameters(p => { p["mapName", typeof(string)] = name.Trim().ToLowerInvariant(); })
                .PerformCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"export map {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>WHAT WAS EXPLORED HERE, CLEARED — <c>explore clear</c>: the trail starts afresh and the marks are forgotten; refused while exploring.</summary>
    public Answer ClearExploration()
    {
        try
        {
            return Answer.Of(robot.Actor.Using(
                @"
                    {
                        g.Exploration.Clear();
                        print g.Exploration.Covered 'covered', g.Exploration.WallCount 'walls', g.Exploration.FrontierCount 'frontier';
                    }
                ")
                .PerformCommand());
        }
        catch (Exception ex) { return Answer.Refusal("explore clear: " + GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>THE PROBE, from where the body stands (its own report, like the run after an arrival — ajuste 119): refused, nothing written, when
    /// the golem is not exploring, holds a route, or has nothing unknown left to reach.</summary>
    internal Answer Probed(Pose here)
    {
        if (here == null) return Answer.Refusal("no telemetry from the body: the probe needs where it stands");
        try
        {
            return Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Exploration.Probing) Error 'the golem is not exploring with somewhere unknown to reach, or it holds a route';
                ", @"
                    {
                        me = Pose(@px, @py, @ptheta);
                        route = g.Exploration.Probe(me);
                        print g.Exploration.Probes 'probes', g.Exploration.Covered 'covered', g.Exploration.FrontierCount 'frontier';
                        if (route.IsPending()) {
                            print route.Id 'route', route.Order 'action', route.Waiting 'waiting';
                            if (route.IsWalkable) {
                                print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                      route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                      route.Following 'following', route.StopsLeft 'stopsLeft';
                            }
                        } else {
                            print route.Id 'route', route.Status 'ended';
                        }
                    }
                ")
                .WithParameters(p => {
                    p["px", typeof(double)] = Resolution.Metres(here.X);
                    p["py", typeof(double)] = Resolution.Metres(here.Y);
                    p["ptheta", typeof(double)] = Resolution.Radians(here.Theta);
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal("probe: " + GolemEmbodiment.Reason(ex)); }
    }

    // THE CLOCK'S QUESTIONS: a probe when the golem stands free while exploring (the first after `explore`, or after a probe refused or a body
    // stuck — an arrival asks the next one by itself), and the finish when nothing unknown is left
    internal void ProbeIfIdle()
    {
        if (robot.Pose is not { } here) return;
        try
        {
            var probed = Probed(here);
            if (probed.Ok) robot.Report(probed, "exploring: the next probe opens");
        }
        catch { /* the clock asks again */ }
    }

    internal void FinishIfComplete()
    {
        try
        {
            var finished = Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Exploration.Concluded) Error 'the exploration is not complete, or the golem is not exploring';
                ", @"
                    {
                        g.Exploration.Finish();
                        print g.Exploration.Exploring 'exploring', g.Exploration.Finished 'finished', g.Exploration.Probes 'probes',
                              g.Exploration.Covered 'covered', g.Exploration.WallCount 'walls';
                    }
                ")
                .PerformCheckThenCommand());
            if (finished.Ok) robot.Report(finished, "the exploration is complete: nothing unknown is left next to what the golem knows");
        }
        catch { /* the clock asks again */ }
    }
}
