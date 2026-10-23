using System.Globalization;
using System.Text.RegularExpressions;

namespace GolemAPI.Commanding;

/// <summary>A line the operator wrote, read: the golem it is for (`golem red …`; "" when it names none — the console's own), the peers
/// the same command is carried to (`--with blue,green`, `--with all`), the command, the points it names, its options (`--all`), and the
/// text it carries (a help topic). The points are what an errand is made of — points, never places (Juan, 16-sep-2026).</summary>
public sealed record Command(string Golem, IReadOnlyList<string> With, string Verb, IReadOnlyList<(double X, double Y)> Points, IReadOnlyList<string> Options, string Text,
                             IReadOnlyDictionary<string, string> Values)
{
    public Command(string golem, IReadOnlyList<string> with, string verb, IReadOnlyList<(double X, double Y)> points, IReadOnlyList<string> options, string text)
        : this(golem, with, verb, points, options, text, new Dictionary<string, string>()) { }
}

/// <summary>A line that is no command, and why, in words the operator reads and writes again — never a 500.</summary>
public sealed class CommandSyntaxException : Exception
{
    public CommandSyntaxException(string message) : base(message) { }
}

/// <summary>One command of the language, as the help tells it: how it is written, what it does, an example.</summary>
public sealed record CommandHelp(string Verb, string Usage, string What, string Example);

// THE LANGUAGE THE GOLEM IS COMMANDED IN (propuesta 58, 28-sep-2026; Juan: "un lenguaje nuevo que al final lo transforma, como si fuera
// un comando de linux, un binario haciendo las acciones que ya tenemos: el api traduce eso llamando a GolemEmbodiment para que ejecute
// los scripts ya hechos"). ONE line, like a shell's: `golem visit 2,9.5 9,8 --with blue` — the binary's name; the golem's, when the line
// is written away from its console (`golem red visit …`: the binary's business, refused on another golem's console); a command; its
// arguments, a point being `x,y`; its options with `--` — `--with blue,green` / `--with all` carries the SAME command to those peers too
// (Juan, 28-sep-2026: "si yo ya soy golem, ¿debería especificarme a mí mismo? quizás un parámetro tipo -- para especificar a los otros";
// "sí, con with": the line is always this golem's own, the peers ride along; the answers come back one per golem). Read here for the panel's console, a REPL and the binary alike,
// so the syntax is written and documented once, in the API. No script of the actor's ever travels on a line: the reads are commands of
// their own (state, route, where, obstacles). A `@name` is a value the CONSOLE keeps and puts on the line before it is sent (`set`,
// `show`): here it is only refused with the way to set it. The parser knows nothing of the golem: it reads; the Commander acts.
public static class CommandLine
{
    /// <summary>Every command, in the order the help lists them. `set` and `show` are the console's own (their values never reach the golem).</summary>
    public static readonly IReadOnlyList<CommandHelp> Help = new[]
    {
        new CommandHelp("visit", "visit x,y [x,y …]", "an errand through those points, in that order", "visit 2,9.5 9,8"),
        new CommandHelp("cover", "cover x,y x,y [x,y …]", "an errand through those points, in the order the golem finds shortest", "cover 9,1.5 2,9.5 9,9.5"),
        new CommandHelp("form", "form <name> square|pentagon|triangle --center x,y --side s [--angle a] [--fleet a,b] [--by rank|distance|rps] [--cue <formation>] [--on-cue] [--after <formation> [--every 5s]] [--open [--places n]]  ·  form <name> circle --center x,y --radius r …  ·  form <name> propeller --center x,y --radius R [--inner-radius hub] [--blades n] …", "a formation of the golem's (propuesta 104): its name, its figure, its centre, its size (a polygon's side, a circle's radius, a propeller's tips — its hub a third of it and three blades unless told; a step turns it one blade, every golem to the next blade at its own distance), its orientation (counter-clockwise, 0 as laid out), its FLEET (left out: this golem and every peer) and the POLICY that shares the places — by rank, the names sorted, the default; by distance, who stands nearest (ajuste 73); by rps, who stands nearest and, when several want the same place, rock, paper, scissors between them (propuesta 115); told again under the same name, it is made again (moved, turned, resized, re-crewed); every golem of the fleet is told the same (--with is refused); with --cue <formation> the last of the fleet to finish tells that formation 'your turn' once every member said finish (propuesta 117; by the formation's name since propuesta 123: every member of it takes the cue, no golem relays it); with --on-cue the formation starts on that word — no take: it waits, its steps queued, and dances when it is cued; with --after <formation> (propuesta 118, the pull) it waits the same way, and its first golem by rank COMMITS ITSELF to ask that formation whether it finished — its first golem by rank answers — every 5 s, or what --every says (propuesta 122: the engine's own remember, a recurrence armed inside the Follows act that asks while the answer has not come and stops by itself when it does — its cue — or the formation is taken or dissolved); with --open (propuesta 123) the formation is one of a PROGRAMME said before it on the same tab — no --fleet: the union's candidates are its fleet until the union's law picks its members by distance or rps (--by rank cannot open; a polygon's vertices are its places, or --places n says for how many golems — the corners first, the rest on the sides — and a circle opens with a count); it answers the places it resolved and who holds each", "form square-2 square --center 5.5,5.5 --side 2 --fleet blue,red,green,yellow"),
        new CommandHelp("union", "union <name> <formation,formation,…> [--fleet a,b] [--by distance|rps] [--link cue|ask [--every 5s]]", "THE UNION (propuesta 123): several OPEN formations (each told `form … --open` after it, on every candidate's tab) and the golems shared among them by the law — nobody is told a fleet: the candidates are every golem the union was told to (left out: this golem and every peer); `take <union>` is each one's single word, where it stands; with every word in and every figure told, each copy shares every candidate among every place of every figure — by distance the free place nearest to each, over all the figures at once, a figure full sending the next one to the other; by rps the places wanted by several are played; more candidates than places: the farthest are left out and free; a formation that would get nobody leaves the union unresolved and says why. THE SCORE (propuesta 124): every formation of the union is taken at once and the steps queued in each (`rotate <formation> …`, written on every tab) are its part — the k-th step of every formation is the k-th BEAT, danced together: a beat opens when every formation has every member on its place (one with nothing queued stays), and the movers of the whole union set out together by the LINK — by the cue (the words of line-up across the union, the default) or by asking every so often (to come); the union is done when every member of every formation said finish. An open formation has no --cue, --on-cue or --after of its own", "union two-triangles tri-left,tri-right --by distance --link cue"),
        new CommandHelp("program", "program <name> [--fleet a,b] [--by distance|rps] [--link cue|ask [--every 5s]] --form <formation> triangle|square|pentagon|circle <cx,cy> side|radius <m> [angle <degrees>] [places <n>] [--form …] [--score <formation>=cw,ccw,-,… …]", "THE PROGRAM (propuesta 124): the whole union in ONE line, the same on every candidate's tab — its formations in order (--form, each with its figure, centre, size, orientation and how many golems it takes; a polygon left without places takes its vertices) and its SCORE (--score: a row per formation, a column per beat — cw, ccw, or - for a rest, that beat the formation stays); the domain makes the union, opens every formation into it, queues every step and implies each formation's finish at the end of its row; the line is this golem's word too, where it stands (no take): with every candidate's word in, the law shares them by distance or rps and every formation sets out at once, beat by beat, agreed by the link; the union is done when everybody finished", "program two-triangles --by distance --link cue --form tri-left triangle 3,5.5 side 3 angle 0 places 3 --form tri-right triangle 8,5.5 side 3 places 3 --score tri-left=cw,cw,cw tri-right=cw,cw,-"),
        new CommandHelp("unions", "unions", "the unions this golem was told: candidates, formations, who spoke, whether resolved, who dances where, who was left out", "unions"),
        new CommandHelp("take", "take <name>", "the fleet takes that formation: this golem says where it stands, every peer of the fleet is told and says where it stands, and the routes open when everybody spoke — each to the place the policy gives it, ending facing the centre; it spreads by itself (--with is refused)", "take square-2"),
        new CommandHelp("rotate", "rotate <name> clockwise|counterclockwise", "one step of that formation: every body takes the next place in that sense, once everybody stands on its own; steps queue — say it three times for three steps (ajuste 77); it spreads by itself (--with is refused)", "rotate square-2 clockwise"),
        new CommandHelp("dissolve", "dissolve <name>", "the formation leaves every golem of its fleet; a route of it still pending is let go", "dissolve square-2"),
        new CommandHelp("finish", "finish <name>", "this golem finishes its part of the formation (propuesta 117): the last line of its script about it, queued like the steps — it counts when the last step is walked; the fleet is told, and the one that counts the fleet's last finish cues the golem the formation names (--cue); the formation stays (dissolve lets it go)", "finish tri-left"),
        new CommandHelp("disputes", "disputes", "the places this golem disputed with another by distance, formation by formation: whether it won the place or yielded it, to whom, and both distances (a golem disputes the place nearest to where it stood)", "disputes"),
        new CommandHelp("formations", "formations", "the formations this golem is in: each one's figure, centre, size, orientation, policy, fleet and who holds which place", "formations"),
        new CommandHelp("play", "play <name> pacman --pacman <golem> --ghosts a,b [--as pacman|ghost] | play | play over", "a play staged on this golem by its name (its identity across the fleet, like a formation's) and its kind — pacman: who plays pacman and who the ghosts (left out, this golem and every peer but pacman); pacman is autonomous — the rule alone drives it (21-oct-2026); written on every member's tab by the warden, each line saying with --as what that golem plays (left out, what the cast says of its name), never --with; alone, play says the current play — its part, state, heading, runs, a ghost's chases, who ended it; play over ends it for the whole cast (a ghost touching pacman ends it by itself)", "play game-1 pacman --pacman blue --ghosts red,green,yellow"),
        new CommandHelp("plays", "plays", "every play this golem was told, the ended ones among them: name, kind, state, cast, who ended it", "plays"),
        new CommandHelp("start", "start <name>", "the start of a play on this golem, written on every member's tab (16-oct-2026): pacman sets out — its first run, straight down the corridor — and a ghost joins the hunt, chasing from then on as pacman's runs release it from the den", "start game-1"),
        new CommandHelp("then", "then x,y", "one more stop, told to the newest route while it is pending", "then 5.5,5.5"),
        new CommandHelp("pause", "pause", "hold the golem where its body stands", "pause"),
        new CommandHelp("resume", "resume", "let it go on", "resume"),
        new CommandHelp("forget", "forget x,y  ·  forget disputes | forget play <name> | forget map <name>", "what stood there is gone: the golem forgets it, marks and all, and tells the peers — or, with disputes, it forgets the disputes it had over places (the next ones come with the next rounds played); forget play <name> takes a play off the list once it is over (a running one: play over first)", "forget 10.13,5.5"),
        new CommandHelp("anchor", "anchor", "the golem takes where its body REALLY stands (telemetry) as where it stands — the scenario whose region holds it, a waking there, a plan underway decided again — without a reset and without moving anything; for when its belief drifted from the truth (13-oct-2026)", "anchor"),
        new CommandHelp("reset", "reset [--all]", "let go: every pending route abandoned, the body back on its mark; --all wipes the journal — the peers' too — and reboots reborn", "reset"),
        new CommandHelp("state", "state", "the board: the routes pending, the one underway and its next point, how the last one ended", "state"),
        new CommandHelp("route", "route", "the newest route: its way, the legs ahead, what it asks the body now", "route"),
        new CommandHelp("where", "where", "where the golem knows its body stands, facing which way, in which zone", "where"),
        new CommandHelp("obstacles", "obstacles", "what the bodies learned by touching: the things outlined, the peers met", "obstacles"),
        new CommandHelp("place", "place x,y | place blue@x,y red@x,y …", "a lab lever (ajuste 87): the body carried onto that mark — every pending route let go, the body re-anchored there, the golem awake where it stands; with names, each golem of the fleet carried onto its own mark (this golem's own by its name or bare, every peer's line carried to it); never --with", "place blue@6,6.2 red@7,6.3 green@3.5,7.5 yellow@3,3"),
        new CommandHelp("optimize", "optimize on-the-way|door-by-door", "how the golem optimizes the routes it computes from here on: switches on one of its two strategies — the doors crossed on the way (fewer stops), or every door a leg of its own (the direct way, nothing converted); every route is born direct and the script improves it with a dash while on-the-way is active — at birth and at its next arrival (ajustes 62, 63; was adopt, ajuste 82)", "optimize on-the-way"),
        new CommandHelp("speed", "speed <m/s> [--turn <rad/s>]", "the body's pace from here on (propuesta 111): its cruise in metres per second and, with --turn, how fast it turns in place in radians per second — every order after it carries them; born at 2 m/s and 1.5 rad/s (body_v1 and the body node's own cap); the motors cap what the body can really do", "speed 3 --turn 2.5"),
        new CommandHelp("use", "use map <scenario>", "the golem uses that map — it enters the scenario it knows by that name, its collisions with it, from here on (warehouse, open-floor); refused while a route is pending (was enter, 7-oct-2026)", "use map open-floor"),
        new CommandHelp("scenarios", "scenarios", "the scenarios the golem knows, and the one it is in", "scenarios"),
        new CommandHelp("explored", "explored", "what the golem explored here: the trail, the cells it knows (free, wall) and the frontier of the unknown", "explored"),
        new CommandHelp("explore", "explore [stop | clear]", "the exploration mode (propuesta 120), always BLIND: the golem blindfolds itself first — a scenario of its own with no map at all, blind-<scenario>, where its body stands (taken up again if it exists) — and probes by itself, cell by cell into the unknown, until nothing unknown is left next to what it knows, or until explore stop; explore clear starts what was explored here afresh (stopped first)", "explore"),
        new CommandHelp("export", "export map <name>", "the map the golem deduced here becomes a scenario of its own under that name (propuesta 120): use map <name> enters it, forget map <name> drops it", "export map explored-1"),
        new CommandHelp("set", "set name x,y [x,y …]", "the console keeps a value under that name, to write @name on a line (visit @stops)", "set stops 2,9.5 9,8"),
        new CommandHelp("show", "show [name]", "the values the console keeps", "show"),
        new CommandHelp("help", "help [command]", "this table, or one command of it", "help visit"),
        new CommandHelp("--with", "<command> … --with blue[,green] | --with all", "the same command carried to those peers too, over the wire; one answer per golem comes back", "visit 2,9.5 --with blue"),
    };

    private static readonly Regex Parenthesised = new(@"\(\s*(-?\d+(?:\.\d+)?)\s*,\s*(-?\d+(?:\.\d+)?)\s*\)", RegexOptions.Compiled);
    private static readonly Regex Point = new(@"^(?<x>-?\d+(?:\.\d+)?),(?<y>-?\d+(?:\.\d+)?)$", RegexOptions.Compiled);
    private static readonly Regex Name = new(@"^[A-Za-z_][A-Za-z0-9_-]*$", RegexOptions.Compiled);
    private static readonly Regex Names = new(@"^[A-Za-z_][A-Za-z0-9_-]*(,[A-Za-z_][A-Za-z0-9_-]*)*$", RegexOptions.Compiled);
    private const string WithUsage = "--with: expected the peers to carry the command to, like --with blue, --with blue,green or --with all";

    /// <summary>The line read. A line that is no command throws, saying what was expected and where.</summary>
    public static Command Parse(string line)
    {
        string text = Parenthesised.Replace((line ?? "").Trim(), "$1,$2");   // `(2, 9.5)` is welcome too: it reads as 2,9.5
        var tokens = text.Split((char[])null, StringSplitOptions.RemoveEmptyEntries).ToList();
        string named = "";
        if (tokens.Count > 0 && tokens[0].ToLowerInvariant() == "golem")
        {
            tokens.RemoveAt(0);   // the binary's name, optional here
            // the golem's name may follow it — `golem red visit …` — when the next word is no command of the language
            if (tokens.Count > 0 && Name.IsMatch(tokens[0]) && !Help.Any(h => h.Verb == tokens[0].ToLowerInvariant()))
            {
                named = tokens[0].ToLowerInvariant();
                tokens.RemoveAt(0);
            }
        }
        if (tokens.Count == 0) throw new CommandSyntaxException("nothing to do: write a command, or help");
        string verb = tokens[0].ToLowerInvariant();
        var args = tokens.Skip(1).ToList();
        // `--with blue,green` anywhere after the command: the peers the same command is carried to; `all` for every peer on the wire
        var with = new List<string>();
        for (int i = 0; i < args.Count;)
        {
            if (args[i].ToLowerInvariant() != "--with") { i++; continue; }
            if (i + 1 >= args.Count || !Names.IsMatch(args[i + 1])) throw new CommandSyntaxException(WithUsage);
            with.AddRange(args[i + 1].ToLowerInvariant().Split(','));
            args.RemoveRange(i, 2);
        }
        with = with.Distinct().ToList();
        var none = Array.Empty<(double X, double Y)>();
        var noOptions = Array.Empty<string>();
        switch (verb)
        {
            case "visit":
                return new Command(named, with, verb, Points(verb, args, atLeast: 1), noOptions, "");
            case "cover":
                return new Command(named, with, verb, Points(verb, args, atLeast: 2), noOptions, "");
            case "then":
            case "forget":
                if (args.Count == 1 && args[0].Equals("disputes", StringComparison.OrdinalIgnoreCase))
                    return new Command(named, with, verb, none, noOptions, "disputes");   // the disputes over places forgotten (14-oct-2026)
                if (args.Count >= 1 && args[0].Equals("map", StringComparison.OrdinalIgnoreCase))   // a scenario the explorer exported, dropped (propuesta 120)
                {
                    if (args.Count != 2 || !Name.IsMatch(args[1])) throw new CommandSyntaxException("forget map: expected the map's name, like forget map explored-1");
                    return new Command(named, with, verb, none, noOptions, "map", new Dictionary<string, string> { ["map"] = args[1].ToLowerInvariant() });
                }
                if (args.Count >= 1 && args[0].Equals("play", StringComparison.OrdinalIgnoreCase))   // a play forgotten (16-oct-2026): off the list
                {
                    if (args.Count != 2 || !Name.IsMatch(args[1])) throw new CommandSyntaxException("forget play: expected the play's name, like forget play game-1");
                    return new Command(named, with, verb, none, noOptions, "play", new Dictionary<string, string> { ["play"] = args[1].ToLowerInvariant() });
                }
                return new Command(named, with, verb, Points(verb, args, exactly: 1), noOptions, "");
            case "form":
            {
                // a formation of the golem's (propuesta 104): its name, its figure, then --center x,y, --side s | --radius r [--inner-radius hub],
                // --angle degrees, --fleet a,b, --by rank|distance|rps, --cue <formation>, --on-cue, --after <formation> [--every 5s]
                var values = new Dictionary<string, string>();
                var rest = new List<string>();
                for (int i = 0; i < args.Count; i++)
                {
                    string a = args[i].ToLowerInvariant();
                    if (a == "--on-cue") { values["on-cue"] = "true"; continue; }   // a flag: the formation starts on cue (propuesta 117)
                    if (a == "--open") { values["open"] = "true"; continue; }       // a flag: one of a union, its members picked by the law (propuesta 123)
                    if (a == "--ask") throw new CommandSyntaxException("form: --ask is gone (propuesta 123) — the question names the formation waited for, --after <formation>, and its first golem by rank answers");
                    if (a is "--center" or "--side" or "--radius" or "--inner-radius" or "--angle" or "--fleet" or "--by" or "--blades" or "--cue" or "--after" or "--every" or "--places")
                    {
                        if (i + 1 >= args.Count) throw new CommandSyntaxException($"form: {a} needs a value, like {a switch { "--center" => "--center 5.5,5.5", "--side" => "--side 2.0", "--radius" => "--radius 1.5", "--inner-radius" => "--inner-radius 0.8", "--fleet" => "--fleet blue,red", "--by" => "--by rank", "--blades" => "--blades 3", "--cue" => "--cue tri-right", "--after" => "--after tri-left", "--every" => "--every 5s", "--places" => "--places 3", _ => "--angle 45" }}");
                        values[a[2..]] = args[++i];
                        continue;
                    }
                    if (a.StartsWith("--")) throw new CommandSyntaxException($"form: '{Head(a)}' is no option; the options are --center x,y, --side s | --radius r [--inner-radius hub] [--blades n], --angle degrees, --fleet a,b, --by rank|distance|rps, --cue <formation>, --on-cue, --after <formation> [--every 5s], --open [--places n]");
                    rest.Add(args[i]);
                }
                if (rest.Count != 2 || !Name.IsMatch(rest[0])) throw new CommandSyntaxException("form: expected the formation's name and its figure, like form square-2 square --center 5.5,5.5 --side 2 --fleet blue,red");
                string figure = rest[1].ToLowerInvariant();
                if (figure is not ("square" or "pentagon" or "triangle" or "circle" or "propeller")) throw new CommandSyntaxException($"form: the figure is square, pentagon, triangle, circle or propeller; found '{Head(rest[1])}'");
                bool round = figure is "circle" or "propeller";
                string size = round ? "radius" : "side";
                if (values.ContainsKey(round ? "side" : "radius")) throw new CommandSyntaxException(round ? $"form: a {figure} is said by its --radius, not a side" : $"form: a {figure} is said by its --side, not a radius");
                if (!values.ContainsKey("center") || !values.ContainsKey(size)) throw new CommandSyntaxException($"form {rest[0]}: expected --center x,y and --{size} {(size == "side" ? "s" : "r")}");
                var centre = Points(verb, new[] { values["center"] }, exactly: 1);
                if (!double.TryParse(values[size], NumberStyles.Float, CultureInfo.InvariantCulture, out double measure) || measure <= 0)
                    throw new CommandSyntaxException($"form: --{size} expects metres greater than zero, like --{size} {(size == "side" ? "2.0" : "1.5")}; found '{Head(values[size])}'");
                if (figure == "propeller")
                {
                    // the propeller (propuesta 114): its hub (--inner-radius, a third of the radius unless told) and its blades (three unless told)
                    if (values.TryGetValue("inner-radius", out var hub) && (!double.TryParse(hub, NumberStyles.Float, CultureInfo.InvariantCulture, out double h) || h <= 0 || h >= measure))
                        throw new CommandSyntaxException("form: a propeller's --inner-radius (its hub) is metres greater than zero and smaller than --radius");
                    if (values.TryGetValue("blades", out var bladeText) && (!int.TryParse(bladeText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int b) || b < 2 || b > 12))
                        throw new CommandSyntaxException($"form: --blades expects a whole number from 2 to 12, like --blades 3; found '{Head(bladeText)}'");
                }
                else if (values.ContainsKey("inner-radius")) throw new CommandSyntaxException($"form: only a propeller has an inner radius (its hub); a {figure} has none");
                if (values.ContainsKey("blades") && figure != "propeller") throw new CommandSyntaxException($"form: only a propeller has --blades; a {figure} has none");
                if (values.TryGetValue("angle", out var angle) && !double.TryParse(angle, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    throw new CommandSyntaxException($"form: --angle expects degrees, like --angle 45; found '{Head(angle)}'");
                if (values.TryGetValue("by", out var by))
                {
                    values["by"] = by.ToLowerInvariant();
                    if (values["by"] is not ("rank" or "distance" or "rps")) throw new CommandSyntaxException($"form: --by is rank, distance or rps; found '{Head(by)}'");
                }
                if (values.TryGetValue("fleet", out var crew) && !Names.IsMatch(crew)) throw new CommandSyntaxException($"form: --fleet expects names like blue,red; found '{Head(crew)}'");
                if (values.TryGetValue("cue", out var cue))
                {
                    // the formation the last of this fleet to finish cues (propuesta 117; its name since propuesta 123): one name, not this one
                    if (!Name.IsMatch(cue) || cue.Contains(',')) throw new CommandSyntaxException($"form: --cue expects one formation's name, like --cue tri-right; found '{Head(cue)}'");
                    values["cue"] = cue.ToLowerInvariant();
                    if (values["cue"] == rest[0].ToLowerInvariant()) throw new CommandSyntaxException($"form {rest[0]}: a formation cannot cue itself");
                }
                // the pull (propuesta 118): the formation this one starts after — the one asked (propuesta 123: its first golem by rank answers)
                if (values.TryGetValue("after", out var after))
                {
                    if (!Name.IsMatch(after)) throw new CommandSyntaxException($"form: --after expects a formation's name, like --after tri-left; found '{Head(after)}'");
                    values["after"] = after.ToLowerInvariant();
                    if (values["after"] == rest[0].ToLowerInvariant()) throw new CommandSyntaxException($"form {rest[0]}: a formation cannot start after itself");
                }
                // how often the asker asks (propuesta 121): seconds above zero, with or without an s; only with --after …
                if (values.TryGetValue("every", out var every))
                {
                    if (!values.ContainsKey("after")) throw new CommandSyntaxException("form: --every goes with --after <formation>: how often the golem asks");
                    string seconds = every.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? every[..^1] : every;
                    if (!double.TryParse(seconds, NumberStyles.Float, CultureInfo.InvariantCulture, out double period) || !double.IsFinite(period) || period <= 0)
                        throw new CommandSyntaxException($"form: --every expects seconds above zero, like --every 5s; found '{Head(every)}'");
                    values["every"] = period.ToString(CultureInfo.InvariantCulture);
                }
                if (values.ContainsKey("open"))
                {
                    // the open formation (propuesta 123): of a union, no fleet of its own, shared by distance or rps, a polygon
                    if (values.ContainsKey("fleet")) throw new CommandSyntaxException("form: --open takes no --fleet — the union's candidates are its fleet until the union's law picks its members");
                    if (values.ContainsKey("cue") || values.ContainsKey("on-cue") || values.ContainsKey("after")) throw new CommandSyntaxException("form: --open takes no --cue, --on-cue or --after — an open formation follows the beats of its union (propuesta 124): the link is the union's, --link cue|ask");
                    if (!values.TryGetValue("by", out var openBy) || openBy == "rank") throw new CommandSyntaxException("form: --open shares its places by distance or rps — write --by distance (or --by rps): by rank nobody chooses");
                    if (round && !values.ContainsKey("places")) throw new CommandSyntaxException($"form: a {figure} opens for a number of golems — --places n; a polygon alone opens for its vertices");
                }
                // how many golems an open formation takes (22-oct-2026): a whole number above zero, only with --open
                if (values.TryGetValue("places", out var placesText))
                {
                    if (!values.ContainsKey("open")) throw new CommandSyntaxException("form: --places goes with --open — how many of the union's candidates this figure takes; a formation with its fleet has as many places as the fleet");
                    if (!int.TryParse(placesText, NumberStyles.Integer, CultureInfo.InvariantCulture, out int placesCount) || placesCount < 1) throw new CommandSyntaxException($"form: --places expects a whole number above zero, like --places 3; found '{Head(placesText)}'");
                    values["places"] = placesCount.ToString(CultureInfo.InvariantCulture);
                }
                values["figure"] = figure;
                return new Command(named, with, verb, centre, noOptions, rest[0].ToLowerInvariant(), values);
            }
            case "union":
            {
                // THE PROGRAMME (propuesta 123): its name, its formations in order, [--fleet a,b] (left out: this golem and every peer), [--by distance|rps]
                var values = new Dictionary<string, string>();
                var rest = new List<string>();
                for (int i = 0; i < args.Count; i++)
                {
                    string a = args[i].ToLowerInvariant();
                    if (a is "--fleet" or "--by" or "--link" or "--every")
                    {
                        if (i + 1 >= args.Count) throw new CommandSyntaxException($"union: {a} needs a value, like {a switch { "--fleet" => "--fleet blue,red", "--by" => "--by distance", "--link" => "--link cue", _ => "--every 5s" }}");
                        values[a[2..]] = args[++i];
                        continue;
                    }
                    if (a.StartsWith("--")) throw new CommandSyntaxException($"union: '{Head(a)}' is no option; the options are --fleet a,b, --by distance|rps and --link cue|ask [--every 5s]");
                    rest.Add(args[i]);
                }
                if (rest.Count != 2 || !Name.IsMatch(rest[0]) || !Names.IsMatch(rest[1])) throw new CommandSyntaxException("union: expected its name and its formations in order, like union two-triangles tri-left,tri-right");
                var bill = rest[1].ToLowerInvariant().Split(',').ToList();
                if (bill.Distinct().Count() != bill.Count) throw new CommandSyntaxException("union: each formation once");
                if (bill.Contains(rest[0].ToLowerInvariant())) throw new CommandSyntaxException($"union {rest[0]}: a union cannot name itself as a formation");
                if (values.TryGetValue("by", out var by))
                {
                    values["by"] = by.ToLowerInvariant();
                    if (values["by"] is not ("distance" or "rps")) throw new CommandSyntaxException($"union: --by is distance or rps — by rank nobody chooses; found '{Head(by)}'");
                }
                if (values.TryGetValue("fleet", out var crew) && !Names.IsMatch(crew)) throw new CommandSyntaxException($"union: --fleet expects names like blue,red; found '{Head(crew)}'");
                // THE LINK (propuesta 124): how the beats are agreed among the golems — by the cue, or by asking every so often
                if (values.TryGetValue("link", out var link))
                {
                    values["link"] = link.ToLowerInvariant();
                    if (values["link"] is not ("cue" or "ask")) throw new CommandSyntaxException($"union: --link is cue or ask — how the beats are agreed; found '{Head(link)}'");
                }
                if (values.TryGetValue("every", out var every))
                {
                    if (!values.TryGetValue("link", out var l) || l != "ask") throw new CommandSyntaxException("union: --every goes with --link ask: how often the askers ask");
                    string secs = every.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? every[..^1] : every;
                    if (!double.TryParse(secs, NumberStyles.Float, CultureInfo.InvariantCulture, out double period) || !double.IsFinite(period) || period <= 0)
                        throw new CommandSyntaxException($"union: --every expects seconds above zero, like --every 5s; found '{Head(every)}'");
                    values["every"] = period.ToString(CultureInfo.InvariantCulture);
                }
                values["formations"] = string.Join(",", bill);
                return new Command(named, with, verb, none, noOptions, rest[0].ToLowerInvariant(), values);
            }
            case "program":
            {
                // THE PROGRAM (propuesta 124): its name, [--fleet a,b] [--by distance|rps] [--link cue|ask [--every 5s]], then --form clauses and --score rows;
                // the line composes the DEFINITION the domain reads — `name: figure x,y side m angle g places n = steps; …` — and validates its shape only
                if (args.Count == 0 || !Name.IsMatch(args[0])) throw new CommandSyntaxException("program: expected its name first, like program two-triangles --form …");
                var values = new Dictionary<string, string>();
                var forms = new List<(string Name, string Text)>();
                var score = new Dictionary<string, string>(StringComparer.Ordinal);
                int i = 1;
                while (i < args.Count)
                {
                    string a = args[i].ToLowerInvariant();
                    if (a is "--fleet" or "--by" or "--link" or "--every")
                    {
                        if (i + 1 >= args.Count) throw new CommandSyntaxException($"program: {a} needs a value, like {a switch { "--fleet" => "--fleet blue,red", "--by" => "--by distance", "--link" => "--link cue", _ => "--every 5s" }}");
                        values[a[2..]] = args[i + 1];
                        i += 2;
                        continue;
                    }
                    if (a == "--form")
                    {
                        var clause = new List<string>();
                        for (i++; i < args.Count && !args[i].StartsWith("--"); i++) clause.Add(args[i]);
                        if (clause.Count < 5 || !Name.IsMatch(clause[0])) throw new CommandSyntaxException("program: --form expects <formation> <figure> <cx,cy> side|radius <m> [angle <degrees>] [places <n>], like --form tri-left triangle 3,5.5 side 3 places 3");
                        string formation = clause[0].ToLowerInvariant(), figure = clause[1].ToLowerInvariant();
                        if (figure is not ("triangle" or "square" or "pentagon" or "circle")) throw new CommandSyntaxException($"program: a formation of a program is a triangle, a square, a pentagon or a circle; found '{Head(clause[1])}'");
                        if (!Point.IsMatch(clause[2])) throw new CommandSyntaxException($"program: --form {formation}: the centre is x,y, like 3,5.5; found '{Head(clause[2])}'");
                        var pairs = new List<string>();
                        for (int k = 3; k < clause.Count; k += 2)
                        {
                            string key = clause[k].ToLowerInvariant();
                            if (key is not ("side" or "radius" or "angle" or "places") || k + 1 >= clause.Count || !double.TryParse(clause[k + 1], NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                                throw new CommandSyntaxException($"program: --form {formation}: expected side|radius <m>, angle <degrees>, places <n>; found '{Head(string.Join(' ', clause.Skip(k)))}'");
                            pairs.Add($"{key} {clause[k + 1]}");
                        }
                        if (forms.Any(f => f.Name == formation)) throw new CommandSyntaxException($"program: --form {formation} twice");
                        forms.Add((formation, $"{formation}: {figure} {clause[2]} {string.Join(' ', pairs)}"));
                        continue;
                    }
                    if (a == "--score")
                    {
                        for (i++; i < args.Count && !args[i].StartsWith("--"); i++)
                        {
                            int eq = args[i].IndexOf('=');
                            if (eq <= 0) throw new CommandSyntaxException($"program: --score expects <formation>=<steps>, like tri-left=cw,cw,-; found '{Head(args[i])}'");
                            string formation = args[i][..eq].ToLowerInvariant(), steps = args[i][(eq + 1)..].ToLowerInvariant();
                            if (!Name.IsMatch(formation)) throw new CommandSyntaxException($"program: --score: '{Head(formation)}' is no formation's name");
                            if (steps.Split(',').Any(s => s is not ("cw" or "ccw" or "-" or "clockwise" or "counterclockwise" or "rest"))) throw new CommandSyntaxException($"program: --score {formation}: a step is cw, ccw or - (a rest); found '{Head(steps)}'");
                            score[formation] = steps;
                        }
                        continue;
                    }
                    throw new CommandSyntaxException($"program: '{Head(args[i])}' is no option; the options are --fleet a,b, --by distance|rps, --link cue|ask [--every 5s], --form … and --score …");
                }
                if (forms.Count == 0) throw new CommandSyntaxException("program: at least one --form <formation> …");
                foreach (var row in score.Keys) if (forms.All(f => f.Name != row)) throw new CommandSyntaxException($"program: --score names {row}, which no --form told");
                if (forms.Any(f => f.Name == args[0].ToLowerInvariant())) throw new CommandSyntaxException($"program {args[0]}: a program cannot name itself as a formation");
                if (values.TryGetValue("by", out var by))
                {
                    values["by"] = by.ToLowerInvariant();
                    if (values["by"] is not ("distance" or "rps")) throw new CommandSyntaxException($"program: --by is distance or rps — by rank nobody chooses; found '{Head(by)}'");
                }
                if (values.TryGetValue("fleet", out var crew) && !Names.IsMatch(crew)) throw new CommandSyntaxException($"program: --fleet expects names like blue,red; found '{Head(crew)}'");
                if (values.TryGetValue("link", out var link))
                {
                    values["link"] = link.ToLowerInvariant();
                    if (values["link"] is not ("cue" or "ask")) throw new CommandSyntaxException($"program: --link is cue or ask — how the beats are agreed; found '{Head(link)}'");
                }
                if (values.TryGetValue("every", out var every))
                {
                    if (!values.TryGetValue("link", out var l) || l != "ask") throw new CommandSyntaxException("program: --every goes with --link ask: how often the askers ask");
                    string secs = every.EndsWith("s", StringComparison.OrdinalIgnoreCase) ? every[..^1] : every;
                    if (!double.TryParse(secs, NumberStyles.Float, CultureInfo.InvariantCulture, out double period) || !double.IsFinite(period) || period <= 0)
                        throw new CommandSyntaxException($"program: --every expects seconds above zero, like --every 5s; found '{Head(every)}'");
                    values["every"] = period.ToString(CultureInfo.InvariantCulture);
                }
                values["formations"] = string.Join(",", forms.Select(f => f.Name));
                values["definition"] = string.Join("; ", forms.Select(f => score.TryGetValue(f.Name, out var steps) ? $"{f.Text} = {steps}" : f.Text));
                return new Command(named, with, verb, none, noOptions, args[0].ToLowerInvariant(), values);
            }
            case "take":
                // the formation taken (propuesta 104): its name — who goes where is the golem's to resolve, never the line's
                if (args.Count != 1 || !Name.IsMatch(args[0])) throw new CommandSyntaxException(args.Count > 1 && args[1].ToLowerInvariant() is "--place" or "--vertex"
                    ? "take: --place is gone (propuesta 104) — the golem resolves its own place by the formation's policy; write take square-2"
                    : "take: expected the formation's name, like take square-2");
                return new Command(named, with, verb, none, noOptions, args[0].ToLowerInvariant());
            case "rotate":
            {
                // one step of a formation, in that sense (ajuste 77); the whole figure — the rings turned apart went with the double ring (19-oct-2026)
                if (args.Count < 2 || !Name.IsMatch(args[0]) || args[1].ToLowerInvariant() is not ("clockwise" or "counterclockwise"))
                    throw new CommandSyntaxException("rotate: expected the formation's name and the sense, clockwise or counterclockwise, like rotate square-2 clockwise");
                var values = new Dictionary<string, string> { ["sense"] = args[1].ToLowerInvariant() };
                if (args.Count != 2) throw new CommandSyntaxException(args[2].ToLowerInvariant() == "--ring" ? "rotate: --ring is gone with the double ring (19-oct-2026) — a step turns the whole figure" : $"rotate: nothing comes after the sense; found '{Head(string.Join(' ', args.Skip(2)))}'");
                return new Command(named, with, verb, none, noOptions, args[0].ToLowerInvariant(), values);
            }
            case "dissolve":
                if (args.Count != 1 || !Name.IsMatch(args[0])) throw new CommandSyntaxException("dissolve: expected the formation's name, like dissolve square-2");
                return new Command(named, with, verb, none, noOptions, args[0].ToLowerInvariant());
            case "finish":
                // this golem finishes its part of the formation (propuesta 117): its name
                if (args.Count != 1 || !Name.IsMatch(args[0])) throw new CommandSyntaxException("finish: expected the formation's name, like finish tri-left");
                return new Command(named, with, verb, none, noOptions, args[0].ToLowerInvariant());
            case "play":
            {
                // the play of pacman (propuesta 108): alone, the read; else its title and --pacman <golem>, --ghosts a,b
                if (args.Count == 0) return new Command(named, with, verb, none, noOptions, "");
                if (args.Count == 1 && args[0].ToLowerInvariant() == "over") return new Command(named, with, verb, none, noOptions, "over");   // the warden ends it (fase 4)
                var values = new Dictionary<string, string>();
                var rest = new List<string>();
                for (int i = 0; i < args.Count; i++)
                {
                    string a = args[i].ToLowerInvariant();
                    if (a is "--pacman" or "--ghosts")
                    {
                        if (i + 1 >= args.Count || !(a == "--pacman" ? Name : Names).IsMatch(args[i + 1])) throw new CommandSyntaxException($"play: {a} needs {(a == "--pacman" ? "a golem's name" : "names, like --ghosts red,green")}");
                        values[a[2..]] = args[++i].ToLowerInvariant();
                        continue;
                    }
                    if (a == "--as")   // the part this golem plays, the warden's word on each tab (16-oct-2026)
                    {
                        if (i + 1 >= args.Count || args[i + 1].ToLowerInvariant() is not ("pacman" or "ghost")) throw new CommandSyntaxException("play: --as needs pacman or ghost");
                        values["as"] = args[++i].ToLowerInvariant();
                        continue;
                    }
                    if (a == "--steer") throw new CommandSyntaxException("play: --steer is gone (21-oct-2026) — pacman is autonomous, the rule alone drives it");
                    if (a.StartsWith("--")) throw new CommandSyntaxException($"play: '{Head(a)}' is no option; the options are --pacman <golem>, --ghosts a,b and --as pacman|ghost");
                    rest.Add(args[i]);
                }
                // the play by NAME and KIND (16-oct-2026; Juan: "generar un id del juego"): play game-1 pacman …
                if (rest.Count != 2 || !Name.IsMatch(rest[0]) || rest[1].ToLowerInvariant() != "pacman") throw new CommandSyntaxException("play: expected the play's name and its kind, like play game-1 pacman --pacman blue --ghosts red,green");
                values["kind"] = rest[1].ToLowerInvariant();
                return new Command(named, with, verb, none, noOptions, rest[0].ToLowerInvariant(), values);
            }
            case "head":
            case "steer":   // the keyboard and the driving modes are gone (21-oct-2026; Juan: "pacman autónomo nada más")
                throw new CommandSyntaxException($"'{verb}' is gone (21-oct-2026): pacman is autonomous — the rule alone drives it, nothing steers it");
            case "plays":   // every play told (16-oct-2026)
                if (args.Count > 0) throw new CommandSyntaxException($"plays takes nothing more; found '{Head(string.Join(' ', args))}'");
                return new Command(named, with, verb, none, noOptions, "");
            case "start":   // the start of a play, this golem's own (16-oct-2026): start <name>
                if (args.Count != 1 || !Name.IsMatch(args[0])) throw new CommandSyntaxException("start: expected the play's name, like start game-1");
                return new Command(named, with, verb, none, noOptions, args[0].ToLowerInvariant());
            case "optimize":
                if (args.Count != 1 || args[0].ToLowerInvariant() is not ("on-the-way" or "door-by-door")) throw new CommandSyntaxException("optimize: expected the strategy, on-the-way or door-by-door");
                return new Command(named, with, verb, none, noOptions, args[0].ToLowerInvariant());
            case "speed":
            {
                // the body's pace (propuesta 111): the cruise, and the turn rate after --turn; numbers with a decimal point, above zero
                if (args.Count is not (1 or 3) || !double.TryParse(args[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double cruise) || cruise <= 0)
                    throw new CommandSyntaxException("speed: expected the cruise in metres per second above zero, then --turn and the rate in radians per second, or nothing — like speed 3 --turn 2.5");
                var values = new Dictionary<string, string>();
                if (args.Count == 3)
                {
                    if (args[1].ToLowerInvariant() != "--turn" || !double.TryParse(args[2], NumberStyles.Float, CultureInfo.InvariantCulture, out double turning) || turning <= 0)
                        throw new CommandSyntaxException($"speed: after the cruise comes --turn and the rate in radians per second above zero; found '{Head(string.Join(' ', args.Skip(1)))}'");
                    values["turn"] = turning.ToString(CultureInfo.InvariantCulture);
                }
                return new Command(named, with, verb, none, noOptions, cruise.ToString(CultureInfo.InvariantCulture), values);
            }
            case "use":
                // the map the golem uses (Juan, 7-oct-2026: "cambiar en el lenguaje del api el comando de enter por use map"): `use map open-floor`
                // — the scenario named after that map; the domain's verb stays g.Enter
                if (args.Count != 2 || args[0].ToLowerInvariant() != "map" || !Name.IsMatch(args[1])) throw new CommandSyntaxException("use: expected map and the scenario's name, like use map open-floor");
                return new Command(named, with, verb, none, noOptions, args[1].ToLowerInvariant());
            case "enter":
                throw new CommandSyntaxException("enter is now use map <scenario>, like use map open-floor");
            case "pause":
            case "resume":
            case "state":
            case "route":
            case "where":
            case "obstacles":
            case "scenarios":
            case "explored":
            case "formations":
            case "unions":
            case "disputes":
                if (args.Count > 0) throw new CommandSyntaxException($"{verb} takes nothing more; found '{Head(string.Join(' ', args))}'");
                return new Command(named, with, verb, none, noOptions, "");
            case "place":
            {
                // a lab lever (ajuste 87): a mark for this golem — `place 3,3` — or one per golem — `place blue@6,6.2 red@7,6.3`; the names are
                // the line's values (the bare mark under ""), each carried to its golem by the Commander
                if (args.Count == 0) throw new CommandSyntaxException("place: expected a mark like 3,3 for this golem, or one per golem like blue@6,6.2 red@7,6.3");
                var marks = new Dictionary<string, string>();
                foreach (var a in args)
                {
                    int k = a.IndexOf('@');
                    if (k == 0) throw new CommandSyntaxException($"place: {a} is a name the console resolves — set it first, or write a golem's mark like blue@6,6.2");
                    string who = k > 0 ? a[..k].ToLowerInvariant() : "";
                    string at = k > 0 ? a[(k + 1)..] : a;
                    if (k > 0 && !Name.IsMatch(who)) throw new CommandSyntaxException($"place: expected a golem's name before @ at '{Head(a)}'");
                    if (!Point.IsMatch(at)) throw new CommandSyntaxException($"place: expected a mark like 3,3 at '{Head(a)}'");
                    if (marks.ContainsKey(who)) throw new CommandSyntaxException(who == "" ? "place: this golem's mark was given twice" : $"place: {who}'s mark was given twice");
                    marks[who] = at;
                }
                return new Command(named, with, verb, none, noOptions, "", marks);
            }
            case "anchor":
                if (args.Count > 0) throw new CommandSyntaxException($"anchor takes nothing more; found '{Head(string.Join(' ', args))}'");
                return new Command(named, with, verb, none, noOptions, "");
            case "explore":   // the exploration mode on, or off with stop (propuesta 120)
                if (args.Count == 0) return new Command(named, with, verb, none, noOptions, "");
                if (args.Count == 1 && args[0].ToLowerInvariant() is "stop" or "clear") return new Command(named, with, verb, none, noOptions, args[0].ToLowerInvariant());
                throw new CommandSyntaxException($"explore takes nothing, stop or clear; found '{Head(string.Join(' ', args))}'");
            case "export":   // the deduced map exported as a scenario of the golem's (propuesta 120): export map <name>
                if (args.Count != 2 || !args[0].Equals("map", StringComparison.OrdinalIgnoreCase) || !Name.IsMatch(args[1])) throw new CommandSyntaxException("export map: expected the map's name, like export map explored-1");
                return new Command(named, with, verb, none, noOptions, args[1].ToLowerInvariant());
            case "reset":
                foreach (var a in args)
                    if (a.ToLowerInvariant() != "--all") throw new CommandSyntaxException($"reset takes no argument but the option --all; found '{Head(a)}'");
                return new Command(named, with, verb, none, args.Select(a => a.ToLowerInvariant()).Distinct().ToList(), "");
            case "set":
                if (args.Count == 0 || !Name.IsMatch(args[0])) throw new CommandSyntaxException("set: expected a name and its points, like set stops 2,9.5 9,8");
                return new Command(named, with, verb, Points(verb, args.Skip(1).ToList(), atLeast: 1), noOptions, args[0]);
            case "show":
                if (args.Count > 1 || (args.Count == 1 && !Name.IsMatch(args[0]))) throw new CommandSyntaxException("show: expected nothing, or one name, like show stops");
                return new Command(named, with, verb, none, noOptions, args.Count == 1 ? args[0] : "");
            case "help":
                if (args.Count > 1) throw new CommandSyntaxException("help: expected nothing, or one command, like help visit");
                if (args.Count == 1 && !Help.Any(h => h.Verb == args[0].ToLowerInvariant()))
                    throw new CommandSyntaxException($"help: '{Head(args[0])}' is no command; the commands are {Verbs()}");
                return new Command(named, with, verb, none, noOptions, args.Count == 1 ? args[0].ToLowerInvariant() : "");
            default:
                throw new CommandSyntaxException($"'{Head(verb)}' is no command; the commands are {Verbs()} — help tells each");
        }
    }

    // The points after the command: `x,y` one after another; a `@name` is the console's to resolve first; anything else says where it stopped reading.
    private static IReadOnlyList<(double X, double Y)> Points(string verb, IReadOnlyList<string> args, int atLeast = 0, int exactly = 0)
    {
        var points = new List<(double X, double Y)>();
        foreach (var a in args)
        {
            if (a.StartsWith('@')) throw new CommandSyntaxException($"{verb}: {a} is a name the console resolves — set it first: set {a.TrimStart('@')} 2,9.5 9,8");
            var m = Point.Match(a);
            if (!m.Success) throw new CommandSyntaxException($"{verb}: expected a point like 2,9.5 at '{Head(a)}'");
            points.Add((double.Parse(m.Groups["x"].Value, CultureInfo.InvariantCulture), double.Parse(m.Groups["y"].Value, CultureInfo.InvariantCulture)));
        }
        if (exactly > 0 && points.Count != exactly)
            throw new CommandSyntaxException($"{verb}: expected {(exactly == 1 ? "one point" : exactly + " points")} like 2,9.5; found {points.Count}");
        if (points.Count < atLeast)
            throw new CommandSyntaxException($"{verb}: expected at least {(atLeast == 1 ? "one point" : atLeast + " points")} like 2,9.5" + (points.Count > 0 ? $"; found {points.Count}" : ""));
        return points;
    }

    private static string Verbs() => string.Join(", ", Help.Select(h => h.Verb));
    private static string Head(string s) => s.Length <= 24 ? s : s[..24] + "…";
}
