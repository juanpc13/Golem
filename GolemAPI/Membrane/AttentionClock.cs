using System.Collections.Concurrent;
using Puppeteer.Attention;

namespace GolemAPI.Membrane;

/// <summary>
/// THE ATTENTION CLOCK (propuesta 122, 21-oct-2026; the engine's `remember`, paper 0B — the training-lab's `puppeteer-remember` guide: "the clock
/// is a port… it promises liveness and nothing else"): the host's implementation of <see cref="IAttentionClock"/>, one <see cref="Timer"/> per
/// appointment. The actor hands it a DURATION (never a time) and, on the knock, reads its own present and decides: too early, it re-arms here;
/// due, it performs the body of the `remember` as a command of its own. A knock for an appointment already closed is ignored by the actor, so
/// a duplicate or an early knock costs nothing. A clock apart from <see cref="ClockSource"/>: that one is an INPUT (a tick is a command the
/// dispatch routes), this one only knocks.
/// </summary>
public sealed class AttentionClock : IAttentionClock
{
    private readonly ConcurrentDictionary<(string Actor, long Appointment), Timer> timers = new();
    private Action<WakeCall> handler;
    private int knocks;

    /// <summary>How many times it knocked so far (the lab's eyes).</summary>
    public int Knocks => knocks;

    public void ScheduleWake(string actorName, long appointmentId, TimeSpan delay)
    {
        if (delay < TimeSpan.Zero) delay = TimeSpan.Zero;
        var timer = new Timer(_ =>
        {
            Interlocked.Increment(ref knocks);
            handler?.Invoke(new WakeCall(actorName, appointmentId));
        }, null, delay, Timeout.InfiniteTimeSpan);
        timers.AddOrUpdate((actorName, appointmentId), timer, (_, old) => { old.Dispose(); return timer; });
    }

    public void CancelWake(string actorName, long appointmentId)
    {
        if (timers.TryRemove((actorName, appointmentId), out var timer)) timer.Dispose();
    }

    public void RegisterWakeHandler(Action<WakeCall> wakeHandler) => handler = wakeHandler ?? throw new ArgumentNullException(nameof(wakeHandler));
}
