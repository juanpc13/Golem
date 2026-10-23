using System.Globalization;
using System.Text.RegularExpressions;
using WardenCli.Formations;

namespace WardenCli;

/// <summary>
/// THE FORMATIONS TO SEND (13-oct-2026; Juan: "¿habría la posibilidad de ver algo que aún no ha sido creado en el golem? quisiera poder pedirle
/// más cosas de la formación circle-1 para poder ponerle justo después del take algún rotate… construir todo el script de todos para armar la
/// coreografía completa"): a `form <name> …` line on a tab, not sent yet, is a formation the console already knows in full — the line is its
/// own syntax and the figures' geometry is its own (<see cref="Figure"/>) — so the formations tab lists it beside the golems' reads, the map
/// projects it, ↻ ↺ write its steps after its take, the grips draft it and the shot rewrites its line. The places are resolved BY RANK as the
/// golem would (the names sorted take the places in order); by distance nothing is anticipated: the holders come with the take.
/// </summary>
public static class PendingFormations
{
    private static readonly Regex FormLine = new(@"^\s*form\s+(?<name>[A-Za-z_][A-Za-z0-9_-]*)\s+(?<figure>square|pentagon|triangle|circle|propeller)\b(?<opts>.*)$",
                                                 RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Option = new(@"--(?<key>[a-z-]+)\s+(?<value>(?!--)\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);   // a flag (--open, --on-cue) takes no value: the next option is not its value
    private static readonly Regex UnionLine = new(@"^\s*union\s+(?<name>[A-Za-z_][A-Za-z0-9_-]*)\s+(?<bill>[A-Za-z_][A-Za-z0-9_,-]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>THE PROGRAMMES ON A TAB (propuesta 123): `union <name> <f1,f2,…>` lines — which formation belongs to which union.</summary>
    public static IReadOnlyDictionary<string, string> UnionsOf(string script)
    {
        var bill = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var raw in (script ?? "").Split('\n'))
        {
            string line = raw.TrimEnd('\r');
            var m = UnionLine.Match(line);
            if (m.Success) { foreach (var f in m.Groups["bill"].Value.ToLowerInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries)) bill[f] = m.Groups["name"].Value.ToLowerInvariant(); continue; }
            var p = ProgramLine.Match(line);
            if (p.Success) foreach (var f in BillOf(line)) bill[f] = p.Groups["name"].Value.ToLowerInvariant();   // propuesta 124
        }
        return bill;
    }

    private static readonly Regex ByOption = new(@"--by\s+(?<by>\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex FleetOption = new(@"--fleet\s+(?<fleet>[^\s-][^\s]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex LinkOption = new(@"--link\s+(?<link>\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex ProgramLine = new(@"^\s*program\s+(?<name>[A-Za-z_][A-Za-z0-9_-]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);   // propuesta 124: the whole union in one line
    private static readonly Regex FormClause = new(@"--form\s+(?<formation>[A-Za-z_][A-Za-z0-9_-]*)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>The formations a `program` line names (its --form clauses), in order, lower case.</summary>
    public static IReadOnlyList<string> BillOf(string programLine) => FormClause.Matches(programLine ?? "").Select(m => m.Groups["formation"].Value.ToLowerInvariant()).ToList();

    /// <summary>THE UNIONS TO SEND (22-oct-2026): every `union <name> <bill> [--fleet a,b] [--by p]` line on the tabs and not sent, one view per name
    /// (the first tab that names it), with the tabs it is on — listed in the unions tab like a pending formation; nothing is in any golem yet.</summary>
    public static IReadOnlyList<UnionView> Unions(IEnumerable<Golem> golems)
    {
        var views = new List<UnionView>();
        var onTabs = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        var all = golems.ToList();
        string everybody = string.Join(",", all.Select(g => g.Name).OrderBy(n => n, StringComparer.Ordinal));
        foreach (var golem in all)
            foreach (var raw in (golem.Script ?? "").Split('\n'))
            {
                var line = raw.TrimEnd('\r');
                var m = UnionLine.Match(line);
                var program = m.Success ? null : ProgramLine.Match(line);
                if (!m.Success && !(program?.Success ?? false)) continue;
                string name = (m.Success ? m.Groups["name"].Value : program!.Groups["name"].Value).ToLowerInvariant();
                var named = m.Success ? m.Groups["bill"].Value.ToLowerInvariant().Split(',', StringSplitOptions.RemoveEmptyEntries) : BillOf(line).ToArray();
                if (!onTabs.TryGetValue(name, out var tabs)) onTabs[name] = tabs = new List<string>();
                if (!tabs.Contains(golem.Name)) tabs.Add(golem.Name);
                if (views.Any(v => v.Name == name)) continue;
                var by = ByOption.Match(line);
                var fleet = FleetOption.Match(line);
                var link = LinkOption.Match(line);
                views.Add(new UnionView(name, "", named,
                                        fleet.Success ? fleet.Groups["fleet"].Value.ToLowerInvariant() : everybody,
                                        by.Success ? by.Groups["by"].Value.ToLowerInvariant() : "distance",
                                        0, false, false, false, false, "", false, "", "", "", Array.Empty<ShareView>(),
                                        link.Success ? link.Groups["link"].Value.ToLowerInvariant() : "cue") { IsPending = true });
            }
        return views.Select(v => v with { PendingOn = string.Join(", ", onTabs[v.Name]) }).ToList();
    }

    /// <summary>Every formation told on the golems' tabs and not sent, one view per name (the first tab that names it), with the tabs it is on.</summary>
    public static IReadOnlyList<FormationView> Parse(IEnumerable<Golem> golems)
    {
        var all = golems.ToList();
        var everybody = all.Select(g => g.Name.ToLowerInvariant()).ToList();
        var views = new List<FormationView>();
        var onTabs = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var golem in all)
        {
            var unions = UnionsOf(golem.Script);
            foreach (var raw in (golem.Script ?? "").Split('\n'))
            {
                var m = FormLine.Match(raw.TrimEnd('\r'));
                if (!m.Success) continue;
                string name = m.Groups["name"].Value.ToLowerInvariant();
                if (!onTabs.TryGetValue(name, out var tabs)) onTabs[name] = tabs = new List<string>();
                if (!tabs.Contains(golem.Name)) tabs.Add(golem.Name);
                if (views.Any(v => v.Name == name)) continue;
                var view = FromLine(name, m.Groups["figure"].Value.ToLowerInvariant(), m.Groups["opts"].Value, golem.Name, everybody, unions.TryGetValue(name, out var show) ? show : "");
                if (view != null) views.Add(view);
            }
        }
        foreach (var v in views) { v.IsPending = true; v.Owner = "pending"; v.PendingOn = string.Join(", ", onTabs[v.Name]); }
        return views;
    }

    // which orbit of the figure the place of that index lies in: 0 for every figure but the propeller (its centre, then every rung)
    private static int OrbitOf(Figure shape, int index, int count)
    {
        var orbits = shape.Orbits(count);
        for (int o = 0; o < orbits.Count; o++)
            if (index >= orbits[o].Start && index < orbits[o].Start + orbits[o].Count) return o;
        return 0;
    }

    /// <summary>The view of one `form` line: figure, centre, measure, orientation, fleet and policy as the line says; the places by the
    /// console's geometry; the holders by rank. Null when the line lacks what a figure needs.</summary>
    public static FormationView? FromLine(string name, string figure, string options, string teller, IReadOnlyList<string> everybody, string union = "")
    {
        var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match o in Option.Matches(options)) opts[o.Groups["key"].Value] = o.Groups["value"].Value;
        bool onCue = Regex.IsMatch(options, @"(^|\s)--on-cue(\s|$)", RegexOptions.IgnoreCase);   // a flag without a value (propuesta 117)
        bool open = Regex.IsMatch(options, @"(^|\s)--open(\s|$)", RegexOptions.IgnoreCase);       // a flag: one of a union, the golems choose (propuesta 123)
        double? Num(string key) => opts.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        List<string> Names(string key) => opts.TryGetValue(key, out var v) ? v.Split(',').Select(n => n.Trim().ToLowerInvariant()).Where(n => n != "").Distinct().ToList() : new List<string>();
        if (!opts.TryGetValue("center", out var centre)) return null;
        var xy = centre.Split(',');
        if (xy.Length != 2 || !double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double cx) || !double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double cy)) return null;
        bool propeller = figure == "propeller";
        bool round = propeller || figure == "circle";
        double? measure = round ? Num("radius") : Num("side");
        if (measure is not > 0) return null;
        double angle = Num("angle") ?? 0.0;
        Policy policy = opts.TryGetValue("by", out var by) ? Policies.Parse(by) ?? Policy.Rank : Policy.Rank;
        // the fleet as the golem resolves it: the names sorted (rank); left out, this golem and every peer — here, every golem of the workspace
        var outer = Names("fleet");
        if (outer.Count == 0) outer = everybody.ToList();
        outer.Sort(StringComparer.Ordinal);
        var crew = outer.ToList();
        int places = crew.Count;
        int openPlaces = open && Num("places") is { } told && told >= 1 ? (int)told : 0;   // an open formation told for how many (22-oct-2026)
        if (openPlaces > 0) places = openPlaces;
        if (places == 0) return null;
        Figure shape;
        try
        {
            shape = propeller
                ? new Propeller(new Spot(cx, cy), measure.Value, Num("inner-radius") ?? measure.Value / 3, (int)(Num("blades") ?? 3), angle)
                : Figure.Named(figure, new Spot(cx, cy), measure.Value).Oriented(angle);
        }
        catch (ArgumentException) { return null; }
        var spots = shape.Places(places).Select(s => (s.X, s.Y)).ToList();
        var holders = new List<HolderView>();
        if (policy == Policy.Rank)
            for (int i = 0; i < Math.Min(places, spots.Count); i++)
                holders.Add(new HolderView(crew[i], i, spots[i].X, spots[i].Y, OrbitOf(shape, i, spots.Count)));
        return new FormationView(name, figure, cx, cy, measure.Value, angle, policy, string.Join(",", crew),
                                 spots.Count, 0, 0, 0, -1, holders, spots,
                                 inner: shape is Propeller p ? p.Hub : 0, arms: shape is Propeller q ? q.Blades : 0,
                                 cueTo: opts.TryGetValue("cue", out var cue) ? cue.Trim().ToLowerInvariant() : "", onCue: onCue || opts.ContainsKey("after"), cued: !onCue && !opts.ContainsKey("after"),
                                 after: opts.TryGetValue("after", out var after) ? after.Trim().ToLowerInvariant() : "",
                                 open: open, union: union, resolved: false, member: true, openPlaces: openPlaces,
                                 every: opts.TryGetValue("every", out var every) && double.TryParse(every.TrimEnd('s', 'S'), NumberStyles.Float, CultureInfo.InvariantCulture, out double period) && period > 0 ? period : 0);
    }
}
