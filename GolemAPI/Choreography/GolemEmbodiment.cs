using System.Text.Json;
using Choreography.Theater;
using GolemAPI.Choreography.Roles;
using GolemAPI.Membrane;
using GolemAPI.Panel;
using Puppeteer;

namespace GolemAPI.Choreography;

// THE GOLEM'S EMBODIMENT: the golem given a body — the whole robot as the host sees it, mind and flesh together
// (Juan, 17-sep-2026: "algo más relacionado al robot en general, el embody o el conjunto del robot"; the class was
// `Robot` from 16-sep to 17-sep). The mind is the actor (every script and query goes to it); the flesh is the body's
// pose and what the body reports; between them, RobotMechanics — the output target, how an order becomes movement.
// Juan, 16/17-sep: "el robot solo es el cuerpo; nosotros le decimos qué hacer, y cuando termina le dice al actor por un
// endpoint que ya terminó… ahí estarán los métodos-acción: el controller recibe y valida los parámetros y llama; el
// método tiene los scripts, los valida una segunda vez, corre los scripts y hace los respectivos prints, y esos prints
// terminan llamando al Push automáticamente al terminar el script, sin necesidad de que nosotros lo disparemos".
//
// THE ROBOT PLAYS ROLES, ONE PER CAPABILITY OF ITS BODY (Juan, 18-sep-2026: "en base a las capacidades del robot: los motores,
// el botón del chasis que dice si tocamos algo… eventualmente un escáner con lidar, brazos mecánicos; si no tiene dicho rol no
// podemos mandar a usarlo"): the Displacer — the motors: the errand, the hold, the turn and the move reported, the stall — and
// the CollisionCaptor — the bumper: the bump, the operator's forget. Each role owns the scripts of its acts (Roles/); the
// embodiment builds ONLY the roles the operator declared for this body (Capabilities, `ROLES` in the compose) and the
// controllers ask the embodiment for the role, refusing the endpoint (409) when the body has none. What is the MIND's and no
// capability's stays here: the waking (Wake: the golem wakes where its body stands — the first act of every boot), the letting
// go of everything, the uptake of the peers' tells (a golem hears whatever its body can do), the print every act ends with
// (written in full in each script), the clock, the reads.
//
// EVERY ACTION IS THE SAME SHAPE (Juan, 18-sep-2026: "el método debería llamar a un script de golemActor.Using, registrar el acto
// en la global g y hacer un print con el nuevo lugar al que debe ir; ese print termina en RobotMechanics porque es el
// OutputTarget: Push > Dispatch > switch > ros > robot"): the parameters validated by the controller, ONE script — a Check in the
// domain's voice, a braced block that finds or builds the objects and performs the act on `g`, the print of what the route asks
// now — performed on the golem's actor, its answer returned to the caller (a refusal, a 409). No helper wraps the call, no
// lambda. That print is PUSHED to the output target by the engine itself: RobotMechanics defines one Reaction per act shape,
// so the body gets its next action without anybody dispatching it (lab-push-real.txt, 17-sep-2026); the returned print is kept
// only to answer the caller. No windows, no waits, no counters here: the domain concludes inside what a report meant. What stays
// the host's is the clock (asking the journal when no push brought it, the follower's linger), the wire, and the operator's
// levers on the body (let go, reset). No plan, no cursor, no legs: the journal's.
public sealed class GolemEmbodiment
{
    private readonly PerformanceV2 performance;   // the journal's entry id and the shutdown
    private readonly ActorV2 golemActor;          // the golem itself: every script and query goes to it
    private readonly IBodyWire ros;
    private readonly PanelFeed feed;
    private readonly ITellWire wire;
    private readonly string golem;
    private readonly (double X, double Y) home;
    private readonly string journalPath;

    public GolemEmbodiment(PerformanceV2 performance, IBodyWire ros, PanelFeed feed, ITellWire wire,
                 string golem, (double X, double Y) home, string journalPath, Capabilities capabilities, IReadOnlyList<string> peers = null)
    {
        if (capabilities == null) throw new ArgumentNullException(nameof(capabilities), "the embodiment needs to know which roles its body can play");
        Peers = peers ?? Array.Empty<string>();
        this.performance = performance;
        this.golemActor = performance.Actor;   // the golem itself, taken from the performance once
        this.ros = ros;
        this.feed = feed;
        this.wire = wire;
        this.golem = golem;
        this.home = home;
        this.journalPath = journalPath;
        Capabilities = capabilities;
        Mechanics = new RobotMechanics(this, ros);
        Displacer = capabilities.Has(Capabilities.Displacer) ? new Displacer(this) : null;
        Captor = capabilities.Has(Capabilities.CollisionCaptor) ? new CollisionCaptor(this) : null;
    }

    /// <summary>The golem itself — the actor every script is performed on.</summary>
    public ActorV2 Actor => golemActor;
    /// <summary>The golem's name: the journal's identity (env GOLEM), never domain state — what a touch is told with.</summary>
    public string Name => golem;
    /// <summary>The peers this golem can reach by name — its tell routes; what `--with all` means.</summary>
    public IReadOnlyList<string> Peers { get; }
    /// <summary>A line of the command language handed to a peer's console over the wire (`visit … --with blue`); null when no such peer.</summary>
    public Task<PeerReply> CommandPeerAsync(string peer, string line) => wire.CommandPeerAsync(peer, line);
    /// <summary>Where the body believes it stands (telemetry, never the journal's); null before the first word from it.</summary>
    public Pose Pose => ros.LatestPose;
    /// <summary>The robot's mechanics — the output target: the print the reactions emit, switched on and sent to the body over ROS.</summary>
    public RobotMechanics Mechanics { get; }
    /// <summary>The roles the operator declared for this body.</summary>
    public Capabilities Capabilities { get; }
    /// <summary>The motors' role — null when the body declared none: the errand and the reports of a turn or a move have nobody to go to.</summary>
    public Displacer Displacer { get; }
    /// <summary>The bumper's role — null when the body declared none: a bump report has nobody to go to.</summary>
    public CollisionCaptor Captor { get; }

    // ==================================================================
    // What the route asks now, IN THE ROBOT'S WORDS — the print every act ends with, and what the next-order reactions
    // emit. It ALWAYS says something (`pending`), so the engine pushes it even when nothing is pending and the body must
    // stop. `action`: advance | back | turnLeft | turnRight | stop (never 'decide' since 18-sep-2026: the route decides inside); `amount`:
    // the metres or radians (Juan, 17-sep-2026: "qué tanto debe moverse hacia adelante, qué tanto debe rotar"); then the
    // point the body heads to, for the panel and the log. Nothing pending: no route, no order. The route underway is found
    // ONCE and held in a local inside the guarded block (Juan, 21-sep-2026: "una variable local para no estar accediendo todo
    // el tiempo"; lab-local.txt: a braced local reads the same in a query, in an Emit and in a command, and leaks no global).
    // It is WRITTEN IN FULL wherever it is spoken — Wake, LetGo, the bumper's report, the clock's question, the reactions'
    // emit — never held in a constant and appended (Juan, 22-sep-2026: "el script ponlo completo, no lo generalices en una
    // estática"): a script reads whole where it stands.
    // ==================================================================

    // ==================================================================
    // What the peers' tells become in MY journal (the uptakes GolemSpeech binds) — the MIND's, whatever the body can do: a golem
    // without a bumper still hears where a peer bumped. Only plain @params — a nested call as an argument faults the
    // reaction matcher — the object built inside the block.
    // ==================================================================
    public const string UptakePointVisited = @"
        {
            point = Position(@x, @y);
            route = g.Follow(point);
            if (g.Strategy.OnTheWay.IsActive) {
                route = g.Dash(route);
            }
        }
        ";
    public const string UptakeBumpedAt = @"
        {
            peer = Pose(@bodyX, @bodyY, @bodyHeading);
            g.HearBump(@who, peer, @bearing);
        }
        ";
    public const string UptakeObstacleGone = @"
        {
            at = Position(@x, @y);
            g.LearnForget(at);
        }
        ";
    // A peer was called to a formation and told me I was called too (ajustes 71, 72): if I am in its fleet — the golem knows who it is —
    // I join: my own Join, from where I will stand, by my rank. The script ends with its print, like every script that changes what the
    // body must do; what pushes the order to my body at once is the next-order-join reaction on the Join (a uptake's print returns to
    // nobody). No expose: my join spreads nothing.
    // A peer convened by distance and told me where it stood (ajuste 73): if I am in the fleet — already convened, its word may take my
    // place and interrupt my route; not yet, I keep its word and convene myself, once, and my act tells everybody where I stand. The
    // peer's position enters as @px @py: x and y are the print's labels.
    public const string UptakeStoodFor = @"
        {
            fleet = Fleet(@names);
            if (fleet.Has(g)) {
                center = Position(@cx, @cy);
                side = Meters(@sideLength);
                formation = g.Choreography.Formation(@figure, center, side);
                muster = g.Choreography.Muster(@callId, formation, fleet);
                peer = fleet.Member(@teller);
                at = Position(@px, @py);
                me = fleet.Member(g);
                if (muster.Knows(me)) {
                    route = muster.Stood(peer, at);
                    if (g.Strategy.OnTheWay.IsActive) {
                        route = g.Dash(route);
                    }
                    if (route.IsPending()) {
                        print route.Id 'route', route.Order 'action';
                        if (route.IsWalkable) {
                            print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                  route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                  route.Following 'following', route.StopsLeft 'stopsLeft';
                        }
                    } else {
                        print route.Id 'route', route.Status 'ended';
                        if (route.EndedShort) {
                            print route.Why 'why';
                        }
                    }
                } else {
                    muster.Stood(peer, at);
                    from = g.Destination;
                    route = g.Choreography.Convene(from, muster, me);
                    if (g.Strategy.OnTheWay.IsActive) {
                        route = g.Dash(route);
                    }
                    if (route.IsPending()) {
                        print route.Id 'route', route.Order 'action';
                        if (route.IsWalkable) {
                            print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                  route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                  route.Following 'following', route.StopsLeft 'stopsLeft';
                        }
                    } else {
                        print route.Id 'route', route.Status 'ended';
                        if (route.EndedShort) {
                            print route.Why 'why';
                        }
                    }
                    expose @figure shape, @cx atX, @cy atY, @sideLength length, @names crew, @callId call, me.Name who, from.X stoodX, from.Y stoodY;
                }
            }
        }
        ";
    public const string UptakeCalledTo = @"
        {
            fleet = Fleet(@names);
            if (fleet.Has(g)) {
                from = g.Destination;
                center = Position(@cx, @cy);
                side = Meters(@sideLength);
                formation = g.Choreography.Formation(@figure, center, side);
                me = fleet.Member(g);
                muster = g.Choreography.Muster(@callId, formation, fleet);
                route = g.Choreography.Join(from, muster, me);
                if (g.Strategy.OnTheWay.IsActive) {
                    route = g.Dash(route);
                }
                if (route.IsPending()) {
                    print route.Id 'route', route.Order 'action';
                    if (route.IsWalkable) {
                        print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                              route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                              route.Following 'following', route.StopsLeft 'stopsLeft';
                    }
                } else {
                    print route.Id 'route', route.Status 'ended';
                    if (route.EndedShort) {
                        print route.Why 'why';
                    }
                }
            }
        }
        ";
    // A PEER STANDS ON ITS PLACE (ajuste 77): its word, recorded in the convocation of that call — if this golem has its place in it;
    // when with it everybody is placed and a step is queued, the next step opens and its route is printed (next-order-placed pushes).
    public const string UptakePlacedAt = @"
        {
            if (g.Choreography.Knows(@call)) {
                muster = g.Choreography.Muster(@call);
                peer = muster.Fleet.Member(@who);
                route = muster.Heard(peer);
                if (g.Strategy.OnTheWay.IsActive) {
                    route = g.Dash(route);
                }
                if (route.IsPending()) {
                    print route.Id 'route', route.Order 'action';
                    if (route.IsWalkable) {
                        print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                              route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                              route.Following 'following', route.StopsLeft 'stopsLeft';
                    }
                } else {
                    print route.Id 'route', route.Status 'ended';
                    if (route.EndedShort) {
                        print route.Why 'why';
                    }
                }
            }
        }
        ";
    // A STEP was asked of the fleet (ajuste 77): the same step is queued here, in the same order it was queued there (the wire keeps a
    // topic's order); it opens at once when everybody already stands on its place (next-order-step pushes).
    public const string UptakeRotateTo = @"
        {
            if (g.Choreography.Knows(@call)) {
                muster = g.Choreography.Muster(@call);
                muster.Rotate(@sense, @stepId);
                if (muster.CanStep) {
                    route = muster.Step();
                    if (g.Strategy.OnTheWay.IsActive) {
                        route = g.Dash(route);
                    }
                    if (route.IsPending()) {
                        print route.Id 'route', route.Order 'action';
                        if (route.IsWalkable) {
                            print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                  route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                  route.Following 'following', route.StopsLeft 'stopsLeft';
                        }
                    } else {
                        print route.Id 'route', route.Status 'ended';
                        if (route.EndedShort) {
                            print route.Why 'why';
                        }
                    }
                } else {
                    print muster.Queued 'queued';
                }
            }
        }
        ";

    // ==================================================================
    // THE WAKING: the golem wakes where its body stands — ONE act, the first of every boot, once the membrane brought the pose
    // (Juan, 18-sep-2026: "veo innecesario el decide"). The golem keeps the pose, so a told point taken up by a reaction — which
    // has no telemetry — is planned from there at once; with a plan underway the route decides it again from there INSIDE
    // (the body may have been carried anywhere while the golem was down). The reaction on g.Wake(_) pushes the print. The
    // act is its own block and the print of what the body must do now follows it, in full, spoken once (Juan, 21-sep-2026);
    // the pose's parameters are named `px py ptheta`, never like a print label: the journal's canonical render substitutes a
    // parameter wherever its name appears, labels included (`'x'` came out as `'6.3'` on 21-sep).
    // ==================================================================
    public Answer Wake()
    {
        var pose = ros.LatestPose;
        if (pose == null) return Answer.Refusal("no telemetry from the body yet: the golem wakes where its body stands");
        return Answer.Of(golemActor.Using(@"
                {
                    me = Pose(@px, @py, @ptheta);
                    g.Wake(me);
                }
                {
                    print g.HasPendingMission() 'pending', g.Held 'held';
                    if (g.HasPendingMission()) {
                        route = g.Underway();
                        print route.Id 'route', route.Order 'action';
                        if (route.IsWalkable) {
                            print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                  route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                  route.Following 'following', route.StopsLeft 'stopsLeft';
                        }
                    }
                }
            ")
            .WithParameters(p => {
                p["px", typeof(double)] = Resolution.Metres(pose.X);
                p["py", typeof(double)] = Resolution.Metres(pose.Y);
                p["ptheta", typeof(double)] = Resolution.Radians(pose.Theta);
            })
            .PerformCommand());
    }

    // The operator lets go of everything: every pending route abandoned with the reason, in ONE command — the mind's, no
    // capability's. No reaction matches a foreach: the body is stopped by the lever itself (LetGoAsync). The print of what
    // the body must do now follows, in full, spoken once.
    private Answer LetGo(string reason) => Answer.Of(golemActor.Using(@"
            {
                foreach (pending in g.PendingRoutes()) {
                    pending.Abandon(@reason);
                }
            }
            {
                print g.HasPendingMission() 'pending', g.Held 'held';
                if (g.HasPendingMission()) {
                    route = g.Underway();
                    print route.Id 'route', route.Order 'action';
                    if (route.IsWalkable) {
                        print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                              route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                              route.Following 'following', route.StopsLeft 'stopsLeft';
                    }
                }
            }
        ")
        .WithParameters(p => {
            p["reason", typeof(string)] = reason;
        })
        .PerformCommand());

    // What a script answered, for the record: refused → said so (the domain's words). The next order is NOT dispatched
    // here: the reaction on the act pushes it to the output target.
    internal void Report(Answer answer, string done)
    {
        if (!answer.Ok)
        {
            Console.WriteLine($"[golem {golem}] refused: {answer.Refused}");
            feed.Broadcast(new PanelEvent(performance.CurrentEntryId, "runtime", "", $"refused — {answer.Refused}", DateTime.UtcNow));
            return;
        }
        if (done != "") Console.WriteLine($"[golem {golem}] {done} (entry {performance.CurrentEntryId})");
        if (answer.Order is { IsEnded: true, Why: not "" } ended) Note($"route {ended.Route} {ended.Ended}: {ended.Why}");   // the route ended short in this act, and says why
    }

    // ==================================================================
    // The clock: awake, and now and then — the journal is asked what it wants when no push brought it (a push is
    // ephemeral: the engine promises no delivery), and the answer is obeyed only when it differs from what the body carries.
    // ==================================================================
    public async Task RunAsync(CancellationToken ct)
    {
        // Awake: once the body has said where it is, the golem wakes there — ONE act (g.Wake(me)); with a plan underway the
        // route decides it again from there inside. No pose in time: the golem starts without it, and the first act that
        // brings a pose situates it (a told point cannot be planned until then).
        var patience = DateTime.UtcNow + TimeSpan.FromSeconds(15);
        while (ros.LatestPose == null && DateTime.UtcNow < patience && !ct.IsCancellationRequested) await Task.Delay(200, ct);
        var here = ros.LatestPose;
        if (here == null) Note("no pose from the body yet: the golem starts without waking where it stands");
        else
        {
            try { Report(Wake(), $"awake where the body stands ({here.X:0.0}, {here.Y:0.0}) facing {here.Theta:0.00}"); }
            catch (Exception ex) { Note($"waking refused: {Reason(ex)}"); }
        }
        Mechanics.Dispatch(AskOrder());
        while (!ct.IsCancellationRequested)
        {
            await Task.Delay(2000, ct);
            var now = AskOrder();
            var mine = Mechanics.Carrying;
            if (now == null) { if (mine != null) Mechanics.Stop(); continue; }
            if (mine != null && mine.SameAs(now)) continue;
            Mechanics.Dispatch(now);
        }
    }

    // ==================================================================
    // The scenario the golem is in (ajuste 60)
    // ==================================================================

    /// <summary>The golem ENTERS a scenario it knows (ajuste 60): from here on its ways, distances and touches are decided on that
    /// map and its collisions — every script reads them through `g.Current` (no global map or collisions since 29-sep). The
    /// mind's own act, no role's. Refused while a route is pending, or for a name it does not know.</summary>
    public Answer Enter(string scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario)) return Answer.Refusal("a scenario needs a name");
        try
        {
            return Answer.Of(Actor.Using(
                @"
                    {
                        scenario = g.Scenario(@into);
                        g.Enter(scenario);
                        print g.Current.Name 'scenario', g.Scenarios().Count 'known';
                    }
                ")
                .WithParameters(p => { p["into", typeof(string)] = scenario.Trim(); })
                .PerformCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"enter {scenario}: " + Reason(ex)); }
    }

    // THE STRATEGIES OF NAVIGATION (ajuste 63): the golem's own objects, one fixed template per strategy — a property, never a name,
    // never a constructor (Juan: "if (g.Strategy.OnTheWay.IsActive), así déjalo, no como string")
    private const string ActivateOnTheWay = @"
                    {
                        strategy = g.Strategy.OnTheWay;
                        strategy.Activate();
                        print strategy.Name 'navigation', strategy.IsActive 'active';
                    }
                ";
    private const string ActivateDoorByDoor = @"
                    {
                        strategy = g.Strategy.DoorByDoor;
                        strategy.Activate();
                        print strategy.Name 'navigation', strategy.IsActive 'active';
                    }
                ";

    /// <summary>The golem SWITCHES ON one of its strategies of navigation (ajuste 63; Juan: "más bien es como un toggle y esos objetos
    /// ya existen dentro del golem"): `on-the-way` (the doors crossed on the way, fewer stops) or `door-by-door` (every door a leg of
    /// its own, the direct way). The strategies live in the golem from birth — `g.Strategy.OnTheWay`, `g.Strategy.DoorByDoor` — and the
    /// operator's word picks the template that activates one; nothing is built, nothing found by a name. Every route is born direct and
    /// improved by the script while on-the-way is active (ajuste 62). The mind's own act, no role's.</summary>
    public Answer Adopt(string navigation)
    {
        string which = (navigation ?? "").Trim().ToLowerInvariant();
        if (which is not ("on-the-way" or "door-by-door")) return Answer.Refusal($"a strategy of navigation is on-the-way or door-by-door, not '{navigation}'");
        try
        {
            return Answer.Of(Actor.Using(which == "on-the-way" ? ActivateOnTheWay : ActivateDoorByDoor).PerformCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"adopt {which}: " + Reason(ex)); }
    }

    // ==================================================================
    // The operator's levers on the body.
    // ==================================================================

    // Let go of everything: the body stopped and put back on its mark (ephemeral, a lab lever); the letting go itself
    // is the golem's act, one command for every pending route.
    public async Task LetGoAsync()
    {
        Mechanics.StopOnMark();
        try { await ros.TeleportAsync(home.X, home.Y, 0.0, CancellationToken.None); }
        catch { /* the world reset is best-effort; the journaled fact is the point */ }
        Report(LetGo("the operator let go of everything"), "let go of every pending route");
        Note("letting go of every pending route");
        // the body was carried: the golem wakes where it stands now (22-sep-2026, ajuste 49) — a told point that comes next is
        // planned from the real pose, not from where the body stood before the operator moved it
        await SettledAtHomeAsync(CancellationToken.None);
        try { Report(Wake(), $"awake where the body stands after the reset"); }
        catch (Exception ex) { Note($"waking after the reset refused: {Reason(ex)}"); }
    }

    /// <summary>Reborn: the body is put back on its mark and told it stands there (dead reckoning re-anchors) — and the golem
    /// waits until the membrane brings a pose ON the mark, so the waking that follows is measured from there (22-sep-2026 live:
    /// blue woke with the pose from before the teleport, planned a told point from it and turned into a wall).</summary>
    public async Task PutBackHomeAsync(CancellationToken ct)
    {
        await ros.TeleportAsync(home.X, home.Y, 0.0, ct);
        Mechanics.StopOnMark();
        await SettledAtHomeAsync(ct);
    }

    // The body's pose, as the membrane brings it, within a body's width of the mark — or five seconds of patience.
    private async Task SettledAtHomeAsync(CancellationToken ct)
    {
        var patience = DateTime.UtcNow + TimeSpan.FromSeconds(5);
        while (DateTime.UtcNow < patience && !ct.IsCancellationRequested)
        {
            var pose = ros.LatestPose;
            if (pose != null && Math.Sqrt((pose.X - home.X) * (pose.X - home.X) + (pose.Y - home.Y) * (pose.Y - home.Y)) < 0.3) return;
            await Task.Delay(100, ct);
        }
        Note("the body's pose did not settle on its mark in time: the golem wakes where the membrane says it stands");
    }

    // The hard reset — a LAB lever, not a domain fact: stop the body, wipe THIS golem's journal and exit; Docker
    // restarts the container reborn at entry 1. With cascade, every peer does the same (the hearer dedups the
    // sender's once-ids, so one side alone would swallow the next tells as repeats).
    public async Task ResetEverythingAsync(bool cascade)
    {
        Note($"reset everything — wiping the journal, the golem reboots reborn{(cascade ? ", peers too" : "")}");
        if (cascade)
            foreach (var peer in wire.Peers)
                await wire.AskPeerAsync(peer, "reset-everything", "{\"cascade\": false}");
        Mechanics.Stop();
        _ = Task.Run(async () =>
        {
            await Task.Delay(500);
            try { performance.Dispose(); } catch { /* the loop may be mid-perform; we are leaving anyway */ }
            string mine = Path.Combine(journalPath, golem);
            for (int attempt = 0; attempt < 5; attempt++)
            {
                try { if (Directory.Exists(mine)) Directory.Delete(mine, recursive: true); break; }
                catch (IOException) { await Task.Delay(200); }
            }
            Console.WriteLine($"[golem {golem}] journal wiped at {mine} — exiting for a reborn boot");
            Environment.Exit(0);
        });
    }

    // The localization experiment's readout: on dead reckoning, say — at every route's end, in the runtime lane only —
    // where the body believes it stands and where the world says it stands.
    internal void ReportLocalization(int route)
    {
        if (ros.Source != PoseSource.Wheels) return;
        var believed = ros.LatestPose;
        var truth = ros.LatestTruth;
        if (believed == null || truth == null) return;
        double off = Math.Sqrt((believed.X - truth.X) * (believed.X - truth.X) + (believed.Y - truth.Y) * (believed.Y - truth.Y));
        Note($"route {route}: the body believes it stands at ({believed.X:0.00}, {believed.Y:0.00}); the world says ({truth.X:0.00}, {truth.Y:0.00}) — {off:0.00} m apart");
    }

    internal void Note(string text)
    {
        Console.WriteLine($"[golem {golem}] {text}");
        feed.Broadcast(new PanelEvent(performance.CurrentEntryId, "runtime", "", text, DateTime.UtcNow));
    }

    /// <summary>An order sent to the body — WHOLE, for the log, the panel and the lab (kind 'order' on the feed: the print with the ticket
    /// the mechanics stamped); the body itself got only its words (ajuste 55).</summary>
    internal void Told(Order order, string text)
    {
        Console.WriteLine($"[golem {golem}] {text}");
        feed.Broadcast(new PanelEvent(performance.CurrentEntryId, "order", JsonSerializer.Serialize(new
        {
            order = order.Ticket, route = order.Route, action = order.Action, amount = order.Amount, kind = order.Kind, name = order.Name,
            x = order.X, y = order.Y, heading = order.Heading, following = order.Following, stopsLeft = order.StopsLeft,
        }), text, DateTime.UtcNow));
    }

    /// <summary>What the body reported on its result topic — its only word back (ajuste 55, 24-sep-2026): `done`, `bumped` (where it
    /// stood, facing which way, where on its shell), `stuck` (why) — handed to the role that takes it, as the /robot endpoints used to.
    /// A body without that role, or a word without its parts: noted, nothing written.</summary>
    public void Resulted(string json)
    {
        JsonDocument doc;
        try { doc = JsonDocument.Parse(json); }
        catch (JsonException) { Note($"a result I cannot read: {json}"); return; }
        using (doc)
        {
            var e = doc.RootElement;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("order", out var o) || o.ValueKind != JsonValueKind.Number
                || !e.TryGetProperty("result", out var r)) { Note($"a result without its order or its word: {json}"); return; }
            int order = o.GetInt32();
            double D(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : double.NaN;
            switch (r.GetString())
            {
                case "done":
                    if (Displacer == null) { Note("the body says done, but this body has no motors"); return; }
                    Displacer.Arrived(order);
                    return;
                case "bumped":
                    if (Captor == null) { Note("the body says it bumped, but this body has no bumper"); return; }
                    double x = D("x"), y = D("y"), heading = D("heading"), bearing = D("bearing");
                    if (double.IsNaN(x) || double.IsNaN(y) || double.IsNaN(heading) || double.IsNaN(bearing)) { Note($"a bump without where the body stood or where it was pressed: {json}"); return; }
                    Captor.Bumped(x, y, heading, bearing);
                    return;
                case "stuck":
                    if (Displacer == null) { Note("the body says it is stuck, but this body has no motors"); return; }
                    string reason = e.TryGetProperty("reason", out var why) ? (why.GetString() ?? "").Trim() : "";
                    Displacer.Stuck(order, reason == "" ? "the body could not say why" : reason);
                    return;
                default:
                    Note($"a result I do not know: {json}");
                    return;
            }
        }
    }

    // A script that faults (the domain refused inside the command, not in its Check) answers as a refusal, in its words.
    internal static string Reason(Exception ex)
    {
        var inner = ex;
        while (inner.InnerException != null) inner = inner.InnerException;
        return inner.Message;
    }

    // ------------------------------------------------------------------
    // Reads: the golem's answers, from the print of a query (JSON) — never from a shared lease: the panel's polls and the
    // body's reports reach the actor on different threads at once, and a rented Out parameter was found empty under that
    // concurrency ("Unknown parameter v", 16-sep-2026 lab).
    // ------------------------------------------------------------------

    // The handle of the route just opened, to tell it the rest.
    internal int Newest() => Read("print g.Newest().Id 'v';").GetInt32();

    // What the route asks now — the same question every script ends with, asked of the journal when no push brought it.
    private Order AskOrder()
    {
        try
        {
            return Order.Parse(golemActor.Using(@"
                {
                    print g.HasPendingMission() 'pending', g.Held 'held';
                    if (g.HasPendingMission()) {
                        route = g.Underway();
                        print route.Id 'route', route.Order 'action';
                        if (route.IsWalkable) {
                            print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                  route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                  route.Following 'following', route.StopsLeft 'stopsLeft';
                        }
                    }
                }
            ").PerformQuery());
        }
        catch (Exception e) { Console.WriteLine($"[golem {golem}] asking the order failed: {e.Message}"); return null; }
    }

    /// <summary>The body the golem declared in its journal: what the robot needs to carry an order out.</summary>
    internal (double Speed, double Radius, double Retreat) BodyDeclared()
    {
        using var doc = JsonDocument.Parse(golemActor.Using(@"
            print body.Speed.InMetersPerSecond 'speed', body.Radius.InMeters 'radius', body.Retreat.InMeters 'retreat';
        ").PerformQuery());
        var e = doc.RootElement;
        return (e.GetProperty("speed").GetDouble(), e.GetProperty("radius").GetDouble(), e.GetProperty("retreat").GetDouble());
    }

    internal double LingerAfterTold()     => Read("print body.LingerAfterTold.InSeconds 'v';").GetDouble();

    private JsonElement Read(string script, int? id = null)
    {
        using var doc = JsonDocument.Parse(golemActor.Using(script)
            .WithParameters(p => { if (id.HasValue) p["id", typeof(int)] = id.Value; })
            .PerformQuery());
        return doc.RootElement.GetProperty("v").Clone();
    }
}
