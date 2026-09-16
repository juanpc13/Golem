using Choreography.Theater;
using Choreography.Told;
using Choreography.Transport.Brokered;
using GolemAPI.Controllers;
using GolemAPI.Membrane;
using GolemAPI.Panel;
using Puppeteer;

namespace GolemAPI.Choreography;

// What the golem SAYS to its peers and what it takes up from them. Speech is a Reaction, never a command (paper 04):
// a tell asserts a lived fact, past tense, when the act that made it lands in the journal. A reaction captures no
// object, so the acts that must be told expose their values beside them (the labels below) and the reaction matches
// those. The uptakes bind the peers' tells to the scripts GolemController declares.
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

    public GolemSpeech(PerformanceV2 performance, ActorV2 golemActor, HttpBroker wire, PanelFeed feed, string golem, string tellDoneTo, IReadOnlyList<string> peers)
    {
        this.performance = performance;
        this.golemActor = golemActor;
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
            // The touch itself, with my name and the pose of the touch: a peer that bumped there and then knows it met
            // ME, not a thing. One tell per peer in one entry (several statements: no single-tell elision), one once-id
            // per addressee.
            string bumped = string.Join("\n", peers.Select(p => $@"
                tell BumpedAt with @x, @y, @heading, @who, @px, @py
                    to {p}
                    once 'bump-' + @who + '-' + @x + ',' + @y + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-bumped")
                .Cue().Company().WithSharedHydration()
                .Seek("Bumped").One()
                    .OnMatch("expose $x x, $y y, $heading heading, $who who, $px px, $py py;")
                .Causation.Continue(bumped);

            // A standing body touched: a body did it (things do not move). Told so the mover knows it met one; no mark.
            string touched = string.Join("\n", peers.Select(p => $@"
                tell TouchedAt with @x, @y, @who, @px, @py
                    to {p}
                    once 'touch-' + @who + '-' + @x + ',' + @y + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-touched")
                .Cue().Company().WithSharedHydration()
                .Seek("Touched").One()
                    .OnMatch("expose $x tx, $y ty, $who twho, $px tpx, $py tpy;")
                .Causation.Continue(touched);

            // It was a body: the mark the bump presumed is taken back, here and in every peer that learned it.
            string met = string.Join("\n", peers.Select(p => $@"
                tell MetPeer with @x, @y
                    to {p}
                    once 'met-{golem}-' + @x + ',' + @y + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-met")
                .Cue().Company().WithSharedHydration()
                .Seek("Met").One()
                    .OnMatch("expose $x ex, $y ey;")
                .Causation.Continue(met);

            // Somebody took a thing away: the fleet must forget it together, or one golem would keep skirting what
            // another can already drive through.
            string forgotten = string.Join("\n", peers.Select(p => $@"
                tell ObstacleGone with @x, @y
                    to {p}
                    once 'gone-{golem}-' + @x + ',' + @y + '-{p}';"));
            golemActor.Reactions.DefineReaction("echo-forgotten")
                .Cue().Company().WithSharedHydration()
                .Seek("Forgotten").One()
                    .OnMatch("expose $x gx, $y gy;")
                .Causation.Continue(forgotten);
        }

        if (tellDoneTo == null) return;
        // Every stop the body reaches is told to the peer that follows, whatever route it belongs to. Two statements
        // on purpose (engine 2.0.1-beta.10017): a single-tell entry is elided with its ack, and an elided tail gets
        // its ids reused at the next boot; announcing the stop first keeps the entry out of elision.
        golemActor.Reactions.DefineReaction("echo-reached")
            .Cue().Company().WithSharedHydration()
            .Seek("Reached").One()
                .OnMatch("expose $missionId rid, $x rx, $y ry;")
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
                .Command(GolemController.UptakePointVisited)
            .Told("BumpedAt").With<double>("x").With<double>("y").With<double>("heading").With<string>("who").With<double>("px").With<double>("py")
                .Command(GolemController.UptakeBumpedAt)
            .Told("TouchedAt").With<double>("x").With<double>("y").With<string>("who").With<double>("px").With<double>("py")
                .Command(GolemController.UptakeTouchedAt)
            .Told("MetPeer").With<double>("x").With<double>("y")
                .Command(GolemController.UptakeMetPeer)
            .Told("ObstacleGone").With<double>("x").With<double>("y")
                .Command(GolemController.UptakeObstacleGone)
            .Start();
        feed.Broadcast(new PanelEvent(performance.CurrentEntryId, "runtime", "", $"listening for tells as '{golem}' on topic 'tell-{golem}'", DateTime.UtcNow));
        Console.WriteLine($"[golem {golem}] listening for tells on topic 'tell-{golem}'");
        if (tellDoneTo != null) Console.WriteLine($"[golem {golem}] will tell '{tellDoneTo}' every stop it reaches");
    }
}
