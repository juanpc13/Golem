using GolemAPI.Membrane;
using Puppeteer;

namespace GolemAPI.Choreography.Roles;

// THE DISPLACER — the role of the MOTORS: the body displaces itself through the world (Juan, 17-sep-2026: "Displacer";
// 18-sep: "las capacidades del robot: los motores"). A ROLE is the encapsulation unit of the acts ONE capability of the body
// writes in the golem's journal (puppeteer-reactions: "an actor name is a role = the encapsulation unit"); the golem keeps one
// journal, so a role is the unit INSIDE it. This one owns everything whose subject is the body's DISPLACEMENT — the errand
// and the way it decides, the hold and the letting go on, the turn and the move the body reports, the stall. It RECEIVES the
// robot (the golem with its body: the actor every act is performed on, the pose, the mechanics, the log) and builds nothing.
// The embodiment plays it only when the body declared it (Capabilities.Displacer, `ROLES` in the compose); the controller
// asks for the role and refuses the endpoint when the body has none.
//
// EVERY ACTION IS ONE ACT IN THE GOLEM'S WORDS (Juan, 18-sep-2026): the validated parameters, one script — a Check that
// refuses in the domain's voice, a braced block whose values enter as @params and whose objects are found or built inside,
// the print of what the route asks now, asked of the route the act was on — performed on the golem's actor, its answer
// returned to the caller. The print RETURNS to whoever performed the action; what reaches the body is pushed by the reaction
// the act fires (RobotMechanics.DefineReactions), so no action here dispatches anything.
public sealed class Displacer
{
    private readonly GolemEmbodiment robot;   // the golem with its body: every act is written to its actor

    public Displacer(GolemEmbodiment robot)
    {
        if (robot == null) throw new ArgumentNullException(nameof(robot), "a displacer needs the robot whose body it displaces");
        this.robot = robot;
    }

    // ==================================================================
    // THE OPERATOR'S ERRANDS (the controller validated the JSON; the script validates again, in the domain's voice)
    // ==================================================================

    /// <summary>Send the golem through points in THIS order (points only, never places). The first point opens the route from
    /// where the errand starts — the domain's own answer, `g.Destination` (ajuste 67): where the body stands, or where its last
    /// pending route ends — and the route decides its
    /// whole way inside; every further point is told to it, one entry each, and it decides again through them all. The
    /// first order is pushed to the body by the reaction on the act. Returns the last answer, or the first refusal.</summary>
    public Answer Move(IReadOnlyList<(double X, double Y)> points)
    {
        var first = points[0];
        Answer answer;
        try
        {
            answer = Answer.Of(robot.Actor.Using(
                @"
                    Check(g.KnowsWhereItStands) Error 'the golem does not know yet where its body stands: it wakes on its mark first';
                    Check(g.Current.Map.IsOnMap(Position(@sx, @sy))) Error 'that point is nowhere on the map';
                ",
                @"
                    {
                        from = g.Destination;
                        point = Position(@sx, @sy);
                        route = g.Visit(from, point);
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
                ")
                .WithParameters(p => {
                    p["sx", typeof(double)] = Resolution.Metres(first.X);
                    p["sy", typeof(double)] = Resolution.Metres(first.Y);
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"stop ({first.X:0.##}, {first.Y:0.##}): " + GolemEmbodiment.Reason(ex)); }   // no way fits the body, or the domain refused inside
        if (!answer.Ok) return Answer.Refusal($"stop ({first.X:0.##}, {first.Y:0.##}): " + answer.Refused);
        return ThenEach(robot.Newest(), points.Skip(1), answer);
    }

    // ==================================================================
    // THE FORMATIONS ARE THE GOLEM'S (propuesta 104, 8-oct-2026; Juan: "las formaciones le pertenecerán al golem y además a los otros golems
    // involucrados se les proporcionará la misma formación… el CLI sólo servirá como interfaz para administrar esas formaciones"): FORM tells
    // the golem a formation — its name, figure, centre, measure, orientation, FLEET and POLICY — and the golem keeps it by its name; the act
    // exposes it and echo-formed tells every peer, whose uptake forms the same if it is in the fleet (one hop: the uptake exposes nothing).
    // TAKE: this golem says where it stands (Convene) and the round of words opens the routes — by rank or by distance (ajuste 80); the
    // warden never says who goes where (Juan: "lo que teníamos malo era --place 0… los que resuelven todo es el dominio del golem"). ROTATE
    // queues a step of the named formation — of one ring of a double ring, or whole. DISSOLVE lets it go, in every copy. Every value enters
    // as a parameter named unlike every print label (@formationName, never @name; @measureLength, never @measure: a local named like a
    // parameter resolves to it); the STAMP is the once of the call, minted here and kept by every copy, so the tells about a formation
    // told again under the same name are new words.
    // ==================================================================

    private const string FormCheck = @"
                    Check(g.Current.Map.IsOnMap(Position(@cx, @cy))) Error 'the centre is nowhere on the map';
                ";

    private const string FormFormation = @"
                    {
                        center = Position(@cx, @cy);
                        measure = Meters(@measureLength);
                        turn = Degrees(@angle);
                        fleet = Fleet(@names);
                        formation = g.Choreography.Form(@formationName, @figure, center, measure, turn, fleet, @by, @stamp);
                        print formation.Name 'called', formation.Figure.Name 'shape', formation.Figure.Center.X 'atX', formation.Figure.Center.Y 'atY',
                              formation.Figure.Measure.InMeters 'length', formation.Figure.Turn.InDegrees 'degrees', formation.Policy 'policy',
                              fleet.Count 'crew', formation.PlaceCount 'count';
                        foreach (holders in formation.Holders()) {
                            print holders.Name 'who', holders.Index 'place', holders.X 'x', holders.Y 'y', holders.Orbit 'ring';
                        }
                        expose @formationName call, @figure shape, @cx atX, @cy atY, @measureLength length, @angle degrees, @names crew, @by policy, @stamp nonce;
                    }
                ";

    private const string FormDoubleRing = @"
                    {
                        center = Position(@cx, @cy);
                        outer = Meters(@outerRadius);
                        inner = Meters(@innerRadius);
                        turn = Degrees(@angle);
                        fleet = Fleet(@names, @innerNames);
                        formation = g.Choreography.FormRings(@formationName, center, outer, inner, turn, fleet, @by, @stamp);
                        print formation.Name 'called', formation.Figure.Name 'shape', formation.Figure.Center.X 'atX', formation.Figure.Center.Y 'atY',
                              formation.Figure.Measure.InMeters 'length', formation.Figure.Turn.InDegrees 'degrees', formation.Policy 'policy',
                              fleet.Count 'crew', formation.PlaceCount 'count';
                        foreach (holders in formation.Holders()) {
                            print holders.Name 'who', holders.Index 'place', holders.X 'x', holders.Y 'y', holders.Orbit 'ring';
                        }
                        expose @formationName call, @cx atX, @cy atY, @outerRadius outerLength, @innerRadius innerLength, @angle degrees, @names crew, @innerNames innerCrew, @by policy, @stamp nonce;
                    }
                ";

    private const string TakeCheck = @"
                    Check(g.KnowsWhereItStands) Error 'the golem does not know yet where its body stands: it wakes on its mark first';
                    Check(g.Choreography.HasFormation(@formationName)) Error 'the golem was told no formation by that name: form it first';
                ";

    // THE TAKE: this golem's word into the formation (Convene, ajuste 81); the route opens with the word that completes the round — this one
    // when the fleet is just itself, else the last heard (UptakeStoodFor). The act exposes what the peers need: the name, the stamp, who and
    // where it stood (labels never a parameter's name).
    private const string TakeFormation = @"
                    {
                        formation = g.Choreography.Find(@formationName);
                        fleet = formation.Fleet;
                        me = fleet.Member(g);
                        from = g.Destination;
                        formation.Convene(from, me);
                        if (formation.IsComplete) {
                            route = formation.Route;
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
                            print formation.StoodCount 'stood', fleet.Count 'of';
                        }
                        expose @formationName call, formation.Stamp nonce, me.Name who, from.X stoodX, from.Y stoodY;
                    }
                ";

    // THE STEP (ajuste 77; Juan: "que la flota tome la posición del otro en el sentido de las agujas del reloj y antihorario… se pueden
    // encolar"): a step is queued on the named formation — nothing moves here unless everybody already stands on its place, then the step
    // opens at once. The move is the FIGURE's (ajuste 84), of one ring or whole (propuesta 104). The act exposes what the peers need to queue
    // the same step (echo-rotate); the labels never a parameter's name.
    private const string RotateFormation = @"
                    {
                        formation = g.Choreography.Find(@formationName);
                        move = formation.Figure.Rotate(@sense, @ring);
                        formation.Queue(move, @stepId);
                        if (formation.CanStep) {
                            route = formation.Step();
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
                            print formation.Queued 'queued';
                        }
                        expose @formationName call, @sense turning, @ring orbit, @stepId step;
                    }
                ";

    private const string DissolveFormation = @"
                    {
                        formation = g.Choreography.Find(@formationName);
                        g.Choreography.Dissolve(formation);
                        print formation.Name 'called', formation.Dissolved 'gone';
                        expose formation.Name call, formation.Stamp nonce;
                    }
                ";

    /// <summary>A FORMATION TOLD (propuesta 104): its name, figure, centre, measure (a polygon's side, a circle's radius), orientation, the
    /// fleet's names and the policy — made, or made again when the name is known. Its print is the places as this golem resolved them and who
    /// holds each by rank, so the warden draws what the golem knows. The fleet is told (echo-formed). No route: nothing for the body to do.</summary>
    public Answer Form(string name, string figure, (double X, double Y) center, double measure, double angle, IReadOnlyList<string> fleet, string policy)
    {
        if (fleet == null || fleet.Count == 0) return Answer.Refusal("a formation needs a fleet: at least this golem");
        string stamp = $"{robot.Name}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";   // the once of the call, frozen as a parameter
        try
        {
            return Answer.Of(robot.Actor.Using(FormCheck, FormFormation)
                .WithParameters(p => {
                    p["formationName", typeof(string)] = name;   // never @name: the route's print has a label 'name'
                    p["figure", typeof(string)] = figure;
                    p["cx", typeof(double)] = Resolution.Metres(center.X);
                    p["cy", typeof(double)] = Resolution.Metres(center.Y);
                    p["measureLength", typeof(double)] = Resolution.Metres(measure);   // never @measure: the script's own local is named so
                    p["angle", typeof(double)] = Math.Round(angle, Resolution.Digits, MidpointRounding.AwayFromZero);
                    p["names", typeof(string)] = string.Join(",", fleet);   // never "fleet": the script's own variable holds the object built from it
                    p["by", typeof(string)] = policy.Trim().ToLowerInvariant();
                    p["stamp", typeof(string)] = stamp;
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"form {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>A DOUBLE RING TOLD (propuesta 104): the outer ring's radius and golems, the inner ring's radius and golems.</summary>
    public Answer FormRings(string name, (double X, double Y) center, double outerRadius, double innerRadius, double angle, IReadOnlyList<string> outer, IReadOnlyList<string> inner, string policy)
    {
        if (outer == null || outer.Count == 0) return Answer.Refusal("the outer ring needs at least one golem");
        if (inner == null || inner.Count == 0) return Answer.Refusal("the inner ring needs at least one golem");
        string stamp = $"{robot.Name}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        try
        {
            return Answer.Of(robot.Actor.Using(FormCheck, FormDoubleRing)
                .WithParameters(p => {
                    p["formationName", typeof(string)] = name;
                    p["cx", typeof(double)] = Resolution.Metres(center.X);
                    p["cy", typeof(double)] = Resolution.Metres(center.Y);
                    p["outerRadius", typeof(double)] = Resolution.Metres(outerRadius);   // never @outer / @inner: the script's locals are named so
                    p["innerRadius", typeof(double)] = Resolution.Metres(innerRadius);
                    p["angle", typeof(double)] = Math.Round(angle, Resolution.Digits, MidpointRounding.AwayFromZero);
                    p["names", typeof(string)] = string.Join(",", outer);
                    p["innerNames", typeof(string)] = string.Join(",", inner);
                    p["by", typeof(string)] = policy.Trim().ToLowerInvariant();
                    p["stamp", typeof(string)] = stamp;
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"form {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>THE FORMATION TAKEN (propuesta 104): this golem says where it stands; every peer of the fleet is told and says where it
    /// stands; the routes open when everybody spoke — by rank each to the place of its rank, by distance to the nearest left (the first law).
    /// Refused for a formation the golem was not told, or one it is not in. The order is pushed by the reaction on the act that opens it.</summary>
    public Answer Take(string name)
    {
        try
        {
            var answer = Answer.Of(robot.Actor.Using(TakeCheck, TakeFormation)
                .WithParameters(p => { p["formationName", typeof(string)] = name; })
                .PerformCheckThenCommand());
            return answer.Ok ? answer : Answer.Refusal($"take {name}: " + answer.Refused);
        }
        catch (Exception ex) { return Answer.Refusal($"take {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>A STEP of the named formation (ajuste 77; propuesta 104): every body of the ring — <c>whole</c>, <c>outer</c> or <c>inner</c> —
    /// takes the next place in that sense, <c>clockwise</c> or <c>counterclockwise</c>, once everybody stands on its place; steps queue. The
    /// step's id, minted here, is the once of the tell that spreads it. The sense and the ring travel as the MEMBER'S NAME of the domain's
    /// closed sets (ajuste 90): declared <c>typeof(string)</c> because the script exposes them (the engine refuses to expose an Enum).</summary>
    public Answer Rotate(string name, string sense, string ring)
    {
        if (string.IsNullOrWhiteSpace(sense)) return Answer.Refusal("a step needs its sense: clockwise or counterclockwise");
        string turning = sense.Trim().ToLowerInvariant() switch { "clockwise" => "Clockwise", "counterclockwise" => "Counterclockwise", _ => null };
        if (turning == null) return Answer.Refusal($"a step turns clockwise or counterclockwise, not '{sense}'");
        string orbit = (ring ?? "whole").Trim().ToLowerInvariant() switch { "whole" => "Whole", "outer" => "Outer", "inner" => "Inner", _ => null };
        if (orbit == null) return Answer.Refusal($"a step turns the whole figure, its outer ring or its inner ring, not '{ring}'");
        string stepId = $"{robot.Name}-step-{DateTime.UtcNow.Ticks}";   // ticks, not milliseconds: two steps asked in one millisecond are two steps
        try
        {
            var answer = Answer.Of(robot.Actor.Using(TakeCheck, RotateFormation)
                .WithParameters(p => {
                    p["formationName", typeof(string)] = name;
                    p["sense", typeof(string)] = turning;
                    p["ring", typeof(string)] = orbit;
                    p["stepId", typeof(string)] = stepId;
                })
                .PerformCheckThenCommand());
            return answer.Ok ? answer : Answer.Refusal($"rotate {name} {sense}: " + answer.Refused);
        }
        catch (Exception ex) { return Answer.Refusal($"rotate {name} {sense}: " + GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>A FORMATION DISSOLVED (propuesta 104): it leaves this golem's list — a route of it still pending let go — and every peer is told.</summary>
    public Answer Dissolve(string name)
    {
        try
        {
            var answer = Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Choreography.HasFormation(@formationName)) Error 'the golem was told no formation by that name';
                ", DissolveFormation)
                .WithParameters(p => { p["formationName", typeof(string)] = name; })
                .PerformCheckThenCommand());
            return answer.Ok ? answer : Answer.Refusal($"dissolve {name}: " + answer.Refused);
        }
        catch (Exception ex) { return Answer.Refusal($"dissolve {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>Send the golem through several points and let it choose the order that makes the way shortest: the same
    /// errand, opened with g.Cover — the route reorders the points still ahead every time one is told to it.</summary>
    public Answer Cover(IReadOnlyList<(double X, double Y)> points)
    {
        var first = points[0];
        Answer answer;
        try
        {
            answer = Answer.Of(robot.Actor.Using(
                @"
                    Check(g.KnowsWhereItStands) Error 'the golem does not know yet where its body stands: it wakes on its mark first';
                    Check(g.Current.Map.IsOnMap(Position(@sx, @sy))) Error 'that point is nowhere on the map';
                ",
                @"
                    {
                        from = g.Destination;
                        point = Position(@sx, @sy);
                        route = g.Cover(from, point);
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
                ")
                .WithParameters(p => {
                    p["sx", typeof(double)] = Resolution.Metres(first.X);
                    p["sy", typeof(double)] = Resolution.Metres(first.Y);
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"stop ({first.X:0.##}, {first.Y:0.##}): " + GolemEmbodiment.Reason(ex)); }
        if (!answer.Ok) return Answer.Refusal($"stop ({first.X:0.##}, {first.Y:0.##}): " + answer.Refused);
        return ThenEach(robot.Newest(), points.Skip(1), answer);
    }

    /// <summary>One more stop, told to the NEWEST route while it is pending — the console's `then`, the panel's next point on a route
    /// underway (propuesta 58) — the same entry Move writes for every point after the first; the route decides its way again through
    /// them all. Refused when there is no route, or the newest is no longer pending.</summary>
    public Answer Then((double X, double Y) point)
    {
        int id;
        try { id = robot.Newest(); }
        catch (Exception ex) { return Answer.Refusal("no route to tell a stop to: " + GolemEmbodiment.Reason(ex)); }
        return ThenEach(id, new[] { point }, Answer.Refusal("no route to tell a stop to"));
    }

    // Every further point is told to the route just opened, one entry each; the route decides its way again through them all.
    private Answer ThenEach(int id, IEnumerable<(double X, double Y)> points, Answer answer)
    {
        foreach (var point in points)
        {
            try
            {
                answer = Answer.Of(robot.Actor.Using(
                    @"
                        Check(g.Knows(@id) && g.Find(@id).IsPending()) Error 'the route is no longer pending';
                        Check(g.Current.Map.IsOnMap(Position(@sx, @sy))) Error 'that point is nowhere on the map';
                    ",
                    @"
                        {
                            route = g.Find(@id);
                            point = Position(@sx, @sy);
                            route.Then(point);
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
                    ")
                    .WithParameters(p => {
                        p["id", typeof(int)] = id;
                        p["sx", typeof(double)] = Resolution.Metres(point.X);
                        p["sy", typeof(double)] = Resolution.Metres(point.Y);
                    })
                    .PerformCheckThenCommand());
            }
            catch (Exception ex) { return Answer.Refusal($"stop ({point.X:0.##}, {point.Y:0.##}): " + GolemEmbodiment.Reason(ex)); }
            if (!answer.Ok) return Answer.Refusal($"stop ({point.X:0.##}, {point.Y:0.##}): " + answer.Refused);
        }
        return answer;
    }

    // ==================================================================
    // THE HOLD (Juan, 17-sep: "pausamos el cerebro, no la ruta"): the golem is held where its body stands, and let go on.
    // ==================================================================

    /// <summary>The operator holds the GOLEM where its body stands: the pose is kept, the route underway is held with it and
    /// handed back; the body stops. Plan and cursor keep.</summary>
    public Answer Pause()
    {
        var pose = robot.Pose;
        if (pose == null) return Answer.Refusal("no telemetry from the body yet: the hold needs where it stands");
        return Answer.Of(robot.Actor.Using(
            @"
                Check(g.HasPendingMission()) Error 'nothing underway: no pending route';
                Check(!g.Held) Error 'the golem is already paused';
            ",
            @"
                {
                    me = Pose(@px, @py, @ptheta);
                    route = g.Pause(me);
                    if (route.IsPending()) {
                        print route.Id 'route', route.Order 'action', g.HeldAt.X 'heldX', g.HeldAt.Y 'heldY';
                    } else {
                        print route.Id 'route', route.Status 'ended';
                        if (route.EndedShort) {
                            print route.Why 'why';
                        }
                    }
                }
            ")
            .WithParameters(p => {
                p["px", typeof(double)] = Resolution.Metres(pose.X);
                p["py", typeof(double)] = Resolution.Metres(pose.Y);
                p["ptheta", typeof(double)] = Resolution.Radians(pose.Theta);
            })
            .PerformCheckThenCommand());
    }

    /// <summary>The operator lets the golem go on: the route underway takes up its next leg from where the body stands now
    /// (it may have been pushed while standing) — the route gives that leg its heading again from there.</summary>
    public Answer Resume()
    {
        var pose = robot.Pose;
        if (pose == null) return Answer.Refusal("no telemetry from the body yet: the resume needs where it stands");
        return Answer.Of(robot.Actor.Using(
            @"
                Check(g.Held) Error 'the golem is not paused';
                Check(g.HasPendingMission()) Error 'nothing underway: no pending route';
            ",
            @"
                {
                    me = Pose(@px, @py, @ptheta);
                    route = g.Resume(me);
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
            ")
            .WithParameters(p => {
                p["px", typeof(double)] = Resolution.Metres(pose.X);
                p["py", typeof(double)] = Resolution.Metres(pose.Y);
                p["ptheta", typeof(double)] = Resolution.Radians(pose.Theta);
            })
            .PerformCheckThenCommand());
    }

    // ==================================================================
    // WHAT THE MOTORS REPORT (sim/bridge/body.py posts them; the controller validated the JSON). Each is ONE act with the pose
    // the body reports; the domain concludes inside. A report about an order the body was not given (superseded meanwhile) is
    // acknowledged and not journaled.
    // ==================================================================

    /// <summary>The body did the one thing it was told and reports it: `route.Arrive(me)` on the ROUTE UNDERWAY — the golem
    /// knows which it is; no id enters the act (Juan, 18-sep-2026; lab-underway.txt: a reaction on `[_:Golem].Underway()` pushes
    /// the print) — with where the body stands now. The EXPOSE carries only what the act does not say (ajuste 42): the route's
    /// id and whether the order done was a stop, so the reaction that tells the follower fires on a stop alone (the literal
    /// `true`) and announces for the right route; the pose it tells is captured from the `Pose(@…)` beside the act. The `route`
    /// the body echoes is the host's TICKET on the order (ajuste 55: it knows nothing of routes), to tell a stale report from the order carried. A follower on its last stop lingers
    /// before anything else, so the leader keeps its lead: the clock is the host's. Null: not the order the body was given.</summary>
    public Answer? Arrived(int order)
    {
        var was = robot.Mechanics.Carrying;
        if (was == null || was.Ticket != order) return null;   // the ticket the body echoes: a word about another order is stale (ajuste 55)
        robot.Mechanics.Done();
        if (was.IsHalt) return Halted(was);                    // the body stopped for a route that yields its place (propuesta 74)
        var here = robot.Pose ?? new Pose(was.X, was.Y, was.Heading);
        bool stop = was.IsMove && was.Kind == "stop";
        Answer answer;
        try
        {
            answer = Answer.Of(robot.Actor.Using(
                @"
                    Check(g.HasPendingMission()) Error 'nothing underway: no pending route';
                ",
                @"
                    {
                        route = g.Underway();
                        leg = route.NextLeg;
                        route.Arrive(leg);
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
                    expose @id rid, @stop reached, @sx x, @sy y;
                ")
                .WithParameters(p => {
                    p["id", typeof(int)] = was.Route;
                    p["stop", typeof(bool)] = stop;
                    p["sx", typeof(double)] = Resolution.Metres(was.X);   // the point the order the body did headed to — told to a follower when it was a stop
                    p["sy", typeof(double)] = Resolution.Metres(was.Y);
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal(GolemEmbodiment.Reason(ex)); }
        robot.Report(answer, $"route {was.Route}: {was.Describe()} done, standing at ({here.X:0.0}, {here.Y:0.0}) facing {here.Theta:0.00}");
        if (answer.Ok && answer.Order is { IsEnded: true, Ended: "completed" })
        {
            // the route completed: if it was the one to this golem's place in a formation, the golem says it stands on it (ajuste 77) — its own
            // act, so the arrival keeps its one expose (a script carries one expose: a second one unbinds the first's parameters, lab 1-oct)
            var placed = Placed();
            if (placed.Ok) { robot.Report(placed, $"route {was.Route}: on its place in the formation — the fleet is told"); return placed; }
        }
        if (!answer.Ok || !stop) return answer;
        robot.ReportLocalization(was.Route);
        if (was.Following && was.IsLastStop)
        {
            var linger = TimeSpan.FromSeconds(robot.LingerAfterTold());
            robot.Mechanics.Linger(linger);
            robot.Note($"lingering {linger.TotalSeconds:0} s at ({was.X:0.0}, {was.Y:0.0}) to keep the leader's lead");
        }
        return answer;
    }

    /// <summary>The body STOPPED for the route that yields its place and stands (propuesta 74): where it really stood — telemetry, like
    /// the hold — enters as `me`, and the convocation abandons that route and opens the one that replaces it from there —
    /// `route = muster.Halted(me)`. The script writes `route = g.Underway()` first, so `next-order-underway` pushes the new order.</summary>
    private Answer Halted(Order was)
    {
        var pose = robot.Pose;
        if (pose == null) { var refused = Answer.Refusal("no telemetry from the body: the halt needs where it stands"); robot.Report(refused, ""); return refused; }
        Answer answer;
        try
        {
            answer = Answer.Of(robot.Actor.Using(
                @"
                    Check(g.HasPendingMission()) Error 'nothing underway: no pending route';
                    Check(g.Underway().Yielding) Error 'the route underway does not yield its place';
                ",
                @"
                    {
                        me = Pose(@px, @py, @ptheta);
                        route = g.Underway();
                        formation = g.Choreography.FormationOf(route);
                        route = formation.Halted(me);
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
                ")
                .WithParameters(p => {
                    p["px", typeof(double)] = Resolution.Metres(pose.X);
                    p["py", typeof(double)] = Resolution.Metres(pose.Y);
                    p["ptheta", typeof(double)] = Resolution.Radians(pose.Theta);
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { answer = Answer.Refusal(GolemEmbodiment.Reason(ex)); }
        robot.Report(answer, $"route {was.Route}: stopped, its place went to a peer — standing at ({pose.X:0.0}, {pose.Y:0.0}) facing {pose.Theta:0.00}");
        return answer;
    }

    // THE GOLEM STANDS ON ITS PLACE (ajuste 77): the route to its place in the formation in place completed — the formation records its
    // word and, when with it everybody is placed and a step is queued, opens the next step; the act exposes who and which call, so
    // echo-placed tells every peer (the stamp in the once). No parameter: the formation in place is the domain's to know (`g.Choreography.Current`), and the Check
    // refuses when the golem does not stand on its place, or stands in no formation — nothing is written then.
    private const string PlacedScript = @"
                    {
                        formation = g.Choreography.Current;
                        me = formation.Me;
                        roundPlaced = formation.Round;
                        route = formation.Placed(me);
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
                        expose me.Name who, formation.Name call, formation.Stamp nonce, roundPlaced round;
                    }
                ";
    // `roundPlaced`, read BEFORE the word (lab 9-oct-2026): the once of the tell is the round the word is ABOUT — when this golem is the last
    // of the round and a step waits, Placed opens the step and Round is already the next one, so the key of "last in round 2" was the key of
    // "placed in round 3" and the engine kept the second tell back: the fleet waited for a word that never came, and only this golem stepped

    private Answer Placed()
    {
        try
        {
            return Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Choreography.Current.Reached) Error 'the golem does not stand on a place of the formation in place';
                ", PlacedScript)
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal(GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>The body could not: stalled, timed out. The route fails in the body's words; the next route's order follows.
    /// Null: not the order the body was given.</summary>
    public Answer? Stuck(int order, string reason)
    {
        var was = robot.Mechanics.Carrying;
        if (was == null || was.Ticket != order) return null;
        robot.Mechanics.Done();
        var answer = Failed(reason);
        robot.Report(answer, $"route {was.Route} failed: {reason}");
        return answer;
    }

    // The world said no — a collision, a stall, no way — in the body's words; the route ends, the next one's order follows.
    private Answer Failed(string reason) => Answer.Of(robot.Actor.Using(
        @"
            Check(g.HasPendingMission()) Error 'nothing underway: no pending route';
        ",
        @"
            {
                route = g.Underway();
                route.Fail(@reason);
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
        ")
        .WithParameters(p => {
            p["reason", typeof(string)] = reason;
        })
        .PerformCheckThenCommand());
}
