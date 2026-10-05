using Choreography.Theater;
using GolemAPI.Choreography;
using GolemAPI.Membrane;
using GolemAPI.Panel;
using Puppeteer;

namespace GolemAPI.Coordination;

// THE WARDEN'S MIND (propuesta 88, 2-oct-2026): the host of a subject without a body — the equivalent of GolemEmbodiment for the Warden.
// Every journal script of the warden lives here: the operator's calls (choreograph, rotate), the uptakes of the golems' words (where each
// stands, that it stands on its place, that it woke), the reads the panel asks. It decides nothing: the warden's domain does, in its
// journal; the words travel by the reactions WardenSpeech defines. No ROS, no mechanics: the warden drives nothing.
public sealed class WardenMind
{
    private readonly PerformanceV2 performance;
    private readonly ActorV2 wardenActor;
    private readonly PanelFeed feed;
    private readonly ITellWire wire;
    private readonly string warden;

    public WardenMind(PerformanceV2 performance, PanelFeed feed, ITellWire wire, string warden, IReadOnlyList<string> peers)
    {
        this.performance = performance;
        this.wardenActor = performance.Actor;
        this.feed = feed;
        this.wire = wire;
        this.warden = warden;
        Peers = peers ?? Array.Empty<string>();
    }

    /// <summary>The warden itself — the actor every script is performed on.</summary>
    public ActorV2 Actor => wardenActor;
    /// <summary>The warden's name: the journal's identity (env WARDEN).</summary>
    public string Name => warden;
    /// <summary>The golems it can reach by name — its tell routes: whom a call is told to, whom a line is carried to.</summary>
    public IReadOnlyList<string> Peers { get; }
    /// <summary>A line of the command language carried to a golem's console over the wire (`golem blue visit …`); null when no such golem.</summary>
    public Task<PeerReply> CommandPeerAsync(string peer, string line) => wire.CommandPeerAsync(peer, line);

    // ==================================================================
    // What the golems' words become in the WARDEN's journal (the uptakes WardenSpeech binds). Only plain @params; the objects built inside.
    // ==================================================================

    // A golem woke (or was carried) and says where it stands and in which scenario: the fleet is LEARNED from these words (Juan: "flota
    // aprendida") — the roster keeps the latest about each.
    public const string UptakeAwoke = @"
        {
            at = Position(@px, @py);
            w.Roster.Heard(@who, at, @scenario);
        }
        ";
    // A golem answers the call: where it stood. The warden records it in the convocation and in the roster; when with it the round is
    // complete, the LAW is applied once and the places SHARED — the act the reaction tells every golem on, the whole table as one word
    // (expose inside the block: the call, the round, the table and the centre to face). Nothing of the warden's moves.
    public const string UptakeStoodAt = @"
        {
            muster = w.Muster(@call);
            member = muster.Fleet.Member(@who);
            at = Position(@px, @py);
            muster.Stood(member, at);
            w.Roster.Heard(@who, at, @scenario);
            if (muster.IsComplete) {
                muster.Share();
                center = muster.Formation.Center;
                print muster.Call 'call', muster.Round 'round', muster.Table 'table';
                expose muster.Call call, muster.Round round, muster.Table table, center.X atX, center.Y atY;
            } else {
                print muster.StoodCount 'stood', muster.Fleet.Count 'of';
            }
        }
        ";
    // A golem stands on its place: recorded; when with it everybody is placed and a step waits, the STEP opens — the next table, shared
    // the same way (the reaction on Step tells it; the once carries the round).
    public const string UptakePlacedAt = @"
        {
            muster = w.Muster(@call);
            member = muster.Fleet.Member(@who);
            muster.Placed(member);
            w.Roster.Stands(@who, muster.PlaceOf(member));
            if (muster.CanStep) {
                muster.Step();
                center = muster.Formation.Center;
                print muster.Call 'call', muster.Round 'round', muster.Table 'table';
                expose muster.Call call, muster.Round round, muster.Table table, center.X atX, center.Y atY;
            } else {
                print muster.PlacedCount 'placed', muster.Fleet.Count 'of', muster.Queued 'queued';
            }
        }
        ";

    // ==================================================================
    // The operator's verbs on the warden.
    // ==================================================================

    // THE CALL: the formation by its name at a centre with a side, the fleet (told, or everybody the warden heard from) and the policy.
    // The warden convenes it — the act exposes what the golems need to answer: the formation, the fleet, the call, the policy — and
    // waits for their words; the places open when everybody spoke (UptakeStoodAt).
    private const string CallFormation = @"
                    {
                        center = Position(@cx, @cy);
                        side = Meters(@sideLength);
                        formation = w.Formation(@figure, center, side);
                        fleet = Fleet(@names);
                        muster = w.Muster(@callId, formation, fleet, @by);
                        muster.Convene();
                        print muster.Call 'call', fleet.Count 'of', muster.Policy 'by';
                        expose @figure shape, @cx atX, @cy atY, @sideLength length, @names crew, @callId call, @by policy;
                    }
                ";
    /// <summary>The fleet is CALLED to a formation: the figure, the centre, the side, the names (empty: everybody the warden heard from)
    /// and the policy. The golems are told and answer where they stand; the places are shared when everybody spoke.</summary>
    public Answer Call(string figure, (double X, double Y) center, double side, IReadOnlyList<string> fleet, string policy)
    {
        if (string.IsNullOrWhiteSpace(policy)) return Answer.Refusal("a call shares the places by rank or by distance");
        string callId = $"{warden}-{DateTime.UtcNow:yyyyMMddHHmmssfff}";   // one call, frozen as a parameter: the convocation's identity, the once of every word
        try
        {
            string names = fleet is { Count: > 0 } ? string.Join(",", fleet) : KnownNames();
            if (names == "") return Answer.Refusal("the warden heard from no golem yet: nobody to call (--fleet names them outright)");
            return Answer.Of(wardenActor.Using(CallFormation)
                .WithParameters(p => {
                    p["cx", typeof(double)] = Resolution.Metres(center.X);
                    p["cy", typeof(double)] = Resolution.Metres(center.Y);
                    p["sideLength", typeof(double)] = Resolution.Metres(side);
                    p["figure", typeof(string)] = figure;
                    p["callId", typeof(string)] = callId;
                    p["by", typeof(string)] = policy.Trim().ToLowerInvariant();
                    p["names", typeof(string)] = names;   // never "fleet": the script's own variable holds the object built from it
                })
                .PerformCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"{figure} at ({center.X:0.##}, {center.Y:0.##}), side {side:0.##}, by {policy}: " + GolemEmbodiment.Reason(ex)); }
    }

    // THE STEP: a move of the formation in place queued; opened at once when everybody already stands (the act exposes the next table).
    private const string RotateFormation = @"
                    {
                        muster = w.Current;
                        move = muster.Formation.Rotate(@sense);
                        muster.Queue(move, @stepId);
                        if (muster.CanStep) {
                            muster.Step();
                            center = muster.Formation.Center;
                            print muster.Call 'call', muster.Round 'round', muster.Table 'table';
                            expose muster.Call call, muster.Round round, muster.Table table, center.X atX, center.Y atY;
                        } else {
                            print muster.Queued 'queued', muster.PlacedCount 'placed', muster.Fleet.Count 'of';
                        }
                    }
                ";
    /// <summary>A STEP of the formation in place: every body takes the next place in that sense once everybody stands on its own; steps
    /// queue. Refused when the fleet stands in no formation.</summary>
    public Answer Rotate(string sense)
    {
        if (string.IsNullOrWhiteSpace(sense)) return Answer.Refusal("a step needs its sense: clockwise or counterclockwise");
        string stepId = $"{warden}-step-{DateTime.UtcNow:yyyyMMddHHmmssfff}";
        try
        {
            return Answer.Of(wardenActor.Using(RotateFormation)
                .WithParameters(p => {
                    p["sense", typeof(string)] = sense.Trim().ToLowerInvariant();
                    p["stepId", typeof(string)] = stepId;
                })
                .PerformCommand());
        }
        catch (Exception ex) { return Answer.Refusal($"rotate {sense}: " + GolemEmbodiment.Reason(ex)); }
    }

    // ==================================================================
    // The reads: what the warden knows, asked of its journal.
    // ==================================================================

    /// <summary>The names of every golem that spoke, comma-joined; "" when none.</summary>
    public string KnownNames()
    {
        var doc = System.Text.Json.JsonDocument.Parse(wardenActor.Using(@"
            foreach (golems in w.Roster.All()) {
                print golems.Name 'name';
            }
        ").PerformQuery());
        var names = new List<string>();
        Walk(doc.RootElement, e => { if (e.TryGetProperty("name", out var n)) names.Add(n.GetString()); });
        return string.Join(",", names.Where(n => !string.IsNullOrEmpty(n)).Distinct().OrderBy(n => n, StringComparer.Ordinal));
    }

    /// <summary>THE BOARD: every golem the warden heard from — where it said it stands, its scenario, how many words — and the formation in
    /// place if any: the call, the policy, the round, who stood, who is placed, the table. One query, JSON.</summary>
    public string Board() =>
        wardenActor.Using(@"
            {
                print w.Name 'warden', w.Roster.Count 'golems', w.Musters().Count 'calls';
                foreach (golems in w.Roster.All()) {
                    print golems.Name 'name', golems.Standing.X 'x', golems.Standing.Y 'y', golems.Scenario 'scenario', golems.Words 'words';
                }
                if (w.Musters().Count > 0) {
                    muster = w.Current;
                    print muster.Call 'call', muster.Formation.Name 'figure', muster.Policy 'by', muster.Round 'round',
                          muster.StoodCount 'stood', muster.Fleet.Count 'of', muster.IsShared 'shared', muster.PlacedCount 'placed', muster.Queued 'queued';
                    if (muster.IsShared) {
                        print muster.Table 'table';
                    }
                }
            }
        ")
        .PerformQuery();

    private static void Walk(System.Text.Json.JsonElement e, Action<System.Text.Json.JsonElement> onObject)
    {
        switch (e.ValueKind)
        {
            case System.Text.Json.JsonValueKind.Object: onObject(e); foreach (var p in e.EnumerateObject()) Walk(p.Value, onObject); break;
            case System.Text.Json.JsonValueKind.Array: foreach (var i in e.EnumerateArray()) Walk(i, onObject); break;
        }
    }

    internal void Note(string text)
    {
        Console.WriteLine($"[warden {warden}] {text}");
        feed.Broadcast(new PanelEvent(performance.CurrentEntryId, "runtime", "", text, DateTime.UtcNow));
    }
}
