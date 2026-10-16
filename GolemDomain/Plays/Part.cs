namespace GolemDomain.Plays;

/// <summary>The PART a golem plays in a play (propuesta 108, fase 2, 12-oct-2026): pacman, or a ghost. A closed set of the domain (ajuste
/// 90: an enum travels as its member's name, the engine resolves it at the verb). Not the host's <c>Roles</c>, which are what a body can do.</summary>
internal enum Part
{
    Pacman,
    Ghost,
}

/// <summary>A DIRECTION in the maze, the four a corridor can run: what the keyboard says (<c>head north</c>) and the way pacman is heading.</summary>
internal enum Direction
{
    North,
    South,
    East,
    West,
}

/// <summary>HOW PACMAN IS DRIVEN (propuesta 108, ajuste 16-oct-2026; Juan: "por los botones del teclado… otra forma puede ser autónoma y la otra
/// autónoma + teclado para guiarle donde queremos que tome, pero siempre autónomo"): MANUAL — at a decision the keyboard alone decides, and
/// without a wish pacman stands and waits (every decision cell ends a run); AUTONOMOUS — the domain decides by its policy and never waits, the
/// keyboard does not steer it; GUIDED — the domain decides, but the wish rules where it opens, and pacman stays autonomous (the mode at birth).
/// A closed set of the domain, an enum like Sense (ajuste 90).</summary>
internal enum Steering
{
    Manual,
    Autonomous,
    Guided,
}

/// <summary>Where a play stands: STAGED (told, nobody released), RUNNING (pacman set out), OVER (a ghost caught pacman, or the warden ended it).</summary>
internal enum PlayState
{
    Staged,
    Running,
    Over,
}
