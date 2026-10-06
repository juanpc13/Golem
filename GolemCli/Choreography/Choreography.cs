namespace GolemCli.Choreography;

/// <summary>THE CHOREOGRAPHY PRECALCULATED BY THE CONSOLE (propuesta 95, 6-oct-2026; Juan: "operaciones que vamos a pedirle al CLI para que
/// nos ayude a generar los scripts completos para todos los golems en la operación para poder crear dicha coreografía precalculada… podemos
/// empezar con el cuadrado"): a figure, the fleet, how the places are shared, and how many steps around in which sense. It WRITES THE SCRIPT
/// OF EVERY GOLEM — plain lines the golem already understands (`visit x,y`) — and, between the rounds, the console's own directive
/// <c>@sync</c>: a barrier the console keeps, never sent, where every queue waits for the others before the next place. So the fleet moves in
/// rounds, like the golem's own module does with its words, but decided here, visible and editable before anything goes. Apart from the
/// golem's module (`choreograph`, `rotate`), which stays.</summary>
public sealed class Choreography
{
    public const string Sync = "@sync";

    public Choreography(Figure figure, Fleet fleet, Assignment assignment, int steps, bool clockwise)
    {
        if (steps < 0 || steps > 200) throw new ArgumentException("the steps are 0 to 200");
        Figure = figure ?? throw new ArgumentNullException(nameof(figure));
        Fleet = fleet ?? throw new ArgumentNullException(nameof(fleet));
        Assignment = assignment ?? throw new ArgumentNullException(nameof(assignment));
        Steps = steps;
        Clockwise = clockwise;
    }

    public Figure Figure { get; }
    public Fleet Fleet { get; }
    public Assignment Assignment { get; }
    public int Steps { get; }
    public bool Clockwise { get; }

    /// <summary>The places of the figure for this fleet, in the figure's order.</summary>
    public IReadOnlyList<Spot> Places => Figure.Places(Fleet.Count);

    /// <summary>Who takes which place at round 0: the assignment's answer.</summary>
    public IReadOnlyDictionary<string, int> Shared => Assignment.Share(Fleet, Places);

    /// <summary>The place of a member at a round: its place at round 0 moved along the figure's order one place per step — down the order
    /// clockwise (the places run counter-clockwise), up it otherwise; the hole of a free place moves with the fleet.</summary>
    public Spot PlaceAt(string member, int round)
    {
        var places = Places;
        int index = Shared[member];
        int shift = Clockwise ? -round : round;
        return places[((index + shift) % places.Count + places.Count) % places.Count];
    }

    /// <summary>THE SCRIPTS: for every member, its lines. Two PACES (Juan, 6-oct-2026: "con visit de todos los puntos calculados cubre parte de
    /// completar una coreografía"): ONE ERRAND — a single <c>visit</c> through every place of its way, the golem walks them in order at its
    /// own pace, one command and one ack per golem — or ROUNDS — a <c>visit</c> per place with a <c>@sync</c> between, every queue waiting for
    /// the rest before the next place, so the fleet moves step by step together.</summary>
    public IReadOnlyDictionary<string, IReadOnlyList<string>> Scripts(Pace pace = Pace.OneErrand)
    {
        var scripts = new Dictionary<string, IReadOnlyList<string>>(StringComparer.Ordinal);
        foreach (var member in Fleet.Members)
        {
            var lines = new List<string> { $"# {Describe()}, {(pace == Pace.OneErrand ? "one errand" : "in rounds")} — {member.Name}'s part, laid out by the console" };
            var way = Enumerable.Range(0, Steps + 1).Select(round => PlaceAt(member.Name, round)).ToList();
            if (pace == Pace.OneErrand)
                lines.Add("visit " + string.Join(" ", way));
            else
                for (int round = 0; round < way.Count; round++)
                {
                    if (round > 0) lines.Add(Sync);
                    lines.Add($"visit {way[round]}");
                }
            scripts[member.Name] = lines;
        }
        return scripts;
    }

    public string Describe() =>
        $"{Figure.Name} at {Figure.Center}, by {Assignment.Name}, {Steps} step(s) {(Clockwise ? "clockwise" : "counter-clockwise")}, {Fleet.Count} golem(s)";
}

/// <summary>How the fleet paces a choreography: one errand per golem through all its places, or rounds with a barrier between places.</summary>
public enum Pace
{
    OneErrand,
    Rounds,
}
