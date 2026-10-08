using Puppeteer;

namespace GolemAPI.Commanding;

// THE FIXED READS the panel's endpoints and the console share — each ONE query of the journal, written once (Juan, 16-sep-2026:
// "una estructura bien redactada… no armarse a punta de StringBuilder"): the board, the obstacles, the newest route, where the
// golem knows its body stands. A read asks; it decides nothing.
public static class Readings
{
    /// <summary>The board the panel paints from: the routes pending, the one underway found once and held in a local, how the last one ended.</summary>
    public static string Board(ActorV2 actor) =>
        actor.Using(@"
            {
                print g.PendingRoutes().Count 'pending', g.Routes().Count 'total', g.HasPendingMission() 'hasNext';
                if (g.HasPendingMission()) {
                    route = g.Underway();
                    print route.Id 'nextId', route.NextLeg.Target.X 'nextX', route.NextLeg.Target.Y 'nextY',
                          route.StopsLeft 'stopsLeft', route.Paused 'paused';
                }
                if (g.Routes().Count > 0) {
                    last = g.Newest();
                    print last.Id 'lastId', last.Status 'lastStatus';
                    if (last.EndedShort) {
                        print last.Why 'lastWhy';
                    }
                }
            }
        ")
        .PerformQuery();

    /// <summary>The obstacles the golem hypothesizes: one row per obstacle — its zone the map's answer (ajuste 54) — and, under it, one per vertex.</summary>
    public static string Obstacles(ActorV2 actor) =>
        actor.Using(@"
            print g.Current.Collisions.All().Count 'total', g.Current.Collisions.Things().Count 'things',
                  g.Current.Collisions.EncounterCount 'met', g.Current.Collisions.MarkCount 'marks';
            foreach (obstacles in g.Current.Collisions.All()) {
                print obstacles.Kind 'kind', g.Current.Map.ZoneNameOf(obstacles.Center) 'zone', obstacles.Shape 'shape', obstacles.Size 'size',
                      obstacles.Who 'who', obstacles.Center.X 'cx', obstacles.Center.Y 'cy';
                foreach (vertices in obstacles.Vertices()) {
                    print vertices.At.X 'x', vertices.At.Y 'y', vertices.Heading 'normal', vertices.Reach 'reach';
                }
            }
        ")
        .PerformQuery();

    /// <summary>The newest route, if any: its way as decided, the legs ahead, what it asks the body now, how it ended and why.</summary>
    public static string Route(ActorV2 actor) =>
        actor.Using(@"
            {
                print g.Routes().Count 'total';
                if (g.Routes().Count > 0) {
                    route = g.Newest();
                    print route.Id 'route', route.Status 'status', route.AsPlan() 'plan', route.LegsAhead.Count 'ahead', route.StopsLeft 'stopsLeft';
                    if (route.IsPending()) {
                        print route.Order 'action', route.Paused 'paused';
                        if (route.IsWalkable) {
                            print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                  route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading';
                        }
                    } else {
                        if (route.EndedShort) {
                            print route.Why 'why';
                        }
                    }
                }
            }
        ")
        .PerformQuery();

    /// <summary>Where the golem knows its body stands — the pose the acts brought it — and the zone that is in.</summary>
    public static string Where(ActorV2 actor) =>
        actor.Using(@"
            {
                print g.KnowsWhereItStands 'knows', g.Held 'held', g.Current.Name 'scenario', g.Navigation.Name 'navigation';
                if (g.KnowsWhereItStands) {
                    print g.Standing.X 'x', g.Standing.Y 'y', g.Standing.Heading 'heading', g.Current.Map.ZoneNameOf(g.Standing) 'zone';
                }
            }
        ")
        .PerformQuery();

    /// <summary>The scenarios the golem knows — each with what was learned in it — and the one it is in (ajuste 60).</summary>
    /// <summary>The formations the warden told the golem (propuesta 99): each one's name, figure, centre, side and orientation.</summary>
    public static string Formations(ActorV2 actor) =>
        actor.Using(@"
            {
                print g.Choreography.Formations().Count 'count';
                foreach (told in g.Choreography.Formations()) {
                    print told.Called 'called', told.Name 'shape', told.Center.X 'atX', told.Center.Y 'atY', told.Measure.InMeters 'length', told.Turn.InDegrees 'degrees', told.PlaceCount 'places';
                }
            }
        ")
        .PerformQuery();

    public static string Scenarios(ActorV2 actor) =>
        actor.Using(@"
            {
                print g.Current.Name 'current';
                foreach (known in g.Scenarios()) {
                    print known.Name 'name', known.Map.ZoneCount 'zones', known.Collisions.MarkCount 'marks';
                }
            }
        ")
        .PerformQuery();
}
