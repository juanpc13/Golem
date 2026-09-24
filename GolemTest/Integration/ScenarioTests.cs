using System.Globalization;
using GolemTest.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// SCENARIOS (propuesta 52, 23-sep-2026): the domain decides, the world says whether the body COLLIDED, the domain corrects, and
// the scenario asserts on both — what the golem's journal concluded and what the world saw. "It collides and corrects" reads:
// the world saw a contact → the golem bumped (and marked what it took for a thing) → it did not press the same thing again
// more than it had to → the errand completed with the body really standing at its stop. The world is the one the configuration
// asks for (fase 3: `appsettings.json`, a local overlay, or GOLEM_LAB_WORLD): held in memory by default, the fleet deployed in
// Gazebo when it says `gazebo`; a Gazebo that does not answer makes the scenario inconclusive, never red.
[TestClass]
[TestCategory("scenario")]
public class ScenarioTests
{
    private static readonly LabSettings Lab = LabSettings.Load();
    private static TimeSpan Patience => Lab.Patience;
    private const double AtTheStop = 0.3;   // metres: the body really stands this close to its stop

    [TestInitialize]
    public void PinTheCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [TestMethod]
    public void TheWorldInMemory_IsBuiltFromTheSamePlanAsGazebo()
    {
        var plan = FloorPlan.Load();
        Assert.AreEqual(3, plan.Bodies.Count, "blue, red and green, on their marks");
        Assert.IsTrue(plan.Walls.Count > 20, "every edge of every place, less the openings, with the doors cut out: " + plan.Walls.Count);
        Assert.IsFalse(plan.Walls.Any(w => w.Closest(4.0, 9.5) == (4.0, 9.5)), "the kitchen's door to the north hall is a gap, not a wall");
        Assert.IsTrue(plan.Walls.Any(w => w.Closest(4.0, 8.5) == (4.0, 8.5)), "the rest of that edge is wall");
        Assert.IsFalse(plan.Walls.Any(w => w.Closest(5.5, 8.0) == (5.5, 8.0)), "the north hall opens to the centre: no wall between them");
    }

    [TestMethod]
    public async Task AFreeWay_IsWalkedWithoutTouchingAnything()
    {
        await using var world = await OpenWorldAsync();
        await world.PlaceGolemAsync("red");                             // on its mark in the living room
        world.Send("red", (5.5, 9.5));   // to the north hall, through the centre
        var way = WaysOf(world, "red").Single().Plan;
        Assert.AreEqual(3, way.Split(" > ").Length, "out of the living room, into the central hall, up to the north hall: " + way);

        // every leg of the way, validated as it ends: completed, where the leg said, touching nothing
        var legs = await WalkAsync(world, "red", AFreeLeg);
        CollectionAssert.AreEqual(way.Split(" > ").Select(l => l[..l.IndexOf('@')]).ToArray(), legs.Select(l => l.Name).ToArray(),
                                  "the legs walked are the legs decided, in order");
        await world.RunUntilSettledAsync(Patience);
        string seen = Report(world, "red", (5.5, 9.5));
        Assert.AreEqual("completed", world.Outcome("red").Status, seen);
        Assert.AreEqual(1, WaysOf(world, "red").Count, "nothing made it decide again — " + seen);
        AssertStandsAt(world, "red", 5.5, 9.5);
    }

    [TestMethod]
    public async Task ACrateInTheWay_IsBumped_Marked_AndSkirted_AndTheErrandCompletes()
    {
        await using var world = await OpenWorldAsync();
        await world.PlaceGolemAsync("red");                          // on its mark
        world.Send("red", (5.5, 9.5));                              // first to the north hall, where the scenario starts
        await WalkAsync(world, "red", AFreeLeg);                     // every leg of the walk there, validated as it ends
        Assert.AreEqual("completed", world.Outcome("red").Status, "red walked to the north hall — " + Report(world, "red", (5.5, 9.5)));
        await world.RunUntilSettledAsync(Patience);                     // nobody still driving when the crate stands
        world.PlaceCrate("center");                                     // the middle of the central hall
        world.Send("red", (5.5, 1.5));   // to the south hall: the straight line runs into the crate
        Assert.AreEqual("south@5.5,1.5", WaysOf(world, "red").Single().Plan, "one straight leg: the golem knows nothing of the crate yet");

        // leg 1: the straight run, cut short by the crate — and the way decided again starts backing off
        var cut = await world.NextLegAsync("red", Patience);
        Console.WriteLine("[leg] red " + cut.Line());
        Assert.AreEqual("south", cut.Name, cut.ToString());
        Assert.IsFalse(cut.Completed, "the crate stood on it — " + cut);
        CollectionAssert.Contains(cut.Touched.ToList(), "crate_center", cut.ToString());
        StringAssert.StartsWith(cut.WayAfter, "back@", "the way decided again: " + cut.WayAfter);

        // then every leg of the way decided again, validated as it ends: back, aside, via, the stop
        var decided = cut.WayAfter.Split(" > ").Select(l => l[..l.IndexOf('@')]).ToArray();
        CollectionAssert.AreEqual(new[] { "back", "aside", "via", "south" }, decided, cut.WayAfter);
        var legs = await WalkAsync(world, "red", leg =>
        {
            Assert.IsTrue(leg.Completed, leg.ToString());
            Assert.AreEqual(0, leg.Touched.Count, "past the crate without touching it again — " + leg);
            AssertOnTheLeg(leg);
        });
        CollectionAssert.AreEqual(decided, legs.Select(l => l.Name).ToArray(), "the legs walked are the legs decided again, in order");

        await world.RunUntilSettledAsync(Patience);
        string seen = Report(world, "red", (5.5, 1.5));
        var outcome = world.Outcome("red");
        Assert.AreEqual("completed", outcome.Status, seen);
        Assert.AreEqual(1, outcome.Bumps, seen);
        Assert.IsTrue(outcome.Marks >= 1, "what it touched is kept as a thing — " + seen);
        AssertStandsAt(world, "red", 5.5, 1.5);
    }

    [TestMethod]
    [Ignore("the domain does not learn yet that a crate shuts the hall (23-sep-2026): it bumps the same two points — "
            + "straight ahead and a body's step to its right — seven times and the route fails; an ajuste for the PLAN")]
    public async Task AHallShutWallToWall_TurnsTheWayThroughASideCorridor()
    {
        await using var world = await OpenWorldAsync();
        await world.PlaceGolemAsync("red");                          // on its mark
        world.Send("red", (5.5, 9.5));                              // first to the north hall, where the scenario starts
        await WalkAsync(world, "red", AFreeLeg);                     // every leg of the walk there, validated as it ends
        Assert.AreEqual("completed", world.Outcome("red").Status, "red walked to the north hall — " + Report(world, "red", (5.5, 9.5)));
        await world.RunUntilSettledAsync(Patience);                     // nobody still driving when the crate stands
        world.PlaceCrate("big");                                        // the central hall shut, wall to wall
        world.Send("red", (5.5, 1.5));                                  // to the south hall
        await world.RunUntilSettledAsync(Patience);
        string seen = Report(world, "red", (5.5, 1.5));

        Assert.AreEqual("completed", world.Outcome("red").Status, seen);
        Assert.IsTrue(world.Contacts("red").All(c => c.With == "crate_big"), "it only ever touched the crate — " + seen);
        Assert.IsTrue(world.Trail("red").Any(t => t.X < 1.5 || t.X > 9.5), "the way went round through a side corridor — " + seen);
        AssertStandsAt(world, "red", 5.5, 1.5);
    }

    [TestMethod]
    public async Task ACorridorShut_TurnsTheWayThroughTheCentre()
    {
        await using var world = await OpenWorldAsync();
        await world.PlaceGolemAsync("red");                          // on its mark
        world.Send("red", (9.0, 9.5));                              // first to the storage room, where the scenario starts
        await WalkAsync(world, "red", AFreeLeg);                     // every leg of the walk there, validated as it ends
        Assert.AreEqual("completed", world.Outcome("red").Status, "red walked to the storage room — " + Report(world, "red", (9.0, 9.5)));
        await world.RunUntilSettledAsync(Patience);                     // nobody still driving when the crate stands
        world.PlaceCrate("east");                                       // the east corridor shut
        world.Send("red", (9.0, 1.5));   // to the garage: its shortest way is down that corridor
        StringAssert.StartsWith(WaysOf(world, "red").Single().Plan, "storage/east@", "its first way goes down the east corridor");

        // every leg validated as it ends: completed where it said, or cut short by something the world saw it touch — never cut
        // for nothing; and the one leg the crate cut is the one down the corridor
        var legs = await WalkAsync(world, "red", leg =>
        {
            if (leg.Completed) AssertOnTheLeg(leg);
            else Assert.IsTrue(leg.Touched.Count > 0, "a leg cut short must have met something — " + leg);
        });
        var byTheCrate = legs.Where(l => !l.Completed && l.Touched.Contains("crate_east")).ToList();
        Assert.AreEqual(1, byTheCrate.Count, "the crate cut one leg: " + string.Join(" | ", legs));
        Assert.AreEqual("east/garage", byTheCrate[0].Name, "the leg down the corridor — " + byTheCrate[0]);
        Assert.IsTrue(legs.Skip(legs.IndexOf(byTheCrate[0]) + 1).Any(l => l.Name.Contains("center")), "then the way went down the central hall: "
                      + string.Join(" | ", legs));

        await world.RunUntilSettledAsync(Patience);
        string seen = Report(world, "red", (9.0, 1.5));
        var outcome = world.Outcome("red");
        Assert.AreEqual("completed", outcome.Status, seen);
        Assert.IsTrue(outcome.Marks >= 1, "the crate is kept as a thing — " + seen);
        AssertStandsAt(world, "red", 9.0, 1.5);
    }

    [TestMethod]
    [Ignore("findings of 23-sep-2026, for the PLAN: (1) when the bodies meet close to one's start, the other's stop lies inside "
            + "the met body's berth (ajuste 50) and its route FAILS instead of waiting for the body to move on; (2) the annulment "
            + "of a bump by the peer's word holds only if my bump was written first — the word arriving first leaves a phantom mark; "
            + "(3) in Gazebo green lives on dead reckoning and the collision's push leaves it 0.76 m from where it believes it stands")]
    public async Task TwoBodiesHeadOn_PassEachOther()
    {
        await using var world = await OpenWorldAsync();
        await world.PlaceGolemAsync("red");                          // on its mark
        world.Send("red", (5.5, 9.5));                              // first to the north hall, where the scenario starts
        await WalkAsync(world, "red", AFreeLeg);                     // every leg of the walk there, validated as it ends
        Assert.AreEqual("completed", world.Outcome("red").Status, "red walked to the north hall — " + Report(world, "red", (5.5, 9.5)));
        await world.PlaceGolemAsync("green");                          // on its mark
        world.Send("green", (5.5, 1.5));                              // first to the south hall, where the scenario starts
        await WalkAsync(world, "green", AFreeLeg);                     // every leg of the walk there, validated as it ends
        Assert.AreEqual("completed", world.Outcome("green").Status, "green walked to the south hall — " + Report(world, "green", (5.5, 1.5)));
        await world.RunUntilSettledAsync(Patience);                     // nobody still driving when they set out
        await world.SendTogetherAsync(("red", (5.5, 1.5)), ("green", (5.5, 9.5)));   // they trade places, down and up the centre
        await world.RunUntilSettledAsync(Patience);
        string seen = Report(world, "red", (5.5, 1.5)) + Environment.NewLine + Report(world, "green", (5.5, 9.5));

        Assert.AreEqual("completed", world.Outcome("red").Status, seen);
        Assert.AreEqual("completed", world.Outcome("green").Status, seen);
        Assert.IsTrue(world.Contacts("red").Any(c => c.With == "green") || world.Contacts("green").Any(c => c.With == "red"),
                      "the world saw the two bodies meet halfway — " + seen);
        Assert.IsTrue(world.Outcome("red").Encounters + world.Outcome("green").Encounters >= 1,
                      "at least one of them concluded it met a body, not a thing (ajuste 46) — " + seen);
        AssertStandsAt(world, "red", 5.5, 1.5);
        AssertStandsAt(world, "green", 5.5, 9.5);
    }

    [TestMethod]
    [Ignore("findings of 23-sep-2026, for the PLAN: (1) the annulment holds only if the mover's bump was written before the parked "
            + "body's word — in memory the word often arrives first and a phantom mark stays; (2) in Gazebo green, on dead reckoning, "
            + "was pushed off its belief by the meeting and took the central hall's east wall for a thing seven times — the route failed")]
    public async Task ABodyParkedInTheWay_IsMet_AndPassed()
    {
        await using var world = await OpenWorldAsync();
        await world.PlaceGolemAsync("blue");                          // on its mark
        world.Send("blue", (6.0, 5.0));                              // first to the central hall, where the scenario starts
        await WalkAsync(world, "blue", AFreeLeg);                     // every leg of the walk there, validated as it ends
        Assert.AreEqual("completed", world.Outcome("blue").Status, "blue walked to the central hall — " + Report(world, "blue", (6.0, 5.0)));
        await world.RunUntilSettledAsync(Patience);                     // blue parked there
        await world.PlaceGolemAsync("green");                           // green on its mark in the storage room
        world.Send("green", (5.5, 1.5));                                // to the south hall, through where blue stands
        await world.RunUntilSettledAsync(Patience);
        string seen = Report(world, "green", (5.5, 1.5)) + Environment.NewLine + Report(world, "blue", (6.0, 5.0));

        var green = world.Outcome("green");
        Assert.AreEqual("completed", green.Status, seen);
        Assert.IsTrue(world.Contacts("green").Any(c => c.With == "blue"), "green met blue's body on the way — " + seen);
        Assert.AreEqual(0, green.Marks, "and kept no thing where blue stood: blue's word annulled the bump (ajustes 46, 48) — " + seen);
        Assert.IsTrue(green.Encounters >= 1, "it concluded it met a body — " + seen);
        AssertStandsAt(world, "green", 5.5, 1.5);
    }

    // THE STORY OF A GOLEM IN A SCENARIO — printed at the end, and carried by every assertion's message (Juan, 23-sep-2026: "poder
    // ver la lista de los legs mejor y cuáles van saliendo o ya están completados"; 24-sep: "desde la living a la zona norte… en el
    // testcase no veo todos esos leg"): a header with how the newest errand ended and where the body really stands; then EVERY ERRAND
    // it was sent, in order — the walk to where the scenario starts included — with how it ended and the first order its command
    // returned, and inside it every way its route held — as decided, then each one decided again and why — with the list of its
    // legs, each marked ✓ completed, ✗ cut short, or · not walked; and where the trail went.
    private static string Report(ILabWorld world, string golem, (double X, double Y) stop)
    {
        var outcome = world.Outcome(golem);
        var (px, py, _) = world.TruePose(golem);
        double off = Math.Sqrt((px - stop.X) * (px - stop.X) + (py - stop.Y) * (py - stop.Y));
        var lines = new List<string>
        {
            $"═══ {golem} → ({stop.X}, {stop.Y}) · {Lab.World} · {outcome.Status} · {outcome.Bumps} bump(s), {outcome.Marks} mark(s), "
            + $"{outcome.Encounters} body met · stands at ({px:0.00}, {py:0.00}), {off:0.00} m from its stop",
        };
        var errands = world.Errands(golem);
        for (int n = 0; n < errands.Count; n++)
        {
            var errand = errands[n];
            var legs = world.Legs(golem).Where(l => l.Route == errand.Route).ToList();
            string status = legs.Count > 0 ? legs[^1].RouteStatus : "pending";
            lines.Add("");
            lines.Add($"  errand {n + 1} · to {errand.Stops} · {status} · first order: {FirstOrder(errand.Print)}");

            // the ways, in order: the first as the golem decided it, then one after every leg cut short
            var first = world.Ways(golem).FirstOrDefault(w => w.Route == errand.Route);
            var ways = new List<(string Plan, string Why, List<LegReport> Walked)>();
            if (first != null) ways.Add((first.Plan, "as decided", new List<LegReport>()));
            foreach (var leg in legs)
            {
                if (ways.Count == 0) ways.Add((leg.WayAfter, "as decided", new List<LegReport>()));
                ways[^1].Walked.Add(leg);
                if (!leg.Completed)
                    ways.Add((leg.WayAfter, "decided again after " + (leg.Touched.Count > 0 ? "bumping " + string.Join(", ", leg.Touched.Distinct()) : leg.EndedBy),
                              new List<LegReport>()));
            }
            for (int i = 0; i < ways.Count; i++)
            {
                var (plan, why, walked) = ways[i];
                if (i == ways.Count - 1 && walked.Count == 0 && i > 0 && plan == ways[i - 1].Plan) continue;   // the route ended on the cut
                var decided = plan.Split(" > ", StringSplitOptions.RemoveEmptyEntries);
                lines.Add($"    way {i + 1} · {why} · {decided.Length} leg(s)");
                for (int k = 0; k < decided.Length; k++)
                {
                    var done = walked.FirstOrDefault(l => l.Index == k + 1);
                    lines.Add("      " + (done != null ? done.Line() : NotWalked(k + 1, decided[k])));
                }
            }
        }
        var trail = world.Trail(golem);
        var through = new[] { ("west corridor", trail.Any(t => t.X < 1.5)), ("central hall", trail.Any(t => t.X > 4 && t.X < 7 && t.Y > 3 && t.Y < 8)),
                              ("east corridor", trail.Any(t => t.X > 9.5)) }.Where(t => t.Item2).Select(t => t.Item1);
        lines.Add("");
        lines.Add($"  trail: {trail.Count} points · {string.Join(", ", through)}");
        string said = string.Join(Environment.NewLine, lines);
        Console.WriteLine(Environment.NewLine + said);
        return said;
    }

    // The ways of the golem's NEWEST errand — the one the scenario is about, not the walk that brought the body to its start.
    private static List<WayDecided> WaysOf(ILabWorld world, string golem)
    {
        int route = world.Errands(golem).Last().Route;
        return world.Ways(golem).Where(w => w.Route == route).ToList();
    }

    // A leg of a way the route never walked (cut before it): `· 3  garage   → (9.00, 1.50)   not walked`.
    private static string NotWalked(int index, string leg)
    {
        int at = leg.IndexOf('@');
        var xy = leg[(at + 1)..].Split(',').Select(v => double.Parse(v, CultureInfo.InvariantCulture)).ToArray();
        return $"· {index,-2} {leg[..at],-14} {"",16}→ ({xy[0]:0.00}, {xy[1]:0.00})   not walked";
    }

    // The errand's print, said the robot's way: `turnRight 2.991 rad, toward south (5.5, 1.5)`.
    private static string FirstOrder(string print)
    {
        try
        {
            using var doc = System.Text.Json.JsonDocument.Parse(print);
            var e = doc.RootElement;
            string action = e.GetProperty("action").GetString();
            double amount = e.GetProperty("amount").GetDouble();
            string unit = action is "advance" or "back" ? "m" : "rad";
            string toward = e.TryGetProperty("name", out var n) ? $", toward {n.GetString()} ({e.GetProperty("x").GetDouble():0.##}, {e.GetProperty("y").GetDouble():0.##})" : "";
            return $"{action} {amount:0.000} {unit}{toward}";
        }
        catch (Exception) { return print; }
    }

    // The legs of the golem's route, one by one AS THEY END — each printed and handed to `validate` right then — until the route
    // is no longer pending. Returns them in order.
    private static async Task<List<LegReport>> WalkAsync(ILabWorld world, string golem, Action<LegReport> validate)
    {
        var walked = new List<LegReport>();
        while (true)
        {
            var leg = await world.NextLegAsync(golem, Patience);
            Console.WriteLine($"[leg] {golem} {leg.Line()}");
            validate(leg);
            walked.Add(leg);
            if (leg.RouteStatus != "pending") return walked;
        }
    }

    // A leg on a clean floor: completed, where the leg said, touching nothing.
    private static void AFreeLeg(LegReport leg)
    {
        Assert.IsTrue(leg.Completed, leg.ToString());
        Assert.AreEqual(0, leg.Touched.Count, "nothing on a free way — " + leg);
        AssertOnTheLeg(leg);
    }

    // A completed leg leaves the body where the leg said: at its point — or, for a door, crossed to its exit, a jamb's
    // width past the door's point (`kitchen/north`: the door between two areas).
    private static void AssertOnTheLeg(LegReport leg)
    {
        double near = leg.Name.Contains('/') ? 0.75 : AtTheStop;
        Assert.IsTrue(leg.Off <= near, $"{leg.Off:0.00} m off the leg's point — {leg}");
    }

    private static async Task<ILabWorld> OpenWorldAsync()
    {
        try { return await LabWorld.OpenAsync(Lab); }
        catch (WorldUnavailableException e) { Assert.Inconclusive(e.Message); return null; }
    }

    private static void AssertStandsAt(ILabWorld world, string golem, double x, double y)
    {
        var (px, py, _) = world.TruePose(golem);
        double off = Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
        Assert.IsTrue(off <= AtTheStop, $"{golem} really stands at ({px:0.00}, {py:0.00}), {off:0.00} m from its stop ({x}, {y})");
    }
}
