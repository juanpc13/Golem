using GolemDomain.Geometry;
using GolemDomain.Routes;

namespace GolemDomain.Plays;

/// <summary>
/// THE GOLEM'S DYNAMICS (propuesta 108, 12-oct-2026; Juan: "un módulo nuevo para el golem… de dinámicas, no tanto formaciones o
/// coreografías"): the plays it is told and takes part in — born with its golem like <see cref="Formations.Choreographies"/>, it asks the
/// golem for every route (no delegates, ajuste 78). The first play is <see cref="Pacman"/>: <c>play = g.Dynamics.Stage(@title, cast, @stamp)</c>
/// stages it (told to one golem, spread by tell, every member of the cast stages the same), <c>play.Head(@direction)</c> is the keyboard's
/// wish, <c>route = g.Dynamics.Run(from, play)</c> releases pacman — and, every time a run reaches its decision cell, opens the next by the
/// rule. The module is in the namespace <c>Plays</c>: a class may not share its name with a namespace.
/// </summary>
internal sealed class Dynamics
{
    private readonly Golem golem;
    private Pacman current;

    internal Dynamics(Golem golem)
    {
        this.golem = golem ?? throw new GolemDomainException("Dynamics.Dynamics: 'golem' was not given");
    }

    /// <summary>The play staged, if any — this golem's copy.</summary>
    internal Pacman Current => current;
    internal bool Knows => current != null;
    /// <summary>The part this golem plays in the play staged.</summary>
    internal Part Me => current == null ? throw new GolemDomainException("no play is staged") : current.Cast.PartOf(golem);

    /// <summary>A PLAY STAGED: by its name (only <c>pacman</c> so far), its cast and the stamp of the call (the host's once); this golem must
    /// be in the cast. Staged again under any name, the former play is over — unless a run of it is still underway.</summary>
    internal Pacman Stage(string name, Cast cast, string stamp)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("a play needs a name");
        if (cast == null) throw new GolemDomainException("Dynamics.Stage: 'cast' was not given");
        if (string.IsNullOrWhiteSpace(stamp)) throw new GolemDomainException("a play needs the stamp of its call");
        string title = name.Trim().ToLowerInvariant();
        if (title != "pacman") throw new GolemDomainException($"the only play known is pacman, not '{name}'");
        if (!cast.Has(golem)) throw new GolemDomainException($"'{golem.Name}' is not in the cast: pacman is {cast.Pacman}, the ghosts {cast.Roster}");
        if (current != null && current.Route != null && current.Route.IsPending()) throw new GolemDomainException($"a run of the play '{current.Name}' is still underway: reset it first");
        current?.End();
        current = new Pacman(title, cast, stamp);
        return current;
    }

    /// <summary>A play found by its name, the one place the name enters after Stage.</summary>
    internal Pacman Find(string name)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("Dynamics.Find: 'name' was not given");
        if (current == null || current.Name != name.Trim().ToLowerInvariant()) throw new GolemDomainException($"the golem was told no play named '{name}'");
        return current;
    }

    /// <summary>Whether pacman's run underway reached its decision cell: the host asks before writing the next run.</summary>
    internal bool Reached => current != null && current.Cast.PartOf(golem) == Part.Pacman && current.Reached;

    /// <summary>PACMAN RUNS (released, or arrived at a decision cell): from where it stands, the play decides the next decision cell by the
    /// rule and the golem opens the route there — square, one advance down the corridor, no facing. Only pacman runs; a ghost chases (fase 3).</summary>
    internal Route Run(Position from, Pacman play)
    {
        if (from == null) throw new GolemDomainException("Dynamics.Run: 'from' was not given");
        if (play == null) throw new GolemDomainException("Dynamics.Run: 'play' was not given");
        if (!ReferenceEquals(play, current)) throw new GolemDomainException($"the play '{play.Name}' is not the one staged");
        if (play.Cast.PartOf(golem) != Part.Pacman) throw new GolemDomainException($"'{golem.Name}' plays a ghost: only pacman runs");
        if (play.IsOver) throw new GolemDomainException($"the play '{play.Name}' is over");
        if (play.Route != null && play.Route.IsPending()) throw new GolemDomainException("pacman is already running: its run is underway");
        var map = golem.Current.Map;
        var cell = map.ZoneAt(from) ?? throw new GolemDomainException($"pacman stands nowhere on the maze: ({Fmt(from.X)}, {Fmt(from.Y)})");
        var (target, heading) = play.NextRun(map, cell);
        var route = golem.RunTo(from, target.Center, $"the decision cell {target.Name}");
        play.Ran(route, heading);
        return route;
    }

    private static string Fmt(double d) => d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
