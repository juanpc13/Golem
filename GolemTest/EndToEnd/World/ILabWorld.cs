namespace GolemTest.World;

/// <summary>What the world said happened to a body: it touched something — a wall piece, a crate, another body — standing
/// there, facing that way, on that part of its shell. The world's testimony, never the golem's belief.</summary>
public sealed record WorldContact(string Golem, string With, double BodyX, double BodyY, double BodyHeading, double Bearing, int Sequence);

/// <summary>How a golem's newest errand ended, as the golem's own journal says it.</summary>
public sealed record ErrandOutcome(string Status, int Bumps, int Marks, int Encounters);

// ONE FACE FOR BOTH WORLDS (propuesta 52): a scenario is written once against it — put crates, send errands, let the world
// run, ask what the golem concluded and what the world saw — and runs against the world held in memory (FloorWorld) or,
// in fase 2, against Gazebo. The domain decides, the world says whether it COLLIDED, the scenario asserts.
public interface ILabWorld : IAsyncDisposable
{
    /// <summary>A crate from the kiosk's table (west, center, east, big) stands in the world.</summary>
    void PlaceCrate(string spot);
    /// <summary>An errand for a golem: the points to visit, in order.</summary>
    void Send(string golem, params (double X, double Y)[] stops);
    /// <summary>Let the world run until no golem has anything pending and the bodies are quiet — or the timeout.</summary>
    Task RunUntilSettledAsync(TimeSpan timeout);
    /// <summary>How the golem's newest errand ended, in its own words.</summary>
    ErrandOutcome Outcome(string golem);
    /// <summary>Every contact the world saw on that golem's body, in order.</summary>
    IReadOnlyList<WorldContact> Contacts(string golem);
    /// <summary>Where the body really is, and facing which way.</summary>
    (double X, double Y, double Heading) TruePose(string golem);
}
