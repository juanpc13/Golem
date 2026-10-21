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
        // a formation is the warden's to tell to every golem of its fleet, one line on each tab (propuesta 112, 13-oct-2026; the words of the round
        // still cross between the members by tell); a play spreads by itself (propuesta 108) — never a line carried by --with
        if (command.Verb is "form" or "take" or "rotate" or "dissolve" or "finish" or "play" && command.With.Count > 0)
            return Reply.Syntax($"{command.Verb}: told to every golem involved on its own tab — never --with, which carries single orders (visit, reset, use, optimize, speed)");
        // place carries each golem to a mark of ITS OWN (ajuste 87): the names are on the line, never --with
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
            "form" => Form(command),
            "take" => Take(command),
            "rotate" => Rotate(command),
            "dissolve" => Choreographer(out var m, out var noMotors) ? Answered(m.Dissolve(command.Text), $"dissolved {command.Text} — every golem of its fleet is told") : noMotors,
            "finish" => Choreographer(out var fm, out var fnm) ? Answered(fm.Finish(command.Text), $"finish {command.Text} — counted when its part is done; the fleet is told") : fnm,
            "formations" => Formations(),
            "disputes" => Disputes(),
            "play" => command.Text == "" ? PlayRead() : command.Text == "over" ? (Dynamics(out var po, out var por) ? Answered(po.Over(), "the play is over — the cast is told") : por) : Play(command),
            "plays" => PlaysRead(),
            "start" => Dynamics(out var rl, out var rr) ? Answered(rl.Start(command.Text), $"{command.Text} started here") : rr,
            "then" => Then(command),
            "pause" => Motors(out var d, out var refusal) ? Answered(d.Pause()) : refusal,
            "resume" => Motors(out var d, out var refusal) ? Answered(d.Resume()) : refusal,
            "forget" => command.Text == "map" ? Answered(golem.DropScenario(command.Values["map"]), $"map {command.Values["map"]} forgotten: off the golem's scenarios") : command.Text == "disputes" ? ForgetDisputes() : command.Text == "play" ? (Dynamics(out var fp, out var fpr) ? Answered(fp.ForgetPlay(command.Values["play"]), $"play {command.Values["play"]} forgotten: off the list") : fpr) : Forget(command),
            "anchor" => Answered(golem.Anchor(), "anchored: awake where the body really stands"),
            "reset" => await ResetAsync(command),
            "place" => await PlaceAsync(command),
            "state" => State(),
            "route" => Route(),
            "where" => Where(),
            "obstacles" => Obstacles(),
            "use" => Answered(await golem.EnterAsync(command.Text), $"using map {command.Text}"),
            "optimize" => Motors(out var op, out var opr) ? Answered(op.Optimize(command.Text), $"navigating {command.Text.Replace('-', ' ')}") : opr,
            "speed" => Pace(command),
            "scenarios" => Scenarios(),
            "explored" => Explored(),
            "explore" => Explorer(out var ex, out var exr) ? (command.Text == "stop" ? Answered(ex.StopExploring(), "exploration stopped — a probe underway ends on its own") : command.Text == "clear" ? Answered(ex.ClearExploration(), "what was explored here is cleared: the trail starts afresh, the marks are forgotten") : Answered(ex.Explore(), "exploring blind: no map at all — every touch a wall learned, the floor ending where it touches — until nothing unknown is left next to what it knows")) : exr,
            "export" => Explorer(out var xm, out var xmr) ? Answered(xm.ExportMap(command.Text), $"map {command.Text} exported: among the golem's scenarios now — use map {command.Text} enters it") : xmr,
            "set" or "show" => Reply.Syntax($"{command.Verb} is the console's own: it keeps the value and writes @{(command.Text == "" ? "name" : command.Text)} on the line before it is sent — nothing of it reaches the golem"),
            "help" => Help(command),
            _ => Reply.Syntax($"'{command.Verb}' is no command"),
        };
        golem.Note($"> {line.Trim()} — {FirstLine(reply.Text)}");
        return reply;
    }

    // ---- the operator's verbs: the same validation as the endpoint, the same role ----

    // the fleet of a formation: told outright (--fleet), or this golem and every peer it can reach (ajuste 71)
    private IReadOnlyList<string> FleetOf(Command command, string key = "fleet")
    {
        if (command.Values.TryGetValue(key, out var told)) return told.Split(',').Select(n => n.Trim().ToLowerInvariant()).Where(n => n != "").Distinct().ToList();
        return new[] { golem.Name.ToLowerInvariant() }.Concat(golem.Peers.Select(p => p.ToLowerInvariant())).Distinct().ToList();
    }

    // a formation told (propuesta 104): the same validation as POST /form; the answer says the places the golem resolved and who holds each
    private Reply Form(Command command)
    {
        double? Metres(string key) => command.Values.TryGetValue(key, out var v) ? double.Parse(v, CultureInfo.InvariantCulture) : null;
        var request = new FormRequest(command.Text, command.Values["figure"], new PointRequest(command.Points[0].X, command.Points[0].Y),
                                      Metres("side"), Metres("radius"), Metres("inner-radius"), Metres("angle") ?? 0.0,
                                      FleetOf(command).ToList(),
                                      command.Values.TryGetValue("by", out var by) ? by : null,
                                      command.Values.TryGetValue("blades", out var blades) ? int.Parse(blades, CultureInfo.InvariantCulture) : null,
                                      command.Values.TryGetValue("cue", out var cue) ? cue : null,
                                      command.Values.ContainsKey("on-cue"),
                                      command.Values.TryGetValue("after", out var after) ? after : null,
                                      command.Values.TryGetValue("ask", out var ask) ? ask : null,
                                      Metres("every"));
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return Reply.Syntax(string.Join("; ", problems));
        if (!Choreographer(out var choreographer, out var refusal)) return refusal;
        var center = (request.Center.X.Value, request.Center.Y.Value);
        var answer = request.IsPropeller
            ? choreographer.FormPropeller(request.Name, center, request.Radius.Value, request.Hub.Value, request.BladeCount, request.Angle ?? 0.0, request.Fleet, request.Policy, request.CuedGolem, request.OnCue, request.StartsAfter, request.AskedGolem)
            : choreographer.Form(request.Name, request.Kind, center, request.Measure.Value, request.Angle ?? 0.0, request.Fleet, request.Policy, request.CuedGolem, request.OnCue, request.StartsAfter, request.AskedGolem);
        if (!answer.Ok) return Reply.Refused(answer.Refused);
        var holders = new List<string>();
        try
        {
            var printed = JsonDocument.Parse(answer.Print ?? "{}").RootElement;
            if (printed.TryGetProperty("holders", out var v) && v.ValueKind == JsonValueKind.Array)
                foreach (var h in v.EnumerateArray()) holders.Add($"{Str(h, "who")} {Num(h, "x")},{Num(h, "y")}");
        }
        catch (JsonException) { }
        return Reply.Done($"formed {request.Name} for {request.Fleet.Count} by {request.Policy} — every golem of the fleet is told" + (holders.Count > 0 ? $"; places: {string.Join(" · ", holders)}" : ""), answer.Print ?? "");
    }

    // the formation taken (propuesta 104): the same validation as POST /take; the route opens when the whole fleet said where it stands
    private Reply Take(Command command)
    {
        var request = new TakeRequest(command.Text);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return Reply.Syntax(string.Join("; ", problems));
        if (!Choreographer(out var choreographer, out var refusal)) return refusal;
        return Called(choreographer.Take(request.Name));
    }

    // a step of a formation (ajuste 77; propuesta 104): opened at once when everybody already stands — the order, as any — or queued, said so
    private Reply Rotate(Command command)
    {
        var request = new RotateRequest(command.Text, command.Values["sense"]);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return Reply.Syntax(string.Join("; ", problems));
        if (!Choreographer(out var choreographer, out var refusal)) return refusal;
        return Stepped(choreographer.Rotate(request.Name, request.Sense), $"{request.Name} {request.Sense}");
    }

    // the disputes by distance (14-oct-2026): one line each, won or yielded, against whom, both distances
    private Reply Disputes()
    {
        string json = Readings.Disputes(golem.Actor);
        var e = JsonDocument.Parse(json).RootElement;
        var lines = new List<string>();
        if (e.TryGetProperty("disputes", out var all) && all.ValueKind == JsonValueKind.Array)
            foreach (var d in all.EnumerateArray())
            {
                bool won = d.TryGetProperty("won", out var w) && w.ValueKind == JsonValueKind.True;
                string game = Str(d, "game");
                lines.Add($"{Str(d, "called")} · place {Int(d, "place")} at ({Num(d, "x")}, {Num(d, "y")}) · {(won ? "won it against" : "yielded it to")} {Str(d, "rival")}{(game == "" ? "" : " · " + game)} · {Num(d, "mine")} m from it, {Str(d, "rival")} {Num(d, "theirs")} m");
            }
        return Reply.Done(lines.Count == 0 ? "no disputes: nobody contended with this golem for a place, by distance or by the game" : string.Join(Environment.NewLine, lines), json);
    }

    private Reply Formations()
    {
        string json = Readings.Formations(golem.Actor);
        var e = JsonDocument.Parse(json).RootElement;
        var lines = new List<string>();
        if (e.TryGetProperty("told", out var told) && told.ValueKind == JsonValueKind.Array)
            foreach (var f in told.EnumerateArray())
            {
                var who = new List<string>();
                if (f.TryGetProperty("holders", out var hs) && hs.ValueKind == JsonValueKind.Array)
                    foreach (var h in hs.EnumerateArray()) who.Add($"{Str(h, "who")}@{Num(h, "x")},{Num(h, "y")}");
                lines.Add($"{Str(f, "called")} · {Str(f, "shape")} at {Num(f, "atX")},{Num(f, "atY")}, {(Str(f, "shape") == "square" || Str(f, "shape") == "pentagon" || Str(f, "shape") == "triangle" ? "side" : "radius")} {Num(f, "length")} m, {Num(f, "degrees")}° · by {PolicyWords.Word(Str(f, "policy"))} · {Str(f, "crew")}, {Int(f, "placed")} placed, {Int(f, "queued")} step(s) queued, round {Int(f, "round")}" + (who.Count > 0 ? $" · {string.Join(" · ", who)}" : " · nobody placed yet"));
            }
        return Reply.Done(lines.Count == 0 ? "in no formation" : string.Join(Environment.NewLine, lines), json);
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

    // the disputes forgotten (14-oct-2026): how many went
    private Reply ForgetDisputes()
    {
        if (!Choreographer(out var choreographer, out var refusal)) return refusal;
        var answer = choreographer.ForgetDisputes();
        if (!answer.Ok) return Reply.Refused(answer.Refused);
        int n = 0;
        try { n = JsonDocument.Parse(answer.Print ?? "{}").RootElement.TryGetProperty("forgotten", out var f) && f.ValueKind == JsonValueKind.Number ? f.GetInt32() : 0; }
        catch (JsonException) { }
        return Reply.Done($"{n} dispute(s) forgotten — the next ones come with the next rounds played", answer.Print ?? "");
    }

    private Reply Forget(Command command)
    {
        var request = new PointRequest(command.Points[0].X, command.Points[0].Y);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return Reply.Syntax(string.Join("; ", problems));
        var captor = golem.Bumper;
        if (captor == null) return Reply.Refused($"this body has no bumper: the role '{Capabilities.Bumper}' is not among its capabilities ({golem.Capabilities})");
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
        string scenario = (Str(e, "scenario") != "" ? $" · scenario {Str(e, "scenario")}" : "") + (Str(e, "navigation") != "" ? $" · navigating {Str(e, "navigation")}" : "")
                        + (e.TryGetProperty("speed", out _) ? $" · pacing {Num(e, "speed")} m/s, turning {Num(e, "turn")} rad/s" : "");
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
    // the body's pace told again (propuesta 111): `speed 3 [--turn 2.5]` — the cruise in m/s and, when given, the turn rate in rad/s
    private Reply Pace(Command command)
    {
        double cruise = double.Parse(command.Text, CultureInfo.InvariantCulture);
        double? turning = command.Values.TryGetValue("turn", out var t) ? double.Parse(t, CultureInfo.InvariantCulture) : null;
        if (!Motors(out var motors, out var noMotors)) return noMotors;
        var answer = motors.Pace(cruise, turning);
        if (!answer.Ok) return Reply.Refused(answer.Refused);
        var e = JsonDocument.Parse(answer.Print ?? "{}").RootElement;
        return Reply.Done($"pacing at {Num(e, "speed")} m/s, turning {Num(e, "turn")} rad/s — every order from here on", answer.Print ?? "");
    }

    // a play staged (propuesta 108): pacman by name, the ghosts by name — left out, every peer but pacman
    private Reply Play(Command command)
    {
        if (!Dynamics(out var player, out var refusal)) return refusal;
        string pacman = command.Values.TryGetValue("pacman", out var star) ? star.Trim().ToLowerInvariant() : golem.Name.ToLowerInvariant();
        var ghosts = command.Values.TryGetValue("ghosts", out var crew)
            ? crew.Split(',').Select(n => n.Trim().ToLowerInvariant()).Where(n => n != "").Distinct().ToList()
            : new[] { golem.Name.ToLowerInvariant() }.Concat(golem.Peers.Select(x => x.ToLowerInvariant())).Where(n => n != pacman).Distinct().ToList();
        string kind = command.Values.TryGetValue("kind", out var k) ? k : "pacman";
        string role = command.Values.TryGetValue("as", out var part) ? part : null;
        var answer = player.Play(command.Text, kind, pacman, ghosts, role);
        if (!answer.Ok) return Reply.Refused(answer.Refused);
        var e = JsonDocument.Parse(answer.Print ?? "{}").RootElement;
        return Reply.Done($"staged {Str(e, "play")} ({Str(e, "kind")}) — {golem.Name} plays {Str(e, "part").ToLowerInvariant()}; pacman {Str(e, "pacman")}, ghosts {Str(e, "ghosts")}", answer.Print ?? "");
    }

    // EVERY PLAY TOLD (16-oct-2026): the list, the ended ones among them
    private Reply PlaysRead()
    {
        string json = Readings.Plays(golem.Actor);
        var e = JsonDocument.Parse(json).RootElement;
        if (!e.TryGetProperty("told", out var told) || told.ValueKind != JsonValueKind.Array || told.GetArrayLength() == 0) return Reply.Done("told no play yet", json);
        var lines = new List<string>();
        foreach (var p in told.EnumerateArray())
        {
            string end = Str(p, "state") != "Over" ? "" : Bool(p, "caught") ? $" — {Str(p, "catcher")} caught {Str(p, "pacman")}" : $" — ended by the {Str(p, "catcher")}";
            lines.Add($"{Str(p, "called")} · {Str(p, "kind")} · {Str(p, "state").ToLowerInvariant()}{end} · pacman {Str(p, "pacman")}, ghosts {Str(p, "ghosts")}");
        }
        return Reply.Done(string.Join("\n", lines), json);
    }

    private Reply PlayRead()
    {
        string json = Readings.Play(golem.Actor);
        var e = JsonDocument.Parse(json).RootElement;
        if (!Bool(e, "playing")) return Reply.Done("in no play", json);
        string cast = $"pacman {Str(e, "pacman")}, ghosts {Str(e, "ghosts")}";
        if (Str(e, "state") == "Over")   // the end (fase 4): who caught pacman, or the warden
            return Reply.Done($"{Str(e, "play")} · {Str(e, "part").ToLowerInvariant()} · over — {(Bool(e, "caught") ? $"{Str(e, "catcher")} caught {Str(e, "pacman")}" : $"ended by the {Str(e, "catcher")}")} · {cast}", json);
        if (Str(e, "part") == "Ghost")   // a ghost's copy (fase 3): whether it left the den, where pacman was last seen and on which run, its chases
        {
            string seen = Bool(e, "seen") ? $"pacman seen at ({Num(e, "seenX"):0.##}, {Num(e, "seenY"):0.##}) on run {Num(e, "runsSeen")}" : "pacman not seen yet";
            return Reply.Done($"{Str(e, "play")} · ghost · {Str(e, "state").ToLowerInvariant()} · {(Bool(e, "released") ? "out of the den" : "in the den")} · {seen} · {Num(e, "chases")} chase(s) · {cast}", json);
        }
        return Reply.Done($"{Str(e, "play")} · pacman · {Str(e, "state").ToLowerInvariant()} · heading {Str(e, "heading").ToLowerInvariant()} · {Num(e, "runs")} run(s) · {cast}", json);
    }

    private static Reply Answered(Answer answer, string done) => answer.Ok ? Reply.Done(done, answer.Print ?? "") : Reply.Refused(answer.Refused);

    // a formation taken (ajuste 80; propuesta 104): the route opens when the whole fleet said where it stands — at once for a fleet of one, else said so
    private Reply Called(Answer answer) =>
        answer.Ok && answer.Order == null ? Reply.Done("taken — the routes open when everybody said where it stands", answer.Print ?? "") : Answered(answer);

    // a step asked (ajuste 77): opened at once when everybody already stands on its place — the order, as any — or queued, said so
    private Reply Stepped(Answer answer, string sense) =>
        answer.Ok && answer.Order == null ? Reply.Done($"step {sense} queued — it opens when everybody stands on its place", answer.Print ?? "") : Answered(answer);

    // What the golem explored (propuesta 120): the summary and the drawing of the cells it knows, rendered here for a console
    private Reply Explored()
    {
        string json = Readings.Explored(golem.Actor);
        var e = JsonDocument.Parse(json).RootElement;
        var lines = new List<string> { $"explored in {Str(e, "scenario")}: {Int(e, "strides")} stride(s), {Num(e, "metres")} m walked, {Int(e, "covered")} cell(s) free, {Int(e, "walls")} wall(s), {Int(e, "frontier")} at the frontier{(e.TryGetProperty("complete", out var c) && c.ValueKind == JsonValueKind.True ? " — complete" : "")}" };
        return Reply.Done(string.Join(Environment.NewLine, lines), json);
    }

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

    private bool Motors(out DisplacerRol displacer, out Reply refusal)
    {
        displacer = golem.Displacer;
        refusal = displacer == null ? Reply.Refused($"this body has no motors: the role '{Capabilities.Displacer}' is not among its capabilities ({golem.Capabilities})") : null;
        return displacer != null;
    }

    // the roles of the mind's modules (16-oct-2026): the formations and the plays
    private bool Choreographer(out ChoreographerRol choreographer, out Reply refusal)
    {
        choreographer = golem.Choreographer;
        refusal = choreographer == null ? Reply.Refused($"this body plays no choreographer: the role '{Capabilities.Choreographer}' is not among its capabilities ({golem.Capabilities})") : null;
        return choreographer != null;
    }

    private bool Explorer(out ExplorerRol explorer, out Reply refusal)
    {
        explorer = golem.Explorer;
        refusal = explorer == null ? Reply.Refused($"this body explores nothing: the role '{Capabilities.Explorer}' is not among its capabilities ({golem.Capabilities})") : null;
        return explorer != null;
    }

    private bool Dynamics(out DynamicsRol dynamics, out Reply refusal)
    {
        dynamics = golem.Dynamics;
        refusal = dynamics == null ? Reply.Refused($"this body plays no dynamics: the role '{Capabilities.Dynamics}' is not among its capabilities ({golem.Capabilities})") : null;
        return dynamics != null;
    }

    private static string FirstLine(string text) { int i = text.IndexOf('\n'); return i < 0 ? text : text[..i].TrimEnd('\r'); }
    private static string Fmt(double v, string format = "0.##") => v.ToString(format, CultureInfo.InvariantCulture);
    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetInt32() : 0;
    private static double Dbl(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0.0;
    private static string Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? Fmt(v.GetDouble()) : "?";
    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
}
