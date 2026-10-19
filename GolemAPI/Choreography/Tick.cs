using Choreography.Dispatch;

namespace GolemAPI.Choreography;

/// <summary>
/// A TICK OF THE CLOCK as the dispatch's message (propuesta 118; the training-lab's `puppeteer-consume-and-dispatch`): the routing tag 'T' and the
/// tick's number — what the <see cref="GolemAPI.Membrane.ClockSource"/>'s signal becomes at the boundary, so the golem's dispatch runs the clock's
/// questions on it. The wire is ours: <c>raw[0]</c> the tag, the rest the number.
/// </summary>
public sealed class Tick : IDispatchMessage
{
    public static int TypeId => 'T';
    public long Number { get; private init; }

    public static IDispatchMessage Deserialize(string raw) =>
        new Tick { Number = long.TryParse(raw.AsSpan(1), System.Globalization.NumberStyles.Integer, System.Globalization.CultureInfo.InvariantCulture, out long n) ? n : 0 };
}
