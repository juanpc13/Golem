using Choreography.Theater;
using Choreography.Told;
using Choreography.Transport.Brokered;
using GolemAPI.Membrane;
using GolemAPI.Panel;
using Puppeteer;

using GolemAPI.Choreography.Roles;

namespace GolemAPI.Choreography;

// What the golem SAYS to its peers and what it takes up from them. Speech is a Reaction, never a command (paper 04):
// a tell asserts a lived fact, past tense, when the act that made it lands in the journal. A reaction captures no
// object variable, but it captures the @params of the act — and of the object built beside it: a constructor pattern
// next to the act casts the `Pose(@…)` of the same entry (18-sep-2026, lab-nested.txt) — so the acts expose ONLY what
// they do not contain (the golem's name; whether the arrival was a stop). The uptakes bind the peers' tells to the
// scripts GolemEmbodiment declares.
public sealed class GolemSpeech
{
    private readonly PerformanceV2 performance;
    private readonly ActorV2 golemActor;
    private readonly IMessageBroker wire;
    private readonly PanelFeed feed;
    private readonly string golem;
    private readonly string tellDoneTo;
    private readonly IReadOnlyList<string> peers;
    private readonly TellBindingTable bindings = new();
    private ToldListener toldListener;

    public GolemSpeech(PerformanceV2 performance, IMessageBroker wire, PanelFeed feed, string golem, string tellDoneTo, IReadOnlyList<string> peers)
    {
        this.performance = performance;
        this.golemActor = performance.Actor;
        this.wire = wire;
        this.feed = feed;
        this.golem = golem;
        this.tellDoneTo = tellDoneTo;
        this.peers = peers;
    }

    // BEFORE performance.Start(): the tell transport and every Reaction. Start is what arms a .Cue() reaction.
    public void DefineReactions()
    {
        bindings.Bind(golem, $"tell-{golem}");
        foreach (var peer in peers.Union(tellDoneTo == null ? Array.Empty<string>() : new[] { tellDoneTo }))
            bindings.Bind(peer, $"tell-{peer}");
        performance.UseTellTransport(new BrokerTellTransport(wire, bindings, golem));

        if (peers.Count > 0)
        {
            // The touch itself, in the body's words — where it stood, facing which way, where on its shell — and my name:
            // the pose is captured from the `me = Pose(…)` built beside the act, the bearing from the act's own argument,
            // and only the name is exposed (the journal's identity, in no act). One tell per peer in one entry (several
            // statements: no single-tell elision), one once-id per addressee.
            string bumped = string.Join("\n", peers.Select(p => $@"
                tell BumpedAt with @bodyX, @bodyY, @bodyHeading, @bearing, @who, @scenario
                    to {p}
                    once 'bump-' + @who + '-' + @bodyX + ',' + @bodyY + ',' + @bearing + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-bumped")
                .Cue().Company().WithSharedHydration()
                .Seek("Bumped").One()
                    .OnMatch("Pose($bodyX, $bodyY, $bodyHeading) [_:Golem].Bump(_, $bearing) expose $who who, $scenario scenario;")
                .Causation.Continue(bumped);

            // Somebody took a thing away: the fleet must forget it together, or one golem would keep skirting what
            // another can already drive through.
            string forgotten = string.Join("\n", peers.Select(p => $@"
                tell ObstacleGone with @x, @y
                    to {p}
                    once 'gone-{golem}-' + @x + ',' + @y + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-forgotten")
                .Cue().Company().WithSharedHydration()
                .Seek("Forgotten").One()
                    .OnMatch("Position($x, $y) [_:Golem].Forget(_)")   // the point built beside the act: no expose at all
                .Causation.Continue(forgotten);

            // THE CALL (ajustes 73, 80, both policies): a golem that convened tells every peer where it stood — its act's expose, the position read from
            // the domain (expose takes any primitive expression). The peer that had not convened convenes itself and tells in turn; each
            // golem convenes once, so the word spreads with no loop. The once is per call, teller and peer.
            // NO FormedAs, FormedRingsAs, RotateTo since propuesta 112 (13-oct-2026; Juan: "ya no será el golem blue haciendo tell para pasarles la
            // coreografía: cuando se cree la formación todos los golems sabrán cuál es la formación a la que pertenecen porque se mandó a crear"): the
            // warden tells form, take and rotate to every golem of the fleet, one line on each tab; the words of the round (StoodFor, PlacedAt,
            // AlignedAt) and the dissolution stay: lived facts between the members, never an operator's order relayed
            // A PLAY is no longer told by tell (16-oct-2026; Juan: "este comando también debería viajar a los otros tabs de los golems involucrados… la única
            // cosa que pasa por el tell cuando está ejecutándose la dinámica de pacman es el bump"): the warden writes `play …` on every member's tab,
            // each line naming that golem's part; StagedAs, echo-staged and its uptake are gone, like FormedAs (propuesta 112)
            // PACMAN RUNS (propuesta 108, fase 3): every run told to every peer — the play, its stamp, which run and the decision cell it goes to;
            // a ghost's copy keeps where pacman was seen and chases it (UptakeRanTo); the once is per run and peer. The act is the host's (Release,
            // Ran), never an uptake's, so its expose is read.
            string ran = string.Join("\n", peers.Select(p => $@"
                tell RanTo with @title, @nonce, @runs, @cellX, @cellY
                    to {p}
                    once 'ran-' + @nonce + '-' + @runs + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-ran")
                .Cue().Company().WithSharedHydration()
                .Seek("Ran").One()
                    .OnMatch("[_:Dynamics].Run(_, _) expose $title title, $nonce nonce, $runs runs, $cellX cellX, $cellY cellY;")
                .Causation.Continue(ran);
            // GAME OVER (propuesta 108, fase 4): the end of the play, concluded in this copy (a ghost and pacman met; the warden's `play over`)
            // and announced by the clock's act — never the uptake's (an uptake-written act cannot be exposed): every peer is told who ended it
            string over = string.Join("\n", peers.Select(p => $@"
                tell GameOver with @title, @nonce, @who
                    to {p}
                    once 'over-' + @nonce + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-over")
                .Cue().Company().WithSharedHydration()
                .Seek("Over").One()
                    .OnMatch("[_:Play].Announce() expose $title title, $nonce nonce, $who who;")
                .Causation.Continue(over);
            // THE TAKE (ajustes 73, 80, 81; propuesta 104): the golem said where it stands into the named formation — every peer is told the name,
            // the stamp, who and where, and convenes in its own copy
            string stood = string.Join("\n", peers.Select(p => $@"
                tell StoodFor with @call, @nonce, @who, @stoodX, @stoodY
                    to {p}
                    once 'stood-' + @nonce + '-' + @who + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-stood")
                .Cue().Company().WithSharedHydration()
                .Seek("Stood").One()
                    .OnMatch("[_:Formation].Convene(_, _) expose $call call, $nonce nonce, $who who, $stoodX stoodX, $stoodY stoodY;")   // the act on the formation (ajuste 81)
                .Causation.Continue(stood);
            golemActor.Reactions.DefineReaction("echo-stood-cued")   // the take by cue (propuesta 117): the same word to the fleet
                .Cue().Company().WithSharedHydration()
                .Seek("Stood").One()
                    .OnMatch("[_:Formation].TakeCue(_, _) expose $call call, $nonce nonce, $who who, $stoodX stoodX, $stoodY stoodY;")
                .Causation.Continue(stood);
            // THE FORMATION IN PLACE (ajuste 77): a golem that reached its place says so to every peer — the arrival's act exposes who and
            // which formation (the peer's own Placed, written by its uptake, carries no expose: one hop) — and a step asked of one golem is told
            // to every peer, who queues the same step; the once is per step and peer.
            string placed = string.Join("\n", peers.Select(p => $@"
                tell PlacedAt with @call, @who
                    to {p}
                    once 'placed-' + @nonce + '-' + @round + '-' + @who + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-placed")
                .Cue().Company().WithSharedHydration()
                .Seek("Placed").One()
                    .OnMatch("[_:Formation].Placed(_) expose $who who, $call call, $nonce nonce, $round round;")   // the round in the once: a word per round, not one per call
                .Causation.Continue(placed);
            // THE START TOGETHER (propuesta 106): a golem lined up for the step says so to every peer; the once is per round and peer
            string aligned = string.Join("\n", peers.Select(p => $@"
                tell AlignedAt with @call, @who, @round
                    to {p}
                    once 'aligned-' + @nonce + '-' + @round + '-' + @who + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-aligned")
                .Cue().Company().WithSharedHydration()
                .Seek("Aligned").One()
                    .OnMatch("[_:Formation].Aligned(_) expose $who who, $call call, $nonce nonce, $round round;")
                .Causation.Continue(aligned);
            string dissolved = string.Join("\n", peers.Select(p => $@"
                tell Dissolved with @call
                    to {p}
                    once 'dissolved-' + @nonce + '-{p}';"));
            // THE CUE BETWEEN DANCERS (propuesta 117, 19-oct-2026): a member whose part is done says it finished (the act that COUNTS exposes
            // finishedNow true; the one that only queued the finish does not) — the fleet hears it; and the LAST OF THE FLEET, every member finished,
            // gives the cue to ONE golem by name — the act the clock writes (CueIfFinished: the count completes inside an uptake, which no expose may
            // observe) — told to every peer with that name inside, since a tell goes to a fixed route (the name as a literal, ajuste 71): only the
            // named one takes it, as the take of the formation it waits in
            string finished = string.Join("\n", peers.Select(p => $@"
                tell FinishedAt with @call, @who
                    to {p}
                    once 'finished-' + @nonce + '-' + @who + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-finished")
                .Cue().Company().WithSharedHydration()
                .Seek("Finished").One()
                    .OnMatch("[_:Formation].Finish(_) expose $call call, $nonce nonce, $who who, true finishedNow;")
                .Causation.Continue(finished);
            // the cue: the copy that counted the fleet's last finish (Juan: "el último en salir") — the act the clock writes (CueIfFinished), told to
            // every peer with the cued golem's name inside; only the named one takes it, the first word standing
            string yourTurn = string.Join("\n", peers.Select(p => $@"
                tell YourTurn with @to
                    to {p}
                    once 'cue-' + @nonce + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-cue")
                .Cue().Company().WithSharedHydration()
                .Seek("Cue").One()
                    .OnMatch("[_:Formation].Cue() expose $call call, $nonce nonce, $to to;")
                .Causation.Continue(yourTurn);
            // THE PULL (propuesta 118, 20-oct-2026): the asker's question and the asked one's answer, two tells — both acts written by the actor itself
            // (the activation of the asker's `remember`, propuesta 122, and AnswerIfAsked on the clock), so no uptake stands between the fact and the word; both to every peer with the name inside, the
            // named one taking it
            string ask = string.Join("\n", peers.Select(p => $@"
                tell DidYouFinish with @call, @to, @who
                    to {p}
                    once 'ask-' + @nonce + '-' + @asked + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-ask")
                .Cue().Company().WithSharedHydration()
                .Seek("Asked").One()
                    .OnMatch("[_:Formation].Ask() expose $call call, $nonce nonce, $to to, $who who, $asked asked;")   // an activation of the asker's recurrence (propuesta 122): the question asked
                .Causation.Continue(ask);
            string answer = string.Join("\n", peers.Select(p => $@"
                tell FinishedState with @call, @to, @done
                    to {p}
                    once 'answer-' + @call + '-' + @to + '-' + @number + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-answer")
                .Cue().Company().WithSharedHydration()
                .Seek("Answered").One()
                    .OnMatch("[_:Choreographies].Answer() expose $call call, $to to, $done done, $number number;")
                .Causation.Continue(answer);
            golemActor.Reactions.DefineReaction("echo-dissolved")
                .Cue().Company().WithSharedHydration()
                .Seek("Dissolved").One()
                    .OnMatch("[_:Choreographies].Dissolve(_) expose $call call, $nonce nonce;")
                .Causation.Continue(dissolved);
        }

        if (tellDoneTo == null) return;
        // Every stop the body reaches is told to the peer that follows, whatever route it belongs to: the pose the body
        // STANDS at (captured from the `me = Pose(…)` beside the act — a tell asserts a lived fact), on the literal `true
        // reached` alone (whether the arrival was a stop is the domain's conclusion inside Arrive: frozen on the entry as an
        // expose, with the route's id). Two statements on purpose (engine 2.0.1-beta.10017): a single-tell entry is elided
        // with its ack, and an elided tail gets its ids reused at the next boot; announcing the stop first keeps the entry
        // out of elision.
        golemActor.Reactions.DefineReaction("echo-reached")
            .Cue().Company().WithSharedHydration()
            .Seek("Reached").One()
                .OnMatch("[_:Route].Arrive(_) expose $missionId rid, true reached, $x x, $y y;")   // the point the stop order headed to (ajuste 68: the arrival says the leg, no pose built beside it)
            .Causation.Continue($@"
                {{
                    route = g.Find(@missionId);
                    route.Announce();
                }}
                tell PointVisited with @x, @y
                    to {tellDoneTo}
                    once 'visited-' + @missionId + '-' + @x + ',' + @y;
            ");
    }

    // AFTER performance.Start(): take up the peers' tells as acts of my own journal.
    public void Listen()
    {
        toldListener = performance
            .ListenAs(golem, bindings, wire)
            .Told("PointVisited").With<double>("x").With<double>("y")
                .Command(DisplacerRol.UptakePointVisited)
            .Told("BumpedAt").With<double>("bodyX").With<double>("bodyY").With<double>("bodyHeading").With<double>("bearing").With<string>("who").With<string>("scenario")
                .Command(BumperRol.UptakeBumpedAt)
            .Told("ObstacleGone").With<double>("x").With<double>("y")
                .Command(BumperRol.UptakeObstacleGone)
            .Told("RanTo").With<string>("title").With<string>("nonce").With<int>("runs").With<double>("cx").With<double>("cy")
                .Command(DynamicsRol.UptakeRanTo)
            .Told("GameOver").With<string>("title").With<string>("nonce").With<string>("who")
                .Command(DynamicsRol.UptakeGameOver)
            .Told("StoodFor").With<string>("call").With<string>("nonce").With<string>("teller").With<double>("px").With<double>("py")
                .Command(ChoreographerRol.UptakeStoodFor)
            .Told("PlacedAt").With<string>("call").With<string>("who")
                .Command(ChoreographerRol.UptakePlacedAt)
            .Told("AlignedAt").With<string>("call").With<string>("who").With<int>("round")
                .Command(ChoreographerRol.UptakeAlignedAt)
            .Told("Dissolved").With<string>("call")
                .Command(ChoreographerRol.UptakeDissolved)
            .Told("FinishedAt").With<string>("call").With<string>("who")
                .Command(ChoreographerRol.UptakeFinishedAt)
            .Told("DidYouFinish").With<string>("about").With<string>("asked").With<string>("asker")
                .Command(ChoreographerRol.UptakeDidYouFinish)
            .Told("FinishedState").With<string>("about").With<string>("asked").With<bool>("finished")
                .Command(ChoreographerRol.UptakeFinishedState)
            .Told("YourTurn").With<string>("to")
                .Command(ChoreographerRol.UptakeYourTurn)
            .Start();
        feed.Broadcast(new PanelEvent(performance.CurrentEntryId, "runtime", "", $"listening for tells as '{golem}' on topic 'tell-{golem}'", DateTime.UtcNow));
        Console.WriteLine($"[golem {golem}] listening for tells on topic 'tell-{golem}'");
        if (tellDoneTo != null) Console.WriteLine($"[golem {golem}] will tell '{tellDoneTo}' every stop it reaches");
    }
}
