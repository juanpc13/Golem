using System.ComponentModel;
using System.Globalization;
using System.Text.Json;
using WardenCli.Formations;

namespace WardenCli;

/// <summary>Who holds a place of a formation, as the golem's `formations` read says it: the name, the index of the place, where it stands,
/// the ring it lies in (0 the outer or only one, 1 the inner).</summary>
public sealed record HolderView(string Name, int Index, double X, double Y, int Ring);

/// <summary>
/// A FORMATION AS THE GOLEM TELLS IT (propuesta 104, 8-oct-2026; Juan: "la pestaña que vemos de formaciones será más bien una consulta GET del
/// golem para ver a cuáles él está metido"): what the selected golem's `formations` read lists — its name, figure, centre, size, orientation,
/// policy, fleet, places, who holds which, how many stand on theirs, the steps queued, the round. The console DRAWS it and composes the lines
/// that administer it (`rotate`, `dissolve`, `form` again when it is moved, turned or resized on the map); it decides nothing: the places and
/// their holders are the golem's answer.
/// </summary>
public sealed class FormationView : INotifyPropertyChanged
{
    public FormationView(string name, string shape, double centerX, double centerY, double measure, double degrees, string policy, string crew, int division,
                         int places, int placed, int queued, int round, int mine, IReadOnlyList<HolderView> holders, IReadOnlyList<(double X, double Y)> spots, int aligned = 0)
    {
        Name = name; Shape = shape; CenterX = centerX; CenterY = centerY; Measure = measure; Degrees = degrees; Policy = policy; Crew = crew; Division = division;
        Places = places; Placed = placed; Queued = queued; Round = round; Mine = mine; Holders = holders; Spots = spots; Aligned = aligned;
    }

    public string Name { get; }
    /// <summary>square, pentagon, triangle, circle, double ring — the golem's word.</summary>
    public string Shape { get; }
    public double CenterX { get; }
    public double CenterY { get; }
    /// <summary>A polygon's side, a circle's radius, a double ring's outer radius.</summary>
    public double Measure { get; }
    public double Degrees { get; }
    public string Policy { get; }
    /// <summary>The fleet's names as the golem lists them, in rank order, comma-separated.</summary>
    public string Crew { get; }
    /// <summary>Where the inner ring begins in the crew (0: one ring).</summary>
    public int Division { get; }
    public int Places { get; }
    public int Placed { get; }
    public int Queued { get; }
    public int Round { get; }
    /// <summary>How many members said they are lined up for the step underway (propuesta 106): the fleet starts when every mover is.</summary>
    public int Aligned { get; }
    /// <summary>The selected golem's own place in it, −1 before it took it.</summary>
    public int Mine { get; }
    public IReadOnlyList<HolderView> Holders { get; }
    /// <summary>Every place of the figure, in the figure's order, as the golem resolved them.</summary>
    public IReadOnlyList<(double X, double Y)> Spots { get; }

    private bool shown = true;
    /// <summary>Whether the console projects it on the map — the eye in the list; the console's own, remembered by name across the reads.</summary>
    public bool Shown
    {
        get => shown;
        set { if (shown == value) return; shown = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Shown))); }
    }

    private Figure? draft;
    /// <summary>THE DRAFT (9-oct-2026; Juan: "quisiera algo como el botón shot… mientras del lado del CLI lo podemos modificar y, hasta estar
    /// seguros de los nuevos ajustes, se envían al golem seleccionado y éste se encarga de actualizar a todos los involucrados"): the figure the
    /// operator moved, turned or resized on the map, NOT told yet — the map draws it over the golem's, *shot* tells it (`FormLine(Draft)`),
    /// ↶ drops it. Kept by the console by name across the reads.</summary>
    public Figure? Draft
    {
        get => draft;
        set
        {
            if (ReferenceEquals(draft, value)) return;
            draft = value;
            foreach (var n in new[] { nameof(Draft), nameof(HasDraft), nameof(DraftText) }) PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(n));
        }
    }
    public bool HasDraft => draft != null;
    public string DraftText => draft == null ? "" : "  · draft — shot writes it";
    /// <summary>What the console projects and grips: the draft while there is one, else the figure as the golem told it.</summary>
    public Figure? Projected => draft ?? AsFigure();

    public bool IsDoubleRing => Shape == "double ring";
    public IReadOnlyList<string> CrewNames => Crew.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).ToList();
    public int CrewCount => CrewNames.Count;
    public string? HolderOf(int index) => Holders.FirstOrDefault(h => h.Index == index)?.Name;

    // ---- the list's words ----
    private static string N(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);
    public string CenterText => $"{N(CenterX)},{N(CenterY)}";
    public string RoundText => $"  by {Policy} · round {Round} · {Placed}/{CrewCount} placed{(Aligned > 0 ? $" · {Aligned} lined up" : "")}{(Queued > 0 ? $" · {Queued} step(s) queued" : "")}";
    public string HoldersText
    {
        get
        {
            var figure = AsFigure();
            if (figure == null) return Crew;
            string Of(int start, int count) => string.Join(" · ", Enumerable.Range(start, count).Select(i => HolderOf(i) is { } who ? $"{who} {figure.Label(i, Places)}" : $"{figure.Label(i, Places)} free"));
            if (Holders.Count == 0) return $"{Crew} — nobody placed yet (by {Policy})";
            return IsDoubleRing ? $"outer: {Of(0, Division)} — inner: {Of(Division, Places - Division)}" : Of(0, Places);
        }
    }
    public string Describe() => $"{Name} · {Shape} at {CenterText}, {(IsDoubleRing || Shape == "circle" ? "radius" : "side")} {N(Measure)} m, {N(Degrees)}°, by {Policy}: {HoldersText}";

    /// <summary>The geometry the console draws and grips — built from what the golem said: the figure of that kind, centre, size and
    /// orientation; a double ring with its inner radius read from its spots. Null when the shape is unknown to the console.</summary>
    public Figure? AsFigure()
    {
        try
        {
            if (IsDoubleRing)
            {
                var centre = new Spot(CenterX, CenterY);
                int outer = Division > 0 ? Division : Math.Max(1, Places / 2);
                double inner = Spots.Count > outer ? new Spot(Spots[outer].X, Spots[outer].Y).DistanceTo(centre) : Measure / 2;
                return new DoubleRing(centre, Measure, Math.Max(0.1, Math.Round(inner, 3)), outer, Math.Max(1, Places - outer), Degrees);
            }
            return Figure.Named(Shape, new Spot(CenterX, CenterY), Measure).Oriented(Degrees);
        }
        catch (ArgumentException) { return null; }
    }

    /// <summary>The name on the wire: a `--fleet` of the crew, and for a double ring the outer ring's names and `--inner-fleet` the inner's.</summary>
    public string FleetOptions()
    {
        var names = CrewNames;
        if (!IsDoubleRing || Division <= 0) return $"--fleet {string.Join(",", names)}";
        return $"--fleet {string.Join(",", names.Take(Division))} --inner-fleet {string.Join(",", names.Skip(Division))}";
    }

    /// <summary>The `form` line that tells the golem this formation AGAIN with that figure — moved, turned or resized on the map (propuesta 104:
    /// saying it again is making it again); the same name, fleet and policy.</summary>
    public string FormLine(Figure figure)
    {
        string size = figure is DoubleRing rings
            ? $"--radius {N(rings.OuterRadius)} --inner-radius {N(rings.InnerRadius)}"
            : figure is Circle ? $"--radius {N(figure.Measure)}" : $"--side {N(figure.Measure)}";
        string shape = figure is DoubleRing ? "double-ring" : figure.Name;
        return $"form {Name} {shape} --center {figure.Center} {size} --angle {N(figure.Angle)} {FleetOptions()} --by {Policy}";
    }

    public static IReadOnlyList<FormationView> Parse(JsonElement json)
    {
        var list = new List<FormationView>();
        if (JsonWalk.Find(json, "told") is not { ValueKind: JsonValueKind.Array } items) return list;
        foreach (var f in items.EnumerateArray())
        {
            var holders = new List<HolderView>();
            if (f.TryGetProperty("holders", out var hs) && hs.ValueKind == JsonValueKind.Array)
                foreach (var h in hs.EnumerateArray()) holders.Add(new HolderView(Str(h, "who"), (int)Num(h, "place"), Num(h, "x"), Num(h, "y"), (int)Num(h, "ring")));
            var spots = new List<(double X, double Y)>();
            if (f.TryGetProperty("spots", out var ss) && ss.ValueKind == JsonValueKind.Array)
                foreach (var sp in ss.EnumerateArray()) spots.Add((Num(sp, "x"), Num(sp, "y")));
            list.Add(new FormationView(Str(f, "called"), Str(f, "shape"), Num(f, "atX"), Num(f, "atY"), Num(f, "length"), Num(f, "degrees"), Str(f, "policy"), Str(f, "crew"),
                                       (int)Num(f, "division"), (int)Num(f, "places"), (int)Num(f, "placed"), (int)Num(f, "queued"), (int)Num(f, "round"), (int)Num(f, "mine"), holders, spots, (int)Num(f, "aligned")));
        }
        return list;
    }

    private static double Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";

    public event PropertyChangedEventHandler? PropertyChanged;
}
