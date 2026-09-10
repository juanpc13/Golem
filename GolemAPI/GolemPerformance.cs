using System.Reflection;
using Choreography.Theater;

namespace GolemAPI;

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
    //   init    — the golem is born with its body: its size (the radius of the disk it occupies,
    //             matching the chassis the sim builds — with it the golem reckons WHERE a touch
    //             happened and holds that point against its map), its cruise speed (world units
    //             per second) and how long it lingers at a stop a peer told it about.
    //   map_v1  — the golem charts its map: a ring of rooms around two solid blocks, with a
    //             wide shortcut through the middle (north ~ center ~ south, open boundaries)
    //             and two long narrow corridors on the sides (west, east). One fluent chain
    //             per place declaring its doors (a point on the shared wall) and its open
    //             boundaries. Shortest roads become visible: kitchen -> garage cuts through
    //             the center, kitchen -> living takes the west corridor. The map is the golem's
    //             own knowledge: roads are planned through the passages (shortest path), and
    //             every golem shares the same map.
    // (7-sep-2026: the language was rewritten in one go — MoveTo/Cover/Follow, Cross/Reach,
    // Fail/Abandon, Chart/DoorTo/OpenTo, Embody/Cruise/Linger — and the journals of the
    // turtlesim era were archived; journals born before that day do not rehydrate.)
    // RULE: an applied release is never edited (the engine guards its body signature) —
    // evolve the golem by APPENDING the next release.
    protected override void OnHydrated()
    {
        Actor.Using(@"
            upgrade('init') {
                g = Golem();
                g.Embody(0.25);
                g.Cruise(2.0);
                g.Linger(6);
            }
            upgrade('map_v1') {
                g.Chart('kitchen', 0, 8, 4, 3).DoorTo('north', 4, 9.5).DoorTo('west', 0.75, 8);
                g.Chart('north',   4, 8, 3, 3).DoorTo('storage', 7, 9.5).OpenTo('center');
                g.Chart('storage', 7, 8, 4, 3).DoorTo('east', 10.25, 8);
                g.Chart('west',    0, 3, 1.5, 5).DoorTo('living', 0.75, 3);
                g.Chart('center',  4, 3, 3, 5).OpenTo('south');
                g.Chart('east',    9.5, 3, 1.5, 5).DoorTo('garage', 10.25, 3);
                g.Chart('living',  0, 0, 4, 3).DoorTo('south', 4, 1.5);
                g.Chart('south',   4, 0, 3, 3).DoorTo('garage', 7, 1.5);
                g.Chart('garage',  7, 0, 4, 3);
            }
        ")
        .PerformCommand();
    }
}
