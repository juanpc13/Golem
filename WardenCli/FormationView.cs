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
    public FormationView(string name, string shape, double centerX, double centerY, double measure, double degrees, Policy policy, string crew,
                         int places, int placed, int queued, int round, int mine, IReadOnlyList<HolderView> holders, IReadOnlyList<(double X, double Y)> spots, int aligned = 0,
                         bool stranded = false, string strandedWhy = "", double inner = 0, int arms = 0,
                         string cueTo = "", bool onCue = false, bool cued = true, int finished = 0, bool done = false, bool cueGiven = false,
                         string after = "", string askTo = "", int asked = 0, int notYet = 0, double every = 0, bool committed = false)
    {
        Inner = inner; Arms = arms;
        CueTo = cueTo ?? ""; OnCue = onCue; Cued = cued; Finished = finished; Done = done; CueGiven = cueGiven;
        After = after ?? ""; AskTo = askTo ?? ""; Asked = asked; NotYet = notYet; Every = every; Committed = committed;
        Name = name; Shape = shape; CenterX = centerX; CenterY = centerY; Measure = measure; Degrees = degrees; Policy = policy; Crew = crew;
        Places = places; Placed = placed; Queued = queued; Round = round; Mine = mine; Holders = holders; Spots = spots; Aligned = aligned;
        Stranded = stranded; StrandedWhy = strandedWhy;
    }

    /// <summary>The figure's inner measure as the golem says it (propuesta 114): a propeller's hub; 0 otherwise.</summary>
    public double Inner { get; }
    /// <summary>A propeller's blades as the golem says them; 0 otherwise.</summary>
    public int Arms { get; }

    /// <summary>THE CUE BETWEEN DANCERS (propuesta 117): whom the last of this fleet to finish cues ("" nobody); whether the formation starts on
    /// a cue and whether the cue came; how many members finished; whether every one did; whether the cue was given.</summary>
    public string CueTo { get; }
    public bool OnCue { get; }
    public bool Cued { get; }
    public int Finished { get; }
    public bool Done { get; }
    public bool CueGiven { get; }
    /// <summary>THE PULL (propuesta 118): the formation this one starts after and the golem asked ("" none), how many questions, how many "not yet".</summary>
    public string After { get; }
    public string AskTo { get; }
    public int Asked { get; }
    public int NotYet { get; }
    /// <summary>THE COMMITMENT (propuesta 121): how often the asker asks (0 when the golem does not say) and whether this golem's copy holds the
    /// inquiry open — it is the asker, the answer not come yet.</summary>
    public double Every { get; }
    public bool Committed { get; }
    public string SequenceText =>
        (After != "" ? $" · after {After}, asks {AskTo}{(Every > 0 ? $" every {N(Every)} s" : "")}{(Committed ? " (committed)" : "")}{(Asked > 0 ? $" ({Asked} asked, {NotYet} not yet)" : "")}" : "")
        + (OnCue ? (Cued ? " · cued" : After != "" ? " — waiting" : " · on cue — waiting") : "")
        + (CueTo != "" ? $" · cue → {CueTo}{(CueGiven ? " (given)" : "")}" : "")
        + (Finished > 0 ? $" · {Finished}/{CrewCount} finished{(Done ? " — done" : "")}" : "");

    /// <summary>This golem could not reach its place twice (propuesta 113): the formation waits for the warden — take again, move, dissolve.</summary>
    public bool Stranded { get; }
    public string StrandedWhy { get; }
    public string StrandedText => Stranded ? $"✗ stranded — {StrandedWhy} — take it again, move it or dissolve it" : "";

    public string Name { get; }
    /// <summary>WHOSE READ this is (12-oct-2026; Juan: "me gustaría poder ver las de los otros también, no solo las del golem seleccionado"): the golem
    /// that answered it, and whether it is another golem's than the one selected — then its row says so, and ↻ ↺ ✕ act on that golem.</summary>
    public string Owner { get; set; } = "";
    public bool IsOthers { get; set; }
    /// <summary>A formation TO SEND (13-oct-2026): told on the tabs — `form … / take …` not sent yet — and listed from those lines, with the tabs it is on.</summary>
    public bool IsPending { get; set; }
    public string PendingOn { get; set; } = "";
    public string OwnerText => IsPending ? $"  to send · on {PendingOn}" : IsOthers ? $"  {Owner}'s" : "";
    /// <summary>square, pentagon, triangle, circle, propeller — the golem's word.</summary>
    public string Shape { get; }
    public double CenterX { get; }
    public double CenterY { get; }
    /// <summary>A polygon's side, a circle's radius, a propeller's tips.</summary>
    public double Measure { get; }
    public double Degrees { get; }
    public Policy Policy { get; }
    /// <summary>The policy as a line says it: rank, distance, rps.</summary>
    public string PolicyWord => Policies.Word(Policy);
    /// <summary>The fleet's names as the golem lists them, in rank order, comma-separated.</summary>
    public string Crew { get; }
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

    public bool IsPropeller => Shape == "propeller";
    public IReadOnlyList<string> CrewNames => Crew.Split(',', StringSplitOptions.RemoveEmptyEntries).Select(n => n.Trim()).ToList();
    public int CrewCount => CrewNames.Count;
    public string? HolderOf(int index) => Holders.FirstOrDefault(h => h.Index == index)?.Name;

    // ---- the list's words ----
    private static string N(double v) => Math.Round(v, 3).ToString("0.###", CultureInfo.InvariantCulture);
    public string CenterText => $"{N(CenterX)},{N(CenterY)}";
    public string RoundText => $"  by {PolicyWord} · round {Round} · {Placed}/{CrewCount} placed{(Aligned > 0 ? $" · {Aligned} lined up" : "")}{(Queued > 0 ? $" · {Queued} step(s) queued" : "")}{SequenceText}";
    public string HoldersText
    {
        get
        {
            var figure = AsFigure();
            if (figure == null) return Crew;
            string Of(int start, int count) => string.Join(" · ", Enumerable.Range(start, count).Select(i => HolderOf(i) is { } who ? $"{who} {figure.Label(i, Places)}" : $"{figure.Label(i, Places)} free"));
            if (Holders.Count == 0) return $"{Crew} — nobody placed yet (by {PolicyWord})";
            return Of(0, Places);
        }
    }
    public string Describe() => $"{Name} · {Shape} at {CenterText}, {(IsPropeller || Shape == "circle" ? "radius" : "side")} {N(Measure)} m, {N(Degrees)}°, by {PolicyWord}: {HoldersText}";

    /// <summary>The geometry the console draws and grips — built from what the golem said: the figure of that kind, centre, size and
    /// orientation; a propeller with its hub and its blades. Null when the shape is unknown to the console.</summary>
    public Figure? AsFigure()
    {
        try
        {
            if (IsPropeller)
                return new Propeller(new Spot(CenterX, CenterY), Measure, Inner > 0 && Inner < Measure ? Inner : Measure / 3, Arms >= 2 ? Arms : 3, Degrees);
            return Figure.Named(Shape, new Spot(CenterX, CenterY), Measure).Oriented(Degrees);
        }
        catch (ArgumentException) { return null; }
    }

    /// <summary>The fleet on the wire: a `--fleet` of the crew.</summary>
    public string FleetOptions() => $"--fleet {string.Join(",", CrewNames)}";

    /// <summary>The `form` line that tells the golem this formation AGAIN with that figure — moved, turned or resized on the map (propuesta 104:
    /// saying it again is making it again); the same name, fleet and policy.</summary>
    public string FormLine(Figure figure)
    {
        string size = figure is Propeller propeller ? $"--radius {N(propeller.Radius)} --inner-radius {N(propeller.Hub)} --blades {propeller.Blades}"
            : figure is Circle ? $"--radius {N(figure.Measure)}" : $"--side {N(figure.Measure)}";
        string shape = figure.Name;
        return $"form {Name} {shape} --center {figure.Center} {size} --angle {N(figure.Angle)} {FleetOptions()} --by {PolicyWord}{(CueTo != "" ? $" --cue {CueTo}" : "")}{(After != "" ? $" --after {After} --ask {AskTo}{(Every > 0 ? $" --every {N(Every)}s" : "")}" : OnCue ? " --on-cue" : "")}";
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
            list.Add(new FormationView(Str(f, "called"), Str(f, "shape"), Num(f, "atX"), Num(f, "atY"), Num(f, "length"), Num(f, "degrees"), Policies.Parse(Str(f, "policy")) ?? Policy.Rank, Str(f, "crew"),
                                       (int)Num(f, "places"), (int)Num(f, "placed"), (int)Num(f, "queued"), (int)Num(f, "round"), (int)Num(f, "mine"), holders, spots, (int)Num(f, "aligned"),
                                       f.TryGetProperty("stranded", out var st) && st.ValueKind == JsonValueKind.True, Str(f, "strandedWhy"), Num(f, "inner"), (int)Num(f, "arms"),
                                       Str(f, "cueTo"), Flag(f, "onCue"), !f.TryGetProperty("cued", out var cu) || cu.ValueKind != JsonValueKind.False, (int)Num(f, "finished"), Flag(f, "done"), Flag(f, "cueGiven"),
                                       Str(f, "after"), Str(f, "askTo"), (int)Num(f, "asked"), (int)Num(f, "notYet"), Num(f, "every"), Flag(f, "committed")));
        }
        return list;
    }

    private static bool Flag(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static double Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";

    public event PropertyChangedEventHandler? PropertyChanged;
}
