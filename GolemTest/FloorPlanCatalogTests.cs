using System.Globalization;
using Choreography.Theater;
using GolemDomain;
using GolemDomain.Layouts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;

namespace GolemTest;

// The catalog of named maps (Catalog): each one is built with the domain's own class and reaches a golem's
// journal as the release it renders — so the journal, not the code, keeps the map. Every test
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
    public void TheWarehouse_RendersTheReleaseTheHostCarries_EachAreaFoundOnceAndToldInOneTrain()
    {
        string release = Catalog.Warehouse().AsRelease();

        // one concrete map builds everything: the area, where it stands, how big it is, its doors and what it opens to
        StringAssert.StartsWith(release, "upgrade('warehouse_v1') {\n    map = MapLayout('warehouse');");
        StringAssert.Contains(release, "    map.Area('kitchen').At(Position(0.0, 8.0)).Size(4.0, 3.0).DoorAt('north', Position(4.0, 9.5)).DoorAt('west', Position(0.75, 8.0));\n");
        StringAssert.Contains(release, "    map.Area('north').At(Position(4.0, 8.0)).Size(3.0, 3.0).DoorAt('storage', Position(7.0, 9.5)).OpenTo('center');\n");
        StringAssert.Contains(release, "    map.Area('west').At(Position(0.0, 3.0)).Size(1.5, 5.0).DoorAt('living', Position(0.75, 3.0));\n");
        StringAssert.Contains(release, "    map.Area('garage').At(Position(7.0, 0.0)).Size(4.0, 3.0);\n");
        Assert.IsFalse(release.Contains("g."), "the golem writes nothing here: the map builds itself, and the golem receives it");

        Born(release);
        Assert.AreEqual(9, Int("g.PlaceCount()"));
        Assert.AreEqual(10, Int("g.PassageCount()"), "eight doors and two openings, as the host's warehouse");
        Assert.AreEqual("center", Text("g.PlaceAt(5.5, 5.5).Name"));
        // and the map answers on its own, without the golem — as a maquette and as a layout, because it is both
        Assert.AreEqual(9, Int("map.AreaCount"), "what the maquette disposes");
        Assert.AreEqual(2, Int("map.Find('kitchen').Neighbours().Count"), "the kitchen connects to two areas");
        Assert.IsTrue(Bool("map.Connects(map.Find('north'), map.Find('center'))"), "connected by an opening: information");
        Assert.IsTrue(Bool("map.Touches(map.Find('north'), map.Find('center'))"), "and actually sharing an edge on the plane: geometry");
        Assert.IsFalse(Bool("map.Connects(map.Find('kitchen'), map.Find('garage'))"));
        Assert.AreEqual(9, Int("map.ZoneCount"), "every area laid out");
        Assert.AreEqual("north", Text("map.ZoneOf(Position(5.5, 9.5)).Name"));
        Assert.AreEqual(4.0, Double("map.Find('kitchen').Width"), 1e-9, "found once, read as the zone it is");
        Assert.AreEqual(0.25, Double("body.Radius"), 1e-9, "and the body");
    }

    [TestMethod]
    public void CrossCorridors_FourRoomsInTheCorners_TwoAislesThatCross()
    {
        Born(Catalog.Named("cross-corridors").AsRelease());

        Assert.AreEqual(9, Int("g.PlaceCount()"), "four rooms, four aisles, the crossing");
        Assert.AreEqual(12, Int("g.PassageCount()"), "eight doors, four open boundaries around the crossing");
        Assert.IsFalse(Bool("g.IsOnMap(4.0, 5.5)") && Bool("g.IsOnMap(-1.0, 5.5)"), "the floor is 11 x 11");
        Assert.AreEqual("crossing", Text("g.PlaceAt(5.5, 5.5).Name"));

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
        double diagonal = Double("g.Distance(map.Find('northwest'), map.Find('southeast'))");
        double straight = Math.Sqrt(6.25 * 6.25 * 2);   // ~8.84, through the crossing's corner: not a road
        Assert.IsTrue(diagonal > straight + 1.5 && diagonal < 11.5, "door, aisle, crossing, aisle, door — a bit over ten metres: " + diagonal);
    }

    [TestMethod]
    public void RingCorridor_FourRoomsInTheMiddle_OneCorridorAllTheWayRound()
    {
        Born(Catalog.Named("ring-corridor").AsRelease());

        Assert.AreEqual(8, Int("g.PlaceCount()"), "four rooms, four stretches of corridor");
        Assert.AreEqual(16, Int("g.PassageCount()"), "eight doors to the corridor, four between the rooms, four open stretches");
        Assert.AreEqual("west-corridor", Text("g.PlaceAt(0.75, 5.5).Name"));

        // neighbouring rooms are joined directly: center to center through the door they share
        Assert.AreEqual(4.0, Double("g.Distance(map.Find('northwest'), map.Find('northeast'))"), 0.01, "two metres to the door, two more to the neighbour's center");
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
        CollectionAssert.AreEqual(new[] { "warehouse", "cross-corridors", "ring-corridor" }, Catalog.Names());
        var refused = Assert.ThrowsException<DomainException>(() => Catalog.Named("attic"));
        StringAssert.Contains(refused.Message, "no map named 'attic'");
    }

    // ---- helpers ----

    private void Born(string mapRelease)
    {
        string name = "golem-under-test-" + Guid.NewGuid().ToString("N");
        perf = new PerformanceV2(name, DomainLibrary.Assembly);
        perf.ConfigureStorage(DatabaseType.IN_MEMORY, name);
        perf.Start();
        perf.Actor.Using(
            "upgrade('body_v1') { body = Body(0.25, 2.0, 6.0); }\n"
            + mapRelease
            + "upgrade('init') { collisions = Collisions(map); g = Golem(body, map, collisions); }\n")
        .PerformCommand();
    }

    private void Visit(int id, string[] stops)
    {
        var script = new System.Text.StringBuilder();
        for (int i = 0; i < stops.Length; i++)
            script.Append(stops[i].Contains(',') ? $"g.Visit(@id, Position(@x{i}, @y{i}));\n" : $"g.Visit(@id, map.Find(@p{i}));\n");
        perf.Actor.Using(script.ToString())
        .WithParameters(p => {
            p["id", typeof(int)] = id;
            for (int i = 0; i < stops.Length; i++)
            {
                if (stops[i].Contains(','))
                {
                    var xy = stops[i].Split(',');
                    p[$"x{i}", typeof(double)] = double.Parse(xy[0], CultureInfo.InvariantCulture);
                    p[$"y{i}", typeof(double)] = double.Parse(xy[1], CultureInfo.InvariantCulture);
                }
                else p[$"p{i}", typeof(string)] = stops[i];
            }
        })
        .PerformCommand();
    }

    private void Visit(int id, string place) =>
        perf.Actor.Using(@"
            g.Visit(@id, map.Find(@place));
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
