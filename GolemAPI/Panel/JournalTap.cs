using System.Collections.Concurrent;
using System.Text.Json;
using System.Text.RegularExpressions;

namespace GolemAPI.Panel;

// The panel's journal lane, fed by the journal itself: every record the framework
// writes — defines (templates), actions (their arguments), literal scripts (the release
// chain, tells, acks, verdicts) — decoded from the wire frame at commit time. At boot the
// whole journal is replayed into the lane, so the operator always sees the full diary.
//
// Rows: Script = the statement as the journal holds it (an action shows the template with
// its arguments substituted), Note = JSON {kind, actionId, x?, y?} for the panel's tags and map.
internal sealed class JournalTap
{
    private static readonly TimeSpan Grace = TimeSpan.FromMilliseconds(600);

    private readonly GolemPerformance perf;
    private readonly PanelFeed feed;
    private readonly ConcurrentDictionary<int, (string[] Params, string Body)> templates = new();
    private readonly List<(long EntryId, byte[] Wire)> pending = new();
    private readonly object gate = new();
    private long lastEmitted;
    private bool live;
    private Timer flushTimer;

    internal JournalTap(GolemPerformance perf, PanelFeed feed)
    {
        this.perf = perf;
        this.feed = feed;
    }

    // Hook first (so nothing written meanwhile is lost), then replay the history in
    // order, then let the live records through — in entry order after a short grace.
    internal void Start()
    {
        perf.WatchJournal((entryId, wire) =>
        {
            lock (gate)
            {
                pending.Add((entryId, wire));
                if (live) flushTimer?.Change(Grace, Timeout.InfiniteTimeSpan);
            }
        });

        foreach (var record in perf.ReadJournalAfter(0))
            Emit(record.EntryId, record.Record);

        lock (gate)
        {
            live = true;
            flushTimer = new Timer(_ => Flush(), null, Grace, Timeout.InfiniteTimeSpan);
        }
    }

    private void Flush()
    {
        (long EntryId, byte[] Wire)[] batch;
        lock (gate)
        {
            batch = pending.OrderBy(p => p.EntryId).ToArray();
            pending.Clear();
        }
        foreach (var (entryId, wire) in batch)
            Emit(entryId, wire);
    }

    private void Emit(long entryId, byte[] wire)
    {
        if (entryId <= lastEmitted && lastEmitted > 0) return; // the history already showed it
        lastEmitted = Math.Max(lastEmitted, entryId);

        if (!JournalPeek.TryDescribe(wire, out var peek))
        {
            feed.Broadcast(new PanelEvent(entryId, "command", "(record not readable)", Meta("record", 0), DateTime.UtcNow));
            return;
        }

        switch (peek.Kind)
        {
            case "define":
                templates[peek.ActionId] = ParseDefine(peek.Text);
                feed.Broadcast(new PanelEvent(entryId, "command", JournalPeek.OneLine(peek.Text),
                    Meta("define", peek.ActionId), DateTime.UtcNow));
                break;

            case "action":
                string call = templates.TryGetValue(peek.ActionId, out var t)
                    ? Substitute(t.Params, t.Body, peek.Text)
                    : $"action {peek.ActionId} ← {peek.Text}";
                var (x, y) = PointOf(call);
                feed.Broadcast(new PanelEvent(entryId, "command", JournalPeek.OneLine(call),
                    Meta("action", peek.ActionId, x, y), DateTime.UtcNow));
                break;

            default:
                feed.Broadcast(new PanelEvent(entryId, "command", JournalPeek.OneLine(peek.Text),
                    Meta(peek.Kind, 0), DateTime.UtcNow));
                break;
        }
    }

    private static string Meta(string kind, int actionId, double? x = null, double? y = null) =>
        JsonSerializer.Serialize(new { kind, actionId, x, y });

    // "define action 1 (id:int, x:double, y:double) as g.Assign(id, x, y); end;" -> (params, body)
    private static (string[] Params, string Body) ParseDefine(string sentence)
    {
        var m = Regex.Match(sentence, @"define\s+action\s+\d+\s*\((?<params>[^)]*)\)\s*as\s*(?<body>.*?)\s*end;?\s*$", RegexOptions.Singleline);
        if (!m.Success) return (Array.Empty<string>(), sentence);
        var names = m.Groups["params"].Value
            .Split(',', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries)
            .Select(p => p.Split(':')[0].Trim())
            .ToArray();
        return (names, m.Groups["body"].Value.Trim());
    }

    // The action's payload is its argument list as DSL literals ("1,8.5,2.5" / "'a reason'"):
    // put each one where the template names its parameter.
    private static string Substitute(string[] names, string body, string argsCsv)
    {
        var args = SplitArgs(argsCsv);
        if (args.Count != names.Length) return $"{body} ← {argsCsv}";
        string call = body;
        for (int i = 0; i < names.Length; i++)
            call = Regex.Replace(call, $@"\b{Regex.Escape(names[i])}\b", args[i].Replace("$", "$$"));
        return call;
    }

    private static List<string> SplitArgs(string csv)
    {
        var parts = new List<string>();
        var current = new System.Text.StringBuilder();
        bool quoted = false;
        foreach (char c in csv)
        {
            if (c == '\'') quoted = !quoted;
            if (c == ',' && !quoted) { parts.Add(current.ToString().Trim()); current.Clear(); }
            else current.Append(c);
        }
        if (current.Length > 0) parts.Add(current.ToString().Trim());
        return parts;
    }

    // A point the map can mark: Assign(id, x, y) or AssignTold(x, y).
    private static (double?, double?) PointOf(string call)
    {
        var assign = Regex.Match(call, @"g\.Assign\(\s*[^,]+,\s*(?<x>[-0-9.]+)\s*,\s*(?<y>[-0-9.]+)\s*\)");
        var told = Regex.Match(call, @"g\.AssignTold\(\s*(?<x>[-0-9.]+)\s*,\s*(?<y>[-0-9.]+)\s*\)");
        var m = assign.Success ? assign : told.Success ? told : null;
        if (m == null) return (null, null);
        return (double.Parse(m.Groups["x"].Value, System.Globalization.CultureInfo.InvariantCulture),
                double.Parse(m.Groups["y"].Value, System.Globalization.CultureInfo.InvariantCulture));
    }
}
