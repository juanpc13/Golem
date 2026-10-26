namespace GolemDomain.Plays;

/// <summary>Where a car goes, in a driver's words (propuesta 125): straight on, left or right at the crossing. The exit arm is the
/// crossing's business (<see cref="Crossing.Exit"/>). An enum of the domain, like <see cref="Direction"/>: it travels as its member's name.</summary>
internal enum Turn
{
    Straight,
    Left,
    Right,
}

/// <summary>THE JOURNEYS of a traffic play (propuesta 125; Juan: "los carros saben su trayecto"): every car's turn at the crossing, told by the
/// warden on the play line and written in the journal as text — <c>red:straight,green:left,yellow:right</c> — the way the cast's names are.
/// Read, never computed: a car knows its journey from the moment the play is staged.</summary>
internal sealed class Journeys
{
    private readonly Dictionary<string, Turn> turns = new();
    private readonly Dictionary<string, Direction> arms = new();   // the arm a car comes from, when the journey says it (§12: the queue)

    internal Journeys(string text)
    {
        if (text == null) throw new GolemDomainException("Journeys.Journeys: 'text' was not given");
        foreach (var part in text.Split(',').Select(p => p.Trim()).Where(p => p != ""))
        {
            var halves = part.Split(':');
            if (halves.Length is not (2 or 3) || halves[0].Trim() == "") throw new GolemDomainException($"a journey is a car and its turn — red:straight — or a car, the arm it comes from and its turn — red:south:straight — not '{part}'");
            string car = halves[0].Trim().ToLowerInvariant();
            string turnWord = halves[^1].Trim();
            if (!Enum.TryParse<Turn>(turnWord, ignoreCase: true, out var turn)) throw new GolemDomainException($"'{car}' goes straight, left or right, not '{turnWord}'");
            if (turns.ContainsKey(car)) throw new GolemDomainException($"'{car}' has two journeys");
            if (halves.Length == 3)
            {
                if (!Enum.TryParse<Direction>(halves[1].Trim(), ignoreCase: true, out var arm)) throw new GolemDomainException($"'{car}' comes from the north, south, east or west arm, not '{halves[1].Trim()}'");
                arms[car] = arm;
            }
            turns[car] = turn;
        }
        if (turns.Count == 0) throw new GolemDomainException("a traffic play needs at least one journey, like red:straight");
        if (arms.Count != 0 && arms.Count != turns.Count) throw new GolemDomainException("every journey names the arm its car comes from, or none does");
    }

    /// <summary>Whether the journeys say where every car comes from (§12): then the queues of the arms are known to every copy from the start.</summary>
    internal bool KnowArms => arms.Count == turns.Count;

    /// <summary>The arm a car comes from, as its journey says it; refused when the journeys say no arms.</summary>
    internal Direction ArmOf(string car)
    {
        if (!Knows(car)) throw new GolemDomainException($"'{car}' has no journey: the cars are {Roster}");
        if (!KnowArms) throw new GolemDomainException("the journeys say no arms: a car's arm is where it stands");
        return arms[car.Trim().ToLowerInvariant()];
    }

    /// <summary>The cars coming from one arm, sorted by name: the order of that arm's queue (§12) — deterministic, every copy alike.</summary>
    internal IReadOnlyList<string> OnArm(Direction arm) => KnowArms ? Cars.Where(c => arms[c] == arm).ToList() : Array.Empty<string>();

    internal int Count => turns.Count;
    internal IReadOnlyList<string> Cars => turns.Keys.OrderBy(n => n, StringComparer.Ordinal).ToList();
    internal string Roster => string.Join(",", Cars);
    /// <summary>The journeys as they were told, the cars sorted: what the read prints.</summary>
    internal string Text => string.Join(",", Cars.Select(c => KnowArms ? $"{c}:{arms[c].ToString().ToLowerInvariant()}:{turns[c].ToString().ToLowerInvariant()}" : $"{c}:{turns[c].ToString().ToLowerInvariant()}"));

    internal bool Knows(string car) => !string.IsNullOrWhiteSpace(car) && turns.ContainsKey(car.Trim().ToLowerInvariant());

    internal Turn Of(string car)
    {
        if (!Knows(car)) throw new GolemDomainException($"'{car}' has no journey: the cars are {Roster}");
        return turns[car.Trim().ToLowerInvariant()];
    }
}
