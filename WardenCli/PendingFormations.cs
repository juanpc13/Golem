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
    private static readonly Regex FormLine = new(@"^\s*form\s+(?<name>[A-Za-z_][A-Za-z0-9_-]*)\s+(?<figure>square|pentagon|triangle|circle|double-ring|propeller)\b(?<opts>.*)$",
                                                 RegexOptions.IgnoreCase | RegexOptions.Compiled);
    private static readonly Regex Option = new(@"--(?<key>[a-z-]+)\s+(?<value>\S+)", RegexOptions.IgnoreCase | RegexOptions.Compiled);

    /// <summary>Every formation told on the golems' tabs and not sent, one view per name (the first tab that names it), with the tabs it is on.</summary>
    public static IReadOnlyList<FormationView> Parse(IEnumerable<Golem> golems)
    {
        var all = golems.ToList();
        var everybody = all.Select(g => g.Name.ToLowerInvariant()).ToList();
        var views = new List<FormationView>();
        var onTabs = new Dictionary<string, List<string>>(StringComparer.Ordinal);
        foreach (var golem in all)
        {
            foreach (var raw in (golem.Script ?? "").Split('\n'))
            {
                var m = FormLine.Match(raw.TrimEnd('\r'));
                if (!m.Success) continue;
                string name = m.Groups["name"].Value.ToLowerInvariant();
                if (!onTabs.TryGetValue(name, out var tabs)) onTabs[name] = tabs = new List<string>();
                if (!tabs.Contains(golem.Name)) tabs.Add(golem.Name);
                if (views.Any(v => v.Name == name)) continue;
                var view = FromLine(name, m.Groups["figure"].Value.ToLowerInvariant(), m.Groups["opts"].Value, golem.Name, everybody);
                if (view != null) views.Add(view);
            }
        }
        foreach (var v in views) { v.IsPending = true; v.Owner = "pending"; v.PendingOn = string.Join(", ", onTabs[v.Name]); }
        return views;
    }

    /// <summary>The view of one `form` line: figure, centre, measure, orientation, fleet and policy as the line says; the places by the
    /// console's geometry; the holders by rank. Null when the line lacks what a figure needs.</summary>
    public static FormationView? FromLine(string name, string figure, string options, string teller, IReadOnlyList<string> everybody)
    {
        var opts = new Dictionary<string, string>(StringComparer.OrdinalIgnoreCase);
        foreach (Match o in Option.Matches(options)) opts[o.Groups["key"].Value] = o.Groups["value"].Value;
        double? Num(string key) => opts.TryGetValue(key, out var v) && double.TryParse(v, NumberStyles.Float, CultureInfo.InvariantCulture, out var d) ? d : null;
        List<string> Names(string key) => opts.TryGetValue(key, out var v) ? v.Split(',').Select(n => n.Trim().ToLowerInvariant()).Where(n => n != "").Distinct().ToList() : new List<string>();
        if (!opts.TryGetValue("center", out var centre)) return null;
        var xy = centre.Split(',');
        if (xy.Length != 2 || !double.TryParse(xy[0], NumberStyles.Float, CultureInfo.InvariantCulture, out double cx) || !double.TryParse(xy[1], NumberStyles.Float, CultureInfo.InvariantCulture, out double cy)) return null;
        bool rings = figure == "double-ring";
        bool propeller = figure == "propeller";
        bool round = rings || propeller || figure == "circle";
        double? measure = round ? Num("radius") : Num("side");
        if (measure is not > 0) return null;
        double angle = Num("angle") ?? 0.0;
        string policy = opts.TryGetValue("by", out var by) ? by.ToLowerInvariant() : "rank";
        // the fleet as the golem resolves it: the names sorted (rank); left out, this golem and every peer — here, every golem of the workspace
        var outer = Names("fleet");
        if (outer.Count == 0) outer = everybody.ToList();
        outer.Sort(StringComparer.Ordinal);
        var inner = rings ? Names("inner-fleet") : new List<string>();
        inner.Sort(StringComparer.Ordinal);
        var crew = outer.Concat(inner).ToList();
        int places = crew.Count;
        if (places == 0) return null;
        Figure shape;
        try
        {
            shape = rings
                ? new DoubleRing(new Spot(cx, cy), measure.Value, Num("inner-radius") ?? measure.Value / 2, outer.Count, Math.Max(1, inner.Count), angle)
                : propeller
                ? new Propeller(new Spot(cx, cy), measure.Value, Num("inner-radius") ?? measure.Value / 3, (int)(Num("blades") ?? 3), angle)
                : Figure.Named(figure, new Spot(cx, cy), measure.Value).Oriented(angle);
        }
        catch (ArgumentException) { return null; }
        var spots = shape.Places(places).Select(s => (s.X, s.Y)).ToList();
        var holders = new List<HolderView>();
        if (policy == "rank")
            for (int i = 0; i < Math.Min(places, spots.Count); i++)
                holders.Add(new HolderView(crew[i], i, spots[i].X, spots[i].Y, rings && i >= outer.Count ? 1 : 0));
        return new FormationView(name, rings ? "double ring" : figure, cx, cy, measure.Value, angle, policy, string.Join(",", crew), rings ? outer.Count : 0,
                                 spots.Count, 0, 0, 0, -1, holders, spots,
                                 inner: shape is Propeller p ? p.Hub : shape is DoubleRing d ? d.InnerRadius : 0, arms: shape is Propeller q ? q.Blades : 0);
    }
}
