namespace GolemTest.World;

/// <summary>What the world said happened to a body: it touched something — a wall piece, a crate, another body — standing
/// there, facing that way, on that part of its shell. The world's testimony, never the golem's belief.</summary>
public sealed record WorldContact(string Golem, string With, double BodyX, double BodyY, double BodyHeading, double Bearing, int Sequence);

/// <summary>A way the golem's route held at some moment — `route.AsPlan()`, every leg of it — numbered in the SAME sequence as
/// the contacts, so a scenario can tell the story in order: the way first decided, the contact, the way decided again.</summary>
public sealed record WayDecided(string Golem, int Route, string Plan, int Sequence);

/// <summary>An errand sent to a golem and the PRINT its command returned — the same order the engine pushes to the body —
/// numbered in the same sequence as the contacts and the ways.</summary>
public sealed record ErrandSent(string Golem, string Stops, string Print, int Sequence)
{
    /// <summary>The handle of the route the errand opened, as its print says it (-1 when the print names none).</summary>
    public int Route
    {
        get
        {
            try
            {
                using var doc = System.Text.Json.JsonDocument.Parse(Print);
                return doc.RootElement.TryGetProperty("route", out var route) ? route.GetInt32() : -1;
            }
            catch (System.Text.Json.JsonException) { return -1; }
        }
    }
}

/// <summary>How a golem's newest errand ended, as the golem's own journal says it (`none` when it never had one).</summary>
public sealed record ErrandOutcome(string Status, int Bumps, int Marks, int Encounters);

// ONE FACE FOR BOTH WORLDS (propuesta 52): a scenario is written once against it — put crates, send errands, let the world
// run, ask what the golem concluded and what the world saw — and runs against the world held in memory (MockWorld) or,
// in fase 2, against Gazebo. The domain decides, the world says whether it COLLIDED, the scenario asserts.
public interface ILabWorld : IAsyncDisposable
{
    /// <summary>The golem takes part in the scenario, its body on its mark (sim/world/plan.json) — and nowhere else: a scenario that
    /// starts somewhere else WALKS the golem there with an errand of its own, leg by leg, validated like any other (Juan,
    /// 24-sep-2026: "quisiera validar todos los legs… desde la living a la zona norte"). Walk it there BEFORE the crates stand:
    /// a golem walking to its start must not learn the scenario's crate on the way.</summary>
    Task PlaceGolemAsync(string golem);
    /// <summary>A crate from the kiosk's table (west, center, east, big) stands in the world.</summary>
    void PlaceCrate(string spot);
    /// <summary>An errand for a golem: the points to visit, in order. Returns the print its command returned — what the route
    /// asks of the body now, in the robot's words.</summary>
    string Send(string golem, params (double X, double Y)[] stops);
    /// <summary>Every errand sent to the golem since it was placed, with the print each one returned.</summary>
    IReadOnlyList<ErrandSent> Errands(string golem);
    /// <summary>Errands for several golems that must SET OUT TOGETHER (two bodies meeting halfway): each body starts once every
    /// one of them holds its first order.</summary>
    Task SendTogetherAsync(params (string Golem, (double X, double Y) Stop)[] errands);
    /// <summary>Let the world run until no golem has anything pending and every body is quiet — a follower still driving after its
    /// leader too — or the timeout.</summary>
    Task RunUntilSettledAsync(TimeSpan timeout);
    /// <summary>How the golem's newest errand ended, in its own words.</summary>
    ErrandOutcome Outcome(string golem);
    /// <summary>Every contact the world saw on that golem's body, in order.</summary>
    IReadOnlyList<WorldContact> Contacts(string golem);
    /// <summary>Every way the golem's routes held since it was placed, in order, each with its route's handle: the first as decided,
    /// then each time it was decided again (a bump, a word from a peer, a stranded retreat). Asked of the golem's own journal.</summary>
    IReadOnlyList<WayDecided> Ways(string golem);
    /// <summary>The next leg of the golem's route that ENDED — completed, or cut short — waiting for it while the body moves:
    /// a scenario validates the legs one by one, as they happen, instead of only at the end.</summary>
    Task<LegReport> NextLegAsync(string golem, TimeSpan timeout);
    /// <summary>Every leg that ended since the golem was placed, in order.</summary>
    IReadOnlyList<LegReport> Legs(string golem);
    /// <summary>The next thing the golem's BODY reported — an order done (arrived, standing there) or a bump — as the emulator tells it,
    /// paired with the order it carried (Juan, 24-sep-2026: "cada arrive es algo que el emulador nos está contando").</summary>
    Task<BodyReport> NextReportAsync(string golem, TimeSpan timeout);
    /// <summary>The next LEG the body walked to its end, told by its own reports: reached — every order toward it done and the golem's
    /// next order headed elsewhere, or the body told to stand — or cut short by a bump. Nothing asked of the golem.</summary>
    Task<LegWalked> NextLegWalkedAsync(string golem, TimeSpan timeout);
    /// <summary>Every report the body made since the golem was placed, in order.</summary>
    IReadOnlyList<BodyReport> Reports(string golem);
    /// <summary>Every leg the body walked to its end since the golem was placed, in order.</summary>
    IReadOnlyList<LegWalked> LegsWalked(string golem);
    /// <summary>Where the body really went: its true position, sampled along the way, from its placing to now.</summary>
    IReadOnlyList<(double X, double Y)> Trail(string golem);
    /// <summary>Where the body really is, and facing which way.</summary>
    (double X, double Y, double Heading) TruePose(string golem);
}
