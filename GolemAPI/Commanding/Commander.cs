using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using System.Text.RegularExpressions;
using GolemAPI.Choreography;
using GolemAPI.Choreography.Roles;
using GolemAPI.Controllers;

namespace GolemAPI.Commanding;

/// <summary>What a command answered: `done`, `refused` (the domain's words) or `syntax` (the line was no command); the text for a
/// console, and the JSON the endpoint would have given for a program.</summary>
public sealed record Reply(string Kind, string Text, string Json)
{
    public bool Ok => Kind == "done";
    public static Reply Done(string text, string json = "") => new("done", text, json);
    public static Reply Refused(string why) => new("refused", "refused: " + why, "");
    public static Reply Syntax(string what) => new("syntax", what, "");
}

// THE COMMANDER: a line read (CommandLine) becomes the SAME call the panel's buttons made — the language is the robot's, never the
// actor's DSL (no script travels on a line; the reads are commands) —  — the request validated as the endpoint
// validates it, the role's own method, the fixed reads (Readings) — and answers in words for a console and in the endpoint's JSON
// for a program. It decides nothing: the domain does, inside the role; the host validates and calls (Juan, 17-sep-2026). What was
// commanded and what it answered go to the feed, so the panel's runtime lane keeps the conversation.
public sealed class Commander
{
    private readonly GolemEmbodiment golem;

    public Commander(GolemEmbodiment golem)
    {
        this.golem = golem ?? throw new ArgumentNullException(nameof(golem));
    }

    /// <summary>The line executed, and answered. The line is this golem's own; `--with blue,green` (Juan, 28-sep-2026: "un parámetro
    /// tipo -- para especificar a los otros… sí, con with") carries the same command to those peers over the wire — `all`, to every
    /// peer it can reach — and the answers come back one per golem, this one first, named.</summary>
    public async Task<Reply> ExecuteAsync(string line)
    {
        Command command;
        try { command = CommandLine.Parse(line); }
        catch (CommandSyntaxException e) { return Reply.Syntax(e.Message); }
        // `golem blue …` on red's console: the line is another golem's — a console commands its own golem; the peers ride along with --with
        if (command.Golem != "" && !IsMe(command.Golem))
            return Reply.Refused($"this is {golem.Name}: '{command.Golem}' is commanded on its own console — write --with {command.Golem} to carry the command there too");
        // a choreography spreads by itself, by tell (ajuste 71): this golem is called, its peers are told — never a line carried to them
        if (command.Verb is "choreograph" or "rotate" && command.With.Count > 0)
            return Reply.Syntax($"{command.Verb}: a choreography spreads by itself — command one golem; --with carries single orders (visit, reset, enter, optimize)");
        // place carries each golem to a mark of ITS OWN (ajuste 87): the names are on the line, never --with
        // take: every golem has a vertex of its own (propuesta 99) — never the same line carried to the peers
        if (command.Verb == "take" && command.With.Count > 0)
            return Reply.Syntax("take gives this golem its own vertex — every golem has its own: write the line on each golem's console, not --with");
        if (command.Verb == "place" && command.With.Count > 0)
            return Reply.Syntax("place carries each golem to its own mark — write the names on the line, place blue@6,6.2 red@7,6.3, not --with");
        if (command.With.Count == 0) return await MineAsync(command, line);

        var peers = command.With.Contains("all") ? golem.Peers.Concat(command.With.Where(w => w != "all")).Distinct().ToList() : command.With.ToList();
        if (peers.Count == 0) return Reply.Refused($"this is {golem.Name}: --with all, but it has no peers on the wire");
        var replies = new List<(string Who, Reply Reply)> { (golem.Name, await MineAsync(command, line)) };
        foreach (var who in peers.Where(w => !IsMe(w)))
        {
            var peer = await golem.CommandPeerAsync(who, ForPeer(line, who));
            replies.Add((who, peer == null ? Reply.Refused($"this is {golem.Name}: no golem named '{who}' among its peers")
                            : peer.Status == 200 ? Reply.Done(peer.Text, "")
                            : peer.Status == 409 ? new Reply("refused", peer.Text, "")
                            : Reply.Syntax(peer.Text)));
        }
        var json = new JsonObject();
        foreach (var (who, reply) in replies)
        {
            if (reply.Json != "") { try { json[who] = JsonNode.Parse(reply.Json); continue; } catch (JsonException) { } }
            json[who] = reply.Text;
        }
        string kind = replies.All(r => r.Reply.Ok) ? "done" : replies.Any(r => r.Reply.Kind == "refused") ? "refused" : "syntax";
        return new Reply(kind, string.Join(Environment.NewLine, replies.Select(r => $"{r.Who} › {r.Reply.Text}")), json.ToJsonString());
    }

    private bool IsMe(string who) => string.Equals(who, golem.Name, StringComparison.OrdinalIgnoreCase);

    // the same command, addressed to one peer alone: `golem visit 2,9.5 --with blue,green` → `golem blue visit 2,9.5`
    private static string ForPeer(string line, string who)
    {
        string rest = Regex.Replace(line.Trim(), @"^golem\s+", "", RegexOptions.IgnoreCase);
        var first = Regex.Match(rest, @"^[A-Za-z_][A-Za-z0-9_-]*");                          // this golem's own name, when written — never a command
        if (first.Success && !CommandLine.Help.Any(h => h.Verb == first.Value.ToLowerInvariant())) rest = rest[first.Length..].TrimStart();
        rest = Regex.Replace(rest, @"\s*--with\s+\S+", "", RegexOptions.IgnoreCase).Trim();
        return $"golem {who} {rest}";
    }

    // this golem's own part of a line: the command reaching the same role the endpoint reaches
    private async Task<Reply> MineAsync(Command command, string line)
    {
        Reply reply = command.Verb switch
        {
            "visit" or "cover" => Errand(command),
            "choreograph" => Choreograph(command),
            "rotate" => Motors(out var m, out var noMotors) ? Stepped(m.Rotate(command.Text), command.Text) : noMotors,
            "form" => Form(command),
            "take" => Take(command),
            "formations" => Formations(),
            "then" => Then(command),
            "pause" => Motors(out var d, out var refusal) ? Answered(d.Pause()) : refusal,
            "resume" => Motors(out var d, out var refusal) ? Answered(d.Resume()) : refusal,
            "forget" => Forget(command),
            "reset" => await ResetAsync(command),
            "place" => await PlaceAsync(command),
            "state" => State(),
            "route" => Route(),
            "where" => Where(),
            "obstacles" => Obstacles(),
            "use" => Answered(golem.Enter(command.Text), $"using map {command.Text}"),
            "optimize" => Answered(golem.Optimize(command.Text), $"navigating {command.Text.Replace('-', ' ')}"),
            "scenarios" => Scenarios(),
            "set" or "show" => Reply.Syntax($"{command.Verb} is the console's own: it keeps the value and writes @{(command.Text == "" ? "name" : command.Text)} on the line before it is sent — nothing of it reaches the golem"),
            "help" => Help(command),
            _ => Reply.Syntax($"'{command.Verb}' is no command"),
        };
        golem.Note($"> {line.Trim()} — {FirstLine(reply.Text)}");
        return reply;
    }

    // ---- the operator's verbs: the same validation as the endpoint, the same role ----

    // the fleet of a formation: told outright (--fleet), or this golem and the peers named with --with (all: every peer on the wire)
    private IReadOnlyList<string> FleetOf(Command command, IReadOnlyList<string> peers)
    {
        if (command.Values.TryGetValue("fleet", out var told)) return told.Split(',').Select(n => n.Trim().ToLowerInvariant()).Where(n => n != "").Distinct().ToList();
        return new[] { golem.Name.ToLowerInvariant() }.Concat(peers.Select(p => p.ToLowerInvariant())).Distinct().ToList();
    }

    // the fleet takes a square (propuesta 59; ajuste 65): this golem's part — its corner, by the policy asked (rank, the only one built)
    private Reply Choreograph(Command command)
    {
        var peers = golem.Peers.ToList();   // the fleet, unless told outright: this golem and every peer it can reach (ajuste 71)
        var request = new FormationRequest(command.Text, new PointRequest(command.Points[0].X, command.Points[0].Y),
                                           double.Parse(command.Values["side"], CultureInfo.InvariantCulture), FleetOf(command, peers).ToList(),
                                           command.Values.TryGetValue("by", out var by) ? by : null);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return Reply.Syntax(string.Join("; ", problems));
        if (!Motors(out var displacer, out var refusal)) return refusal;
        var center = (request.Center.X.Value, request.Center.Y.Value);
        return Called(displacer.Call(request.Figure.Trim().ToLowerInvariant(), center, request.Side.Value, request.Fleet, request.Policy));
    }

    // a formation told (propuesta 99): the same validation as POST /form; the answer says the vertices the golem resolved
    private Reply Form(Command command)
    {
        var request = new FormRequest(command.Text, command.Values["figure"], new PointRequest(command.Points[0].X, command.Points[0].Y),
                                      double.Parse(command.Values["side"], CultureInfo.InvariantCulture),
                                      command.Values.TryGetValue("angle", out var a) ? double.Parse(a, CultureInfo.InvariantCulture) : 0.0);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return Reply.Syntax(string.Join("; ", problems));
        if (!Motors(out var displacer, out var refusal)) return refusal;
        var answer = displacer.Form(request.Name.Trim().ToLowerInvariant(), request.Figure, (request.Center.X.Value, request.Center.Y.Value), request.Side.Value, request.Angle ?? 0.0);
        if (!answer.Ok) return Reply.Refused(answer.Refused);
        var vertices = new List<string>();
        try
        {
            var printed = JsonDocument.Parse(answer.Print ?? "{}").RootElement;
            if (printed.TryGetProperty("vertices", out var v) && v.ValueKind == JsonValueKind.Array)
                for (int i = 0; i < v.GetArrayLength(); i++)
                    vertices.Add($"{i}: {Num(v[i], "x")},{Num(v[i], "y")}");
        }
        catch (JsonException) { }
        return Reply.Done($"formed {request.Name.Trim().ToLowerInvariant()} — vertices {string.Join("  ", vertices)}", answer.Print ?? "");
    }

    // a vertex taken (propuesta 99): the same validation as POST /take; the route's first order, as any errand's
    private Reply Take(Command command)
    {
        var request = new TakeRequest(command.Text, int.Parse(command.Values["vertex"], CultureInfo.InvariantCulture));
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return Reply.Syntax(string.Join("; ", problems));
        if (!Motors(out var displacer, out var refusal)) return refusal;
        return Answered(displacer.Take(request.Name.Trim().ToLowerInvariant(), request.Vertex.Value));
    }

    private Reply Formations()
    {
        string json = Readings.Formations(golem.Actor);
        var e = JsonDocument.Parse(json).RootElement;
        var lines = new List<string>();
        if (e.TryGetProperty("told", out var told) && told.ValueKind == JsonValueKind.Array)
            foreach (var f in told.EnumerateArray())
                lines.Add($"{Str(f, "called")} · {Str(f, "shape")} at {Num(f, "atX")},{Num(f, "atY")}, side {Num(f, "length")} m, {Num(f, "degrees")}°");
        return Reply.Done(lines.Count == 0 ? "no formation told yet" : string.Join(Environment.NewLine, lines), json);
    }

    // the two errands: visit in this order, cover in the order the golem finds shortest — both by the strategy the golem adopted (ajuste 61)
    private Reply Errand(Command command)
    {
        var request = new ErrandRequest(command.Points.Select(p => new StopRequest(p.X, p.Y)).ToList());
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return Reply.Syntax(string.Join("; ", problems));
        if (!Motors(out var displacer, out var refusal)) return refusal;
        var points = request.Stops.Select(s => (s.X.Value, s.Y.Value)).ToList();
        return Answered(command.Verb == "cover" ? displacer.Cover(points) : displacer.Move(points));
    }

    private Reply Then(Command command)
    {
        var request = new PointRequest(command.Points[0].X, command.Points[0].Y);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return Reply.Syntax(string.Join("; ", problems));
        if (!Motors(out var displacer, out var refusal)) return refusal;
        return Answered(displacer.Then((request.X.Value, request.Y.Value)));
    }

    private Reply Forget(Command command)
    {
        var request = new PointRequest(command.Points[0].X, command.Points[0].Y);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return Reply.Syntax(string.Join("; ", problems));
        var captor = golem.Captor;
        if (captor == null) return Reply.Refused($"this body has no bumper: the role '{Capabilities.CollisionCaptor}' is not among its capabilities ({golem.Capabilities})");
        return Answered(captor.Forget(request.X.Value, request.Y.Value));
    }

    // THE FLEET CARRIED ONTO MARKS (ajuste 87, a lab lever): this golem's own mark by this golem; every other name's line carried to that
    // peer, one value each — the one line that carries a different value to every peer, so it has no --with
    private async Task<Reply> PlaceAsync(Command command)
    {
        var marks = command.Values.Select(v => (Who: v.Key == "" ? golem.Name : v.Key, At: v.Value)).ToList();
        if (marks.Count(m => IsMe(m.Who)) > 1) return Reply.Syntax($"place: this golem's mark was given twice, bare and as {golem.Name}@…");
        var replies = new List<(string Who, Reply Reply)>();
        foreach (var (who, at) in marks.OrderBy(m => IsMe(m.Who) ? 0 : 1))
        {
            var point = CommandLine.Parse($"visit {at}").Points[0];
            if (IsMe(who))
            {
                await golem.PlaceAsync(point.X, point.Y);
                replies.Add((golem.Name, Reply.Done($"placed at ({point.X:0.##}, {point.Y:0.##}): every pending route let go, the body carried there", Readings.Where(golem.Actor))));
                continue;
            }
            var peer = await golem.CommandPeerAsync(who, $"golem {who} place {at}");
            replies.Add((who, peer == null ? Reply.Refused($"this is {golem.Name}: no golem named '{who}' among its peers")
                            : peer.Status == 200 ? Reply.Done(peer.Text, "")
                            : peer.Status == 409 ? new Reply("refused", peer.Text, "")
                            : Reply.Syntax(peer.Text)));
        }
        if (replies.Count == 1 && IsMe(replies[0].Who)) return replies[0].Reply;
        var json = new JsonObject();
        foreach (var (who, reply) in replies)
        {
            if (reply.Json != "") { try { json[who] = JsonNode.Parse(reply.Json); continue; } catch (JsonException) { } }
            json[who] = reply.Text;
        }
        string kind = replies.All(r => r.Reply.Ok) ? "done" : replies.Any(r => r.Reply.Kind == "refused") ? "refused" : "syntax";
        return new Reply(kind, string.Join(Environment.NewLine, replies.Select(r => $"{r.Who} › {r.Reply.Text}")), json.ToJsonString());
    }

    private async Task<Reply> ResetAsync(Command command)
    {
        if (command.Options.Contains("--all"))
        {
            await golem.ResetEverythingAsync(cascade: true);
            return Reply.Done("reset everything: the journal wiped, the peers' too; reborn on the mark");
        }
        await golem.LetGoAsync();
        return Reply.Done("let go: every pending route abandoned, the body back on its mark", Readings.Board(golem.Actor));
    }

    // ---- the reads, in words ----

    private Reply State()
    {
        string json = Readings.Board(golem.Actor);
        var e = JsonDocument.Parse(json).RootElement;
        var parts = new List<string> { $"{Int(e, "pending")} of {Int(e, "total")} route(s) pending" };
        if (Bool(e, "hasNext"))
            parts.Add($"underway: route {Int(e, "nextId")}, next point ({Num(e, "nextX")}, {Num(e, "nextY")}), {Int(e, "stopsLeft")} stop(s) left" + (Bool(e, "paused") ? ", paused" : ""));
        if (e.TryGetProperty("lastId", out _))
            parts.Add($"last: route {Int(e, "lastId")} {Str(e, "lastStatus")}" + (Str(e, "lastWhy") != "" ? " — " + Str(e, "lastWhy") : ""));
        return Reply.Done(string.Join(" · ", parts), json);
    }

    private Reply Route()
    {
        string json = Readings.Route(golem.Actor);
        var e = JsonDocument.Parse(json).RootElement;
        if (!e.TryGetProperty("route", out _)) return Reply.Done("no route yet", json);
        var lines = new List<string>
        {
            $"route {Int(e, "route")} {Str(e, "status")}" + (Str(e, "why") != "" ? " — " + Str(e, "why") : "")
            + (Bool(e, "paused") ? " (paused)" : ""),
            $"  way: {Str(e, "plan")}",
            $"  {Int(e, "ahead")} leg(s) ahead, {Int(e, "stopsLeft")} stop(s) left",
        };
        if (e.TryGetProperty("name", out _))
            lines.Add($"  asks now: {Str(e, "action")} {Amount(Str(e, "action"), Dbl(e, "amount"))}, toward {Str(e, "name")} ({Num(e, "x")}, {Num(e, "y")})");
        else if (Str(e, "status") == "pending")
            lines.Add($"  asks now: {Str(e, "action")}");
        return Reply.Done(string.Join(Environment.NewLine, lines), json);
    }

    private Reply Where()
    {
        string json = Readings.Where(golem.Actor);
        var e = JsonDocument.Parse(json).RootElement;
        string scenario = (Str(e, "scenario") != "" ? $" · scenario {Str(e, "scenario")}" : "") + (Str(e, "navigation") != "" ? $" · navigating {Str(e, "navigation")}" : "");
        if (!Bool(e, "knows")) return Reply.Done("the golem does not know where its body stands yet: no act brought its pose" + scenario, json);
        string zone = Str(e, "zone");
        return Reply.Done($"standing at ({Num(e, "x")}, {Num(e, "y")}) facing {Num(e, "heading")} rad" + (zone != "" ? $", in {zone}" : ", nowhere on the map") + (Bool(e, "held") ? " — held" : "") + scenario, json);
    }

    // the scenarios the golem knows, the one it is in marked: the query's loop renders as an array named after its variable, `known`
    private Reply Scenarios()
    {
        string json = Readings.Scenarios(golem.Actor);
        var e = JsonDocument.Parse(json).RootElement;
        string current = Str(e, "current");
        var lines = new List<string>();
        if (e.TryGetProperty("known", out var known) && known.ValueKind == JsonValueKind.Array)
            foreach (var k in known.EnumerateArray())
                lines.Add($"{(Str(k, "name") == current ? "› " : "  ")}{Str(k, "name")} · {Int(k, "zones")} zone(s), {Int(k, "marks")} mark(s)");
        return Reply.Done($"in {current}" + (lines.Count > 0 ? Environment.NewLine + string.Join(Environment.NewLine, lines) : ""), json);
    }

    // an act of the mind (no route to print): done in a few words, or refused in the domain's
    private static Reply Answered(Answer answer, string done) => answer.Ok ? Reply.Done(done, answer.Print ?? "") : Reply.Refused(answer.Refused);

    // a call made (ajuste 80): the route opens when the whole fleet said where it stands — at once for a fleet of one, else said so
    private Reply Called(Answer answer) =>
        answer.Ok && answer.Order == null ? Reply.Done("called — the routes open when everybody said where it stands", answer.Print ?? "") : Answered(answer);

    // a step asked (ajuste 77): opened at once when everybody already stands on its place — the order, as any — or queued, said so
    private Reply Stepped(Answer answer, string sense) =>
        answer.Ok && answer.Order == null ? Reply.Done($"step {sense} queued — it opens when everybody stands on its place", answer.Print ?? "") : Answered(answer);

    private Reply Obstacles()
    {
        string json = Readings.Obstacles(golem.Actor);
        var e = JsonDocument.Parse(json).RootElement;
        var lines = new List<string> { $"{Int(e, "total")} obstacle(s): {Int(e, "things")} thing(s), {Int(e, "met")} peer(s) met, {Int(e, "marks")} mark(s)" };
        if (e.TryGetProperty("obstacles", out var all))
            foreach (var o in all.EnumerateArray())
            {
                string zone = Str(o, "zone");
                lines.Add($"  {Str(o, "kind")}{(Str(o, "who") != "" ? " " + Str(o, "who") : "")} · {Str(o, "shape")} of {Int(o, "size")} at ({Num(o, "cx")}, {Num(o, "cy")}){(zone != "" ? ", in " + zone : "")}");
            }
        return Reply.Done(string.Join(Environment.NewLine, lines), json);
    }

    private static Reply Help(Command command)
    {
        var verbs = command.Text == "" ? CommandLine.Help : CommandLine.Help.Where(h => h.Verb == command.Text).ToList();
        int width = verbs.Max(h => h.Usage.Length);
        var lines = verbs.Select(h => $"{h.Usage.PadRight(width)}   {h.What}   e.g. {h.Example}");
        return Reply.Done(string.Join(Environment.NewLine, lines), JsonSerializer.Serialize(verbs));
    }

    // ---- what an act answered: refused in the domain's words, or done — the board plus the print, as the endpoint answers ----

    private Reply Answered(Answer answer)
    {
        if (!answer.Ok) return Reply.Refused(answer.Refused);
        var board = JsonNode.Parse(Readings.Board(golem.Actor))?.AsObject() ?? new JsonObject();
        if (!string.IsNullOrWhiteSpace(answer.Print))
            try { board["print"] = JsonNode.Parse(answer.Print); }
            catch (JsonException) { board["print"] = answer.Print; }
        return Reply.Done(Words(answer), board.ToJsonString());
    }

    // The print in the robot's words: `route 3 · turnRight 0.838 rad, toward living/south (3.4, 1.5)`; how the route ended, and why.
    private static string Words(Answer answer)
    {
        var o = answer.Order;
        if (o == null) return string.IsNullOrWhiteSpace(answer.Print) ? "done" : "done: nothing pending — the body stands";
        string said = $"route {o.Route} · {(o.IsEnded ? o.Ended : o.Action)}";   // an order in the robot's words, or how the route ended (ajuste 66)
        if (o.IsMove || o.IsTurn) said += " " + Amount(o.Action, o.Amount);
        if (o.Name != "") said += $", toward {o.Name} ({Fmt(o.X)}, {Fmt(o.Y)})";
        if (o.Why != "") said += " — " + o.Why;
        return said;
    }

    private static string Amount(string action, double amount) =>
        action is "advance" or "back" ? Fmt(amount, "0.000") + " m" : action is "turnLeft" or "turnRight" ? Fmt(amount, "0.000") + " rad" : Fmt(amount);

    private bool Motors(out Displacer displacer, out Reply refusal)
    {
        displacer = golem.Displacer;
        refusal = displacer == null ? Reply.Refused($"this body has no motors: the role '{Capabilities.Displacer}' is not among its capabilities ({golem.Capabilities})") : null;
        return displacer != null;
    }

    private static string FirstLine(string text) { int i = text.IndexOf('\n'); return i < 0 ? text : text[..i].TrimEnd('\r'); }
    private static string Fmt(double v, string format = "0.##") => v.ToString(format, CultureInfo.InvariantCulture);
    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
    private static double Dbl(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0.0;
    private static string Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? Fmt(v.GetDouble()) : "?";
    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
