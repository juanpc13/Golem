using GolemDomain.Geometry;
using GolemDomain.Routes;
using GolemDomain.Units;

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
    private readonly List<Union> unions = new();   // THE PROGRAMMES (propuesta 123): each naming several open formations and sharing the golems among them
    private readonly Dictionary<string, Dictionary<string, (Position At, string Nonce)>> overheard = new();   // words of a round heard AHEAD of the formation they are about: name → who → where
    private readonly HashSet<string> pendingCues = new(StringComparer.Ordinal);   // cues heard before the formation they name was told here (propuesta 117; by the formation's name since propuesta 123)
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

    // ---- THE CUE BETWEEN DANCERS (propuesta 117, 19-oct-2026): the word "continúas tú" named a golem, who relayed it to its team; SINCE PROPUESTA 123
    // (21-oct-2026; Juan: "cómo solucionar el problema sin coordinador") IT NAMES A FORMATION, and every member of it takes the cue by itself ----

    /// <summary>Whether THIS golem waits in the formation named by a cue — told, starting on cue, not cued yet, of its fleet, not taken — what the uptake of
    /// <c>YourTurn</c> asks: <c>if (g.Choreography.AwaitsCue(@to)) { formation = g.Choreography.Cued(@to); formation.TakeCue(from, me); … }</c>. Every
    /// member of the formation named takes it, so the round completes with no relay.</summary>
    internal bool AwaitsCue(string formationName)
    {
        if (formationName == null) throw new GolemDomainException("Choreographies.AwaitsCue: 'formationName' was not given");
        string called = formationName.Trim().ToLowerInvariant();
        return formations.Any(f => f.Name == called && WaitsForTheCue(f));
    }

    // an open formation waits for no cue before its union resolved (lab 21-oct-2026: a cue kept from an earlier show, taken by a candidate that was no member, made the right dance at once)
    private bool WaitsForTheCue(Formation f) => !f.Dissolved && f.OnCue && !f.IsCued && (!f.IsOpen || f.IsResolved) && f.Fleet.Has(golem) && !f.Knows(f.Fleet.Member(golem));

    /// <summary>The formation of that name this golem waits in for the cue — the latest told. Taking it ends a cue kept ahead for that name. Refused when none waits.</summary>
    internal Formation Cued(string formationName)
    {
        if (formationName == null) throw new GolemDomainException("Choreographies.Cued: 'formationName' was not given");
        string called = formationName.Trim().ToLowerInvariant();
        var formation = formations.LastOrDefault(f => f.Name == called && WaitsForTheCue(f)) ?? throw new GolemDomainException($"this golem waits in no formation named '{called}' for a cue");
        pendingCues.Remove(called);
        return formation;
    }

    /// <summary>A CUE HEARD AHEAD (propuesta 117): the word named a formation this golem was not told yet (its `form … --on-cue` comes late, a slow
    /// `use map` on its tab): kept by the name, and the clock takes it once the formation is told (<c>TakeIfCued</c>, <see cref="CuedFormation"/>).</summary>
    internal void OverheardCue(string formationName)
    {
        if (formationName == null) throw new GolemDomainException("Choreographies.OverheardCue: 'formationName' was not given");
        pendingCues.Add(formationName.Trim().ToLowerInvariant());
    }

    /// <summary>Whether a cue kept ahead has its formation now — told, waiting, this golem of its fleet: the clock's question.</summary>
    internal bool HasPendingCue => pendingCues.Any(AwaitsCue);

    /// <summary>The formation a cue kept ahead is about, waiting now — the clock's take (<c>TakeIfCued</c>). Refused when none.</summary>
    internal Formation CuedFormation => Cued(pendingCues.FirstOrDefault(AwaitsCue) ?? throw new GolemDomainException("no cue kept here waits for a formation"));

    // ---- THE PULL (propuesta 118, 20-oct-2026; Juan: "que yellow esté pendiente de consultarle a alguno si ya terminó"): a question and an answer, two
    // tells; the asker's golem COMMITS ITSELF to ask every so often (propuesta 122: the engine's own remember), and since propuesta 123 the question
    // names the FORMATION asked, not a golem: the first by rank of that formation answers from its own copy, on its clock ----

    /// <summary>Whether a word addressed to a golem by name is for THIS golem — the uptake of an answer asks it first (the answer names the asker).</summary>
    internal bool AskedOf(string golemName)
    {
        if (golemName == null) throw new GolemDomainException("Choreographies.AskedOf: 'golemName' was not given");
        return golem.Name != null && golem.Name == golemName.Trim().ToLowerInvariant();
    }

    /// <summary>Whether THIS golem answers for the formation named (propuesta 123): told it, in force, of its fleet, and ITS FIRST BY RANK — the
    /// election by rank, zero words: every copy of that formation computes the same one. What the uptake of <c>DidYouFinish</c> asks:
    /// <c>if (g.Choreography.Answers(@about)) { g.Choreography.AskedAbout(@about, @asker); }</c>.</summary>
    internal bool Answers(string formationName)
    {
        if (formationName == null) throw new GolemDomainException("Choreographies.Answers: 'formationName' was not given");
        string called = formationName.Trim().ToLowerInvariant();
        var formation = formations.LastOrDefault(f => f.Name == called && InForce(f));
        return formation != null && formation.Fleet.Has(golem) && formation.Asker == golem.Name;
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

    private bool IsWaitingAfter(Formation f, string formationName) => !f.Dissolved && f.After == formationName.Trim().ToLowerInvariant() && !f.IsCued && (!f.IsOpen || f.IsResolved) && f.Fleet.Has(golem);

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

    // ---- THE PROGRAMME (propuesta 123, 21-oct-2026): several open formations in order, the golems shared among them by the law ----

    /// <summary>THE PROGRAMME TOLD — <c>union = g.Choreography.Union(@unionName, @formationNames, fleet, @by, @stamp);</c>: its formations by
    /// name, in order; its candidates (the fleet it was told to: this golem and its peers, unless the warden says); its policy — distance or rps.
    /// Told again under the same name: made again, the old one dissolved with its formations. Words of its round heard ahead are taken in.</summary>
    internal Union Union(string name, string formationNames, Fleet candidates, Policy by, string stamp) => Union(name, formationNames, candidates, by, Link.Cue, null, stamp);

    /// <summary>The same, WITH ITS LINK (propuesta 124) — <c>union = g.Choreography.Union(@unionName, @formationNames, fleet, @by, @link, period, @stamp);</c>:
    /// how its beats are agreed among the golems, by the cue or by asking every so often.</summary>
    internal Union Union(string name, string formationNames, Fleet candidates, Policy by, Link link, Duration every, string stamp)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Union: 'name' was not given");
        if (formationNames == null) throw new GolemDomainException("Choreographies.Union: 'formationNames' was not given");
        if (candidates == null) throw new GolemDomainException("Choreographies.Union: 'candidates' was not given");
        string called = name.Trim().ToLowerInvariant();
        if (called == "") throw new GolemDomainException("a union needs a name");
        if (formations.Any(f => f.Name == called && InForce(f))) throw new GolemDomainException($"'{called}' names a formation already: a union needs a name of its own");
        var union = new Union(called, formationNames, candidates, golem, this, by, link, every, stamp);
        unions.FirstOrDefault(x => x.Name == called && !x.Dissolved)?.Dissolve();   // told again: the old one and its formations go
        foreach (var billed in union.Names) pendingCues.Remove(billed);              // a cue kept for a name of a show told again is of the old show (lab 21-oct-2026)
        unions.Add(union);
        if (overheard.Remove(called, out var words))
            foreach (var (who, word) in words)
                if (who != golem.Name && candidates.Names.Contains(who)) union.Stood(candidates.Member(who), word.At, word.Nonce);
        return union;
    }

    /// <summary>THE PROGRAM (propuesta 124, 22-oct-2026, night; Juan: "un solo comando con toda la definición… el dominio debería crear el objeto completo
    /// del sindicato y el dominio entiende que debe terminar creando los dos objetos de las formaciones con todas sus instrucciones") —
    /// <c>union = g.Choreography.Program(@programName, @definition, fleet, @by, @link, period, @stamp);</c>: the whole union from ONE definition
    /// (<see cref="Score.Read"/>): the union itself, then every formation of it — made with its figure, OPENED into the union, its part of the score
    /// queued step by step (a rest a <see cref="Move.Rest"/>) and its finish implied at the end of its part. The same text on every candidate's tab makes
    /// the same copy everywhere; the golem's own word (its take) is the Convene that follows in the same act. Told again under its name: made again.</summary>
    internal Union Program(string name, string definition, Fleet candidates, Policy by, Link link, Duration every, string stamp)
    {
        if (name == null) throw new GolemDomainException("Choreographies.Program: 'name' was not given");
        if (definition == null) throw new GolemDomainException("Choreographies.Program: 'definition' was not given");
        if (candidates == null) throw new GolemDomainException("Choreographies.Program: 'candidates' was not given");
        if (string.IsNullOrWhiteSpace(stamp)) throw new GolemDomainException("a program needs its stamp: the once of its call");
        var score = Score.Read(definition);
        var union = Union(name, score.Bill, candidates, by, link, every, stamp);
        foreach (var part in score.Parts)
        {
            var formation = Form(part.Name, part.Figure, part.Center, part.Measure, part.Turn, candidates, by, stamp);   // the union's stamp: one hand at the game in every copy
            Open(formation, part.Places);
            for (int i = 0; i < part.Steps.Count; i++)
                formation.Queue(part.Steps[i] is Sense sense ? formation.Figure.Rotate(sense) : Move.Rest, $"{stamp}-{part.Name}-{i + 1}");
            formation.FinishesAtTheEnd();
        }
        return union;
    }

    /// <summary>Whether a union of that name was told and is in force.</summary>
    internal bool HasUnion(string name)
    {
        if (name == null) throw new GolemDomainException("Choreographies.HasUnion: 'name' was not given");
        string called = name.Trim().ToLowerInvariant();
        return unions.Any(x => x.Name == called && !x.Dissolved);
    }

    /// <summary>The union of that name, in force — <c>union = g.Choreography.FindUnion(@unionName);</c>. Refused when none.</summary>
    internal Union FindUnion(string name)
    {
        if (name == null) throw new GolemDomainException("Choreographies.FindUnion: 'name' was not given");
        string called = name.Trim().ToLowerInvariant();
        return unions.LastOrDefault(x => x.Name == called && !x.Dissolved) ?? throw new GolemDomainException($"the golem was told no union named '{called}': say it first");
    }

    /// <summary>Whether a word about that formation is HEARD here (propuesta 124): this golem has a route in it, or it is a formation of a union this
    /// golem dances in — every copy keeps the whole score of its union, the other formations' words included (<c>PlacedAt</c>, <c>AlignedAt</c>,
    /// <c>FinishedAt</c>), so the beats step and start the same everywhere.</summary>
    internal bool Hears(string formationName)
    {
        if (formationName == null) throw new GolemDomainException("Choreographies.Hears: 'formationName' was not given");
        string called = formationName.Trim().ToLowerInvariant();
        var formation = formations.LastOrDefault(f => f.Name == called && InForce(f));
        if (formation == null) return false;
        if (formation.Route != null) return true;
        return formation.IsOpen && formation.Union != null && formation.Union.IsMember;
    }

    /// <summary>The unions in force, in the order they were told (the warden's read).</summary>
    internal IReadOnlyList<Union> Unions() => unions.Where(x => !x.Dissolved).ToList();

    /// <summary>The union in force that names that formation; null when none.</summary>
    internal Union UnionNaming(string formationName)
    {
        if (formationName == null) throw new GolemDomainException("Choreographies.UnionNaming: 'formationName' was not given");
        string called = formationName.Trim().ToLowerInvariant();
        return unions.LastOrDefault(x => !x.Dissolved && x.Names.Contains(called));
    }

    /// <summary>The formation of that name told here, in force, OPEN and of that union; null when none — what the union asks to know whether
    /// every figure of its is told (<see cref="Formations.Union.IsKnown"/>).</summary>
    internal Formation OpenFormationOf(string formationName, string unionName)
    {
        if (formationName == null) throw new GolemDomainException("Choreographies.OpenFormationOf: 'formationName' was not given");
        if (unionName == null) throw new GolemDomainException("Choreographies.OpenFormationOf: 'unionName' was not given");
        string called = formationName.Trim().ToLowerInvariant(), show = unionName.Trim().ToLowerInvariant();
        return formations.LastOrDefault(f => f.Name == called && InForce(f) && f.IsOpen && f.UnionName == show);
    }

    /// <summary>OPENED (propuesta 123) — <c>union = g.Choreography.Open(formation);</c>, the warden's <c>--open</c> right after the Form: the formation
    /// belongs to the union that names it (said before it, on the same tab), its fleet is the union's candidates, and if with it the union's
    /// round is complete (the words came first), the law runs now. Refused when no union in force names it.</summary>
    internal Union Open(Formation formation) => Open(formation, 0);

    /// <summary>The same, FOR HOW MANY golems the figure takes (22-oct-2026): <c>union = g.Choreography.Open(formation, @openPlaces);</c>.</summary>
    internal Union Open(Formation formation, int places)
    {
        if (formation == null) throw new GolemDomainException("Choreographies.Open: 'formation' was not given");
        if (!formations.Contains(formation)) throw new GolemDomainException($"the golem was told no formation named '{formation.Name}'");
        var union = UnionNaming(formation.Name) ?? throw new GolemDomainException($"no union names {formation.Name}: say the union first — union <name> {formation.Name},…");
        formation.Opens(union, places);
        union.Told();
        return union;
    }

    /// <summary>A PROGRAMME DISSOLVED — <c>g.Choreography.DissolveUnion(union);</c>: it and its formations leave the list; told to the fleet by the reaction on this act.</summary>
    internal void DissolveUnion(Union union)
    {
        if (union == null) throw new GolemDomainException("Choreographies.DissolveUnion: 'union' was not given");
        if (!unions.Contains(union)) throw new GolemDomainException($"the golem was told no union named '{union.Name}'");
        union.Dissolve();
        overheard.Remove(union.Name);
        foreach (var name in union.Names) { overheard.Remove(name); pendingCues.Remove(name); }
    }

    /// <summary>THE RECURRENCE TO ARM (propuesta 123): whether some formation of this golem has its asker's recurrence due — this golem the asker, the
    /// formation waiting for its answer, open ones enrolled — the clock's question (<c>ArmIfAsker</c>); and which.</summary>
    internal bool ArmsInquiry => formations.Any(f => InForce(f) && f.InquiryDue);
    internal Formation InquiryToArm => formations.FirstOrDefault(f => InForce(f) && f.InquiryDue) ?? throw new GolemDomainException("no recurrence of this golem is due to arm");

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
        pendingCues.Remove(formation.Name);  // a cue kept for it too
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
