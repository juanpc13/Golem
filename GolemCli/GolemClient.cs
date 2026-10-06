using System.Net.Http;
using System.Net.Http.Json;
using System.Text.Json;

namespace GolemCli;

/// <summary>What a golem answered to a line: done, refused (the domain's words), syntax (the parser's), or unreachable.</summary>
public sealed record Reply(string Kind, string Text, JsonElement? Json)
{
    public bool Ok => Kind == "done";
}

/// <summary>The golem's HTTP surface, as the console uses it: a line to POST /command (JSON back for a program), the telemetry of
/// GET /body to see whether it is there, the language of GET /commands. The console decides nothing: it carries lines and reads answers.</summary>
public static class GolemClient
{
    private static readonly HttpClient Wire = new() { Timeout = TimeSpan.FromSeconds(90) };

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
        var reply = await SendAsync(golem, "where", ct);
        if (!reply.Ok || reply.Json == null) { golem.Status = reply.Kind == "unreachable" ? "unreachable" : reply.Text; return false; }
        golem.LastX = JsonWalk.Number(reply.Json.Value, "x");
        golem.LastY = JsonWalk.Number(reply.Json.Value, "y");
        golem.LastHeading = JsonWalk.Number(reply.Json.Value, "heading");
        string? scenario = JsonWalk.String(reply.Json.Value, "scenario");
        golem.Status = golem.LastX == null ? "awake, position unknown" : $"in {scenario ?? "?"}";
        return true;
    }

    /// <summary>How the golem's latest route ended — `route`: its status (completed, failed, abandoned, pending) and the why when it ended
    /// short; the ACK the console says after a command that opened a route.</summary>
    public static async Task<(string Status, string Why)> LastRouteAsync(Golem golem, CancellationToken ct = default)
    {
        var reply = await SendAsync(golem, "route", ct);
        if (!reply.Ok || reply.Json == null) return ("", "");
        return (JsonWalk.String(reply.Json.Value, "status") ?? "", JsonWalk.String(reply.Json.Value, "why") ?? "");
    }

    /// <summary>How many routes the golem still has pending — `state`, read for the queue: the next command goes when it is zero.</summary>
    public static async Task<int?> PendingAsync(Golem golem, CancellationToken ct = default)
    {
        var reply = await SendAsync(golem, "state", ct);
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
