using Choreography.Theater;
using GolemAPI.Membrane;
using GolemAPI.Panel;
using GolemDomain;
using Puppeteer;

namespace GolemAPI.Coordination;

/// <summary>Who the warden is and whom it can reach — what the environment says in a container, what a scenario says in a test.</summary>
public sealed record WardenSettings(
    string Name,                         // who I am: names the journal
    IReadOnlyList<string> Golems,        // every golem I can tell and command: my tell routes
    DatabaseType Storage,                // FileSystem in a container, IN_MEMORY in a test
    string JournalPath);                 // where the FileSystem journal lives

// THE WARDEN ASSEMBLED (propuesta 88): the actor with the domain's assembly, the speech, the mind — started. No body wire: the warden
// drives nothing; the golems' words come over the tell wire and its lines go over the same wire's command path.
public sealed class WardenHost : IAsyncDisposable
{
    private readonly WardenPerformance performance;

    public WardenSettings Settings { get; }
    public PerformanceV2 Performance => performance;
    public WardenMind Mind { get; }
    public WardenSpeech Speech { get; }
    public PanelFeed Feed { get; }
    public ITellWire TellWire { get; }
    public bool BornThisBoot => performance.BornThisBoot;

    private WardenHost(WardenSettings settings, WardenPerformance performance, WardenSpeech speech, WardenMind mind, PanelFeed feed, ITellWire tellWire)
    {
        Settings = settings;
        this.performance = performance;
        Speech = speech;
        Mind = mind;
        Feed = feed;
        TellWire = tellWire;
    }

    public static WardenHost Build(WardenSettings settings, ITellWire tellWire, PanelFeed feed)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (tellWire == null) throw new ArgumentNullException(nameof(tellWire), "the warden needs a wire for the golems' words");
        if (feed == null) throw new ArgumentNullException(nameof(feed));

        bool inMemory = settings.Storage != DatabaseType.FileSystem;
        string actor = inMemory ? $"{settings.Name}-{Guid.NewGuid():N}" : settings.Name;
        var performance = new WardenPerformance(actor, settings.Name, DomainLibrary.Assembly);
        performance.ConfigureStorage(settings.Storage, inMemory ? actor : $"path={settings.JournalPath}");

        var speech = new WardenSpeech(performance, tellWire, feed, settings.Name, settings.Golems);
        speech.DefineReactions();
        var mind = new WardenMind(performance, feed, tellWire, settings.Name, settings.Golems);

        performance.Start();
        new JournalTap(performance, feed).Start();
        Console.WriteLine($"[warden {settings.Name}] journal {(settings.Storage == DatabaseType.FileSystem ? "at " + settings.JournalPath : "in memory")}; rehydrated at entry {performance.CurrentEntryId}");
        return new WardenHost(settings, performance, speech, mind, feed, tellWire);
    }

    /// <summary>The warden takes up the golems' words.</summary>
    public Task ConnectAsync(CancellationToken ct)
    {
        Speech.Listen();
        return Task.CompletedTask;
    }

    public ValueTask DisposeAsync()
    {
        performance.Dispose();
        return ValueTask.CompletedTask;
    }
}
