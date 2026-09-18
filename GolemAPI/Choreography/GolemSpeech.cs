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
    private readonly HttpBroker wire;
    private readonly PanelFeed feed;
    private readonly string golem;
    private readonly string tellDoneTo;
    private readonly IReadOnlyList<string> peers;
    private readonly TellBindingTable bindings = new();
    private ToldListener toldListener;

    public GolemSpeech(PerformanceV2 performance, HttpBroker wire, PanelFeed feed, string golem, string tellDoneTo, IReadOnlyList<string> peers)
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
                .OnMatch("Pose($x, $y, _) [_:Route].Arrive(_) expose $missionId rid, true reached;")
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
            .Start();
        feed.Broadcast(new PanelEvent(performance.CurrentEntryId, "runtime", "", $"listening for tells as '{golem}' on topic 'tell-{golem}'", DateTime.UtcNow));
        Console.WriteLine($"[golem {golem}] listening for tells on topic 'tell-{golem}'");
        if (tellDoneTo != null) Console.WriteLine($"[golem {golem}] will tell '{tellDoneTo}' every stop it reaches");
    }
}
