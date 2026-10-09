using System.IO;
using System.Text.Json;

namespace WardenCli;

/// <summary>A WORKSPACE is ONE FILE, <c>name.golemws</c> (Alvaro, 6-oct-2026: "otro archivo más que es un workspace… los golems con los que está
/// trabajando y el último script de cada golem, para hacer switch entre workspaces"): the golems in operation — name, host, port — and the
/// script each one was left with — and, since 7-oct-2026, THE ACTIVE FORMATIONS (Juan: "guardar las formaciones en el workspace"): each one's
/// figure, centre, measure and orientation, its fleet, who holds which vertex, the steps it turned, its eye and its draft. One file, so a
/// workspace travels and switching is opening another. The routines stay files of their own
/// (<c>*.routine</c>, one command per line), looked for next to the workspace. The console remembers the workspaces opened lately and the
/// last one, in the user's profile, and reopens the last at start.</summary>
public static class Workspace
{
    public const string Extension = ".golemws";
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    private sealed record SavedGolem(string Name, string Host, int Port, string Script);
    // version 2 carried the console's formations; since propuesta 104 (8-oct-2026) the formations are the golems' own, read from them, and a
    // file's Formations are ignored
    private sealed record File_(string Kind, int Version, List<SavedGolem> Golems);
    private sealed record Settings(List<string> Recent, string? Last, PaneSizes? Panes = null);

    public static void Save(string path, IEnumerable<Golem> golems)
    {
        var file = new File_("golem workspace", 3, golems.Select(g => new SavedGolem(g.Name, g.Host, g.Port, g.Script)).ToList());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(file, Pretty));
        Remember(path);
    }

    /// <summary>The golems a workspace file holds, with the script each was left with.</summary>
    public static List<Golem> Load(string path)
    {
        var file = JsonSerializer.Deserialize<File_>(File.ReadAllText(path)) ?? throw new InvalidDataException("the workspace file could not be read");
        if (file.Kind != "golem workspace") throw new InvalidDataException("this is not a golem workspace file");
        Remember(path);
        return file.Golems.Select(s => new Golem { Name = s.Name, Host = s.Host, Port = s.Port, Script = s.Script ?? "" }).ToList();
    }

    /// <summary>Where the routines of a workspace are looked for: next to its file; the documents folder when there is none.</summary>
    public static string RoutinesFolder(string? workspacePath) =>
        workspacePath == null ? Environment.GetFolderPath(Environment.SpecialFolder.MyDocuments) : Path.GetDirectoryName(Path.GetFullPath(workspacePath))!;

    // ---- the console's memory of workspaces: %AppData%\WardenCli\settings.json ----

    // WARDENCLI_SETTINGS names another file — a console started for a test keeps its recents apart from the operator's (7-oct-2026: a test
    // console writing the shared file while the operator's started left it opening no workspace)
    private static string? SettingsOverride => Environment.GetEnvironmentVariable("WARDENCLI_SETTINGS") is { Length: > 0 } path ? path : null;
    private static string SettingsPath => SettingsOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WardenCli", "settings.json");
    // where they lived while the console was GolemCli (until 7-oct-2026): read while the new file does not exist yet, so nothing is lost
    private static string LegacySettingsPath => SettingsOverride ?? Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GolemCli", "settings.json");

    public static IReadOnlyList<string> Recent() => ReadSettings().Recent.Where(File.Exists).ToList();

    public static string? Last()
    {
        string? last = ReadSettings().Last;
        return last != null && File.Exists(last) ? last : null;
    }

    public static void Forget(string path)
    {
        var s = ReadSettings();
        s.Recent.RemoveAll(p => string.Equals(p, path, StringComparison.OrdinalIgnoreCase));
        WriteSettings(new Settings(s.Recent, string.Equals(s.Last, path, StringComparison.OrdinalIgnoreCase) ? null : s.Last, s.Panes));
    }

    private static void Remember(string path)
    {
        string full = Path.GetFullPath(path);
        var s = ReadSettings();
        s.Recent.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        s.Recent.Insert(0, full);
        WriteSettings(new Settings(s.Recent.Take(8).ToList(), full, s.Panes));
    }

    /// <summary>The sizes the operator gave the panes when the console last closed; null before it ever did (Juan, 7-oct-2026: "que los
    /// tamaños de los paneles se recuerden al cerrar"). The machine's, not the workspace's: they stay whatever workspace is open.</summary>
    public static PaneSizes? Panes() => ReadSettings().Panes;

    public static void RememberPanes(PaneSizes panes)
    {
        var s = ReadSettings();
        WriteSettings(new Settings(s.Recent, s.Last, panes));
    }

    private static Settings ReadSettings()
    {
        try
        {
            string path = File.Exists(SettingsPath) ? SettingsPath : LegacySettingsPath;
            if (File.Exists(path))
                return JsonSerializer.Deserialize<Settings>(File.ReadAllText(path)) ?? new Settings(new List<string>(), null);
        }
        catch (Exception ex) when (ex is JsonException or IOException) { }
        return new Settings(new List<string>(), null);
    }

    private static void WriteSettings(Settings s)
    {
        try
        {
            Directory.CreateDirectory(Path.GetDirectoryName(SettingsPath)!);
            File.WriteAllText(SettingsPath, JsonSerializer.Serialize(s, Pretty));
        }
        catch (IOException) { }
    }
}

/// <summary>The sizes of the console's panes, as the dividers left them: the golems' column (px), the console and the debugger's shares of the
/// window (stars — their ratio is what counts), the log's height (px), the map's height if the operator set it (px; null: as high as it is
/// wide), and whether the debugger was shown.</summary>
// …and whether the map shows the other golems (8-oct-2026; Juan: "guarda el checkbox de other golems… o tal vez al cerrar"): a way of looking,
// the machine's like the sizes, not the workspace's; absent, they are shown
public sealed record PaneSizes(double GolemsWidth, double ConsoleShare, double DebuggerShare, double LogHeight, double? MapHeight, bool DebuggerShown, bool? ShowOthers = null);
