using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace WardenCli;

/// <summary>What a golem answered to a line: done, refused (the domain's words), syntax (the parser's), or unreachable.</summary>
public sealed record Reply(string Kind, string Text, JsonElement? Json)
{
    public bool Ok => Kind == "done";
}

/// <summary>An endpoint of the golem's program for one of the console's own lines (16-oct-2026): the method, the path and the JSON body.</summary>
public sealed record Call(HttpMethod Method, string Path, object? Body)
{
    public string Via => $"{Method.Method} /{Path}";
}

/// <summary>The golem's HTTP surface, as the console uses it: the tabs' lines to POST /command (the language, JSON back for a program), and
/// since 16-oct-2026 (Juan: "el CLI debería usar esos endpoints también") THE READS BY GET (`/where`, `/state`, `/route`, `/scenarios`,
/// `/obstacles`, `/formations`, `/disputes`, `/play`) and THE LEVERS ACTED AT ONCE BY THEIR POSTS (`/enter`, `/optimize`, `/speed`, `/place`,
/// `/anchor`, `/reset`, `/reset-everything`, `/head`, `/start`, `/over`, `/forget`, `/forget-disputes`, `/dissolve`) — the same documents
/// and the same acts the lines reach, <see cref="Endpoint"/> saying which; a golem that does not know an endpoint yet (404) is asked by the
/// line. The telemetry of GET /body says whether it is there, GET /commands its language. The console decides nothing: it carries lines and
/// reads answers.</summary>
public static class GolemClient
{
    private static readonly HttpClient Wire = new() { Timeout = TimeSpan.FromSeconds(90) };

    private static readonly HashSet<string> Reads = new(StringComparer.Ordinal) { "where", "state", "route", "scenarios", "obstacles", "formations", "disputes", "play", "plays" };

    /// <summary>THE ENDPOINT OF A LINE the console writes by itself — a read, or a lever of the environment, the map, the play or a tab —
    /// or null when the line is the language's (a tab's script goes by POST /command). Pure: tested as such.</summary>
    public static Call? Endpoint(string line)
    {
        var words = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries);
        if (words.Length == 0) return null;
        string verb = words[0].ToLowerInvariant();
        string rest = string.Join(' ', words.Skip(1));
        switch (verb)
        {
            case var read when words.Length == 1 && Reads.Contains(read): return new Call(HttpMethod.Get, read, null);
            case "use" when words.Length == 3 && words[1].Equals("map", StringComparison.OrdinalIgnoreCase): return new Call(HttpMethod.Post, "enter", new { scenario = words[2] });
            case "optimize" when words.Length == 2: return new Call(HttpMethod.Post, "optimize", new { navigation = words[1] });
            case "speed" when words.Length is 2 or 4 && TryNumber(words[1], out double speed):
                if (words.Length == 2) return new Call(HttpMethod.Post, "speed", new { speed });
                return words[2].Equals("--turn", StringComparison.OrdinalIgnoreCase) && TryNumber(words[3], out double turn) ? new Call(HttpMethod.Post, "speed", new { speed, turn }) : null;
            case "anchor" when words.Length == 1: return new Call(HttpMethod.Post, "anchor", null);
            case "reset" when words.Length == 1: return new Call(HttpMethod.Post, "reset", null);
            case "reset" when words.Length == 2 && words[1] == "--all": return new Call(HttpMethod.Post, "reset-everything", new { cascade = true });
            case "head" when words.Length == 2: return new Call(HttpMethod.Post, "head", new { direction = words[1] });
            case "steer" when words.Length == 2: return new Call(HttpMethod.Post, "steer", new { steer = words[1] });
            case "start" when words.Length == 2: return new Call(HttpMethod.Post, "start", new { name = words[1] });
            case "play" when words.Length == 2 && words[1].Equals("over", StringComparison.OrdinalIgnoreCase): return new Call(HttpMethod.Post, "over", null);
            case "forget" when words.Length == 2 && words[1].Equals("disputes", StringComparison.OrdinalIgnoreCase): return new Call(HttpMethod.Post, "forget-disputes", null);
            case "forget" when words.Length == 3 && words[1].Equals("play", StringComparison.OrdinalIgnoreCase): return new Call(HttpMethod.Post, "forget-play", new { name = words[2] });
            case "forget" when words.Length == 2 && TryPoint(words[1], out var at): return new Call(HttpMethod.Post, "forget", new { x = at.X, y = at.Y });
            case "place" when words.Length == 2 && TryPoint(words[1], out var to): return new Call(HttpMethod.Post, "place", new { x = to.X, y = to.Y });
            case "dissolve" when words.Length == 2: return new Call(HttpMethod.Post, "dissolve", new { name = rest });
            default: return null;
        }
    }

    private static bool TryNumber(string s, out double v) => double.TryParse(s, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out v) && double.IsFinite(v);

    private static bool TryPoint(string s, out (double X, double Y) point)
    {
        point = default;
        var parts = s.Split(',');
        if (parts.Length != 2 || !TryNumber(parts[0], out double x) || !TryNumber(parts[1], out double y)) return false;
        point = (x, y);
        return true;
    }

    /// <summary>A LINE ACTED AT ONCE by the console (a lever, a read, a tab's button): by its endpoint when it has one, else by POST /command.</summary>
    public static Task<Reply> ActAsync(Golem golem, string line, CancellationToken ct = default) =>
        Endpoint(line) is { } call ? CallAsync(golem, call, line, ct) : SendAsync(golem, line, ct);

    /// <summary>A READ of the golem — GET /{path} — described for the log like the line of the same name.</summary>
    public static Task<Reply> GetAsync(Golem golem, string path, CancellationToken ct = default) => CallAsync(golem, new Call(HttpMethod.Get, path, null), path, ct);

    // the endpoint called; a golem that does not know it yet (404: an older program) is asked by the line
    private static async Task<Reply> CallAsync(Golem golem, Call call, string line, CancellationToken ct)
    {
        try
        {
            using var request = new HttpRequestMessage(call.Method, new Uri(golem.Url, call.Path));
            if (call.Body != null) request.Content = JsonContent.Create(call.Body);
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await Wire.SendAsync(request, ct);
            string body = await response.Content.ReadAsStringAsync(ct);
            if ((int)response.StatusCode == 404) return await SendAsync(golem, line, ct);
            if (response.IsSuccessStatusCode)
            {
                JsonElement? json = null;
                if (body.Length > 0 && response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    try { json = JsonDocument.Parse(body).RootElement.Clone(); } catch (JsonException) { }
                }
                return new Reply("done", json == null ? (body == "" ? "accepted" : body) : Describe(line, json.Value, body), json);
            }
            return new Reply((int)response.StatusCode == 409 ? "refused" : "syntax", body, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new Reply("unreachable", $"nobody answers at {golem.Url}: {ex.Message}", null);
        }
    }

    /// <summary>A LINE OF THE LANGUAGE — POST /command, the tabs' way: what the golem answers, JSON back for the console's parsing.</summary>
    public static async Task<Reply> SendAsync(Golem golem, string line, CancellationToken ct = default)
    {
        try
        {
            using var request = new HttpRequestMessage(HttpMethod.Post, new Uri(golem.Url, "command"))
            {
                Content = JsonContent.Create(new { line }),
            };
            request.Headers.Accept.ParseAdd("application/json");
            using var response = await Wire.SendAsync(request, ct);
            string body = await response.Content.ReadAsStringAsync(ct);
            if (response.IsSuccessStatusCode)
            {
                JsonElement? json = null;
                if (response.Content.Headers.ContentType?.MediaType == "application/json")
                {
                    try { json = JsonDocument.Parse(body).RootElement.Clone(); } catch (JsonException) { }
                }
                return new Reply("done", json == null ? body : Describe(line, json.Value, body), json);
            }
            return new Reply((int)response.StatusCode == 409 ? "refused" : "syntax", body, null);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException)
        {
            return new Reply("unreachable", ex.Message, null);
        }
    }

    /// <summary>Whether the golem answers at its address: its name from GET /body, or null.</summary>
    public static async Task<string?> PingAsync(Golem golem, CancellationToken ct = default)
    {
        try
        {
            using var cts = CancellationTokenSource.CreateLinkedTokenSource(ct);
            cts.CancelAfter(TimeSpan.FromSeconds(4));
            using var response = await Wire.GetAsync(new Uri(golem.Url, "body"), cts.Token);
            if (!response.IsSuccessStatusCode) return null;
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(cts.Token)).RootElement;
            return json.TryGetProperty("golem", out var g) ? g.GetString() : null;
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return null; }
    }

    /// <summary>The language the golem speaks: GET /commands, as text lines (usage and what it does).</summary>
    public static async Task<string> CommandsAsync(Golem golem, CancellationToken ct = default)
    {
        try
        {
            using var response = await Wire.GetAsync(new Uri(golem.Url, "commands"), ct);
            var json = JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement;
            var lines = new List<string>();
            foreach (var h in json.EnumerateArray())
                lines.Add($"{JsonWalk.String(h, "Usage")}   {JsonWalk.String(h, "What")}");
            return string.Join(Environment.NewLine, lines);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return "the golem did not answer: " + ex.Message; }
    }

    /// <summary>Where the golem knows its body stands — the console asks `where` and keeps only this last answer.</summary>
    public static async Task<bool> RefreshWhereAsync(Golem golem, CancellationToken ct = default)
    {
        var reply = await GetAsync(golem, "where", ct);
        if (!reply.Ok || reply.Json == null) { golem.Status = reply.Kind == "unreachable" ? "unreachable" : reply.Text; return false; }
        golem.LastX = JsonWalk.Number(reply.Json.Value, "x");
        golem.LastY = JsonWalk.Number(reply.Json.Value, "y");
        golem.LastHeading = JsonWalk.Number(reply.Json.Value, "heading");
        string? scenario = JsonWalk.String(reply.Json.Value, "scenario");
        golem.Knowledge = golem.Knowledge with
        {
            Zone = JsonWalk.String(reply.Json.Value, "zone") ?? "",
            Scenario = scenario ?? "",
            Navigation = JsonWalk.String(reply.Json.Value, "navigation") ?? "",
            Speed = JsonWalk.Number(reply.Json.Value, "speed"),
            Turn = JsonWalk.Number(reply.Json.Value, "turn"),
            Held = JsonWalk.Find(reply.Json.Value, "held") is { ValueKind: JsonValueKind.True },
        };
        golem.Status = golem.LastX == null ? "awake, position unknown" : $"in {scenario ?? "?"}";
        if (scenario != null && (golem.Plan == null || golem.Plan.Name != scenario)) golem.Plan = await MapAsync(golem, ct);
        return true;
    }

    /// <summary>Only what the formations tab needs of ANOTHER golem (12-oct-2026: the other golems' formations listed too): its where and its
    /// formations, so a refresh with the box on stays light.</summary>
    public static async Task<bool> RefreshFormationsAsync(Golem golem, CancellationToken ct = default)
    {
        if (!await RefreshWhereAsync(golem, ct)) return false;
        var formations = await GetAsync(golem, "formations", ct);
        if (formations.Ok && formations.Json is { } fj)
            golem.Knowledge = golem.Knowledge with { Formations = FormationView.Parse(fj) };
        return true;
    }

    /// <summary>Everything the golem answers to its reads — where, state, route, obstacles — in one refresh (Juan, 7-oct-2026: the reads are
    /// information beside the map, not buttons on the script); false when the golem did not answer `where`.</summary>
    public static async Task<bool> RefreshReadingsAsync(Golem golem, CancellationToken ct = default)
    {
        if (!await RefreshWhereAsync(golem, ct)) return false;
        var k = golem.Knowledge;
        var state = await GetAsync(golem, "state", ct);
        if (state.Ok && state.Json is { } s)
            k = k with { Pending = (int?)JsonWalk.Number(s, "pending"), Routes = (int?)JsonWalk.Number(s, "total") };
        var route = await GetAsync(golem, "route", ct);
        if (route.Ok && route.Json is { } r)
            k = k with
            {
                RouteId = (int?)JsonWalk.Number(r, "route"),
                RouteStatus = JsonWalk.String(r, "status") ?? "",
                RouteAction = JsonWalk.String(r, "action") ?? "",
                RoutePlan = JsonWalk.String(r, "plan") ?? "",
                RouteWhy = JsonWalk.String(r, "why") ?? "",
            };
        var scenarios = await GetAsync(golem, "scenarios", ct);
        if (scenarios.Ok && scenarios.Json is { } sc)
            k = k with { Scenarios = Readings.ParseScenarios(sc) };
        var obstacles = await GetAsync(golem, "obstacles", ct);
        if (obstacles.Ok && obstacles.Json is { } o)
            k = k with
            {
                Things = (int)(JsonWalk.Number(o, "things") ?? 0),
                Met = (int)(JsonWalk.Number(o, "met") ?? 0),
                Marks = (int)(JsonWalk.Number(o, "marks") ?? 0),
                Obstacles = Obstacle.Parse(o),
            };
        // THE FORMATIONS THE GOLEM IS IN (propuesta 104): the console's formations tab is this read of the selected golem
        var formations = await GetAsync(golem, "formations", ct);
        if (formations.Ok && formations.Json is { } fj)
            k = k with { Formations = FormationView.Parse(fj) };
        // THE DISPUTES (14-oct-2026): the disputes tab is this read of the selected golem
        var disputes = await GetAsync(golem, "disputes", ct);
        if (disputes.Ok && disputes.Json is { } dj)
            k = k with { Disputes = DisputeView.Parse(dj) };
        // THE PLAY (propuesta 108): the play tab is this read of the selected golem
        var play = await GetAsync(golem, "play", ct);
        if (play.Ok && play.Json is { } pj)
            k = k with { Play = PlayView.Parse(pj) };
        // EVERY PLAY TOLD (16-oct-2026): the play tab's list
        var plays = await GetAsync(golem, "plays", ct);
        if (plays.Ok && plays.Json is { } pl)
            k = k with { Plays = PlayView.ParseList(pl) };
        golem.Knowledge = k;
        return true;
    }

    /// <summary>What the golem's current scenario disposes — GET /map: its zones, doors and open sides, for the console to draw.</summary>
    public static async Task<FloorPlan?> MapAsync(Golem golem, CancellationToken ct = default)
    {
        try
        {
            using var response = await Wire.GetAsync(new Uri(golem.Url, "map"), ct);
            if (!response.IsSuccessStatusCode) return null;
            return FloorPlan.Parse(JsonDocument.Parse(await response.Content.ReadAsStringAsync(ct)).RootElement);
        }
        catch (Exception ex) when (ex is HttpRequestException or TaskCanceledException or JsonException) { return null; }
    }

    /// <summary>How the golem's latest route ended — `route`: its status (completed, failed, abandoned, pending) and the why when it ended
    /// short; the ACK the console says after a command that opened a route.</summary>
    public static async Task<(string Status, string Why)> LastRouteAsync(Golem golem, CancellationToken ct = default)
    {
        var reply = await GetAsync(golem, "route", ct);
        if (!reply.Ok || reply.Json == null) return ("", "");
        return (JsonWalk.String(reply.Json.Value, "status") ?? "", JsonWalk.String(reply.Json.Value, "why") ?? "");
    }

    /// <summary>How many routes the golem was ever given — `state`'s total, read by the queue before a line that may open one, so it can tell a
    /// route that opened and ended at once (the body already there) from no route at all (a take waiting for the fleet, a step queued).</summary>
    public static async Task<int?> RouteTotalAsync(Golem golem, CancellationToken ct = default)
    {
        var reply = await GetAsync(golem, "state", ct);
        if (!reply.Ok || reply.Json == null) return null;
        double? total = JsonWalk.Number(reply.Json.Value, "total");
        return total == null ? null : (int)total.Value;
    }

    /// <summary>Where this golem's copy of a formation stands for the queue (13-oct-2026): whether its round opened its route yet, and whether it
    /// is stranded — `formations`, the formation of that name. Null when the golem does not answer or was told no such formation.</summary>
    public static async Task<FormationState?> FormationStateAsync(Golem golem, string name, CancellationToken ct = default)
    {
        var reply = await GetAsync(golem, "formations", ct);
        if (!reply.Ok || reply.Json == null || JsonWalk.Find(reply.Json.Value, "told") is not { ValueKind: JsonValueKind.Array } told) return null;
        foreach (var f in told.EnumerateArray())
            if (f.TryGetProperty("called", out var called) && string.Equals(called.GetString(), name, StringComparison.OrdinalIgnoreCase))
                return new FormationState(f.TryGetProperty("routed", out var r) && r.ValueKind == JsonValueKind.True,
                                          f.TryGetProperty("stranded", out var st) && st.ValueKind == JsonValueKind.True,
                                          (int)(JsonWalk.Number(f, "queued") ?? 0), (int)(JsonWalk.Number(f, "placed") ?? 0),
                                          (JsonWalk.String(f, "crew") ?? "").Split(',', StringSplitOptions.RemoveEmptyEntries).Length,
                                          (int)(JsonWalk.Number(f, "round") ?? 0),
                                          f.TryGetProperty("onCue", out var oc) && oc.ValueKind == JsonValueKind.True,
                                          !f.TryGetProperty("cued", out var cu) || cu.ValueKind != JsonValueKind.False,
                                          f.TryGetProperty("finishing", out var fi) && fi.ValueKind == JsonValueKind.True,
                                          (int)(JsonWalk.Number(f, "finished") ?? 0));
        return null;
    }

    /// <summary>How many routes the golem still has pending — `state`, read for the queue: the next command goes when it is zero.</summary>
    public static async Task<int?> PendingAsync(Golem golem, CancellationToken ct = default)
    {
        var reply = await GetAsync(golem, "state", ct);
        if (!reply.Ok || reply.Json == null) return null;
        double? pending = JsonWalk.Number(reply.Json.Value, "pending");
        return pending == null ? null : (int)pending.Value;
    }

    // A one-line account of a JSON answer, for the log: the console's own words over the golem's numbers.
    private static string Describe(string line, JsonElement json, string raw)
    {
        string verb = line.Trim().Split(' ', StringSplitOptions.RemoveEmptyEntries).FirstOrDefault() ?? "";
        switch (verb)
        {
            case "where":
                var x = JsonWalk.Number(json, "x"); var y = JsonWalk.Number(json, "y"); var h = JsonWalk.Number(json, "heading");
                return x == null ? "awake, position unknown" : $"standing at ({x:0.00}, {y:0.00}) facing {h:0.00} rad, in {JsonWalk.String(json, "zone")} · scenario {JsonWalk.String(json, "scenario")}";
            case "state":
                return $"pending {JsonWalk.Number(json, "pending")}, routes {JsonWalk.Number(json, "total")}";
            case "route":
                var id = JsonWalk.Number(json, "route");
                return id == null ? "no route yet" : $"route {id} {JsonWalk.String(json, "status")} · {JsonWalk.String(json, "plan")}";
            case "use":   // POST /enter answers the where: the scenario entered
                return $"using map {JsonWalk.String(json, "scenario")}";
            case "optimize":
                return $"navigating {JsonWalk.String(json, "navigation")}";
            case "speed":
                return $"pacing {JsonWalk.Number(json, "speed"):0.##} m/s, turning {JsonWalk.Number(json, "turn"):0.##} rad/s";
            case "place":
                return $"placed at ({JsonWalk.Number(json, "x"):0.##}, {JsonWalk.Number(json, "y"):0.##}), in {JsonWalk.String(json, "zone")} · scenario {JsonWalk.String(json, "scenario")}";
            case "play" when JsonWalk.String(json, "state") is { } st:
                return $"{JsonWalk.String(json, "play")} · {st.ToLowerInvariant()}{(JsonWalk.String(json, "catcher") is { Length: > 0 } c ? $" — ended by {c}" : "")}";
            default:
                var action = JsonWalk.String(json, "action"); var rid = JsonWalk.Number(json, "route");
                if (rid != null && action != null) return $"route {rid} · {action} {JsonWalk.Number(json, "amount"):0.###}";
                return raw.Length > 200 ? raw[..200] + "…" : raw;
        }
    }
}

/// <summary>The engine prints JSON as nested objects and arrays; the console looks for a label wherever it is, like the panels did.</summary>
public static class JsonWalk
{
    public static JsonElement? Find(JsonElement e, string name)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object:
                foreach (var p in e.EnumerateObject())
                {
                    if (p.Name == name) return p.Value;
                    var inner = Find(p.Value, name);
                    if (inner != null) return inner;
                }
                return null;
            case JsonValueKind.Array:
                foreach (var item in e.EnumerateArray())
                {
                    var inner = Find(item, name);
                    if (inner != null) return inner;
                }
                return null;
            default:
                return null;
        }
    }

    public static double? Number(JsonElement e, string name)
    {
        var v = Find(e, name);
        if (v == null) return null;
        return v.Value.ValueKind switch
        {
            JsonValueKind.Number => v.Value.GetDouble(),
            JsonValueKind.String => double.TryParse(v.Value.GetString(), System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out var d) ? d : null,
            _ => null,
        };
    }

    public static string? String(JsonElement e, string name)
    {
        var v = Find(e, name);
        return v == null ? null : v.Value.ValueKind == JsonValueKind.String ? v.Value.GetString() : v.Value.ToString();
    }
}

/// <summary>A formation as the queue waits on it (13/14-oct-2026): whether this golem's round opened its route, whether it is stranded, how many steps
/// wait in its queue, how many members stand on their places this round, and how many the fleet has.</summary>
/// <summary>What the queue waits on for a formation (propuesta 117): routed, stranded, steps queued, placed of the crew, the round, whether it
/// starts on cue and was cued, whether this golem's finish is queued and not counted yet, how many finished.</summary>
public sealed record FormationState(bool Routed, bool Stranded, int Queued, int Placed, int Crew, int Round = 0, bool OnCue = false, bool Cued = true, bool Finishing = false, int Finished = 0);
