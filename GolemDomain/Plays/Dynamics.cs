using GolemDomain.Geometry;
using GolemDomain.Routes;

namespace GolemDomain.Plays;

/// <summary>
/// THE GOLEM'S DYNAMICS (propuesta 108, 12-oct-2026; Juan: "un módulo nuevo para el golem… de dinámicas, no tanto formaciones o
/// coreografías"): the plays it is told and takes part in — born with its golem like <see cref="Formations.Choreographies"/>, it asks the
/// golem for every route (no delegates, ajuste 78). SINCE 16-oct-2026 IT KEEPS THE LIST (Juan: "generar un id del juego… la lista de las
/// dinámicas de juegos de pacman u otros"): every play told, BY NAME — the identity across the fleet, like a formation's — of a KIND
/// (<c>pacman</c> today), the ended ones among them (history); <c>play = g.Dynamics.Stage(@playName, @kind, cast, @stamp)</c> stages one
/// (told again under its name: made again, the former ended), <c>g.Dynamics.Current</c> is the latest told, <c>Find(@playName)</c> one by
/// name, <c>Plays()</c> all of them (the warden's read). The pacman verbs ask <see cref="Pacman"/> — the current play as pacman: the engine
/// binds a member by the declared type, and <c>Start</c>, <c>Seen</c> are not every play's. The module is in the
/// namespace <c>Plays</c>: a class may not share its name with a namespace.
/// </summary>
internal sealed class Dynamics
{
    private readonly Golem golem;
    private readonly List<Play> plays = new();   // every play told, by name, the ended ones among them (history)

    internal Dynamics(Golem golem)
    {
        this.golem = golem ?? throw new GolemDomainException("Dynamics.Dynamics: 'golem' was not given");
    }

    /// <summary>The play told last — this golem's copy; null before any.</summary>
    internal Play Current => plays.Count == 0 ? null : plays[^1];
    /// <summary>Whether a play is staged and not over.</summary>
    internal bool Knows => Current != null;
    /// <summary>The part this golem plays in the current play.</summary>
    internal Part Me => Current == null ? throw new GolemDomainException("no play is staged") : Current.PartOf(golem);
    /// <summary>EVERY PLAY TOLD, in the order told, the ended ones too: the warden's read (<c>plays</c>).</summary>
    internal IReadOnlyList<Play> Plays() => plays;

    /// <summary>THE CURRENT PLAY AS PACMAN — what the pacman verbs are written on (<c>play = g.Dynamics.Pacman; route = g.Dynamics.Run(me, play);</c>);
    /// refused when no play is staged or the current one is of another kind.</summary>
    internal Pacman Pacman => Current switch
    {
        null => throw new GolemDomainException("no play is staged"),
        Pacman pacman => pacman,
        var other => throw new GolemDomainException($"the play '{other.Name}' is {other.Kind}, not pacman"),
    };

    /// <summary>A PLAY STAGED: by its name (the warden's, <c>game-1</c>), its kind (only <c>pacman</c> so far), its cast and the stamp of the call
    /// (the host's once); this golem must be in the cast. Told again under a name known, the former play of that name is over (unless a run
    /// of it is still underway: reset it first) and the new one takes its place at the end of the list.</summary>
    internal Play Stage(string name, string kind, Cast cast, string stamp) => Stage(name, kind, cast, cast == null ? Part.Pacman : cast.PartOf(golem), stamp);

    internal Play Stage(string name, string kind, Cast cast, Part part, string stamp) => Staging(name, kind, cast, null, part, stamp);

    /// <summary>A TRAFFIC PLAY staged (propuesta 125): the cast is the light (the lead) and the cars; the journeys say every car's turn.</summary>
    internal Play Stage(string name, string kind, Cast cast, Journeys journeys, Part part, string stamp)
    {
        if (journeys == null) throw new GolemDomainException("Dynamics.Stage: 'journeys' was not given");
        return Staging(name, kind, cast, journeys, part, stamp);
    }

    /// <summary>THE SAME, WITH THE PART THE LINE SAYS (16-oct-2026; Juan: "en el comando play se le pasará qué rol tiene él dentro del juego, si es
    /// ghost o pacman"): the warden writes the play on every member's tab, each line naming that golem's part — pacman or ghost — and the cast
    /// must agree, else the line is refused (the cast is the one truth every copy computes alike).</summary>
    private Play Staging(string name, string kind, Cast cast, Journeys journeys, Part part, string stamp)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("a play needs a name");
        if (!Enum.IsDefined(part)) throw new GolemDomainException($"a golem plays pacman, a ghost, a car or the light, not '{part}'");
        if (string.IsNullOrWhiteSpace(kind)) throw new GolemDomainException("a play needs its kind: pacman or traffic");
        if (cast == null) throw new GolemDomainException("Dynamics.Stage: 'cast' was not given");
        if (string.IsNullOrWhiteSpace(stamp)) throw new GolemDomainException("a play needs the stamp of its call");
        string called = name.Trim().ToLowerInvariant();
        string what = kind.Trim().ToLowerInvariant();
        if (what != Pacman.KindName && what != Traffic.KindName) throw new GolemDomainException($"the plays known are pacman and traffic, not '{kind}'");
        if (what == Traffic.KindName && journeys == null) throw new GolemDomainException("a traffic play needs the journeys of its cars, like red:straight,green:left");
        if (what == Pacman.KindName && journeys != null) throw new GolemDomainException("a pacman play has no journeys");
        if (!cast.Has(golem)) throw new GolemDomainException(what == Traffic.KindName
            ? $"'{golem.Name}' is not in the cast: the light is {cast.Pacman}, the cars {cast.Roster}"
            : $"'{golem.Name}' is not in the cast: pacman is {cast.Pacman}, the ghosts {cast.Roster}");
        Play play = what == Traffic.KindName ? new Traffic(called, cast, journeys, stamp) : new Pacman(called, cast, stamp);
        if (play.PartOf(golem) != part) throw new GolemDomainException($"'{golem.Name}' is {play.PartOf(golem).ToString().ToLowerInvariant()} in the cast, not {part.ToString().ToLowerInvariant()}");
        var former = plays.FirstOrDefault(p => p.Name == called);
        if (former != null)
        {
            if (former.Route != null && former.Route.IsPending()) throw new GolemDomainException($"a run of the play '{called}' is still underway: reset it first");
            former.End();
            plays.Remove(former);
        }
        plays.Add(play);
        return play;
    }

    /// <summary>A play found by its name, the one place the name enters after Stage; refused when the golem was told none of that name.</summary>
    internal Play Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("Dynamics.Find: 'name' was not given");
        string called = name.Trim().ToLowerInvariant();
        return plays.FirstOrDefault(p => p.Name == called) ?? throw new GolemDomainException($"the golem was told no play named '{name}'");
    }

    /// <summary>A PLAY FORGOTTEN (16-oct-2026; Juan: "se deberían poder eliminar también"): taken off the list — history dropped — once it is over, or
    /// never set out (staged); one running is refused: <c>play over</c> first. The warden says it on every member's tab; nothing spreads.</summary>
    internal void Forget(Play play)
    {
        if (play == null) throw new GolemDomainException("Dynamics.Forget: 'play' was not given");
        if (!plays.Contains(play)) throw new GolemDomainException($"the golem was told no play named '{play.Name}'");
        if (play.IsRunning) throw new GolemDomainException($"the play '{play.Name}' is running: play over first");
        if (!play.IsOver) play.End();
        plays.Remove(play);
    }

    /// <summary>The play of that name as pacman — for a peer's word about it (<c>play = g.Dynamics.PacmanNamed(@title); play.Seen(…)</c>).</summary>
    internal Pacman PacmanNamed(string name) => Find(name) as Pacman ?? throw new GolemDomainException($"the play '{name}' is not pacman");

    /// <summary>Whether this golem was TOLD that play — by its name, the identity across the fleet (every copy's stamp is its own since the
    /// warden writes the play on every tab, 16-oct-2026): what an uptake asks before taking a word of the play
    /// (<c>if (g.Dynamics.Told(@title)) { … }</c>).</summary>
    internal bool Told(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("Dynamics.Told: 'name' was not given");
        string called = name.Trim().ToLowerInvariant();
        return plays.Any(p => p.Name == called);
    }

    // ---- pacman's runs (fase 2) ----

    private Pacman CurrentPacman => Current as Pacman;

    /// <summary>Whether this golem plays PACMAN in the current play — the start's script runs pacman and sets a ghost hunting.</summary>
    internal bool PlaysPacman => CurrentPacman is { } play && play.PartOf(golem) == Part.Pacman;

    /// <summary>Whether pacman's run underway reached its end: the host asks before writing the next run.</summary>
    internal bool Reached => CurrentPacman is { } play && play.Cast.PartOf(golem) == Part.Pacman && play.Reached;

    /// <summary>PACMAN RUNS (started, or arrived at the end of a run): from where it stands, the play decides the next cell by the rule and the
    /// golem opens the route there — square, one advance down the corridor, no facing. Only pacman runs; a ghost chases. Pacman is AUTONOMOUS
    /// (21-oct-2026): the rule alone decides, nothing steers it underway.</summary>
    internal Route Run(Position from, Pacman play)
    {
        if (from == null) throw new GolemDomainException("Dynamics.Run: 'from' was not given");
        if (play == null) throw new GolemDomainException("Dynamics.Run: 'play' was not given");
        if (!ReferenceEquals(play, Current)) throw new GolemDomainException($"the play '{play.Name}' is not the one staged");
        if (play.Cast.PartOf(golem) != Part.Pacman) throw new GolemDomainException($"'{golem.Name}' plays a ghost: only pacman runs");
        if (play.IsOver) throw new GolemDomainException($"the play '{play.Name}' is over");
        if (play.Route != null && play.Route.IsPending()) throw new GolemDomainException("pacman is already running: its run is underway");
        var map = golem.Current.Map;
        var cell = map.ZoneAt(from) ?? throw new GolemDomainException($"pacman stands nowhere on the maze: ({Fmt(from.X)}, {Fmt(from.Y)})");
        var (target, heading) = play.NextRun(map, cell);
        var route = golem.RunTo(from, target.Center, $"the decision cell {target.Name}");
        play.Ran(route, heading, target.Center);
        return route;
    }

    // ---- the ghosts (fase 3, 15-oct-2026) ----

    /// <summary>Whether this golem, a ghost, has left the den or may: released by pacman's runs (the staggered exit).</summary>
    internal bool Released => CurrentPacman is { } play && play.Cast.PartOf(golem) == Part.Ghost && play.IsReleased(golem);

    /// <summary>Whether this golem, a ghost, should open a chase now: released, idle, and pacman seen somewhere new — the host asks it at an
    /// arrival and the uptake of pacman's word asks it before chasing.</summary>
    internal bool MayChase => CurrentPacman is { } play && play.Cast.PartOf(golem) == Part.Ghost && play.WantsChase(golem);

    /// <summary>A GHOST CHASES: from where it stands, the route to the cell pacman was last seen running to — the golem opens it like a run
    /// (square, no facing, no berths); the first chase is the way out of the den. Refused for pacman, before release, with a chase underway,
    /// or when pacman was not seen since the last chase.</summary>
    internal Route Chase(Position from, Pacman play)
    {
        if (from == null) throw new GolemDomainException("Dynamics.Chase: 'from' was not given");
        if (play == null) throw new GolemDomainException("Dynamics.Chase: 'play' was not given");
        if (!ReferenceEquals(play, Current)) throw new GolemDomainException($"the play '{play.Name}' is not the one staged");
        if (play.Cast.PartOf(golem) != Part.Ghost) throw new GolemDomainException($"'{golem.Name}' plays pacman: only a ghost chases");
        if (play.IsOver) throw new GolemDomainException($"the play '{play.Name}' is over");
        if (!play.HasSeen) throw new GolemDomainException("pacman has not been seen running yet: nothing to chase");
        if (!play.Started) throw new GolemDomainException($"'{golem.Name}' has not started the hunt: start {play.Name} first");
        if (!play.IsReleased(golem)) throw new GolemDomainException($"'{golem.Name}' is still in the den: it leaves at pacman's run {1 + Pacman.RunsBetweenReleases * play.Cast.RankOf(golem)}, {play.RunsSeen} heard");
        if (play.Route != null && play.Route.IsPending()) throw new GolemDomainException("the ghost is already chasing: its route is underway");
        if (!play.WantsChase(golem)) throw new GolemDomainException("pacman was not seen anywhere new since the last chase");
        var seen = play.LastSeen;
        var route = golem.RunTo(from, seen, $"where pacman was seen ({Fmt(seen.X)}, {Fmt(seen.Y)})");
        play.Chased(route, seen);
        return route;
    }

    // ---- the law (fase 4, 15-oct-2026) ----

    /// <summary>A BODY MET (the golem concluded inside HearBump that a peer's touch landed on its body): if the current play runs and the two are
    /// a ghost and pacman — whichever this golem is — the play is OVER, caught by the ghost; a ghost meeting a ghost, or a golem outside the
    /// cast, is an encounter and nothing more. Both sides conclude it, so a lost word does not keep the game alive.</summary>
    internal void Met(string who)
    {
        if (string.IsNullOrWhiteSpace(who)) throw new GolemDomainException("Dynamics.Met: 'who' was not given");
        var play = Current;
        if (play == null || !play.IsRunning || !play.Cast.Knows(who)) return;
        var mine = play.Cast.PartOf(golem);
        var theirs = play.Cast.PartOf(who);
        if (mine == theirs) return;
        play.Over(mine == Part.Ghost ? golem.Name : who.Trim().ToLowerInvariant());
    }

    /// <summary>Whether this copy concluded the end of a play and has not announced it yet: the clock asks, and writes <c>play.Announce()</c>
    /// on <see cref="Concluding"/>.</summary>
    // ---- THE TRAFFIC PLAY (propuesta 125, 23-oct-2026): the car's drive and its two words, the light's law ----

    private Traffic CurrentTraffic => Current as Traffic;

    /// <summary>What kind of play the current one is — the host's scripts branch on these, never on a string.</summary>
    internal bool IsPacman => Current is Pacman;
    internal bool IsTraffic => Current is Traffic;

    /// <summary>The current play AS TRAFFIC — the traffic verbs are written on it; refused for another kind.</summary>
    internal Traffic Traffic => Current switch
    {
        null => throw new GolemDomainException("no play is staged"),
        Traffic t => t,
        var p => throw new GolemDomainException($"the play '{p.Name}' is {p.Kind}, not traffic"),
    };

    internal Traffic TrafficNamed(string name) => Find(name) as Traffic ?? throw new GolemDomainException($"the play '{name}' is not traffic");

    internal bool PlaysCar => CurrentTraffic is { } play && play.PartOf(golem) == Part.Car;
    internal bool PlaysLight => CurrentTraffic is { } play && play.PartOf(golem) == Part.Light;

    /// <summary>THE CAR'S DRIVE: one route, decided whole on the scenario the golem is in — the stop line of its arm (the gate), the exit
    /// point of the arm its turn leaves by, that arm's far end. The play keeps it; the host prints its first order.</summary>
    internal Route Drive(Position from, Traffic play)
    {
        if (from == null) throw new GolemDomainException("Dynamics.Drive: 'from' was not given");
        if (play == null) throw new GolemDomainException("Dynamics.Drive: 'play' was not given");
        if (!plays.Contains(play)) throw new GolemDomainException($"the golem was told no play named '{play.Name}'");
        if (play.PartOf(golem) != Part.Car) throw new GolemDomainException($"'{golem.Name}' is the light: only a car drives");
        if (!play.IsRunning) throw new GolemDomainException($"the play '{play.Name}' is {play.State.ToString().ToLowerInvariant()}: start it first");
        if (play.Route != null && play.Route.IsPending()) throw new GolemDomainException($"'{golem.Name}' is driving already");
        var crossing = Crossing.In(golem.Current.Map);
        var stops = play.Plan(crossing, from, golem.Name);
        var route = golem.DriveTo(from, stops, stops[0], "the car's journey");
        if (!play.IsFirst) route.StartTogether();   // §12: a car behind lines up and waits for the one ahead to be at its place
        play.Driving(route, golem.Name);
        return route;
    }

    /// <summary>The car behind, lined up, may set out: the one ahead said it stopped at its place (§12).</summary>
    internal bool MaySetOut => CurrentTraffic is { } play && play.PartOf(golem) == Part.Car && play.MaySetOut(golem.Name);

    /// <summary>THE CAR SETS OUT (§12): the one ahead is at its place — the route waiting at its start goes on to this car's place.</summary>
    internal Route SetOut(Traffic play)
    {
        if (play == null) throw new GolemDomainException("Dynamics.SetOut: 'play' was not given");
        if (play.PartOf(golem) != Part.Car) throw new GolemDomainException($"'{golem.Name}' is the light: only a car sets out");
        if (!play.MaySetOut(golem.Name)) throw new GolemDomainException($"'{golem.Name}' may not set out: {(play.WaitsToSetOut ? "the one ahead is not at its place" : "it is not waiting to set out")}");
        return play.Route.Go();
    }

    /// <summary>The queue moved ahead of this car (§12): a car ahead on its arm got the green — it advances to the nearer place.</summary>
    internal bool MayAdvance => CurrentTraffic is { } play && play.PartOf(golem) == Part.Car && play.MayAdvance;

    /// <summary>THE CAR ADVANCES IN ITS QUEUE (§12): the route to its old place abandoned, a new one from where it stands to the nearer place —
    /// the gate there — through the crossing to its parking place; no word to anybody.</summary>
    internal Route Advance(Position from, Traffic play)
    {
        if (from == null) throw new GolemDomainException("Dynamics.Advance: 'from' was not given");
        if (play == null) throw new GolemDomainException("Dynamics.Advance: 'play' was not given");
        if (play.PartOf(golem) != Part.Car) throw new GolemDomainException($"'{golem.Name}' is the light: only a car queues");
        var stops = play.Advance(golem.Name);
        var route = golem.DriveTo(from, stops, stops[0], "the car's place in the queue");
        play.Driving(route, golem.Name);
        return route;
    }

    /// <summary>The car stands at its stop line and has not said so: the clock, or the arrival, writes its word.</summary>
    internal bool AtTheLine => CurrentTraffic is { } play && play.PartOf(golem) == Part.Car && play.AtTheLine;
    /// <summary>The car stands at a place of its queue and has not said so (§12): the arrival writes its word, with the place.</summary>
    internal bool AtItsPlace => CurrentTraffic is { } play && play.PartOf(golem) == Part.Car && play.AtItsPlace;

    /// <summary>THE CAR STOPPED at its place: its word, from where the body stands — the line's for the light, any place's for the car behind.</summary>
    internal Traffic Stopped(Pose me, Traffic play)
    {
        if (me == null) throw new GolemDomainException("Dynamics.Stopped: 'me' was not given");
        if (play == null) throw new GolemDomainException("Dynamics.Stopped: 'play' was not given");
        if (play.PartOf(golem) != Part.Car) throw new GolemDomainException($"'{golem.Name}' is the light: only a car stops at a line");
        play.Stop(me);
        play.Queued(golem.Name, play.Arm, play.Slot);
        return play;
    }

    /// <summary>The light may grant the crossing: this golem is the light, the play runs, the crossing is free and a car waits.</summary>
    internal bool MayGrant => CurrentTraffic is { } play && play.PartOf(golem) == Part.Light && play.MayGrant;

    /// <summary>THE LIGHT GRANTS the crossing to the first car that stopped: the permit, told to the cast.</summary>
    internal Permit Grant(Traffic play)
    {
        if (play == null) throw new GolemDomainException("Dynamics.Grant: 'play' was not given");
        if (play.PartOf(golem) != Part.Light) throw new GolemDomainException($"'{golem.Name}' is a car: only the light grants");
        return play.Grant();
    }

    /// <summary>This car holds the green and stands at its gate: the light's word opens it.</summary>
    internal bool HasGreen => CurrentTraffic is { } play && play.PartOf(golem) == Part.Car && play.HasGreen(golem.Name) && play.Route != null && play.Route.IsPending() && play.Route.Waiting;

    /// <summary>THE CAR GOES: on the light's word the route that waited at the line is left and THE WAY THROUGH opens from where the body
    /// stands — the exit point and the parking place as the greens' order gives it (§12).</summary>
    internal Route Go(Traffic play)
    {
        if (play == null) throw new GolemDomainException("Dynamics.Go: 'play' was not given");
        if (play.PartOf(golem) != Part.Car) throw new GolemDomainException($"'{golem.Name}' is the light: only a car goes");
        if (!play.HasGreen(golem.Name)) throw new GolemDomainException($"'{golem.Name}' has no green: the crossing is {(play.IsFree ? "free" : play.Holder + "'s")}");
        var from = play.Route.Standing;
        var stops = play.Through(golem.Name);
        var route = golem.DriveThrough(from, stops, "the way through the crossing");
        play.Driving(route, golem.Name);
        return route;
    }

    /// <summary>The car is past the crossing and has not said so.</summary>
    internal bool PastTheCrossing => CurrentTraffic is { } play && play.PartOf(golem) == Part.Car && play.PastTheCrossing(golem.Name);

    /// <summary>THE CAR CLEARED the crossing: its word, from where the body stands; the crossing is free in its copy.</summary>
    internal Traffic Cleared(Pose me, Traffic play)
    {
        if (me == null) throw new GolemDomainException("Dynamics.Cleared: 'me' was not given");
        if (play == null) throw new GolemDomainException("Dynamics.Cleared: 'play' was not given");
        if (play.PartOf(golem) != Part.Car) throw new GolemDomainException($"'{golem.Name}' is the light: only a car clears");
        play.Clear(golem.Name, me);
        return play;
    }

    internal bool Concluded => plays.Any(p => p.Concluded);

    /// <summary>The play whose end this copy concluded and has not announced — the one the clock announces.</summary>
    internal Play Concluding => plays.FirstOrDefault(p => p.Concluded) ?? throw new GolemDomainException("no end of a play to announce here");

    private static string Fmt(double d) => d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
