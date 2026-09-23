using System.Globalization;
using GolemTest.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// SCENARIOS (propuesta 52, 23-sep-2026): the domain decides, the world says whether the body COLLIDED, the domain corrects, and
// the scenario asserts on both — what the golem's journal concluded and what the world saw. "It collides and corrects" reads:
// the world saw a contact → the golem bumped (and marked what it took for a thing) → it did not press the same thing again
// more than it had to → the errand completed with the body really standing at its stop. The world is the one held in memory
// (fase 1) unless GOLEM_LAB_WORLD=gazebo asks for the fleet deployed in Gazebo (fase 2); a Gazebo that does not answer makes
// the scenario inconclusive, never red.
[TestClass]
[TestCategory("scenario")]
public class ScenarioTests
{
    private static bool InGazebo => string.Equals(Environment.GetEnvironmentVariable("GOLEM_LAB_WORLD"), "gazebo", StringComparison.OrdinalIgnoreCase);
    private static TimeSpan Patience => InGazebo ? TimeSpan.FromSeconds(180) : TimeSpan.FromSeconds(60);
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
        world.Send("red", (5.5, 9.5));                                  // to the north hall, through the centre
        await world.RunUntilSettledAsync(Patience);

        var outcome = world.Outcome("red");
        Assert.AreEqual("completed", outcome.Status, outcome.ToString());
        Assert.AreEqual(0, world.Contacts("red").Count, "the world saw no contact: " + string.Join("; ", world.Contacts("red")));
        Assert.AreEqual(0, outcome.Bumps);
        AssertStandsAt(world, "red", 5.5, 9.5);
    }

    [TestMethod]
    public async Task ACrateInTheWay_IsBumped_Marked_AndSkirted_AndTheErrandCompletes()
    {
        await using var world = await OpenWorldAsync();
        await world.PlaceGolemAsync("red", (5.5, 9.5));                 // in the north hall, before the crate stands
        world.PlaceCrate("center");                                     // the middle of the central hall
        world.Send("red", (5.5, 1.5));                                  // to the south hall: the straight line runs into the crate
        await world.RunUntilSettledAsync(Patience);

        var outcome = world.Outcome("red");
        var touches = world.Contacts("red");
        Assert.AreEqual("completed", outcome.Status, outcome + " — " + string.Join("; ", touches));
        Assert.IsTrue(touches.Count >= 1, "the world saw the body meet the crate");
        Assert.IsTrue(touches.All(t => t.With == "crate_center"), "and nothing but the crate: " + string.Join("; ", touches));
        Assert.IsTrue(touches.Count <= 2, "once head-on, at most once more while it learns the crate's width: " + string.Join("; ", touches));
        Assert.AreEqual(touches.Count, outcome.Bumps, "every contact the world saw is a bump the golem wrote");
        Assert.IsTrue(outcome.Marks >= 1, "what it touched is kept as a thing");
        AssertStandsAt(world, "red", 5.5, 1.5);
    }

    private static async Task<ILabWorld> OpenWorldAsync()
    {
        if (!InGazebo) return new FloorWorld();
        try
        {
            return await GazeboWorld.OpenAsync(new Uri("ws://localhost:9090"), new Dictionary<string, Uri>
            {
                ["blue"] = new Uri("http://localhost:8081/"),
                ["red"] = new Uri("http://localhost:8082/"),
                ["green"] = new Uri("http://localhost:8083/"),
            }, Patience);
        }
        catch (WorldUnavailableException e) { Assert.Inconclusive(e.Message); return null; }
    }

    private static void AssertStandsAt(ILabWorld world, string golem, double x, double y)
    {
        var (px, py, _) = world.TruePose(golem);
        Console.WriteLine($"[scenario] {(InGazebo ? "gazebo" : "memory")} — {golem}: {world.Outcome(golem)}; stands at ({px:0.00}, {py:0.00}); "
                          + $"contacts: {string.Join(" | ", world.Contacts(golem).Select(c => $"{c.With} at ({c.BodyX:0.00}, {c.BodyY:0.00}) bearing {c.Bearing:0.00}"))}");
        double off = Math.Sqrt((px - x) * (px - x) + (py - y) * (py - y));
        Assert.IsTrue(off <= AtTheStop, $"{golem} really stands at ({px:0.00}, {py:0.00}), {off:0.00} m from its stop ({x}, {y})");
    }
}
