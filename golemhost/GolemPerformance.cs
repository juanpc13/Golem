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

    // The releases: the golem is born, is given its world, and the world gets its rock.
    // The world is the golem's own knowledge — turtlesim has no physics — and every
    // golem runs this same script, so all of them share one world.
    protected override void OnHydrated()
    {
        Actor.Using(@"
            upgrade('init')     { g = Golem(); }
            upgrade('world_v1') { g.Inhabit(11.08, 0.6); }
            upgrade('rock_v1')  { g.PlaceRock(7.5, 4.5, 1.0); }
        ")
        .PerformCommand();
    }
}
