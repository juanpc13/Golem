using System.Text.Json;
using GolemAPI.Membrane;
using Puppeteer;

using GolemAPI.Commanding;

namespace GolemAPI.Choreography.Roles;

// THE CHOREOGRAPHER — the role of the FORMATIONS (16-oct-2026; Juan: "la combinación del golemActor.Using + el string del script debe ser
// parte del rol… en Roles/ se encuentren todos los scripts necesarios"): every act the golem writes on its choreographies module — a
// formation told, taken, stepped, dissolved; its word of the round (placed, lined up, the retake) and the words heard from the fleet (the
// uptakes GolemSpeech binds); the disputes forgotten. It moves the body through the motors, so the body must play the Displacer too
// (Capabilities). It RECEIVES the robot and builds nothing; the Displacer asks it at an arrival what the formation says next.
public sealed class ChoreographerRol
{
    private readonly GolemEmbodiment robot;   // the golem with its body: every act is written to its actor

    public ChoreographerRol(GolemEmbodiment robot)
    {
        if (robot == null) throw new ArgumentNullException(nameof(robot), "a choreographer needs the robot whose formations it keeps");
        this.robot = robot;
    }

    // ==================================================================
    // THE FORMATIONS ARE THE GOLEM'S (propuesta 104, 8-oct-2026; Juan: "las formaciones le pertenecerán al golem y además a los otros golems
    // involucrados se les proporcionará la misma formación… el CLI sólo servirá como interfaz para administrar esas formaciones"): FORM tells
    // the golem a formation — its name, figure, centre, measure, orientation, FLEET and POLICY — and the golem keeps it by its name; the act
    // exposes it and echo-formed tells every peer, whose uptake forms the same if it is in the fleet (one hop: the uptake exposes nothing).
    // TAKE: this golem says where it stands (Convene) and the round of words opens the routes — by rank or by distance (ajuste 80); the
    // warden never says who goes where (Juan: "lo que teníamos malo era --place 0… los que resuelven todo es el dominio del golem"). ROTATE
    // queues a step of the named formation. DISSOLVE lets it go, in every copy. Every value enters
    // as a parameter named unlike every print label (@formationName, never @name; @measureLength, never @measure: a local named like a
    // parameter resolves to it); the STAMP is the once of the call, minted here and kept by every copy, so the tells about a formation
    // told again under the same name are new words.
    // ==================================================================

    /// <summary>A FORMATION TOLD (propuesta 104): its name, figure, centre, measure (a polygon's side, a circle's radius), orientation, the
    /// fleet's names and the policy — made, or made again when the name is known. Its print is the places as this golem resolved them and who
    /// holds each by rank, so the warden draws what the golem knows. The fleet is told (echo-formed). No route: nothing for the body to do.</summary>
    public Answer Form(string name, string figure, (double X, double Y) center, double measure, double angle, IReadOnlyList<string> fleet, string policy, string cueTo = null, bool onCue = false, string after = null, double? every = null, bool open = false, int places = 0)
    {
        if (PolicyWords.Member(policy) == null) return Answer.Refusal($"the places are shared by rank, distance or rps, not by '{policy}'");
        if (fleet == null || fleet.Count == 0) return Answer.Refusal("a formation needs a fleet: at least this golem");
        string stamp = $"{robot.Name}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";   // the once of the call, frozen as a parameter
        try
        {
            var formed = Answer.Of(robot.Actor.Using(@"
                    Check(g.Current.Map.IsOnMap(Position(@cx, @cy))) Error 'the centre is nowhere on the map';
                ", @"
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
                    }
                ")
                .WithParameters(p => {
                    p["formationName", typeof(string)] = name;   // never @name: the route's print has a label 'name'
                    p["figure", typeof(string)] = figure;
                    p["cx", typeof(double)] = Resolution.Metres(center.X);
                    p["cy", typeof(double)] = Resolution.Metres(center.Y);
                    p["measureLength", typeof(double)] = Resolution.Metres(measure);   // never @measure: the script's own local is named so
                    p["angle", typeof(double)] = Math.Round(angle, Resolution.Digits, MidpointRounding.AwayFromZero);
                    p["names", typeof(string)] = string.Join(",", fleet);   // never "fleet": the script's own variable holds the object built from it
                    p["by", typeof(string)] = PolicyWords.Member(policy);   // the member's name: the engine resolves the domain's Policy by it (ajuste 90)
                    p["stamp", typeof(string)] = stamp;
                })
                .PerformCheckThenCommand());
            return Sequenced(name, stamp, cueTo, onCue, after, every, open, places, formed);
        }
        catch (Exception ex) { return Answer.Refusal($"form {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>A PROPELLER TOLD (propuesta 114): its tip, its hub and its blades; the fleet along them, a step turns it one blade.</summary>
    public Answer FormPropeller(string name, (double X, double Y) center, double radius, double hub, int blades, double angle, IReadOnlyList<string> fleet, string policy, string cueTo = null, bool onCue = false, string after = null, double? every = null, bool open = false, int places = 0)
    {
        if (PolicyWords.Member(policy) == null) return Answer.Refusal($"the places are shared by rank, distance or rps, not by '{policy}'");
        string stamp = $"{robot.Name}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        try
        {
            var formed = Answer.Of(robot.Actor.Using(@"
                    Check(g.Current.Map.IsOnMap(Position(@cx, @cy))) Error 'the centre is nowhere on the map';
                ", @"
                    {
                        center = Position(@cx, @cy);
                        tip = Meters(@tipRadius);
                        hub = Meters(@hubRadius);
                        turn = Degrees(@angle);
                        fleet = Fleet(@names);
                        formation = g.Choreography.FormPropeller(@formationName, center, tip, hub, @blades, turn, fleet, @by, @stamp);
                        print formation.Name 'called', formation.Figure.Name 'shape', formation.Figure.Center.X 'atX', formation.Figure.Center.Y 'atY',
                              formation.Figure.Measure.InMeters 'length', formation.Figure.Turn.InDegrees 'degrees', formation.Policy 'policy',
                              fleet.Count 'crew', formation.PlaceCount 'count';
                        foreach (holders in formation.Holders()) {
                            print holders.Name 'who', holders.Index 'place', holders.X 'x', holders.Y 'y', holders.Orbit 'ring';
                        }
                    }
                ")
                .WithParameters(p => {
                    p["formationName", typeof(string)] = name;
                    p["cx", typeof(double)] = Resolution.Metres(center.X);
                    p["cy", typeof(double)] = Resolution.Metres(center.Y);
                    p["tipRadius", typeof(double)] = Resolution.Metres(radius);   // never @tip / @hub: the script's locals are named so
                    p["hubRadius", typeof(double)] = Resolution.Metres(hub);
                    p["blades", typeof(int)] = blades;
                    p["angle", typeof(double)] = Math.Round(angle, Resolution.Digits, MidpointRounding.AwayFromZero);
                    p["names", typeof(string)] = string.Join(",", fleet);
                    p["by", typeof(string)] = PolicyWords.Member(policy);   // the member's name: the engine resolves the domain's Policy by it (ajuste 90)
                    p["stamp", typeof(string)] = stamp;
                })
                .PerformCheckThenCommand());
            return Sequenced(name, stamp, cueTo, onCue, after, every, open, places, formed);
        }
        catch (Exception ex) { return Answer.Refusal($"form {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    // THE TAKE: this golem's word into the formation (Convene, ajuste 81); the route opens with the word that completes the round — this one
    // when the fleet is just itself, else the last heard (UptakeStoodFor). The act exposes what the peers need: the name, the stamp, who and
    // where it stood (labels never a parameter's name).
    // TAKEN ALREADY (13-oct-2026; lab, the marching hexagon: red heard blue's word 13 ms after forming the march and, as a hearer does (ajuste 80),
    // convened by itself; its own take that followed was REFUSED — "already took its place" — and the console's queue, taking a refusal for the end
    // of the line, went on at once to the next `form`, which made the march again under the fleet's feet): the take asks first whether this golem
    // already spoke in that formation, and if so it is taken — nothing written, the route (or the round's wait) is the one its word opened.
    /// <summary>THE FORMATION TAKEN (propuesta 104): this golem says where it stands; every peer of the fleet is told and says where it
    /// stands; the routes open when everybody spoke — by rank each to the place of its rank, by distance to the nearest left (the first law).
    /// Refused for a formation the golem was not told, or one it is not in. The order is pushed by the reaction on the act that opens it.</summary>
    public Answer Take(string name)
    {
        if (IsUnion(name)) return TakeUnion(name);   // the union's ONE word (propuesta 123)
        try
        {
            using (var taken = JsonDocument.Parse(robot.Actor.Using(@"
                    {
                        if (g.Choreography.HasFormation(@formationName)) {
                            formation = g.Choreography.Find(@formationName);
                            fleet = formation.Fleet;
                            if (fleet.Has(g)) {
                                me = fleet.Member(g);
                                print formation.Knows(me) 'spoke', formation.StoodCount 'stood', fleet.Count 'of', formation.Stranded 'stranded';
                            } else {
                                print false 'spoke';
                            }
                        } else {
                            print false 'spoke';
                        }
                    }
                ").WithParameters(p => { p["formationName", typeof(string)] = name; }).PerformQuery()))
                if (taken.RootElement.TryGetProperty("spoke", out var spoke) && spoke.ValueKind == JsonValueKind.True)
                {
                    if (taken.RootElement.TryGetProperty("stranded", out var st) && st.ValueKind == JsonValueKind.True) return TakeAgain(name, false);   // stranded: taken again (22-oct-2026)
                    robot.Note($"take {name}: already taken — it convened when it heard a peer's word; nothing written");
                    return Answer.Of(taken.RootElement.GetRawText());
                }
            var answer = Answer.Of(robot.Actor.Using(@"
                    Check(g.KnowsWhereItStands) Error 'the golem does not know yet where its body stands: it wakes on its mark first';
                    Check(g.Choreography.HasFormation(@formationName)) Error 'the golem was told no formation by that name: form it first';
                ", @"
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
                            print formation.StoodCount 'stood', fleet.Count 'of';
                        }
                        expose @formationName call, formation.Stamp nonce, me.Name who, from.X stoodX, from.Y stoodY;
                    }
                ")
                .WithParameters(p => { p["formationName", typeof(string)] = name; })
                .PerformCheckThenCommand());
            if (!answer.Ok) return Answer.Refusal($"take {name}: " + answer.Refused);
            // the body already stands on its place: the route opened is nothing to walk and completed at once — no arrival will say it, so the golem
            // says it here (13-oct-2026), and the fleet hears PlacedAt instead of waiting for a line-up that never comes
            var placed = Placed();
            if (placed.Ok) { robot.Report(placed, $"take {name}: already on its place — the fleet is told"); return placed; }
            return answer;
        }
        catch (Exception ex) { return Answer.Refusal($"take {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    // THE CUE BETWEEN DANCERS (propuesta 117, 19-oct-2026; Juan: "el último del primer grupo le hace la seña a alguno del otro grupo… a él sólo le
    // dicen 'continúas tú' y éste le avisa a su equipo"; since propuesta 123, 21-oct-2026 — "cómo solucionar el problema sin coordinador" — the cue
    // names a FORMATION and every member of it takes the cue by itself, no relay): a formation told `--cue <formation>` cues that formation when every
    // member finished — its own act right after the Form; one told `--on-cue` is born waiting, its take IS the cue — its own act too. The Form
    // templates stay untouched.
    private Answer Sequenced(string name, string stamp, string cueTo, bool onCue, string after, double? every, bool open, int places, Answer formed)
    {
        if (!formed.Ok) return formed;
        try
        {
            if (open)
            {
                // THE OPEN FORMATION (propuesta 123): it belongs to the union that names it — said before it on the same tab — and its fleet is the
                // union's candidates until the union's law picks its members; if the words came first and with this the round is complete, the
                // law runs here and this golem's route may open (next-order-open pushes it)
                var opened = Answer.Of(robot.Actor.Using(@"
                        Check(g.Choreography.HasFormation(@formationName)) Error 'the golem was told no formation by that name';
                    ", @"
                        {
                            formation = g.Choreography.Find(@formationName);
                            union = g.Choreography.Open(formation, @openPlaces);
                            print formation.Name 'called', union.Name 'union', union.Candidates.Roster 'candidates', formation.Capacity 'places', union.IsResolved 'resolved';
                            if (union.IsResolved) {
                                if (union.IsMember) {
                                    mine = union.Mine;
                                    print mine.Name 'mine', mine.HasRoute 'routed';
                                    if (mine.HasRoute) {
                                        route = mine.Route;
                                        if (g.Strategy.OnTheWay.IsActive) {
                                            route = g.Dash(route);
                                        }
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
                                } else {
                                    print union.IsLeftOut 'leftOut';
                                }
                            }
                        }
                    ")
                    .WithParameters(p => { p["formationName", typeof(string)] = name; p["openPlaces", typeof(int)] = Math.Max(0, places); })   // 0: its vertices
                    .PerformCheckThenCommand());
                if (!opened.Ok) return Answer.Refusal($"form {name} --open: " + opened.Refused);
                robot.Note($"form {name}: open — of the union {Read(opened, "union")}, {Read(opened, "places")} place(s) for its candidates ({Read(opened, "candidates")}); the union's law picks its members{(Read(opened, "resolved") == "true" ? " — resolved now" : "")}");
            }
            if (cueTo != null)
            {
                var cues = Answer.Of(robot.Actor.Using(@"
                        Check(g.Choreography.HasFormation(@formationName)) Error 'the golem was told no formation by that name';
                    ", @"
                        {
                            formation = g.Choreography.Find(@formationName);
                            formation.Cues(@cueTo);
                            print formation.Name 'called', formation.CueTo 'cueTo';
                        }
                    ")
                    .WithParameters(p => { p["formationName", typeof(string)] = name; p["cueTo", typeof(string)] = cueTo; })
                    .PerformCheckThenCommand());
                if (!cues.Ok) return Answer.Refusal($"form {name} --cue {cueTo}: " + cues.Refused);
                robot.Note($"form {name}: cues the formation {cueTo} when every member finished — every member of it takes the cue (propuesta 123)");
            }
            if (onCue)
            {
                var waits = Answer.Of(robot.Actor.Using(@"
                        Check(g.Choreography.HasFormation(@formationName)) Error 'the golem was told no formation by that name';
                    ", @"
                        {
                            formation = g.Choreography.Find(@formationName);
                            formation.StartsOnCue();
                            print formation.Name 'called', formation.OnCue 'onCue';
                        }
                    ")
                    .WithParameters(p => { p["formationName", typeof(string)] = name; })
                    .PerformCheckThenCommand());
                if (!waits.Ok) return Answer.Refusal($"form {name} --on-cue: " + waits.Refused);
                robot.Note($"form {name}: starts on cue — its take is the word 'your turn' to one of its members");
            }
            if (after != null)
            {
                // THE PULL (propuesta 118): the formation starts after another, and its asker COMMITS ITSELF (propuesta 122, 21-oct-2026; the engine's
                // own `remember`, paper 0B) to ask THAT FORMATION, every so often, whether it finished (propuesta 123: no golem is named on the line —
                // the first by rank of the formation asked answers, Choreographies.Answers). The Follows act says the fact; THE CLOCK ARMS THE RECURRENCE
                // (ArmIfAsker, the next tick) once the asker is known — at once for a formation with its fleet told, once the union resolved for an
                // open one, whose asker is the first of its MEMBERS.
                double period = every is { } told && told > 0 ? told : 5.0;   // the domain's own default (Formation.DefaultEverySeconds), internal to it
                var follows = Answer.Of(robot.Actor.Using(@"
                        Check(g.Choreography.HasFormation(@formationName)) Error 'the golem was told no formation by that name';
                    ", @"
                        {
                            formation = g.Choreography.Find(@formationName);
                            period = Seconds(@everySeconds);   // never @every: a local named like a parameter resolves to it (ajuste 65)
                            formation.Follows(@after, period);
                            print formation.Name 'called', formation.After 'after', formation.Asker 'asker', formation.EverySeconds 'every', formation.IsAsker 'asks';
                        }
                    ")
                    .WithParameters(p =>
                    {
                        p["formationName", typeof(string)] = name; p["stamp", typeof(string)] = stamp; p["after", typeof(string)] = after;
                        p["everySeconds", typeof(double)] = Math.Round(period, 3);
                    })
                    .PerformCheckThenCommand());
                if (!follows.Ok) return Answer.Refusal($"form {name} --after {after} --every {period:0.#}s: " + follows.Refused);
                robot.Note($"form {name}: starts after {after} — its asker ({(open ? "the first by rank of its members, once the union resolves" : Read(follows, "asker"))}) commits itself on the next tick to ask {after} every {period:0.#} s whether it finished (a recurrence of its own, until the answer comes; the first by rank of {after} answers)");
            }
            return formed;
        }
        catch (Exception ex) { return Answer.Refusal($"form {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>THE RECURRENCE ARMED (propuesta 123, 21-oct-2026; the engine's own `remember`, propuesta 122): the clock asks whether a formation of this golem
    /// has its asker's recurrence due — this golem the asker, the formation waiting for its answer, an open one enrolled — and writes ONE act:
    /// <c>formation.ArmInquiry()</c> with the <c>remember</c> beside it, which asks (<c>asking.Ask()</c>, told by echo-ask) every so often while the domain
    /// says it still asks (<c>StillAsks</c>: the answer not come, the formation not taken nor dissolved) — the period crosses as the engine wants it: the
    /// first question one period from now, the frequency in ticks (the DSL knows no TimeSpan), a times count of an hour's questions (ENGINE FINDING, lab
    /// 21-oct-2026: a times count omitted is ONE, so the while alone bounds nothing); the host's AttentionClock knocks and the engine rebuilds the pending
    /// recurrence from the journal on replay.</summary>
    public void ArmIfAsker()
    {
        try
        {
            string name, stamp; double period;
            using (var due = JsonDocument.Parse(robot.Actor.Using(@"
                    {
                        print g.Choreography.ArmsInquiry 'due';
                        if (g.Choreography.ArmsInquiry) {
                            arming = g.Choreography.InquiryToArm;
                            print arming.Name 'called', arming.Stamp 'nonce', arming.EverySeconds 'every';
                        }
                    }
                ").PerformQuery()))
            {
                var e = due.RootElement;
                if (!e.TryGetProperty("due", out var d) || d.ValueKind != JsonValueKind.True) return;
                name = e.GetProperty("called").GetString() ?? ""; stamp = e.GetProperty("nonce").GetString() ?? "";
                period = e.TryGetProperty("every", out var ev) && ev.ValueKind == JsonValueKind.Number ? ev.GetDouble() : 0.0;
            }
            if (period <= 0) period = 5.0;
            var armed = Answer.Of(robot.Actor.Using(@"
                    Check(g.Choreography.ArmsInquiry) Error 'no recurrence of this golem is due to arm';
                ", @"
                    {
                        formation = g.Choreography.Find(@formationName);
                        formation.ArmInquiry();
                        remember (@firstAt, @everyTicks, @timesAtMost) {
                            asking = g.Choreography.Find(@formationName);
                            asking.Ask();
                            print asking.Name 'called', asking.Asked 'asked', @attempt 'attempt';
                            expose asking.After call, asking.Stamp nonce, asking.Asker who, asking.Asked asked;
                        }
                        while (g.Choreography.StillAsks(@formationName, @stamp));
                        print formation.Name 'called', formation.After 'after', formation.EverySeconds 'every', formation.InquiryArmed 'armed';
                    }
                ")
                .WithParameters(p =>
                {
                    p["formationName", typeof(string)] = name; p["stamp", typeof(string)] = stamp;
                    p["firstAt", typeof(DateTime)] = DateTime.Now.AddSeconds(period);
                    p["everyTicks", typeof(long)] = TimeSpan.FromSeconds(period).Ticks;
                    p["timesAtMost", typeof(int)] = Math.Max(1, (int)Math.Ceiling(3600.0 / period));   // an hour of questions at most: the engine takes a times count omitted as ONE (lab 21-oct-2026), so the while alone bounds nothing
                })
                .PerformCheckThenCommand());
            if (armed.Ok) robot.Note($"the pull: this golem, the asker of {Read(armed, "called")}, commits itself to ask {Read(armed, "after")} every {period:0.#} s whether it finished (a recurrence of its own, until the answer comes)");
        }
        catch { /* the clock asks again */ }
    }

    /// <summary>THE PROGRAMME TOLD (propuesta 123): its formations in order, its candidates (left out: this golem and every peer) and how they are shared
    /// — distance or rps. Told again under the same name, made again. Every candidate is told the same on its own tab (propuesta 112).</summary>
    public Answer Union(string name, IReadOnlyList<string> formations, IReadOnlyList<string> fleet, string policy, string link = "cue", double? every = null)
    {
        if (PolicyWords.Member(policy) == null) return Answer.Refusal($"the candidates are shared by distance or rps, not by '{policy}'");
        // THE LINK (propuesta 124): how the beats are agreed — the member's name travels, the engine resolves it at the verb (ajuste 90)
        string agreed = (link ?? "cue").Trim().ToLowerInvariant() switch { "cue" => "Cue", "ask" => "Ask", _ => null };
        if (agreed == null) return Answer.Refusal($"the beats are agreed by cue or ask, not by '{link}'");
        if (agreed == "Ask") return Answer.Refusal("the beats by asking are not built yet: --link cue");   // refused HERE, before the act: a verb that passes its Check and throws inside is journaled whole and replays as an error (lab 23-oct-2026, lab-enum-replay.txt)
        if (every.HasValue && every.Value <= 0) return Answer.Refusal("a union asks every so often: seconds above zero");
        if (fleet == null || fleet.Count == 0) return Answer.Refusal("a union needs candidates: at least this golem");
        if (formations == null || formations.Count == 0) return Answer.Refusal("a union names at least one formation");
        string stamp = $"{robot.Name}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        try
        {
            var answer = Answer.Of(robot.Actor.Using(@"
                    {
                        fleet = Fleet(@names);
                        period = Seconds(@everySeconds);
                        union = g.Choreography.Union(@unionName, @formationNames, fleet, @by, @link, period, @stamp);
                        print union.Name 'called', union.Bill 'formations', fleet.Roster 'candidates', union.Policy 'policy', union.Link 'link', union.StoodCount 'stood', union.IsKnown 'known', union.IsResolved 'resolved';
                    }
                ")
                .WithParameters(p => {
                    p["unionName", typeof(string)] = name;
                    p["formationNames", typeof(string)] = string.Join(",", formations);
                    p["names", typeof(string)] = string.Join(",", fleet);
                    p["by", typeof(string)] = PolicyWords.Member(policy);
                    p["link", typeof(string)] = agreed;
                    p["everySeconds", typeof(double)] = every ?? 5.0;   // the domain's default (Formation.DefaultEverySeconds, internal)
                    p["stamp", typeof(string)] = stamp;
                })
                .PerformCommand());
            return answer.Ok ? answer : Answer.Refusal($"union {name}: " + answer.Refused);
        }
        catch (Exception ex) { return Answer.Refusal($"union {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>THE PROGRAM (propuesta 124, 22-oct-2026, night; Juan: "un solo comando con toda la definición… el dominio debería crear el objeto completo
    /// del sindicato"): ONE line on every candidate's tab — the union, its formations and its score as one definition the domain reads
    /// (<c>g.Choreography.Program</c>: the union made, every formation made and opened into it, its steps and rests queued, its finish implied) — and
    /// in the same act this golem's word, where it stands (the Convene <c>echo-stood-union</c> and <c>next-order-convene-union</c> observe).</summary>
    public Answer Program(string name, string definition, IReadOnlyList<string> fleet, string policy, string link = "cue", double? every = null)
    {
        if (string.IsNullOrWhiteSpace(definition)) return Answer.Refusal("a program needs its definition: the formations and their score");
        if (PolicyWords.Member(policy) == null) return Answer.Refusal($"the candidates are shared by distance or rps, not by '{policy}'");
        if (fleet == null || fleet.Count == 0) return Answer.Refusal("a program needs candidates: at least this golem");
        string agreed = (link ?? "cue").Trim().ToLowerInvariant() switch { "cue" => "Cue", "ask" => "Ask", _ => null };
        if (agreed == null) return Answer.Refusal($"the beats are agreed by cue or ask, not by '{link}'");
        if (agreed == "Ask") return Answer.Refusal("the beats by asking are not built yet: --link cue");   // refused HERE, before the act: a verb that passes its Check and throws inside is journaled whole and replays as an error (lab 23-oct-2026, lab-enum-replay.txt)
        if (every.HasValue && every.Value <= 0) return Answer.Refusal("a union asks every so often: seconds above zero");
        string stamp = $"{robot.Name}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        try
        {
            var answer = Answer.Of(robot.Actor.Using(@"
                    Check(g.KnowsWhereItStands) Error 'the golem does not know yet where its body stands: it wakes on its mark first';
                ", @"
                    {
                        fleet = Fleet(@names);
                        period = Seconds(@everySeconds);
                        union = g.Choreography.Program(@programName, @definition, fleet, @by, @link, period, @stamp);
                        me = fleet.Member(g);
                        from = g.Destination;
                        union.Convene(from, me);
                        print union.Name 'called', union.Bill 'formations', fleet.Roster 'candidates', union.Policy 'policy', union.Link 'link', union.StoodCount 'stood', fleet.Count 'of', union.IsKnown 'known', union.IsResolved 'resolved';
                        if (union.IsResolved) {
                            if (union.IsMember) {
                                mine = union.Mine;
                                print mine.Name 'mine', mine.HasRoute 'routed';
                                if (mine.HasRoute) {
                                    route = mine.Route;
                                    if (g.Strategy.OnTheWay.IsActive) {
                                        route = g.Dash(route);
                                    }
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
                            } else {
                                print union.IsLeftOut 'leftOut';
                            }
                        }
                        expose @programName call, union.Stamp nonce, me.Name who, from.X stoodX, from.Y stoodY;
                    }
                ")
                .WithParameters(p => {
                    p["programName", typeof(string)] = name;
                    p["definition", typeof(string)] = definition;
                    p["names", typeof(string)] = string.Join(",", fleet);
                    p["by", typeof(string)] = PolicyWords.Member(policy);
                    p["link", typeof(string)] = agreed;
                    p["everySeconds", typeof(double)] = every ?? 5.0;
                    p["stamp", typeof(string)] = stamp;
                })
                .PerformCheckThenCommand());
            if (!answer.Ok) return Answer.Refusal($"program {name}: " + answer.Refused);
            var placed = Placed();
            if (placed.Ok) { robot.Report(placed, $"program {name}: already on its place — the fleet is told"); return placed; }
            return answer;
        }
        catch (Exception ex) { return Answer.Refusal($"program {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    // THE PROGRAMME TAKEN (propuesta 123): this golem's ONE word for the whole union — where it stands; the fleet's words complete the round and the
    // law shares everybody among the figures: this golem's route in its own formation opens (or that formation waits for its cue), or it was left out
    private Answer TakeUnion(string name)
    {
        try
        {
            using (var spoke = JsonDocument.Parse(robot.Actor.Using(@"
                    {
                        union = g.Choreography.FindUnion(@unionName);
                        fleet = union.Candidates;
                        if (fleet.Has(g)) {
                            me = fleet.Member(g);
                            print union.Knows(me) 'spoke', union.StoodCount 'stood', fleet.Count 'of', union.IsStranded 'stranded';
                        } else {
                            print false 'spoke';
                        }
                    }
                ").WithParameters(p => { p["unionName", typeof(string)] = name; }).PerformQuery()))
                if (spoke.RootElement.TryGetProperty("spoke", out var s) && s.ValueKind == JsonValueKind.True)
                {
                    if (spoke.RootElement.TryGetProperty("stranded", out var st) && st.ValueKind == JsonValueKind.True) return TakeAgain(name, true);   // stranded in its formation: taken again (22-oct-2026)
                    robot.Note($"take {name}: this golem spoke for the union already; nothing written");
                    return Answer.Of(spoke.RootElement.GetRawText());
                }
            var answer = Answer.Of(robot.Actor.Using(@"
                    Check(g.KnowsWhereItStands) Error 'the golem does not know yet where its body stands: it wakes on its mark first';
                    Check(g.Choreography.HasUnion(@unionName)) Error 'the golem was told no union by that name: say it first';
                ", @"
                    {
                        union = g.Choreography.FindUnion(@unionName);
                        fleet = union.Candidates;
                        me = fleet.Member(g);
                        from = g.Destination;
                        union.Convene(from, me);
                        if (union.IsResolved) {
                            if (union.IsMember) {
                                mine = union.Mine;
                                print mine.Name 'mine', mine.HasRoute 'routed';
                                if (mine.HasRoute) {
                                    route = mine.Route;
                                    if (g.Strategy.OnTheWay.IsActive) {
                                        route = g.Dash(route);
                                    }
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
                            } else {
                                print union.IsLeftOut 'leftOut';
                            }
                        } else {
                            print union.StoodCount 'stood', fleet.Count 'of', union.IsKnown 'known';
                        }
                        expose @unionName call, union.Stamp nonce, me.Name who, from.X stoodX, from.Y stoodY;
                    }
                ")
                .WithParameters(p => { p["unionName", typeof(string)] = name; })
                .PerformCheckThenCommand());
            if (!answer.Ok) return Answer.Refusal($"take {name}: " + answer.Refused);
            var placed = Placed();
            if (placed.Ok) { robot.Report(placed, $"take {name}: already on its place — the fleet is told"); return placed; }
            return answer;
        }
        catch (Exception ex) { return Answer.Refusal($"take {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    // A STRANDED MEMBER TAKEN AGAIN (22-oct-2026, night; Juan: "que un miembro varado no deje al sindicato esperando… take <union> retoma al varado"):
    // the warden's `take` on a golem that spoke already and whose formation is stranded for it — the route to its place opened again from where
    // the body really stands (telemetry, like the halt and the retake), the stranding forgotten; refused again, stranded again, said in the read
    private Answer TakeAgain(string name, bool ofUnion)
    {
        var pose = robot.Pose;
        if (pose == null) return Answer.Refusal($"take {name}: no telemetry from the body — taking the place again needs where it stands");
        try
        {
            var answer = Answer.Of(robot.Actor.Using(ofUnion ? @"
                    Check(g.Choreography.FindUnion(@formationName).IsStranded) Error 'this golem is not stranded in its formation of the union';
                " : @"
                    Check(g.Choreography.Find(@formationName).Stranded) Error 'this golem is not stranded in that formation';
                ", ofUnion ? @"
                    {
                        me = Pose(@px, @py, @ptheta);
                        union = g.Choreography.FindUnion(@formationName);
                        formation = union.Mine;
                        route = formation.TakeAgain(me);
                        if (g.Strategy.OnTheWay.IsActive) {
                            route = g.Dash(route);
                        }
                        print formation.Name 'called', formation.Stranded 'stranded', formation.StrandedWhy 'why';
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
                " : @"
                    {
                        me = Pose(@px, @py, @ptheta);
                        formation = g.Choreography.Find(@formationName);
                        route = formation.TakeAgain(me);
                        if (g.Strategy.OnTheWay.IsActive) {
                            route = g.Dash(route);
                        }
                        print formation.Name 'called', formation.Stranded 'stranded', formation.StrandedWhy 'why';
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
                    p["formationName", typeof(string)] = name;
                    p["px", typeof(double)] = Resolution.Metres(pose.X);
                    p["py", typeof(double)] = Resolution.Metres(pose.Y);
                    p["ptheta", typeof(double)] = Resolution.Radians(pose.Theta);
                })
                .PerformCheckThenCommand());
            if (!answer.Ok) return Answer.Refusal($"take {name}: " + answer.Refused);
            robot.Note($"take {name}: stranded — its place taken again from where the body stands ({pose.X:0.0}, {pose.Y:0.0}){(Read(answer, "stranded") == "true" ? $"; stranded again: {Read(answer, "why")}" : "")}");
            var placed = Placed();
            if (placed.Ok) { robot.Report(placed, $"take {name}: already on its place — the fleet is told"); return placed; }
            return answer;
        }
        catch (Exception ex) { return Answer.Refusal($"take {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    private bool IsUnion(string name)
    {
        try
        {
            using var doc = JsonDocument.Parse(robot.Actor.Using("{ print g.Choreography.HasUnion(@unionName) 'union'; }").WithParameters(p => { p["unionName", typeof(string)] = name; }).PerformQuery());
            return doc.RootElement.TryGetProperty("union", out var v) && v.ValueKind == JsonValueKind.True;
        }
        catch { return false; }
    }

    /// <summary>The clock's question (propuesta 118, the pull): did somebody ask this golem whether a formation finished? Then it answers from its own
    /// copy — <c>response = g.Choreography.Answer()</c> — and echo-answer tells <c>FinishedState</c> to the one who asked.</summary>
    public void AnswerIfAsked()
    {
        try
        {
            var answered = Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Choreography.Answering) Error 'nobody asked this golem anything';
                ", @"
                    {
                        response = g.Choreography.Answer();
                        print response.Call 'called', response.To 'to', response.Done 'done';
                        expose response.Call call, response.To to, response.Done done, response.Number number;
                    }
                ")
                .PerformCheckThenCommand());
            if (answered.Ok) robot.Note($"the pull: answered {Read(answered, "to")} about {Read(answered, "called")} — finished: {Read(answered, "done")}");
        }
        catch { /* the clock asks again */ }
    }

    private static string Read(Answer answer, string field)
    {
        try { return JsonDocument.Parse(answer.Print ?? "{}").RootElement.TryGetProperty(field, out var v) ? v.ToString() : ""; } catch { return ""; }
    }

    /// <summary>THIS GOLEM FINISHES ITS PART of the formation (propuesta 117): the last line of its script about it, <c>finish tri-left</c> — queued
    /// like the steps: with steps still to walk it means "when what is queued is done, I finished", and the clock writes it again when the part is
    /// done (<see cref="FinishIfDone"/>); the act that counts exposes <c>finishedNow</c> true and echo-finished tells the fleet. Refused for a
    /// formation the golem was not told, or is not of.</summary>
    public Answer Finish(string name)
    {
        try
        {
            var answer = Answer.Of(robot.Actor.Using(@"
                    Check(g.Choreography.HasFormation(@formationName)) Error 'the golem was told no formation by that name';
                ", @"
                    {
                        formation = g.Choreography.Find(@formationName);
                        me = formation.Fleet.Member(g);
                        route = formation.Finish(me);
                        print formation.Name 'called', formation.Finishing 'finishing', formation.FinishedCount 'finished', formation.Fleet.Count 'of', formation.IsFinished 'done';
                        expose formation.Name call, formation.Stamp nonce, me.Name who, formation.JustFinished finishedNow;
                    }
                ")
                .WithParameters(p => { p["formationName", typeof(string)] = name; })
                .PerformCheckThenCommand());
            return answer.Ok ? answer : Answer.Refusal($"finish {name}: " + answer.Refused);
        }
        catch (Exception ex) { return Answer.Refusal($"finish {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>The clock's question (propuesta 117): is a finish this golem asked DUE — its part done (the last step walked, placed) and its word not
    /// counted yet? Then <c>Finish(me)</c> is written again, and this time it counts and tells the fleet.</summary>
    public void FinishIfDone()
    {
        try
        {
            var finished = Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Choreography.Finishing) Error 'no finish of this golem is due';
                ", @"
                    {
                        formation = g.Choreography.FinishingFormation;
                        me = formation.Fleet.Member(g);
                        route = formation.Finish(me);
                        print formation.Name 'called', formation.FinishedCount 'finished', formation.Fleet.Count 'of', formation.IsFinished 'done';
                        expose formation.Name call, formation.Stamp nonce, me.Name who, formation.JustFinished finishedNow;
                    }
                ")
                .PerformCheckThenCommand());
            if (finished.Ok) robot.Report(finished, "its part of the formation is done: it says it finished — the fleet is told");
        }
        catch { /* the clock asks again */ }
    }

    /// <summary>The clock's question (propuesta 117; Juan: "que el último en salir de la coreografía sea el que diga realmente a yellow"): did this
    /// copy count the fleet's LAST finish, with somebody to cue? Then it gives the cue — <c>formation.Cue()</c> — and echo-cue tells <c>YourTurn</c>
    /// with the cued golem's name to every peer; only the named one takes it, the first word standing.</summary>
    public void CueIfFinished()
    {
        try
        {
            var cued = Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Choreography.Cueing) Error 'no formation of this golem has a cue to give';
                ", @"
                    {
                        formation = g.Choreography.CueingFormation;
                        formation.Cue();
                        print formation.Name 'called', formation.CueTo 'cueTo';
                        expose formation.Name call, formation.Stamp nonce, formation.CueTo to;
                    }
                ")
                .PerformCheckThenCommand());
            if (cued.Ok) robot.Report(cued, "every member finished the formation — the last to leave cues the next formation by name: 'your turn' to every member of it");
        }
        catch { /* the clock asks again */ }
    }

    /// <summary>The clock's question (propuesta 117): was a formation this golem waits in cued before it was told here? Then the cue kept ahead takes it now:
    /// the same take the uptake of <c>YourTurn</c> writes.</summary>
    public void TakeIfCued()
    {
        try
        {
            var taken = Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Choreography.HasPendingCue) Error 'no cue waits for a formation of this golem';
                ", @"
                    {
                        formation = g.Choreography.CuedFormation;
                        fleet = formation.Fleet;
                        me = fleet.Member(g);
                        from = g.Destination;
                        formation.TakeCue(from, me);
                        if (formation.IsComplete) {
                            route = formation.Route;
                            if (g.Strategy.OnTheWay.IsActive) {
                                route = g.Dash(route);
                            }
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
                            print formation.StoodCount 'stood', fleet.Count 'of';
                        }
                        expose formation.Name call, formation.Stamp nonce, me.Name who, from.X stoodX, from.Y stoodY;
                    }
                ")
                .PerformCheckThenCommand());
            if (taken.Ok) robot.Report(taken, "its formation was cued before it was told here: it takes it now — the fleet hears its word");
        }
        catch { /* the clock asks again */ }
    }

    // THE STEP (ajuste 77; Juan: "que la flota tome la posición del otro en el sentido de las agujas del reloj y antihorario… se pueden
    // encolar"): a step is queued on the named formation — nothing moves here unless everybody already stands on its place, then the step
    // opens at once. The move is the FIGURE's (ajuste 84), of one ring or whole (propuesta 104). The act exposes what the peers need to queue
    // the same step (echo-rotate); the labels never a parameter's name.
    /// <summary>A STEP of the named formation (ajuste 77; propuesta 104): every body
    /// takes the next place in that sense, <c>clockwise</c> or <c>counterclockwise</c>, once everybody stands on its place; steps queue. The
    /// step's id, minted here, is the once of the tell that spreads it. The sense and the ring travel as the MEMBER'S NAME of the domain's
    /// closed sets (ajuste 90): declared <c>typeof(string)</c> because the script exposes them (the engine refuses to expose an Enum).</summary>
    public Answer Rotate(string name, string sense)
    {
        if (string.IsNullOrWhiteSpace(sense)) return Answer.Refusal("a step needs its sense: clockwise or counterclockwise");
        string turning = sense.Trim().ToLowerInvariant() switch { "clockwise" => "Clockwise", "counterclockwise" => "Counterclockwise", _ => null };
        if (turning == null) return Answer.Refusal($"a step turns clockwise or counterclockwise, not '{sense}'");
        string stepId = $"{robot.Name}-step-{DateTime.UtcNow.Ticks}";   // ticks, not milliseconds: two steps asked in one millisecond are two steps
        try
        {
            var answer = Answer.Of(robot.Actor.Using(@"
                    Check(g.KnowsWhereItStands) Error 'the golem does not know yet where its body stands: it wakes on its mark first';
                    Check(g.Choreography.HasFormation(@formationName)) Error 'the golem was told no formation by that name: form it first';
                ", @"
                    {
                        formation = g.Choreography.Find(@formationName);
                        move = formation.Figure.Rotate(@sense);
                        formation.Queue(move, @stepId);
                        if (formation.CanStep) {
                            route = formation.Step();
                            if (g.Strategy.OnTheWay.IsActive) {
                                route = g.Dash(route);
                            }
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
                            print formation.Queued 'queued';
                        }
                    }
                ")
                .WithParameters(p => {
                    p["formationName", typeof(string)] = name;
                    p["sense", typeof(string)] = turning;
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
            if (IsUnion(name))
            {
                // a union dissolved (propuesta 123): its formations with it; every peer is told the same name (UptakeDissolved)
                var gone = Answer.Of(robot.Actor.Using(
                    @"
                        Check(g.Choreography.HasUnion(@unionName)) Error 'the golem was told no union by that name';
                    ", @"
                        {
                            union = g.Choreography.FindUnion(@unionName);
                            g.Choreography.DissolveUnion(union);
                            print union.Name 'called', union.Dissolved 'gone';
                            expose union.Name call, union.Stamp nonce;
                        }
                    ")
                    .WithParameters(p => { p["unionName", typeof(string)] = name; })
                    .PerformCheckThenCommand());
                return gone.Ok ? gone : Answer.Refusal($"dissolve {name}: " + gone.Refused);
            }
            var answer = Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Choreography.HasFormation(@formationName)) Error 'the golem was told no formation by that name';
                ", @"
                    {
                        formation = g.Choreography.Find(@formationName);
                        g.Choreography.Dissolve(formation);
                        print formation.Name 'called', formation.Dissolved 'gone';
                        expose formation.Name call, formation.Stamp nonce;
                    }
                ")
                .WithParameters(p => { p["formationName", typeof(string)] = name; })
                .PerformCheckThenCommand());
            return answer.Ok ? answer : Answer.Refusal($"dissolve {name}: " + answer.Refused);
        }
        catch (Exception ex) { return Answer.Refusal($"dissolve {name}: " + GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>The body STOPPED for the route that yields its place and stands (propuesta 74): where it really stood — telemetry, like
    /// the hold — enters as `me`, and the convocation abandons that route and opens the one that replaces it from there —
    /// `route = muster.Halted(me)`. The script writes `route = g.Underway()` first, so `next-order-underway` pushes the new order.</summary>
    internal Answer Halted(Order was)
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

    // THE RETAKE (propuesta 113, 13-oct-2026): this golem's route to its place in the formation ended short — the formation opens it again from
    // where the body stands (telemetry, like the halt), once; a second failure leaves the formation stranded for this golem, said in its read.
    // Fired by whatever answer says the route ended short (an arrival whose correction found no road, a stuck body); nothing written otherwise.
    // `roundPlaced`, read BEFORE the word (lab 9-oct-2026): the once of the tell is the round the word is ABOUT — when this golem is the last
    // of the round and a step waits, Placed opens the step and Round is already the next one, so the key of "last in round 2" was the key of
    // "placed in round 3" and the engine kept the second tell back: the fleet waited for a word that never came, and only this golem stepped

    private Answer Retake()
    {
        var pose = robot.Pose;
        if (pose == null) return Answer.Refusal("no telemetry from the body: the retake needs where it stands");
        try
        {
            return Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Choreography.Current.NeedsRetake) Error 'no route of the formation in place ended short';
                ", @"
                    {
                        me = Pose(@px, @py, @ptheta);
                        formation = g.Choreography.Current;
                        route = formation.Retake(me);
                        if (g.Strategy.OnTheWay.IsActive) {
                            route = g.Dash(route);
                        }
                        print formation.Name 'called', formation.Stranded 'stranded';
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
                    p["px", typeof(double)] = Resolution.Metres(pose.X);
                    p["py", typeof(double)] = Resolution.Metres(pose.Y);
                    p["ptheta", typeof(double)] = Resolution.Radians(pose.Theta);
                })
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal(GolemEmbodiment.Reason(ex)); }
    }

    // a route ended short: the formation's retake, if it was this golem's route to its place (propuesta 113); null when nothing was written
    internal Answer? Retaken(Answer answer, Order was)
    {
        if (!answer.Ok || answer.Order is not { IsEnded: true, Ended: "failed" or "abandoned" }) return null;
        var retaken = Retake();
        if (!retaken.Ok) return null;
        bool stranded = false;
        try { stranded = JsonDocument.Parse(retaken.Print ?? "{}").RootElement.TryGetProperty("stranded", out var v) && v.ValueKind == JsonValueKind.True; } catch { }
        robot.Report(retaken, stranded ? $"route {was.Route} ended short twice: the formation is stranded for this golem — take it again, move it or dissolve it"
                                       : $"route {was.Route} ended short: its place is taken again from where the body stands");
        return retaken;
    }

    // THE LINE-UP (propuesta 106): this golem's body turned to face its next place in a step and stands; `Aligned(me)` records it, and with the
    // last mover's word the route goes on (the print turns from stop to advance). The expose carries what the tell needs; `roundPlaced`, read
    // before the act, like the placed word's.
    internal Answer Aligned()
    {
        try
        {
            return Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Choreography.Current.AwaitsMyWord) Error 'the golem is not waiting, lined up, for the fleet to start';
                ", @"
                    {
                        formation = g.Choreography.Current;
                        me = formation.Me;
                        roundPlaced = formation.Round;
                        route = formation.Aligned(me);
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
                        expose me.Name who, formation.Name call, formation.Stamp nonce, roundPlaced round;
                    }
                ")
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal(GolemEmbodiment.Reason(ex)); }
    }

    /// <summary>The clock's question (13-oct-2026): does this golem stand on its place of the formation in place with nobody told yet? A route to
    /// a place can complete AT BIRTH (the body on its place and facing it already) inside the uptake of a peer's word that closed the round, and
    /// then no arrival says it — so the clock asks the domain, and only its yes (the same Check as an arrival's) says Placed.</summary>
    public void PlacedIfStanding()
    {
        try
        {
            var placed = Placed();
            if (placed.Ok) robot.Report(placed, "on its place in the formation with nothing to walk — the fleet is told");
        }
        catch { /* the clock asks again */ }
    }

    /// <summary>The clock's other question (19-oct-2026): did this golem's route to its place end short with no arrival to say so? A route to a
    /// place is BORN FAILED inside the uptake of a peer's word that closed the round when no road fits past the marks and the berths
    /// (<c>Golem.FailToTake</c>; lab 14-oct: the march by rps, orange without a route and the fleet waiting for ever) — no arrival and no stuck
    /// follow it, so the clock asks the domain, and only its yes (the same Check as an arrival's) takes the place again from where the body
    /// stands, once; the second refusal strands the formation for this golem, said in its read.</summary>
    public void RetakeIfEndedShort()
    {
        try
        {
            var retaken = Retake();
            if (!retaken.Ok) return;
            bool stranded = false;
            try { stranded = JsonDocument.Parse(retaken.Print ?? "{}").RootElement.TryGetProperty("stranded", out var v) && v.ValueKind == JsonValueKind.True; } catch { }
            robot.Report(retaken, stranded ? "the route to its place could not be opened twice: the formation is stranded for this golem — take it again, move it or dissolve it"
                                           : "the route to its place ended short with no arrival to say it: its place is taken again from where the body stands");
        }
        catch { /* the clock asks again */ }
    }

    // THE GOLEM STANDS ON ITS PLACE (ajuste 77): the route to its place in the formation in place completed — the formation records its
    // word and, when with it everybody is placed and a step is queued, opens the next step; the act exposes who and which call, so
    // echo-placed tells every peer (the stamp in the once). No parameter: the formation in place is the domain's to know (`g.Choreography.Current`), and the Check
    // refuses when the golem does not stand on its place, or stands in no formation — nothing is written then.
    internal Answer Placed()
    {
        try
        {
            return Answer.Of(robot.Actor.Using(
                @"
                    Check(g.Choreography.Current.Reached) Error 'the golem does not stand on a place of the formation in place';
                ", @"
                    {
                        formation = g.Choreography.Current;
                        me = formation.Me;
                        roundPlaced = formation.Round;
                        route = formation.Placed(me);
                        if (g.Strategy.OnTheWay.IsActive) {
                            route = g.Dash(route);
                        }
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
                        expose me.Name who, formation.Name call, formation.Stamp nonce, roundPlaced round;
                    }
                ")
                .PerformCheckThenCommand());
        }
        catch (Exception ex) { return Answer.Refusal(GolemEmbodiment.Reason(ex)); }
    }

    // THE DISPUTES FORGOTTEN (14-oct-2026): the mind's own act, how many it forgot printed
    /// <summary>THE DISPUTES FORGOTTEN (14-oct-2026; Juan: "algún botón para limpiar las disputas"): every dispute this golem had, in every formation,
    /// goes; the next ones come with the next rounds played. Written, so a golem reborn forgets them too.</summary>
    public Answer ForgetDisputes()
    {
        try { return Answer.Of(robot.Actor.Using(@"
                    {
                        forgotten = g.Choreography.Disputes().Count;
                        g.Choreography.ForgetDisputes();
                        print forgotten 'forgotten';
                    }
                ").PerformCommand()); }
        catch (Exception ex) { return Answer.Refusal("forget disputes: " + GolemEmbodiment.Reason(ex)); }
    }

    // ==================================================================
    // THE WORDS OF THE FLEET (GolemSpeech binds them): what a peer's tell becomes in MY journal — plain @params only, the object built inside
    // ==================================================================
    // A PEER TOOK THE FORMATION and told me where it stood (ajustes 73, 80; propuesta 104): if I was told that formation and I am in its fleet —
    // already convened, its word may complete my round or take my place and interrupt my route; not yet, I keep its word and convene myself,
    // once, and my act tells everybody where I stand. The peer's position enters as @px @py: x and y are the print's labels.
    // A WORD OF A PROGRAMME'S ROUND (propuesta 123): the same tell, the call the union's name — kept in the union; with the round complete the law
    // shares everybody, and this golem's route in its formation opens (or that formation waits for its cue); a word about a union not told here
    // yet is kept for it. Then, as always, a formation's word.
    public const string UptakeStoodFor = @"
        {
            if (g.Choreography.HasUnion(@call)) {
                union = g.Choreography.FindUnion(@call);
                if (union.Awaits(@teller)) {
                    fleet = union.Candidates;
                    peer = fleet.Member(@teller);
                    at = Position(@px, @py);
                    union.Stood(peer, at, @nonce);
                    if (union.IsResolved) {
                        if (union.IsMember) {
                            mine = union.Mine;
                            print mine.Name 'mine', mine.HasRoute 'routed';
                            if (mine.HasRoute) {
                                route = mine.Route;
                                if (g.Strategy.OnTheWay.IsActive) {
                                    route = g.Dash(route);
                                }
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
                        } else {
                            print union.IsLeftOut 'leftOut';
                        }
                    } else {
                        print union.StoodCount 'stood', fleet.Count 'of', union.IsKnown 'known';
                    }
                } else {
                    said = Position(@px, @py);
                    g.Choreography.Overheard(@call, @teller, said, @nonce);
                }
            } else {
            if (g.Choreography.Awaits(@call, @teller)) {
                formation = g.Choreography.Find(@call);
                fleet = formation.Fleet;
                if (fleet.Has(g)) {
                    peer = fleet.Member(@teller);
                    at = Position(@px, @py);
                    me = fleet.Member(g);
                    if (formation.Knows(me)) {
                        formation.Stood(peer, at, @nonce);
                        if (formation.IsComplete) {
                            route = formation.Route;
                            if (g.Strategy.OnTheWay.IsActive) {
                                route = g.Dash(route);
                            }
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
                            print formation.StoodCount 'stood', fleet.Count 'of';
                        }
                    } else {
                        formation.Stood(peer, at, @nonce);
                        from = g.Destination;
                        formation.Convene(from, me);
                        if (formation.IsComplete) {
                            route = formation.Route;
                            if (g.Strategy.OnTheWay.IsActive) {
                                route = g.Dash(route);
                            }
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
                            print formation.StoodCount 'stood', fleet.Count 'of';
                        }
                        expose @call call, formation.Stamp nonce, me.Name who, from.X stoodX, from.Y stoodY;
                    }
                }
            } else {
                said = Position(@px, @py);
                g.Choreography.Overheard(@call, @teller, said, @nonce);
            }
            }
        }
        ";
    // A PEER STANDS ON ITS PLACE (ajuste 77): its word, recorded in the formation of that name — if this golem has its place in it; when with
    // it everybody is placed and a step is queued, the next step opens and its route is printed (next-order-heard pushes).
    public const string UptakePlacedAt = @"
        {
            if (g.Choreography.Hears(@call)) {
                formation = g.Choreography.Find(@call);
                peer = formation.Fleet.Member(@who);
                route = formation.Heard(peer);
                if (g.Strategy.OnTheWay.IsActive) {
                    route = g.Dash(route);
                }
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
        }
        ";
    // A PEER IS LINED UP for a round (propuesta 106; the tell carries the round): its word is recorded in that round; with the last mover's word
    // this copy lets its own route go and the print turns from stop to advance (next-order-heard-aligned pushes it). The word may come before
    // this copy reached that round: it is kept there.
    public const string UptakeAlignedAt = @"
        {
            if (g.Choreography.HasFormation(@call)) {
                formation = g.Choreography.Find(@call);
                peer = formation.Fleet.Member(@who);
                route = formation.HeardAligned(peer, @round);
                if (formation.HasRoute) {
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
                    print formation.AlignedCount 'aligned';
                }
            }
        }
        ";
    // A FORMATION DISSOLVED by a peer's warden (propuesta 104): mine goes too, a route of it pending let go (next-order-dissolve pushes the stop).
    public const string UptakeDissolved = @"
        {
            if (g.Choreography.HasUnion(@call)) {
                union = g.Choreography.FindUnion(@call);
                g.Choreography.DissolveUnion(union);
                print union.Name 'called';
            } else {
                if (g.Choreography.HasFormation(@call)) {
                    formation = g.Choreography.Find(@call);
                    g.Choreography.Dissolve(formation);
                    print formation.Name 'called';
                }
            }
        }
        ";
    // A PEER FINISHED ITS PART of a formation this golem is in (propuesta 117): its word counted in this copy; with every member's, this copy has the
    // cue to give (the clock asks). A word about a formation this golem was not told, or is not in, is nobody's here.
    public const string UptakeFinishedAt = @"
        {
            if (g.Choreography.Hears(@call)) {
                formation = g.Choreography.Find(@call);
                fleet = formation.Fleet;
                peer = fleet.Member(@who);
                formation.HeardFinished(peer);
                print formation.Name 'called', formation.FinishedCount 'finished', fleet.Count 'of', formation.IsFinished 'done';
            }
        }
        ";
    // YOUR TURN (propuesta 117; by the formation's name since propuesta 123): the last of a fleet that finished cues a FORMATION by name; every member
    // of it, waiting for the cue, takes it — TakeCue, the take by cue, which echo-stood and next-order-convene observe like a Convene, so the round of
    // words completes among them with no relay (ajuste 80). Heard before that formation was told here, the cue is kept for the clock (TakeIfCued) — only while NO formation of that name is told here (lab 21-oct-2026: a cue kept by a golem that was no member outlived the show);
    // a cue to a formation this golem is not in is nobody's here.
    // A QUESTION (propuesta 118, the pull; propuesta 123): somebody asks whether a FORMATION finished; its first by rank — the election by rank, no
    // golem named — keeps the question, and its clock answers from its own copy.
    public const string UptakeDidYouFinish = @"
        {
            if (g.Choreography.Answers(@about)) {
                g.Choreography.AskedAbout(@about, @asker);
                print @about 'called', @asker 'who';
            }
        }
        ";
    // AN ANSWER (propuesta 118, the pull): the golem asked says whether the formation finished; this golem — the asker, named — takes 'yes' as its
    // cue (TakeCue, the take by cue: its team hears its word, the inquiry desisted inside) and counts 'not yet' (the commitment asks again when due).
    public const string UptakeFinishedState = @"
        {
            if (g.Choreography.AwaitsAnswer(@asked, @about)) {
                formation = g.Choreography.FormationAfter(@about);
                if (@finished) {
                    fleet = formation.Fleet;
                    me = fleet.Member(g);
                    from = g.Destination;
                    formation.TakeCue(from, me);
                    if (formation.IsComplete) {
                        route = formation.Route;
                        if (g.Strategy.OnTheWay.IsActive) {
                            route = g.Dash(route);
                        }
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
                        print formation.StoodCount 'stood', fleet.Count 'of';
                    }
                    expose formation.Name call, formation.Stamp nonce, me.Name who, from.X stoodX, from.Y stoodY;
                } else {
                    formation.HeardNotYet();
                    print formation.Name 'called', formation.NotYet 'notYet';
                }
            }
        }
        ";
    public const string UptakeYourTurn = @"
        {
            if (g.Choreography.AwaitsCue(@to)) {
                formation = g.Choreography.Cued(@to);
                fleet = formation.Fleet;
                me = fleet.Member(g);
                from = g.Destination;
                formation.TakeCue(from, me);
                if (formation.IsComplete) {
                    route = formation.Route;
                    if (g.Strategy.OnTheWay.IsActive) {
                        route = g.Dash(route);
                    }
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
                    print formation.StoodCount 'stood', fleet.Count 'of';
                }
                expose formation.Name call, formation.Stamp nonce, me.Name who, from.X stoodX, from.Y stoodY;
            } else {
                if (g.Choreography.HasFormation(@to)) {
                    print @to 'called', false 'mine';
                } else {
                    g.Choreography.OverheardCue(@to);
                }
            }
        }
        ";

}
