using System.ComponentModel;

namespace WardenCli.Formations;

/// <summary>The SENSE of a turn around a figure — the console's own closed set, the same two words as the golem's module.</summary>
public enum Sense
{
    Clockwise,
    Counterclockwise,
}

/// <summary>Which ring of a double ring (8-oct-2026): the OUTER one — the wider, Juan's "superior" — or the INNER one, his "inferior".</summary>
public enum Ring
{
    Outer,
    Inner,
}

/// <summary>A FORMATION IN FORCE (propuesta 96, 7-oct-2026; Juan: "cuando uno cree una formación se cree una lista de formaciones activas…
/// tenemos que tener un objeto que nos permita guardar el vértice que corresponde a cada golem para controlarlo desde el CLI y así ir
/// seteando los scripts asociados a dicha rotación"): a figure, its fleet, and THE VERTEX EACH MEMBER HOLDS NOW. Born from a choreography
/// laid out — where its steps leave everybody — and kept by the console in its list of active formations; every ROTATION the operator asks
/// moves every member one vertex in the sense (the places run counter-clockwise, so clockwise steps DOWN the order; the hole of a free vertex
/// moves with the fleet) and WRITES the line each golem gets, a <c>visit</c> through the vertices it passes; the vertices held are kept here,
/// so the next rotation starts from them. The console's own object: the golem's module knows nothing of it, and nothing goes until SEND.</summary>
public sealed class Formation : INotifyPropertyChanged
{
    private readonly Dictionary<string, int> held;
    private readonly int[] turned;

    /// <param name="turned">the steps it had turned, and <paramref name="unshot"/> whether its figure had changed since its last shot — what a
    /// formation saved in a workspace is born again with (7-oct-2026); a formation laid out now starts at 0, with nothing to shoot;
    /// <paramref name="innerTurned"/> the inner ring's steps, for a double ring</param>
    public Formation(int number, Figure figure, Fleet fleet, IReadOnlyDictionary<string, int> held, int turned = 0, bool unshot = false, int innerTurned = 0)
    {
        if (number < 1) throw new ArgumentException("a formation is numbered from 1");
        Number = number;
        Figure = figure ?? throw new ArgumentNullException(nameof(figure));
        Fleet = fleet ?? throw new ArgumentNullException(nameof(fleet));
        if (held == null) throw new ArgumentNullException(nameof(held));
        int places = Places.Count;
        this.held = new Dictionary<string, int>(StringComparer.Ordinal);
        foreach (var member in Fleet.Members)
        {
            if (!held.TryGetValue(member.Name, out int index)) throw new ArgumentException($"'{member.Name}' holds no vertex of the {figure.Name}");
            if (index < 0 || index >= places) throw new ArgumentException($"'{member.Name}' holds vertex {index}: the {figure.Name} has {places}");
            var other = this.held.FirstOrDefault(h => h.Value == index).Key;
            if (other != null) throw new ArgumentException($"'{member.Name}' and '{other}' hold the same vertex ({Figure.Label(index, places)})");
            this.held[member.Name] = index;
        }
        this.turned = Figure.Orbits(Fleet.Count).Count > 1 ? new[] { turned, innerTurned } : new[] { turned };
        Unshot = unshot;
    }

    /// <summary>A DOUBLE RING laid out (8-oct-2026; Juan: "seleccionar cuáles serían los golems del anillo inferior y superior"): the outer
    /// ring and the inner one by their diameters, each with as many places as the golems the operator put on it; on each ring the names sorted
    /// take its places in order, from due east.</summary>
    public static Formation DoubleRing(int number, Spot center, double outerDiameter, double innerDiameter, IReadOnlyCollection<string> outer, IReadOnlyCollection<string> inner)
    {
        if (outer == null || outer.Count == 0) throw new ArgumentException("the outer ring needs at least one golem");
        if (inner == null || inner.Count == 0) throw new ArgumentException("the inner ring needs at least one golem");
        var both = outer.Intersect(inner, StringComparer.Ordinal).ToList();
        if (both.Count > 0) throw new ArgumentException($"a golem rides one ring: {string.Join(", ", both)} on both");
        var figure = new DoubleRing(center, outerDiameter / 2, innerDiameter / 2, outer.Count, inner.Count);
        var places = outer.OrderBy(n => n, StringComparer.Ordinal).Select((n, i) => (n, i))
            .Concat(inner.OrderBy(n => n, StringComparer.Ordinal).Select((n, i) => (n, outer.Count + i)))
            .ToDictionary(p => p.n, p => p.Item2, StringComparer.Ordinal);
        return new Formation(number, figure, new Fleet(outer.Concat(inner).Select(n => new Member(n, null))), places);
    }

    /// <summary>Its order of birth in the console's list: "square #2".</summary>
    public int Number { get; }
    public Figure Figure { get; private set; }
    public Fleet Fleet { get; }
    /// <summary>How many steps it has turned since it was laid out, clockwise negative — the places run counter-clockwise; the outer ring's,
    /// for a double ring.</summary>
    public int Turned => turned[0];

    /// <summary>The inner ring's steps, for a double ring; 0 for any other figure.</summary>
    public int InnerTurned => turned.Length > 1 ? turned[1] : 0;

    /// <summary>Whether it is a double ring: its rings turn apart.</summary>
    public bool IsDoubleRing => Figure is DoubleRing;

    /// <summary>The steps turned, in the list's words: "turned 2", or "outer 1 · inner -1" for a double ring.</summary>
    public string TurnedText => IsDoubleRing ? $"  outer {Turned} · inner {InnerTurned}" : $"  turned {Turned}";

    /// <summary>The ring a member rides, for a double ring.</summary>
    public Ring RingOf(string member) =>
        Figure is DoubleRing r ? (r.IsInner(IndexOf(member)) ? Ring.Inner : Ring.Outer) : throw new ArgumentException($"{Name} has no rings");

    private bool shown = true;
    /// <summary>Whether the console PROJECTS its figure on the map — the eye beside it in the list (Juan, 7-oct-2026: "un botón como un ojo
    /// para mostrar ese overlay de la figura en el mapa"); on when it is laid out.</summary>
    public bool Shown
    {
        get => shown;
        set { if (shown == value) return; shown = value; PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Shown))); }
    }

    public string Name => $"{Figure.Name} #{Number}";
    public IReadOnlyList<Spot> Places => Figure.Places(Fleet.Count);

    /// <summary>The index, in the figure's order, of the vertex a member holds.</summary>
    public int IndexOf(string member) =>
        held.TryGetValue(member, out int i) ? i : throw new ArgumentException($"'{member}' is not in {Name} ({string.Join(", ", Fleet.Names)})");

    /// <summary>The vertex a member holds, on the floor.</summary>
    public Spot PlaceOf(string member) => Places[IndexOf(member)];

    /// <summary>Who holds a vertex; null when it is free.</summary>
    public string? HolderOf(int index) => held.FirstOrDefault(h => h.Value == index).Key;

    /// <summary>A ROTATION: every member moves that many places in the sense ROUND ITS OWN ORBIT — one by default — and the line each golem
    /// gets is a <c>visit</c> through the way it takes (<see cref="Figure.Way"/>: the next vertex on a polygon, the arc on a circle); the places
    /// held move with them. A double ring turns both rings in that sense, or the one named alone (8-oct-2026: "que se pueda rotar el superior y
    /// el inferior"); a ring's step leaves the other ring's golems where they are, with no line. Nothing is sent here.</summary>
    public IReadOnlyDictionary<string, string> Rotate(Sense sense, int steps = 1, Ring? ring = null)
    {
        if (steps < 1 || steps > 200) throw new ArgumentException("a rotation is 1 to 200 steps");
        var orbits = Figure.Orbits(Fleet.Count);
        if (ring.HasValue && orbits.Count < 2) throw new ArgumentException($"{Name} has no rings: turn it whole");
        var turning = Enumerable.Range(0, orbits.Count).Where(o => !ring.HasValue || o == (int)ring.Value).Where(o => orbits[o].Count >= 2).ToList();
        if (turning.Count == 0) throw new ArgumentException($"{Name}{(ring.HasValue ? $", its {ring.Value.ToString().ToLowerInvariant()} ring," : "")} has one place: nothing to rotate");
        int sign = sense == Sense.Clockwise ? -1 : 1;
        var lines = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in Fleet.Members)
        {
            int at = held[member.Name];
            int o = Enumerable.Range(0, orbits.Count).First(k => at >= orbits[k].Start && at < orbits[k].Start + orbits[k].Count);
            if (!turning.Contains(o)) continue;
            var (start, n) = orbits[o];
            var way = new List<Spot>();
            int here = at;
            for (int k = 1; k <= steps; k++)
            {
                int next = start + Mod(here - start + sign, n);
                way.AddRange(Figure.Way(here, next, Fleet.Count, sense));
                here = next;
            }
            lines[member.Name] = "visit " + string.Join(" ", way);
            held[member.Name] = here;
        }
        foreach (int o in turning) turned[o] += sign * steps;
        Unshot = false;   // its visits take everybody onto the figure as it stands now
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Holders)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Turned)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(TurnedText)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Unshot)));
        return lines;
    }

    /// <summary>A MOVE (propuesta 97, 7-oct-2026; Juan: "seleccionar la formación y poder desplazar el overlay del mapa para cambiarle el centro a
    /// otra posición"): the same figure, its centre elsewhere. A DRAFT until <see cref="Shot"/> (Juan, 7-oct-2026: "cuando suelto el clic
    /// termina de poner el comando visit… quizás quisiera un botón para agregar a los tabs, como un shot formation"). The console knows nothing
    /// of the map, so where it goes is the operator's to judge.</summary>
    public void Move(Spot center) => Reshape(Figure.At(center));

    /// <summary>A TURN OF THE FIGURE ITSELF (propuesta 98, 7-oct-2026; Juan: "rotar y redimensionar esa figura de la formación"): the same figure,
    /// the same centre and size, at this orientation, degrees counter-clockwise from the way the golem's module lays it. Not
    /// <see cref="Rotate"/>, which passes every member to the next vertex: here nobody changes vertex, the vertices themselves turn. A draft.</summary>
    public void Orient(double angle) => Reshape(Figure.Oriented(angle));

    /// <summary>A RESIZE (propuesta 98): the same figure, bigger or smaller — a polygon by its side, a circle by its radius. A draft.</summary>
    public void Resize(double measure) => Reshape(Figure.Sized(measure));

    /// <summary>Whether the figure changed — moved, turned, resized — since its lines last went to the tabs: what <see cref="Shot"/> would write.</summary>
    public bool Unshot { get; private set; }

    /// <summary>THE SHOT: the formation as it stands, in lines — every member's <c>visit</c> to the vertex it holds, where that vertex is now.
    /// The draft is over; nothing is sent here.</summary>
    public IReadOnlyDictionary<string, string> Shot()
    {
        var places = Places;
        var lines = Fleet.Members.ToDictionary(m => m.Name, m => "visit " + places[held[m.Name]], StringComparer.Ordinal);
        Unshot = false;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Unshot)));
        return lines;
    }

    // what a move, a turn and a resize share: the figure changes, every member KEEPS ITS VERTEX, the rotations counted stay, and the
    // formation waits for its shot
    private void Reshape(Figure next)
    {
        next.Places(Fleet.Count);   // a figure the fleet does not fit is refused before anything changes
        Figure = next;
        Unshot = true;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Figure)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Holders)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Unshot)));
    }

    // ==================================================================
    // THE CONTEXT, NEVER THE COORDINATE (propuesta 99, 8-oct-2026; Juan: "darle contexto al golem pero nunca darle la coordenada exacta de su
    // visit"): a polygon is TOLD to every golem by its name — `form square-1 square --center x,y --side s --angle a`, with `--places n` for more
    // golems than vertices — and each golem is told the NUMBER of its place — `take square-1 --place 2` —; the golem resolves where it stands,
    // on its own map. A circle's places hang on
    // how many bodies take it, so a circle still goes as visits.
    // ==================================================================

    /// <summary>Whether the golems can be told this formation rather than sent to its points: a polygon, and since ajuste 102 (8-oct-2026;
    /// Juan: "no con el contexto del doble anillo para decirle cuál es su posición") a circle and a double ring — a ring is a circle of the
    /// golem's, told for how many ride it.</summary>
    public bool CanForm => Figure is Polygon or Circle or global::WardenCli.Formations.DoubleRing;

    /// <summary>The name the golems know a ring of a double ring by: "double-ring-1-outer", "double-ring-1-inner".</summary>
    public string RingWireName(Ring ring) => $"{WireName}-{ring.ToString().ToLowerInvariant()}";

    /// <summary>The places of a ring of a double ring, in its own order.</summary>
    public IReadOnlyList<Spot> RingPlaces(Ring ring)
    {
        var (start, count) = Figure.Orbits(Fleet.Count)[ring == Ring.Outer ? 0 : 1];
        return Places.Skip(start).Take(count).ToList();
    }

    /// <summary>The name the golems know it by: the console's name without the space and the sign — "square #1" is <c>square-1</c>.</summary>
    public string WireName => $"{Figure.Name.Replace(' ', '-')}-{Number}";

    /// <summary>The line that tells a golem this formation as it stands now — made, or made again after a move, a turn or a resize; for more
    /// golems than vertices it says how many it is laid out for (ajuste 101), so the golem resolves the same places.</summary>
    public string FormLine() => Figure switch
    {
        Circle circle => $"form {WireName} circle --center {Figure.Center} --radius {Spot.Fmt(circle.Radius)} --angle {Spot.Fmt(Figure.Angle)} --places {Fleet.Count}",
        global::WardenCli.Formations.DoubleRing => throw new ArgumentException($"{Name} is told ring by ring: FormLine(member)"),
        _ => $"form {WireName} {Figure.Name} --center {Figure.Center} --side {Spot.Fmt(Figure.Measure)} --angle {Spot.Fmt(Figure.Angle)}" +
             (Figure is Polygon p && Fleet.Count > p.VertexCount ? $" --places {Fleet.Count}" : ""),
    };

    /// <summary>The line that tells THIS member the formation (ajuste 102): a double ring is told ring by ring — each golem its own ring, a
    /// circle of the golem's with the ring's radius and how many ride it; any other formation, the same line for all.</summary>
    public string FormLine(string member)
    {
        if (Figure is not DoubleRing rings) return FormLine();
        var ring = RingOf(member);
        double radius = ring == Ring.Outer ? rings.OuterRadius : rings.InnerRadius;
        int count = ring == Ring.Outer ? rings.OuterPlaces : rings.InnerPlaces;
        return $"form {RingWireName(ring)} circle --center {Figure.Center} --radius {Spot.Fmt(radius)} --angle {Spot.Fmt(Figure.Angle)} --places {count}";
    }

    /// <summary>The line that tells a member which place it takes: the number it holds now — a vertex, or a point of a side (ajuste 101).</summary>
    public string TakeLine(string member) => TakeLine(member, null);

    /// <summary>The line that tells a member its place — and, for a STEP round a ring (ajuste 102), the sense, so the golem goes along the arc;
    /// on a double ring the number is the place on the member's own ring.</summary>
    public string TakeLine(string member, Sense? sense)
    {
        string step = sense.HasValue && Figure is Circle or global::WardenCli.Formations.DoubleRing ? $" --sense {(sense == Sense.Clockwise ? "clockwise" : "counterclockwise")}" : "";
        if (Figure is DoubleRing)
        {
            var ring = RingOf(member);
            int start = Figure.Orbits(Fleet.Count)[ring == Ring.Outer ? 0 : 1].Start;
            return $"take {RingWireName(ring)} --place {IndexOf(member) - start}{step}";
        }
        return $"take {WireName} --place {IndexOf(member)}{step}";
    }

    /// <summary>Who holds what, vertex by vertex in the figure's order — "blue NE · green NW · red SW · yellow SE"; a free vertex says so.</summary>
    public string Holders
    {
        get
        {
            int count = Places.Count;
            string Of(int start, int n) => string.Join(" · ", Enumerable.Range(start, n).Select(i => HolderOf(i) is { } who ? $"{who} {Figure.Label(i, count)}" : $"{Figure.Label(i, count)} free"));
            var orbits = Figure.Orbits(Fleet.Count);
            return orbits.Count == 1 ? Of(0, count) : $"outer: {Of(orbits[0].Start, orbits[0].Count)} — inner: {Of(orbits[1].Start, orbits[1].Count)}";
        }
    }

    public string Describe() => $"{Name} at {Figure.Center}, {Fleet.Count} golem(s), turned {Turned}: {Holders}";

    private static int Mod(int a, int n) => ((a % n) + n) % n;

    public event PropertyChangedEventHandler? PropertyChanged;
}
