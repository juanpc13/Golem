using System.Reflection;
using Choreography.Theater;
using GolemAPI.Panel;

namespace GolemAPI.Coordination;

// THE WARDEN'S PERFORMANCE (propuesta 88, 2-oct-2026): a subject without a body — its release chain is its birth alone, `w = Warden(@wardenName)`.
// No body, no map, no scenario: what it knows of the world it hears from the golems. The same hydration hooks as the golem's
// (hosting-environments guide, "Seed / migration idiom"); the panel taps its journal the same way.
internal sealed class WardenPerformance : PerformanceV2, IJournalWatch
{
    private readonly string wardenName;

    internal bool BornThisBoot { get; private set; }

    internal WardenPerformance(string actorName, string wardenName, params Assembly[] libraryAssemblies)
        : base(actorName, libraryAssemblies)
    {
        if (string.IsNullOrWhiteSpace(wardenName)) throw new ArgumentException("a warden needs a name to be born with", nameof(wardenName));
        this.wardenName = wardenName;
    }

    protected override void OnFirstHydration()
    {
        BornThisBoot = true;
    }

    public void WatchJournal(Action<long, byte[]> onRecordWritten)
    {
        hook.OnRecordWritten = (entryId, wire) => onRecordWritten(entryId, wire);
    }

    public List<Puppeteer.EventSourcing.DB.JournalWireRecord> ReadJournalAfter(long afterEntryId)
    {
        var records = new List<Puppeteer.EventSourcing.DB.JournalWireRecord>();
        hook.ReadJournalRecordsAfter(afterEntryId, records);
        return records;
    }

    // RULE: an applied release is never edited — evolve the warden by APPENDING the next release.
    protected override void OnHydrated()
    {
        Actor.Using(@"
            upgrade('init') {
                w = Warden(@wardenName);
            }
        ")
        .WithParameters(p => { p["wardenName", typeof(string)] = wardenName; })
        .PerformCommand();
    }
}
