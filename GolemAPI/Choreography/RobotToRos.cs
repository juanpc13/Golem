using System.Text.Json;
using Choreography.Theater;
using GolemAPI.Membrane;
using Puppeteer;

namespace GolemAPI.Choreography;

// THE OUTPUT TARGET: the golem's print, on its way to the robot over ROS (Juan, 16-sep-2026: "una nueva clase que sirva de
// salida, RobotToRos, que herede de IOutputSink, que se ocupe del obey y haga el switch case para enviar al robot").
// Every command the golem performs ends with the same print (GolemController.NextOrder); the print comes back to
// whoever performed it and is handed here — Obey — or pushed here by the engine when a Reaction emits it (Push). It is
// parsed and SWITCHED on, and the robot gets one of its BASE ACTIONS over the websocket (rosbridge, std_msgs/String on
// /golem/<body>/order): ADVANCE to a point, BACK to a point in reverse, TURN LEFT or TURN RIGHT to a heading, STOP,
// CONTINUE what it was doing before a stop — the print's own JSON rides along, plus what the body needs (the arrival
// tolerance, the body the journal declared). The robot answers with its reports (arrived, bump, stuck): those reach
// the Robot through the golem's endpoints. The domain says WHAT (the heading, the point); this class says it in the
// robot's words (which way round to turn is read off the body's pose: the servo's business, not the journal's).
public sealed class RobotToRos : IOutputSink
{
    private const double ArriveWithin = 0.25;    // a stop is "reached" within the body's radius
    private const double LineUpWithin = 0.15;    // a door is lined up tighter (the crossing must be straight); a retreat ends as tight
    private const double LeaderStandoff = 1.0;   // the follower's last stop is met this short of the leader's spot

    private readonly Robot robot;
    private readonly Rosbridge ros;
    private readonly object gate = new();
    private Order carrying;         // the order the body is carrying out now; null while it stands
    private Order held;             // the order the body was carrying when the operator held it: 'continue' resumes it
    private int heardBefore;        // peers' bumps heard before the current order began are older news than a touch during it
    private DateTime lingerUntil = DateTime.MinValue;   // the follower's linger: no order goes out to the body before this

    public RobotToRos(Robot robot, Rosbridge ros)
    {
        this.robot = robot;
        this.ros = ros;
    }

    /// <summary>The order the body is carrying out now — what a report from it must be about — or null while it stands.</summary>
    public Order Carrying { get { lock (gate) return carrying; } }
    /// <summary>How many peers' bumps had been heard when the current order began.</summary>
    public int HeardBefore { get { lock (gate) return heardBefore; } }

    // ==================================================================
    // The output target: a print arrives, is parsed, switched on, and travels to the robot as one of its actions.
    // ==================================================================

    // ==================================================================
    // How the prints reach this target WITHOUT anybody dispatching them (Juan, 17-sep-2026: "esos print terminarán llamando
    // a Push automáticamente al terminar el script"): a command's print is PULL — it returns to the caller — and only a
    // Reaction's emit is PUSHED. So one Reaction per act shape watches the journal and emits NextOrder when the act lands:
    // Find($id) — every act on a route in hand (Then, Turn, Reach, Bump, Graze, Decide, DecidePast, Pause, Resume, Fail,
    // Abandon) writes `route = g.Find(@id)` first; Visit(_, _) and Cover(_, _) — the errand; Follow(_) — a told point; Pause(_) and Resume() — the operator's hold on the golem itself.
    // Defined BEFORE performance.Start(); the lab (lab-push-real.txt) saw each act push exactly once with the speech
    // reactions around. Many reactions on the SAME act shape fired unreliably (lab-patterns.txt): one per shape, no more.
    // ==================================================================
    public void DefineReactions(PerformanceV2 performance)
    {
        foreach (var (name, pattern) in new[]
        {
            ("next-order-find",   "[_:Golem].Find($id)"),
            ("next-order-visit",  "[_:Golem].Visit(_, _)"),
            ("next-order-cover",  "[_:Golem].Cover(_, _)"),
            ("next-order-follow", "[_:Golem].Follow(_)"),
            ("next-order-pause",  "[_:Golem].Pause(_)"),
            ("next-order-resume", "[_:Golem].Resume(_)"),   // a zero-argument pattern on the golem did not fire (17-sep lab): the pose rides along, and it is true
        })
            performance.Actor.Reactions.DefineReaction(name)
                .Cue().Company().WithSharedHydration()
                .Seek("Act").One()
                    .OnMatch(pattern)
                .Program.Emit(Robot.NextOrder);
    }

    /// <summary>IOutputSink — the print a next-order reaction emitted, pushed by the engine when the act landed. (The speech
    /// reactions push nothing: they tell; anything else that reaches the sink is not an order.)</summary>
    public void Push(in PushDocument document)
    {
        if (!document.ReactionName.StartsWith("next-order", StringComparison.Ordinal)) return;
        Console.WriteLine($"[output] {document.ReactionName} pushed: {document.Document.ReplaceLineEndings(" ")}");
        Obey(document.Document);
    }

    /// <summary>The print a command returned: what the route asks now. Parsed, switched on, sent to the robot.</summary>
    public void Obey(string print) => Obey(Order.Parse(print));

    public void Obey(Order order)
    {
        if (order == null) { Stop(); return; }   // nothing pending: the body stands
        switch (order.What)
        {
            case "hold":
                if (Carrying == null) break;   // already standing: the clock asked again while held
                Hold();
                robot.Note($"route {order.Route}: paused by the operator — the body stands until resumed");
                break;
            case "decide":
                robot.Decide(order.Route, "another road");   // the domain's, not the robot's: the new print comes back here
                break;
            case "turn":
                Turn(order);
                break;
            case "run":
                Advance(order);
                break;
            case "back":
                Back(order);
                break;
            default:
                robot.Note($"an order I do not know: '{order.What}'");
                break;
        }
    }

    // ---- the robot's base actions ----

    // Turn in place to the heading the domain gave — to the left or to the right, whichever is shorter from where the
    // body faces now (its pose: telemetry, the servo's to read).
    private void Turn(Order order)
    {
        if (Resumes(order)) return;
        var pose = robot.Pose;
        double deviation = pose == null ? 0 : Normalize(order.Heading - pose.Theta);
        string action = deviation >= 0 ? "turnLeft" : "turnRight";
        Send(action, order, LineUpWithin, $"{(deviation >= 0 ? "turning left" : "turning right")} to heading {order.Heading:0.00} for ({order.X:0.0}, {order.Y:0.0})");
    }

    // Advance to the leg's point: line up at the approach and run through when it is a door; the follower's last stop
    // is met a body's length short (the leader may still be there).
    private void Advance(Order order)
    {
        if (Resumes(order)) return;
        bool standoff = order.Following && order.IsLastStop;
        string what = order.Kind switch { "stop" => "advancing to a stop", "via" => "advancing to a point", "around" => "skirting to", "aside" => "stepping aside to", _ => $"advancing to the passage {order.Name}" };
        if (standoff) what += $", stopping {LeaderStandoff:0.0} short of the leader's spot";
        Send("advance", order, standoff ? LeaderStandoff : ArriveWithin, $"{what} ({order.X:0.0}, {order.Y:0.0})");
    }

    // Back off, in reverse, to the correction point the route inserted after a touch.
    private void Back(Order order)
    {
        if (Resumes(order)) return;
        Send("back", order, LineUpWithin, $"backing off to ({order.X:0.0}, {order.Y:0.0})");
    }

    // The operator held the route: the body stops where it stands and remembers what it was doing.
    private void Hold()
    {
        lock (gate)
        {
            if (carrying == null) return;   // already standing (the clock asks again while held): nothing to stop, nothing to forget
            held = carrying;
            carrying = null;
        }
        _ = ros.PublishAsync(ros.OrderTopic, "{\"action\":\"stop\"}");
    }

    // The same order the body was holding comes back (Resume): it continues where it stopped.
    private bool Resumes(Order order)
    {
        lock (gate)
        {
            if (held == null || !held.SameAs(order)) { held = null; return false; }
            carrying = order;
            held = null;
        }
        robot.Note($"route {order.Route}: resumed — the body continues what it was doing");
        _ = ros.PublishAsync(ros.OrderTopic, "{\"action\":\"continue\"}");
        return true;
    }

    /// <summary>The body stands: whatever it was doing is dropped. With <paramref name="anchor"/> it also takes the world's
    /// word for where it is (after a teleport: dead reckoning re-anchors).</summary>
    public void Stop(bool anchor = false)
    {
        lock (gate) { carrying = null; held = null; }
        _ = ros.PublishAsync(ros.OrderTopic, anchor ? "{\"action\":\"stop\",\"anchor\":true}" : "{\"action\":\"stop\"}");
    }

    /// <summary>A courtesy step — out of a peer's way, or off the leader's — is an advance with no route: the body reports
    /// it arrived and nothing is journaled (the golem chose the point: g.Aside).</summary>
    public void StepAside(double x, double y, string why)
    {
        var step = new Order(0, "run", "aside", "aside", x, y, x, y, x, y, false, 0, false, 0);
        robot.Note($"{why}: stepping to ({x:0.0}, {y:0.0})");
        lock (gate) { carrying = step; held = null; }
        _ = PublishAsync("advance", step, LineUpWithin);
    }

    /// <summary>The body finished what it was carrying (it arrived, it bumped, it got stuck): what that was.</summary>
    public Order Done()
    {
        lock (gate) { var was = carrying; carrying = null; return was; }
    }

    /// <summary>The follower's linger: no order goes out to the body for this long.</summary>
    public void Linger(TimeSpan span)
    {
        lock (gate) lingerUntil = DateTime.UtcNow + span;
    }

    // The order goes to the robot as the SAME JSON the golem printed, plus the robot's action and what it needs to carry
    // it out. The same order twice (the endpoint's answer after the clock already asked the journal) is not sent again; a
    // different one replaces whatever the body was doing. The follower's linger holds it back a while.
    private void Send(string action, Order order, double within, string note)
    {
        TimeSpan wait;
        lock (gate)
        {
            if (carrying != null && carrying.SameAs(order)) return;
            carrying = order;
            held = null;
            heardBefore = robot.HeardBumpCount();
            wait = lingerUntil - DateTime.UtcNow;
        }
        robot.Note($"route {order.Route}: {note}{(wait > TimeSpan.Zero ? $" — after lingering {wait.TotalSeconds:0} s" : "")}");
        if (wait > TimeSpan.Zero)
            _ = Task.Run(async () =>
            {
                await Task.Delay(wait);
                lock (gate) { if (!ReferenceEquals(carrying, order)) return; }
                await PublishAsync(action, order, within);
            });
        else _ = PublishAsync(action, order, within);
    }

    private Task PublishAsync(string action, Order order, double within)
    {
        var (speed, radius, retreat) = robot.BodyDeclared();
        return ros.PublishAsync(ros.OrderTopic, JsonSerializer.Serialize(new
        {
            action,
            order = order.What, route = order.Route, kind = order.Kind, name = order.Name,
            x = order.X, y = order.Y, ax = order.AX, ay = order.AY, ex = order.EX, ey = order.EY,
            hasHeading = order.HasHeading, heading = order.Heading, following = order.Following, stopsLeft = order.StopsLeft,
            within,
            body = new { speed, radius, retreat }
        }));
    }

    private static double Normalize(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }
}
