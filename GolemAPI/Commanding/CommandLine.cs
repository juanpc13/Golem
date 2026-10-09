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
        new CommandHelp("form", "form <name> square|pentagon|triangle --center x,y --side s [--angle a] [--fleet a,b] [--by rank|distance]  ·  form <name> circle --center x,y --radius r …  ·  form <name> double-ring --center x,y --radius R --inner-radius r --fleet a,b --inner-fleet c,d …", "a formation of the golem's (propuesta 104): its name, its figure, its centre, its size (a polygon's side, a circle's radius, a double ring's two radii), its orientation (counter-clockwise, 0 as laid out), its FLEET (left out: this golem and every peer) and the POLICY that shares the places — by rank, the names sorted, the default; by distance, who stands nearest (ajuste 73); told again under the same name, it is made again (moved, turned, resized, re-crewed); every golem of the fleet is told the same (--with is refused); it answers the places it resolved and who holds each", "form square-2 square --center 5.5,5.5 --side 2 --fleet blue,red,green,yellow"),
        new CommandHelp("take", "take <name>", "the fleet takes that formation: this golem says where it stands, every peer of the fleet is told and says where it stands, and the routes open when everybody spoke — each to the place the policy gives it, ending facing the centre; it spreads by itself (--with is refused)", "take square-2"),
        new CommandHelp("rotate", "rotate <name> clockwise|counterclockwise [--ring outer|inner]", "one step of that formation: every body — of one ring of a double ring, or of the whole figure — takes the next place in that sense, once everybody stands on its own; steps queue — say it three times for three steps (ajuste 77); it spreads by itself (--with is refused)", "rotate square-2 clockwise"),
        new CommandHelp("dissolve", "dissolve <name>", "the formation leaves every golem of its fleet; a route of it still pending is let go", "dissolve square-2"),
        new CommandHelp("formations", "formations", "the formations this golem is in: each one's figure, centre, size, orientation, policy, fleet and who holds which place", "formations"),
        new CommandHelp("then", "then x,y", "one more stop, told to the newest route while it is pending", "then 5.5,5.5"),
        new CommandHelp("pause", "pause", "hold the golem where its body stands", "pause"),
        new CommandHelp("resume", "resume", "let it go on", "resume"),
        new CommandHelp("forget", "forget x,y", "what stood there is gone: the golem forgets it, marks and all, and tells the peers", "forget 10.13,5.5"),
        new CommandHelp("reset", "reset [--all]", "let go: every pending route abandoned, the body back on its mark; --all wipes the journal — the peers' too — and reboots reborn", "reset"),
        new CommandHelp("state", "state", "the board: the routes pending, the one underway and its next point, how the last one ended", "state"),
        new CommandHelp("route", "route", "the newest route: its way, the legs ahead, what it asks the body now", "route"),
        new CommandHelp("where", "where", "where the golem knows its body stands, facing which way, in which zone", "where"),
        new CommandHelp("obstacles", "obstacles", "what the bodies learned by touching: the things outlined, the peers met", "obstacles"),
        new CommandHelp("place", "place x,y | place blue@x,y red@x,y …", "a lab lever (ajuste 87): the body carried onto that mark — every pending route let go, the body re-anchored there, the golem awake where it stands; with names, each golem of the fleet carried onto its own mark (this golem's own by its name or bare, every peer's line carried to it); never --with", "place blue@6,6.2 red@7,6.3 green@3.5,7.5 yellow@3,3"),
        new CommandHelp("optimize", "optimize on-the-way|door-by-door", "how the golem optimizes the routes it computes from here on: switches on one of its two strategies — the doors crossed on the way (fewer stops), or every door a leg of its own (the direct way, nothing converted); every route is born direct and the script improves it with a dash while on-the-way is active — at birth and at its next arrival (ajustes 62, 63; was adopt, ajuste 82)", "optimize on-the-way"),
        new CommandHelp("use", "use map <scenario>", "the golem uses that map — it enters the scenario it knows by that name, its collisions with it, from here on (warehouse, open-floor); refused while a route is pending (was enter, 7-oct-2026)", "use map open-floor"),
        new CommandHelp("scenarios", "scenarios", "the scenarios the golem knows, and the one it is in", "scenarios"),
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
                return new Command(named, with, verb, Points(verb, args, exactly: 1), noOptions, "");
            case "form":
            {
                // a formation of the golem's (propuesta 104): its name, its figure, then --center x,y, --side s | --radius r [--inner-radius r],
                // --angle degrees, --fleet a,b [--inner-fleet c,d], --by rank|distance
                var values = new Dictionary<string, string>();
                var rest = new List<string>();
                for (int i = 0; i < args.Count; i++)
                {
                    string a = args[i].ToLowerInvariant();
                    if (a is "--center" or "--side" or "--radius" or "--inner-radius" or "--angle" or "--fleet" or "--inner-fleet" or "--by")
                    {
                        if (i + 1 >= args.Count) throw new CommandSyntaxException($"form: {a} needs a value, like {a switch { "--center" => "--center 5.5,5.5", "--side" => "--side 2.0", "--radius" => "--radius 1.5", "--inner-radius" => "--inner-radius 0.8", "--fleet" => "--fleet blue,red", "--inner-fleet" => "--inner-fleet green,yellow", "--by" => "--by rank", _ => "--angle 45" }}");
                        values[a[2..]] = args[++i];
                        continue;
                    }
                    if (a == "--places") throw new CommandSyntaxException("form: --places is gone (propuesta 104) — the places are as many as the fleet: --fleet a,b");
                    if (a.StartsWith("--")) throw new CommandSyntaxException($"form: '{Head(a)}' is no option; the options are --center x,y, --side s | --radius r [--inner-radius r], --angle degrees, --fleet a,b [--inner-fleet c,d] and --by rank|distance");
                    rest.Add(args[i]);
                }
                if (rest.Count != 2 || !Name.IsMatch(rest[0])) throw new CommandSyntaxException("form: expected the formation's name and its figure, like form square-2 square --center 5.5,5.5 --side 2 --fleet blue,red");
                string figure = rest[1].ToLowerInvariant();
                if (figure is not ("square" or "pentagon" or "triangle" or "circle" or "double-ring")) throw new CommandSyntaxException($"form: the figure is square, pentagon, triangle, circle or double-ring; found '{Head(rest[1])}'");
                bool round = figure is "circle" or "double-ring";
                string size = round ? "radius" : "side";
                if (values.ContainsKey(round ? "side" : "radius")) throw new CommandSyntaxException(round ? $"form: a {figure.Replace('-', ' ')} is said by its --radius, not a side" : $"form: a {figure} is said by its --side, not a radius");
                if (!values.ContainsKey("center") || !values.ContainsKey(size)) throw new CommandSyntaxException($"form {rest[0]}: expected --center x,y and --{size} {(size == "side" ? "s" : "r")}");
                var centre = Points(verb, new[] { values["center"] }, exactly: 1);
                if (!double.TryParse(values[size], NumberStyles.Float, CultureInfo.InvariantCulture, out double measure) || measure <= 0)
                    throw new CommandSyntaxException($"form: --{size} expects metres greater than zero, like --{size} {(size == "side" ? "2.0" : "1.5")}; found '{Head(values[size])}'");
                if (figure == "double-ring")
                {
                    if (!values.TryGetValue("inner-radius", out var innerRadius) || !double.TryParse(innerRadius, NumberStyles.Float, CultureInfo.InvariantCulture, out double inner) || inner <= 0 || inner >= measure)
                        throw new CommandSyntaxException("form: a double ring needs --inner-radius r, metres greater than zero and smaller than --radius");
                    if (!values.ContainsKey("fleet") || !values.ContainsKey("inner-fleet")) throw new CommandSyntaxException("form: a double ring needs --fleet a,b (the outer ring's golems) and --inner-fleet c,d (the inner ring's)");
                }
                else if (values.ContainsKey("inner-radius") || values.ContainsKey("inner-fleet")) throw new CommandSyntaxException($"form: only a double ring has an inner ring; a {figure} takes --fleet alone");
                if (values.TryGetValue("angle", out var angle) && !double.TryParse(angle, NumberStyles.Float, CultureInfo.InvariantCulture, out _))
                    throw new CommandSyntaxException($"form: --angle expects degrees, like --angle 45; found '{Head(angle)}'");
                if (values.TryGetValue("by", out var by))
                {
                    values["by"] = by.ToLowerInvariant();
                    if (values["by"] is not ("rank" or "distance")) throw new CommandSyntaxException($"form: --by is rank or distance; found '{Head(by)}'");
                }
                foreach (var key in new[] { "fleet", "inner-fleet" })
                    if (values.TryGetValue(key, out var crew) && !Names.IsMatch(crew)) throw new CommandSyntaxException($"form: --{key} expects names like blue,red; found '{Head(crew)}'");
                values["figure"] = figure;
                return new Command(named, with, verb, centre, noOptions, rest[0].ToLowerInvariant(), values);
            }
            case "take":
                // the formation taken (propuesta 104): its name — who goes where is the golem's to resolve, never the line's
                if (args.Count != 1 || !Name.IsMatch(args[0])) throw new CommandSyntaxException(args.Count > 1 && args[1].ToLowerInvariant() is "--place" or "--vertex"
                    ? "take: --place is gone (propuesta 104) — the golem resolves its own place by the formation's policy; write take square-2"
                    : "take: expected the formation's name, like take square-2");
                return new Command(named, with, verb, none, noOptions, args[0].ToLowerInvariant());
            case "rotate":
            {
                // one step of a formation, in that sense (ajuste 77), of one ring or the whole figure (propuesta 104)
                if (args.Count < 2 || !Name.IsMatch(args[0]) || args[1].ToLowerInvariant() is not ("clockwise" or "counterclockwise"))
                    throw new CommandSyntaxException("rotate: expected the formation's name and the sense, clockwise or counterclockwise, like rotate square-2 clockwise");
                var values = new Dictionary<string, string> { ["sense"] = args[1].ToLowerInvariant() };
                if (args.Count == 4 && args[2].ToLowerInvariant() == "--ring" && args[3].ToLowerInvariant() is "outer" or "inner") values["ring"] = args[3].ToLowerInvariant();
                else if (args.Count != 2) throw new CommandSyntaxException($"rotate: after the sense comes --ring outer|inner, or nothing; found '{Head(string.Join(' ', args.Skip(2)))}'");
                return new Command(named, with, verb, none, noOptions, args[0].ToLowerInvariant(), values);
            }
            case "dissolve":
                if (args.Count != 1 || !Name.IsMatch(args[0])) throw new CommandSyntaxException("dissolve: expected the formation's name, like dissolve square-2");
                return new Command(named, with, verb, none, noOptions, args[0].ToLowerInvariant());
            case "optimize":
                if (args.Count != 1 || args[0].ToLowerInvariant() is not ("on-the-way" or "door-by-door")) throw new CommandSyntaxException("optimize: expected the strategy, on-the-way or door-by-door");
                return new Command(named, with, verb, none, noOptions, args[0].ToLowerInvariant());
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
            case "formations":
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
