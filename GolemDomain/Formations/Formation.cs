using System.Globalization;
using GolemDomain.Geometry;
using GolemDomain.Routes;
using GolemDomain.Touches;
using GolemDomain.Units;
using GolemDomain.Units;

namespace GolemDomain.Formations;

/// <summary>Who holds a place of a formation, as this golem's copy knows it: the member's name, the index of its place in the figure's
/// fixed order, where that place stands, and the orbit (the ring) it lies in.</summary>
internal sealed class Holder
{
    internal string Name { get; }
    internal int Index { get; }
    internal Position At { get; }
    internal int Orbit { get; }
    internal double X => At.X;
    internal double Y => At.Y;

    internal Holder(string name, int index, Position at, int orbit)
    {
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("Holder.Holder: 'name' was not given");
        if (at == null) throw new GolemDomainException("Holder.Holder: 'at' was not given");
        Name = name; Index = index; At = at; Orbit = orbit;
    }
}

/// <summary>
/// THE FORMATION (propuesta 104, 8-oct-2026; Juan: "las formaciones le pertenecerán al golem… a los otros golems involucrados se les
/// proporcionará la misma formación… la lógica de las coreografías con esto de las formaciones, que al final vienen siendo la misma cosa"):
/// a NAME the warden gave it (<c>square-2</c>), its FIGURE, its FLEET, its POLICY — the places shared BY RANK (the names sorted) or BY
/// DISTANCE (who stands nearest, the first law) — and THE PLACE EVERY MEMBER HOLDS. Every golem of the fleet keeps its own copy, fed by the
/// same words, so all compute the same table. What was the CONVOCATION (<c>Muster</c>, ajustes 73–86) with a name: every golem CONVENES
/// (<see cref="Convene"/>, it says where it stands); every peer's word (<see cref="Stood"/>) is where it stood. SINCE AJUSTE 80 (1-oct-2026;
/// Juan: "sólo pedir la posición de los demás al inicio de una coreografía y luego ya no") THE ROUND OPENS THE ROUTES: nobody sets out until
/// every member said where it stands — the word that completes the round, the golem's own or the last heard, opens its route — so every
/// copy shares the same table before anyone moves, and the OTHER members enter the planner as berths (<see cref="Berths"/>). A later word,
/// by distance, applies the first law again and may make the route yield (<see cref="Halted"/>, propuesta 74). Then every golem says when
/// it stands on its place (<see cref="Placed"/>), moves are queued (<see cref="Queue"/> — the move the FIGURE says, <see cref="Figure.Rotate(Sense, Ring)"/>,
/// ajuste 84) and the next one opens once everybody is placed (<see cref="Step"/>): all set out together; a move of one ring of a double
/// ring leaves the other ring standing. <see cref="Join"/> stays in the repertoire: by rank, at once, for the tests.
/// </summary>
internal sealed class Formation
{
    private readonly Golem golem;                                        // whose copy this is: it opens every route to a place (ajustes 70, 78)
    private readonly Policy policy;                                      // the policy, the formation's from birth (ajuste 80): rank, who stands nearest, or the game (propuesta 115)
    private bool byDistance => policy != Policy.Rank;                         // shared BY THE WORDS — by distance or by the game: the table is the law over who spoke
    private readonly Dictionary<string, string> nonces = new(StringComparer.Ordinal);   // the stamp each peer's word carried: its hand at the game (propuesta 115)
    private readonly List<(int Place, string Winner, string Loser, string Game)> matches = new();   // the game's matches at the last table
    private Position convenedFrom;                                       // where this golem said it stood, to open its route from once the round completes
    private readonly Dictionary<string, Position> stood = new();         // where each member said it stood, by name
    private readonly List<Dispute> disputes = new();                     // the places this golem disputed by distance, as the law settled them (derived)
    private bool disputesForgotten;                                      // the warden cleared them (14-oct-2026): this copy settles no more
    private readonly Dictionary<string, int> indices = new(StringComparer.Ordinal);   // THE PLACE EVERY MEMBER HOLDS: by rank from birth, by distance once it spoke; shifted by every step
    private readonly HashSet<string> placed = new(StringComparer.Ordinal);   // who said it stands on its place, this round
    private readonly Dictionary<int, HashSet<string>> aligned = new();      // who said it is lined up, BY ROUND (propuesta 106): a word may come before this copy reached that round
    private HashSet<string> movers = new(StringComparer.Ordinal);            // the members the round underway moves: whose line-up the start waits for (the take: everybody)
    private readonly Queue<(Move Move, string Id)> steps = new();        // the moves queued, in the order they were asked (ajuste 84: the figure's)
    private readonly HashSet<string> stepIds = new(StringComparer.Ordinal);
    private Member me;                                                   // this golem, once it joined or convened
    private Position place;                                             // the place it is going to, or holds
    private Route route;                                                // …and the route there
    private string yieldedTo;                                           // the peer whose word made that route yield, until it is replaced
    private int retakes;                                                // how many times this golem's failed route was opened again this round (propuesta 113)
    // THE CUE BETWEEN DANCERS (propuesta 117, 19-oct-2026): whom this fleet cues when every member finished; whether this formation starts on a
    // cue and whether the cue came; who said it finished its part (this golem's own word queued until its part is done); the cue given
    private string cueTo;                                               // the FORMATION the last of this fleet cues when everybody finished (propuesta 123: a name of a formation, never a golem's); null: nobody
    private bool onCue;                                                 // born waiting: its take IS the cue (or a member's word, once the cued one spoke)
    private bool cued;                                                  // the cue came, or a member spoke: the formation may be convened
    // THE PULL (propuesta 118, 20-oct-2026): the formation this one starts after; its asker asks THAT formation whether it finished (propuesta 123: its first by rank answers)
    private string after;                                               // the formation this one starts after; null: none (a push, or none)
    private Duration every;                                             // how often (propuestas 121, 122): the period the asker's recurrence asks at
    private int asked;                                                  // how many questions the asker sent
    private int notYet;                                                 // how many answers said "not yet"
    private bool finishing;                                             // this golem asked to finish: counted the moment its part is done (the clock asks)
    private readonly HashSet<string> finished = new(StringComparer.Ordinal);   // who said it finished its part
    private bool cueGiven;                                              // this copy gave the cue: the one that counted the fleet's last finish
    // THE OPEN FORMATION (propuesta 123, 21-oct-2026): one of a union — its fleet is the union's candidates until the union's law picks its members
    private bool open;                                                  // told --open: the union that names it shares the golems
    private bool enrolled;                                              // the union resolved: Fleet is its members now
    private string unionName;                                       // the union that names it; null: none
    private int openPlaces;                                             // how many golems it takes, told (--places n); 0: its vertices
    private readonly List<Peer> company = new();                        // the candidates that went elsewhere, where they stood: bodies on the same floor, planned around (berths)
    private bool inquiryArmed;                                          // the asker's recurrence was armed (the clock's act, ArmInquiry)
    private Union union;                                                // THE UNION IT FOLLOWS (propuesta 124): its steps are beats of the union, danced with the other formations; null for a loose formation

    /// <param name="stamp">the ONCE of the formation (ajuste 71's call id, kept): the host mints it when the warden forms, every copy keeps the same, and
    /// every tell about the formation carries it, so a formation told again under the same name is a new round of words to the peers</param>
    internal Formation(string name, Figure figure, Fleet fleet, Golem golem, Policy policy, string stamp)
    {
        if (!Enum.IsDefined(policy)) throw new GolemDomainException($"the places are shared by Rank, Distance or RockPaperScissors, not by '{policy}'");
        if (string.IsNullOrWhiteSpace(stamp)) throw new GolemDomainException("a formation needs its stamp: the once of its call");
        if (string.IsNullOrWhiteSpace(name)) throw new GolemDomainException("a formation needs a name");
        if (figure == null) throw new GolemDomainException("Formation.Formation: 'figure' was not given");
        if (fleet == null) throw new GolemDomainException("Formation.Formation: 'fleet' was not given");
        if (golem == null) throw new GolemDomainException("Formation.Formation: 'golem' was not given");
        Name = name.Trim().ToLowerInvariant();
        Stamp = stamp;
        Figure = figure;
        Fleet = fleet;
        this.golem = golem;
        this.policy = policy;
        figure.Places(fleet.Count);   // a figure the fleet does not fit is refused at birth
        if (!byDistance) foreach (var member in fleet.Names) indices[member] = fleet.Member(member).Rank;   // by rank every place is known from birth
    }

    /// <summary>The name the warden gave it, lower case: its identity in every journal of the fleet.</summary>
    internal string Name { get; }
    /// <summary>The once of this formation's call, minted by the host: what every tell about it carries, so the peers take each word once.</summary>
    internal string Stamp { get; }
    /// <summary>How the places are shared: <c>Rank</c> (the names sorted), <c>Distance</c> (who stands nearest, the first law) or
    /// <c>RockPaperScissors</c> (who stands nearest, and the game between those who want the same place — propuesta 115).</summary>
    internal Policy Policy => policy;
    internal Figure Figure { get; }
    internal Fleet Fleet { get; private set; }
    /// <summary>Dissolved by the warden (propuesta 104): it stays in the journal's history and in no list.</summary>
    internal bool Dissolved { get; private set; }
    /// <summary>How many members said where they stood — this golem among them once it convened.</summary>
    internal int StoodCount => stood.Count;
    /// <summary>Every member of the fleet said where it stands: the round is complete, every copy has the same table, the routes open.</summary>
    internal bool IsComplete => stood.Count == Fleet.Count;
    /// <summary>This golem's route in the formation; null before it joined or convened.</summary>
    internal Route Route => route;

    // ---- THE CUE BETWEEN DANCERS (propuesta 117, 19-oct-2026; Juan: "dos grupos de personas van a ir a bailar… ya conocen su coreografía completa…
    // no habrá un director… el último del primer grupo le hace la seña a alguno del otro grupo… uno sólo le dice el nombre del golem… a él sólo le
    // dicen 'continúas tú' y éste le avisa a su equipo"; then: "dejarle a red la responsabilidad de terminar la formación, de notificarle a su grupo
    // que se terminó"; then, 19-oct evening: "volvamos a que el último en salir de la coreografía sea el que diga realmente a yellow") ----

    /// <summary>WHICH FORMATION THIS FLEET CUES when its dance is over (propuesta 117; since propuesta 123, 21-oct-2026, A FORMATION'S NAME, never a
    /// golem's — Juan: "el último le tuvo que decir a los 3… cómo solucionar el problema sin coordinador": the cue named one golem, who relayed it to
    /// its team; now every member of the formation named takes the cue by itself and the round completes with no relay): the warden's
    /// <c>--cue tri-right</c> — <c>formation.Cues(@cueTo);</c> right after the <c>Form</c>. A name and nothing more: this fleet knows nothing of that
    /// formation's figure or fleet. Refused for its own name, and once the cue was given.</summary>
    internal void Cues(string formationName)
    {
        if (formationName == null) throw new GolemDomainException("Formation.Cues: 'formationName' was not given");
        string called = formationName.Trim().ToLowerInvariant();
        if (called == "") throw new GolemDomainException($"{Name} cues a formation by its name");
        if (called == Name) throw new GolemDomainException($"{Name} cannot cue itself");
        if (open) throw new GolemDomainException($"{Name} is open: it follows the beats of its union {unionName} — a cue is for a loose formation");   // propuesta 124
        if (cueGiven) throw new GolemDomainException($"{Name} already gave its cue");
        cueTo = called;
    }

    /// <summary>The formation this fleet cues when it finished; "" when none.</summary>
    internal string CueTo => cueTo ?? "";

    /// <summary>THIS FORMATION STARTS ON A CUE (propuesta 117): the warden's <c>--on-cue</c> — <c>formation.StartsOnCue();</c> right after the
    /// <c>Form</c>. Born waiting: a take is refused until the cue comes to one member (<see cref="Cued"/>) or a member's word is heard
    /// (<see cref="Stood"/>: the cued one took it, the team follows). Everything queued meanwhile — steps, the finish — is kept and done once it
    /// sets out: the golem knows its whole choreography before it dances. Refused once this golem convened.</summary>
    internal void StartsOnCue()
    {
        if (me != null) throw new GolemDomainException($"{Name} was already taken: it starts on cue no more");
        if (open) throw new GolemDomainException($"{Name} is open: it is taken with its union {unionName} — on cue is for a loose formation");   // propuesta 124
        onCue = true;
    }

    /// <summary>How often the asker asks unless the warden says (propuesta 121): every 5 s.</summary>
    internal const double DefaultEverySeconds = 5.0;

    /// <summary>THIS FORMATION STARTS AFTER ANOTHER, BY ASKING (propuesta 118, 20-oct-2026; Juan: "quiero ver que yellow esté pendiente de consultarle
    /// a alguno si ya terminó… por ask me suena bien"): the warden's <c>--after tri-left [--every 5s]</c> — <c>period = Seconds(@everySeconds);
    /// formation.Follows(@after, period);</c> right after the <c>Form</c>. Born waiting like one on cue (a take refused, the steps queued), and
    /// its ASKER — the first of the fleet's order (<see cref="IsAsker"/>) — COMMITS ITSELF (propuesta 122, 21-oct-2026; paper 0B: "a Puppet can claim
    /// future attention", and the engine's own <c>remember</c> since the 3.0) to ask THAT FORMATION whether it finished (propuesta 123: no golem is
    /// named — the first by rank of the formation asked answers, <see cref="Choreographies.Answers"/>), every so often,
    /// until the answer is yes: the recurrence is ARMED IN THE SAME ACT by the host's script — <c>if (formation.IsAsker) { remember (@firstAt,
    /// @everyTicks) { asking.Ask(); … } while (g.Choreography.StillAsks(@formationName, @stamp)); }</c> — and the substrate keeps it: every activation
    /// is a command of the actor's own (<see cref="Ask"/>, the act <c>echo-ask</c> tells <c>DidYouFinish</c> on), the <c>while</c> reads
    /// <see cref="StillAsks"/> at every knock and closes it by itself once the answer came (<see cref="TakeCue"/>), the formation was taken or
    /// dissolved. "Not yet" is counted (<see cref="HeardNotYet"/>) and the recurrence goes on. The domain keeps WHO asks, HOW OFTEN and WHETHER IT
    /// STILL ASKS; the engine keeps the appointments (rebuilt from the journal on replay). The other members arm nothing. Refused once this golem
    /// convened, and for its own name.</summary>
    internal void Follows(string formationName, Duration every)
    {
        if (formationName == null) throw new GolemDomainException("Formation.Follows: 'formationName' was not given");
        if (every == null) throw new GolemDomainException("Formation.Follows: 'every' was not given");
        if (every.IsZero) throw new GolemDomainException($"{Name} asks every so often: zero is no period");
        string called = formationName.Trim().ToLowerInvariant();
        if (called == "") throw new GolemDomainException($"{Name} starts after a formation by its name");
        if (called == Name) throw new GolemDomainException($"{Name} cannot start after itself");
        if (me != null) throw new GolemDomainException($"{Name} was already taken: it starts after nothing now");
        if (open) throw new GolemDomainException($"{Name} is open: it is taken with its union {unionName} — a pull is for a loose formation");   // propuesta 124
        after = called;
        this.every = every;
        onCue = true;
    }

    /// <summary>The same, asking every 5 s (<see cref="DefaultEverySeconds"/>).</summary>
    internal void Follows(string formationName) => Follows(formationName, new Seconds(DefaultEverySeconds));

    /// <summary>The formation this one starts after ("" none) — the one ASKED whether it finished: its first by rank answers (propuesta 123) —, who
    /// asks here (the first by rank), how often (0 when it starts after nothing), how many times, how many "not yet".</summary>
    internal string After => after ?? "";
    internal string Asker => Fleet.Names[0];
    internal double EverySeconds => every?.InSeconds ?? 0.0;
    internal int Asked => asked;
    internal int NotYet => notYet;
    /// <summary>Whether THIS golem is the asker: the first of the fleet's order (a golem without a name is nobody's asker).</summary>
    internal bool IsAsker => golem.Name != null && golem.Name == Asker;
    /// <summary>Whether THIS golem may ask: it is the asker of a formation that starts after another, not cued yet, not taken, not dissolved —
    /// what an activation of the recurrence needs true.</summary>
    internal bool MayAsk => after != null && !Dissolved && !cued && me == null && IsAsker && (!open || enrolled);
    /// <summary>THE RECURRENCE TO ARM (propuesta 123): this golem is the asker of a formation that starts after another, not cued, not taken — and, open,
    /// enrolled already (its asker is the first of its MEMBERS, known only once the union resolved) — and the recurrence is not armed yet: the
    /// clock's question (<c>ArmIfAsker</c>), answered by writing <see cref="ArmInquiry"/> with the engine's <c>remember</c> in the same act.</summary>
    internal bool InquiryDue => MayAsk && !inquiryArmed;
    /// <summary>The asker's recurrence ARMED (propuesta 123): the act the clock writes once, with the <c>remember</c> beside it. Refused when none is due.</summary>
    internal void ArmInquiry()
    {
        if (!InquiryDue) throw new GolemDomainException($"{Name}: no recurrence to arm here — {(inquiryArmed ? "armed already" : !IsAsker ? $"{Asker} asks, not {golem.Name}" : "nothing to ask")}");
        inquiryArmed = true;
    }
    internal bool InquiryArmed => inquiryArmed;

    // ---- THE OPEN FORMATION (propuesta 123, 21-oct-2026; Juan: "sin que uno elija a los golems como tal… ellos deciden dónde estar") ----

    /// <summary>OPENED (propuesta 123): the warden's <c>--open</c> — <c>g.Choreography.Open(formation)</c>, the module's act right after the Form: the
    /// formation belongs to the union that names it, its fleet is the union's CANDIDATES until the union's law picks its MEMBERS
    /// (<see cref="Enrol"/>). A polygon only (its vertices are its places), shared by distance or rps (by rank nobody chooses); refused once taken.</summary>
    internal void Opens(string unionName, Fleet candidates) => Opens(unionName, candidates, 0);

    /// <summary>The same, FOR HOW MANY (22-oct-2026; Juan: "uno puede decir cuántos van destinados para cada una de las formaciones"): the places the
    /// union's law shares out on this figure — its vertices, or, for more, the corners first and the rest on the sides (ajuste 100); with a count
    /// a circle opens too. Zero: the vertices (a polygon only).</summary>
    internal void Opens(string unionName, Fleet candidates, int places)
    {
        if (unionName == null) throw new GolemDomainException("Formation.Opens: 'unionName' was not given");
        if (candidates == null) throw new GolemDomainException("Formation.Opens: 'candidates' was not given");
        if (places < 0) throw new GolemDomainException($"{Name} opens for a number of golems above zero, or its vertices (zero)");
        if (me != null) throw new GolemDomainException($"{Name} was already taken: it opens no more");
        if (enrolled) throw new GolemDomainException($"{Name} was enrolled already");
        if (policy == Policy.Rank) throw new GolemDomainException($"{Name} cannot open by rank: an open formation shares its places by distance or rps — by rank nobody chooses");
        if (places == 0 && Figure is not Polygon) throw new GolemDomainException($"{Name} cannot open: an open formation is a polygon — its vertices are its places; a {Figure.Name} opens for a number of golems (--places n)");
        if (cueTo != null || onCue || after != null) throw new GolemDomainException($"{Name} cannot open: an open formation follows the beats of its union — no --cue, --on-cue or --after of its own (propuesta 124)");
        open = true;
        openPlaces = places;
        this.unionName = unionName.Trim().ToLowerInvariant();
        Fleet = candidates;
        indices.Clear();
    }

    /// <summary>OPENED INTO ITS UNION (propuesta 124): the same, with the union itself — its steps are the union's beats, danced together with the
    /// other formations of it (<see cref="Union.Step"/>, <see cref="Union.LetGoIfAligned"/>).</summary>
    internal void Opens(Union union, int places)
    {
        if (union == null) throw new GolemDomainException("Formation.Opens: 'union' was not given");
        Opens(union.Name, union.Candidates, places);
        this.union = union;
    }

    internal bool IsOpen => open;
    /// <summary>The union that names it; "" when none.</summary>
    internal string UnionName => unionName ?? "";
    /// <summary>The union resolved and this formation got its members.</summary>
    internal bool IsResolved => enrolled;
    /// <summary>Whether THIS golem dances in it: of its fleet — and, open, enrolled: before the union resolves everybody is a candidate, nobody a member.</summary>
    internal bool IsMember => golem.Name != null && Fleet.Has(golem) && (!open || enrolled);
    /// <summary>How many places the figure lays out: as many as the fleet has bodies — or, open and not resolved yet, its VERTICES (the places the
    /// union's law shares).</summary>
    internal int Capacity => open && !enrolled ? (openPlaces > 0 ? openPlaces : ((Polygon)Figure).Vertices().Count) : Fleet.Count;
    /// <summary>How many golems an open formation was told to take (0: its vertices).</summary>
    internal int OpenPlaces => openPlaces;

    /// <summary>ENROLLED (propuesta 123): the union resolved and these are the MEMBERS — the union said who, the formation says where, as it
    /// always did: its own policy over its members' words. A formation that dances at once takes the union's words as its round (every copy has the
    /// same six) and opens this golem's route; one that WAITS (a cue, a pull) keeps only its members and convenes afresh when it starts. The other
    /// candidates are company (berths) on the same floor. A golem that is no member keeps nothing of it: its steps queued are dropped.</summary>
    internal void Enrol(Fleet members, IReadOnlyDictionary<string, (Position At, string Nonce)> words) => Enrol(members, words, null);

    /// <summary>The same, with WHERE THIS GOLEM STOOD AS IT KNOWS IT (22-oct-2026, night; lab: cyan woke facing 2.60 and its take asked `turnLeft 1.44`
    /// toward a heading of 1.44 — the route had opened from the WORD, a position with no heading, so it believed the body faced 0; from that turn on
    /// its belief and the truth parted by 2.6 rad, it said "placed" on a vertex its body never reached and crossed the others' ways): the route opens
    /// from the pose the golem knows (`g.Destination`, ajuste 67), heading included; the word, to the millimetre, stays what the law shares.</summary>
    internal void Enrol(Fleet members, IReadOnlyDictionary<string, (Position At, string Nonce)> words, Position from)
    {
        if (members == null) throw new GolemDomainException("Formation.Enrol: 'members' was not given");
        if (words == null) throw new GolemDomainException("Formation.Enrol: 'words' was not given");
        if (!open) throw new GolemDomainException($"{Name} is not open: its fleet was told");
        if (enrolled) throw new GolemDomainException($"{Name} was enrolled already");
        foreach (var name in members.Names) if (!words.ContainsKey(name)) throw new GolemDomainException($"{Name}: no word of {name}");
        Fleet = members;
        enrolled = true;
        indices.Clear();
        company.Clear();
        foreach (var (who, word) in words) if (!members.Names.Contains(who)) company.Add(new Peer(who, word.At));
        if (!Fleet.Has(golem))
        {
            if (union == null) { steps.Clear(); return; }    // not mine, loose: nothing of it waits here
            // not mine, of a union (propuesta 124): this copy keeps its score and follows it — the members' words, their places by the law, the movers
            // of the take — so the beats step it here too and the start of every beat waits for its movers as well
            foreach (var name in Fleet.Names) { stood[name] = Said(words[name].At); nonces[name] = words[name].Nonce ?? ""; }
            if (byDistance) Retable();
            movers = Fleet.Names.Where(n => !StandsOnItsPlace(n)).ToHashSet(StringComparer.Ordinal);
            foreach (var name in Fleet.Names) if (StandsOnItsPlace(name)) placed.Add(name);   // standing on its place already: placed, no word to wait for
            return;
        }
        if (onCue) return;                                    // it waits for its cue or its answer: its members convene afresh then
        foreach (var name in Fleet.Names) { stood[name] = Said(words[name].At); nonces[name] = words[name].Nonce ?? ""; }
        me = Fleet.Member(golem);
        convenedFrom = from ?? stood[me.Name];   // the pose the golem knows, with its heading; the word alone when none was given (the tests' shorthand)
        if (byDistance) Retable();
        if (IsComplete) Open();
    }
    /// <summary>Whether the asker's recurrence STILL ASKS (propuesta 122): what the <c>while</c> of the <c>remember</c> armed in the Follows act
    /// reads at every knock — true until the answer came (the cue), the formation was taken or dissolved. The same as <see cref="MayAsk"/>, named
    /// for what the engine reads.</summary>
    internal bool StillAsks => MayAsk;

    /// <summary>THE QUESTION (propuesta 118): "did tri-left finish?" — <c>asking.Ask()</c>, the body of the recurrence the asker armed with
    /// <c>remember</c> in its Follows act (propuesta 122): every activation is a command of the actor's own, so <c>echo-ask</c> observes it and
    /// tells <c>DidYouFinish</c> to the golem named. A lived fact: this golem asked. Refused when there is nothing to ask.</summary>
    internal void Ask()
    {
        if (!MayAsk) throw new GolemDomainException($"{Name}: nothing to ask — {(after == null ? "it starts after no formation" : cued ? "the answer came" : golem.Name == Asker ? "it was taken" : $"{Asker} asks, not {golem.Name}")}");
        asked++;
    }

    /// <summary>THE ANSWER "NOT YET" (propuesta 118): <c>formation.HeardNotYet();</c> — counted; the recurrence goes on asking (its while still holds).
    /// The answer "yes" is <see cref="TakeCue"/>.</summary>
    internal void HeardNotYet()
    {
        if (after == null) throw new GolemDomainException($"{Name} starts after no formation: no answer to hear");
        notYet++;
    }

    /// <summary>Whether this formation starts on a cue, and whether that cue came (or a member spoke).</summary>
    internal bool OnCue => onCue;
    internal bool IsCued => !onCue || cued;

    /// <summary>THE CUE CAME TO THIS GOLEM (propuesta 117): "continúas tú" — <c>formation.TakeCue(from, me);</c>, the uptake of <c>YourTurn</c>
    /// with this golem's name in it: its take. ONE verb that checks everything BEFORE it marks anything (lab 19-oct-2026: cyan, left in another
    /// scenario, had its copy marked cued and then the take refused for a place off that map — the cue consumed in memory, the act out of the
    /// journal): the formation on cue and not cued, the place of this golem on the map (by rank), then cued, then the Convene — the same word as
    /// a take, so <c>echo-stood</c> and <c>next-order-convene</c> see it (their patterns name this act too) and its team convenes in turn.
    /// Refused for a formation that does not start on cue, or already cued.</summary>
    internal Route TakeCue(Position from, Member member)
    {
        if (from == null) throw new GolemDomainException("Formation.TakeCue: 'from' was not given");
        if (member == null) throw new GolemDomainException("Formation.TakeCue: 'member' was not given");
        if (!onCue) throw new GolemDomainException($"{Name} does not start on cue");
        if (cued) throw new GolemDomainException($"{Name} was cued already");
        if (Dissolved) throw new GolemDomainException($"{Name} was dissolved");
        MustBeFree(member);
        if (!byDistance) golem.CheckPlace(Figure.Place(member), $"place {member.Rank + 1} of {member.Of} of the {Figure.Name}");   // refused before anything is marked
        cued = true;
        return Convene(from, member);
    }

    /// <summary>THIS GOLEM FINISHES ITS PART (propuesta 117): the last line of its script about the formation, <c>finish tri-left</c> — queued like
    /// everything else: <c>route = formation.Finish(me);</c> written with steps still to walk means "when what is queued is done, I finished"
    /// (<see cref="Finishing"/>; a step queued after it is walked first — the fleet may always move, 19-oct-2026); written — or written again by the clock (<see cref="FinishDue"/>) — once its part is done (<see cref="IsDone"/>:
    /// the route to its place complete, no step queued, placed), it COUNTS: its own word, <see cref="JustFinished"/> true for the act's expose, and
    /// the reaction on this act tells <c>FinishedAt</c> to the fleet. A peer's word is <see cref="HeardFinished"/>. The formation stays: a peer
    /// may still be walking its last step. Refused by a golem that is not of the fleet.</summary>
    internal Route Finish(Member member)
    {
        if (member == null) throw new GolemDomainException("Formation.Finish: 'member' was not given");
        if (Dissolved) throw new GolemDomainException($"{Name} was dissolved");
        if (golem.Name == null || member.Name != golem.Name) throw new GolemDomainException($"'{member.Name}' is not this golem: a peer's word is HeardFinished");
        if (!Fleet.Names.Contains(member.Name)) throw new GolemDomainException($"'{member.Name}' is not a member of the fleet of {Name}");
        if (finished.Contains(member.Name)) throw new GolemDomainException($"{member.Name} already finished its part of {Name}");
        finishing = true;
        JustFinished = IsDone;
        if (JustFinished) finished.Add(member.Name);
        return route;
    }

    /// <summary>THE FINISH IMPLIED BY THE SCORE (propuesta 124): a formation of a programmed union finishes where its part of the score ends — no
    /// <c>finish</c> line: this golem's word is due once its part is done (<see cref="FinishDue"/>, the clock writes it), a non-member's never.</summary>
    internal void FinishesAtTheEnd() => finishing = true;

    /// <summary>This golem's part is DONE: its route to its place complete, no step queued, standing on its place.</summary>
    internal bool IsDone => me != null && route != null && !route.IsPending() && !route.EndedShort && steps.Count == 0 && placed.Contains(me.Name);
    /// <summary>This golem asked to finish and its part is not done yet (or not counted yet): the word waits for the last step.</summary>
    internal bool Finishing => finishing && me != null && !finished.Contains(me.Name);
    /// <summary>The finish asked is due now: the part is done and the word not counted — the clock's question, answered by writing
    /// <see cref="Finish"/> again (<c>FinishIfDone</c>).</summary>
    internal bool FinishDue => !Dissolved && Finishing && IsDone;
    /// <summary>Whether the last <see cref="Finish"/> counted this golem's word — what its act exposes (<c>finishedNow</c>), so the fleet is told
    /// on the act that counted, never on the one that queued.</summary>
    internal bool JustFinished { get; private set; }

    /// <summary>A PEER FINISHED ITS PART (by tell, <c>FinishedAt</c>): <c>formation.HeardFinished(peer)</c>. Counted like this golem's own word;
    /// with every member's, the formation is finished and THE COPY THAT COUNTED THE LAST WORD gives the cue.</summary>
    internal Route HeardFinished(Member peer)
    {
        if (peer == null) throw new GolemDomainException("Formation.HeardFinished: 'peer' was not given");
        if (golem.Name != null && peer.Name == golem.Name) throw new GolemDomainException($"'{peer.Name}' is this golem: its own word is Finish");
        if (!Fleet.Names.Contains(peer.Name)) throw new GolemDomainException($"'{peer.Name}' is not a member of the fleet of {Name}");
        finished.Add(peer.Name);
        return route;
    }

    /// <summary>How many members said they finished their part.</summary>
    internal int FinishedCount => finished.Count;
    /// <summary>Every member of the fleet finished its part: the dance of this formation is over, as this copy knows it.</summary>
    internal bool IsFinished => !Dissolved && finished.SetEquals(Fleet.Names);
    /// <summary>Whether THIS golem has the cue to give (propuesta 117; Juan, 19-oct-2026: "que el último en salir de la coreografía sea el que diga
    /// realmente a yellow"): every member finished as this copy knows it, somebody to cue, the cue not given yet — THE LAST TO LEAVE is whoever
    /// counts the fleet's last finish: its own, or a peer's heard; the words carry no clock, so each copy judges by what it heard, and the cued
    /// golem takes the first word (the rest are dropped there). The clock asks it (the count completes inside an uptake, which no expose may
    /// observe, ajuste 77).</summary>
    internal bool Cueing => IsFinished && cueTo != null && !cueGiven;
    /// <summary>Whether this copy gave the cue already.</summary>
    internal bool CueGiven => cueGiven;

    /// <summary>THE CUE GIVEN (propuesta 117): "continúas tú" — <c>formation.Cue();</c>, written by the clock when <see cref="Cueing"/>; the reaction on
    /// this act tells <c>YourTurn</c> with the cued golem's name to every peer, and only the named one takes it as the take of the formation it
    /// waits in. Refused when there is no cue to give here.</summary>
    internal void Cue()
    {
        if (!Cueing) throw new GolemDomainException($"{Name}: no cue to give here — {finished.Count} of {Fleet.Count} finished, cue to '{CueTo}'");
        cueGiven = true;
    }
    /// <summary>This golem as a member of the fleet; null before it joined or convened.</summary>
    internal Member Me => me;
    /// <summary>The index, in the figure's fixed order of places, of the place this golem holds or heads to — by rank known from birth, by
    /// distance once the words are in; −1 before, and for a golem that is in no fleet.</summary>
    internal int PlaceIndex => golem.Name != null && indices.TryGetValue(golem.Name, out int i) ? i : -1;
    /// <summary>How many members said they stand on their place, this round.</summary>
    internal int PlacedCount => placed.Count;
    /// <summary>Whether this golem has a route in the formation yet — a word heard before it has one is only kept.</summary>
    internal bool HasRoute => route != null;
    /// <summary>Every member stands on its place, this round — what a beat of the union waits for (propuesta 124).</summary>
    internal bool AllPlaced => placed.SetEquals(Fleet.Names);
    /// <summary>Every mover of the round underway is lined up — the start of a beat of the union waits for it in every formation (propuesta 124).</summary>
    internal bool MoversAligned => movers.All(AlignedIn(Round).Contains);
    /// <summary>The union this formation follows; null for a loose one.</summary>
    internal Union Union => union;
    /// <summary>How many members said they are lined up for the round underway (propuesta 106).</summary>
    internal int AlignedCount => AlignedIn(Round).Count;
    private HashSet<string> AlignedIn(int round) => aligned.TryGetValue(round, out var set) ? set : aligned[round] = new HashSet<string>(StringComparer.Ordinal);
    /// <summary>This golem's route waits for the fleet's start and its own word is not recorded yet — what the arrival asks before writing
    /// <see cref="Aligned"/> (<c>Check(g.Choreography.Current.AwaitsMyWord)</c>).</summary>
    internal bool AwaitsMyWord => route != null && route.Waiting && me != null && !AlignedIn(Round).Contains(me.Name);
    /// <summary>Which ROUND the fleet is in: 0 while taking the formation, one more per step opened — the word "placed" of a round is
    /// not the word of the last (the once of the tell that spreads it carries the round, ajuste 79).</summary>
    internal int Round { get; private set; }
    /// <summary>How many steps wait in the queue.</summary>
    internal int Queued => steps.Count;
    /// <summary>Whether the next step may open now: this golem has its route, EVERY member said it stands on its place, and a step is queued.</summary>
    internal bool CanStep => union != null ? union.CanStep : route != null && steps.Count > 0 && placed.SetEquals(Fleet.Names);
    /// <summary>How many places the figure has for this fleet — more than the fleet when vertices stay free (ajuste 86).</summary>
    internal int PlaceCount => Figure.Places(Capacity).Count;

    /// <summary>WHO HOLDS WHAT, as this copy knows it (propuesta 104: the formations read): by rank every member from birth; by distance the
    /// members that spoke, by the table; after every step, where the step took them. In the fleet's order.</summary>
    internal IReadOnlyList<Holder> Holders()
    {
        var places = Figure.Places(Capacity);
        var holders = new List<Holder>();
        foreach (var name in Fleet.Names)
            if (indices.TryGetValue(name, out int index)) holders.Add(new Holder(name, index, places[index], Figure.OrbitOf(index, places.Count)));
        return holders;
    }

    /// <summary>Whether that member already said where it stood — this golem, once it convened.</summary>
    internal bool Knows(Member member)
    {
        if (member == null) throw new GolemDomainException("Formation.Knows: 'member' was not given");
        return stood.ContainsKey(member.Name);
    }

    /// <summary>This golem STANDS on its place and has not said so yet: its route reached the place and its own word is not recorded —
    /// what the arrival asks before writing <see cref="Placed"/> (<c>Check(g.Choreography.Current.Reached)</c>).</summary>
    internal bool Reached => route != null && IsReached(route) && !placed.Contains(me.Name);

    /// <summary>Whether that route is this golem's route to its place in this formation, and it reached it.</summary>
    internal bool IsReached(Route candidate)
    {
        if (candidate == null) throw new GolemDomainException("Formation.IsReached: 'candidate' was not given");
        return ReferenceEquals(candidate, route) && !route.IsPending() && !route.EndedShort;
    }

    /// <summary>THE FIRST LAW, with what is known: over every member that spoke, the pairs (member, place) from the shortest to the
    /// longest — a tie by name, then by place — each member and each place taken once. The place that member gets.</summary>
    internal Position PlaceOf(Member member)
    {
        if (member == null) throw new GolemDomainException("Formation.PlaceOf: 'member' was not given");
        if (!Table().TryGetValue(member.Name, out int index)) throw new GolemDomainException($"'{member.Name}' has not said where it stands in {Name}");
        return Figure.Places(Capacity)[index];
    }

    /// <summary>This golem JOINS BY RANK (propuesta 59, paso 1): the place of its rank among as many places as the fleet has bodies, and the
    /// route to it — <c>route = formation.Join(from, me);</c>. Once. The repertoire's, for the tests: the scripts convene.</summary>
    internal Route Join(Position from, Member member)
    {
        if (from == null) throw new GolemDomainException("Formation.Join: 'from' was not given");
        if (member == null) throw new GolemDomainException("Formation.Join: 'member' was not given");
        MustBeFree(member);
        me = member;
        stood[me.Name] = Said(from);
        indices[me.Name] = member.Rank;
        place = Figure.Place(member);
        route = golem.TakePlace(from, place, Figure.Facing(place), Berths(), $"place {me.Rank + 1} of {me.Of} of the {Figure.Name} at ({Fmt(place.X)}, {Fmt(place.Y)})");
        return route;
    }

    /// <summary>This golem CONVENES — once: it says where it stands (<c>formation.Convene(from, me)</c>). When with its word the
    /// round is complete, its route to its place opens and comes back (<see cref="Route"/> holds it); else nothing yet (null): the word
    /// that completes the round will open it (<see cref="Stood"/>). Ajuste 80: nobody sets out before everybody spoke.</summary>
    internal Route Convene(Position from, Member member)
    {
        if (from == null) throw new GolemDomainException("Formation.Convene: 'from' was not given");
        if (member == null) throw new GolemDomainException("Formation.Convene: 'member' was not given");
        if (Dissolved) throw new GolemDomainException($"{Name} was dissolved");
        if (open && !enrolled) throw new GolemDomainException($"{Name} is open: its union {unionName} convenes — take {unionName}");   // propuesta 123
        if (onCue && !cued) throw new GolemDomainException($"{Name} starts on cue: it waits for the word of the fleet before it");   // propuesta 117: its take is the cue
        MustBeFree(member);
        if (!byDistance) golem.CheckPlace(Figure.Place(member), $"place {member.Rank + 1} of {member.Of} of the {Figure.Name}");   // known at once by rank: refused now, not after the fleet waited
        me = member;
        convenedFrom = from;
        stood[me.Name] = Said(from);
        if (byDistance) Retable();
        return IsComplete ? Open() : null;
    }

    // The round is complete: this golem's place by the policy, and the route there from where it said it stood — the other members as
    // berths, so the way goes around them (ajuste 80).
    private Route Open()
    {
        place = Figure.Places(Capacity)[indices[me.Name]];
        // the take moves everybody — except a member that said it STANDS ON ITS PLACE already (a take again after a failure, 13-oct-2026: its route is
        // nothing to walk, so it never lines up, and the others waited for its word for ever): the fleet sets out together once every body that moves
        // faces its place (propuesta 106)
        movers = Fleet.Names.Where(n => !StandsOnItsPlace(n)).ToHashSet(StringComparer.Ordinal);
        return Take(convenedFrom, Berths(), Where(place));
    }

    // THE ROUTE TO THE PLACE — OR THE FACT THAT THERE IS NONE (19-oct-2026; lab 14-oct, the march by rps: green's word completed orange's round and
    // the take found "no road … past 1 marks"; the exception refused the whole uptake, orange had no route and was not stranded, and the five others
    // waited for its line-up for ever): the golem's take, starting together with the fleet (propuesta 106); refused — no road past the marks and the
    // berths, no room at the place — the route is BORN FAILED with that reason (Golem.FailToTake), so the word of the round is journaled whole, the
    // print says the route ended short and why, and the retake (propuesta 113) opens it again from where the body really stands, once.
    private Route Take(Position from, IReadOnlyList<Peer> berths, string where)
    {
        try { route = golem.TakePlace(from, place, Figure.Facing(place), berths, where); }
        catch (GolemDomainException ex) { route = golem.FailToTake(place, ex.Message); return route; }
        route.StartTogether();
        return route;
    }

    /// <summary>A WORD AS IT TRAVELS (13-oct-2026; lab, the hexagon wheel by distance: green stood 1.7503 m from two places alike, its own copy
    /// held its word at full precision while the other five held it as the tell carried it, to the millimetre — and green's copy gave green the
    /// place the others gave red; both went to it, touched, and were stranded): every word of the round, this golem's own among them, is kept
    /// to the MILLIMETRE, so every copy of the convocation reckons the law on the same numbers and a tie falls the same way in all of them.</summary>
    private static Position Said(Position at) =>
        new Position(Math.Round(at.X, 3, MidpointRounding.AwayFromZero), Math.Round(at.Y, 3, MidpointRounding.AwayFromZero));

    /// <summary>How near its place a member that said where it stands counts as standing ON it.</summary>
    internal const double OnPlaceTolerance = 0.3;

    private bool StandsOnItsPlace(string name) =>
        stood.TryGetValue(name, out var at) && indices.TryGetValue(name, out int index) && at.DistanceTo(Figure.Places(Capacity)[index]) <= OnPlaceTolerance;

    /// <summary>The OTHER members as bodies in the way (ajuste 80): where each said it stands and the place it gets — by rank its rank's,
    /// by distance the table's, once it spoke. What the golem plans around when it opens its route to its place.</summary>
    internal IReadOnlyList<Peer> Berths()
    {
        var berths = new List<Peer>();
        var places = Figure.Places(Capacity);
        foreach (var name in Fleet.Names)
        {
            if (me != null && name == me.Name) continue;
            if (stood.TryGetValue(name, out var at)) berths.Add(new Peer(name, at));
            if (indices.TryGetValue(name, out int index)) berths.Add(new Peer(name, places[index]));
        }
        berths.AddRange(company);   // the union's other candidates, where they stood (propuesta 123): bodies on the same floor
        return berths;
    }

    /// <summary>A PEER'S WORD — where it stood when it convened. Recorded. If with it the round is complete and this golem convened but
    /// has no route yet, its route opens and comes back (ajuste 80). Already on its way, by rank nothing moves it; by distance the law is
    /// applied again (a corrected word): when the peer took its place, with the route still pending — an order of it in the body — the
    /// route YIELDS (propuesta 74): it asks the body to stop, the new place is kept, and the route that replaces it opens once the body said
    /// where it stood (<see cref="Halted"/>); with the route completed, the pose of its last arrival is the truth and the new route opens
    /// at once from there. A route that ended short is not revived by a word; a word after a step changes nothing (the round of words is
    /// the formation's start). Before this golem convened, the word is only kept (null).</summary>
    internal Route Stood(Member peer, Position at) => Stood(peer, at, "");

    /// <summary>The same, with the STAMP the peer's word carried (propuesta 115): its hand at the game is drawn from it.</summary>
    internal Route Stood(Member peer, Position at, string nonce)
    {
        if (peer == null) throw new GolemDomainException("Formation.Stood: 'peer' was not given");
        if (at == null) throw new GolemDomainException("Formation.Stood: 'at' was not given");
        if (me != null && peer.Name == me.Name) throw new GolemDomainException($"'{peer.Name}' is this golem: its own word is its Convene");
        if (!Fleet.Names.Contains(peer.Name)) throw new GolemDomainException($"'{peer.Name}' is not a member of the fleet of {Name}");
        if (Round > 0) return route;
        if (open && !enrolled) return route;                      // open and not resolved yet: its union convenes, a word about it is nobody's (propuesta 123)
        cued = true;                                              // a member spoke: the cued one took it, the team follows (propuesta 117)
        stood[peer.Name] = Said(at);
        nonces[peer.Name] = nonce ?? "";
        if (byDistance) Retable();
        if (me == null) return null;
        if (route == null) return IsComplete ? Open() : null;
        if (!byDistance) return route;                            // by rank the place is the rank's: no word moves it
        if (route.EndedShort) return route;
        var now = Figure.Places(Capacity)[indices[me.Name]];
        if (now.DistanceTo(place) < 1e-6) return route;
        place = now;
        yieldedTo = peer.Name;
        if (route.IsPending() && !route.Waiting)
        {
            if (!route.Yielding) route.Yield();   // the body is carrying an order of it: it stops first and says where it stood
            return route;
        }
        if (route.IsPending()) route.Abandon($"{yieldedTo} took this place by distance: {me.Name} goes to ({Fmt(place.X)}, {Fmt(place.Y)})");   // waiting, lined up, the body stands: nothing to halt (propuesta 106)
        else placed.Remove(me.Name);              // it leaves the place it had reached
        Take(golem.Destination, Berths(), Where(place));   // from where the body is (13-oct-2026): the place it had reached, or wherever an errand took it since; the same round: the others' line-up words stay counted, so it sets out as soon as its own body is lined up
        yieldedTo = null;
        return route;
    }

    /// <summary>TAKEN AGAIN BY THE WARDEN (22-oct-2026, night; Juan: "que un miembro varado no deje al sindicato esperando"): a formation STRANDED for
    /// this golem — its route ended short twice, or no road fit (propuesta 113) — is taken again on the warden's word, `take <name>` / `take <union>`,
    /// from where the body really stands: the stranding is forgotten, the retake spent no more, and the route to the same place opens, starting
    /// together with the round (its line-up word is what the others wait for). Refused by its route the same way: stranded again, said in the read.</summary>
    internal Route TakeAgain(Pose me)
    {
        if (me == null) throw new GolemDomainException("Formation.TakeAgain: 'me' was not given");
        if (Dissolved) throw new GolemDomainException($"{Name} was dissolved");
        if (this.me == null || place == null) throw new GolemDomainException($"{Name}: this golem has no place to take again");
        if (!Stranded) throw new GolemDomainException($"{Name} is not stranded for {this.me.Name}: nothing to take again");
        Stranded = false;
        StrandedWhy = "";
        retakes = 0;
        try
        {
            route = golem.TakePlace(me, place, Figure.Facing(place), Berths(), Where(place));
            route.StartTogether();
        }
        catch (GolemDomainException ex)
        {
            Stranded = true;
            StrandedWhy = $"{this.me.Name} could not reach ({Fmt(place.X)}, {Fmt(place.Y)}) from ({Fmt(me.X)}, {Fmt(me.Y)}): {ex.Message}";
        }
        return route;
    }

    /// <summary>The body STOPPED for the route that yields, and says where it stood (propuesta 74): that route is abandoned in the words
    /// of the peer that took its place, and the route that replaces it opens from where the body really is, to the place kept —
    /// <c>route = formation.Halted(me)</c>. Refused when this golem's route does not yield.</summary>
    /// <summary>Whether this golem's route in the formation ENDED SHORT — failed, or abandoned by a touch's correction that found no road — and
    /// the retake is to be decided (propuesta 113, 13-oct-2026; Juan, on a triangle of 2 m where two members touched on their way and their
    /// routes failed, the fleet then waiting for ever: "que la formación la vuelva a abrir desde donde quedó el cuerpo").</summary>
    internal bool NeedsRetake => !Dissolved && !Stranded && me != null && place != null && route != null && !route.IsPending() && route.EndedShort;

    /// <summary>This golem could not reach its place — its route failed twice in the round: the formation says so, and the warden decides
    /// (take again, move the figure, dissolve).</summary>
    internal bool Stranded { get; private set; }
    internal string StrandedWhy { get; private set; } = "";

    /// <summary>THE RETAKE (propuesta 113): this golem's route to its place ended short — the golem opens it AGAIN BY ITSELF, from where its
    /// body really stands (telemetry, like the hold), to the same place it holds in this copy: <c>route = formation.Retake(me)</c>; the same
    /// round, so it sets out as soon as its own body is lined up (propuesta 106). ONCE: a second failure, or no road from where it stands,
    /// leaves the formation STRANDED for this golem and the failed route as it is — nothing moves in silence.</summary>
    internal Route Retake(Pose me)
    {
        if (me == null) throw new GolemDomainException("Formation.Retake: 'me' was not given");
        if (!NeedsRetake) throw new GolemDomainException($"no route of {Name} ended short: nothing to take again");
        if (retakes >= 1)
        {
            Stranded = true;
            StrandedWhy = $"{this.me.Name} could not reach ({Fmt(place.X)}, {Fmt(place.Y)}) twice: {route.Why}";
            return route;
        }
        retakes++;
        try
        {
            route = golem.TakePlace(me, place, Figure.Facing(place), Berths(), Where(place));
            route.StartTogether();                // the same round: it sets out as soon as its own body is lined up (propuesta 106)
        }
        catch (GolemDomainException ex)
        {
            Stranded = true;
            StrandedWhy = $"{this.me.Name} could not reach ({Fmt(place.X)}, {Fmt(place.Y)}) again from ({Fmt(me.X)}, {Fmt(me.Y)}): {ex.Message}";
        }
        return route;
    }

    internal Route Halted(Pose me)
    {
        if (me == null) throw new GolemDomainException("Formation.Halted: 'me' was not given");
        if (route == null || !route.Yielding || !route.IsPending()) throw new GolemDomainException($"no route of {Name} yields its place: nothing to halt");
        route.Abandon($"{yieldedTo} took this place by distance: {this.me.Name} goes to ({Fmt(place.X)}, {Fmt(place.Y)})");
        Take(me, Berths(), Where(place));         // the same round: it sets out as soon as its own body is lined up (propuesta 106)
        yieldedTo = null;
        return route;
    }

    /// <summary>THIS GOLEM STANDS ON ITS PLACE (ajuste 77) — its own word, written by the arrival when its route reached the place:
    /// <c>route = formation.Placed(me)</c>. Recorded for this round; when with it EVERY member is placed and a step is queued, the next step
    /// opens and its route comes back (<see cref="Step"/>); else the route in place comes back, so the print is the same as ever. Told to
    /// every peer by the reaction on this act — so a peer's word is another act, <see cref="Heard"/>, and the reaction never meets it.</summary>
    internal Route Placed(Member member)
    {
        if (member == null) throw new GolemDomainException("Formation.Placed: 'member' was not given");
        if (me == null || member.Name != me.Name) throw new GolemDomainException($"'{member.Name}' is not this golem: a peer's word is Heard");
        return Record(member);
    }

    /// <summary>A PEER'S WORD — it stands on its place (by tell, <c>PlacedAt</c>): <c>route = formation.Heard(peer)</c>. Recorded like this
    /// golem's own; the step opens the same way when the round is complete. Before this golem has a route, the word is only kept (null
    /// comes back).</summary>
    internal Route Heard(Member peer)
    {
        if (peer == null) throw new GolemDomainException("Formation.Heard: 'peer' was not given");
        if (me != null && peer.Name == me.Name) throw new GolemDomainException($"'{peer.Name}' is this golem: its own word is Placed");
        return Record(peer);
    }

    /// <summary>THIS GOLEM IS LINED UP for the round underway (propuesta 106, 9-oct-2026; Juan: "que todos se sincronicen en posición de salida y
    /// avancen a la vez"; then: "cuando tienen que moverse todos a un nuevo centro… todos deberían moverse juntos si ya están en el ángulo
    /// correcto"): its body turned to face its place and stands — <c>route = formation.Aligned(me)</c>, written by the arrival of that turn —
    /// on the take and on every step. Told to every peer by the reaction on this act (<c>AlignedAt</c>, with the round); a peer's word is
    /// <see cref="HeardAligned(Member, int)"/>. When every mover of the round is lined up, this copy lets its route go (<see cref="Route.Go"/>):
    /// the print turns from stop to advance, and the fleet sets out within the tells' latency of each other.</summary>
    internal Route Aligned(Member member)
    {
        if (member == null) throw new GolemDomainException("Formation.Aligned: 'member' was not given");
        if (me == null || member.Name != me.Name) throw new GolemDomainException($"'{member.Name}' is not this golem: a peer's word is HeardAligned");
        if (route == null || !route.Waiting) throw new GolemDomainException($"'{Name}': this golem's route is not waiting for the fleet's start");
        return RecordAligned(member, Round);
    }

    /// <summary>A PEER'S WORD — it is lined up for that round (by tell, <c>AlignedAt</c>, which carries the round): <c>route =
    /// formation.HeardAligned(peer, @round)</c>. Kept in its round even before this copy reached it (the words travel faster than the last
    /// placed one, at times); the start is given the same way. The words of a round stay until the formation leaves it, so a route opened
    /// again in the same round (by distance, a corrected word) starts as soon as its own body is lined up.</summary>
    internal Route HeardAligned(Member peer, int round)
    {
        if (peer == null) throw new GolemDomainException("Formation.HeardAligned: 'peer' was not given");
        if (me != null && peer.Name == me.Name) throw new GolemDomainException($"'{peer.Name}' is this golem: its own word is Aligned");
        if (round < 0) throw new GolemDomainException($"Formation.HeardAligned: a round is never negative ({round})");
        return RecordAligned(peer, round);
    }

    /// <summary>A peer's word for the round underway (the tests' shorthand).</summary>
    internal Route HeardAligned(Member peer) => HeardAligned(peer, Round);

    private Route RecordAligned(Member member, int round)
    {
        if (!Fleet.Names.Contains(member.Name)) throw new GolemDomainException($"'{member.Name}' is not a member of the fleet of {Name}");
        AlignedIn(round).Add(member.Name);
        if (union != null) return union.LetGoIfAligned();   // THE BEAT'S START (propuesta 124): every mover of every formation of the union is lined up
        if (route != null && route.Waiting && round == Round && movers.All(AlignedIn(Round).Contains)) route.Go();   // THE FLEET'S START: everybody who moves is lined up
        return route;
    }

    private Route Record(Member member)
    {
        if (!Fleet.Names.Contains(member.Name)) throw new GolemDomainException($"'{member.Name}' is not a member of the fleet of {Name}");
        placed.Add(member.Name);
        if (me != null && member.Name == me.Name) { retakes = 0; Stranded = false; StrandedWhy = ""; }   // placed: the retake of this round is spent no more (propuesta 113)
        // placed implies lined up (13-oct-2026): a body standing on its place needs no line-up turn, and its word of being there is at least as
        // strong — so a route waiting for the fleet's start never waits for one that already arrived (a take again with a member already in place)
        AlignedIn(Round).Add(member.Name);
        if (union != null)
        {
            // of a union (propuesta 124): the start and the beat are the union's — the word may be about another formation of it
            union.LetGoIfAligned();
            return union.CanStep ? union.Step() : union.Route;
        }
        if (route != null && route.Waiting && movers.All(AlignedIn(Round).Contains)) route.Go();
        if (route == null) return null;
        return CanStep ? Step() : route;
    }

    /// <summary>A MOVE is queued (ajuste 77; Juan: "una lista de formación/coreografías para cumplir todas"; ajuste 84: the move is the
    /// FIGURE's — <c>move = formation.Figure.Rotate(@sense, @ring); formation.Queue(move, @stepId);</c>): every body does it once everybody
    /// stands on its place. Nothing moves here. The step's id is the once of the tell that spreads it: the same step twice is refused. How
    /// many wait comes back.</summary>
    internal int Queue(Move move, string stepId)
    {
        if (move == null) throw new GolemDomainException("Formation.Queue: 'move' was not given");
        if (string.IsNullOrWhiteSpace(stepId)) throw new GolemDomainException("Formation.Queue: 'stepId' was not given");
        if (Dissolved) throw new GolemDomainException($"{Name} was dissolved");
        if (open && enrolled && !Fleet.Has(golem) && union == null) throw new GolemDomainException($"{golem.Name} is no member of {Name}: the union {unionName} placed it elsewhere");   // propuesta 123; of a union every copy keeps the whole score (propuesta 124)
        if (!stepIds.Add(stepId)) throw new GolemDomainException($"the step {stepId} is already queued in {Name}");
        // a step AFTER the finish is allowed (19-oct-2026; Juan: "siempre debería permitir moverse"): a finish not counted yet counts when the queue
        // empties, this step included; a finish already counted stays counted — the cue given, the pull answered — and the fleet keeps dancing
        steps.Enqueue((move, stepId));
        return steps.Count;
    }

    /// <summary>The next step OPENS (ajuste 77): every member stands on its place and a move waits — every member the move touches heads to
    /// the next place along its orbit (<see cref="Move.Next"/>: the figure's own geometry, ajuste 84), this golem FROM WHERE ITS BODY STANDS
    /// (<see cref="Golem.Destination"/>; 13-oct-2026, Juan: "si le digo visit a blue estando en una posición de formación, cuando le doy rotate no
    /// se posiciona donde tiene que estar" — the step was planned from where the route in place had left the body, so a golem sent elsewhere
    /// in between turned and advanced as if it still stood on its vertex; now it comes back to the figure, to the place the step gives it);
    /// a member of a ring the move leaves alone stays placed. A new round begins. <c>route = formation.Step();</c>. Refused when it cannot step yet.</summary>
    internal Route Step() => union != null ? union.Step() : StepAlone();

    /// <summary>The step of THIS formation alone — what <see cref="Step"/> is for a loose formation, and what a beat of the union does to each of its
    /// formations with a step queued (propuesta 124); in a copy that is no member of it the holders shift and nothing opens (null).</summary>
    internal Route StepAlone()
    {
        if (route == null && union == null) throw new GolemDomainException($"'{Name}': this golem has no place in the formation yet");
        if (steps.Count == 0) throw new GolemDomainException($"'{Name}': no step is queued");
        if (!placed.SetEquals(Fleet.Names)) throw new GolemDomainException($"'{Name}': {placed.Count} of {Fleet.Count} stand on their places — the step waits for everybody");
        var (move, _) = steps.Dequeue();
        if (move.IsRest) return route;                   // THE REST (propuesta 124): this beat the formation stays — placed, nobody moves, the round goes on
        var places = Figure.Places(Capacity);           // the figure's places — more than the fleet when vertices stay free (ajuste 86): the hole moves with everybody
        var orbits = Figure.Orbits(places.Count);
        var staying = new List<string>();
        foreach (var name in indices.Keys.ToList())
        {
            int orbit = Figure.OrbitOf(indices[name], places.Count);
            if (!move.Moves(orbit) || orbits[orbit].Count == 1) { staying.Add(name); continue; }   // an orbit of one place (a propeller's centre, propuesta 114) has nowhere to go: its body stays placed
            indices[name] = move.Next(indices[name], orbits[orbit].Start, orbits[orbit].Count);
        }
        placed.Clear();
        foreach (var name in staying) placed.Add(name);      // a ring the move leaves alone is still in place
        movers = indices.Keys.Where(name => !staying.Contains(name)).ToHashSet(StringComparer.Ordinal);   // whose line-up the start waits for (propuesta 106)
        aligned.Remove(Round);                                // the words of the round left behind
        Round++;
        if (me == null) return null;                          // a formation of my union I am no member of (propuesta 124): its holders shifted, nothing of mine opens
        if (staying.Contains(me.Name)) return route;          // this golem does not move on this step: its route in place stands
        int next = indices[me.Name];
        place = places[next];
        // in lockstep: no berths, the one ahead leaves as I come; from where the body is, wherever an errand took it; the body turns to face its
        // next place, then stands until every mover is lined up (propuesta 106)
        return Take(golem.Destination, Array.Empty<Peer>(), $"place {next + 1} of {places.Count} of the {Figure.Name}, {move.Name}, at ({Fmt(place.X)}, {Fmt(place.Y)})");
    }

    /// <summary>DISSOLVED by the warden (propuesta 104): a route of this golem still pending is abandoned, nothing is queued any more, and
    /// the formation leaves the golem's list. Journaled like everything, so the history keeps what the fleet had formed.</summary>
    internal void Dissolve()
    {
        if (Dissolved) return;
        Dissolved = true;
        if (route != null && route.IsPending()) route.Abandon($"{Name} was dissolved");
        steps.Clear();
    }

    // by distance: the table of who gets what, over every member that spoke (the first law), kept as the indices of the ones who spoke
    private void Retable()
    {
        foreach (var pair in Table()) indices[pair.Key] = pair.Value;
        Settle();
    }

    /// <summary>THE DISPUTES of this golem in this formation (14-oct-2026), by distance, once every member spoke: derived from the words and the
    /// law at every table, never written. By rank there are none.</summary>
    internal IReadOnlyList<Dispute> Disputes => disputes;

    /// <summary>THE DISPUTES FORGOTTEN (14-oct-2026; Juan: "algún botón para limpiar las disputas"): this copy's disputes go, and it settles no more —
    /// a formation told again is a new copy and brings its own. Written, so a golem reborn forgets them too.</summary>
    internal void ForgetDisputes()
    {
        disputes.Clear();
        disputesForgotten = true;
    }

    // who disputed what: every member wants the place nearest to where it said it stood (a tie, the lower number); the law's table says who got it
    private void Settle()
    {
        disputes.Clear();
        if (disputesForgotten || !byDistance || me == null || !IsComplete || !indices.TryGetValue(me.Name, out int mine)) return;
        var places = Figure.Places(Capacity);
        if (policy == Policy.RockPaperScissors)
        {
            // the game's matches this golem played: won or lost, against whom, the throw that decided
            foreach (var m in matches.Where(m => m.Winner == me.Name || m.Loser == me.Name))
            {
                bool won = m.Winner == me.Name;
                string rival = won ? m.Loser : m.Winner;
                disputes.Add(new Dispute(Name, m.Place, places[m.Place], rival, won, stood[me.Name].DistanceTo(places[m.Place]), stood[rival].DistanceTo(places[m.Place]), m.Game));
            }
            return;
        }
        int Nearest(string who)
        {
            int best = 0;
            for (int i = 1; i < places.Count; i++) if (stood[who].DistanceTo(places[i]) < stood[who].DistanceTo(places[best])) best = i;
            return best;
        }
        string HolderOf(int place) => indices.Where(kv => kv.Value == place).Select(kv => kv.Key).FirstOrDefault();
        int wanted = Nearest(me.Name);
        if (wanted != mine && HolderOf(wanted) is string winner)
            disputes.Add(new Dispute(Name, wanted, places[wanted], winner, false, stood[me.Name].DistanceTo(places[wanted]), stood[winner].DistanceTo(places[wanted])));
        foreach (var who in Fleet.Names)
            if (who != me.Name && stood.ContainsKey(who) && Nearest(who) == mine)
                disputes.Add(new Dispute(Name, mine, places[mine], who, true, stood[me.Name].DistanceTo(places[mine]), stood[who].DistanceTo(places[mine])));
    }

    private void MustBeFree(Member member)
    {
        if (me != null) throw new GolemDomainException($"'{me.Name}' already took its place in {Name}");
        if (member.Of != Fleet.Count || member.Rank >= Fleet.Names.Count || Fleet.Names[member.Rank] != member.Name)
            throw new GolemDomainException($"'{member.Name}' is not a member of the fleet of {Name}");
    }

    private IReadOnlyDictionary<string, int> Table() => policy == Policy.RockPaperScissors ? GameTable() : NearestTable();

    // THE GAME (propuesta 115): every member who spoke wants the FREE place nearest to where it stood; a place wanted by one is its; a place
    // wanted by several is played — the contenders by name, the first challenged by the next and the winner going on — and the losers want
    // their next free place, until every member who spoke has one
    private IReadOnlyDictionary<string, int> GameTable()
    {
        matches.Clear();
        var places = Figure.Places(Capacity);
        var free = Enumerable.Range(0, places.Count).ToList();
        var table = new Dictionary<string, int>();
        var waiting = stood.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
        var cursor = waiting.ToDictionary(n => n, _ => 0, StringComparer.Ordinal);
        while (waiting.Count > 0 && free.Count > 0)
        {
            var wants = waiting.ToDictionary(n => n, n => free.OrderBy(i => stood[n].DistanceTo(places[i])).ThenBy(i => i).First(), StringComparer.Ordinal);
            foreach (var group in wants.GroupBy(w => w.Value).OrderBy(g => g.Key))
            {
                var contenders = group.Select(w => w.Key).OrderBy(n => n, StringComparer.Ordinal).ToList();
                string champion = contenders[0];
                foreach (var challenger in contenders.Skip(1)) champion = Play(champion, challenger, group.Key, places[group.Key], cursor);
                table[champion] = group.Key;
                free.Remove(group.Key);
                waiting.Remove(champion);
            }
        }
        return table;
    }

    // one match over a place: each throws its next throw until one beats the other; nine ties, the nearer wins, then the name
    private string Play(string a, string b, int placeIndex, Position at, Dictionary<string, int> cursor)
    {
        string handA = HandOf(a), handB = HandOf(b);
        int ties = 0;
        for (int k = 0; k < Throws.Length; k++)
        {
            char ta = handA[cursor[a]++ % Throws.Length], tb = handB[cursor[b]++ % Throws.Length];
            if (ta == tb) { ties++; continue; }
            bool aWins = Throws.Beats(ta, tb);
            string game = $"{Throws.Word(aWins ? ta : tb)} beats {Throws.Word(aWins ? tb : ta)}{(ties > 0 ? $" after {ties} tie{(ties > 1 ? "s" : "")}" : "")}";
            matches.Add((placeIndex, aWins ? a : b, aWins ? b : a, game));
            return aWins ? a : b;
        }
        double da = stood[a].DistanceTo(at), db = stood[b].DistanceTo(at);
        string nearer = da < db || (da == db && string.CompareOrdinal(a, b) < 0) ? a : b;
        matches.Add((placeIndex, nearer, nearer == a ? b : a, $"{Throws.Length} ties: the nearer keeps it"));
        return nearer;
    }

    // a member's hand: this golem's from its own stamp, a peer's from the stamp its word carried
    private string HandOf(string who) => Throws.Of(who == golem.Name ? Stamp : nonces.TryGetValue(who, out var nonce) ? nonce : "", who);

    private IReadOnlyDictionary<string, int> NearestTable()
    {
        var places = Figure.Places(Capacity);
        var pairs = new List<(double Distance, string Name, int Place)>();
        foreach (var said in stood)
            for (int i = 0; i < places.Count; i++) pairs.Add((said.Value.DistanceTo(places[i]), said.Key, i));
        pairs.Sort((a, b) => a.Distance != b.Distance ? a.Distance.CompareTo(b.Distance)
                           : string.CompareOrdinal(a.Name, b.Name) != 0 ? string.CompareOrdinal(a.Name, b.Name)
                           : a.Place.CompareTo(b.Place));
        var taken = new HashSet<int>();
        var table = new Dictionary<string, int>();
        foreach (var pair in pairs)
        {
            if (table.ContainsKey(pair.Name) || taken.Contains(pair.Place)) continue;
            table[pair.Name] = pair.Place;
            taken.Add(pair.Place);
        }
        return table;
    }

    private string Where(Position at) => $"the place of {me.Name} in {Name} by {Policy} at ({Fmt(at.X)}, {Fmt(at.Y)})";
    private static string Fmt(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
}
