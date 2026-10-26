using System.Text.Json;
using GolemAPI.Membrane;
using Puppeteer;

namespace GolemAPI.Choreography.Roles;

// THE PLAYER — the role of the PLAYS (propuesta 108; a role of its own since 16-oct-2026, Juan: "la combinación del golemActor.Using + el
// string del script debe ser parte del rol"): every act the golem writes on its dynamics module — a play staged, started, ended,
// forgotten; pacman's runs and a ghost's chases; the end announced by the clock; and the words heard from the cast (the uptakes
// GolemSpeech binds). It moves the body through the motors, so the body must play the Displacer too (Capabilities). It RECEIVES the robot
// and builds nothing; the Displacer asks it at an arrival what the play says next.
public sealed class DynamicsRol
{
    private readonly GolemEmbodiment robot;   // the golem with its body: every act is written to its actor

    public DynamicsRol(GolemEmbodiment robot)
    {
        if (robot == null) throw new ArgumentNullException(nameof(robot), "a player needs the robot whose plays it keeps");
        this.robot = robot;
    }

    // ==================================================================
    // THE PLAY (propuesta 108, fase 2, 12-oct-2026): pacman AUTONOMOUS (21-oct-2026; Juan: "pacman autónomo nada más" — no keyboard, no `head`,
    // no `steer`). The play is written on every member's tab (16-oct-2026); `start <name>` opens pacman's first run, and every run that reaches
    // its end opens the next by the domain's rule (Ran, fired by the arrival like PlacedScript). Fixed templates, parameters only, never named
    // like a print label.
    // ==================================================================

    /// <summary>A PLAY STAGED on this golem: pacman by name, the ghosts by name; the cast is told (echo-staged) and every member stages the same.</summary>
    public Answer Play(string name, string kind, string pacman, IReadOnlyList<string> ghosts, string role = null)
    {
        // THE PART ON THE LINE (16-oct-2026): --as pacman|ghost, the warden's word of what this golem plays; left out, what the cast says of its name
        string part = (role ?? "").Trim().ToLowerInvariant() switch { "" => pacman.Trim().ToLowerInvariant() == robot.Name.ToLowerInvariant() ? "Pacman" : "Ghost", "pacman" => "Pacman", "ghost" => "Ghost", _ => null };
        if (part == null) return Answer.Refusal($"a golem plays pacman or a ghost, not '{role}'");
        if (string.IsNullOrWhiteSpace(name)) return Answer.Refusal("a play needs a name: play game-1 pacman …");
        if ((kind ?? "").Trim().ToLowerInvariant() != "pacman") return Answer.Refusal($"the only play known is pacman, not '{kind}'");
        if (string.IsNullOrWhiteSpace(pacman)) return Answer.Refusal("a play needs a pacman: --pacman blue");
        if (ghosts == null || ghosts.Count == 0) return Answer.Refusal("a play needs at least one ghost: --ghosts red,green");
        string stamp = $"{robot.Name}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";   // the once of the call, frozen as a parameter
        try
        {
            return Answer.Of(robot.Actor.Using(@"
                    {
                        cast = Cast(@starName, @crewNames);
                        play = g.Dynamics.Stage(@playName, @kind, cast, @part, @stamp);
                        print play.Name 'play', play.Kind 'kind', play.Stamp 'nonce', g.Dynamics.Me 'part', play.State 'state', cast.Pacman 'pacman', cast.Roster 'ghosts';
                    }
                ")
                .WithParameters(p => {
                    p["playName", typeof(string)] = name.Trim().ToLowerInvariant();   // never @name, @play: the print's labels and the script's local
                    p["kind", typeof(string)] = kind.Trim().ToLowerInvariant();
                    p["starName", typeof(string)] = pacman.Trim().ToLowerInvariant();
                    p["crewNames", typeof(string)] = string.Join(",", ghosts.Select(n => n.Trim().ToLowerInvariant()));
                    p["stamp", typeof(string)] = stamp;
                    p["part", typeof(string)] = part;        // the member's name: the engine resolves the enum at the verb (ajuste 90)
                })
                .PerformCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"play {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    // THE START (16-oct-2026; Juan: "release no me suena, quisiera algo más como empezar y el id de la dinámica… también se pone en los ghosts
    // para que empiecen a buscarlo"): `start <name>` on EVERY member's tab, each one's own — pacman's first run, a ghost's hunt (it chases from
    // then on as pacman's words release it; nothing yet to chase, it waits for them)
    /// <summary>THE START of a play on this golem (16-oct-2026): `start <name>` — pacman's first run, a ghost's hunt.</summary>
    public Answer Start(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Answer.Refusal("start: a play's name is needed, like start game-1");
        if (KindOf(name) == "traffic") return StartTraffic(name);   // the traffic play has its own start (propuesta 125)
        try
        {
            return Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Dynamics.Knows) Error 'no play is staged: play <name> pacman --pacman … --ghosts … first';
                ", @"
                    {
                        play = g.Dynamics.PacmanNamed(@playName);
                        play.Start();
                        from = g.Destination;
                        if (g.Dynamics.PlaysPacman) {
                            route = g.Dynamics.Run(from, play);
                            if (g.Strategy.OnTheWay.IsActive) {
                                route = g.Dash(route);
                            }
                            print play.Heading 'heading', play.Runs 'runs';
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
                        } else {
                            if (g.Dynamics.MayChase) {
                                route = g.Dynamics.Chase(from, play);
                                if (g.Strategy.OnTheWay.IsActive) {
                                    route = g.Dash(route);
                                }
                                print play.Chases 'chases', play.RunsSeen 'runsSeen';
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
                            } else {
                                print play.Name 'play', play.Started 'started', g.Dynamics.Released 'released', play.RunsSeen 'runsSeen';
                            }
                        }
                    }
                ")
                .WithParameters(p => { p["playName", typeof(string)] = name.Trim().ToLowerInvariant(); })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"start {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    // the run reached its decision cell: the next run opens by the rule — refused (nothing written) when this golem is not pacman running there.
    // It opens FROM WHERE THE BODY REALLY STANDS (ajuste 119, 20-oct-2026): the arrival is the body's report, and the pose it brings is the
    // one place the run's drift is corrected — a run chained from `g.Destination` started from the end the golem believed, and the error of
    // every straight run (a heading kept from an imperfect turn) added up run after run until a touch re-anchored it
    internal Answer Ran(Pose here)
    {
        if (here == null) return Answer.Refusal("no telemetry from the body: the next run needs where it stands");
        try
        {
            return Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Dynamics.Reached) Error 'the golem is not pacman standing at a decision cell of its run';
                ", @"
                    {
                        play = g.Dynamics.Pacman;
                        me = Pose(@px, @py, @ptheta);
                        route = g.Dynamics.Run(me, play);
                        if (g.Strategy.OnTheWay.IsActive) {
                            route = g.Dash(route);
                        }
                        print play.Heading 'heading', play.Runs 'runs';
                        if (route.IsPending()) {
                            print route.Id 'route', route.Order 'action', route.Waiting 'waiting';
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
                        expose play.Name title, play.Stamp nonce, play.Runs runs, play.TargetX cellX, play.TargetY cellY;
                    }
                ")
                .WithParameters(p => {
                    p["px", typeof(double)] = Resolution.Metres(here.X);
                    p["py", typeof(double)] = Resolution.Metres(here.Y);
                    p["ptheta", typeof(double)] = Resolution.Radians(here.Theta);
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal(GolemEmbodiment.Reason(ex)); }
    }

    // THE WARDEN ENDS THE PLAY (fase 4): `play over` — concluded here, announced by the clock like a catch
    /// <summary>THE WARDEN ENDS THE PLAY (fase 4): `play over` — this copy is over, its route abandoned, and the clock announces it to the cast.</summary>
    public Answer Over()
    {
        try
        {
            return Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Dynamics.Knows) Error 'no play is staged';
                ", @"
                    {
                        play = g.Dynamics.Current;
                        play.Over(@ender);
                        print play.Name 'play', play.State 'state', play.Catcher 'catcher', g.HasPendingMission() 'pending';
                    }
                ")
                .WithParameters(p => { p["ender", typeof(string)] = "warden"; })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal("play over: " + GolemEmbodiment.Reason(ex)); }
    }

    // THE END ANNOUNCED (fase 4): this copy concluded the end — a ghost and pacman met, or the warden said so — and the clock writes the act
    // whose expose the tell needs (echo-over → GameOver); nothing for the body: the route was abandoned at the conclusion
    /// <summary>The clock's question (fase 4): this copy concluded the end of the play — a ghost and pacman met, in a word heard, or the warden
    /// ended it — and has not announced it: the act is written and the reaction tells GameOver to every peer.</summary>
    public void AnnounceIfConcluded()
    {
        try
        {
            var announced = Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Dynamics.Concluded) Error 'no end of a play to announce here';
                ", @"
                    {
                        play = g.Dynamics.Concluding;
                        play.Announce();
                        print play.Name 'play', play.State 'state', play.Catcher 'catcher';
                        expose play.Name title, play.Stamp nonce, play.Catcher who;
                    }
                ")
                .PerformCheckThenCommand());
            if (announced.Ok) robot.Report(announced, "the play is over here — the cast is told");
        }
        catch { /* the clock asks again */ }
    }

    // A GHOST CHASES (fase 3): from where it stands to the cell pacman was last seen running to — at its arrival (ChaseScript, fired like
    // RanScript) or at pacman's word (UptakeRanTo writes the same act); the first chase is the way out of the den
    // a ghost's chase ended and pacman was seen somewhere new: the next chase opens — refused (nothing written) when this golem is not a
    // released ghost standing idle with somewhere new to go (fase 3); from where the body really stands, like the run (ajuste 119) — the
    // chase at pacman's word (UptakeRanTo) has no telemetry and keeps `g.Destination`
    internal Answer Chased(Pose here)
    {
        if (here == null) return Answer.Refusal("no telemetry from the body: the next chase needs where it stands");
        try
        {
            return Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Dynamics.MayChase) Error 'the golem is not a released ghost with pacman seen anywhere new';
                ", @"
                    {
                        play = g.Dynamics.Pacman;
                        me = Pose(@px, @py, @ptheta);
                        route = g.Dynamics.Chase(me, play);
                        if (g.Strategy.OnTheWay.IsActive) {
                            route = g.Dash(route);
                        }
                        print play.Chases 'chases', play.RunsSeen 'runsSeen';
                        if (route.IsPending()) {
                            print route.Id 'route', route.Order 'action', route.Waiting 'waiting';
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
                ")
                .WithParameters(p => {
                    p["px", typeof(double)] = Resolution.Metres(here.X);
                    p["py", typeof(double)] = Resolution.Metres(here.Y);
                    p["ptheta", typeof(double)] = Resolution.Radians(here.Theta);
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal(GolemEmbodiment.Reason(ex)); }
    }

    // A PLAY FORGOTTEN (16-oct-2026; Juan: "se deberían poder eliminar también"): off the list once over or never set out; a running one is
    // refused by the domain (play over first). The warden says it on every member's tab; nothing spreads.
    public Answer ForgetPlay(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) return Answer.Refusal("forget play: a play's name is needed, like forget play game-1");
        try
        {
            return Answer.Of(robot.Actor.Using(@"
        {
            play = g.Dynamics.Find(@playName);
            g.Dynamics.Forget(play);
            print play.Name 'play', g.Dynamics.Plays().Count 'left';
        }
        ")
                .WithParameters(p => { p["playName", typeof(string)] = name.Trim().ToLowerInvariant(); })
                .PerformCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"forget play {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    // ==================================================================
    // THE WORDS OF THE CAST (GolemSpeech binds them): what a peer's tell becomes in MY journal — plain @params only, the object built inside
    // ==================================================================
    // PACMAN RUNS (propuesta 108, fase 3): a peer in the cast hears which run pacman makes and to which cell — in the play it is in, by title and
    // stamp — keeps it as where pacman was seen, and, a ghost released and idle, chases it from where it stands: its own route, pushed by
    // next-order-chase. Pacman's own copy never hears its own word; a golem outside the cast lets it pass. The cell enters as @cx @cy: x and y
    // are the print's labels.
    // ==================================================================
    // THE TRAFFIC PLAY (propuesta 125, 23-oct-2026; Juan: "3 robots como en una carretera y el semáforo les habla por tell para poder pasar uno
    // recto y otro girando a su izquierda o derecha"): a light and the cars; the warden writes `play <name> traffic --light … --cars a:straight,…
    // --as light|car` on EVERY member's tab; `start <name>` starts each copy — a car drives to its stop line, the light starts its law
    // ==================================================================

    /// <summary>A TRAFFIC PLAY STAGED on this golem: the cast is the light and the cars, the journeys say every car's turn; the part is the
    /// warden's word (--as light|car; left out, what the cast says of this golem's name).</summary>
    public Answer PlayTraffic(string name, string light, string journeys, string role = null)
    {
        if (string.IsNullOrWhiteSpace(name)) return Answer.Refusal("a play needs a name: play road-1 traffic …");
        if (string.IsNullOrWhiteSpace(light)) return Answer.Refusal("a traffic play needs its light: --light orange");
        if (string.IsNullOrWhiteSpace(journeys)) return Answer.Refusal("a traffic play needs its cars and their turns: --cars red:straight,green:left");
        var cars = journeys.Split(',').Select(j => j.Split(':')[0].Trim().ToLowerInvariant()).Where(n => n != "").Distinct().ToList();
        string part = (role ?? "").Trim().ToLowerInvariant() switch { "" => light.Trim().ToLowerInvariant() == robot.Name.ToLowerInvariant() ? "Light" : "Car", "light" => "Light", "car" => "Car", _ => null };
        if (part == null) return Answer.Refusal($"a golem plays the light or a car, not '{role}'");
        string stamp = $"{robot.Name}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        try
        {
            return Answer.Of(robot.Actor.Using(@"
                    {
                        cast = Cast(@starName, @crewNames);
                        journeys = Journeys(@journeyText);
                        play = g.Dynamics.Stage(@playName, @kind, cast, journeys, @part, @stamp);
                        print play.Name 'play', play.Kind 'kind', play.Stamp 'nonce', g.Dynamics.Me 'part', play.State 'state', cast.Pacman 'light', cast.Roster 'cars', journeys.Text 'journeys';
                    }
                ")
                .WithParameters(p => {
                    p["playName", typeof(string)] = name.Trim().ToLowerInvariant();
                    p["kind", typeof(string)] = "traffic";
                    p["starName", typeof(string)] = light.Trim().ToLowerInvariant();
                    p["crewNames", typeof(string)] = string.Join(",", cars);
                    p["journeyText", typeof(string)] = journeys.Trim().ToLowerInvariant();
                    p["stamp", typeof(string)] = stamp;
                    p["part", typeof(string)] = part;   // the member's name: the engine resolves the enum at the verb (ajuste 90)
                })
                .PerformCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"play {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>The kind of the play of that name, as the journal has it — so `start <name>` picks the script of its kind.</summary>
    private string KindOf(string name)
    {
        try
        {
            string json = robot.Actor.Using(@"{ print g.Dynamics.Find(@playName).Kind 'kind'; }")
                .WithParameters(p => { p["playName", typeof(string)] = name.Trim().ToLowerInvariant(); })
                .PerformQuery();
            return System.Text.Json.JsonDocument.Parse(json).RootElement.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "";
        }
        catch { return ""; }
    }

    /// <summary>THE START of a traffic play on this golem: a car's drive — one route to its stop line (the gate), through the crossing by
    /// its turn, to the far end —; the light starts its law (the clock grants from now on).</summary>
    private Answer StartTraffic(string name)
    {
        try
        {
            return Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Dynamics.Knows) Error 'no play is staged: play <name> traffic --light … --cars … first';
                ", @"
                    {
                        play = g.Dynamics.TrafficNamed(@playName);
                        play.Start();
                        if (g.Dynamics.PlaysCar) {
                            from = g.Destination;
                            route = g.Dynamics.Drive(from, play);
                            print play.ArmName 'arm', play.Slot 'slot', play.Journeys.Text 'journeys';
                        if (route.IsPending()) {
                            print route.Id 'route', route.Order 'action', route.Waiting 'waiting';
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
                            print play.Name 'play', play.Started 'started', play.QueueText 'queue', play.IsFree 'free';
                        }
                    }
                ")
                .WithParameters(p => { p["playName", typeof(string)] = name.Trim().ToLowerInvariant(); })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"start {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>THE CAR STOPPED AT ITS LINE (propuesta 125): the arrival that left the route waiting at its gate — the car's word, from where
    /// the body stands, told to the cast (echo-stopped → StoppedAt); refused, nothing written, when the golem is no car standing at its line.</summary>
    internal Answer Stopped(Pose here)
    {
        if (here == null) return Answer.Refusal("no telemetry from the body: the stop needs where it stands");
        try
        {
            return Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Dynamics.AtItsPlace) Error 'the golem is not a car standing at a place of its queue';
                ", @"
                    {
                        play = g.Dynamics.Traffic;
                        me = Pose(@px, @py, @ptheta);
                        play = g.Dynamics.Stopped(me, play);
                        print play.Name 'play', play.ArmName 'arm', play.Slot 'slot', play.QueueText 'queue', play.IsStopped 'stopped';
                        expose play.Name title, play.Stamp nonce, g.Name who, play.ArmWord arm, play.Slot slot;
                    }
                ")
                .WithParameters(p => {
                    p["px", typeof(double)] = Resolution.Metres(here.X);
                    p["py", typeof(double)] = Resolution.Metres(here.Y);
                    p["ptheta", typeof(double)] = Resolution.Radians(here.Theta);
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal(GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>THE LIGHT GRANTS (propuesta 125): on every tick the clock asks whether the crossing is free and a car waits; the permit is the
    /// light's own act, told to the cast (echo-green → GreenFor). The law runs on the light's journal alone: one at a time, the first that stopped.</summary>
    public void GrantIfFree()
    {
        try
        {
            var granted = Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Dynamics.MayGrant) Error 'the golem is not the light with the crossing free and a car waiting';
                ", @"
                    {
                        play = g.Dynamics.Traffic;
                        permit = g.Dynamics.Grant(play);
                        print play.Name 'play', permit.To 'to', permit.Number 'number', play.QueueText 'queue';
                        expose play.Name title, play.Stamp nonce, permit.To to, permit.Number number;
                    }
                ")
                .PerformCheckThenCommand());
            if (granted.Ok) robot.Report(granted, "the light gave the crossing — the cast is told");
        }
        catch { /* the clock asks again */ }
    }

    /// <summary>THE CAR CLEARED THE CROSSING (propuesta 125): asked at every arrival — refused, nothing written, until the car holds the green
    /// and its exit point is behind; the car's word, told to the cast (echo-cleared → Cleared); its drive goes on to the far end.</summary>
    internal Answer Cleared(Pose here)
    {
        if (here == null) return Answer.Refusal("no telemetry from the body: the clearing needs where it stands");
        try
        {
            return Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Dynamics.PastTheCrossing) Error 'the golem is not a car past the crossing with the green';
                ", @"
                    {
                        play = g.Dynamics.Traffic;
                        me = Pose(@px, @py, @ptheta);
                        play = g.Dynamics.Cleared(me, play);
                        print play.Name 'play', play.ClearedText 'cleared', play.IsFree 'free', play.IsDone 'done', play.State 'state';
                        expose play.Name title, play.Stamp nonce, g.Name who;
                    }
                ")
                .WithParameters(p => {
                    p["px", typeof(double)] = Resolution.Metres(here.X);
                    p["py", typeof(double)] = Resolution.Metres(here.Y);
                    p["ptheta", typeof(double)] = Resolution.Radians(here.Theta);
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal(GolemEmbodiment.Reason(ex)); }
    }

    // A CAR STOPPED AT ITS LINE (propuesta 125): a peer's word — in the light's copy it joins the queue, in the order heard here (the law); in a
    // car's copy it is kept too (every copy reads the same state)
    public const string UptakeStoppedAt = @"
        {
            if (g.Dynamics.Told(@title)) {
                play = g.Dynamics.TrafficNamed(@title);
                play.Queued(@who, @arm, @slot);
                if (g.Dynamics.MaySetOut) {
                    route = g.Dynamics.SetOut(play);
                    print play.Name 'play', play.Slot 'slot';
                        if (route.IsPending()) {
                            print route.Id 'route', route.Order 'action', route.Waiting 'waiting';
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
                    print play.Name 'play', play.QueueText 'queue', play.IsFree 'free', g.Dynamics.MayGrant 'mayGrant';
                }
            }
        }
        ";

    /// <summary>THE CAR BEHIND SETS OUT (§12): asked at the arrival that lined it up and from the clock — refused, nothing written, until the one
    /// ahead said it stopped at its place (its word may have come before this car finished its turn).</summary>
    internal Answer SetOutIfAhead()
    {
        try
        {
            return Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Dynamics.MaySetOut) Error 'the golem is not a car lined up with the one ahead at its place';
                ", @"
                    {
                        play = g.Dynamics.Traffic;
                        route = g.Dynamics.SetOut(play);
                        print play.Name 'play', play.Slot 'slot';
                        if (route.IsPending()) {
                            print route.Id 'route', route.Order 'action', route.Waiting 'waiting';
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
                ")
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal(GolemEmbodiment.Reason(ex)); }
    }
    // THE LIGHT GAVE THE CROSSING (propuesta 125): every copy keeps who holds it; the car named GOES — its gate opens and the route goes on
    // through the crossing (next-order-go pushes the advance); any other car keeps standing
    public const string UptakeGreenFor = @"
        {
            if (g.Dynamics.Told(@title)) {
                play = g.Dynamics.TrafficNamed(@title);
                play.Granted(@to);
                if (g.Dynamics.HasGreen) {
                    route = g.Dynamics.Go(play);
                    print play.Name 'play', play.Holder 'holder';
                        if (route.IsPending()) {
                            print route.Id 'route', route.Order 'action', route.Waiting 'waiting';
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
                    if (g.Dynamics.MayAdvance) {
                        was = g.Underway();
                        me = was.Standing;
                        route = g.Dynamics.Advance(me, play);
                        print play.Name 'play', play.Slot 'slot';
                            if (route.IsPending()) {
                                print route.Id 'route', route.Order 'action', route.Waiting 'waiting';
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
                        print play.Name 'play', play.Holder 'holder', play.QueueText 'queue';
                    }
                }
            }
        }
        ";
    // A CAR CLEARED THE CROSSING (propuesta 125): every copy frees it; the last one clears, the play is over in every copy (the clock announces
    // it from the light's, like a catch)
    public const string UptakeCleared = @"
        {
            if (g.Dynamics.Told(@title)) {
                play = g.Dynamics.TrafficNamed(@title);
                play.Freed(@who);
                print play.Name 'play', play.ClearedText 'cleared', play.IsFree 'free', play.IsDone 'done', play.State 'state', g.Dynamics.MayGrant 'mayGrant';
            }
        }
        ";

    public const string UptakeRanTo = @"
        {
            if (g.Dynamics.Told(@title)) {
                play = g.Dynamics.PacmanNamed(@title);
                at = Position(@cx, @cy);
                play.Seen(at, @runs);
                if (g.Dynamics.MayChase) {
                    from = g.Destination;
                    route = g.Dynamics.Chase(from, play);
                    if (g.Strategy.OnTheWay.IsActive) {
                        route = g.Dash(route);
                    }
                    print play.Chases 'chases', play.RunsSeen 'runsSeen';
                    if (route.IsPending()) {
                        print route.Id 'route', route.Order 'action', route.Waiting 'waiting';
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
                    print play.Name 'play', play.RunsSeen 'runsSeen', g.Dynamics.Released 'released';
                }
            }
        }
        ";
    // GAME OVER (propuesta 108, fase 4): a peer announced the end of the play I am in — a ghost caught pacman, or the warden ended it — and my
    // copy closes the same way: the run or chase underway abandoned, nothing to announce in turn (one hop); pushed by next-order-heard-over
    public const string UptakeGameOver = @"
        {
            if (g.Dynamics.Told(@title)) {
                play = g.Dynamics.Find(@title);
                play.HeardOver(@who);
                print play.Name 'play', play.State 'state', play.Catcher 'catcher', g.HasPendingMission() 'pending';
            }
        }
        ";
}
