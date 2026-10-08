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

    // THE FORMATIONS (propuesta 59; ajuste 65: the square alone on the command line for now, said by its side, the places taken BY RANK —
    // the fleet's names sorted, each golem the corner of its own, no word exchanged; ajuste 69: the formation made by the golem's own
    // choreographies module, by its name — `formation = g.Choreography.Formation(@figure, center, side);` — so ONE template per effect
    // serves every shape). The side enters as @sideLength, never @side: a local named like a parameter would resolve to the parameter.
    // The call SPREADS BY TELL (ajuste 71): the act ends with an expose of what the peers need — the labels never a parameter's name —
    // and GolemSpeech's echo-called tells each peer it was called; the peers' own Join (UptakeCalledTo) carries no expose, so it spreads
    // nothing further.
    private const string JoinCheck = @"
                    Check(g.KnowsWhereItStands) Error 'the golem does not know yet where its body stands: it wakes on its mark first';
                    Check(g.Current.Map.IsOnMap(Position(@cx, @cy))) Error 'the centre is nowhere on the map';
                ";
    // THE STEP (ajuste 77; Juan: "que la flota tome la posición del otro en el sentido de las agujas del reloj y antihorario… se pueden
    // encolar"): a step is queued on the formation in place — nothing moves here unless everybody already stands on its place, then the
    // step opens at once. The act exposes what the peers need to queue the same step (echo-rotate); the labels never a parameter's name.
    private const string RotateFormation = @"
                    {
                        muster = g.Choreography.Current;
                        move = muster.Formation.Rotate(@sense);
                        muster.Queue(move, @stepId);
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
                        expose @sense turning, @stepId step, muster.Call call;
                    }
                ";

    // THE CALL (ajustes 71, 73, 80): one template for both policies. This golem says where it stands (Convene); its route opens only when
    // the whole fleet said so — with this very word when it is the last, else with the last word heard (UptakeStoodFor). The act exposes
    // what the peers need to convene in the same convocation: the formation, the fleet, the call, the POLICY and where this golem stood
    // (labels never a parameter's name; one expose per script).
    private const string CallFormation = @"
                    {
                        from = g.Destination;
                        center = Position(@cx, @cy);
                        side = Meters(@sideLength);
                        formation = g.Choreography.Formation(@figure, center, side);
                        fleet = Fleet(@names);
                        me = fleet.Member(g);
                        muster = g.Choreography.Muster(@callId, formation, fleet, @by);
                        muster.Convene(from, me);
                        if (muster.IsComplete) {
                            route = muster.Route;
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
                            print muster.StoodCount 'stood', fleet.Count 'of';
                        }
                        expose @figure shape, @cx atX, @cy atY, @sideLength length, @names crew, @callId call, @by policy, me.Name who, from.X stoodX, from.Y stoodY;
                    }
                ";

    /// <summary>The fleet is CALLED to a formation (propuesta 59; ajustes 65, 71, 73, 80): the formation by its name, at a centre with a side,
    /// the fleet's names (this golem among them) and the policy that shares the places — <c>rank</c> or <c>distance</c>. This golem says
    /// where it stands; every peer is told and says where it stands; the routes open when everybody spoke — this golem's with its own word
    /// when the fleet is just itself, else with the last word heard. The order is pushed by the reaction on the act that opens it.</summary>
    // ==================================================================
    // THE FORMATIONS THE WARDEN NAMES (propuesta 99, 8-oct-2026; Juan: "darle contexto al golem pero nunca darle la coordenada exacta de su
    // visit"): FORM tells the golem a formation — its name, figure, centre, side and orientation — and the golem keeps it by its name; TAKE
    // tells it which VERTEX of it to take, by number: where that vertex stands the golem resolves on its own map, and the route ends facing
    // the centre. Who takes which vertex is the warden's (WardenCli); no fleet, no rank, no word between golems.
    // ==================================================================

    /// <summary>A formation told — made, or made again with its new figure when the name is known. Its print is the vertices the golem
    /// resolved, so the warden can compare them with its own. No route: nothing for the body to do.</summary>
    public Answer Form(string name, string figure, (double X, double Y) center, double measure, double angle, int? places = null)
    {
        try
        {
            // two fixed templates (ajuste 101): the formation as its vertices, or laid out for that many bodies — never assembled; the count enters
            // as @bodies, never @places: a local named like a parameter resolves to it, and the loop below is named places
            return Answer.Of(robot.Actor.Using(places.HasValue
                ? @"
                    {
                        center = Position(@cx, @cy);
                        measure = Meters(@measureLength);
                        turn = Degrees(@angle);
                        formation = g.Choreography.Form(@formationName, @figure, center, measure, turn, @bodies);
                        print formation.Called 'called', formation.Name 'shape', formation.Center.X 'atX', formation.Center.Y 'atY',
                              formation.Measure.InMeters 'length', formation.Turn.InDegrees 'degrees', formation.PlaceCount 'count';
                        foreach (places in formation.Places()) {
                            print places.X 'x', places.Y 'y';
                        }
                    }
                "
                : @"
                    {
                        center = Position(@cx, @cy);
                        measure = Meters(@measureLength);
                        turn = Degrees(@angle);
                        formation = g.Choreography.Form(@formationName, @figure, center, measure, turn);
                        print formation.Called 'called', formation.Name 'shape', formation.Center.X 'atX', formation.Center.Y 'atY',
                              formation.Measure.InMeters 'length', formation.Turn.InDegrees 'degrees', formation.PlaceCount 'count';
                        foreach (places in formation.Places()) {
                            print places.X 'x', places.Y 'y';
                        }
                    }
                ")
                .WithParameters(p => {
                    p["formationName", typeof(string)] = name;   // never @name: the route's print has a label 'name'
                    p["figure", typeof(string)] = figure;
                    p["cx", typeof(double)] = Resolution.Metres(center.X);
                    p["cy", typeof(double)] = Resolution.Metres(center.Y);
                    p["measureLength", typeof(double)] = Resolution.Metres(measure);   // a side, or a circle's radius; never @measure: the script's own local is named so
                    p["angle", typeof(double)] = Math.Round(angle, Resolution.Digits, MidpointRounding.AwayFromZero);
                    if (places.HasValue) p["bodies", typeof(int)] = places.Value;   // never @places: the script's loop over the places is named so
                })
                .PerformCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"form {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>The place of that number of the formation of that name, TAKEN (a vertex, or a point of a side when it was laid out for more
    /// bodies than vertices — ajuste 101): the route from where the errand starts (`g.Destination`) to where the golem resolves that place stands, ending facing the centre; refused off the map, without room, or for a formation the
    /// golem was not told. The first order is pushed to the body by the reaction on the act (next-order-take).</summary>
    public Answer Take(string name, int place, string? sense = null)
    {
        try
        {
            // two fixed templates (ajuste 102): the place taken straight, or a STEP round the figure in that sense — along the arc on a circle
            var answer = Answer.Of(robot.Actor.Using(
                @"
                    Check(g.KnowsWhereItStands) Error 'the golem does not know yet where its body stands: it wakes on its mark first';
                    Check(g.Choreography.HasFormation(@formationName)) Error 'the golem was told no formation by that name: form it first';
                ",
                sense != null
                ? @"
                    {
                        from = g.Destination;
                        formation = g.Choreography.Find(@formationName);
                        place = formation.PlaceNumbered(@index);
                        route = g.Choreography.Take(from, formation, place, @sense);
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
                "
                : @"
                    {
                        from = g.Destination;
                        formation = g.Choreography.Find(@formationName);
                        place = formation.PlaceNumbered(@index);
                        route = g.Choreography.Take(from, formation, place);
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
                    p["formationName", typeof(string)] = name;   // never @name: the route's print has a label 'name'
                    p["index", typeof(int)] = place;
                    if (sense != null) p["sense", typeof(string)] = sense;   // the member's name; the engine resolves it to the domain's Sense (ajuste 90)
                })
                .PerformCheckThenCommand());
            return answer.Ok ? answer : Answer.Refusal($"take {name} place {place}: " + answer.Refused);
        }
        catch (Exception ex) { return Answer.Refusal($"take {name} place {place}: " + GolemEmbodiment.Reason(ex)); }
    }

    public Answer Call(string figure, (double X, double Y) center, double side, IReadOnlyList<string> fleet, string policy)
    {
        if (fleet == null || fleet.Count == 0) return Answer.Refusal("a formation needs a fleet: at least this golem");
        if (string.IsNullOrWhiteSpace(policy)) return Answer.Refusal("a call shares the places by rank or by distance");
        string callId = $"{robot.Name}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";   // one call, frozen as a parameter: the convocation's identity, the once of every tell
        try
        {
            return Answer.Of(robot.Actor.Using(JoinCheck, CallFormation)
                .WithParameters(p => {
                    p["cx", typeof(double)] = Resolution.Metres(center.X);
                    p["cy", typeof(double)] = Resolution.Metres(center.Y);
                    p["sideLength", typeof(double)] = Resolution.Metres(side);
                    p["figure", typeof(string)] = figure;   // the formation's name: the module makes it (ajuste 69)
                    p["callId", typeof(string)] = callId;
                    p["by", typeof(string)] = policy.Trim().ToLowerInvariant();
                    p["names", typeof(string)] = string.Join(",", fleet);   // never "fleet": the script's own variable `fleet` holds the object built from it
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"{figure} at ({center.X:0.##}, {center.Y:0.##}), side {side:0.##}, by {policy}: " + GolemEmbodiment.Reason(ex)); }   // no place, no way, or the domain refused inside
    }

    /// <summary>A STEP of the formation in place (ajuste 77): every body takes the next place in that sense — <c>clockwise</c> or
    /// <c>counterclockwise</c> — once everybody stands on its place; steps queue. The step's id, minted here, is the once of the tell that
    /// spreads it. Refused when the fleet stands in no formation. The sense travels as the MEMBER'S NAME of the domain's closed set
    /// <c>Sense</c> (ajuste 90; the parameters guide: the caller never names the domain's enum type, the engine resolves the name at the
    /// verb, the journal keeps it): <c>clockwise</c> on the line is <c>Clockwise</c> in the journal and in the tell to the peers. Declared
    /// <c>typeof(string)</c>, not <c>typeof(Enum)</c>: this script EXPOSES the sense for the tell that spreads the step, and the engine
    /// (2.0.1-beta.10017) refuses to expose an Enum symbol ("'Expose turning' emits a value of type 'Enum'", lab 5-oct-2026); the name as
    /// text coerces to the enum at the verb all the same.</summary>
    public Answer Rotate(string sense)
    {
        if (string.IsNullOrWhiteSpace(sense)) return Answer.Refusal("a step needs its sense: clockwise or counterclockwise");
        string member = sense.Trim().ToLowerInvariant() switch
        {
            "clockwise" => "Clockwise",
            "counterclockwise" => "Counterclockwise",
            _ => null,
        };
        if (member == null) return Answer.Refusal($"a step turns clockwise or counterclockwise, not '{sense}'");
        string stepId = $"{robot.Name}-step-{DateTime.UtcNow.Ticks}";   // ticks, not milliseconds: two steps asked in one millisecond are two steps
        try
        {
            return Answer.Of(robot.Actor.Using(
                @"
                    Check(g.KnowsWhereItStands) Error 'the golem does not know yet where its body stands: it wakes on its mark first';
                ", RotateFormation)
                .WithParameters(p => {
                    p["sense", typeof(string)] = member;
                    p["stepId", typeof(string)] = stepId;
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"rotate {sense}: " + GolemEmbodiment.Reason(ex)); }
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
                        muster = g.Choreography.MusterOf(route);
                        route = muster.Halted(me);
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

    // THE GOLEM STANDS ON ITS PLACE (ajuste 77): the route to its place in the formation in place completed — the convocation records its
    // word and, when with it everybody is placed and a step is queued, opens the next step; the act exposes who and which call, so
    // echo-placed tells every peer. No parameter: the formation in place is the domain's to know (`g.Choreography.Current`), and the Check
    // refuses when the golem does not stand on its place, or stands in no formation — nothing is written then.
    private const string PlacedScript = @"
                    {
                        muster = g.Choreography.Current;
                        me = muster.Me;
                        route = muster.Placed(me);
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
                        expose me.Name who, muster.Call call, muster.Round round;
                    }
                ";

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
