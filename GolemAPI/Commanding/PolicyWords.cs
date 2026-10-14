namespace GolemAPI.Commanding;

/// <summary>
/// THE POLICY IN TWO VOCABULARIES (14-oct-2026; Juan: "Policy debería ser un Enum"): the operator says <c>rank</c>, <c>distance</c> or <c>rps</c>
/// (the line's <c>--by</c>, the request's <c>"by"</c>); the domain's <c>Policy</c> is an enum whose MEMBER'S NAME the host passes and the engine
/// resolves at the verb (ajuste 90, like the sense) — <c>Rank</c>, <c>Distance</c>, <c>RockPaperScissors</c> — and the read prints. Translated
/// here, both ways, and nowhere else.
/// </summary>
public static class PolicyWords
{
    /// <summary>The domain member's name for the operator's word, or null when it is no policy.</summary>
    public static string Member(string word) => word?.Trim().ToLowerInvariant() switch
    {
        "rank" => "Rank",
        "distance" => "Distance",
        "rps" => "RockPaperScissors",
        _ => null,
    };

    /// <summary>The operator's word for the member's name the read printed (unknown: as it came).</summary>
    public static string Word(string member) => member switch
    {
        "Rank" => "rank",
        "Distance" => "distance",
        "RockPaperScissors" => "rps",
        _ => member ?? "",
    };
}
