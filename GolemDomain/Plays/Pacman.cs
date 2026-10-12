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
/// </summary>
internal sealed class Pacman
{
    private Route route;
    private Direction? wish;

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
    /// <summary>The route of the run underway, or of the last one.</summary>
    internal Route Route => route;

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

    internal void Ran(Route opened, Direction heading)
    {
        if (opened == null) throw new GolemDomainException("Pacman.Ran: 'opened' was not given");
        route = opened;
        Heading = heading;
        if (wish == heading) wish = null;   // the wish taken is consumed; one not open stays for the next decision
        State = PlayState.Running;
        Runs++;
    }

    internal void End()
    {
        State = PlayState.Over;
    }
}
