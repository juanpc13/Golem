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
    // (10-sep-2026: the modules became globals of the actor — body, map, layout, collisions —
    // built in their own releases and handed to the golem: g = Golem(body, layout, collisions).
    // Stops, orders and roads are objects (Position, the map's passages); the road is written
    // act by act (Route, Via, Around, Aside, Stop). Journals born before that day were archived
    // as journal-legacy-20260910-*; they do not rehydrate.)
    // RULE: an applied release is never edited (the engine guards its body signature) —
    // evolve the golem by APPENDING the next release.
    protected override void OnHydrated()
    {
        Actor.Using(@"
            upgrade('body_v1') {
                body = Body(0.25, 2.0, 6.0);
            }
            upgrade('warehouse_v1') {
                map = MapLayout('warehouse');
                map.Area('kitchen').At(Position(0.0, 8.0)).Size(4.0, 3.0).DoorAt('north', Position(4.0, 9.5)).DoorAt('west', Position(0.75, 8.0));
                map.Area('north').At(Position(4.0, 8.0)).Size(3.0, 3.0).DoorAt('storage', Position(7.0, 9.5)).OpenTo('center');
                map.Area('storage').At(Position(7.0, 8.0)).Size(4.0, 3.0).DoorAt('east', Position(10.25, 8.0));
                map.Area('west').At(Position(0.0, 3.0)).Size(1.5, 5.0).DoorAt('living', Position(0.75, 3.0));
                map.Area('center').At(Position(4.0, 3.0)).Size(3.0, 5.0).OpenTo('south');
                map.Area('east').At(Position(9.5, 3.0)).Size(1.5, 5.0).DoorAt('garage', Position(10.25, 3.0));
                map.Area('living').At(Position(0.0, 0.0)).Size(4.0, 3.0).DoorAt('south', Position(4.0, 1.5));
                map.Area('south').At(Position(4.0, 0.0)).Size(3.0, 3.0).DoorAt('garage', Position(7.0, 1.5));
                map.Area('garage').At(Position(7.0, 0.0)).Size(4.0, 3.0);
            }
            upgrade('init') {
                collisions = Collisions(map);
                g = Golem(body, map, collisions);
            }
        ")
        .PerformCommand();
    }
}
