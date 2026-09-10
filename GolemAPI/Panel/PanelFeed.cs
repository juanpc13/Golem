using System.Threading.Channels;

namespace GolemAPI.Panel;

// One journal event as the panel sees it. Kind: "command" (journaled),
// "runtime" (ephemeral, never journaled), "info" (lifecycle).
public sealed record PanelEvent(long Entry, string Kind, string Script, string Note, DateTime At);

// The live feed behind the panel's SSE endpoint: recent history replayed to
// every new client, then a channel per client for what happens next. Emitted
// by the single journal writer at commit time — every event carries the entry
// the journal reached.
public sealed class PanelFeed
{
    private readonly List<PanelEvent> history = new();
    private readonly List<Channel<PanelEvent>> clients = new();
    private readonly object gate = new();

    public void Broadcast(PanelEvent e)
    {
        lock (gate)
        {
            history.Add(e);
            if (history.Count > 300) history.RemoveAt(0);
            foreach (var client in clients)
                client.Writer.TryWrite(e);
        }
    }

    public (IReadOnlyList<PanelEvent> Replay, ChannelReader<PanelEvent> Live, IDisposable Ticket) Attach()
    {
        var channel = Channel.CreateUnbounded<PanelEvent>();
        lock (gate)
        {
            var replay = history.ToArray();
            clients.Add(channel);
            return (replay, channel.Reader, new Ticket(() =>
            {
                lock (gate) clients.Remove(channel);
            }));
        }
    }

    private sealed class Ticket : IDisposable
    {
        private readonly Action dispose;
        internal Ticket(Action dispose) => this.dispose = dispose;
        public void Dispose() => dispose();
    }
}
