using System.ComponentModel;

namespace WardenCli.Formations;

/// <summary>The SENSE of a turn around a figure — the console's own closed set, the same two words as the golem's module.</summary>
public enum Sense
{
    Clockwise,
    Counterclockwise,
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

    public Formation(int number, Figure figure, Fleet fleet, IReadOnlyDictionary<string, int> held)
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
    }

    /// <summary>Its order of birth in the console's list: "square #2".</summary>
    public int Number { get; }
    public Figure Figure { get; private set; }
    public Fleet Fleet { get; }
    /// <summary>How many steps it has turned since it was laid out, clockwise negative — the places run counter-clockwise.</summary>
    public int Turned { get; private set; }

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

    /// <summary>A ROTATION: every member moves that many vertices in the sense — one by default — and the line each golem gets is a
    /// <c>visit</c> through the vertices it passes, in order; the vertices held move with them. Nothing is sent here.</summary>
    public IReadOnlyDictionary<string, string> Rotate(Sense sense, int steps = 1)
    {
        if (steps < 1 || steps > 200) throw new ArgumentException("a rotation is 1 to 200 steps");
        var places = Places;
        if (places.Count < 2) throw new ArgumentException($"{Name} has one vertex: nothing to rotate");
        int sign = sense == Sense.Clockwise ? -1 : 1;
        var lines = new Dictionary<string, string>(StringComparer.Ordinal);
        foreach (var member in Fleet.Members)
        {
            int from = held[member.Name];
            var way = Enumerable.Range(1, steps).Select(k => places[Mod(from + sign * k, places.Count)]);
            lines[member.Name] = "visit " + string.Join(" ", way);
            held[member.Name] = Mod(from + sign * steps, places.Count);
        }
        Turned += sign * steps;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Holders)));
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Turned)));
        return lines;
    }

    /// <summary>A MOVE (propuesta 97, 7-oct-2026; Juan: "seleccionar la formación y poder desplazar el overlay del mapa para cambiarle el centro a
    /// otra posición"): the same figure, its centre elsewhere — every member KEEPS ITS VERTEX and the line it gets is the <c>visit</c> to that
    /// vertex at the new place; the rotations counted stay. Nothing is sent here; the console knows nothing of the map, so where it goes is
    /// the operator's to judge.</summary>
    public IReadOnlyDictionary<string, string> Move(Spot center)
    {
        var moved = Figure.At(center);
        var places = moved.Places(Fleet.Count);
        var lines = Fleet.Members.ToDictionary(m => m.Name, m => "visit " + places[held[m.Name]], StringComparer.Ordinal);
        Figure = moved;
        PropertyChanged?.Invoke(this, new PropertyChangedEventArgs(nameof(Figure)));
        return lines;
    }

    /// <summary>Who holds what, vertex by vertex in the figure's order — "blue NE · green NW · red SW · yellow SE"; a free vertex says so.</summary>
    public string Holders
    {
        get
        {
            int count = Places.Count;
            return string.Join(" · ", Enumerable.Range(0, count).Select(i => HolderOf(i) is { } who ? $"{who} {Figure.Label(i, count)}" : $"{Figure.Label(i, count)} free"));
        }
    }

    public string Describe() => $"{Name} at {Figure.Center}, {Fleet.Count} golem(s), turned {Turned}: {Holders}";

    private static int Mod(int a, int n) => ((a % n) + n) % n;

    public event PropertyChangedEventHandler? PropertyChanged;
}
