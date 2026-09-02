using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Choreography.Transport.Brokered;

namespace GolemHost.Membrane;

// Container-to-container IMessageBroker over plain HTTP, after VeladaApp's
// PhoneToPhone (the bingo): a route table maps a TOPIC to the peer that
// subscribes it, ProduceAsync POSTs the record to that peer's /tell endpoint,
// and the receiving side's TellController hands it to Deliver(), which fans
// out to the local Subscribe handlers. BrokerTellTransport/ListenAs sit on
// top unchanged — swapping the medium never touches the tell layer.
//
// Dev-grade on purpose: a short retry instead of PhoneToPhone's durable
// outbox. If the peer stays unreachable the record is dropped and logged —
// production swaps in a real broker (or grows the outbox), nothing else changes.
public sealed class HttpBroker : IMessageBroker
{
    private static readonly HttpClient Wire = new() { Timeout = TimeSpan.FromSeconds(10) };

    private readonly string whoAmI;
    private readonly IReadOnlyDictionary<string, Uri> routes; // topic -> peer base url
    private readonly ConcurrentDictionary<string, List<Action<BrokerRecord>>> local =
        new(StringComparer.Ordinal);

    public HttpBroker(string whoAmI, IReadOnlyDictionary<string, Uri> routes)
    {
        this.whoAmI = whoAmI;
        this.routes = routes;
    }

    // "topic=http://host:port,topic2=http://..." -> route table.
    public static IReadOnlyDictionary<string, Uri> ParseRoutes(string spec)
    {
        var routes = new Dictionary<string, Uri>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(spec)) return routes;
        foreach (var pair in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = pair.IndexOf('=');
            routes[pair[..eq]] = new Uri(pair[(eq + 1)..], UriKind.Absolute);
        }
        return routes;
    }

    private sealed record Frame(string Topic, string Key, Dictionary<string, string> Headers, string Value);

    public async Task ProduceAsync(string topic, string key,
        IReadOnlyDictionary<string, string> headers, string value,
        CancellationToken cancellationToken = default)
    {
        // A topic subscribed LOCALLY is delivered locally too (self-addressed acks).
        if (local.ContainsKey(topic))
        {
            Deliver(topic, key, headers, value);
            return;
        }

        if (!routes.TryGetValue(topic, out Uri peer))
        {
            Console.WriteLine($"[wire {whoAmI}] no route for topic '{topic}' — record dropped");
            return;
        }

        var frame = new Frame(topic, key,
            headers == null ? new Dictionary<string, string>() : new Dictionary<string, string>(headers),
            value);
        var body = JsonSerializer.Serialize(frame);

        for (int attempt = 1; attempt <= 5; attempt++)
        {
            try
            {
                using var content = new StringContent(body, Encoding.UTF8, "application/json");
                using var answer = await Wire.PostAsync(new Uri(peer, "tell"), content, cancellationToken);
                if (answer.IsSuccessStatusCode) return;
            }
            catch (Exception) when (attempt < 5)
            {
            }
            await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
        }
        Console.WriteLine($"[wire {whoAmI}] could not reach {peer} for topic '{topic}' — record dropped");
    }

    public IDisposable Subscribe(string topic, Action<BrokerRecord> onRecord)
    {
        var handlers = local.GetOrAdd(topic, _ => new List<Action<BrokerRecord>>());
        lock (handlers) handlers.Add(onRecord);
        return new Subscription(() => { lock (handlers) handlers.Remove(onRecord); });
    }

    // Entry point for the TellController: a record arrived over HTTP.
    public void Deliver(string topic, string key, IReadOnlyDictionary<string, string> headers, string value)
    {
        if (!local.TryGetValue(topic, out var handlers))
        {
            Console.WriteLine($"[wire {whoAmI}] record for '{topic}' but nobody listens here — dropped");
            return;
        }
        var record = new BrokerRecord(topic, key,
            headers ?? new Dictionary<string, string>(), value);
        Action<BrokerRecord>[] snapshot;
        lock (handlers) snapshot = handlers.ToArray();
        foreach (var handler in snapshot)
        {
            try { handler(record); }
            catch (Exception ex) { Console.WriteLine($"[wire {whoAmI}] handler failed on '{topic}': {ex.Message}"); }
        }
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action dispose;
        internal Subscription(Action dispose) => this.dispose = dispose;
        public void Dispose() => dispose();
    }
}
