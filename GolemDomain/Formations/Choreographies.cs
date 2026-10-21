using GolemDomain.Geometry;
using GolemDomain.Routes;

namespace GolemDomain.Formations;

/// <summary>
/// The golem's CHOREOGRAPHIES module (ajuste 69, 30-sep-2026; Juan: "el módulo asociado a coreografías… g.coreografia.formacion('cuadrado'),
/// y ese creará el objeto de la variable formación, que puede ser cualquier forma al final"): born with the golem, like its strategies,
/// and reached as <c>g.Choreography</c>. Since propuesta 104 (8-oct-2026; Juan: "las formaciones le pertenecerán al golem… el CLI sólo
/// servirá como interfaz para administrar esas formaciones") it KEEPS THE FORMATIONS BY NAME — each a figure WITH its fleet, its policy and
/// the place every member holds — made here (<see cref="Form"/>, <see cref="FormPropeller"/>: told again, made again — moved, turned, resized),
/// found by name (<see cref="Find"/>, the one place the name enters after it is made), listed for the warden (<see cref="Formations"/>) and
/// dissolved (<see cref="Dissolve"/>). The formation takes the words (<c>formation.Convene(from, me)</c>, ajuste 81: the act on the object
/// that keeps it), gives the routes through the golem it was born with, and takes the fleet's steps. The concrete figures stay constructible
/// for the tests and for this module (<see cref="Figure(string, Position, Units.Length)"/>).
/// </summary>
internal sealed class Choreographies
{
    private static readonly string[] Known = { "square", "pentagon", "triangle", "circle", "propeller" };
    private readonly Golem golem;                        // the golem it was born with: it opens the route to a place (refused off the map or without room)
    private readonly List<Formation> formations = new();   // the formations the golem was told, by name, the dissolved ones among them (history)
    private readonly Dictionary<string, Dictionary<string, (Position At, string Nonce)>> overheard = new();   // words of a round heard AHEAD of the formation they are about: name → who → where
    private bool pendingCue;                                            // a cue to THIS golem heard before the formation it waits in was told (propuesta 117)
    private readonly Queue<(string Call, string Who)> questions = new();   // THE PULL (propuesta 118): who asked whether which formation finished, to be answered on the tick
    private int answers;                                                // how many answers this golem gave: the once of each

    // born with its golem (like the strategies' switches, ajuste 63): objects that know each other, no delegate (ajuste 78)
    internal Choreographies(Golem golem)
    {
        if (golem == null) throw new GolemDomainException("Choreographies.Choreographies: 'golem' was not given");
        this.golem = golem;
    }

    /// <summary>A figure by its name — <c>square</c>, <c>pentagon</c>, <c>triangle</c> or <c>circle</c>, any case — at that centre, with its natural
    /// measure: a polygon's SIDE (ajuste 65), a circle's RADIUS. A name that is none of them is refused; the propeller is made by
    /// <see cref="FormPropeller"/>, with its hub and its blades.</summary>
    internal Figure Figure(string name, Position center, Units.Length measure) => Figure(name, center, measure, new Units.Degrees(0.0));

    internal Figure Figure(string name, Position center, Units.Length measure, Units.Angle turn)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Figure: 'name' was not given");
        if (center == null) throw new GolemDomainException("Choreographies.Figure: 'center' was not given");
        if (measure == null) throw new GolemDomainException("Choreographies.Figure: 'measure' was not given");
        if (turn == null) throw new GolemDomainException("Choreographies.Figure: 'turn' was not given");
        return name.Trim().ToLowerInvariant() switch
        {
            "square" => new Square(center, measure, turn),
            "pentagon" => new Pentagon(center, measure, turn),
            "triangle" => new Triangle(center, measure, turn),
            "circle" => new Circle(center, measure, turn),
            "propeller" => throw new GolemDomainException("a propeller is said by its radius, its hub and its blades: FormPropeller"),
            _ => throw new GolemDomainException($"the golem knows no figure named '{name}': {string.Join(", ", Known)}")
        };
    }

    /// <summary>The figures the golem knows how to take, by name.</summary>
    internal IReadOnlyList<string> Names() => Known;

    /// <summary>THE FORMATION TOLD (propuesta 104) — <c>formation = g.Choreography.Form(@formationName, @figure, center, measure, turn, fleet, @by);</c>:
    /// made with its figure, its fleet and its <c>Policy</c> (the member's name, <c>@by</c>), and KEPT by its name; told again with the same name it
    /// is MADE AGAIN — moving, turning, resizing or re-crewing a formation is saying it again — the old one dissolved (a route already given
    /// keeps the figure it was given by). The same words reach every golem of the fleet, so every copy is the same.</summary>
    internal Formation Form(string name, string figure, Position center, Units.Length measure, Units.Angle turn, Fleet fleet, Policy by) => Form(name, figure, center, measure, turn, fleet, by, name);

    /// <summary>The same, with the STAMP the host minted — the once of the call, carried by every tell about the formation.</summary>
    internal Formation Form(string name, string figure, Position center, Units.Length measure, Units.Angle turn, Fleet fleet, Policy by, string stamp)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Form: 'name' was not given");
        if (figure == null) throw new GolemDomainException("Choreographies.Form: 'figure' was not given");
        if (center == null) throw new GolemDomainException("Choreographies.Form: 'center' was not given");
        if (measure == null) throw new GolemDomainException("Choreographies.Form: 'measure' was not given");
        if (turn == null) throw new GolemDomainException("Choreographies.Form: 'turn' was not given");
        if (fleet == null) throw new GolemDomainException("Choreographies.Form: 'fleet' was not given");
        return Keep(name, Figure(figure, center, measure, turn), fleet, by, stamp);
    }

    /// <summary>The same, with the figure in hand — the tests' and this module's way (methods take instances).</summary>
    internal Formation Form(string name, Figure figure, Fleet fleet, Policy by)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Form: 'name' was not given");
        if (figure == null) throw new GolemDomainException("Choreographies.Form: 'figure' was not given");
        if (fleet == null) throw new GolemDomainException("Choreographies.Form: 'fleet' was not given");
        return Keep(name, figure, fleet, by, name);
    }

    /// <summary>THE PROPELLER TOLD (propuesta 114) — <c>formation = g.Choreography.FormPropeller(@formationName, center, tip, hub, @blades, turn, fleet, @by, @stamp);</c>:
    /// its blades from the hub to the tip, the fleet along them; a step turns it one blade.</summary>
    internal Formation FormPropeller(string name, Position center, Units.Length radius, Units.Length hub, int blades, Units.Angle turn, Fleet fleet, Policy by) => FormPropeller(name, center, radius, hub, blades, turn, fleet, by, name);

    internal Formation FormPropeller(string name, Position center, Units.Length radius, Units.Length hub, int blades, Units.Angle turn, Fleet fleet, Policy by, string stamp)
    {
        if (name == null) throw new GolemDomainException("Choreographies.FormPropeller: 'name' was not given");
        if (center == null) throw new GolemDomainException("Choreographies.FormPropeller: 'center' was not given");
        if (radius == null) throw new GolemDomainException("Choreographies.FormPropeller: 'radius' was not given");
        if (hub == null) throw new GolemDomainException("Choreographies.FormPropeller: 'hub' was not given");
        if (turn == null) throw new GolemDomainException("Choreographies.FormPropeller: 'turn' was not given");
        if (fleet == null) throw new GolemDomainException("Choreographies.FormPropeller: 'fleet' was not given");
        return Keep(name, new Propeller(center, radius, hub, blades, turn), fleet, by, stamp);
    }

    private Formation Keep(string name, Figure figure, Fleet fleet, Policy by, string stamp)
    {
        string called = name.Trim().ToLowerInvariant();
        if (called == "") throw new GolemDomainException("a formation needs a name");
        var formation = new Formation(called, figure, fleet, golem, by, stamp);
        var known = formations.FirstOrDefault(f => f.Name == called && !f.Dissolved);
        known?.Dissolve();                                  // told again: the old one goes, the new one takes its name
        formations.Add(formation);
        // the words of its round heard before this golem was told it (13-oct-2026): they are this telling's, and taken in now
        if (overheard.Remove(called, out var words))
            foreach (var (who, word) in words)
                if (who != golem.Name && fleet.Names.Contains(who)) formation.Stood(fleet.Member(who), word.At, word.Nonce);
        return formation;
    }

    /// <summary>Whether a member's word about the formation of that name is for the copy this golem holds NOW (13-oct-2026, propuesta 112): the
    /// golem was told it, the teller is of its fleet and has not spoken in it yet. Every member says its word once per telling, so a word from a
    /// member already heard — or about a formation the golem was not told yet — is about the NEXT telling of that name: the warden writes the same
    /// `form` on every tab, and a golem whose line comes late (its `use map` carried the body first) hears the others' words before its own
    /// `form`. <c>if (g.Choreography.Awaits(@call, @teller)) { … } else { g.Choreography.Overheard(@call, @teller, at); }</c></summary>
    internal bool Awaits(string name, string teller)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Awaits: 'name' was not given");
        if (teller == null) throw new GolemDomainException("Choreographies.Awaits: 'teller' was not given");
        string called = name.Trim().ToLowerInvariant();
        var formation = formations.LastOrDefault(f => f.Name == called && !f.Dissolved);
        if (formation == null || !formation.Fleet.Names.Contains(teller)) return false;
        return !formation.Knows(formation.Fleet.Member(teller));
    }

    /// <summary>A WORD HEARD AHEAD (13-oct-2026; lab: red's `form` came two seconds after blue's and green's words of the round, which found no
    /// formation and were lost — red never completed its round, and blue and green waited for its line-up for ever): where a member said it stood,
    /// about a formation this golem was not told yet — or told again — KEPT by its name (the latest word of each member), and taken in by the
    /// <see cref="Form"/> of that name. A dissolution drops them.</summary>
    internal void Overheard(string name, string teller, Position at) => Overheard(name, teller, at, "");

    /// <summary>The same, with the stamp the word carried (propuesta 115: the teller's hand at the game).</summary>
    internal void Overheard(string name, string teller, Position at, string nonce)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Overheard: 'name' was not given");
        if (teller == null) throw new GolemDomainException("Choreographies.Overheard: 'teller' was not given");
        if (at == null) throw new GolemDomainException("Choreographies.Overheard: 'at' was not given");
        string called = name.Trim().ToLowerInvariant();
        if (!overheard.TryGetValue(called, out var words)) overheard[called] = words = new Dictionary<string, (Position, string)>();
        words[teller] = (new Position(at.X, at.Y), nonce ?? "");
    }

    // ---- THE CUE BETWEEN DANCERS (propuesta 117, 19-oct-2026): the word "continúas tú" names a golem; the named one knows which formation it waits in ----

    /// <summary>Whether THIS golem is the one named by a cue and has a formation that starts on cue, of its fleet, not taken yet — what the uptake of
    /// <c>YourTurn</c> asks: <c>if (g.Choreography.AwaitsCue(@to)) { formation = g.Choreography.CuedFormation; formation.Cued(); formation.Convene(from, me); … }</c>.</summary>
    internal bool AwaitsCue(string golemName)
    {
        if (golemName == null) throw new GolemDomainException("Choreographies.AwaitsCue: 'golemName' was not given");
        if (golem.Name == null || golem.Name != golemName.Trim().ToLowerInvariant()) return false;
        return formations.Any(WaitsForTheCue);
    }

    private bool WaitsForTheCue(Formation f) => !f.Dissolved && f.OnCue && !f.IsCued && f.Fleet.Has(golem) && !f.Knows(f.Fleet.Member(golem));

    /// <summary>The formation this golem waits in for the cue — the latest told, when several do. Taking it ends a cue kept ahead. Refused when none waits.</summary>
    internal Formation CuedFormation
    {
        get
        {
            var formation = formations.LastOrDefault(WaitsForTheCue) ?? throw new GolemDomainException("this golem waits in no formation for a cue");
            pendingCue = false;
            return formation;
        }
    }

    /// <summary>A CUE HEARD AHEAD (propuesta 117): the word named this golem and it waits in no formation yet (its `form … --on-cue` comes late, a slow
    /// `use map` on its tab): kept, and the clock takes it once the formation is told (<c>TakeIfCued</c>). A cue to another golem is nobody's here.</summary>
    internal void OverheardCue(string golemName)
    {
        if (golemName == null) throw new GolemDomainException("Choreographies.OverheardCue: 'golemName' was not given");
        if (golem.Name != null && golem.Name == golemName.Trim().ToLowerInvariant()) pendingCue = true;
    }

    /// <summary>Whether a cue kept ahead has its formation now: the clock's question.</summary>
    internal bool HasPendingCue => pendingCue && formations.Any(WaitsForTheCue);

    // ---- THE PULL (propuesta 118, 20-oct-2026; Juan: "que yellow esté pendiente de consultarle a alguno si ya terminó"): a question and an answer, two
    // tells; the asker's golem COMMITS ITSELF to ask every so often (propuesta 121: the inquiry in g.Commitments, kept by the attention when due),
    // the asked one's clock answers from its own copy ----

    /// <summary>Whether a word addressed to a golem by name is for THIS golem — the uptakes of the pull ask it first.</summary>
    internal bool AskedOf(string golemName)
    {
        if (golemName == null) throw new GolemDomainException("Choreographies.AskedOf: 'golemName' was not given");
        return golem.Name != null && golem.Name == golemName.Trim().ToLowerInvariant();
    }

    /// <summary>A QUESTION HEARD (by tell, <c>DidYouFinish</c>): who asked whether which formation finished — kept until this golem's clock answers
    /// (<see cref="Answer"/>): <c>g.Choreography.AskedAbout(@about, @asker);</c>.</summary>
    internal void AskedAbout(string formationName, string who)
    {
        if (formationName == null) throw new GolemDomainException("Choreographies.AskedAbout: 'formationName' was not given");
        if (who == null) throw new GolemDomainException("Choreographies.AskedAbout: 'who' was not given");
        questions.Enqueue((formationName.Trim().ToLowerInvariant(), who.Trim().ToLowerInvariant()));
    }

    /// <summary>Whether a question waits for this golem's answer: the clock's question (<c>AnswerIfAsked</c>).</summary>
    internal bool Answering => questions.Count > 0;

    /// <summary>THE ANSWER (propuesta 118): the oldest question answered from this copy — <c>response = g.Choreography.Answer();</c>: the formation
    /// finished as this golem knows it (told, not dissolved, every member finished), or not; the reaction on this act tells <c>FinishedState</c>
    /// to the one who asked. Refused when nobody asked.</summary>
    internal Response Answer()
    {
        if (questions.Count == 0) throw new GolemDomainException("nobody asked this golem anything");
        var (call, who) = questions.Dequeue();
        answers++;
        bool done = formations.Any(f => f.Name == call && !f.Dissolved && f.IsFinished);
        return new Response(call, who, done, answers);
    }

    /// <summary>Whether this golem waits for an answer about that formation — named by the answer, the asker of a formation that starts after it and
    /// is not cued yet: what the uptake of <c>FinishedState</c> asks.</summary>
    internal bool AwaitsAnswer(string golemName, string formationName)
    {
        if (golemName == null) throw new GolemDomainException("Choreographies.AwaitsAnswer: 'golemName' was not given");
        if (formationName == null) throw new GolemDomainException("Choreographies.AwaitsAnswer: 'formationName' was not given");
        return AskedOf(golemName) && formations.Any(f => IsWaitingAfter(f, formationName));
    }

    private bool IsWaitingAfter(Formation f, string formationName) => !f.Dissolved && f.After == formationName.Trim().ToLowerInvariant() && !f.IsCued && f.Fleet.Has(golem);

    /// <summary>The formation of this golem that starts after the one named — the latest told. Refused when none waits.</summary>
    internal Formation FormationAfter(string formationName)
    {
        if (formationName == null) throw new GolemDomainException("Choreographies.FormationAfter: 'formationName' was not given");
        return formations.LastOrDefault(f => IsWaitingAfter(f, formationName)) ?? throw new GolemDomainException($"no formation of this golem starts after '{formationName}'");
    }

    /// <summary>Whether a formation of this golem has its cue to give — every member finished as this copy knows it, somebody to cue, nothing given
    /// yet: the clock's question (<c>Check(g.Choreography.Cueing)</c>).</summary>
    internal bool Cueing => formations.Any(f => f.Cueing);

    /// <summary>The formation whose cue this golem gives now — <c>formation = g.Choreography.CueingFormation; formation.Cue();</c>. Refused when none.</summary>
    internal Formation CueingFormation => formations.FirstOrDefault(f => f.Cueing) ?? throw new GolemDomainException("no formation of this golem has a cue to give");

    /// <summary>Whether a finish this golem asked is due in some formation — its part done, its word not counted: the clock's question
    /// (<c>Check(g.Choreography.Finishing)</c>), answered by writing <c>Finish(me)</c> again on <see cref="FinishingFormation"/>.</summary>
    internal bool Finishing => formations.Any(f => f.FinishDue);
    internal Formation FinishingFormation => formations.FirstOrDefault(f => f.FinishDue) ?? throw new GolemDomainException("no finish of this golem is due");

    /// <summary>How many words this golem keeps for the next telling of that name — a read for the lab and the tests.</summary>
    internal int OverheardCount(string name)
    {
        if (name == null) throw new GolemDomainException("Choreographies.OverheardCount: 'name' was not given");
        return overheard.TryGetValue(name.Trim().ToLowerInvariant(), out var words) ? words.Count : 0;
    }

    /// <summary>IN FORCE: told and not dissolved. A formation whose fleet FINISHED stays (19-oct-2026; Juan, after a try at dropping it from the
    /// list: "creo que quizás no debería borrarse, pero creo que siempre debería permitir moverse"): it is still listed, found and stepped —
    /// the finish was a point of its script, the cue given and the pull answered from it; only a dissolution takes it off the list.</summary>
    private static bool InForce(Formation f) => !f.Dissolved;

    /// <summary>The formation of that name, told before and in force — <c>formation = g.Choreography.Find(@formationName);</c>, the one
    /// place its name enters after it is made, like a route's id. Refused when the golem was told no formation by that name.</summary>
    internal Formation Find(string name)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Find: 'name' was not given");
        string called = name.Trim().ToLowerInvariant();
        return formations.LastOrDefault(f => f.Name == called && InForce(f)) ?? throw new GolemDomainException($"the golem was told no formation named '{called}': form it first");
    }

    /// <summary>WHETHER THE ASKER'S RECURRENCE STILL ASKS (propuesta 122, 21-oct-2026): what the <c>while</c> of the <c>remember</c> armed in the
    /// Follows act reads at every knock — <c>while (g.Choreography.StillAsks(@formationName, @stamp));</c> — by name AND stamp, so a copy told again
    /// under the same name (the old one dissolved, its stamp gone with it) ends the old recurrence by itself; false once the answer came, the
    /// formation was taken or dissolved, or it was never told here.</summary>
    internal bool StillAsks(string name, string stamp)
    {
        if (name == null) throw new GolemDomainException("Choreographies.StillAsks: 'name' was not given");
        if (stamp == null) throw new GolemDomainException("Choreographies.StillAsks: 'stamp' was not given");
        string called = name.Trim().ToLowerInvariant();
        return formations.Any(f => f.Name == called && f.Stamp == stamp && InForce(f) && f.StillAsks);
    }

    /// <summary>Whether the golem was told a formation by that name, still in force — what a script asks before it finds it.</summary>
    internal bool HasFormation(string name)
    {
        if (name == null) throw new GolemDomainException("Choreographies.HasFormation: 'name' was not given");
        string called = name.Trim().ToLowerInvariant();
        return formations.Any(f => f.Name == called && InForce(f));
    }

    /// <summary>Whether this golem has its place in the formation of that name — so a word about it (a member placed, a step) is its
    /// business: <c>if (g.Choreography.Knows(@call)) { … }</c>.</summary>
    internal bool Knows(string name)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Knows: 'name' was not given");
        string called = name.Trim().ToLowerInvariant();
        return formations.Any(f => f.Name == called && InForce(f) && f.Route != null);
    }

    /// <summary>THE FORMATIONS THE GOLEM IS IN (propuesta 104: the warden's GET): every one it was told and not dissolved — a finished one
    /// among them — in the order it was told them, each with its figure, its fleet, its policy and who holds what.</summary>
    internal IReadOnlyList<Formation> Formations() => formations.Where(InForce).ToList();

    /// <summary>THE DISPUTES this golem had (14-oct-2026): over the places of every formation it was told, the dissolved ones too (history), in the
    /// order it was told them — won or yielded, against whom.</summary>
    internal IReadOnlyList<Dispute> Disputes() => formations.SelectMany(f => f.Disputes).ToList();

    /// <summary>EVERY DISPUTE FORGOTTEN (14-oct-2026): in every formation it was told, the dissolved ones too — <c>g.Choreography.ForgetDisputes();</c>.
    /// The ones to come appear when a formation is told again and its round is played.</summary>
    internal void ForgetDisputes()
    {
        foreach (var formation in formations) formation.ForgetDisputes();
    }

    /// <summary>DISSOLVED (propuesta 104) — <c>g.Choreography.Dissolve(formation);</c>: it leaves the list; a route of this golem still pending
    /// is abandoned. Told to the fleet by the reaction on this act.</summary>
    internal void Dissolve(Formation formation)
    {
        if (formation == null) throw new GolemDomainException("Choreographies.Dissolve: 'formation' was not given");
        if (!formations.Contains(formation)) throw new GolemDomainException($"the golem was told no formation named '{formation.Name}'");
        formation.Dissolve();
        overheard.Remove(formation.Name);   // words kept for a next telling of a name dissolved are words of nothing
    }

    /// <summary>The FORMATION IN PLACE (ajuste 77): the one of the latest route this golem took a place by — what the arrival's word is
    /// about. Refused when the golem stands in no formation.</summary>
    internal Formation Current =>
        formations.Where(f => f.Route != null && InForce(f)).OrderByDescending(f => f.Route.Id).FirstOrDefault()
        ?? throw new GolemDomainException("the golem stands in no formation: form one and take it first");

    /// <summary>Whether that route was this golem's route to its place in a formation, and reached it — the arrival asks it to say the
    /// golem is placed: <c>if (g.Choreography.Reached(route)) { … }</c>.</summary>
    internal bool Reached(Route route)
    {
        if (route == null) throw new GolemDomainException("Choreographies.Reached: 'route' was not given");
        return formations.Any(f => InForce(f) && f.IsReached(route));
    }

    /// <summary>The formation a route of this golem belongs to (propuesta 74): the one that holds it now —
    /// <c>formation = g.Choreography.FormationOf(route);</c>, the body's word on a route that yields. Refused for a route of no formation.</summary>
    internal Formation FormationOf(Route route)
    {
        if (route == null) throw new GolemDomainException("Choreographies.FormationOf: 'route' was not given");
        return formations.FirstOrDefault(f => ReferenceEquals(f.Route, route))
            ?? throw new GolemDomainException($"route {route.Id} was given by no formation");
    }
}
