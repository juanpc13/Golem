using System.Collections.Concurrent;
using System.Text;
using System.Text.Json;
using System.Threading.Channels;
using Choreography.Transport.Brokered;

namespace GolemAPI.Membrane;

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
//
// AJUSTE 76 (1-oct-2026): /tell ANSWERS ON RECEIPT. The handler a record is handed to is Puppeteer's BrokerTellConsumer,
// which runs the uptake and then emits the ACK synchronously (ProduceAsync(...).GetAwaiter().GetResult()); run inside the
// request, that held the teller's POST for as long as the ack took — up to the retry window when nobody consumed the ack
// topic at the origin — so the teller's client timed out (10 s), retried (2 s) and every other tell behind the same key
// gate waited: 12 to 18 s between golems convening at once (lab, 1-oct). Now Deliver retains the record, QUEUES it and
// returns; one consumer per topic applies the records in the order they arrived (what the broker promises per key); and
// an ack — the transport's receipt, no lived fact — goes out ONCE, with a short timeout, outside the key gate, no retry.
public sealed class HttpBroker : ITellWire
{
    private static readonly HttpClient Wire = new() { Timeout = TimeSpan.FromSeconds(10) };
    private static readonly TimeSpan AckTimeout = TimeSpan.FromSeconds(3);
    private const int RetainedPerTopic = 500;

    // Every frame carries where it came from, and a tell's id is remembered with its origin: the ack
    // the hearer emits for that tell id goes back to whoever told, not to the one URL a route table
    // can hold — so several peers can tell the same golem and each gets its own acks.
    private const string OriginHeader = "golem-origin";
    private const string TellIdHeader = "puppeteer-tell-id";

    private readonly string whoAmI;
    private readonly Uri myUrl;
    private readonly IReadOnlyDictionary<string, Uri> routes;
    private readonly TimeSpan retryWindow;
    private readonly ConcurrentDictionary<string, List<Action<BrokerRecord>>> local = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, List<BrokerRecord>> retained = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, SemaphoreSlim> keyGates = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Uri> originOfTell = new(StringComparer.Ordinal);
    private readonly ConcurrentDictionary<string, Channel<BrokerRecord>> queues = new(StringComparer.Ordinal);   // one queue, one consumer per topic (ajuste 76)

    public HttpBroker(string whoAmI, IReadOnlyDictionary<string, Uri> routes, TimeSpan retryWindow, Uri myUrl = null)
    {
        this.whoAmI = whoAmI;
        this.routes = routes;
        this.retryWindow = retryWindow;
        this.myUrl = myUrl;
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
    public async Task<bool> AskPeerAsync(Uri peer, string relativePath, string json)
    {
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(3));
            using var body = new StringContent(json ?? "{}", System.Text.Encoding.UTF8, "application/json");
            using var answer = await Wire.PostAsync(new Uri(peer, relativePath), body, cts.Token);
            return answer.IsSuccessStatusCode;
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[wire {whoAmI}] {peer} did not take {relativePath}: {ex.Message}");
            return false;
        }
    }

    // A command line carried to a peer's console: the peer is found by its tell route (`tell-<name>`), the line goes to its
    // /command as the panel's would, and its text comes back with the status — the console that fans out shows it per golem.
    public async Task<PeerReply> CommandPeerAsync(string peer, string line)
    {
        if (peer == null) throw new ArgumentNullException(nameof(peer));
        if (line == null) throw new ArgumentNullException(nameof(line));
        if (!routes.TryGetValue("tell-" + peer, out Uri url)) return null;
        try
        {
            using var cts = new CancellationTokenSource(TimeSpan.FromSeconds(8));
            using var body = new StringContent(JsonSerializer.Serialize(new { line }), Encoding.UTF8, "application/json");
            using var answer = await Wire.PostAsync(new Uri(url, "command"), body, cts.Token);
            return new PeerReply((int)answer.StatusCode, await answer.Content.ReadAsStringAsync(cts.Token));
        }
        catch (Exception ex)
        {
            Console.WriteLine($"[wire {whoAmI}] {peer} did not answer the line: {ex.Message}");
            return new PeerReply(0, $"{peer} did not answer: {ex.Message}");
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

        // An ack goes back to whoever told; anything else follows the route table.
        bool isAck = topic.EndsWith(".acks", StringComparison.Ordinal);
        Uri peer = null;
        if (isAck && headers != null
            && headers.TryGetValue(TellIdHeader, out string tellId) && originOfTell.TryRemove(tellId, out Uri origin))
            peer = origin;
        if (peer == null && !routes.TryGetValue(topic, out peer))
            throw new InvalidOperationException($"[wire {whoAmI}] no route for topic '{topic}' — bind it in TELL_ROUTES");

        var outgoing = headers == null ? new Dictionary<string, string>() : new Dictionary<string, string>(headers);
        if (myUrl != null) outgoing[OriginHeader] = myUrl.ToString();
        var frame = new Frame(topic, key, outgoing, value);
        string body = JsonSerializer.Serialize(frame);

        // An ack is the transport's receipt: one attempt, short, outside the key gate, no retry (ajuste 76). Lost, it only
        // delays settlement — the consumer notes it and the origin resolves on its next recovery citation.
        if (isAck)
        {
            using var brief = CancellationTokenSource.CreateLinkedTokenSource(cancellationToken);
            brief.CancelAfter(AckTimeout);
            using var content = new StringContent(body, Encoding.UTF8, "application/json");
            using var answer = await Wire.PostAsync(new Uri(peer, "tell"), content, brief.Token);
            if (!answer.IsSuccessStatusCode)
                throw new HttpRequestException($"{(int)answer.StatusCode} from {peer} for the ack on '{topic}'");
            return;
        }

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
        QueueOf(topic);   // the topic's consumer is up before anything is queued for it

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

    // Entry point for the TellController: a record arrived over HTTP. It is retained and QUEUED for the topic's consumer, and
    // the request returns at once (ajuste 76): true when this golem subscribes the topic — the handlers run off the request,
    // in arrival order — false when the topic is nobody's here (503: the origin keeps retrying).
    public bool Deliver(string topic, string key, IReadOnlyDictionary<string, string> headers, string value)
    {
        // A tell that arrives remembers who told it, so its ack can find the way back.
        if (headers != null && !topic.EndsWith(".acks", StringComparison.Ordinal)
            && headers.TryGetValue(TellIdHeader, out string tellId) && headers.TryGetValue(OriginHeader, out string origin)
            && Uri.TryCreate(origin, UriKind.Absolute, out Uri from))
            originOfTell[tellId] = from;

        var record = new BrokerRecord(topic, key, headers ?? new Dictionary<string, string>(), value);
        var log = retained.GetOrAdd(topic, _ => new List<BrokerRecord>());
        lock (log)
        {
            log.Add(record);
            if (log.Count > RetainedPerTopic) log.RemoveAt(0);
        }

        if (!local.ContainsKey(topic)) return false;
        return QueueOf(topic).Writer.TryWrite(record);
    }

    // The topic's queue, its consumer started with it: one record at a time, in the order they came, each handed to every
    // handler of the topic; a handler that throws is noted and the next record follows.
    private Channel<BrokerRecord> QueueOf(string topic) => queues.GetOrAdd(topic, t =>
    {
        var queue = Channel.CreateUnbounded<BrokerRecord>(new UnboundedChannelOptions { SingleReader = true });
        _ = Task.Run(async () =>
        {
            await foreach (var record in queue.Reader.ReadAllAsync())
            {
                if (!local.TryGetValue(t, out var handlers)) continue;
                Action<BrokerRecord>[] snapshot;
                lock (handlers) snapshot = handlers.ToArray();
                foreach (var handler in snapshot)
                {
                    try { handler(record); }
                    catch (Exception ex) { Console.WriteLine($"[wire {whoAmI}] handler failed on '{t}': {ex.Message}"); }
                }
            }
        });
        return queue;
    });

    private sealed class Subscription : IDisposable
    {
        private readonly Action dispose;
        internal Subscription(Action dispose) => this.dispose = dispose;
        public void Dispose() => dispose();
    }
}
