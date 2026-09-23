using System.Globalization;

namespace GolemAPI.Choreography;

// THE RESOLUTION OF A MEASURE (Juan, 23-sep-2026, ajuste 53: "¿tantos decimales nos dan más exactitud o sólo nos están haciendo
// ruido?"): a pose from the body, a bearing from the bumper, a point from the operator carry far less precision than a double can
// write — a contact's pose in Gazebo is ~0.1 m off, an arrival 2–7 cm, dead reckoning up to 0.76 m — so the host rounds every
// magnitude it writes into the journal, and every amount it sends the body, to the MILLIMETRE and the MILLIRADIAN. The journal
// reads `Pose(5.855, 4.780, 1.456)`, nothing measured is lost, and what is written is exactly what the domain uses: replay stays
// exact, and a word the wire repeats is still the same word. The domain keeps computing in doubles inside; this is the boundary.
public static class Resolution
{
    /// <summary>Decimals kept: a millimetre, a milliradian.</summary>
    public const int Digits = 3;

    /// <summary>A length (a coordinate, an amount to advance), to the millimetre.</summary>
    public static double Metres(double value) => Math.Round(value, Digits, MidpointRounding.AwayFromZero);

    /// <summary>An angle (a heading, a bearing, an amount to turn), to the milliradian.</summary>
    public static double Radians(double value) => Math.Round(value, Digits, MidpointRounding.AwayFromZero);

    /// <summary>A value as it is written on the wire: invariant culture, no more than the digits kept.</summary>
    public static string Written(double value) => value.ToString("0.###", CultureInfo.InvariantCulture);
}
