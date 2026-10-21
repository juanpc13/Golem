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
            print g.Current.Collisions.Present().Count 'total', g.Current.Collisions.Things().Count 'things',
                  g.Current.Collisions.PeersInTheWay().Count 'met', g.Current.Collisions.MarkCount 'marks';
            foreach (obstacles in g.Current.Collisions.Present()) {
                print obstacles.Kind 'kind', g.Current.Map.ZoneNameOf(obstacles.Center) 'zone', obstacles.Shape 'shape', obstacles.Size 'size',
                      obstacles.Who 'who', obstacles.Center.X 'cx', obstacles.Center.Y 'cy';
                foreach (vertices in obstacles.Vertices()) {
                    print vertices.At.X 'x', vertices.At.Y 'y', vertices.Heading 'normal', vertices.Reach 'reach';
                }
            }
        ")
        .PerformQuery();

    /// <summary>WHAT THE GOLEM EXPLORED in the scenario it is in (propuesta 120): the trail's strides and metres, the cells it knows (free where the
    /// trail crossed, wall where a touch's normal pointed) and the frontier — the unknown next to the known — as its exploration module derives them.</summary>
    public static string Explored(ActorV2 actor) =>
        actor.Using(@"
            {
                explored = g.Exploration;
                print g.Current.Name 'scenario', explored.Grain 'grain', explored.StrideCount 'strides', explored.Distance 'metres',
                      explored.Covered 'covered', explored.WallCount 'walls', explored.FrontierCount 'frontier', explored.IsComplete 'complete',
                      explored.Exploring 'exploring', explored.Finished 'finished', explored.Probes 'probes', explored.Policy 'policy';
                foreach (cells in explored.Cells()) {
                    print cells.Col 'col', cells.Row 'row', cells.Kind 'kind', cells.X 'x', cells.Y 'y';
                }
                foreach (unknown in explored.Frontier()) {
                    print unknown.X 'fx', unknown.Y 'fy';
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
                        print route.Order 'action', route.Waiting 'waiting', route.Paused 'paused';
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
                print g.KnowsWhereItStands 'knows', g.Held 'held', g.Current.Name 'scenario', g.Navigation.Name 'navigation', body.Speed.InMetersPerSecond 'speed', body.TurnRate.InRadiansPerSecond 'turn';
                if (g.KnowsWhereItStands) {
                    print g.Standing.X 'x', g.Standing.Y 'y', g.Standing.Heading 'heading', g.Current.Map.ZoneNameOf(g.Standing) 'zone';
                }
            }
        ")
        .PerformQuery();

    /// <summary>The scenarios the golem knows — each with what was learned in it — and the one it is in (ajuste 60).</summary>
    /// <summary>THE FORMATIONS THE GOLEM IS IN (propuesta 104: the warden's GET): each one's name, figure, centre, measure, orientation, policy,
    /// fleet, places, how many stand on theirs, the steps queued, the round, this golem's place — and WHO HOLDS WHAT, as this copy knows it.</summary>
    public static string Formations(ActorV2 actor) =>
        actor.Using(@"
            {
                print g.Choreography.Formations().Count 'count';
                foreach (told in g.Choreography.Formations()) {
                    print told.Name 'called', told.Figure.Name 'shape', told.Figure.Center.X 'atX', told.Figure.Center.Y 'atY',
                          told.Figure.Measure.InMeters 'length', told.Figure.InnerMeters 'inner', told.Figure.Arms 'arms', told.Figure.Turn.InDegrees 'degrees', told.Policy 'policy',
                          told.Fleet.Roster 'crew', told.PlaceCount 'places', told.PlacedCount 'placed', told.AlignedCount 'aligned', told.Stranded 'stranded', told.StrandedWhy 'strandedWhy', told.Queued 'queued', told.Round 'round', told.PlaceIndex 'mine', told.HasRoute 'routed',
                          told.CueTo 'cueTo', told.OnCue 'onCue', told.IsCued 'cued', told.Finishing 'finishing', told.FinishedCount 'finished', told.IsFinished 'done', told.CueGiven 'cueGiven',
                          told.After 'after', told.AskTo 'askTo', told.Asked 'asked', told.NotYet 'notYet';
                    foreach (holders in told.Holders()) {
                        print holders.Name 'who', holders.Index 'place', holders.X 'x', holders.Y 'y', holders.Orbit 'ring';
                    }
                    foreach (spots in told.Figure.Places(told.Fleet.Count)) {
                        print spots.X 'x', spots.Y 'y';
                    }
                }
            }
        ")
        .PerformQuery();

    /// <summary>THE DISPUTES (14-oct-2026): the places this golem disputed with another by distance — the formation, the place, the rival, whether
    /// it won or yielded, and both distances.</summary>
    public static string Disputes(ActorV2 actor) =>
        actor.Using(@"
            {
                print g.Choreography.Disputes().Count 'count';
                foreach (disputes in g.Choreography.Disputes()) {
                    print disputes.Called 'called', disputes.Place 'place', disputes.At.X 'x', disputes.At.Y 'y', disputes.Rival 'rival',
                          disputes.Won 'won', disputes.Mine 'mine', disputes.Theirs 'theirs', disputes.Game 'game';
                }
            }
        ")
        .PerformQuery();

    /// <summary>THE PLAY THE GOLEM IS IN (propuesta 108): its title, this golem's part, the state, pacman's heading, the runs made.</summary>
    public static string Play(ActorV2 actor) =>
        actor.Using(@"
            {
                print g.Dynamics.Knows 'playing';
                if (g.Dynamics.Knows) {
                    play = g.Dynamics.Pacman;
                    print play.Name 'play', play.Kind 'kind', play.Stamp 'nonce', g.Dynamics.Me 'part', play.State 'state', play.Cast.Pacman 'pacman', play.Cast.Roster 'ghosts',
                          play.Heading 'heading', play.Runs 'runs',
                          play.HasSeen 'seen', play.RunsSeen 'runsSeen', play.Chases 'chases', g.Dynamics.Released 'released',
                          play.WasCaught 'caught', play.Catcher 'catcher', play.Started 'started';
                    if (play.HasSeen) {
                        print play.LastSeen.X 'seenX', play.LastSeen.Y 'seenY';
                    }
                }
            }
        ")
        .PerformQuery();

    /// <summary>EVERY PLAY the golem was told (16-oct-2026): name, kind, state, cast, who ended it — the warden's list, the ended ones among them.</summary>
    public static string Plays(ActorV2 actor) =>
        actor.Using(@"
            {
                print g.Dynamics.Plays().Count 'count';
                foreach (told in g.Dynamics.Plays()) {
                    print told.Name 'called', told.Kind 'kind', told.Stamp 'nonce', told.State 'state', told.Cast.Pacman 'pacman', told.Cast.Roster 'ghosts',
                          told.WasCaught 'caught', told.Catcher 'catcher';
                }
            }
        ")
        .PerformQuery();

    public static string Scenarios(ActorV2 actor) =>
        actor.Using(@"
            {
                print g.Current.Name 'current';
                foreach (known in g.Scenarios()) {
                    print known.Name 'name', known.Map.ZoneCount 'zones', known.Collisions.MarkCount 'marks', known.Exported 'exported', known.Frame 'frame';
                }
            }
        ")
        .PerformQuery();
}
