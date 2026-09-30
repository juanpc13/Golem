using GolemDomain.Units;

namespace GolemDomain.Formations;

/// <summary>
/// An EFFECT on a formation (propuesta 59, paso 3, 29-sep-2026; Juan: "efectos de giro… rotate-clockwise… 10s"): the fleet turns
/// around the figure, every body to the next place in the sense given, for as long as this lasts. <c>Rotation('clockwise', Seconds(10.0))</c>;
/// the other sense is 'counterclockwise'. The places of a circle run counter-clockwise from due east, so clockwise steps DOWN the ranks.
/// </summary>
internal sealed class Rotation
{
    internal bool Clockwise { get; }
    internal Duration Lasting { get; }

    internal Rotation(string direction, Duration lasting)
    {
        if (string.IsNullOrWhiteSpace(direction)) throw new GolemDomainException("Rotation.Rotation: 'direction' was not given");
        if (lasting == null) throw new GolemDomainException("Rotation.Rotation: 'lasting' was not given");
        string d = direction.Trim().ToLowerInvariant().Replace("-", "");
        if (d is not ("clockwise" or "counterclockwise")) throw new GolemDomainException($"a rotation turns 'clockwise' or 'counterclockwise', not '{direction}'");
        if (lasting.InSeconds <= 0) throw new GolemDomainException("a rotation lasts longer than nothing");
        Clockwise = d == "clockwise";
        Lasting = lasting;
    }

    internal string Direction => Clockwise ? "clockwise" : "counterclockwise";
}
