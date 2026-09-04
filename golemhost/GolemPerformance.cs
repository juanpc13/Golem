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

    // The release chain.
    //   init    — the golem is born with its body: cruise speed (world units per second)
    //             and how long it pauses at a point a peer told it about.
    //   map_v1  — the golem learns its map: a ring of rooms around two solid blocks, with a
    //             wide shortcut through the middle (north ~ center ~ south, open boundaries)
    //             and two long narrow corridors on the sides (west, east). One fluent chain
    //             per place declaring its doors (a point on the shared wall) and its open
    //             boundaries. Shortest roads become visible: kitchen -> garage cuts through
    //             the center, kitchen -> living takes the west corridor. The map is the golem's own knowledge: roads are planned
    //             through the passages (shortest path), and every golem shares the same map.
    // RULE: an applied release is never edited (the engine guards its body signature) —
    // evolve the golem by APPENDING the next release.
    protected override void OnHydrated()
    {
        Actor.Using(@"
            upgrade('init') {
                g = Golem();
                g.Embody(2.0);
                g.Pace(6);
            }
            upgrade('map_v1') {
                g.AddPlace('kitchen', 0, 8, 4, 3).Door('north', 4, 9.5).Door('west', 0.75, 8);
                g.AddPlace('north',   4, 8, 3, 3).Door('storage', 7, 9.5).Open('center');
                g.AddPlace('storage', 7, 8, 4, 3).Door('east', 10.25, 8);
                g.AddPlace('west',    0, 3, 1.5, 5).Door('living', 0.75, 3);
                g.AddPlace('center',  4, 3, 3, 5).Open('south');
                g.AddPlace('east',    9.5, 3, 1.5, 5).Door('garage', 10.25, 3);
                g.AddPlace('living',  0, 0, 4, 3).Door('south', 4, 1.5);
                g.AddPlace('south',   4, 0, 3, 3).Door('garage', 7, 1.5);
                g.AddPlace('garage',  7, 0, 4, 3);
            }
        ")
        .PerformCommand();
    }
}
