using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Routes;

namespace GolemDomain.Plays;

/// <summary>
/// THE PLAY OF PACMAN, as one golem's copy of it (propuesta 108, fase 2, 12-oct-2026; Juan: "sería como enviarle que tome el tramo de
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
internal sealed class Pacman
{
    /// <summary>How many runs of pacman's between one ghost's release and the next (fase 3): the first ghost leaves at run 1, the second at 3, the third at 5…</summary>
    internal const int RunsBetweenReleases = 2;

    private Route route;
    private Direction? wish;
    private Position target;      // pacman's copy: the decision cell the run underway goes to — what RanTo tells
    private Position lastSeen;    // a ghost's copy: where pacman was last told to run
    private int runsSeen;         // …and which run that was
    private Position chasing;     // a ghost's copy: the cell its route underway (or its last) chases

    internal Pacman(string name, Cast cast, string stamp)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("a play needs a name");
        if (cast == null) throw new GolemDomainException("Pacman.Pacman: 'cast' was not given");
        if (string.IsNullOrWhiteSpace(stamp)) throw new GolemDomainException("a play needs the stamp of its call");
        Name = name.Trim().ToLowerInvariant();
        Cast = cast;
        Stamp = stamp;
        State = PlayState.Staged;
        Heading = Direction.East;
    }

    internal string Name { get; }
    internal Cast Cast { get; }
    /// <summary>The once of the call that staged it, the host's — every copy carries the same.</summary>
    internal string Stamp { get; }
    internal PlayState State { get; private set; }
    /// <summary>The direction pacman heads along its corridor (east at birth, as it stands in the cradle).</summary>
    internal Direction Heading { get; private set; }
    /// <summary>Whether the keyboard asked a direction not yet consumed, and which.</summary>
    internal bool HasWish => wish != null;
    internal Direction Wish => wish ?? Heading;
    /// <summary>How many runs pacman made since released.</summary>
    internal int Runs { get; private set; }
    internal bool IsRunning => State == PlayState.Running;
    internal bool IsOver => State == PlayState.Over;
    /// <summary>The route of the run underway, or of the last one (pacman's run, a ghost's chase).</summary>
    internal Route Route => route;
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
        if (!IsRunning || lastSeen == null || !IsReleased(ghost)) return false;
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

    /// <summary>The keyboard's word: the direction to take at the next decision cell. Said again, the newest wins.</summary>
    internal void Head(Direction direction)
    {
        if (IsOver) throw new GolemDomainException($"the play '{Name}' is over");
        wish = direction;
    }

    /// <summary>Whether this golem's run reached its decision cell: the route underway completed (not short), so the next run is to be decided.</summary>
    internal bool Reached => IsRunning && route != null && !route.IsPending() && !route.EndedShort;

    /// <summary>THE NEXT RUN, decided on the map from the cell pacman stands in: the way on by the rule, then straight to the next decision
    /// cell; answers that cell and the heading that reaches it. Refused when no way is open at all.</summary>
    internal (Zone Cell, Direction Heading) NextRun(MapLayout map, Zone cell)
    {
        if (map == null) throw new GolemDomainException("Pacman.NextRun: 'map' was not given");
        if (cell == null) throw new GolemDomainException("Pacman.NextRun: 'cell' was not given");
        var way = Decide(map, cell);
        var here = Neighbour(map, cell, way) ?? throw new GolemDomainException($"no way out of {cell.Name}: every side is closed");
        while (!IsDecision(map, here, way)) here = Neighbour(map, here, way);
        return (here, way);
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

    internal void End()
    {
        State = PlayState.Over;
    }

    // ---- the law (fase 4, 15-oct-2026; Juan: "cuando un fantasma toque al golem principal, debe resolver por tell qué fue de la dinámica:
    // deben decir que un golem fantasma tocó al golem pacman y terminó el juego") ----

    private bool concluded;   // this copy concluded the end (a ghost and pacman met, or the warden said so) and has to announce it
    private bool announced;

    /// <summary>Who ended the play: the ghost that caught pacman (either side concludes it), or <c>warden</c>; empty while it runs.</summary>
    internal string Catcher { get; private set; } = "";
    /// <summary>Whether a ghost caught pacman (the warden's end is no catch).</summary>
    internal bool WasCaught => IsOver && Catcher != "" && Catcher != "warden" && Cast.Knows(Catcher);
    /// <summary>Whether this copy concluded the end and has not announced it yet — the clock asks, then writes <c>play.Announce()</c>.</summary>
    internal bool Concluded => concluded && !announced;

    /// <summary>THE PLAY IS OVER, concluded HERE: by the domain when a ghost's touch and pacman's body met (<see cref="Dynamics.Met"/>), or by
    /// the warden's line (<c>play.Over('warden')</c>). The route of the run or chase underway is abandoned — the body stops — and the end is
    /// to be announced (<see cref="Announce"/>). Over already: nothing more (the first word stands).</summary>
    internal void Over(string who)
    {
        if (string.IsNullOrWhiteSpace(who)) throw new GolemDomainException("Pacman.Over: 'who' was not given");
        if (IsOver) return;
        Catcher = who.Trim().ToLowerInvariant();
        State = PlayState.Over;
        concluded = true;
        if (route != null && route.IsPending()) route.Abandon(Catcher == "warden" ? "the warden ended the play" : $"game over: {Catcher} caught {Cast.Pacman}");
    }

    /// <summary>THE WORD HEARD (fase 4): a peer announced the end — <c>play.HeardOver(@who)</c>; this copy closes the same way, nothing to
    /// announce in turn (one hop). Heard again, or after its own conclusion: nothing.</summary>
    internal void HeardOver(string who)
    {
        if (string.IsNullOrWhiteSpace(who)) throw new GolemDomainException("Pacman.HeardOver: 'who' was not given");
        if (IsOver) return;
        Catcher = who.Trim().ToLowerInvariant();
        State = PlayState.Over;
        announced = true;
        if (route != null && route.IsPending()) route.Abandon(Catcher == "warden" ? "the warden ended the play" : $"game over: {Catcher} caught {Cast.Pacman}");
    }

    /// <summary>THE END ANNOUNCED — <c>play.Announce()</c>, the host's act the clock writes once this copy concluded; its expose carries the
    /// play, its stamp and who ended it, and the reaction tells <c>GameOver</c> to every peer. Refused when there is nothing to announce.</summary>
    internal void Announce()
    {
        if (!concluded) throw new GolemDomainException($"the play '{Name}' has no end to announce here");
        if (announced) throw new GolemDomainException($"the end of '{Name}' was announced already");
        announced = true;
    }
}
