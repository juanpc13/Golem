using System.Globalization;
using Choreography.Theater;
using GolemDomain;
using GolemDomain.Layouts;
using Microsoft.VisualStudio.TestTools.UnitTesting;
using Puppeteer;

namespace GolemTest;

// WHAT ONLY THE ENGINE CAN SHOW (Juan, 21-sep-2026: the domain is tested as objects; a script belongs where the engine is
// what is being proved): that the releases the host carries build the three globals in a journal, that the panel's queries
// render the domain's objects as the JSON the panel paints from, that a domain refusal reaches a command in the domain's own
// words, and that an errand written as the host writes it prints the robot's words. A real actor, an in-memory journal.
[TestClass]
public class JournalAcceptanceTests
{
    private PerformanceV2 perf;

    [TestInitialize]
    public void AGolemIsBorn()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
        string name = "golem-under-test-" + Guid.NewGuid().ToString("N");
        perf = new PerformanceV2(name, DomainLibrary.Assembly);
        perf.ConfigureStorage(DatabaseType.IN_MEMORY, name);
        perf.Start();
        perf.Actor.Using(
            @"
                upgrade('body_v1') {
                    radius = Meters(0.25);
                    speed = MetersPerSecond(2.0);
                    linger = Seconds(6.0);
                    retreat = Meters(0.6);
                    body = Body(radius, speed, linger, retreat);
                }
            "
            + Catalog.Warehouse().AsRelease()
            + @"
                upgrade('init') {
                    g = Golem(body);
                    scenario = g.Stage(warehouse);
                    g.Enter(scenario);
                }
            ").PerformCommand();
    }

    [TestCleanup]
    public void TheGolemRests() => perf.Dispose();

    [TestMethod]
    public void TheReleases_BuildTheThreeGlobals_AndTheMapsVariablesDieWithTheirBlock()
    {
        Assert.AreEqual(0.25, Double("body.Radius.InMeters"), 1e-9, "the body, a magnitude read in its base unit");
        Assert.AreEqual(9, Int("g.Current.Map.ZoneCount"), "the map, laid out");
        Assert.AreEqual(10, Int("g.Current.Map.PassageCount"));
        Assert.AreEqual(0, Int("g.Current.Collisions.MarkCount"), "the collisions module, empty");
        Assert.IsFalse(Bool("g.HasPendingMission()"), "the golem, idle");
        bool leaked = true;
        try { Text("kitchen.Name"); } catch (Exception) { leaked = false; }
        Assert.IsFalse(leaked, "the areas' variables die with their block: only body, the maps, the scenarios and g are globals");
        Assert.AreEqual("center", Text("g.Current.Map.ZoneAt(Position(5.5, 5.5)).Name"), "a query builds the object from literals with a decimal point");
    }

    [TestMethod]
    public void TheMagnitudes_AreTypedInTheJournal_ADurationWhereALengthGoesIsRefusedBeforeAnythingRuns()
    {
        Refuses("b = Body(Seconds(0.25), MetersPerSecond(2.0), Seconds(6.0), Meters(0.6));", "a value of type 'Length' is expected");
        Refuses("b = Body(Meters(0.0), MetersPerSecond(2.0), Seconds(6.0), Meters(0.6));", "Error while instantiating class 'Body'");
    }

    [TestMethod]
    public void ADomainRefusal_ReachesACommand_InTheDomainsOwnWords()
    {
        Refuses("{ point = Position(5.5, 9.5); g.Follow(point); }", "does not know where its body stands");
        Refuses("{ route = g.Pause(Pose(10.25, 6.1, -1.5708)); }", "no pending mission");
        Refuses("g.Visit(Position(2.0, 9.5), g.Current.Map.Find('attic'));", "attic");
    }

    [TestMethod]
    public void AnErrand_WrittenAsTheHostWritesIt_PrintsTheRobotsWords()
    {
        perf.Actor.Using("{ me = Pose(@px, @py, @ptheta); g.Wake(me); }")
            .WithParameters(p => { p["px", typeof(double)] = 6.3; p["py", typeof(double)] = 10.4; p["ptheta", typeof(double)] = 0.0; })
            .PerformCommand();   // the golem wakes on its mark, as the host wakes it: the errand then starts where the domain says (ajuste 67)
        string print = perf.Actor.Using(@"
            {
                from = g.Destination;
                point = Position(@x, @y);
                route = g.Visit(from, point);
                if (route.IsPending()) {
                    print route.Id 'route', route.Order 'action';
                    if (route.IsWalkable) {
                        print route.Amount 'amount', route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                              route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                              route.Following 'following', route.StopsLeft 'stopsLeft';
                    }
                } else {
                    print route.Id 'route', route.Status 'ended';
                    if (route.EndedShort) {
                        print route.Why 'why';
                    }
                }
            }
        ")
        .WithParameters(p => {
            p["x", typeof(double)] = 5.5; p["y", typeof(double)] = 1.5;
        })
        .PerformCommand();
        using var doc = System.Text.Json.JsonDocument.Parse(print);
        var e = doc.RootElement;
        Assert.AreEqual(1, e.GetProperty("route").GetInt32());
        Assert.AreEqual("turnRight", e.GetProperty("action").GetString(), "the print speaks the robot's words");
        Assert.IsTrue(e.GetProperty("amount").GetDouble() > 1.5, "radians to turn");
        Assert.AreEqual("stop", e.GetProperty("kind").GetString(), "north hall to south hall: one leg");
        Assert.AreEqual(5.5, e.GetProperty("x").GetDouble(), 1e-9);
        Assert.IsFalse(e.TryGetProperty("why", out _), "a pending route has no why to tell (ajuste 66)");
        Assert.IsFalse(e.TryGetProperty("ended", out _));
    }

    // THE ELSE (ajuste 66): a route no longer pending prints how it ended, and why only when it ended short — never an action or an amount.
    [TestMethod]
    public void ARouteThatEnded_PrintsHowItEnded_AndWhyOnlyWhenItEndedShort()
    {
        perf.Actor.Using("{ me = Pose(@px, @py, @ptheta); g.Wake(me); }")
            .WithParameters(p => { p["px", typeof(double)] = 6.3; p["py", typeof(double)] = 10.4; p["ptheta", typeof(double)] = 0.0; })
            .PerformCommand();   // the golem wakes on its mark, as the host wakes it: the errand then starts where the domain says (ajuste 67)
        string print = perf.Actor.Using(@"
            {
                from = g.Destination;
                point = Position(@x, @y);
                route = g.Visit(from, point);
                route.Abandon(@reason);
                if (route.IsPending()) {
                    print route.Id 'route', route.Order 'action';
                } else {
                    print route.Id 'route', route.Status 'ended';
                    if (route.EndedShort) {
                        print route.Why 'why';
                    }
                }
            }
        ")
        .WithParameters(p => {
            p["x", typeof(double)] = 5.5; p["y", typeof(double)] = 1.5;
            p["reason", typeof(string)] = "the lab lets it go";
        })
        .PerformCommand();
        using var doc = System.Text.Json.JsonDocument.Parse(print);
        var e = doc.RootElement;
        Assert.AreEqual("abandoned", e.GetProperty("ended").GetString());
        Assert.AreEqual("the lab lets it go", e.GetProperty("why").GetString(), "it ended short: the why is said");
        Assert.IsFalse(e.TryGetProperty("action", out _), "an ended route asks the body nothing");
        Assert.IsFalse(e.TryGetProperty("amount", out _));
    }

    [TestMethod]
    public void ThePanelsQuery_RendersThePlacesWithTheirDoorsAndOpenings_AsTheEngineLaysOutAForeach()
    {
        string json = perf.Actor.Using(@"
            foreach (places in g.Current.Map.Zones) {
                print places.Name 'name', places.X 'x', places.Y 'y', places.Width 'w', places.Height 'h', places.Center.X 'cx', places.Center.Y 'cy';
                foreach (doors in places.Doorways()) { print doors.To 'to', doors.At.X 'x', doors.At.Y 'y'; }
                foreach (opens in places.OpenSides()) { print opens.To 'to'; }
            }
        ").PerformQuery();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var places = doc.RootElement.GetProperty("places");
        Assert.AreEqual(9, places.GetArrayLength(), "the engine renders each foreach as an array named after its variable");
        var kitchen = places[0];
        Assert.AreEqual("kitchen", kitchen.GetProperty("name").GetString());
        Assert.AreEqual(2.0, kitchen.GetProperty("cx").GetDouble(), 0.001, "a property chain: places.Center.X");
        Assert.AreEqual(2, kitchen.GetProperty("doors").GetArrayLength(), "nested foreach: the place's doors, as the place sees them");
        Assert.AreEqual("north", kitchen.GetProperty("doors")[0].GetProperty("to").GetString());
        Assert.IsFalse(kitchen.TryGetProperty("opens", out _), "a place with no open boundary simply lacks the key");
        Assert.AreEqual("center", places[1].GetProperty("opens")[0].GetProperty("to").GetString(), "north opens to the center");
    }

    [TestMethod]
    public void ThePanelsQuery_RendersCornersWallsDoorsAndJambs_InheritedMembersIncluded()
    {
        string json = perf.Actor.Using(@"
            foreach (places in g.Current.Map.Zones) {
                print places.Name 'name';
                foreach (corners in places.Corners()) { print corners.Label 'label', corners.X 'x', corners.Y 'y'; }
                foreach (walls in places.Walls()) {
                    print walls.From.X 'x0', walls.From.Y 'y0', walls.To.X 'x1', walls.To.Y 'y1', walls.Length 'len', walls.Thickness 't';
                    foreach (doors in walls.Doors()) {
                        print doors.Name 'name', doors.At.X 'x', doors.At.Y 'y', doors.Width 'w', doors.Height 'h';
                        foreach (jambs in doors.Jambs()) { print jambs.Label 'label', jambs.X 'x', jambs.Y 'y'; }
                    }
                }
            }
        ").PerformQuery();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        var kitchen = doc.RootElement.GetProperty("places")[0];
        var corners = kitchen.GetProperty("corners");
        Assert.AreEqual(4, corners.GetArrayLength(), "a rectangle: four corner locations");
        Assert.AreEqual("kitchen ne", corners[2].GetProperty("label").GetString());
        Assert.AreEqual(4.0, corners[2].GetProperty("x").GetDouble(), 0.001, "X is inherited from Position and the engine binds it");
        var walls = kitchen.GetProperty("walls");
        Assert.AreEqual(4, walls.GetArrayLength(), "the kitchen has no open boundary: four walls");
        var east = walls[3];
        Assert.AreEqual(3.0, east.GetProperty("len").GetDouble(), 0.001, "Length is inherited from Segment");
        var door = east.GetProperty("doors")[0];
        Assert.AreEqual("kitchen/north", door.GetProperty("name").GetString(), "the east wall holds the door to the north hall");
        Assert.AreEqual(2, door.GetProperty("jambs").GetArrayLength(), "a door is two locations on its wall");
        Assert.AreEqual(8.8, door.GetProperty("jambs")[0].GetProperty("y").GetDouble(), 0.001);
        Assert.AreEqual(3, doc.RootElement.GetProperty("places")[1].GetProperty("walls").GetArrayLength(), "the north hall's south edge is open: three walls");
    }

    [TestMethod]
    public void ThePanelsQuery_RendersTheObstaclesAsAFlatList_ThingsFirstThenPeers()
    {
        perf.Actor.Using(@"
            g.Current.Collisions.Mark(Pose(10.25, 5.85, -1.5708));
            g.Current.Collisions.Mark(Pose(10.25, 5.15, 1.5708));
            g.Current.Collisions.Mark(Pose(9.9, 5.5, 0.0));
            g.Current.Collisions.Mark(Pose(2.0, 9.5, 0.0));
            g.Met('blue', Position(4.7, 9.5));
        ").PerformCommand();
        string json = perf.Actor.Using(@"
            print g.Current.Collisions.All().Count 'total';
            foreach (obstacles in g.Current.Collisions.All()) {
                print obstacles.Kind 'kind', g.Current.Map.ZoneNameOf(obstacles.Center) 'zone', obstacles.Shape 'shape', obstacles.Size 'size', obstacles.Who 'who';
                foreach (vertices in obstacles.Vertices()) { print vertices.At.X 'x', vertices.At.Y 'y', vertices.Heading 'normal'; }
            }
        ").PerformQuery();
        using var doc = System.Text.Json.JsonDocument.Parse(json);
        Assert.AreEqual(3, doc.RootElement.GetProperty("total").GetInt32(), "two things and one peer");
        var list = doc.RootElement.GetProperty("obstacles");
        Assert.AreEqual("thing", list[0].GetProperty("kind").GetString());
        Assert.AreEqual("east", list[0].GetProperty("zone").GetString(), "the flat list says the zone, asked of the map with the obstacle's centre (ajuste 54): no need to walk the places");
        Assert.AreEqual(3, list[0].GetProperty("vertices").GetArrayLength(), "its three touches, each with the normal");
        Assert.AreEqual("peer", list[2].GetProperty("kind").GetString());
        Assert.AreEqual("blue", list[2].GetProperty("who").GetString());
        Assert.IsFalse(list[2].TryGetProperty("vertices", out _), "a peer outlines nothing: bodies move on");
    }

    // ---- helpers: reads from a query's Out parameters, and the refusal a script gets ----

    private void Refuses(string script, string fragment)
    {
        try { perf.Actor.Using(script).PerformCommand(); }
        catch (Exception e)
        {
            var root = e; while (root.InnerException != null) root = root.InnerException;
            StringAssert.Contains(e.ToString(), fragment, "refused for another reason: " + root.Message);
            return;
        }
        Assert.Fail("the script was accepted: " + script);
    }

    private int Int(string expression) => Read<int>(expression);
    private double Double(string expression) => Read<double>(expression);
    private bool Bool(string expression) => Read<bool>(expression);
    private string Text(string expression) => Read<string>(expression);

    private T Read<T>(string expression)
    {
        using var rented = perf.Actor.RentedParameters();
        perf.Actor.Using($"@value = {expression};")
            .WithParameters(rented, p => {
                p[Parameter.Out, "value", typeof(T)] = default;
            })
            .PerformQuery();
        return rented["value"].GetValue<T>();
    }
}
