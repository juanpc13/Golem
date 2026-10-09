using Choreography.Theater;
using Choreography.Told;
using Choreography.Transport.Brokered;
using GolemAPI.Membrane;
using GolemAPI.Panel;
using Puppeteer;

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
                tell BumpedAt with @bodyX, @bodyY, @bodyHeading, @bearing, @who
                    to {p}
                    once 'bump-' + @who + '-' + @bodyX + ',' + @bodyY + ',' + @bearing + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-bumped")
                .Cue().Company().WithSharedHydration()
                .Seek("Bumped").One()
                    .OnMatch("Pose($bodyX, $bodyY, $bodyHeading) [_:Golem].Bump(_, $bearing) expose $who who;")
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
            // THE FORMATION TOLD (propuesta 104): the operator's form exposes the formation — its name, figure, centre, measure, orientation, fleet,
            // policy and the stamp of the call — and every peer is told; the peer's own form (its uptake) carries no expose, so it spreads nothing
            // further. The once is per stamp and peer: a formation told again under the same name is a new call.
            string formed = string.Join("\n", peers.Select(p => $@"
                tell FormedAs with @call, @shape, @atX, @atY, @length, @degrees, @crew, @policy, @nonce
                    to {p}
                    once 'formed-' + @nonce + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-formed")
                .Cue().Company().WithSharedHydration()
                .Seek("Formed").One()
                    .OnMatch("[_:Choreographies].Form(_, _, _, _, _, _, _, _) expose $call call, $shape shape, $atX atX, $atY atY, $length length, $degrees degrees, $crew crew, $policy policy, $nonce nonce;")
                .Causation.Continue(formed);
            string formedRings = string.Join("\n", peers.Select(p => $@"
                tell FormedRingsAs with @call, @atX, @atY, @outerLength, @innerLength, @degrees, @crew, @innerCrew, @policy, @nonce
                    to {p}
                    once 'formed-' + @nonce + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-formed-rings")
                .Cue().Company().WithSharedHydration()
                .Seek("FormedRings").One()
                    .OnMatch("[_:Choreographies].FormRings(_, _, _, _, _, _, _, _) expose $call call, $atX atX, $atY atY, $outerLength outerLength, $innerLength innerLength, $degrees degrees, $crew crew, $innerCrew innerCrew, $policy policy, $nonce nonce;")
                .Causation.Continue(formedRings);
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
            string rotate = string.Join("\n", peers.Select(p => $@"
                tell RotateTo with @call, @sense, @ring, @stepId
                    to {p}
                    once 'rotate-' + @stepId + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-rotate")
                .Cue().Company().WithSharedHydration()
                .Seek("Rotate").One()
                    .OnMatch("[_:Formation].Queue(_, _) expose $call call, $sense turning, $ring orbit, $stepId step;")   // the figure's move queued in the formation (ajuste 84)
                .Causation.Continue(rotate);
            string dissolved = string.Join("\n", peers.Select(p => $@"
                tell Dissolved with @call
                    to {p}
                    once 'dissolved-' + @nonce + '-{p}';"));
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
                .Command(GolemEmbodiment.UptakePointVisited)
            .Told("BumpedAt").With<double>("bodyX").With<double>("bodyY").With<double>("bodyHeading").With<double>("bearing").With<string>("who")
                .Command(GolemEmbodiment.UptakeBumpedAt)
            .Told("ObstacleGone").With<double>("x").With<double>("y")
                .Command(GolemEmbodiment.UptakeObstacleGone)
            .Told("FormedAs").With<string>("call").With<string>("shape").With<double>("cx").With<double>("cy").With<double>("measureLength")
                .With<double>("angle").With<string>("names").With<string>("by").With<string>("nonce")
                .Command(GolemEmbodiment.UptakeFormedAs)
            .Told("FormedRingsAs").With<string>("call").With<double>("cx").With<double>("cy").With<double>("outerRadius").With<double>("innerRadius")
                .With<double>("angle").With<string>("names").With<string>("innerNames").With<string>("by").With<string>("nonce")
                .Command(GolemEmbodiment.UptakeFormedRingsAs)
            .Told("StoodFor").With<string>("call").With<string>("nonce").With<string>("teller").With<double>("px").With<double>("py")
                .Command(GolemEmbodiment.UptakeStoodFor)
            .Told("PlacedAt").With<string>("call").With<string>("who")
                .Command(GolemEmbodiment.UptakePlacedAt)
            .Told("RotateTo").With<string>("call").With<string>("sense").With<string>("ring").With<string>("stepId")
                .Command(GolemEmbodiment.UptakeRotateTo)
            .Told("Dissolved").With<string>("call")
                .Command(GolemEmbodiment.UptakeDissolved)
            .Start();
        feed.Broadcast(new PanelEvent(performance.CurrentEntryId, "runtime", "", $"listening for tells as '{golem}' on topic 'tell-{golem}'", DateTime.UtcNow));
        Console.WriteLine($"[golem {golem}] listening for tells on topic 'tell-{golem}'");
        if (tellDoneTo != null) Console.WriteLine($"[golem {golem}] will tell '{tellDoneTo}' every stop it reaches");
    }
}
