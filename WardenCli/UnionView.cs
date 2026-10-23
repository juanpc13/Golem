using System.Text.Json;

namespace WardenCli;

/// <summary>A UNION AS THE GOLEM TELLS IT (22-oct-2026; Juan: "debería haber un apartado o endpoint de las unionize que existen o a las que pertenece
/// el golem, así uno puede hacerle GET en el CLI para ver las formaciones que tiene encima esa unionize"): the golem's copy of a union — its name,
/// the formations it administers in order (the bill), the candidates, the policy, where its round stands (who spoke, known, complete, resolved,
/// stuck and why), whether THIS golem dances in it and where, who was left out, and the share every golem got — the `unions` read (`GET /unions`);
/// the console only shows it and asks the golem to dissolve it.</summary>
public sealed record UnionView(string Name, string Stamp, IReadOnlyList<string> Bill, string Candidates, string Policy, int Stood, bool Known, bool Complete,
                               bool Resolved, bool Stuck, string Why, bool Member, string Mine, string LeftOut, string Games, IReadOnlyList<ShareView> Shares,
                               string Link = "cue", int Beat = 0, bool Done = false, bool Stranded = false, string StrandedWhy = "")
{
    /// <summary>A union written on a tab and not sent yet (like a pending formation): nothing in the golem.</summary>
    public bool IsPending { get; init; }
    /// <summary>The tabs the pending union is written on.</summary>
    public string PendingOn { get; init; } = "";

    public int CandidateCount => Candidates.Split(',', StringSplitOptions.RemoveEmptyEntries).Length;
    public string BillText => string.Join(" → ", Bill);
    public string PolicyWord => Policy.Equals("RockPaperScissors", StringComparison.OrdinalIgnoreCase) ? "rps" : Policy.ToLowerInvariant();

    /// <summary>Where the union stands, in the chip: to send, waiting for the words (k of n), waiting for a formation to be told, resolved, or stuck.</summary>
    public string StateText =>
        IsPending ? $"to send · on {PendingOn}"
        : Stuck ? $"stuck — {Why}"
        : Stranded ? $"stranded — {StrandedWhy} — take {Name} again"
        : Done ? $"done — {Beat} beat{(Beat == 1 ? "" : "s")}"
        : Resolved ? (Beat > 0 ? $"dancing — beat {Beat}" : "resolved — taking")
        : !Known && Stood >= CandidateCount && CandidateCount > 0 ? "waiting for a formation to be told"
        : $"waiting for the words — {Stood}/{CandidateCount} stood";
    public bool IsStuck => (Stuck || Stranded) && !IsPending;
    public bool IsResolved => Resolved && !IsPending;

    /// <summary>What this golem got: its formation, left out, or nothing yet.</summary>
    public string MineText =>
        IsPending ? "" : !Resolved ? "" : Member ? $"mine: {Mine}" : LeftOut != "" && LeftOut.Split(',').Any(n => n.Trim() != "") ? "not mine" : "";

    /// <summary>THE FORMATIONS UNDER THE UNION with who went to each — one line per formation of the bill, in order, from the shares the law settled;
    /// before the round resolved, the bill alone.</summary>
    public string FormationsText => string.Join("\n", Bill.Select(f =>
    {
        var who = Shares.Where(s => s.Formation.Equals(f, StringComparison.OrdinalIgnoreCase)).OrderBy(s => s.Place).Select(s => $"{s.Who} ({s.Place})").ToList();
        return who.Count == 0 ? $"{f}: —" : $"{f}: {string.Join(", ", who)}";
    })) + (LeftOut.Trim() == "" ? "" : $"\nleft out: {LeftOut}");

    public string LinkWord => Link.Trim().ToLowerInvariant();
    public string CandidatesText => $"candidates {Candidates.Replace(",", ", ")} · by {PolicyWord} · the beats by {(LinkWord == "ask" ? "asking" : "the cue")}" + (Games.Trim() == "" ? "" : $" · {Games}");

    public static IReadOnlyList<UnionView> Parse(JsonElement json)
    {
        var list = new List<UnionView>();
        if (JsonWalk.Find(json, "shows") is not { ValueKind: JsonValueKind.Array } shows) return list;
        foreach (var s in shows.EnumerateArray())
        {
            var shares = new List<ShareView>();
            if (s.TryGetProperty("shares", out var sh) && sh.ValueKind == JsonValueKind.Array)
                foreach (var x in sh.EnumerateArray()) shares.Add(new ShareView(Str(x, "who"), Str(x, "in"), (int)Num(x, "place")));
            list.Add(new UnionView(Str(s, "called"), Str(s, "nonce"),
                                   Str(s, "formations").Split(',', StringSplitOptions.RemoveEmptyEntries).Select(f => f.Trim()).ToList(),
                                   Str(s, "candidates"), Str(s, "policy"), (int)Num(s, "stood"),
                                   Bool(s, "known"), Bool(s, "complete"), Bool(s, "resolved"), Bool(s, "stuck"), Str(s, "why"),
                                   Bool(s, "member"), Str(s, "mine"), Str(s, "leftOut"), Str(s, "games"), shares,
                                   Str(s, "link") == "" ? "cue" : Str(s, "link"), (int)Num(s, "beat"), Bool(s, "done"), Bool(s, "stranded"), Str(s, "strandedWhy")));
        }
        return list;
    }

    private static bool Bool(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.True;
    private static double Num(JsonElement e, string name) => e.TryGetProperty(name, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : 0;
    private static string Str(JsonElement e, string name) => e.TryGetProperty(name, out var v) ? (v.ValueKind == JsonValueKind.String ? v.GetString() ?? "" : v.ToString()) : "";
}

/// <summary>One share the union's law settled: who, in which formation, which place.</summary>
public sealed record ShareView(string Who, string Formation, int Place);
