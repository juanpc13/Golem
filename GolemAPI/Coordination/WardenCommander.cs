using System.Globalization;
using System.Text.Json;
using System.Text.Json.Nodes;
using GolemAPI.Choreography;
using GolemAPI.Commanding;
using GolemAPI.Controllers;

namespace GolemAPI.Coordination;

// THE WARDEN'S CONSOLE — the main CLI (propuesta 88; Juan: "ese otro actor será básicamente el CLI principal para controlarlos… pensado
// para más cosas, no sólo para las coreografías"). Two kinds of lines: the warden's OWN — `choreograph`, `rotate`, `fleet`, `place`,
// `help` — and the lines CARRIED to the golems, the golem named in front: `golem blue visit 2,9.5`, `golem all enter open-floor`,
// `golem red,yellow reset`. A carried line is plumbing (decision 5): the warden journals nothing of it; what it knows of the golems
// is what they tell it. `--with` is the golems' consoles' word, not this one's: here the addressee goes in front.
public sealed class WardenCommander
{
    private readonly WardenMind warden;

    public WardenCommander(WardenMind warden)
    {
        this.warden = warden ?? throw new ArgumentNullException(nameof(warden));
    }

    public async Task<Reply> ExecuteAsync(string line)
    {
        Command command;
        try { command = CommandLine.Parse(line); }
        catch (CommandSyntaxException e) { return Reply.Syntax(e.Message); }
        if (command.With.Count > 0)
            return Reply.Syntax("on the warden's console the golems go in front: golem blue visit 2,9.5 · golem red,yellow reset · golem all enter open-floor — not --with");
        if (command.Golem != "") return await CarryAsync(command, line);

        Reply reply = command.Verb switch
        {
            "choreograph" => Choreograph(command),
            "rotate" => Stepped(warden.Rotate(command.Text), command.Text),
            "fleet" => Fleet(),
            "place" => await PlaceAsync(command),
            "help" => Help(command),
            "visit" or "cover" or "then" or "pause" or "resume" or "forget" or "reset" or "state" or "route" or "where" or "obstacles" or "enter" or "optimize" or "scenarios"
                => Reply.Syntax($"{command.Verb} is a golem's: name it in front — golem blue {command.Verb} …, or golem all {command.Verb} …"),
            "set" or "show" => Reply.Syntax($"{command.Verb} is the console's own: it keeps the value and writes @{(command.Text == "" ? "name" : command.Text)} on the line before it is sent"),
            _ => Reply.Syntax($"'{command.Verb}' is no command"),
        };
        warden.Note($"> {line.Trim()} — {FirstLine(reply.Text)}");
        return reply;
    }

    // a line carried to the golems named in front (`all`: everybody on the wire), each getting its own line; one answer per golem
    private async Task<Reply> CarryAsync(Command command, string line)
    {
        if (command.Verb is "choreograph" or "rotate") return Reply.Syntax($"{command.Verb} is the warden's own: write it without a golem in front");
        var names = command.Golem == "all" ? warden.Peers.ToList() : command.Golem.Split(',').Select(n => n.Trim().ToLowerInvariant()).Where(n => n != "").Distinct().ToList();
        if (names.Count == 0) return Reply.Refused($"this is the warden: no golem on the wire to carry '{command.Verb}' to");
        var replies = new List<(string Who, Reply Reply)>();
        foreach (var who in names)
        {
            var peer = await warden.CommandPeerAsync(who, CommandLine.ForGolem(line, who));
            replies.Add((who, peer == null ? Reply.Refused($"this is the warden: no golem named '{who}' on the wire")
                            : peer.Status == 200 ? Reply.Done(peer.Text, "")
                            : peer.Status == 409 ? new Reply("refused", peer.Text, "")
                            : Reply.Syntax(peer.Text)));
        }
        warden.Note($"> {line.Trim()} — carried to {string.Join(", ", names)}");
        return Gathered(replies);
    }

    private static Reply Gathered(List<(string Who, Reply Reply)> replies)
    {
        var json = new JsonObject();
        foreach (var (who, reply) in replies)
        {
            if (reply.Json != "") { try { json[who] = JsonNode.Parse(reply.Json); continue; } catch (JsonException) { } }
            json[who] = reply.Text;
        }
        string kind = replies.All(r => r.Reply.Ok) ? "done" : replies.Any(r => r.Reply.Kind == "refused") ? "refused" : "syntax";
        return new Reply(kind, string.Join(Environment.NewLine, replies.Select(r => $"{r.Who} › {r.Reply.Text}")), json.ToJsonString());
    }

    // the fleet takes a formation: the figure, the centre, the side, the policy, the fleet told outright or everybody the warden heard from
    private Reply Choreograph(Command command)
    {
        var request = new FormationRequest(command.Text, new PointRequest(command.Points[0].X, command.Points[0].Y),
                                           double.Parse(command.Values["side"], CultureInfo.InvariantCulture),
                                           command.Values.TryGetValue("fleet", out var told) ? told.Split(',').Select(n => n.Trim().ToLowerInvariant()).Where(n => n != "").Distinct().ToList() : new List<string>(),
                                           command.Values.TryGetValue("by", out var by) ? by : null);
        var problems = request.Problems().ToList();
        if (problems.Count > 0) return Reply.Syntax(string.Join("; ", problems));
        var answer = warden.Call(request.Figure.Trim().ToLowerInvariant(), (request.Center.X.Value, request.Center.Y.Value), request.Side.Value, request.Fleet, request.Policy);
        return answer.Ok ? Reply.Done("called — the places are shared when everybody said where it stands", answer.Print ?? "") : Reply.Refused(answer.Refused);
    }

    private static Reply Stepped(Answer answer, string sense) =>
        answer.Ok ? Reply.Done($"step {sense} queued — it opens when everybody stands on its place", answer.Print ?? "") : Reply.Refused(answer.Refused);

    // THE BOARD: what the warden knows of the golems, and the formation in place
    private Reply Fleet()
    {
        string json = warden.Board();
        var lines = new List<string>();
        var root = JsonDocument.Parse(json).RootElement;
        var golems = new List<string>();
        string call = "", figure = "", by = "", table = ""; int round = 0, stood = 0, of = 0, placed = 0, queued = 0; bool shared = false, formation = false;
        Walk(root, e =>
        {
            if (e.TryGetProperty("name", out var n)) golems.Add($"  {n.GetString()} · stands at ({Num(e, "x")}, {Num(e, "y")}) in {Str(e, "scenario")} · {Int(e, "words")} word(s)");
            if (e.TryGetProperty("call", out var c)) { formation = true; call = c.GetString() ?? ""; figure = Str(e, "figure"); by = Str(e, "by"); round = Int(e, "round"); stood = Int(e, "stood"); of = Int(e, "of"); placed = Int(e, "placed"); queued = Int(e, "queued"); shared = e.TryGetProperty("shared", out var s) && s.ValueKind == JsonValueKind.True; }
            if (e.TryGetProperty("table", out var t)) table = t.GetString() ?? "";
        });
        lines.Add(golems.Count == 0 ? "no golem spoke yet" : $"{golems.Count} golem(s) heard from:");
        lines.AddRange(golems);
        if (formation)
        {
            lines.Add($"formation in place: {figure} by {by}, call {call}, round {round} — {stood} of {of} stood, {(shared ? "places shared" : "places not shared yet")}, {placed} of {of} placed, {queued} step(s) queued");
            if (table != "") foreach (var entry in table.Split(';')) lines.Add("  " + entry.Replace("@", " stands at ").Replace(">", " → goes to "));
        }
        return Reply.Done(string.Join(Environment.NewLine, lines), json);
    }

    // the lab's lever (ajuste 87) from the warden: each name's mark carried as that golem's own line
    private async Task<Reply> PlaceAsync(Command command)
    {
        if (command.Values.ContainsKey("")) return Reply.Syntax("the warden has no body to carry: name the golems, place blue@6,6.2 red@7,6.3");
        var replies = new List<(string Who, Reply Reply)>();
        foreach (var (who, at) in command.Values)
        {
            var peer = await warden.CommandPeerAsync(who, $"golem {who} place {at}");
            replies.Add((who, peer == null ? Reply.Refused($"this is the warden: no golem named '{who}' on the wire")
                            : peer.Status == 200 ? Reply.Done(peer.Text, "")
                            : peer.Status == 409 ? new Reply("refused", peer.Text, "")
                            : Reply.Syntax(peer.Text)));
        }
        return Gathered(replies);
    }

    private static Reply Help(Command command)
    {
        var own = new[] { "choreograph", "rotate", "fleet", "place", "help" };
        if (command.Text != "")
        {
            var one = CommandLine.Help.FirstOrDefault(h => h.Verb == command.Text) ?? WardenHelp.FirstOrDefault(h => h.Verb == command.Text);
            return one == null ? Reply.Syntax($"'{command.Text}' is no command") : Reply.Done($"{one.Usage}   {one.What}   e.g. {one.Example}", "");
        }
        var lines = new List<string> { "the warden's own:" };
        lines.AddRange(WardenHelp.Select(h => $"  {h.Usage}   {h.What}"));
        lines.AddRange(CommandLine.Help.Where(h => h.Verb is "choreograph" or "rotate" or "fleet").Select(h => $"  {h.Usage}   {h.What}"));
        lines.Add("a golem's, with the golem in front — golem blue …, golem red,yellow …, golem all …:");
        lines.AddRange(CommandLine.Help.Where(h => !own.Contains(h.Verb) && h.Verb != "--with").Select(h => $"  golem <names|all> {h.Usage}   {h.What}"));
        return Reply.Done(string.Join(Environment.NewLine, lines), JsonSerializer.Serialize(WardenHelp.Concat(CommandLine.Help.Where(h => h.Verb != "--with")).ToList()));
    }

    /// <summary>The warden's own commands, for the help and the panel.</summary>
    public static readonly IReadOnlyList<CommandHelp> WardenHelp = new[]
    {
        new CommandHelp("place", "place blue@x,y red@x,y …", "a lab lever: each golem named carried onto its own mark (its own line, carried)", "place blue@6,6.2 red@7,6.3 green@3.5,7.5 yellow@3,3"),
        new CommandHelp("golem", "golem <name|a,b|all> <command …>", "a golem's command carried to the golems named in front, each its own line; the answers come back one per golem", "golem all enter open-floor"),
    };

    private static void Walk(JsonElement e, Action<JsonElement> onObject)
    {
        switch (e.ValueKind)
        {
            case JsonValueKind.Object: onObject(e); foreach (var p in e.EnumerateObject()) Walk(p.Value, onObject); break;
            case JsonValueKind.Array: foreach (var i in e.EnumerateArray()) Walk(i, onObject); break;
        }
    }
    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : "";
    private static int Int(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetInt32(out int i) ? i : 0;
    private static string Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.TryGetDouble(out double d) ? d.ToString("0.##", CultureInfo.InvariantCulture) : "?";
    private static string FirstLine(string text) => (text ?? "").Split('\n')[0].Trim();
}
