using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Routes;

namespace GolemDomain.Plays;

/// <summary>
/// THE PLAY OF PACMAN, as one golem's copy of it — a <see cref="Play"/> of the kind <c>pacman</c>, by its name (propuesta 108, fase 2, 12-oct-2026; Juan: "sería como enviarle que tome el tramo de
/// arriba/abajo/izquierda/derecha cuando entre en una intersección, si no seguir; cuando llegue a un camino sin salida… girar siempre a la
/// derecha"). Told to one golem and spread by tell, every member of the cast keeps the same play by the same name and stamp. What it keeps:
/// the cast, where it stands (<see cref="PlayState"/>), the direction pacman HEADS, the direction the keyboard WISHES (consumed at the next
/// decision), how many runs were made, and the route of the run underway. What it decides: THE NEXT RUN — not a route, the CELL the next route
/// goes to: pacman runs straight along the corridor to the next DECISION CELL (an intersection, three or more neighbours; or where straight
/// is closed, a corner or a dead end) and there the RULE picks the way on: the wish if open, else straight, else the right of the heading,
/// else the left, else back. The golem opens the route (<see cref="Dynamics.Run"/>), square (propuesta 110): one advance down the corridor.
/// THE GHOSTS (fase 3, 15-oct-2026): a ghost's copy keeps WHERE PACMAN WAS LAST SEEN — the decision cell every run of pacman's is told to
/// (<c>RanTo</c>), <see cref="Seen"/> — and CHASES it (<see cref="Dynamics.Chase"/>): a route to that cell, decided when the ghost is idle
/// (released, standing, or its last chase ended) and pacman was seen somewhere else; a word heard underway is kept and taken at the arrival.
/// The ghosts leave the den STAGGERED BY PACMAN'S RUNS, no word of their own (<see cref="IsReleased"/>): the k-th ghost of the cast's order
/// sets out when it has heard the run 1 + <see cref="RunsBetweenReleases"/>·k; the planner finds the way out of the den by itself (its cells
/// open north through two doors).
/// </summary>
internal sealed class Pacman : Play
{
    internal const string KindName = "pacman";

    /// <summary>How many runs of pacman's between one ghost's release and the next (fase 3): the first ghost leaves at run 1, the second at 3, the third at 5…</summary>
    internal const int RunsBetweenReleases = 2;

    private Direction? wish;
    private Position target;      // pacman's copy: the decision cell the run underway goes to — what RanTo tells
    private Position lastSeen;    // a ghost's copy: where pacman was last told to run
    private int runsSeen;         // …and which run that was
    private Position chasing;     // a ghost's copy: the cell its route underway (or its last) chases

    internal Pacman(string name, Cast cast, string stamp) : base(name, KindName, cast, stamp)
    {
        Heading = Direction.East;
    }

    /// <summary>How pacman is driven (ajuste 16-oct-2026): manual, autonomous or guided — guided at birth; <c>play.Drive(@steering)</c> changes it.</summary>
    internal Steering Steering { get; private set; } = Steering.Guided;
    /// <summary>The direction pacman heads along its corridor (east at birth, as it stands in the cradle).</summary>
    internal Direction Heading { get; private set; }
    /// <summary>Whether the keyboard asked a direction not yet consumed, and which.</summary>
    internal bool HasWish => wish != null;
    internal Direction Wish => wish ?? Heading;
    /// <summary>How many runs pacman made since released.</summary>
    internal int Runs { get; private set; }
    /// <summary>Pacman's copy: the decision cell its run underway goes to — the reads a tell carries (<c>expose play.TargetX cellX, …</c>).</summary>
    internal double TargetX => target == null ? throw new GolemDomainException("pacman has not run yet") : target.X;
    internal double TargetY => target == null ? throw new GolemDomainException("pacman has not run yet") : target.Y;

    // ---- a ghost's copy (fase 3) ----

    /// <summary>Whether pacman was heard running, and where to (the decision cell of its latest run told).</summary>
    internal bool HasSeen => lastSeen != null;
    internal Position LastSeen => lastSeen ?? throw new GolemDomainException("pacman has not been seen running yet");
    /// <summary>Which run of pacman's was the latest heard (0 before any).</summary>
    internal int RunsSeen => runsSeen;
    /// <summary>How many chases this ghost opened.</summary>
    internal int Chases { get; private set; }
    /// <summary>Whether this member STARTED (16-oct-2026; Juan: "desde el momento que lo liberamos… también se pone en los ghosts para que empiecen
    /// a buscarlo"): the warden's `start <name>` on its own tab — pacman's first run, a ghost's hunt (it chases from then on, as pacman's words
    /// release it).</summary>
    internal bool Started { get; private set; }

    /// <summary>THE START, this member's own — <c>play.Start()</c>: pacman sets out (the run is opened by the module), a ghost joins the hunt;
    /// said again, nothing. Refused once the play is over.</summary>
    internal void Start()
    {
        if (IsOver) throw new GolemDomainException($"the play '{Name}' is over");
        Started = true;
    }

    /// <summary>PACMAN WAS SEEN: a peer's word that pacman runs to that cell on its n-th run — <c>play.Seen(at, @runs)</c>. The latest word wins;
    /// a ghost's copy becomes Running with the first. Refused once the play is over.</summary>
    internal void Seen(Position cell, int runs)
    {
        if (cell == null) throw new GolemDomainException("Pacman.Seen: 'cell' was not given");
        if (runs < 1) throw new GolemDomainException("Pacman.Seen: a run is counted from 1");
        if (IsOver) throw new GolemDomainException($"the play '{Name}' is over");
        if (runs < runsSeen) return;   // an older word, arrived late: the latest run heard stands
        lastSeen = cell;
        runsSeen = runs;
        if (State == PlayState.Staged) State = PlayState.Running;
    }

    /// <summary>Whether that ghost has LEFT THE DEN, or may: the k-th ghost of the cast's order is released once it heard pacman's run
    /// 1 + RunsBetweenReleases·k — the staggered exit, by pacman's words alone.</summary>
    internal bool IsReleased(Golem ghost)
    {
        if (ghost == null) throw new GolemDomainException("Pacman.IsReleased: 'ghost' was not given");
        return runsSeen >= 1 + RunsBetweenReleases * Cast.RankOf(ghost);
    }

    /// <summary>Whether that ghost should open a chase NOW: the play runs, the ghost is released, it stands idle (no route pending) and pacman
    /// was last seen somewhere other than where its last chase went (or that chase ended short) — and not where the ghost stands.</summary>
    internal bool WantsChase(Golem ghost)
    {
        if (ghost == null) throw new GolemDomainException("Pacman.WantsChase: 'ghost' was not given");
        if (!Started || !IsRunning || lastSeen == null || !IsReleased(ghost)) return false;
        if (route != null && route.IsPending()) return false;
        if (chasing != null && !route.EndedShort && chasing.DistanceTo(lastSeen) <= Route.WithinAHair) return false;
        return ghost.Standing == null || ghost.Standing.DistanceTo(lastSeen) > Route.WithinAHair;
    }

    // ---- what the module says into the play (internal to the repertoire: the journal speaks to Dynamics) ----

    internal void Chased(Route opened, Position toward)
    {
        if (opened == null) throw new GolemDomainException("Pacman.Chased: 'opened' was not given");
        if (toward == null) throw new GolemDomainException("Pacman.Chased: 'toward' was not given");
        route = opened;
        chasing = toward;
        Chases++;
        State = PlayState.Running;
    }

    /// <summary>The keyboard's word: the direction to take at the next decision cell — or, underway and guided, the one to steer to now
    /// (<see cref="SteerTarget"/>). Said again, the newest wins; straight on while running is nothing to wish. Refused when autonomous.</summary>
    internal void Head(Direction direction)
    {
        if (IsOver) throw new GolemDomainException($"the play '{Name}' is over");
        if (Steering == Steering.Autonomous) throw new GolemDomainException("pacman drives itself: the keyboard does not steer it (steer guided, or manual)");
        wish = IsRunning && route != null && route.IsPending() && direction == Heading ? null : direction;
    }

    /// <summary>HOW PACMAN IS DRIVEN from here on (ajuste 16-oct-2026): <c>play.Drive(@steering)</c> — manual, autonomous or guided.</summary>
    internal void Drive(Steering steering)
    {
        if (!Enum.IsDefined(steering)) throw new GolemDomainException($"pacman is driven manual, autonomous or guided, not '{steering}'");
        if (IsOver) throw new GolemDomainException($"the play '{Name}' is over");
        Steering = steering;
    }

    /// <summary>Whether this golem's run reached its end and the next is to be decided: the route underway completed (not short) — and, driven
    /// manual, a wish was given (without one pacman stands and waits for the keyboard).</summary>
    internal bool Reached => IsRunning && route != null && !route.IsPending() && !route.EndedShort && (Steering != Steering.Manual || wish != null);

    /// <summary>THE NEXT RUN, decided on the map from the cell pacman stands in: the way on by the rule, then straight DOWN THE CORRIDOR to
    /// where the run ends (<see cref="EndsRun"/>); answers that cell and the heading that reaches it. Refused when no way is open at all.</summary>
    internal (Zone Cell, Direction Heading) NextRun(MapLayout map, Zone cell)
    {
        if (map == null) throw new GolemDomainException("Pacman.NextRun: 'map' was not given");
        if (cell == null) throw new GolemDomainException("Pacman.NextRun: 'cell' was not given");
        var way = Decide(map, cell);
        var here = Neighbour(map, cell, way) ?? throw new GolemDomainException($"no way out of {cell.Name}: every side is closed");
        while (!EndsRun(map, here, way)) here = Neighbour(map, here, way);
        return (here, way);
    }

    /// <summary>WHERE A RUN ENDS (ajuste 16-oct-2026; Juan: "deberían seguir de largo"): where straight on is closed — a corner, a dead end —
    /// or at the intersection where the WISH opens (sideways; straight or back are no turn here); driven manual, at every decision cell, so the
    /// keyboard chooses (the way it was until then).</summary>
    internal bool EndsRun(MapLayout map, Zone cell, Direction heading)
    {
        if (map == null) throw new GolemDomainException("Pacman.EndsRun: 'map' was not given");
        if (cell == null) throw new GolemDomainException("Pacman.EndsRun: 'cell' was not given");
        if (Steering == Steering.Manual) return IsDecision(map, cell, heading);
        if (Neighbour(map, cell, heading) == null) return true;
        return wish is Direction wanted && wanted != heading && wanted != Back(heading) && Neighbour(map, cell, wanted) != null;
    }

    /// <summary>WHERE TO STEER TO, underway (ajuste 16-oct-2026): from where the body is — the cell at that position — the wish's turn ahead in
    /// this corridor (the run shortened to the intersection where it opens, the same heading), or the way back when the wish is the opposite
    /// direction (the run turned round, as far as it goes); null when the wish opens nowhere ahead (kept for the next decision), or there is
    /// no wish, or the wish is straight on.</summary>
    internal (Zone Cell, Direction Heading)? SteerTarget(MapLayout map, Position at)
    {
        if (map == null) throw new GolemDomainException("Pacman.SteerTarget: 'map' was not given");
        if (at == null) throw new GolemDomainException("Pacman.SteerTarget: 'at' was not given");
        return wish is Direction wanted ? SteerTarget(map, at, wanted) : null;
    }

    /// <summary>The same, for a wish not yet said (the host asks before saying it).</summary>
    internal (Zone Cell, Direction Heading)? SteerTarget(MapLayout map, Position at, Direction wanted)
    {
        if (map == null) throw new GolemDomainException("Pacman.SteerTarget: 'map' was not given");
        if (at == null) throw new GolemDomainException("Pacman.SteerTarget: 'at' was not given");
        if (wanted == Heading) return null;
        var cell = map.ZoneAt(at);
        if (cell == null) return null;
        if (wanted == Back(Heading))
        {
            var back = Neighbour(map, cell, wanted);
            if (back == null) return null;
            var there = back;
            while (!EndsRun(map, there, wanted)) there = Neighbour(map, there, wanted);
            return (there, wanted);
        }
        var here = cell;
        while (here != null)
        {
            if (Neighbour(map, here, wanted) != null) return (here, Heading);
            here = Neighbour(map, here, Heading);
        }
        return null;
    }

    /// <summary>THE RULE at a cell: the wish if that side is open, else straight on, else to the right of the heading, else to the left,
    /// else back the way it came (the only way out of a dead end).</summary>
    internal Direction Decide(MapLayout map, Zone cell)
    {
        if (map == null) throw new GolemDomainException("Pacman.Decide: 'map' was not given");
        if (cell == null) throw new GolemDomainException("Pacman.Decide: 'cell' was not given");
        if (wish is Direction wanted && Neighbour(map, cell, wanted) != null) return wanted;
        foreach (var candidate in new[] { Heading, Right(Heading), Left(Heading), Back(Heading) })
            if (Neighbour(map, cell, candidate) != null) return candidate;
        throw new GolemDomainException($"no way out of {cell.Name}: every side is closed");
    }

    /// <summary>A decision cell on the way: an intersection (three or more neighbours), or one where the way straight on is closed.</summary>
    internal static bool IsDecision(MapLayout map, Zone cell, Direction heading)
    {
        if (map == null) throw new GolemDomainException("Pacman.IsDecision: 'map' was not given");
        if (cell == null) throw new GolemDomainException("Pacman.IsDecision: 'cell' was not given");
        return map.Neighbours(cell).Count >= 3 || Neighbour(map, cell, heading) == null;
    }

    /// <summary>The neighbouring cell on that side, open to this one; null when that side is closed.</summary>
    internal static Zone Neighbour(MapLayout map, Zone cell, Direction side)
    {
        if (map == null) throw new GolemDomainException("Pacman.Neighbour: 'map' was not given");
        if (cell == null) throw new GolemDomainException("Pacman.Neighbour: 'cell' was not given");
        var c = cell.Center;
        foreach (var area in map.Neighbours(cell))
        {
            if (area is not Zone z) continue;
            double dx = z.Center.X - c.X, dy = z.Center.Y - c.Y;
            bool there = side switch
            {
                Direction.East => dx > 1e-6 && Math.Abs(dy) < 1e-6,
                Direction.West => dx < -1e-6 && Math.Abs(dy) < 1e-6,
                Direction.North => dy > 1e-6 && Math.Abs(dx) < 1e-6,
                _ => dy < -1e-6 && Math.Abs(dx) < 1e-6,
            };
            if (there) return z;
        }
        return null;
    }

    internal static Direction Right(Direction d) => d switch { Direction.North => Direction.East, Direction.East => Direction.South, Direction.South => Direction.West, _ => Direction.North };
    internal static Direction Left(Direction d) => d switch { Direction.North => Direction.West, Direction.West => Direction.South, Direction.South => Direction.East, _ => Direction.North };
    internal static Direction Back(Direction d) => d switch { Direction.North => Direction.South, Direction.South => Direction.North, Direction.East => Direction.West, _ => Direction.East };

    // ---- what the module says into the play (internal to the repertoire: the journal speaks to Dynamics) ----

    internal void Ran(Route opened, Direction heading, Position toward)
    {
        if (opened == null) throw new GolemDomainException("Pacman.Ran: 'opened' was not given");
        if (toward == null) throw new GolemDomainException("Pacman.Ran: 'toward' was not given");
        route = opened;
        target = toward;
        Heading = heading;
        if (wish == heading) wish = null;   // the wish taken is consumed; one not open stays for the next decision
        State = PlayState.Running;
        Runs++;
    }
}
