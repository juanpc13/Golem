using System.Globalization;
using System.Text.Json;

namespace GolemAPI.Choreography;

// What the golem tells its body to do NOW — one thing at a time (Juan, 16-sep-2026: "un comando ejecutado produce un
// print del siguiente punto que debe alcanzar y una vez alcanzado pide el siguiente"), IN THE ROBOT'S OWN WORDS (Juan,
// 17-sep-2026: "al robot se le dice muy sencillamente lo que debe moverse hacia adelante, qué tanto debe rotar"). Every
// script that changes it (the errand, a turn made, a point reached, a bump, a hold…) ends with the same print, and the
// command RETURNS that print to whoever performed it, at write time. The ACTION is one of the robot's base actions —
// advance, back, turnLeft, turnRight, stop — with its AMOUNT (metres, or radians). Never `decide` since 18-sep-2026: a route
// is born with its way and decides it again by itself. Nothing pending → no order. The rest (kind, name, the point headed to, following, stopsLeft) is
// what the panel and the log show; the body needs only the action and the amount.
public sealed record Order(int Route, string Action, double Amount, string Kind, string Name, double X, double Y, double Heading,
                           bool Following, int StopsLeft)
{
    /// <summary>The ticket the host stamped on the order when it sent it to the body (ajuste 55, 24-sep-2026): all the body echoes
    /// back — it knows nothing of the route. 0 on an order parsed from a print, before it was sent.</summary>
    public int Ticket { get; init; }

    public bool IsLastStop => Kind == "stop" && StopsLeft <= 1;
    public bool IsTurn => Action == "turnLeft" || Action == "turnRight";
    public bool IsMove => Action == "advance" || Action == "back";

    /// <summary>The same order as another: the same route asked for the same action, by the same amount, toward the same point.</summary>
    public bool SameAs(Order other) =>
        other != null && Route == other.Route && Action == other.Action && Kind == other.Kind && X == other.X && Y == other.Y
        && Math.Abs(Amount - other.Amount) < 1e-6;

    // The labels the golem prints at the end of every act (the same print, written in full in each script): route, action, amount and, for a leg, kind name x y heading
    // following stopsLeft. `held` true — the golem is held — reads as `stop` whatever the route says.
    public static Order Parse(string json)
    {
        if (string.IsNullOrWhiteSpace(json)) return null;
        try
        {
            using var doc = JsonDocument.Parse(json);
            var e = doc.RootElement;
            if (e.ValueKind != JsonValueKind.Object || !e.TryGetProperty("route", out var route) || !e.TryGetProperty("action", out var action)) return null;
            string a = action.GetString() ?? "";
            if (e.TryGetProperty("held", out var held) && held.ValueKind == JsonValueKind.True) a = "stop";
            double D(string n, double d = 0) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.Number ? v.GetDouble() : d;
            bool B(string n) => e.TryGetProperty(n, out var v) && v.ValueKind == JsonValueKind.True;
            string S(string n) => e.TryGetProperty(n, out var v) ? v.GetString() ?? "" : "";
            return new Order(route.GetInt32(), a, D("amount"), S("kind"), S("name"), D("x"), D("y"), D("heading"), B("following"), (int)D("stopsLeft"));
        }
        catch (JsonException) { return null; }
    }

    public string Describe() => IsTurn ? $"{Action} {Amount.ToString("0.00", CultureInfo.InvariantCulture)} rad"
        : IsMove ? $"{Action} {Amount.ToString("0.00", CultureInfo.InvariantCulture)} m to {(Kind == "door" || Kind == "opening" ? Name : Kind)}@{X.ToString("0.##", CultureInfo.InvariantCulture)},{Y.ToString("0.##", CultureInfo.InvariantCulture)}"
        : Action;
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
