using GolemDomain.Routes;

namespace GolemDomain.Plays;

/// <summary>
/// A PLAY, as one golem's copy of it (propuesta 108; the identity and the list since 16-oct-2026 — Juan: "generar un id del juego de pacman;
/// crear la lista de las dinámicas de juegos de pacman u otros que agregamos a futuro"): the dynamics the golem is told, by NAME — the
/// identity across the fleet, like a formation's (<c>game-1</c>) — of a KIND (<c>pacman</c> today; a traffic light, a camera tomorrow),
/// with the STAMP of its call (the host's once, the same in every copy), its CAST, where it stands (<see cref="PlayState"/>), the route of
/// the run or chase underway, and THE LAW every play ends by: <see cref="Over"/> concluded here, <see cref="HeardOver"/> a peer's word,
/// <see cref="Announce"/> the clock's act that tells it. What each kind decides is the subclass's (<see cref="Pacman"/>).
/// </summary>
internal abstract class Play
{
    protected Route route;
    private bool concluded;   // this copy concluded the end and has to announce it
    private bool announced;

    protected Play(string name, string kind, Cast cast, string stamp)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("a play needs a name");
        if (string.IsNullOrWhiteSpace(kind)) throw new GolemDomainException("a play needs its kind");
        if (cast == null) throw new GolemDomainException("Play.Play: 'cast' was not given");
        if (string.IsNullOrWhiteSpace(stamp)) throw new GolemDomainException("a play needs the stamp of its call");
        Name = name.Trim().ToLowerInvariant();
        Kind = kind.Trim().ToLowerInvariant();
        Cast = cast;
        Stamp = stamp;
        State = PlayState.Staged;
    }

    /// <summary>The play's name — its identity across the fleet, the warden's (<c>game-1</c>).</summary>
    internal string Name { get; }
    /// <summary>What dynamics it is: <c>pacman</c>.</summary>
    internal string Kind { get; }
    /// <summary>The once of the call that staged it, the host's — every copy carries the same.</summary>
    internal string Stamp { get; }
    internal Cast Cast { get; }
    internal PlayState State { get; private protected set; }
    internal bool IsRunning => State == PlayState.Running;
    internal bool IsOver => State == PlayState.Over;
    /// <summary>The route of the run underway, or of the last one (pacman's run, a ghost's chase).</summary>
    internal Route Route => route;

    /// <summary>WHAT a golem is in this play: the cast says who leads and who follows; the play names it (pacman and ghosts; a light and cars —
    /// propuesta 125).</summary>
    internal virtual Part PartOf(Golem golem) => Cast.PartOf(golem);
    internal virtual Part PartOf(string name) => Cast.PartOf(name);

    // ---- the law (fase 4, 15-oct-2026; Juan: "cuando un fantasma toque al golem principal, debe resolver por tell qué fue de la dinámica:
    // deben decir que un golem fantasma tocó al golem pacman y terminó el juego") ----

    /// <summary>Who ended the play: the ghost that caught pacman (either side concludes it), or <c>warden</c>; empty while it runs.</summary>
    internal string Catcher { get; private set; } = "";
    /// <summary>Whether a member of the cast ended it — a ghost caught pacman (the warden's end is no catch).</summary>
    internal bool WasCaught => IsOver && Catcher != "" && Catcher != "warden" && Cast.Knows(Catcher);
    /// <summary>Whether this copy concluded the end and has not announced it yet — the clock asks, then writes <c>play.Announce()</c>.</summary>
    internal bool Concluded => concluded && !announced;

    /// <summary>THE PLAY IS OVER, concluded HERE: by the domain when the encounter the law speaks of was recognised (<see cref="Dynamics.Met"/>),
    /// or by the warden's line (<c>play.Over('warden')</c>). The route underway is abandoned — the body stops — and the end is to be announced
    /// (<see cref="Announce"/>). Over already: nothing more (the first word stands).</summary>
    internal void Over(string who)
    {
        if (string.IsNullOrWhiteSpace(who)) throw new GolemDomainException("Play.Over: 'who' was not given");
        if (IsOver) return;
        Catcher = who.Trim().ToLowerInvariant();
        State = PlayState.Over;
        concluded = true;
        Closing(Catcher == "warden" ? "the warden ended the play" : $"game over: {Catcher} caught {Cast.Pacman}");
    }

    /// <summary>THE WORD HEARD (fase 4): a peer announced the end — <c>play.HeardOver(@who)</c>; this copy closes the same way, nothing to
    /// announce in turn (one hop). Heard again, or after its own conclusion: nothing.</summary>
    internal void HeardOver(string who)
    {
        if (string.IsNullOrWhiteSpace(who)) throw new GolemDomainException("Play.HeardOver: 'who' was not given");
        if (IsOver) return;
        Catcher = who.Trim().ToLowerInvariant();
        State = PlayState.Over;
        announced = true;
        Closing(Catcher == "warden" ? "the warden ended the play" : $"game over: {Catcher} caught {Cast.Pacman}");
    }

    /// <summary>What the end does to the route underway: a chase or a run is abandoned (Pacman); a car finishes its journey (Traffic).</summary>
    protected virtual void Closing(string why)
    {
        if (route != null && route.IsPending()) route.Abandon(why);
    }

    /// <summary>THE END ANNOUNCED — <c>play.Announce()</c>, the host's act the clock writes once this copy concluded; its expose carries the
    /// play, its stamp and who ended it, and the reaction tells <c>GameOver</c> to every peer. Refused when there is nothing to announce.</summary>
    internal void Announce()
    {
        if (!concluded) throw new GolemDomainException($"the play '{Name}' has no end to announce here");
        if (announced) throw new GolemDomainException($"the end of '{Name}' was announced already");
        announced = true;
    }

    /// <summary>Ended by the module, told again under its name (the former copy gives way): over, nothing to announce.</summary>
    internal void End()
    {
        State = PlayState.Over;
        announced = true;
    }
}
