using System.Text.Json;

namespace GolemTest.World;

/// <summary>What the golem's BODY reported, as the emulator tells it (Juan, 24-sep-2026: "cada arrive es algo que el emulador nos
/// está contando: esos son los assert"): the order it carried — the ticket, the action and the amount, and the leg and the point the
/// golem said it was headed to — and what it said at the end: `arrived` (the order done, standing there), `bumped` (pressed on its shell
/// at that bearing, standing there, and what the world saw it touch) or `stuck` (why). The order and the result meet on the ticket,
/// the only thing the body echoes (ajuste 55).</summary>
public sealed record BodyReport(string Golem, int Ticket, int Route, string Said, string Action, double Amount, string Leg, string Kind,
    (double X, double Y) Toward, (double X, double Y, double Heading) Stands, double Bearing, IReadOnlyList<string> Touched, string Reason, int Sequence)
{
    public override string ToString() =>
        $"{Golem} {Said} · order {Ticket} of route {Route} · {Action} {Amount:0.00} toward {Leg} ({Toward.X:0.00}, {Toward.Y:0.00})"
        + $" · stands at ({Stands.X:0.00}, {Stands.Y:0.00}) facing {Stands.Heading:0.00}"
        + (Touched.Count > 0 ? " · touched " + string.Join(", ", Touched) : "") + (Reason != "" ? " · " + Reason : "");
}

/// <summary>A LEG of the way as the body WALKED it, told by its own results: reached — every order toward it done, and the golem's next
/// order headed elsewhere, or the advance to the errand's last stop done — or cut short (a bump, a body stuck: `Why` says).
/// `Point` is where the last order toward it went (a door's exit, a stop's point); `Stands` where the body stood at the end.</summary>
public sealed record LegWalked(string Golem, int Route, string Leg, string Kind, (double X, double Y) Point,
    (double X, double Y, double Heading) Stands, bool Reached, string Why, IReadOnlyList<string> Touched, int Orders, int Sequence)
{
    public double Off => Math.Sqrt((Stands.X - Point.X) * (Stands.X - Point.X) + (Stands.Y - Point.Y) * (Stands.Y - Point.Y));

    /// <summary>The leg in one line: `✓ living/south   → (4.60, 1.50)   stands at (4.60, 1.50), 0.00 m off, 4 order(s)`.</summary>
    public string Line() =>
        $"{(Reached ? "✓" : "✗")} {Leg,-14} → ({Point.X:0.00}, {Point.Y:0.00})   "
        + (Reached ? $"stands at ({Stands.X:0.00}, {Stands.Y:0.00}), {Off:0.00} m off, {Orders} order(s)"
                   : $"cut at ({Stands.X:0.00}, {Stands.Y:0.00}): {Why}");
}

// THE BODY'S RESULTS, MET WITH THE ORDERS ON THE TICKET (propuesta 52, ajuste 55, 24-sep-2026; Juan: "lo que me interesa es lo que el
// simulador me puede decir… una vez hecho el send, los asserts son para cuando un tramo llegó a su destino"). Both worlds feed it the
// two words that meet on the wire: the golem's — every order it told its body, whole, on its feed (kind 'order': the ticket, the action,
// the amount, and the leg and the point it heads to) — and the body's — the result on its own topic: `{"order": 17, "result": "done"}`,
// `bumped` with where it stood and where on its shell, `stuck` with why. A LEG is the run of orders toward the same named leg of the
// same route (a door: its approach, then its exit; a stop is its point); it is done once every order of it has its result and the
// next order heads elsewhere — or the advance to the errand's last stop is done: the route completed there and nothing more is told to
// the body — and cut the moment a bump or a stuck answers one of its orders. Nothing here asks the golem: it only listens.
internal sealed class BodyReports
{
    private sealed class Order
    {
        public int Ticket; public int Route; public string Action; public double Amount; public string Kind; public string Leg;
        public (double X, double Y) Toward; public bool LastStop; public int Sequence;
        public BodyReport Answer;    // the result that ended it (a bump or a stuck voids the orders left open with it)
        // the leg an order belongs to: a door is one leg with two points (its approach, then its exit), a stop is its point
        public (int, string, double, double) Identity => Kind == "stop" ? (Route, Leg, Toward.X, Toward.Y) : (Route, Leg, 0.0, 0.0);
    }

    private sealed class Walk
    {
        public readonly List<Order> Orders = new();
        public int Closed;   // the orders before this index belong to legs already told
    }

    private readonly Dictionary<string, Walk> walks = new();
    private readonly List<BodyReport> reports = new();
    private readonly List<LegWalked> legs = new();
    private readonly Dictionary<string, int> readReports = new(), readLegs = new();
    private readonly object gate = new();

    public IReadOnlyList<BodyReport> Of(string golem) { lock (gate) return reports.Where(r => r.Golem == golem).ToList(); }
    public IReadOnlyList<LegWalked> LegsOf(string golem) { lock (gate) return legs.Where(l => l.Golem == golem).ToList(); }

    /// <summary>The sequence of the golem's last result; 0 when its body said nothing yet.</summary>
    public int LastReportSequence(string golem) { lock (gate) return reports.LastOrDefault(r => r.Golem == golem)?.Sequence ?? 0; }

    public void Forget(string golem)
    {
        lock (gate)
        {
            walks.Remove(golem);
            reports.RemoveAll(r => r.Golem == golem);
            legs.RemoveAll(l => l.Golem == golem);
            readReports.Remove(golem);
            readLegs.Remove(golem);
        }
    }

    /// <summary>The next result of that golem's body not handed out yet, or null.</summary>
    public BodyReport NextReport(string golem) => Next(golem, reports, readReports, r => r.Golem);

    /// <summary>The next leg of that golem walked to its end and not handed out yet, or null.</summary>
    public LegWalked NextLeg(string golem) => Next(golem, legs, readLegs, l => l.Golem);

    private T Next<T>(string golem, List<T> all, Dictionary<string, int> read, Func<T, string> owner) where T : class
    {
        lock (gate)
        {
            var mine = all.Where(x => owner(x) == golem).ToList();
            int i = read.TryGetValue(golem, out var n) ? n : 0;
            if (i >= mine.Count) return null;
            read[golem] = i + 1;
            return mine[i];
        }
    }

    /// <summary>An order the golem told its body, whole, as its feed shows it: `{"order", "route", "action", "amount", "kind", "name", "x", "y", …}`.</summary>
    public void Ordered(string golem, string json, int sequence)
    {
        using var doc = JsonDocument.Parse(json);
        var o = doc.RootElement;
        string action = o.TryGetProperty("action", out var a) ? a.GetString() ?? "" : "";
        if (action is not ("advance" or "back" or "turnLeft" or "turnRight")) return;
        lock (gate)
        {
            var w = WalkOf(golem);
            w.Orders.Add(new Order
            {
                Ticket = o.GetProperty("order").GetInt32(), Route = o.GetProperty("route").GetInt32(), Action = action, Amount = o.GetProperty("amount").GetDouble(),
                Kind = o.TryGetProperty("kind", out var k) ? k.GetString() ?? "" : "", Leg = o.TryGetProperty("name", out var n) ? n.GetString() ?? "" : "",
                Toward = (o.GetProperty("x").GetDouble(), o.GetProperty("y").GetDouble()), Sequence = sequence,
                // the advance to the errand's last stop: reaching it completes the route, and nothing more is told to the body
                LastStop = action == "advance" && o.TryGetProperty("kind", out var kk) && kk.GetString() == "stop"
                           && o.TryGetProperty("stopsLeft", out var sl) && sl.GetInt32() == 1
                           && !(o.TryGetProperty("following", out var f) && f.GetBoolean()),
            });
            Settle(golem, w, sequence);
        }
    }

    /// <summary>The body's word back, as its result topic carries it: `{"order", "result": done | bumped | stuck, …}` — with where the
    /// world says it stands now (a bump says where the body stood itself) and what the world saw it touch since its last word.</summary>
    public void Resulted(string golem, string json, (double X, double Y, double Heading) stands, IReadOnlyList<string> touched, int sequence)
    {
        using var doc = JsonDocument.Parse(json);
        var e = doc.RootElement;
        if (!e.TryGetProperty("order", out var t) || t.ValueKind != JsonValueKind.Number || !e.TryGetProperty("result", out var r)) return;
        int ticket = t.GetInt32();
        string result = r.GetString() ?? "";
        double D(string n, double d) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : d;
        lock (gate)
        {
            var w = WalkOf(golem);
            var order = w.Orders.FirstOrDefault(x => x.Ticket == ticket && x.Answer == null);
            if (order == null && result != "bumped") return;   // a word about no order the world saw told: not this walk
            string said = result == "done" ? "arrived" : result;
            var stood = result == "bumped" ? (D("x", stands.X), D("y", stands.Y), D("heading", stands.Heading)) : stands;
            var report = order == null
                ? new BodyReport(golem, ticket, 0, said, "", 0.0, "", "", (stood.Item1, stood.Item2), stood, D("bearing", 0.0), touched, "", sequence)   // standing, touched
                : new BodyReport(golem, ticket, order.Route, said, order.Action, order.Amount, order.Leg, order.Kind, order.Toward, stood, D("bearing", 0.0),
                                 said == "arrived" ? Array.Empty<string>() : touched, e.TryGetProperty("reason", out var why) ? why.GetString() ?? "" : "", sequence);
            reports.Add(report);
            if (order != null)
            {
                order.Answer = report;
                if (said != "arrived")
                    foreach (var x in w.Orders.Where(x => x.Answer == null && x.Identity == order.Identity)) x.Answer = report;   // the rest of that leg is void
            }
            Settle(golem, w, sequence);
        }
    }

    // The legs told so far, from the orders answered: the next run of orders toward one leg, closed when the body finished it.
    private void Settle(string golem, Walk w, int sequence)
    {
        while (w.Closed < w.Orders.Count)
        {
            var first = w.Orders[w.Closed];
            int end = w.Closed;
            while (end < w.Orders.Count && w.Orders[end].Identity == first.Identity) end++;
            var group = w.Orders.GetRange(w.Closed, end - w.Closed);
            var cut = group.FirstOrDefault(x => x.Answer != null && x.Answer.Said != "arrived");
            if (cut != null)
            {
                string why = cut.Answer.Said == "bumped"
                    ? "bumped " + (cut.Answer.Touched.Count > 0 ? string.Join(", ", cut.Answer.Touched.Distinct()) : "something")
                    : "stuck: " + cut.Answer.Reason;
                legs.Add(new LegWalked(golem, first.Route, first.Leg, first.Kind, first.Toward, cut.Answer.Stands, false, why, cut.Answer.Touched, group.IndexOf(cut) + 1, sequence));
                w.Closed = end;
                continue;
            }
            bool answered = group.All(x => x.Answer != null);
            bool ended = end < w.Orders.Count || (group[^1].LastStop && group[^1].Answer?.Said == "arrived");
            if (!answered || !ended) return;
            legs.Add(new LegWalked(golem, first.Route, first.Leg, first.Kind, group[^1].Toward, group[^1].Answer.Stands, true, "", Array.Empty<string>(), group.Count, sequence));
            w.Closed = end;
        }
    }

    private Walk WalkOf(string golem)
    {
        if (!walks.TryGetValue(golem, out var w)) walks[golem] = w = new Walk();
        return w;
    }
}
