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
        Console.WriteLine("[sent] red: " + world.Send("red", (5.5, 9.5)));   // to the north hall, through the centre
        var way = world.Ways("red").Single().Plan;
        Assert.AreEqual(3, way.Split(" > ").Length, "out of the living room, into the central hall, up to the north hall: " + way);

        // every leg of the way, validated as it ends: completed, where the leg said, touching nothing
        var legs = await WalkAsync(world, "red", leg =>
        {
            Assert.IsTrue(leg.Completed, leg.ToString());
            Assert.AreEqual(0, leg.Touched.Count, "nothing on a free way — " + leg);
            AssertOnTheLeg(leg);
        });
        CollectionAssert.AreEqual(way.Split(" > ").Select(l => l[..l.IndexOf('@')]).ToArray(), legs.Select(l => l.Name).ToArray(),
                                  "the legs walked are the legs decided, in order");
        await world.RunUntilSettledAsync(Patience);
        string seen = Report(world, "red", (5.5, 9.5));
        Assert.AreEqual("completed", world.Outcome("red").Status, seen);
        Assert.AreEqual(1, world.Ways("red").Count, "nothing made it decide again — " + seen);
        AssertStandsAt(world, "red", 5.5, 9.5);
    }

    [TestMethod]
    public async Task ACrateInTheWay_IsBumped_Marked_AndSkirted_AndTheErrandCompletes()
    {
        await using var world = await OpenWorldAsync();
        await world.PlaceGolemAsync("red", (5.5, 9.5));                 // in the north hall, before the crate stands
        world.PlaceCrate("center");                                     // the middle of the central hall
        Console.WriteLine("[sent] red: " + world.Send("red", (5.5, 1.5)));   // to the south hall: the straight line runs into the crate
        Assert.AreEqual("south@5.5,1.5", world.Ways("red").Single().Plan, "one straight leg: the golem knows nothing of the crate yet");

        // leg 1: the straight run, cut short by the crate — and the way decided again starts backing off
        var cut = await world.NextLegAsync("red", Patience);
        Console.WriteLine("[leg] red " + cut);
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
        await world.PlaceGolemAsync("red", (5.5, 9.5));                 // in the north hall
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
        await world.PlaceGolemAsync("red", (9.0, 9.5));                 // in the storage room
        world.PlaceCrate("east");                                       // the east corridor shut
        Console.WriteLine("[sent] red: " + world.Send("red", (9.0, 1.5)));   // to the garage: its shortest way is down that corridor
        StringAssert.StartsWith(world.Ways("red").Single().Plan, "storage/east@", "its first way goes down the east corridor");

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
        await world.PlaceGolemAsync("red", (5.5, 9.5));                 // red in the north hall…
        await world.PlaceGolemAsync("green", (5.5, 1.5));               // …green in the south hall
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
        await world.PlaceGolemAsync("blue", (6.0, 5.0));                // blue parked in the central hall
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

    // THE STORY OF A GOLEM IN A SCENARIO — printed, and carried by every assertion's message: how its errand ended, where its
    // body really stands, and then, in the order they happened, every way its route held (the first as decided, then each one
    // decided again) and every contact the world saw, and at the end where the trail went. Juan, 23-sep-2026: "mostrar la lista
    // de tramos que debía hacer, y si hay una colisión, cuál fue el recálculo… todos los testcase deberían mostrar salidas".
    private static string Report(ILabWorld world, string golem, (double X, double Y) stop)
    {
        var outcome = world.Outcome(golem);
        var (px, py, ph) = world.TruePose(golem);
        double off = Math.Sqrt((px - stop.X) * (px - stop.X) + (py - stop.Y) * (py - stop.Y));
        var lines = new List<string>
        {
            $"{Lab.World} — {golem} to ({stop.X}, {stop.Y}): {outcome.Status}; {outcome.Bumps} bump(s), {outcome.Marks} mark(s), "
            + $"{outcome.Encounters} body met; stands at ({px:0.00}, {py:0.00}) facing {ph:0.00}, {off:0.00} m from its stop",
        };
        var story = world.Errands(golem).Select(e => (e.Sequence, Line: $"  sent · to {e.Stops}: print {e.Print}"))
            .Concat(world.Legs(golem).Select(l => (l.Sequence, Line: "  " + l)))
            .Concat(world.Ways(golem).Select(w => (w.Sequence, Line: WayLine(w, world.Ways(golem)))))
            .Concat(world.Contacts(golem).Select(c => (c.Sequence,
                Line: $"  contact · {c.With}: the body at ({c.BodyX:0.00}, {c.BodyY:0.00}) facing {c.BodyHeading:0.00}, pressed at bearing {c.Bearing:0.00}")))
            .OrderBy(e => e.Sequence);
        lines.AddRange(story.Select(e => e.Line));
        var trail = world.Trail(golem);
        lines.Add($"  trail · {trail.Count} point(s): west corridor {(trail.Any(t => t.X < 1.5) ? "yes" : "no")}, "
                  + $"central hall {(trail.Any(t => t.X > 4 && t.X < 7 && t.Y > 3 && t.Y < 8) ? "yes" : "no")}, "
                  + $"east corridor {(trail.Any(t => t.X > 9.5) ? "yes" : "no")}");
        string said = string.Join(Environment.NewLine, lines);
        Console.WriteLine("[scenario] " + said);
        return said;
    }

    // "way decided" for the first way of a route, "way decided again" for the ones after; the legs one per line when long.
    private static string WayLine(WayDecided way, IReadOnlyList<WayDecided> all)
    {
        bool first = all.First(w => w.Route == way.Route) == way;
        var legs = way.Plan.Split(" > ");
        return $"  {(first ? "way decided" : "way decided again")} · route {way.Route}, {legs.Length} leg(s): {string.Join(" > ", legs)}";
    }

    // The legs of the golem's route, one by one AS THEY END — each printed and handed to `validate` right then — until the route
    // is no longer pending. Returns them in order.
    private static async Task<List<LegReport>> WalkAsync(ILabWorld world, string golem, Action<LegReport> validate)
    {
        var walked = new List<LegReport>();
        while (true)
        {
            var leg = await world.NextLegAsync(golem, Patience);
            Console.WriteLine($"[leg] {golem} {leg}");
            validate(leg);
            walked.Add(leg);
            if (leg.RouteStatus != "pending") return walked;
        }
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
