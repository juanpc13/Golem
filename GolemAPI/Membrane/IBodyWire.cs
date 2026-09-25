namespace GolemAPI.Membrane;

// THE WIRE TO THE BODY, as the host uses it (propuesta 52, fase 0, 23-sep-2026): what the body says — where it believes it
// stands, where it really is (the operator's eyes only), what it last touched — and what the host tells it — the order,
// published as the JSON the journal printed, and the lab's lever that puts it on a mark. Rosbridge is the wire to the
// simulator; a scenario test gives the golem another one (a body in a world held in memory). The domain knows nothing of
// it: an interface of the HOST, never of the domain (paper 09).
public interface IBodyWire : IAsyncDisposable
{
    /// <summary>Where the golem's idea of its own pose comes from: the world's truth, or the wheels' reckoning.</summary>
    PoseSource Source { get; }
    /// <summary>Where the golem believes its body is: THE pose the golem acts on. Null before the first word from the body.</summary>
    Pose LatestPose { get; }
    /// <summary>Where the body really is, for the operator's eyes only. Never for the golem.</summary>
    Pose LatestTruth { get; }
    /// <summary>What the body last touched, when, and where on its shell.</summary>
    Contact LatestContact { get; }
    /// <summary>Where the golem's orders travel to its body.</summary>
    string OrderTopic { get; }
    /// <summary>What the body reported on its result topic, as it said it (ajuste 55, 24-sep-2026): `{"order": 17, "result": "done"}`,
    /// `{"order", "result": "bumped", "x", "y", "heading", "bearing"}`, `{"order", "result": "stuck", "reason"}` — its only word back.</summary>
    event Action<string> ResultReported;

    Task ConnectAsync(CancellationToken ct);
    /// <summary>Declare what is published and subscribe what is watched: after this the body's words arrive.</summary>
    Task BindAsync(CancellationToken ct);
    /// <summary>A JSON document on a topic — the order to the body, as the golem printed it.</summary>
    Task PublishAsync(string topic, string json);
    /// <summary>The lab's lever: put the body on a mark, facing that way.</summary>
    Task TeleportAsync(double x, double y, double theta, CancellationToken ct);
}
