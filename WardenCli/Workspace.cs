using System.IO;
using System.Text.Json;

namespace WardenCli;

/// <summary>A WORKSPACE is ONE FILE, <c>name.golemws</c> (Alvaro, 6-oct-2026: "otro archivo más que es un workspace… los golems con los que está
/// trabajando y el último script de cada golem, para hacer switch entre workspaces"): the golems in operation — name, host, port — and the
/// script each one was left with. One file, so a workspace travels and switching is opening another. The routines stay files of their own
/// (<c>*.routine</c>, one command per line), looked for next to the workspace. The console remembers the workspaces opened lately and the
/// last one, in the user's profile, and reopens the last at start.</summary>
public static class Workspace
{
    public const string Extension = ".golemws";
    private static readonly JsonSerializerOptions Pretty = new() { WriteIndented = true };

    private sealed record SavedGolem(string Name, string Host, int Port, string Script);
    private sealed record File_(string Kind, int Version, List<SavedGolem> Golems);
    private sealed record Settings(List<string> Recent, string? Last);

    public static void Save(string path, IEnumerable<Golem> golems)
    {
        var file = new File_("golem workspace", 1, golems.Select(g => new SavedGolem(g.Name, g.Host, g.Port, g.Script)).ToList());
        Directory.CreateDirectory(Path.GetDirectoryName(Path.GetFullPath(path))!);
        File.WriteAllText(path, JsonSerializer.Serialize(file, Pretty));
        Remember(path);
    }

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

    private static string SettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "WardenCli", "settings.json");
    // where they lived while the console was GolemCli (until 7-oct-2026): read while the new file does not exist yet, so nothing is lost
    private static string LegacySettingsPath => Path.Combine(Environment.GetFolderPath(Environment.SpecialFolder.ApplicationData), "GolemCli", "settings.json");

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
        WriteSettings(new Settings(s.Recent, string.Equals(s.Last, path, StringComparison.OrdinalIgnoreCase) ? null : s.Last));
    }

    private static void Remember(string path)
    {
        string full = Path.GetFullPath(path);
        var s = ReadSettings();
        s.Recent.RemoveAll(p => string.Equals(p, full, StringComparison.OrdinalIgnoreCase));
        s.Recent.Insert(0, full);
        WriteSettings(new Settings(s.Recent.Take(8).ToList(), full));
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
