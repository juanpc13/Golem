using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using Choreography.Transport.Brokered;

namespace GolemHost.Membrane;

// One broker record on the wire — the contract TellController receives.
public sealed record Frame(string Topic, string Key, Dictionary<string, string> Headers, string Value);

// Container-to-container IMessageBroker over plain HTTP: a route table maps a TOPIC
// to the peer that subscribes it, ProduceAsync POSTs the record to that peer's
// /tell endpoint, and the receiving TellController hands it to Deliver, which fans
// out to the local Subscribe handlers. BrokerTellTransport / ListenAs sit on top
// unchanged — swapping the medium never touches the tell layer.
//
// The IMessageBroker contract this adapter honors (transport guide):
//   * a delivery failure surfaces as a FAULTED task — the transport then journals
//     the non-delivery verdict (`tell … unacknowledged by <role>`); never a phantom send;
//   * records of one partition key stay in order (one gate per key);
//   * a per-topic retained log, replayed to a late subscriber, so a record that
//     arrives before the peer's listener is up is not lost;
//   * headers travel intact.
// Delivery policy (retries, timeout) lives here too: TELL_RETRY_SECONDS bounds how
// long an undeliverable record is retried before the verdict is given.
public sealed class HttpBroker : IMessageBroker
{
    private static readonly HttpClient Wire = new() { Timeout = TimeSpan.FromSeconds(10) };
    private const int RetainedPerTopic = 500;

    private readonly string whoAmI;
    private readonly IReadOnlyDictionary<string, Uri> routes;
    private readonly TimeSpan retryWindow;
    private readonly ConcurrentDictionary<string, List<Action<BrokerRecord>>> local = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<BrokerRecord>> retained = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> keyGates = new(StringComparer.Ordinal);

    public HttpBroker(string whoAmI, IReadOnlyDictionary<string, Uri> routes, TimeSpan retryWindow)
    {
        this.whoAmI = whoAmI;
        this.routes = routes;
        this.retryWindow = retryWindow;
    }

    // "topic=http://host:port,topic2=http://..." -> route table. Fails fast on a malformed entry.
    public static IReadOnlyDictionary<string, Uri> ParseRoutes(string spec)
    {
        var routes = new Dictionary<string, Uri>(StringComparer.Ordinal);
        if (string.IsNullOrWhiteSpace(spec)) return routes;
        foreach (var pair in spec.Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = pair.IndexOf('=');
            if (eq <= 0 || eq == pair.Length - 1)
                throw new ArgumentException($"TELL_ROUTES entry '{pair}' must look like topic=http://host:port");
            routes[pair[..eq]] = new Uri(pair[(eq + 1)..], UriKind.Absolute);
        }
        return routes;
    }

    public bool CanRoute(string topic) => routes.ContainsKey(topic) || local.ContainsKey(topic);

    // The distinct peers behind the route table — whoever this golem talks to.
    public IReadOnlyCollection<Uri> Peers => routes.Values.Distinct().ToArray();

    // Operator lever: ask a peer to reset itself too (best effort, short timeout).
    public async Task<bool> AskPeerAsync(Uri peer, string relativePath)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var answer = await Wire.PostAsync(new Uri(peer, relativePath), null, cts.Token);
            return answer.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[wire {whoAmI}] {peer} did not take {relativePath}: {ex.Message}");
            return false;
        }
    }

    public async Task ProduceAsync(string topic, string key,
        IReadOnlyDictionary<string, string> headers, string value,
        CancellationToken cancellationToken = default)
    {
        // A topic subscribed locally is delivered locally (self-addressed acks).
        if (local.ContainsKey(topic))
        {
            Deliver(topic, key, headers, value);
            return;
        }

        if (!routes.TryGetValue(topic, out Uri peer))
            throw new InvalidOperationException($"[wire {whoAmI}] no route for topic '{topic}' — bind it in TELL_ROUTES");

        var frame = new Frame(topic, key,
            headers == null ? new Dictionary<string, string>() : new Dictionary<string, string>(headers), value);
        string body = JsonSerializer.Serialize(frame);

        // One gate per partition key: records to one instance keep their order.
        SemaphoreSlim gate = keyGates.GetOrAdd(key ?? string.Empty, _ => new SemaphoreSlim(1, 1));
        await gate.WaitAsync(cancellationToken);
        try
        {
            var deadline = DateTime.UtcNow + retryWindow;
            Exception last = null;
            while (true)
            {
                try
                {
                    using var content = new StringContent(body, Encoding.UTF8, "application/json");
                    using var answer = await Wire.PostAsync(new Uri(peer, "tell"), content, cancellationToken);
                    if (answer.IsSuccessStatusCode) return; // consumed by the peer
                    last = new HttpRequestException($"{(int)answer.StatusCode} from {peer} for '{topic}'");
                }
                catch (HttpRequestException ex) { last = ex; }
                catch (TaskCanceledException ex) when (!cancellationToken.IsCancellationRequested) { last = ex; }

                if (DateTime.UtcNow >= deadline)
                    throw new InvalidOperationException(
                        $"[wire {whoAmI}] '{topic}' undeliverable to {peer} after {retryWindow.TotalSeconds:0}s", last);
                await Task.Delay(TimeSpan.FromSeconds(2), cancellationToken);
            }
        }
        finally
        {
            gate.Release();
        }
    }

    public IDisposable Subscribe(string topic, Action<BrokerRecord> onRecord)
    {
        var handlers = local.GetOrAdd(topic, _ => new List<Action<BrokerRecord>>());
        lock (handlers) handlers.Add(onRecord);

        // A late subscriber still sees what already arrived on its topic.
        BrokerRecord[] backlog;
        var log = retained.GetOrAdd(topic, _ => new List<BrokerRecord>());
        lock (log) backlog = log.ToArray();
        foreach (var record in backlog)
        {
            try { onRecord(record); }
            catch (Exception ex) { Console.WriteLine($"[wire {whoAmI}] replay on '{topic}' failed: {ex.Message}"); }
        }

        return new Subscription(() => { lock (handlers) handlers.Remove(onRecord); });
    }

    // Entry point for the TellController: a record arrived over HTTP.
    // True when at least one local handler consumed it without throwing.
    public bool Deliver(string topic, string key, IReadOnlyDictionary<string, string> headers, string value)
    {
        var record = new BrokerRecord(topic, key, headers ?? new Dictionary<string, string>(), value);
        var log = retained.GetOrAdd(topic, _ => new List<BrokerRecord>());
        lock (log)
        {
            log.Add(record);
            if (log.Count > RetainedPerTopic) log.RemoveAt(0);
        }

        if (!local.TryGetValue(topic, out var handlers)) return false;
        Action<BrokerRecord>[] snapshot;
        lock (handlers) snapshot = handlers.ToArray();

        bool consumed = false;
        foreach (var handler in snapshot)
        {
            try { handler(record); consumed = true; }
            catch (Exception ex) { Console.WriteLine($"[wire {whoAmI}] handler failed on '{topic}': {ex.Message}"); }
        }
        return consumed;
    }

    private sealed class Subscription : IDisposable
    {
        private readonly Action dispose;
        internal Subscription(Action dispose) => this.dispose = dispose;
        public void Dispose() => dispose();
    }
}
