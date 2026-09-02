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

    protected override void OnHydrated()
    {
        Birth();
    }

    private void Birth()
    {
        PerformCmd(@"
            upgrade('birth') {
                g = Golem();
            };
        ");
    }
}
