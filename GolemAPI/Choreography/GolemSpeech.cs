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

            // The operator called this golem to a formation: every peer is told it was called too (ajustes 71, 72) — the formation and
            // the fleet; each peer knows who it is (born with its name) and joins if it is in the fleet. Only the act with the expose is
            // told: a peer's own join carries none, so the call spreads one hop, no loop. The once is per call and peer: the same square
            // called again is another call.
            string called = string.Join("\n", peers.Select(p => $@"
                tell CalledTo with @figure, @cx, @cy, @sideLength, @names, @callId
                    to {p}
                    once 'called-' + @callId + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-called")
                .Cue().Company().WithSharedHydration()
                .Seek("Called").One()
                    .OnMatch("[_:Choreographies].Join(_, _, _) expose $figure shape, $cx atX, $cy atY, $sideLength length, $names crew, $callId call;")
                .Causation.Continue(called);
            // BY DISTANCE (ajuste 73): a golem that convened tells every peer where it stood — its act's expose, the position read from
            // the domain (expose takes any primitive expression). The peer that had not convened convenes itself and tells in turn; each
            // golem convenes once, so the word spreads with no loop. The once is per call, teller and peer.
            string stood = string.Join("\n", peers.Select(p => $@"
                tell StoodFor with @figure, @cx, @cy, @sideLength, @names, @callId, @who, @stoodX, @stoodY
                    to {p}
                    once 'stood-' + @callId + '-' + @who + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-stood")
                .Cue().Company().WithSharedHydration()
                .Seek("Stood").One()
                    .OnMatch("[_:Choreographies].Convene(_, _, _) expose $figure shape, $cx atX, $cy atY, $sideLength length, $names crew, $callId call, $who who, $stoodX stoodX, $stoodY stoodY;")
                .Causation.Continue(stood);
            // THE FORMATION IN PLACE (ajuste 77): a golem that reached its place says so to every peer — the arrival's act exposes who and
            // which call (the peer's own Placed, written by its uptake, carries no expose: one hop) — and a step asked of one golem is told
            // to every peer, who queues the same step; the once is per step and peer.
            string placed = string.Join("\n", peers.Select(p => $@"
                tell PlacedAt with @call, @who
                    to {p}
                    once 'placed-' + @call + '-' + @round + '-' + @who + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-placed")
                .Cue().Company().WithSharedHydration()
                .Seek("Placed").One()
                    .OnMatch("[_:Muster].Placed(_) expose $who who, $call call, $round round;")   // the round in the once: a word per round, not one per call
                .Causation.Continue(placed);
            string rotate = string.Join("\n", peers.Select(p => $@"
                tell RotateTo with @sense, @stepId, @call
                    to {p}
                    once 'rotate-' + @stepId + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-rotate")
                .Cue().Company().WithSharedHydration()
                .Seek("Rotate").One()
                    .OnMatch("[_:Muster].Rotate(_, _) expose $sense turning, $stepId step, $call call;")
                .Causation.Continue(rotate);
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
            .Told("StoodFor").With<string>("figure").With<double>("cx").With<double>("cy").With<double>("sideLength").With<string>("names")
                .With<string>("callId").With<string>("teller").With<double>("px").With<double>("py")
                .Command(GolemEmbodiment.UptakeStoodFor)
            .Told("CalledTo").With<string>("figure").With<double>("cx").With<double>("cy").With<double>("sideLength").With<string>("names")
                .With<string>("callId")
                .Command(GolemEmbodiment.UptakeCalledTo)
            .Told("PlacedAt").With<string>("call").With<string>("who")
                .Command(GolemEmbodiment.UptakePlacedAt)
            .Told("RotateTo").With<string>("sense").With<string>("stepId").With<string>("call")
                .Command(GolemEmbodiment.UptakeRotateTo)
            .Start();
        feed.Broadcast(new PanelEvent(performance.CurrentEntryId, "runtime", "", $"listening for tells as '{golem}' on topic 'tell-{golem}'", DateTime.UtcNow));
        Console.WriteLine($"[golem {golem}] listening for tells on topic 'tell-{golem}'");
        if (tellDoneTo != null) Console.WriteLine($"[golem {golem}] will tell '{tellDoneTo}' every stop it reaches");
    }
}
