namespace GolemAPI.Membrane;

/// <summary>
/// THE WORLD'S LAYOUT (propuesta 109, 12-oct-2026; Juan: "tener ya los 3 tipos de mapas en Gazebo: al lado izquierdo el open floor, al
/// centro el warehouse y al derecho el maze… y cuando uno le haga place en el CLI se muevan a donde corresponden"): WHERE EACH MAP LIES in the
/// simulator's world — a fact of the world, never of the map (paper 09: the domain declares no staging): every map of the catalog keeps
/// its own frame, and the host translates at the edges — the pose the membrane brings is read in the frame of the scenario the golem is
/// in, a point the operator names is carried to that map's region. Told by the environment, as the world was built:
/// <c>MAP_ORIGINS=open-floor=0,0,11,11;warehouse=13,0,11,11;maze=26,0,10.5,10.5</c> (name = origin x, y, width, height). Nothing told:
/// one frame, the world's and the maps' the same, as before.
/// </summary>
public sealed class WorldLayout
{
    /// <summary>A map's region of the world: where its origin lies and how much floor it covers.</summary>
    public sealed record Region(string Name, double X, double Y, double Width, double Height)
    {
        /// <summary>Whether a point of the world lies in this region — half a metre of slack round it, for a body on its edge.</summary>
        public bool Contains(double wx, double wy) => wx >= X - 0.5 && wx <= X + Width + 0.5 && wy >= Y - 0.5 && wy <= Y + Height + 0.5;
    }

    private readonly IReadOnlyDictionary<string, Region> regions;

    /// <summary>No layout told: every map at the world's origin.</summary>
    public static readonly WorldLayout OneFrame = new(new Dictionary<string, Region>());

    private WorldLayout(IReadOnlyDictionary<string, Region> regions) { this.regions = regions; }

    public static WorldLayout Parse(string text)
    {
        var regions = new Dictionary<string, Region>(StringComparer.Ordinal);
        foreach (var entry in (text ?? "").Split(';', StringSplitOptions.RemoveEmptyEntries | StringSplitOptions.TrimEntries))
        {
            int eq = entry.IndexOf('=');
            if (eq <= 0) throw new ArgumentException($"MAP_ORIGINS: '{entry}' is not name=x,y,w,h");
            string name = entry[..eq].Trim();
            var n = entry[(eq + 1)..].Split(',', StringSplitOptions.TrimEntries);
            if (n.Length != 4 || !n.All(v => double.TryParse(v, System.Globalization.NumberStyles.Float, System.Globalization.CultureInfo.InvariantCulture, out _)))
                throw new ArgumentException($"MAP_ORIGINS: '{entry}' needs x,y,w,h after the name");
            double D(string v) => double.Parse(v, System.Globalization.CultureInfo.InvariantCulture);
            regions[name] = new Region(name, D(n[0]), D(n[1]), D(n[2]), D(n[3]));
        }
        return new WorldLayout(regions);
    }

    public IReadOnlyCollection<Region> Regions => regions.Values.ToList();

    /// <summary>The region of a map; null when the layout says nothing of it (its frame is the world's).</summary>
    public Region Of(string map) => map != null && regions.TryGetValue(map, out var r) ? r : null;

    /// <summary>The map whose region holds a point of the world; null when none does.</summary>
    public string At(double wx, double wy) => regions.Values.FirstOrDefault(r => r.Contains(wx, wy))?.Name;

    /// <summary>A pose of the world read in a map's frame.</summary>
    public Pose ToMap(string map, Pose world) => world == null ? null : Of(map) is { } r ? new Pose(world.X - r.X, world.Y - r.Y, world.Theta) : world;

    /// <summary>A point of a map carried into the world.</summary>
    public (double X, double Y) ToWorld(string map, double x, double y) => Of(map) is { } r ? (x + r.X, y + r.Y) : (x, y);
}
