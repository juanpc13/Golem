using Choreography.Theater;
using Choreography.Told;
using Choreography.Transport.Brokered;
using GolemAPI.Membrane;
using GolemAPI.Panel;
using Puppeteer;

namespace GolemAPI.Coordination;

// What the WARDEN says to the golems and what it takes up from them (propuesta 88). Speech is a Reaction, never a command (paper 04): a
// word asserts a lived fact when the act that made it lands in the warden's journal — "the fleet was called", "the places were shared".
// Every word goes to every golem the warden can reach (its routes); a golem not in the fleet takes nothing from it. The uptakes bind the
// golems' words — where each stands, that it stands on its place, that it woke — to the scripts WardenMind declares.
public sealed class WardenSpeech
{
    private readonly PerformanceV2 performance;
    private readonly ActorV2 wardenActor;
    private readonly IMessageBroker wire;
    private readonly PanelFeed feed;
    private readonly string warden;
    private readonly IReadOnlyList<string> golems;
    private readonly TellBindingTable bindings = new();
    private ToldListener toldListener;

    public WardenSpeech(PerformanceV2 performance, IMessageBroker wire, PanelFeed feed, string warden, IReadOnlyList<string> golems)
    {
        this.performance = performance;
        this.wardenActor = performance.Actor;
        this.wire = wire;
        this.feed = feed;
        this.warden = warden;
        this.golems = golems;
    }

    // BEFORE performance.Start(): the tell transport and every Reaction.
    public void DefineReactions()
    {
        bindings.Bind(warden, $"tell-{warden}");
        foreach (var golem in golems) bindings.Bind(golem, $"tell-{golem}");
        performance.UseTellTransport(new BrokerTellTransport(wire, bindings, warden));
        if (golems.Count == 0) return;

        // THE CALL: the warden convened the fleet — every golem is told the formation, the fleet, the call and the policy; one in the fleet
        // answers where it stands. The once is per call and golem.
        string called = string.Join("\n", golems.Select(g => $@"
            tell CalledTo with @figure, @cx, @cy, @sideLength, @names, @callId, @by
                to {g}
                once 'called-' + @callId + '-{g}';"));
        wardenActor.Reactions.DefineReaction("echo-called")
            .Cue().Company().WithSharedHydration()
            .Seek("Called").One()
                .OnMatch("[_:Muster].Convene() expose $figure shape, $cx atX, $cy atY, $sideLength length, $names crew, $callId call, $by policy;")
            .Causation.Continue(called);

        // THE PLACES SHARED: the whole table of a round as one word, to every golem — on the act that shares the first round (Share) and on
        // every step (Step); each golem takes its own place from it and the others as berths. The once carries the round.
        string shared = string.Join("\n", golems.Select(g => $@"
            tell Shared with @call, @round, @table, @atX, @atY
                to {g}
                once 'shared-' + @call + '-' + @round + '-{g}';"));
        wardenActor.Reactions.DefineReaction("echo-shared")
            .Cue().Company().WithSharedHydration()
            .Seek("Shared").One()
                .OnMatch("[_:Muster].Share() expose $call call, $round round, $table table, $atX atX, $atY atY;")
            .Causation.Continue(shared);
        wardenActor.Reactions.DefineReaction("echo-stepped")
            .Cue().Company().WithSharedHydration()
            .Seek("Stepped").One()
                .OnMatch("[_:Muster].Step() expose $call call, $round round, $table table, $atX atX, $atY atY;")
            .Causation.Continue(shared);
    }

    // AFTER performance.Start(): take up the golems' words as acts of the warden's journal.
    public void Listen()
    {
        toldListener = performance
            .ListenAs(warden, bindings, wire)
            .Told("Awoke").With<string>("who").With<double>("px").With<double>("py").With<string>("scenario")
                .Command(WardenMind.UptakeAwoke)
            .Told("StoodAt").With<string>("call").With<string>("who").With<double>("px").With<double>("py").With<string>("scenario")
                .Command(WardenMind.UptakeStoodAt)
            .Told("PlacedAt").With<string>("call").With<int>("round").With<string>("who")
                .Command(WardenMind.UptakePlacedAt)
            .Start();
        feed.Broadcast(new PanelEvent(performance.CurrentEntryId, "runtime", "", $"listening for the golems' words as '{warden}' on topic 'tell-{warden}'", DateTime.UtcNow));
        Console.WriteLine($"[warden {warden}] listening for tells on topic 'tell-{warden}'; golems on the wire: {string.Join(", ", golems)}");
    }
}
