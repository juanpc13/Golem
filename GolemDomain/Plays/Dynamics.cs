using GolemDomain.Geometry;
using GolemDomain.Routes;

namespace GolemDomain.Plays;

/// <summary>
/// THE GOLEM'S DYNAMICS (propuesta 108, 12-oct-2026; Juan: "un módulo nuevo para el golem… de dinámicas, no tanto formaciones o
/// coreografías"): the plays it is told and takes part in — born with its golem like <see cref="Formations.Choreographies"/>, it asks the
/// golem for every route (no delegates, ajuste 78). The first play is <see cref="Pacman"/>: <c>play = g.Dynamics.Stage(@title, cast, @stamp)</c>
/// stages it (told to one golem, spread by tell, every member of the cast stages the same), <c>play.Head(@direction)</c> is the keyboard's
/// wish, <c>route = g.Dynamics.Run(from, play)</c> releases pacman — and, every time a run reaches its decision cell, opens the next by the
/// rule. The ghosts (fase 3): every run is told (<c>RanTo</c>), a ghost's copy keeps where pacman was seen (<c>play.Seen(at, @runs)</c>)
/// and, released by pacman's runs, <c>route = g.Dynamics.Chase(from, play)</c> goes there. The module is in the namespace <c>Plays</c>: a
/// class may not share its name with a namespace.
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
        play.Ran(route, heading, target.Center);
        return route;
    }

    // ---- the ghosts (fase 3, 15-oct-2026) ----

    /// <summary>Whether this golem is in THAT play — by its title and the stamp of its call, the same in every copy: what an uptake asks before
    /// taking a word of the play (<c>if (g.Dynamics.Plays(@title, @nonce)) { … }</c>).</summary>
    internal bool Plays(string name, string stamp)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("Dynamics.Plays: 'name' was not given");
        if (string.IsNullOrWhiteSpace(stamp)) throw new GolemDomainException("Dynamics.Plays: 'stamp' was not given");
        return current != null && current.Name == name.Trim().ToLowerInvariant() && current.Stamp == stamp;
    }

    /// <summary>Whether this golem, a ghost, has left the den or may: released by pacman's runs (the staggered exit).</summary>
    internal bool Released => current != null && current.Cast.PartOf(golem) == Part.Ghost && current.IsReleased(golem);

    /// <summary>Whether this golem, a ghost, should open a chase now: released, idle, and pacman seen somewhere new — the host asks it at an
    /// arrival and the uptake of pacman's word asks it before chasing.</summary>
    internal bool MayChase => current != null && current.Cast.PartOf(golem) == Part.Ghost && current.WantsChase(golem);

    /// <summary>A GHOST CHASES: from where it stands, the route to the cell pacman was last seen running to — the golem opens it like a run
    /// (square, no facing, no berths); the first chase is the way out of the den. Refused for pacman, before release, with a chase underway,
    /// or when pacman was not seen since the last chase.</summary>
    internal Route Chase(Position from, Pacman play)
    {
        if (from == null) throw new GolemDomainException("Dynamics.Chase: 'from' was not given");
        if (play == null) throw new GolemDomainException("Dynamics.Chase: 'play' was not given");
        if (!ReferenceEquals(play, current)) throw new GolemDomainException($"the play '{play.Name}' is not the one staged");
        if (play.Cast.PartOf(golem) != Part.Ghost) throw new GolemDomainException($"'{golem.Name}' plays pacman: only a ghost chases");
        if (play.IsOver) throw new GolemDomainException($"the play '{play.Name}' is over");
        if (!play.HasSeen) throw new GolemDomainException("pacman has not been seen running yet: nothing to chase");
        if (!play.IsReleased(golem)) throw new GolemDomainException($"'{golem.Name}' is still in the den: it leaves at pacman's run {1 + Pacman.RunsBetweenReleases * play.Cast.RankOf(golem)}, {play.RunsSeen} heard");
        if (play.Route != null && play.Route.IsPending()) throw new GolemDomainException("the ghost is already chasing: its route is underway");
        if (!play.WantsChase(golem)) throw new GolemDomainException("pacman was not seen anywhere new since the last chase");
        var seen = play.LastSeen;
        var route = golem.RunTo(from, seen, $"where pacman was seen ({Fmt(seen.X)}, {Fmt(seen.Y)})");
        play.Chased(route, seen);
        return route;
    }

    // ---- the law (fase 4, 15-oct-2026) ----

    /// <summary>A BODY MET (the golem concluded inside HearBump that a peer's touch landed on its body): if the play runs and the two are a
    /// ghost and pacman — whichever this golem is — the play is OVER, caught by the ghost; a ghost meeting a ghost, or a golem outside the cast,
    /// is an encounter and nothing more. Both sides conclude it, so a lost word does not keep the game alive.</summary>
    internal void Met(string who)
    {
        if (string.IsNullOrWhiteSpace(who)) throw new GolemDomainException("Dynamics.Met: 'who' was not given");
        if (current == null || !current.IsRunning || !current.Cast.Knows(who)) return;
        var mine = current.Cast.PartOf(golem);
        var theirs = current.Cast.PartOf(who);
        if (mine == theirs) return;
        current.Over(mine == Part.Ghost ? golem.Name : who.Trim().ToLowerInvariant());
    }

    /// <summary>Whether this copy concluded the end of the play and has not announced it yet: the clock asks, and writes <c>play.Announce()</c>.</summary>
    internal bool Concluded => current != null && current.Concluded;

    private static string Fmt(double d) => d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
