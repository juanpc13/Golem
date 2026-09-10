using System.Globalization;
using Choreography.Theater;
using GolemDomain;
using GolemDomain.Plans;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;

namespace GolemTest;

// The catalog of named floor plans (FloorPlans): each one is built with the domain's own classes and reaches a
// golem's journal as the release text it renders — so the journal, not the code, keeps the map. Every test
// charts one plan into a fresh actor through the perform and asks the golem about it.
[TestClass]
public class FloorPlanCatalogTests
{
    private PerformanceV2 perf;

    [TestInitialize]
    public void PinTheCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [TestCleanup]
    public void TheGolemRests() => perf?.Dispose();

    [TestMethod]
    public void TheArena_RendersTheReleaseTheHostCarries_OneChainPerPlace()
    {
        string release = FloorPlans.Arena().AsRelease("map_v1");

        StringAssert.StartsWith(release, "upgrade('map_v1') {");
        StringAssert.Contains(release, "g.Chart('kitchen', 0, 8, 4, 3).DoorTo('north', 4, 9.5).DoorTo('west', 0.75, 8);");
        StringAssert.Contains(release, "g.Chart('north', 4, 8, 3, 3).DoorTo('storage', 7, 9.5).OpenTo('center');");
        StringAssert.Contains(release, "g.Chart('storage', 7, 8, 4, 3).DoorTo('east', 10.25, 8);");
        StringAssert.Contains(release, "g.Chart('garage', 7, 0, 4, 3);");

        Born(release);
        Assert.AreEqual(9, Int("g.PlaceCount()"));
        Assert.AreEqual(10, Int("g.PassageCount()"), "eight doors and two open boundaries, as the host's map_v1");
        Assert.AreEqual("center", Text("g.PlaceAt(5.5, 5.5)"));
    }

    [TestMethod]
    public void CrossCorridors_FourRoomsInTheCorners_TwoAislesThatCross()
    {
        Born(FloorPlans.Named("cross-corridors").AsRelease("map_v1"));

        Assert.AreEqual(9, Int("g.PlaceCount()"), "four rooms, four aisles, the crossing");
        Assert.AreEqual(12, Int("g.PassageCount()"), "eight doors, four open boundaries around the crossing");
        Assert.IsFalse(Bool("g.IsOnMap(4.0, 5.5)") && Bool("g.IsOnMap(-1.0, 5.5)"), "the floor is 11 x 11");
        Assert.AreEqual("crossing", Text("g.PlaceAt(5.5, 5.5)"));

        // from the northwest room to the southeast one: out a door into an aisle, through the crossing (two open
        // boundaries), along the other aisle and in through a door — never through a wall. The two ways round
        // (north then east, west then south) are the same length: the planner may take either.
        Visit(1, "southeast");
        string plan = Text("g.Plan(1, 2.375, 8.625)");
        StringAssert.StartsWith(plan, "northwest/");
        Assert.AreEqual(2, plan.Split("crossing~").Length - 1, "in through one aisle, out through the other: " + plan);
        StringAssert.EndsWith(plan, "> southeast@8.63,2.38");
        Assert.AreEqual(2, plan.Split('/').Length - 1, "exactly two doors: " + plan);
        // the two aisles a room opens to: a corner room reaches its diagonal opposite only through the crossing
        double diagonal = Double("g.Distance('northwest', 'southeast')");
        double straight = Math.Sqrt(6.25 * 6.25 * 2);   // ~8.84, through the crossing's corner: not a road
        Assert.IsTrue(diagonal > straight + 1.5 && diagonal < 11.5, "door, aisle, crossing, aisle, door — a bit over ten metres: " + diagonal);
    }

    [TestMethod]
    public void RingCorridor_FourRoomsInTheMiddle_OneCorridorAllTheWayRound()
    {
        Born(FloorPlans.Named("ring-corridor").AsRelease("map_v1"));

        Assert.AreEqual(8, Int("g.PlaceCount()"), "four rooms, four stretches of corridor");
        Assert.AreEqual(16, Int("g.PassageCount()"), "eight doors to the corridor, four between the rooms, four open stretches");
        Assert.AreEqual("west-corridor", Text("g.PlaceAt(0.75, 5.5)"));

        // neighbouring rooms are joined directly: center to center through the door they share
        Assert.AreEqual(4.0, Double("g.Distance('northwest', 'northeast')"), 0.01, "two metres to the door, two more to the neighbour's center");
        // the corridor runs all the way round: from one stretch into the next through an open boundary, no door
        Visit(1, new[] { "9,10.25" });                    // the east end of the north corridor
        string plan = Text("g.Plan(1, 0.75, 10.25)");       // from the northwest corner of the ring
        StringAssert.StartsWith(plan, "west-corridor~north-corridor@1.5,10.2");
        StringAssert.EndsWith(plan, "> north-corridor@9,10.25");
        Assert.IsFalse(plan.Contains('/'), "along the corridor, through no door: " + plan);
        // and cutting through a room beats going round when it is shorter: the planner takes the rooms' doors
        Visit(2, "east-corridor");
        string across = Text("g.Plan(2, 0.75, 10.25)");
        StringAssert.Contains(across, "northeast/north-corridor@7.5,9.5 > northeast/east-corridor@9.5,7.5", "through the northeast room: " + across);
    }

    [TestMethod]
    public void TheCatalog_NamesItsPlans_AndRefusesAnUnknownOne()
    {
        CollectionAssert.AreEqual(new[] { "arena", "cross-corridors", "ring-corridor" }, FloorPlans.Names());
        var refused = Assert.ThrowsException<DomainException>(() => FloorPlans.Named("attic"));
        StringAssert.Contains(refused.Message, "no floor plan named 'attic'");
    }

    // ---- helpers ----

    private void Born(string mapRelease)
    {
        string name = "golem-under-test-" + Guid.NewGuid().ToString("N");
        perf = new PerformanceV2(name, DomainLibrary.Assembly);
        perf.ConfigureStorage(DatabaseType.IN_MEMORY, name);
        perf.Start();
        perf.Actor.Using(@"
            upgrade('init') {
                g = Golem();
                g.Embody(0.25);
                g.Cruise(2.0);
                g.Linger(6);
            }
        " + mapRelease)
        .PerformCommand();
    }

    private void Visit(int id, string[] stops) =>
        perf.Actor.Using(@"
            g.Visit(@id, @stops);
        ")
        .WithParameters(p => {
            p["id",    typeof(int)]      = id;
            p["stops", typeof(string[])] = stops;
        })
        .PerformCommand();

    private void Visit(int id, string place) =>
        perf.Actor.Using(@"
            g.Visit(@id, @place);
        ")
        .WithParameters(p => {
            p["id",    typeof(int)]    = id;
            p["place", typeof(string)] = place;
        })
        .PerformCommand();

    private int Int(string expression)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using($"@value = {expression};")
        .WithParameters(rented, p => {
            p[Parameter.Out, "value", typeof(int)] = default;
        })
        .PerformQuery();
        return rented["value"].GetValue<int>();
    }

    private double Double(string expression)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using($"@value = {expression};")
        .WithParameters(rented, p => {
            p[Parameter.Out, "value", typeof(double)] = default;
        })
        .PerformQuery();
        return rented["value"].GetValue<double>();
    }

    private bool Bool(string expression)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using($"@value = {expression};")
        .WithParameters(rented, p => {
            p[Parameter.Out, "value", typeof(bool)] = default;
        })
        .PerformQuery();
        return rented["value"].GetValue<bool>();
    }

    private string Text(string expression)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using($"@value = {expression};")
        .WithParameters(rented, p => {
            p[Parameter.Out, "value", typeof(string)] = default;
        })
        .PerformQuery();
        return rented["value"].GetValue<string>();
    }
}
