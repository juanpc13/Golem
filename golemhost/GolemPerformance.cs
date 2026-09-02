using System.Reflection;
using Choreography.Theater;

namespace GolemHost;

// The golem's Performance, LottoPerformance-style: initialization is versioned
// INSIDE the actor via hydration hooks, not guarded by the host.
//
// OnHydrated runs after every hydration (first and every restart) and performs
// the upgrade chain: already-applied upgrades are skipped silently, new ones run
// and journal. To evolve the golem, append Upgrade_From_X_To_Y() methods here —
// never edit an upgrade that already shipped (its body signature is validated).
internal sealed class GolemPerformance : PerformanceV2
{
    // True only on the boot where the journal was brand-new (the framework calls
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

    // The substrate's per-record hook (StageHook.OnRecordWritten): fires for every
    // journal entry THIS process writes — including engine-side writes our call
    // sites never see (the tell sentence, its ack, a Told uptake's perform). The
    // panel uses it so the live feed never silently skips an entry id.
    internal void WatchJournal(Action<long, byte[]> onRecordWritten)
    {
        hook.OnRecordWritten = (entryId, wire) => onRecordWritten(entryId, wire);
    }

    // Durable read of the journal's wire records after an entry — the same public
    // seam a replication catch-up uses. The panel warms its define/template cache
    // from it so action rows can name the template they invoke.
    internal List<Puppeteer.EventSourcing.DB.JournalWireRecord> ReadJournalAfter(long afterEntryId)
    {
        var records = new List<Puppeteer.EventSourcing.DB.JournalWireRecord>();
        hook.ReadJournalRecordsAfter(afterEntryId, records);
        return records;
    }

    protected override void OnHydrated()
    {
        Init();
    }

    private void Init()
    {
        PerformCmd(@"
            upgrade('init') {
                g = Golem();
            };
        ");
    }
}
