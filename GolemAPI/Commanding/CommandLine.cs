using System.Globalization;
using System.Text.RegularExpressions;

namespace GolemAPI.Commanding;

/// <summary>A line the operator wrote, read: the golem it is for (`golem red …`; "" when it names none — the console's own), the peers
/// the same command is carried to (`--with blue,green`, `--with all`), the command, the points it names, its options (`--all`), and the
/// text it carries (a help topic). The points are what an errand is made of — points, never places (Juan, 16-sep-2026).</summary>
public sealed record Command(string Golem, IReadOnlyList<string> With, string Verb, IReadOnlyList<(double X, double Y)> Points, IReadOnlyList<string> Options, string Text);

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
        new CommandHelp("dash", "dash x,y [x,y …]", "an errand through those points, in that order, the doors crossed on the way — fewer stops (ajuste 58)", "dash 9,1.5"),
        new CommandHelp("then", "then x,y", "one more stop, told to the newest route while it is pending", "then 5.5,5.5"),
        new CommandHelp("pause", "pause", "hold the golem where its body stands", "pause"),
        new CommandHelp("resume", "resume", "let it go on", "resume"),
        new CommandHelp("forget", "forget x,y", "what stood there is gone: the golem forgets it, marks and all, and tells the peers", "forget 10.13,5.5"),
        new CommandHelp("reset", "reset [--all]", "let go: every pending route abandoned, the body back on its mark; --all wipes the journal — the peers' too — and reboots reborn", "reset"),
        new CommandHelp("state", "state", "the board: the routes pending, the one underway and its next point, how the last one ended", "state"),
        new CommandHelp("route", "route", "the newest route: its way, the legs ahead, what it asks the body now", "route"),
        new CommandHelp("where", "where", "where the golem knows its body stands, facing which way, in which zone", "where"),
        new CommandHelp("obstacles", "obstacles", "what the bodies learned by touching: the things outlined, the peers met", "obstacles"),
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
            case "dash":
                return new Command(named, with, verb, Points(verb, args, atLeast: 1), noOptions, "");
            case "cover":
                return new Command(named, with, verb, Points(verb, args, atLeast: 2), noOptions, "");
            case "then":
            case "forget":
                return new Command(named, with, verb, Points(verb, args, exactly: 1), noOptions, "");
            case "pause":
            case "resume":
            case "state":
            case "route":
            case "where":
            case "obstacles":
                if (args.Count > 0) throw new CommandSyntaxException($"{verb} takes nothing more; found '{Head(string.Join(' ', args))}'");
                return new Command(named, with, verb, none, noOptions, "");
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
