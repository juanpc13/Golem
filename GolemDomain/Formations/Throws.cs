namespace GolemDomain.Formations;

/// <summary>
/// THE HAND a golem plays at rock, paper, scissors (propuesta 115, 14-oct-2026; Juan: "que jueguen rock scissors paper para ver quién se queda la
/// posición"): nine throws — R (rock), P (paper), S (scissors) — DRAWN FROM ITS STAMP AND ITS NAME: its own copy reads its own stamp, the others
/// read the nonce its word carries (StoodFor), so every copy plays the same hands with no word more; the chance was frozen when the host minted
/// the stamp (paper 05). A stable hash (FNV-1a, then a linear congruence), never the runtime's own string hash, which changes per process.
/// </summary>
internal static class Throws
{
    internal const int Length = 9;

    internal static string Of(string stamp, string who)
    {
        if (stamp == null) throw new GolemDomainException("Throws.Of: 'stamp' was not given");
        if (who == null) throw new GolemDomainException("Throws.Of: 'who' was not given");
        ulong x = 14695981039346656037UL;
        foreach (byte b in System.Text.Encoding.UTF8.GetBytes(stamp + "|" + who)) { x ^= b; x *= 1099511628211UL; }
        var hand = new char[Length];
        for (int i = 0; i < Length; i++)
        {
            x = unchecked(x * 6364136223846793005UL + 1442695040888963407UL);
            hand[i] = "RPS"[(int)((x >> 33) % 3)];
        }
        return new string(hand);
    }

    /// <summary>Whether that throw beats the other: rock blunts scissors, scissors cut paper, paper covers rock.</summary>
    internal static bool Beats(char mine, char theirs) => (mine, theirs) is ('R', 'S') or ('S', 'P') or ('P', 'R');

    internal static string Word(char thrown) => thrown switch { 'R' => "rock", 'P' => "paper", _ => "scissors" };
}
