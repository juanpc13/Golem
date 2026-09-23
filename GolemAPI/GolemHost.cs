using Choreography.Theater;
using GolemAPI.Choreography;
using GolemAPI.Membrane;
using GolemAPI.Panel;
using GolemDomain;
using Puppeteer;
using Puppeteer.EventSourcing.Interpreter.Formatters;

namespace GolemAPI;

/// <summary>Who the golem is and what its body can do — what the environment says in a container, what a scenario says in a test.</summary>
public sealed record GolemSettings(
    string Golem,                        // who I am: names the journal
    string Body,                         // the model I drive in the world
    (double X, double Y) Home,           // the body's mark: reborn or let go, it is put back here
    Capabilities Capabilities,           // the roles this body can play
    IReadOnlyList<string> Peers,         // every golem I can tell: what the body bumps into is told to all of them
    string TellDoneTo,                   // the peer that follows my stops, or null
    DatabaseType Storage,                // FileSystem in a container, IN_MEMORY in a test
    string JournalPath);                 // where the FileSystem journal lives (and what reset-everything wipes)

// THE GOLEM ASSEMBLED (propuesta 52, fase 0, 23-sep-2026): what Program.cs did in line, taken out so a scenario test can run
// the SAME golem in its own process — the actor with the domain's assembly, the speech, the embodiment with its roles, the
// mechanics as the output target, the waking — over the wires it is given. Program.cs gives it Rosbridge and HttpBroker and
// hosts the controllers; a test gives it a body in a world held in memory and a broker in memory. Nothing here decides: the
// order of the steps is the only thing it keeps, and it is the order the engine asks for (reactions before Start, the output
// target only after hydration and with the body listening).
public sealed class GolemHost : IAsyncDisposable
{
    private readonly GolemPerformance performance;

    public GolemSettings Settings { get; }
    public PerformanceV2 Performance => performance;
    public GolemEmbodiment Embodiment { get; }
    public GolemSpeech Speech { get; }
    public PanelFeed Feed { get; }
    public IBodyWire BodyWire { get; }
    public ITellWire TellWire { get; }
    /// <summary>True only on the boot where the journal was brand-new: the body is put back on its mark.</summary>
    public bool BornThisBoot => performance.BornThisBoot;

    private GolemHost(GolemSettings settings, GolemPerformance performance, GolemSpeech speech, GolemEmbodiment embodiment,
                      PanelFeed feed, IBodyWire bodyWire, ITellWire tellWire)
    {
        Settings = settings;
        this.performance = performance;
        Speech = speech;
        Embodiment = embodiment;
        Feed = feed;
        BodyWire = bodyWire;
        TellWire = tellWire;
    }

    /// <summary>The actor, its storage, the speech and every reaction, the embodiment and its roles — then Start: rehydration,
    /// the release chain, the reactions armed. The panel's journal lane starts watching. Nothing is connected yet.</summary>
    public static GolemHost Build(GolemSettings settings, IBodyWire bodyWire, ITellWire tellWire, PanelFeed feed)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (bodyWire == null) throw new ArgumentNullException(nameof(bodyWire), "a golem needs a wire to its body");
        if (tellWire == null) throw new ArgumentNullException(nameof(tellWire), "a golem needs a wire for its tells, even with no peers");
        if (feed == null) throw new ArgumentNullException(nameof(feed));

        // Storage first; then the tell transport and every Reaction, because Start is what arms them and runs the release chain.
        // A journal in memory is keyed by the ACTOR's name and outlives the performance in this process: every build in memory
        // gets its own actor name, or a second golem called the same would rehydrate the first one's journal (23-sep-2026, the
        // scenarios in a row: the second 'red' woke at entry 16). The golem's name — the journal's identity in the tells — stays.
        bool inMemory = settings.Storage != DatabaseType.FileSystem;
        string actor = inMemory ? $"{settings.Golem}-{Guid.NewGuid():N}" : settings.Golem;
        var performance = new GolemPerformance(actor, DomainLibrary.Assembly);
        performance.ConfigureStorage(settings.Storage, inMemory ? actor : $"path={settings.JournalPath}");

        var speech = new GolemSpeech(performance, tellWire, feed, settings.Golem, settings.TellDoneTo, settings.Peers);
        speech.DefineReactions();
        // The golem given a body: what the body reports comes to it; every print goes out through the robot's mechanics,
        // the actor's output target.
        var embodiment = new GolemEmbodiment(performance, bodyWire, feed, tellWire, settings.Golem, settings.Home,
                                             settings.JournalPath, settings.Capabilities);
        Console.WriteLine($"[golem {settings.Golem}] roles: {settings.Capabilities}");
        embodiment.Mechanics.DefineReactions(performance);   // one next-order reaction per act shape: the engine pushes the print

        performance.Start();                                  // rehydration + release chain + the .Cue() reactions come alive
        new JournalTap(performance, feed).Start();            // the panel's journal lane: the whole diary, then every record
        Console.WriteLine($"[golem {settings.Golem}] journal {(settings.Storage == DatabaseType.FileSystem ? "at " + settings.JournalPath : "in memory")}");
        Console.WriteLine($"[golem {settings.Golem}] rehydrated at entry {performance.CurrentEntryId}");
        return new GolemHost(settings, performance, speech, embodiment, feed, bodyWire, tellWire);
    }

    /// <summary>The golem comes into the world: it takes up its peers' tells, connects and binds to its body, arms the output
    /// target (only now: after hydration, and with the body listening), and a reborn golem puts its body back on its mark.</summary>
    public async Task ConnectAsync(CancellationToken ct)
    {
        Speech.Listen();
        await BodyWire.ConnectAsync(ct);
        await BodyWire.BindAsync(ct);
        Feed.Broadcast(new PanelEvent(performance.CurrentEntryId, "runtime", "",
            $"membrane connected — driving body '{Settings.Body}', pose from {(BodyWire.Source == PoseSource.Wheels ? "the wheels (dead reckoning: the world's truth is shown to you, never to the golem)" : "the world's truth")}",
            DateTime.UtcNow));
        performance.OutputTarget(Embodiment.Mechanics, new JsonFormatter());
        if (performance.BornThisBoot)
        {
            await Task.Delay(500, ct);   // let the advertise settle before the first publish
            await Embodiment.PutBackHomeAsync(ct);
            Feed.Broadcast(new PanelEvent(performance.CurrentEntryId, "runtime", "",
                $"reborn — body '{Settings.Body}' put back on its mark at ({Settings.Home.X}, {Settings.Home.Y})", DateTime.UtcNow));
        }
    }

    /// <summary>The clock, until cancelled: the journal asked now and then what it wants, when no print brought it.</summary>
    public Task RunAsync(CancellationToken ct) => Embodiment.RunAsync(ct);

    public async ValueTask DisposeAsync()
    {
        await BodyWire.DisposeAsync();
        performance.Dispose();
    }
}
