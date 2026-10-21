using Choreography.Input;

namespace GolemAPI.Membrane;

/// <summary>
/// THE CLOCK AS AN INPUT SOURCE (propuesta 118, 20-oct-2026; the training-lab's `puppeteer-input-surfaces` §3: "the clock is an input source —
/// an actor is the MERGE of N sources"): a timer that emits one <see cref="InputSignal"/> per tick — its id the tick's number (the dispatch's
/// idempotency key), its value the same number — and nothing else. A host implementation of <see cref="IInputSource"/>, like a keyboard or a
/// console would be; the golem's <c>Dispatch</c> routes every tick to the <c>Tick</c> message and runs the clock's questions on it, in series
/// with whatever else that dispatch consumes. The domain never reads the wall-clock: it is asked on the tick and answers from its journal.
/// </summary>
public sealed class ClockSource : IInputSource
{
    private readonly TimeSpan period;
    private int started;

    public ClockSource(TimeSpan period)
    {
        if (period <= TimeSpan.Zero) throw new ArgumentOutOfRangeException(nameof(period), "a clock ticks at a positive period");
        this.period = period;
    }

    public string SourceName => $"clock-{period.TotalMilliseconds:0}ms";

    // Since the 3.0 engine Start hands back an IInputSubscription; a clock remembers no position, so Stop and Dispose both stop the timer.
    public IInputSubscription Start(Action<InputSignal> onSignal)
    {
        if (onSignal == null) throw new ArgumentNullException(nameof(onSignal));
        if (Interlocked.Exchange(ref started, 1) == 1) throw new InvalidOperationException("a clock source is started once");
        long ticks = 0;
        var timer = new Timer(_ =>
        {
            long n = Interlocked.Increment(ref ticks);
            onSignal(new InputSignal($"tick-{n}", "clock", null, n.ToString(System.Globalization.CultureInfo.InvariantCulture)));
        }, null, period, period);
        return new Ticking(timer);
    }

    private sealed class Ticking : IInputSubscription
    {
        private readonly Timer timer;
        internal Ticking(Timer timer) => this.timer = timer;
        public void Stop() => timer.Dispose();
        public void Dispose() => timer.Dispose();
    }
}
