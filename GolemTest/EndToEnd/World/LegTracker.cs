using System.Globalization;
using System.Text.Json;

namespace GolemTest.World;

/// <summary>A LEG of a route, walked (or not): its name and where it went, from where to where, whether the body COMPLETED it —
/// the route's cursor went past it with the way unchanged — or it was cut short (a bump, a way decided again, the route ended),
/// what the world saw the body touch on it, where the body really stood when it ended, and the way the route held after it.</summary>
public sealed record LegReport(
    string Golem, int Route, int Index, string Name, (double X, double Y) From, (double X, double Y) To,
    bool Completed, string EndedBy, IReadOnlyList<string> Touched, (double X, double Y) BodyAt, double Off,
    string WayAfter, string RouteStatus, int Sequence)
{
    /// <summary>The leg in one line, for a list: `✓ 2  aside   (5.51, 6.82) → (4.76, 6.80)   0.03 m off`.</summary>
    public string Line() =>
        $"{(Completed ? "✓" : "✗")} {Index,-2} {Name,-14} ({From.X:0.00}, {From.Y:0.00}) → ({To.X:0.00}, {To.Y:0.00})   "
        + (Completed ? $"{Off:0.00} m off" + (Touched.Count > 0 ? $", touched {string.Join(", ", Touched.Distinct())}" : "")
                     : $"cut at ({BodyAt.X:0.00}, {BodyAt.Y:0.00}): " + (Touched.Count > 0 ? "bumped " + string.Join(", ", Touched.Distinct()) : EndedBy));

    public override string ToString() =>
        $"leg {Index} · {Name} ({From.X:0.##}, {From.Y:0.##}) → ({To.X:0.##}, {To.Y:0.##}): "
        + (Completed ? "completed" : "NOT completed — " + EndedBy)
        + $"; body at ({BodyAt.X:0.00}, {BodyAt.Y:0.00}), {Off:0.00} m from the leg's point"
        + (Touched.Count > 0 ? "; touched " + string.Join(", ", Touched) : "");
}

/// <summary>What a golem's newest route says of itself at a moment: its handle, how many legs are still ahead, its whole way, its status.</summary>
public sealed record RouteState(int Route, int Ahead, string Plan, string Status)
{
    /// <summary>The question both worlds ask the golem's journal (a braced block: it runs verbatim at /query).</summary>
    public const string Query = "{ if (g.Routes().Count > 0) { route = g.Newest(); print route.Id 'route', route.LegsAhead.Count 'ahead', route.AsPlan() 'plan', route.Status 'status'; } }";

    public static RouteState Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;   // a golem with no route yet prints nothing
        using var doc = JsonDocument.Parse(json);
        var e = doc.RootElement;
        if (!e.TryGetProperty("route", out var route)) return null;
        return new RouteState(route.GetInt32(), e.GetProperty("ahead").GetInt32(), e.GetProperty("plan").GetString(), e.GetProperty("status").GetString());
    }

    /// <summary>The way's legs, parsed back from `name@x,y > name@x,y`.</summary>
    public IReadOnlyList<(string Name, double X, double Y)> Legs() => Plan.Split(" > ", StringSplitOptions.RemoveEmptyEntries).Select(l =>
    {
        int at = l.LastIndexOf('@');
        var xy = l[(at + 1)..].Split(',');
        return (l[..at], double.Parse(xy[0], CultureInfo.InvariantCulture), double.Parse(xy[1], CultureInfo.InvariantCulture));
    }).ToList();
}

// WATCHING THE LEGS (propuesta 52, 23-sep-2026; Juan: "el visit tiene varios legs de dos puntos… la idea es validar cada tramo y que
// haya sido exitoso"): each world asks the golem's journal, again and again while the body moves, how its newest route stands
// (RouteState), and hands it here with where the body really is. A leg is COMPLETED when the legs ahead drop by one and the way is
// the same; when the way changes before that, the leg in progress was cut short and the new way's first leg is watched next. The
// tracker decides nothing about the golem: it only reads what the golem's own route says and what the world saw.
internal sealed class LegTracker
{
    private sealed class Watch
    {
        public RouteState Last;
        public (double X, double Y) LegFrom;
        public int LegSince;   // the world's sequence when the leg in progress began: contacts after it belong to the leg
    }

    private readonly Dictionary<string, Watch> watches = new();
    private readonly List<LegReport> legs = new();
    private readonly Dictionary<string, int> read = new();
    private readonly object gate = new();

    public IReadOnlyList<LegReport> Of(string golem) { lock (gate) return legs.Where(l => l.Golem == golem).ToList(); }

    public void Forget(string golem) { lock (gate) { watches.Remove(golem); legs.RemoveAll(l => l.Golem == golem); read.Remove(golem); } }

    /// <summary>The next leg of that golem not handed out yet, or null when none has ended since.</summary>
    public LegReport Next(string golem)
    {
        lock (gate)
        {
            var mine = legs.Where(l => l.Golem == golem).ToList();
            int i = read.TryGetValue(golem, out var n) ? n : 0;
            if (i >= mine.Count) return null;
            read[golem] = i + 1;
            return mine[i];
        }
    }

    /// <summary>The route as it stands now, where the body really is, the contacts the world saw on it (with their sequence),
    /// and the world's sequence counter to number what this observation concludes.</summary>
    public void Observe(string golem, RouteState now, (double X, double Y) body, IReadOnlyList<WorldContact> contacts, Func<int> next)
    {
        if (now == null) return;
        lock (gate)
        {
            if (!watches.TryGetValue(golem, out var w) || w.Last == null || w.Last.Route != now.Route)
            {
                watches[golem] = new Watch { Last = now, LegFrom = body, LegSince = next() };   // a new route: its first leg starts here
                return;
            }
            var before = w.Last;
            if (now.Plan == before.Plan && now.Ahead == before.Ahead && now.Status == before.Status) return;
            var was = before.Legs();
            bool appended = now.Plan != before.Plan && now.Plan.StartsWith(before.Plan + " > ", StringComparison.Ordinal);   // a follower pulls over
            int reached = now.Plan == before.Plan || appended
                ? before.Ahead - (now.Ahead - (appended ? now.Legs().Count - was.Count : 0))
                : 0;
            for (int k = 0; k < reached; k++)
            {
                int index = was.Count - before.Ahead + k;
                var leg = was[index];
                End(golem, w, now, index, leg, completed: true, "", body, contacts, next);
            }
            if (now.Plan != before.Plan && !appended && before.Ahead > 0)                        // the way decided again mid-leg
                End(golem, w, now, was.Count - before.Ahead, was[was.Count - before.Ahead], completed: false, "the way was decided again", body, contacts, next);
            else if (now.Status is "failed" or "abandoned" && before.Status == "pending" && now.Ahead > 0 && reached == 0)
                End(golem, w, now, was.Count - now.Ahead, was[was.Count - now.Ahead], completed: false, "the route " + now.Status, body, contacts, next);
            w.Last = now;
        }
    }

    private void End(string golem, Watch w, RouteState now, int index, (string Name, double X, double Y) leg, bool completed, string endedBy,
                     (double X, double Y) body, IReadOnlyList<WorldContact> contacts, Func<int> next)
    {
        var touched = contacts.Where(c => c.Sequence > w.LegSince).Select(c => c.With).ToList();
        if (!completed && touched.Count > 0) endedBy = "a bump against " + string.Join(", ", touched.Distinct()) + "; " + endedBy;
        double off = Math.Sqrt((body.X - leg.X) * (body.X - leg.X) + (body.Y - leg.Y) * (body.Y - leg.Y));
        int seq = next();
        legs.Add(new LegReport(golem, now.Route, index + 1, leg.Name, w.LegFrom, (leg.X, leg.Y), completed, endedBy, touched, body, off, now.Plan, now.Status, seq));
        w.LegFrom = completed ? (leg.X, leg.Y) : body;
        w.LegSince = seq;
    }
}
