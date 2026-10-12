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
    /// <summary>THE MAZE'S RELEASE (propuesta 108): the text <c>Catalog.Maze().AsRelease(staged: true)</c> renders — kept here verbatim, like the
    /// other maps', and held equal to it by <c>MazeReleaseTests</c>. A journal keeps the map; the code keeps the recipe (the drawing).</summary>
    internal const string MazeRelease = @"upgrade('maze_v1') {
    maze = MapLayout('maze');
    {
        cell06 = maze.Area('cell-0-6').At(Position(0.0, 9.0)).Size(1.5, 1.5);
        cell16 = maze.Area('cell-1-6').At(Position(1.5, 9.0)).Size(1.5, 1.5);
        cell26 = maze.Area('cell-2-6').At(Position(3.0, 9.0)).Size(1.5, 1.5);
        cell36 = maze.Area('cell-3-6').At(Position(4.5, 9.0)).Size(1.5, 1.5);
        cell46 = maze.Area('cell-4-6').At(Position(6.0, 9.0)).Size(1.5, 1.5);
        cell56 = maze.Area('cell-5-6').At(Position(7.5, 9.0)).Size(1.5, 1.5);
        cell66 = maze.Area('cell-6-6').At(Position(9.0, 9.0)).Size(1.5, 1.5);
        cell05 = maze.Area('cell-0-5').At(Position(0.0, 7.5)).Size(1.5, 1.5);
        cell25 = maze.Area('cell-2-5').At(Position(3.0, 7.5)).Size(1.5, 1.5);
        cell45 = maze.Area('cell-4-5').At(Position(6.0, 7.5)).Size(1.5, 1.5);
        cell65 = maze.Area('cell-6-5').At(Position(9.0, 7.5)).Size(1.5, 1.5);
        cell04 = maze.Area('cell-0-4').At(Position(0.0, 6.0)).Size(1.5, 1.5);
        cell24 = maze.Area('cell-2-4').At(Position(3.0, 6.0)).Size(1.5, 1.5);
        cell34 = maze.Area('cell-3-4').At(Position(4.5, 6.0)).Size(1.5, 1.5);
        cell44 = maze.Area('cell-4-4').At(Position(6.0, 6.0)).Size(1.5, 1.5);
        cell64 = maze.Area('cell-6-4').At(Position(9.0, 6.0)).Size(1.5, 1.5);
        cell03 = maze.Area('cell-0-3').At(Position(0.0, 4.5)).Size(1.5, 1.5);
        cell13 = maze.Area('cell-1-3').At(Position(1.5, 4.5)).Size(1.5, 1.5);
        cell33 = maze.Area('cell-3-3').At(Position(4.5, 4.5)).Size(1.5, 1.5);
        cell53 = maze.Area('cell-5-3').At(Position(7.5, 4.5)).Size(1.5, 1.5);
        cell63 = maze.Area('cell-6-3').At(Position(9.0, 4.5)).Size(1.5, 1.5);
        cell02 = maze.Area('cell-0-2').At(Position(0.0, 3.0)).Size(1.5, 1.5);
        cell22 = maze.Area('cell-2-2').At(Position(3.0, 3.0)).Size(1.5, 1.5);
        cell32 = maze.Area('cell-3-2').At(Position(4.5, 3.0)).Size(1.5, 1.5);
        cell42 = maze.Area('cell-4-2').At(Position(6.0, 3.0)).Size(1.5, 1.5);
        cell62 = maze.Area('cell-6-2').At(Position(9.0, 3.0)).Size(1.5, 1.5);
        cell01 = maze.Area('cell-0-1').At(Position(0.0, 1.5)).Size(1.5, 1.5);
        cell21 = maze.Area('cell-2-1').At(Position(3.0, 1.5)).Size(1.5, 1.5);
        cell41 = maze.Area('cell-4-1').At(Position(6.0, 1.5)).Size(1.5, 1.5);
        cell61 = maze.Area('cell-6-1').At(Position(9.0, 1.5)).Size(1.5, 1.5);
        cell00 = maze.Area('cell-0-0').At(Position(0.0, 0.0)).Size(1.5, 1.5);
        cell10 = maze.Area('cell-1-0').At(Position(1.5, 0.0)).Size(1.5, 1.5);
        cell20 = maze.Area('cell-2-0').At(Position(3.0, 0.0)).Size(1.5, 1.5);
        cell30 = maze.Area('cell-3-0').At(Position(4.5, 0.0)).Size(1.5, 1.5);
        cell40 = maze.Area('cell-4-0').At(Position(6.0, 0.0)).Size(1.5, 1.5);
        cell50 = maze.Area('cell-5-0').At(Position(7.5, 0.0)).Size(1.5, 1.5);
        cell60 = maze.Area('cell-6-0').At(Position(9.0, 0.0)).Size(1.5, 1.5);
        cell06.OpenTo(cell16);
        cell16.OpenTo(cell26);
        cell26.OpenTo(cell36);
        cell36.OpenTo(cell46);
        cell46.OpenTo(cell56);
        cell56.OpenTo(cell66);
        cell05.OpenTo(cell06);
        cell25.OpenTo(cell26);
        cell45.OpenTo(cell46);
        cell65.OpenTo(cell66);
        cell04.OpenTo(cell05);
        cell24.OpenTo(cell34);
        cell24.OpenTo(cell25);
        cell34.OpenTo(cell44);
        cell44.OpenTo(cell45);
        cell64.OpenTo(cell65);
        cell03.OpenTo(cell13);
        cell03.OpenTo(cell04);
        cell33.OpenTo(cell34);
        cell53.OpenTo(cell63);
        cell63.OpenTo(cell64);
        cell02.OpenTo(cell03);
        cell22.OpenTo(cell32);
        cell32.OpenTo(cell42);
        cell32.OpenTo(cell33);
        cell62.OpenTo(cell63);
        cell01.OpenTo(cell02);
        cell21.OpenTo(cell22);
        cell41.OpenTo(cell42);
        cell61.OpenTo(cell62);
        cell00.OpenTo(cell10);
        cell00.OpenTo(cell01);
        cell10.OpenTo(cell20);
        cell20.OpenTo(cell30);
        cell20.OpenTo(cell21);
        cell30.OpenTo(cell40);
        cell40.OpenTo(cell50);
        cell40.OpenTo(cell41);
        cell50.OpenTo(cell60);
        cell60.OpenTo(cell61);
    }
    mazeScenario = g.Stage(maze);
}
";

    // True only on the boot where the journal was brand-new (the framework raises
    // OnFirstHydration exactly then). Used for the panel's birth announcement.
    internal bool BornThisBoot { get; private set; }

    private readonly string golemName;   // who the golem is (env GOLEM) — handed to the init release as a parameter (ajuste 72)

    internal GolemPerformance(string actorName, string golemName, params Assembly[] libraryAssemblies)
        : base(actorName, libraryAssemblies)
    {
        if (string.IsNullOrWhiteSpace(golemName)) throw new ArgumentException("a golem needs a name to be born with", nameof(golemName));
        this.golemName = golemName;
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
    // (29-sep-2026, ajuste 60: the golem is born with its body alone and ENTERS a scenario — a map with its own
    // collisions. The golem is born right after its body (`init`, second); every map release names its map's own variable
    // (`warehouse`, `openFloor`) and ends with the golem STAGING it (`warehouseScenario = g.Stage(warehouse)`, its collisions
    // born empty inside) — the warehouse's release enters it too, the golem's home scenario. There is NO global `map` or
    // `collisions` any more: every script reads the scenario the golem is in — `g.Current.Map`, `g.Current.Collisions`
    // (Juan: "no serían necesarios… ya quedaron seteados al momento de g.Enter"). Journals before that day were archived
    // as journal-legacy-20260929-scenarios/ and -variables/; they do not rehydrate.)
    // (30-sep-2026, ajuste 72: the golem is born with its NAME too — `g = Golem(body, @golemName)`, the name from GOLEM as a
    // parameter of the release; the init's body changed, so journals before that day were archived as journal-legacy-20260930-name/.)
    // RULE: an applied release is never edited (the engine guards its body signature) —
    // evolve the golem by APPENDING the next release.
    protected override void OnHydrated()
    {
        Actor.Using(@"
            upgrade('body_v1') {
                radius = Meters(0.25);
                speed = MetersPerSecond(2.0);
                linger = Seconds(6.0);
                retreat = Meters(0.6);
                body = Body(radius, speed, linger, retreat);
            }
            upgrade('init') {
                g = Golem(body, @golemName);
            }
            upgrade('warehouse_v1') {
                warehouse = MapLayout('warehouse');
                {
                    kitchen = warehouse.Area('kitchen').At(Position(0.0, 8.0)).Size(4.0, 3.0);
                    north = warehouse.Area('north').At(Position(4.0, 8.0)).Size(3.0, 3.0);
                    storage = warehouse.Area('storage').At(Position(7.0, 8.0)).Size(4.0, 3.0);
                    west = warehouse.Area('west').At(Position(0.0, 3.0)).Size(1.5, 5.0);
                    center = warehouse.Area('center').At(Position(4.0, 3.0)).Size(3.0, 5.0);
                    east = warehouse.Area('east').At(Position(9.5, 3.0)).Size(1.5, 5.0);
                    living = warehouse.Area('living').At(Position(0.0, 0.0)).Size(4.0, 3.0);
                    south = warehouse.Area('south').At(Position(4.0, 0.0)).Size(3.0, 3.0);
                    garage = warehouse.Area('garage').At(Position(7.0, 0.0)).Size(4.0, 3.0);
                    kitchen.DoorAt(north, Position(4.0, 9.5));
                    kitchen.DoorAt(west, Position(0.75, 8.0));
                    north.DoorAt(storage, Position(7.0, 9.5));
                    north.OpenTo(center);
                    storage.DoorAt(east, Position(10.25, 8.0));
                    west.DoorAt(living, Position(0.75, 3.0));
                    center.OpenTo(south);
                    east.DoorAt(garage, Position(10.25, 3.0));
                    living.DoorAt(south, Position(4.0, 1.5));
                    south.DoorAt(garage, Position(7.0, 1.5));
                }
                warehouseScenario = g.Stage(warehouse);
                g.Enter(warehouseScenario);
            }
            upgrade('open-floor_v1') {
                openFloor = MapLayout('open-floor');
                {
                    floor = openFloor.Area('floor').At(Position(0.0, 0.0)).Size(11.0, 11.0);
                }
                openFloorScenario = g.Stage(openFloor);
            }
        " + MazeRelease + @"
        ")
        .WithParameters(p => { p["golemName", typeof(string)] = golemName; })   // the same release for every golem; only who it is changes
        .PerformCommand();
    }
}
