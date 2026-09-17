using System.Globalization;
using System.Text.Json;

namespace GolemAPI.Choreography;

// What the golem tells its body to do NOW — one thing at a time (Juan, 16-sep-2026: "un comando ejecutado produce un
// print del siguiente punto que debe alcanzar y una vez alcanzado pide el siguiente"). Every script that changes it
// (GolemController: the errand, a point reached, a bump, a hold…) ends with the same print, and the command RETURNS
// that print to whoever performed it, at write time. Four orders exist, one thing each: TURN in place to the next leg's
// heading, RUN to the next leg's point (kind door/opening/via/stop, its approach and exit), HOLD (the operator paused the
// route), or DECIDE (the route has no way, or a bump interrupted it: the route decides it again from where the body
// stands — `route.Decide(from)`). Nothing pending → no order. The Robot parses it and sends the same JSON on to the body.
public sealed record Order(int Route, string What, string Kind, string Name, double X, double Y, double AX, double AY, double EX, double EY,
                           bool HasHeading, double Heading, bool Following, int StopsLeft)
{
    public bool IsCrossedStraight => AX != EX || AY != EY;
    public bool IsLastStop => Kind == "stop" && StopsLeft <= 1;

    /// <summary>The same order as another: the same route asked to do the same thing at the same point.</summary>
    public bool SameAs(Order other) =>
        other != null && Route == other.Route && What == other.What && Kind == other.Kind && X == other.X && Y == other.Y && Heading == other.Heading;

    // The labels the golem prints (GolemController.NextOrder): route, order (hold | decide | turn | run) and, for a leg,
    // kind name x y ax ay ex ey hasHeading heading following stopsLeft.
    public static Order Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var e = doc.RootElement;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("route", out var route) || !e.TryGetProperty("order", out var what)) return null;
            string w = what.GetString() ?? "";
            if (e.TryGetProperty("held", out var held) && held.ValueKind == JsonValueKind.True) w = "hold";   // the golem is held: whatever the route says, the body stands
            double D(string n, double d = 0) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : d;
            bool B(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
            string S(string n) => e.TryGetProperty(n, out var v) ? v.GetString() ?? "" : "";
            double x = D("x"), y = D("y");
            return new Order(route.GetInt32(), w, S("kind"), S("name"), x, y, D("ax", x), D("ay", y), D("ex", x), D("ey", y),
                             B("hasHeading"), D("heading"), B("following"), (int)D("stopsLeft"));
        }
        catch (JsonException) { return null; }
    }

    public string Describe() => What == "turn" ? $"turn to {Heading:0.00}" : What != "run" ? What
        : $"{(Kind == "door" || Kind == "opening" ? Name : Kind)}@{X.ToString("0.##", CultureInfo.InvariantCulture)},{Y.ToString("0.##", CultureInfo.InvariantCulture)}"
          + (HasHeading ? $" heading {Heading:0.00}" : "");
}

// What a script answered: the print it returned (the order the golem hands out — null when nothing is pending), or the
// refusal the Check gave (the domain's own words) — never both.
public readonly record struct Answer(Order Order, string Refused, string Print)
{
    public bool Ok => Refused == "";
    /// <summary>A refusal the host itself gives, in plain words, before or instead of a script.</summary>
    public static Answer Refusal(string why) => new(null, why, "");
    public static Answer Of(string performed)
    {
        // a refused Check comes back as {"EWI":[{"Error":"…"}]}; anything else is the print (or nothing)
        if (!string.IsNullOrWhiteSpace(performed) && performed.Contains("\"EWI\"", StringComparison.Ordinal))
        {
            try
            {
                using var doc = JsonDocument.Parse(performed);
                var first = doc.RootElement.GetProperty("EWI").EnumerateArray().FirstOrDefault();
                if (first.ValueKind == JsonValueKind.Object && first.TryGetProperty("Error", out var error)) return new Answer(null, error.GetString() ?? "refused", performed);
            }
            catch (JsonException) { }
            return new Answer(null, performed, performed);
        }
        return new Answer(Order.Parse(performed), "", performed ?? "");
    }
}
