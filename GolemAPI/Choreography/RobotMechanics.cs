using System.Text.Json;
using Choreography.Theater;
using GolemAPI.Membrane;
using Puppeteer;

namespace GolemAPI.Choreography;

// THE ROBOT'S MECHANICS — the actor's OUTPUT TARGET: how the golem's order becomes the robot's movement (Juan,
// 17-sep-2026: "algo como robot + mecánicas"; the class was `RobotToRos` on 16-sep: "una nueva clase que sirva de
// salida, que herede de IOutputSink, que se ocupe del obey y haga el switch case para enviar al robot"). Only the way
// OUT lives here — golem to body; what comes back (the pose, the contacts, the reports) enters through the membrane and
// the golem's endpoints. Every command the golem performs ends with the same print, written in full in each script; the
// print comes back to whoever performed it and is handed here — Dispatch — or pushed here by the engine when a Reaction
// emits it (Push). SINCE 17-sep THE PRINT ALREADY SPEAKS THE ROBOT'S WORDS (Juan: "al robot se le dice muy sencillamente
// lo que debe moverse hacia adelante, qué tanto debe rotar"): an ACTION — advance, back, turnLeft, turnRight, stop — and
// an AMOUNT (metres, or radians), both the domain's to say; so the dispatch does no arithmetic — it switches on the
// action and sends the body that one base action with its amount over the websocket (rosbridge, std_msgs/String on
// /golem/<body>/order), the print's own JSON riding along with the body the journal declared. `continue` is the sixth
// action: the same order the body was holding when told to stop comes back. No 'decide' reaches the dispatch since
// 18-sep-2026: a route is born with its way and decides it again by itself; what the golem writes at boot is g.Wake(me), the
// pose the membrane brought. The robot answers with its reports (arrived, bump, stuck): those reach the GolemEmbodiment
// through the golem's endpoints. Every
// move has a route, so nothing here does arithmetic on the body's pose.
// NOT the membrane: Rosbridge is the wire (the websocket, the topics, the telemetry it parses) and knows nothing of
// orders; the mechanics know the order — what the body carries, what it held, the linger — and nothing of topics beyond
// the one they publish on. A real robot would change the wire and keep the mechanics.
public sealed class RobotMechanics : IOutputSink
{
    private readonly GolemEmbodiment golemEmbodiment;
    private readonly IBodyWire ros;
    private readonly object gate = new();
    private Order carrying;         // the order the body is carrying out now; null while it stands
    private Order held;             // the order the body was carrying when the operator held it: 'continue' resumes it
    private DateTime lingerUntil = DateTime.MinValue;   // the follower's linger: no order goes out to the body before this
    private int tickets;            // the last ticket stamped on an order sent to the body (ajuste 55)

    public RobotMechanics(GolemEmbodiment golemEmbodiment, IBodyWire ros)
    {
        this.golemEmbodiment = golemEmbodiment;
        this.ros = ros;
    }

    /// <summary>The order the body is carrying out now — what a report from it must be about — or null while it stands.</summary>
    public Order Carrying { get { lock (gate) return carrying; } }

    // ==================================================================
    // How the prints reach this target WITHOUT anybody dispatching them (Juan, 17-sep-2026: "esos print terminarán llamando
    // a Push automáticamente al terminar el script"): a command's print is PULL — it returns to the caller — and only a
    // Reaction's emit is PUSHED. So one Reaction per act shape watches the journal and emits that print, in full, when the act lands:
    // Underway() — the arrival and the ending on the route underway write `route = g.Underway()` first (Arrive, Fail);
    // Bump(_, _) — the bump, `route = g.Bump(me, bearing)`; Wake(_) — the golem wakes where its body stands (a plan underway
    // decided again inside); Find($id) — a route in hand by its handle (Then); Visit(_, _) and Cover(_, _) — the
    // errand; Follow(_) — a told point; Pause(_) and Resume(_) — the operator's hold on the golem itself.
    // Defined BEFORE performance.Start(); the lab (lab-push-real.txt) saw each act push exactly once with the speech
    // reactions around. Many reactions on the SAME act shape fired unreliably (lab-patterns.txt): one per shape, no more.
    // ==================================================================
    public void DefineReactions(PerformanceV2 performance)
    {
        foreach (var (name, pattern) in new[]
        {
            ("next-order-underway", "[_:Golem].Underway()"),   // the arrival and the decisions act on the route underway (18-sep lab: the zero-argument form fires)
            ("next-order-bump",   "[_:Golem].Bump(_, _)"),      // the bump is the golem's: it finds its route underway and corrects it inside
            ("next-order-wake",   "[_:Golem].Wake(_)"),         // the golem wakes where its body stands: a plan underway is decided again inside
            ("next-order-find",   "[_:Golem].Find($id)"),        // a route in hand by its handle: Then
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
                .Program.Emit(@"
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
    }

    // ==================================================================
    // The output target: a print arrives, is parsed, and is DISPATCHED to ROS as the base action it names.
    // ==================================================================

    /// <summary>IOutputSink — the print a next-order reaction emitted, pushed by the engine when the act landed. (The speech
    /// reactions push nothing: they tell; anything else that reaches the sink is not an order.)</summary>
    public void Push(in PushDocument document)
    {
        if (!document.ReactionName.StartsWith("next-order", StringComparison.Ordinal)) return;
        Console.WriteLine($"[mechanics] {document.ReactionName} pushed: {document.Document.ReplaceLineEndings(" ")}");
        Dispatch(document.Document);
    }

    /// <summary>The print a command returned or the clock asked: what the route asks now, parsed and dispatched to ROS.</summary>
    public void Dispatch(string print) => Dispatch(Order.Parse(print));

    /// <summary>The dispatch to ROS: the action the domain named — advance | back | turnLeft | turnRight | stop — travels to
    /// the body with its amount; the same order the body was holding comes back as continue; nothing pending stops the body.</summary>
    public void Dispatch(Order order)
    {
        if (order == null) { Stop(); return; }              // nothing pending: the body stands
        if (Holds(order)) { Continue(order); return; }      // the same order it was holding comes back: the body goes on
        switch (order.Action)
        {
            case "advance":
                Advance(order);
                break;
            case "back":
                Back(order);
                break;
            case "turnLeft":
                TurnLeft(order);
                break;
            case "turnRight":
                TurnRight(order);
                break;
            case "stop":
                if (Carrying == null) break;                // already standing: the clock asked again while held
                Stop();
                golemEmbodiment.Note($"route {order.Route}: paused by the operator — the body stands until resumed");
                break;
            default:
                golemEmbodiment.Note($"an action I do not know: '{order.Action}'");
                break;
        }
    }

    // ==================================================================
    // THE ROBOT'S SIX BASE ACTIONS (Juan, 16-sep-2026: "avanzar / retroceder / girar a la derecha / girar a la izquierda /
    // detener / continuar"). Each one publishes its own name with the amount the domain said; the print rides along.
    // ==================================================================

    /// <summary>Advance: move forward the amount of metres the domain said, toward the point it named.</summary>
    private void Advance(Order order) =>
        Send(order, $"advancing {order.Amount:0.00} m to {Whither(order)} ({order.X:0.0}, {order.Y:0.0})");

    /// <summary>Back: move in reverse the amount of metres the domain said (the retreat a touch inserted).</summary>
    private void Back(Order order) =>
        Send(order, $"backing off {order.Amount:0.00} m to ({order.X:0.0}, {order.Y:0.0})");

    /// <summary>Turn left: rotate in place, counter-clockwise, the amount of radians the domain said.</summary>
    private void TurnLeft(Order order) =>
        Send(order, $"turning left {order.Amount:0.00} rad to face ({order.X:0.0}, {order.Y:0.0})");

    /// <summary>Turn right: rotate in place, clockwise, the amount of radians the domain said.</summary>
    private void TurnRight(Order order) =>
        Send(order, $"turning right {order.Amount:0.00} rad to face ({order.X:0.0}, {order.Y:0.0})");

    /// <summary>Stop: the body stands where it is and remembers what it was doing, so <c>continue</c> can take it up again
    /// (the operator's hold; nothing pending; the operator's levers).</summary>
    public void Stop()
    {
        lock (gate) { if (carrying != null) { held = carrying; carrying = null; } }
        _ = ros.PublishAsync(ros.OrderTopic, "{\"action\":\"stop\"}");
    }

    /// <summary>Continue: the order the body was holding is back — it goes on with what it stopped.</summary>
    private void Continue(Order order)
    {
        lock (gate) { carrying = order; held = null; }
        golemEmbodiment.Note($"route {order.Route}: resumed — the body continues what it was doing");
        _ = ros.PublishAsync(ros.OrderTopic, "{\"action\":\"continue\"}");
    }

    private static string Whither(Order order) => order.Kind switch
    {
        "stop" => "the stop", "via" => "a point", "around" => "a point around the obstacle", "aside" => "a point aside",
        _ => $"the passage {order.Name}"
    };

    private bool Holds(Order order)
    {
        lock (gate) return held != null && held.SameAs(order);
    }

    // ==================================================================
    // The levers on the body that are no order of the route's.
    // ==================================================================

    /// <summary>The body was carried onto its mark (a teleport): it stops, forgets whatever it was doing, and takes the
    /// world's word for where it stands (dead reckoning re-anchors).</summary>
    public void StopOnMark()
    {
        lock (gate) { carrying = null; held = null; }
        _ = ros.PublishAsync(ros.OrderTopic, "{\"action\":\"stop\",\"anchor\":true}");
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

    // The order goes to the robot as the SAME JSON the golem printed, plus the body the journal declared. The same order
    // twice (the endpoint's answer after the clock already asked the journal) is not sent again; a different one replaces
    // whatever the body was doing. The follower's linger holds it back a while.
    private void Send(Order order, string note)
    {
        TimeSpan wait;
        lock (gate)
        {
            if (carrying != null && carrying.SameAs(order)) return;
            order = order with { Ticket = ++tickets };   // what the body echoes back — nothing of the route (ajuste 55)
            carrying = order;
            held = null;
            wait = lingerUntil - DateTime.UtcNow;
        }
        golemEmbodiment.Told(order, $"route {order.Route}: {note}{(wait > TimeSpan.Zero ? $" — after lingering {wait.TotalSeconds:0} s" : "")}");
        if (wait > TimeSpan.Zero)
            _ = Task.Run(async () =>
            {
                await Task.Delay(wait);
                lock (gate) { if (!ReferenceEquals(carrying, order)) return; }
                await PublishAsync(order);
            });
        else _ = PublishAsync(order);
    }

    // The body's words alone travel to it (ajuste 55, 24-sep-2026: "el robot debe abstraerse tanto que no sepa casi nada de quien lo
    // comanda"): the ticket it will echo back, the action, the amount — a millimetre, a milliradian is all it can use on its own
    // odometry (ajuste 53) — and the cruise of the body declared. The route, the leg and the point stay the golem's: the feed shows them.
    private Task PublishAsync(Order order)
    {
        var (speed, _, _) = golemEmbodiment.BodyDeclared();
        return ros.PublishAsync(ros.OrderTopic, JsonSerializer.Serialize(new
        {
            order = order.Ticket, action = order.Action,
            amount = order.IsMove ? Resolution.Metres(order.Amount) : Resolution.Radians(order.Amount),
            speed
        }));
    }

    private static double Normalize(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }
}
