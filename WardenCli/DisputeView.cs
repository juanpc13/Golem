using System.Globalization;
using System.Text.Json;

namespace WardenCli;

/// <summary>A DISPUTE AS THE GOLEM TELLS IT (14-oct-2026; Juan: "otro apartado o tab abajo del mapa relacionado a las disputas que ha tenido el golem
/// para resolver una posición con otro golem y el resultado de si éste cedió o la ganó él, por la política de distance"): a place of a formation
/// this golem wanted, by distance, and whether it won it or yielded it, against whom, with both distances — the golem's `disputes` read; the
/// console only shows it.</summary>
public sealed record DisputeView(string Called, int Place, double X, double Y, string Rival, bool Won, double Mine, double Theirs, string Game = "")
{
    private static string N(double v) => Math.Round(v, 2).ToString("0.##", CultureInfo.InvariantCulture);

    public string Title => Won ? "✓ won" : "↷ yielded";
    public string Text => Won ? $"place {Place} of {Called} against {Rival}" : $"place {Place} of {Called} to {Rival}";
    public string Where => $"({N(X)}, {N(Y)})";
    public string Distances => (Game == "" ? "" : $"{Game} · ") + $"{N(Mine)} m from it · {Rival} {N(Theirs)} m";

    public static IReadOnlyList<DisputeView> Parse(JsonElement json)
    {
        var list = new List<DisputeView>();
        if (JsonWalk.Find(json, "disputes") is not { ValueKind: JsonValueKind.Array } items) return list;
        foreach (var d in items.EnumerateArray())
            list.Add(new DisputeView(Str(d, "called"), (int)Num(d, "place"), Num(d, "x"), Num(d, "y"), Str(d, "rival"),
                                     d.TryGetProperty("won", out var w) && w.ValueKind == JsonValueKind.True, Num(d, "mine"), Num(d, "theirs"), Str(d, "game")));
        return list;
    }

    private static double Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";
}
