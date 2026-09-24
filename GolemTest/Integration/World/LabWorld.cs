using System.Text.Json;

namespace GolemTest.World;

/// <summary>Which world the scenarios run against, and how to reach it — read from the test project's configuration.</summary>
public sealed record LabSettings(string World, Uri Rosbridge, IReadOnlyDictionary<string, Uri> Golems, TimeSpan MockTimeout, TimeSpan GazeboTimeout)
{
    public bool InGazebo => string.Equals(World, "gazebo", StringComparison.OrdinalIgnoreCase);
    /// <summary>How long a scenario may take to settle in the world chosen.</summary>
    public TimeSpan Patience => InGazebo ? GazeboTimeout : MockTimeout;

    /// <summary>`appsettings.json` beside the test binary, overlaid by `appsettings.local.json` when it exists (not versioned: each
    /// one's own wiring), and last by the environment — `GOLEM_LAB_WORLD` (mock | gazebo) and `GOLEM_LAB_ROSBRIDGE` — so a
    /// command line can ask for Gazebo without touching a file. With nothing said, the world is the mock, held in memory (Juan, 24-sep-2026: `mock`, was `memory`).</summary>
    public static LabSettings Load()
    {
        string world = "mock", rosbridge = "ws://localhost:9090";
        var golems = new Dictionary<string, Uri>();
        double mock = 60, gazebo = 180;
        foreach (var file in new[] { "appsettings.json", "appsettings.local.json" })
        {
            string path = Path.Combine(AppContext.BaseDirectory, file);
            if (!File.Exists(path)) continue;
            // comments and trailing commas are allowed: the settings explain themselves (Juan, 23-sep-2026)
            using var doc = JsonDocument.Parse(File.ReadAllText(path), new JsonDocumentOptions { CommentHandling = JsonCommentHandling.Skip, AllowTrailingCommas = true });
            if (!doc.RootElement.TryGetProperty("Lab", out var lab)) continue;
            if (lab.TryGetProperty("World", out var w)) world = w.GetString();
            if (lab.TryGetProperty("Rosbridge", out var r)) rosbridge = r.GetString();
            if (lab.TryGetProperty("Golems", out var g))
                foreach (var p in g.EnumerateObject()) golems[p.Name] = new Uri(p.Value.GetString());
            if (lab.TryGetProperty("MockTimeoutSeconds", out var m)) mock = m.GetDouble();
            if (lab.TryGetProperty("GazeboTimeoutSeconds", out var z)) gazebo = z.GetDouble();
        }
        world = Environment.GetEnvironmentVariable("GOLEM_LAB_WORLD") is { Length: > 0 } ew ? ew : world;
        rosbridge = Environment.GetEnvironmentVariable("GOLEM_LAB_ROSBRIDGE") is { Length: > 0 } er ? er : rosbridge;
        if (world is not ("mock" or "gazebo"))
            throw new InvalidOperationException($"Lab:World is '{world}': it must be 'mock' or 'gazebo'");
        return new LabSettings(world, new Uri(rosbridge), golems, TimeSpan.FromSeconds(mock), TimeSpan.FromSeconds(gazebo));
    }
}

// THE WORLD THE CONFIGURATION ASKS FOR (propuesta 52, fase 3, 23-sep-2026; Juan: "que en el appsettings del GolemTest uno lo
// pueda cablear al simulador y si no está cableado todo es mock"): the mock world, held in memory, by default, Gazebo when the
// settings say so. A Gazebo that does not answer throws WorldUnavailableException — the scenario is inconclusive, never red.
public static class LabWorld
{
    public static async Task<ILabWorld> OpenAsync(LabSettings settings)
    {
        if (settings == null) throw new ArgumentNullException(nameof(settings));
        if (!settings.InGazebo) return new MockWorld();
        if (settings.Golems.Count == 0) throw new InvalidOperationException("Lab:World is 'gazebo' but Lab:Golems names no golem to ask");
        return await GazeboWorld.OpenAsync(settings.Rosbridge, settings.Golems, settings.Patience);
    }
}
