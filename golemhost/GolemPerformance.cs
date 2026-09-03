using System.Reflection;
using Choreography.Theater;

namespace GolemHost;

// The golem's Performance. Initialization is versioned INSIDE the actor through the
// hydration hooks (hosting-environments guide, "Seed / migration idiom"): OnHydrated
// runs after every hydration and issues the release chain; applied releases skip,
// new ones run and journal. To evolve the golem, append the next release below —
// never edit an applied one (its body signature is guarded).
internal sealed class GolemPerformance : PerformanceV2
{
    // True only on the boot where the journal was brand-new (the framework raises
    // OnFirstHydration exactly then). Used for the panel's birth announcement.
    internal bool BornThisBoot { get; private set; }

    internal GolemPerformance(string actorName, params Assembly[] libraryAssemblies)
        : base(actorName, libraryAssemblies)
    {
    }

    protected override void OnFirstHydration()
    {
        BornThisBoot = true;
    }

    // The operator wants to see EVERYTHING the journal receives — defines, actions,
    // literal scripts, tells, acks, verdicts — so the panel taps the framework's
    // StageHook: every record as it is written (live), and the whole journal at boot.
    internal void WatchJournal(Action<long, byte[]> onRecordWritten)
    {
        hook.OnRecordWritten = (entryId, wire) => onRecordWritten(entryId, wire);
    }

    internal List<Puppeteer.EventSourcing.DB.JournalWireRecord> ReadJournalAfter(long afterEntryId)
    {
        var records = new List<Puppeteer.EventSourcing.DB.JournalWireRecord>();
        hook.ReadJournalRecordsAfter(afterEntryId, records);
        return records;
    }

    // The release chain. Today it is one release: the golem is born with its body — its
    // cruise speed in world units per second, and how long it pauses at a point a peer
    // told it about (the follower's lead-keeping). Both are the golem's own knowledge,
    // like the hitbox will be, so it can answer "how long until I am done" by itself.
    // Every golem runs this same chain. RULE: an applied release is never edited (the
    // engine guards its body signature) — once golems live in the field, evolve them by
    // APPENDING the next release below; collapsing into init was possible only while
    // every journal was disposable.
    protected override void OnHydrated()
    {
        Actor.Using(@"
            upgrade('init') {
                g = Golem();
                g.Embody(2.0);
                g.Pace(6);
            }
        ")
        .PerformCommand();
    }
}
