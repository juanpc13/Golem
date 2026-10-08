using System.Globalization;
using System.Text.Json;
using GolemAPI.Commanding;
using GolemTest.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// THE COMMANDER ON A LIVE GOLEM (propuesta 58, fase 1): the same GolemHost the containers run, in the world held in memory, commanded
// by lines — each verb reaching the same role the endpoint reaches, answering in words for a console and in the endpoint's JSON for
// a program; a refusal in the domain's words, a line that is no command in the parser's.
[TestClass]
public class CommandConsoleTests
{
    [TestInitialize]
    public void PinTheCulture()
    {
        CultureInfo.DefaultThreadCurrentCulture = CultureInfo.InvariantCulture;
        CultureInfo.CurrentCulture = CultureInfo.InvariantCulture;
    }

    [TestMethod]
    public async Task AnErrand_IsCommanded_AndAnsweredInTheRobotsWords_WithTheBoardForAProgram()
    {
        await using var world = new MockWorld();
        await world.PlaceGolemAsync("red");                                         // on its mark in the living room, (2.5, 2.5)
        var console = new Commander(world.HostOf("red").Embodiment);

        Assert.AreEqual("refused", (await console.ExecuteAsync("golem blue visit 5.5,9.5")).Kind, "another golem's line: a console commands its own golem");
        StringAssert.Contains((await console.ExecuteAsync("golem blue where")).Text, "this is red: 'blue' is commanded on its own console — write --with blue");
        var reply = await console.ExecuteAsync("golem red visit 5.5,9.5");
        Assert.IsTrue(reply.Ok, reply.Text);
        StringAssert.StartsWith(reply.Text, "route 1 · turn", "the first order in the robot's words: " + reply.Text);
        StringAssert.Contains(reply.Text, " rad, toward living/south (", "how much, and toward what: " + reply.Text);
        using var doc = JsonDocument.Parse(reply.Json);
        Assert.AreEqual(1, doc.RootElement.GetProperty("pending").GetInt32(), "the board, as /move answers it");
        Assert.AreEqual("turnRight", doc.RootElement.GetProperty("print").GetProperty("action").GetString(), "and the print the command returned");

        var state = await console.ExecuteAsync("state");
        StringAssert.StartsWith(state.Text, "1 of 1 route(s) pending · underway: route 1, next point (", state.Text);
        var route = await console.ExecuteAsync("route");
        StringAssert.Contains(route.Text, "route 1 pending", route.Text);
        StringAssert.Contains(route.Text, "way: living/south@4,1.5 > center~south@4.5,3 > north@5.5,9.5", "the way as decided, door by door — a visit (ajuste 58; a dash would cross the door on the way): " + route.Text);
        var where = await console.ExecuteAsync("where");
        StringAssert.StartsWith(where.Text, "standing at (2.5, 2.5) facing 0 rad, in living", where.Text);
    }

    [TestMethod]
    public async Task OneMoreStop_Cover_TheHold_AndForgetting_ReachTheirRoles()
    {
        await using var world = new MockWorld();
        await world.PlaceGolemAsync("red");
        var console = new Commander(world.HostOf("red").Embodiment);

        Assert.AreEqual("refused", (await console.ExecuteAsync("then 9,9.5")).Kind, "no route to tell a stop to");
        Assert.IsTrue((await console.ExecuteAsync("cover 9,1.5 2,9.5")).Ok);
        var then = await console.ExecuteAsync("then 9,9.5");
        Assert.IsTrue(then.Ok, then.Text);
        StringAssert.Contains((await console.ExecuteAsync("state")).Text, "3 stop(s) left", "the stop was told to the route underway");

        var pause = await console.ExecuteAsync("pause");
        Assert.IsTrue(pause.Ok, pause.Text);
        StringAssert.Contains(pause.Text, "route 1 · stop", "held: the body stands — " + pause.Text);
        StringAssert.Contains((await console.ExecuteAsync("where")).Text, "— held");
        Assert.IsTrue((await console.ExecuteAsync("resume")).Ok);
        Assert.AreEqual("refused", (await console.ExecuteAsync("resume")).Kind, "not paused: the domain refuses in its words");

        var forget = await console.ExecuteAsync("forget 10.13,5.5");
        Assert.AreEqual("refused", forget.Kind, "forgetting where nothing stands: the role refuses in the domain's words — " + forget.Text);
        StringAssert.Contains(forget.Text, "refused: the golem holds no obstacle there");
        StringAssert.StartsWith((await console.ExecuteAsync("obstacles")).Text, "0 obstacle(s): 0 thing(s), 0 peer(s) met, 0 mark(s)");
    }

    [TestMethod]
    public async Task ARefusal_IsTheDomains_ALineThatIsNoCommand_TheParsers_AndAValue_IsTheConsoles()
    {
        await using var world = new MockWorld();
        await world.PlaceGolemAsync("red");
        var console = new Commander(world.HostOf("red").Embodiment);

        var offMap = await console.ExecuteAsync("visit -5,-5");
        Assert.AreEqual("refused", offMap.Kind);
        StringAssert.Contains(offMap.Text, "refused: stop (-5, -5): that point is nowhere on the map");
        var noCommand = await console.ExecuteAsync("visit kitchen");
        Assert.AreEqual("syntax", noCommand.Kind);
        StringAssert.Contains(noCommand.Text, "visit: expected a point like 2,9.5 at 'kitchen'");

        var script = await console.ExecuteAsync("query { print g.Routes().Count 'n'; }");
        Assert.AreEqual("syntax", script.Kind, "no script of the actor's travels on a line: the reads are commands");
        StringAssert.Contains(script.Text, "'query' is no command");
        var value = await console.ExecuteAsync("set stops 2,9.5 9,8");
        Assert.AreEqual("syntax", value.Kind, "a value is the console's: it never reaches the golem");
        StringAssert.Contains(value.Text, "set is the console's own: it keeps the value and writes @stops on the line before it is sent");
        StringAssert.Contains((await console.ExecuteAsync("visit @stops")).Text, "@stops is a name the console resolves — set it first");

        var help = await console.ExecuteAsync("help");
        Assert.IsTrue(help.Ok);
        foreach (var h in CommandLine.Help) StringAssert.Contains(help.Text, h.Usage, "every verb in the help: " + help.Text);
        StringAssert.Contains((await console.ExecuteAsync("help then")).Text, "then x,y");
    }

    [TestMethod]
    public async Task Reset_LetsGo_AndTheBodyGoesBackToItsMark()
    {
        await using var world = new MockWorld();
        await world.PlaceGolemAsync("red");
        var console = new Commander(world.HostOf("red").Embodiment);

        Assert.IsTrue((await console.ExecuteAsync("visit 5.5,9.5")).Ok);
        var reset = await console.ExecuteAsync("reset");
        Assert.IsTrue(reset.Ok, reset.Text);
        StringAssert.StartsWith(reset.Text, "let go: every pending route abandoned, the body back on its mark");
        StringAssert.StartsWith((await console.ExecuteAsync("state")).Text, "0 of 1 route(s) pending", "the route abandoned, kept as history");
        StringAssert.Contains((await console.ExecuteAsync("state")).Text, "last: route 1 abandoned");
        var optimize = await console.ExecuteAsync("optimize on-the-way");
        Assert.IsTrue(optimize.Ok, optimize.Text);
        StringAssert.Contains((await console.ExecuteAsync("where")).Text, "navigating on the way", "the strategy the routes are optimized by, read where the golem stands");
        var dash = await console.ExecuteAsync("visit 9,1.5");
        Assert.IsTrue(dash.Ok, dash.Text);
        var way = (await console.ExecuteAsync("route")).Text;
        StringAssert.Contains(way, "living/south@3.4,1.5", "from the mark, on the way: the door where it bends, then the garage in one run through its door — the script's if improved the route born door by door (ajuste 62): " + way);
        Assert.AreEqual("syntax", (await console.ExecuteAsync("optimize sideways")).Kind, "a strategy that is none is no command");
    }

    // MORE THAN ONE GOLEM ON ONE LINE (Juan, 28-sep-2026: "para involucrar más de uno a la vez… que el visit viaje al otro golem"; then "si
    // yo ya soy golem, ¿debería especificarme a mí mismo?… un parámetro para los otros — sí, con with"): the line is this golem's own,
    // `--with` names the peers the same command is carried to, over the wire the tells travel (here in memory, in a container /command
    // over HTTP); the answers come back one per golem, this one first, named.
    [TestMethod]
    public async Task ACommandWithPeers_ReachesEachOfThem_AndAnswersPerGolem()
    {
        await using var world = new MockWorld();
        await world.PlaceGolemAsync("red");                                         // (2.5, 2.5), the living room
        await world.PlaceGolemAsync("blue");                                        // the plan's mark for blue
        var red = new Commander(world.HostOf("red").Embodiment);

        var both = await red.ExecuteAsync("golem visit 5.5,9.5 --with blue");
        Assert.IsTrue(both.Ok, both.Text);
        var lines = both.Text.Split('\n').Select(l => l.TrimEnd('\r')).ToList();
        Assert.AreEqual(2, lines.Count, "one answer per golem, this one first: " + both.Text);
        StringAssert.StartsWith(lines[0], "red › route 1 · ", "red's own part, done here");
        StringAssert.StartsWith(lines[1], "blue › route 1 · ", "blue's, carried to its console and answered in the robot's words");
        using var doc = JsonDocument.Parse(both.Json);
        Assert.IsTrue(doc.RootElement.TryGetProperty("red", out _) && doc.RootElement.TryGetProperty("blue", out _), "for a program: one object per golem");
        StringAssert.Contains((await new Commander(world.HostOf("blue").Embodiment).ExecuteAsync("state")).Text, "1 of 1 route(s) pending", "blue really took the errand");

        var all = await red.ExecuteAsync("where --with all");
        StringAssert.Contains(all.Text, "red › standing at (");
        StringAssert.Contains(all.Text, "blue › standing at (", "all: every peer on red's routes — the mock world's whole fleet, blue and green");
        StringAssert.Contains(all.Text, "green › refused: this is red: no golem named 'green' among its peers", "green is on the routes but not in this world: its row says so");
        Assert.AreEqual("refused", all.Kind, "so the line as a whole is refused, the two that answered still answered");

        var mixed = await red.ExecuteAsync("state --with green");
        Assert.AreEqual("refused", mixed.Kind, "one of them unknown: the line is refused as a whole, this golem's part still answered");
        StringAssert.Contains(mixed.Text, "red › ");
        StringAssert.Contains(mixed.Text, "green › refused: this is red: no golem named 'green' among its peers");
    }

    // THE CALL SPREADS BY TELL (propuesta 59; ajustes 65, 71): the operator commands ONE golem — red — and red's act is told to every peer;
    // blue hears it and joins by itself, its own Join to its own corner by rank. The fleet is red and all its peers (blue, green, red,
    // yellow sorted): blue first (north-east), red third (south-west); with --fleet blue,red, two of four corners: blue north-east, red north-west (ajuste 86). A choreography's --with is refused.
    [TestMethod]
    public async Task AChoreography_CommandedToOneGolem_SpreadsByTell_AndEveryPeerJoinsItsOwnCorner()
    {
        await using var world = new MockWorld();
        await world.PlaceGolemAsync("red");
        await world.PlaceGolemAsync("blue");
        var red = new Commander(world.HostOf("red").Embodiment);
        var blue = new Commander(world.HostOf("blue").Embodiment);

        var call = await red.ExecuteAsync("choreograph square --center 5.5,5.5 --side 2.0 --by rank --fleet blue,red,green,yellow");   // the names: red's corner is the third's
        Assert.IsTrue(call.Ok, call.Text);
        StringAssert.Contains(call.Text, "called", "one golem commanded: it said where it stands, and waits for the fleet's words (ajuste 80) — " + call.Text);
        await Task.Delay(1500);
        StringAssert.Contains((await red.ExecuteAsync("route")).Text, "no route yet", "green and yellow are not in this world: the round never completes, nobody sets out — the fleet waits (ajuste 80)");
        await red.ExecuteAsync("reset");
        call = await red.ExecuteAsync("choreograph square --center 5.5,5.5 --side 2.0 --by rank --fleet blue,red");
        Assert.IsTrue(call.Ok, call.Text);
        StringAssert.Contains(await Until(red, "center@4.5,6.5"), "center@4.5,6.5", "with blue's word the round is complete: red, second of the two names, sets out to the north-west corner (two on a square take two neighbouring corners, the other two free — ajuste 86)");
        string blueWay = await Until(blue, "center@6.5,6.5");
        StringAssert.Contains(blueWay, "center@6.5,6.5", "blue heard the call, said where it stands and set out by itself: first of the two, the north-east corner — " + blueWay);

        Assert.AreEqual("syntax", (await red.ExecuteAsync("choreograph square --center 5.5,5.5 --side 2.0 --with blue")).Kind, "a choreography spreads by itself");
        Assert.AreEqual("syntax", (await red.ExecuteAsync("choreograph circle --center 5.5,5.5 --radius 1.0")).Kind, "the square alone for now");
        Assert.AreEqual("refused", (await red.ExecuteAsync("choreograph square --center 0.75,5.5 --side 2.0")).Kind, "a square the corridor cannot hold: red's corner falls in the wall");

        Assert.AreEqual("syntax", (await red.ExecuteAsync("choreograph square --center 5.5,5.5 --side 2.0 --effect rotate-clockwise --for 10s")).Kind, "the timed turn is gone (ajuste 77)");
        Assert.AreEqual("syntax", (await red.ExecuteAsync("rotate clockwise --with blue")).Kind, "a step spreads by itself too");
    }

    // THE STEP (ajuste 77; Juan: "que la flota tome la posición del otro en el sentido de las agujas del reloj… se pueden encolar"): four
    // golems — blue, green, purple, yellow: no follower among them, unlike red → blue in this world — each put in the centre hall (the
    // world's walls are the warehouse's) a little OUTSIDE its corner of a square of side 2 around (5.5, 5.5), on the diagonal, so the join
    // crosses nobody and every body arrives FACING THE CENTRE: blue the north-east corner (6.5, 6.5), green north-west, purple south-west,
    // yellow south-east, counter-clockwise. Facing the centre, a step costs every body the same turn (45°), so the four move in lockstep
    // and the one behind never runs into the one ahead while it turns (lab 1-oct: a body already facing its way caught the one ahead
    // mid-turn). Once all four stand
    // on their corners, one step clockwise takes each to the PREVIOUS place in the order — along the sides, each following the one ahead:
    // blue to the south-east corner, green to the north-east… A second step, queued while they walk, opens only when all four arrived again.
    [TestMethod]
    public async Task AStep_TakesEveryBodyToTheNextCorner_OnceEverybodyStands_AndStepsQueue()
    {
        await using var world = new MockWorld();
        var names = new[] { "blue", "green", "purple", "yellow" };
        var homes = new Dictionary<string, (double X, double Y)> { ["blue"] = (6.65, 6.65), ["green"] = (4.35, 6.65), ["purple"] = (4.35, 4.35), ["yellow"] = (6.65, 4.35) };   // a hand off the hall's walls
        foreach (var name in names) await world.AddGolemAsync(name, homes[name], peers: names.Where(n => n != name).ToList());
        var fleet = names.ToDictionary(n => n, n => new Commander(world.HostOf(n).Embodiment));
        var blue = fleet["blue"];

        Assert.AreEqual("refused", (await blue.ExecuteAsync("rotate clockwise")).Kind, "no formation in place yet");
        Assert.IsTrue((await blue.ExecuteAsync("choreograph square --center 5.5,5.5 --side 2.0 --by rank --fleet blue,green,purple,yellow")).Ok);
        foreach (var g in fleet.Values) await Until(g, "route 1 completed");
        StringAssert.Contains((await blue.ExecuteAsync("where")).Text, "(6.5, 6.5)", "blue, first of the names: the north-east corner");
        StringAssert.Contains((await fleet["green"].ExecuteAsync("where")).Text, "(4.5, 6.5)", "green, second: north-west");
        StringAssert.Contains((await fleet["purple"].ExecuteAsync("where")).Text, "(4.5, 4.5)", "purple, third: south-west");
        StringAssert.Contains((await fleet["yellow"].ExecuteAsync("where")).Text, "(6.5, 4.5)", "yellow, fourth: south-east");

        // the words cross the wire: every copy hears the four (the in-memory wire may deliver a word on its retry, seconds later)
        for (int i = 0; i < 150 && names.Any(n => world.Read(n, "print g.Choreography.Current.PlacedCount 'v';").GetInt32() < 4); i++) await Task.Delay(200);
        CollectionAssert.AreEqual(new[] { 4, 4, 4, 4 }, names.Select(n => world.Read(n, "print g.Choreography.Current.PlacedCount 'v';").GetInt32()).ToList(), "every copy heard that all four stand on their places");
        var step = await blue.ExecuteAsync("rotate clockwise");
        Assert.IsTrue(step.Ok, step.Text);
        StringAssert.StartsWith(step.Text, "route 2 · ", "everybody already stands: the step opens at once — " + step.Text);
        StringAssert.Contains(step.Text, "(6.5, 4.5)", "one step clockwise: blue heads from the north-east corner to the south-east one — " + step.Text);
        var again = await blue.ExecuteAsync("rotate clockwise");
        Assert.IsTrue(again.Ok, again.Text);
        StringAssert.Contains(again.Text, "queued", "the second step waits for everybody — " + again.Text);
        // the step spreads: every peer queues the same two steps and opens the first — green from north-west to north-east, purple from
        // south-west to north-west, yellow from south-east to south-west (the walk itself is the Gazebo lab's: this world's wire may hand a
        // word over seconds late, and four bodies in lockstep do not forgive that)
        for (int i = 0; i < 150 && names.Any(n => world.Read(n, "print g.Choreography.Current.Queued 'v';").GetInt32() != 1); i++) await Task.Delay(200);
        CollectionAssert.AreEqual(new[] { 1, 1, 1, 1 }, names.Select(n => world.Read(n, "print g.Choreography.Current.Queued 'v';").GetInt32()).ToList(), "every copy queued the second step and opened the first");
        StringAssert.Contains((await fleet["green"].ExecuteAsync("route")).Text, "center@6.5,6.5", "green's step: to the north-east corner");
        StringAssert.Contains((await fleet["purple"].ExecuteAsync("route")).Text, "center@4.5,6.5", "purple's step: to the north-west corner");
        StringAssert.Contains((await fleet["yellow"].ExecuteAsync("route")).Text, "center@4.5,4.5", "yellow's step: to the south-west corner");
    }

    // PLACE (ajuste 87, a lab lever; Juan: "el botón que los ponga en la posición"): the body carried onto a mark — the pending routes let go, the
    // body re-anchored, the golem awake there; with names, every golem of the fleet onto its own mark, each peer's line carried to it.
    // THE FORMATIONS THE WARDEN NAMES (propuesta 99; ajuste 101): told for four golems, a triangle has its three corners and the middle of its
    // first side; the golem resolves them on its own map and takes the one of the number it is told — no coordinate on the line
    [TestMethod]
    public async Task AFormationToldForMoreGolemsThanVertices_IsResolvedByTheGolem_AndItsPlaceTakenByNumber()
    {
        await using var world = new MockWorld();
        await world.PlaceGolemAsync("red");                                         // on its mark in the living room, (2.5, 2.5)
        var console = new Commander(world.HostOf("red").Embodiment);

        var formed = await console.ExecuteAsync("form tri-1 triangle --center 2,2 --side 1 --places 4");
        Assert.IsTrue(formed.Ok, formed.Text);
        StringAssert.StartsWith(formed.Text, "formed tri-1 — places 0: 2,2.58  1: 1.75,2.14  2: 1.5,1.71  3: 2.5,1.71", "the apex, the middle of the first side, the other corners: " + formed.Text);
        StringAssert.Contains((await console.ExecuteAsync("formations")).Json, "\"places\":4", "the formations read says for how many it was told");
        var taken = await console.ExecuteAsync("take tri-1 --place 1");
        Assert.IsTrue(taken.Ok, taken.Text);
        StringAssert.Contains((await console.ExecuteAsync("route")).Text, "face@1.75,2.14", "the middle of a side, taken by its number, faced at the end");
        Assert.AreEqual("refused", (await console.ExecuteAsync("take tri-1 --vertex 4")).Kind, "four places: 0 to 3");

        // a ring told (ajuste 102): a circle for two, and its place taken straight (ajuste 103)
        var ring = await console.ExecuteAsync("form ring-1 circle --center 2.5,2.5 --radius 0.7 --places 2");
        Assert.IsTrue(ring.Ok, ring.Text);
        StringAssert.StartsWith(ring.Text, "formed ring-1 — places 0: 3.2,2.5  1: 1.8,2.5", ring.Text);
        var step = await console.ExecuteAsync("take ring-1 --place 1");
        Assert.IsTrue(step.Ok, step.Text);
        StringAssert.Contains((await console.ExecuteAsync("route")).Text, "face@1.8,2.5", "straight to its west place, facing the centre");
    }

    [TestMethod]
    public async Task Place_CarriesTheBodyOntoAMark_AndWithNames_EveryGolemOntoItsOwn()
    {
        await using var world = new MockWorld();
        await world.PlaceGolemAsync("red");
        await world.PlaceGolemAsync("blue");
        var red = new Commander(world.HostOf("red").Embodiment);
        var blue = new Commander(world.HostOf("blue").Embodiment);

        Assert.IsTrue((await red.ExecuteAsync("visit 9,1.5")).Ok);
        var placed = await red.ExecuteAsync("place 4.5,3");
        Assert.IsTrue(placed.Ok, placed.Text);
        StringAssert.StartsWith(placed.Text, "placed at (4.5, 3): every pending route let go, the body carried there");
        StringAssert.Contains((await red.ExecuteAsync("where")).Text, "(4.5, 3)", "the golem woke where the body was carried");
        StringAssert.Contains((await red.ExecuteAsync("state")).Text, "last: route 1 abandoned", "the errand underway was let go, journaled");

        var fleet = await red.ExecuteAsync("place blue@5.5,8 red@5.5,3.5");
        Assert.IsTrue(fleet.Ok, fleet.Text);
        StringAssert.Contains(fleet.Text, "red › placed at (5.5, 3.5)", "this golem's own mark, by its name — " + fleet.Text);
        StringAssert.Contains(fleet.Text, "blue › placed at (5.5, 8)", "blue's line carried to blue — " + fleet.Text);
        StringAssert.Contains((await blue.ExecuteAsync("where")).Text, "(5.5, 8)");
        StringAssert.Contains((await red.ExecuteAsync("where")).Text, "(5.5, 3.5)");
        Assert.AreEqual("syntax", (await red.ExecuteAsync("place 3,3 --with blue")).Kind, "each golem its own mark: the names go on the line, never --with");
        Assert.AreEqual("refused", (await red.ExecuteAsync("place green@3,3")).Kind, "no golem named green in this world");
    }

    // BY DISTANCE (ajuste 73): red is commanded and convenes; blue hears where red stood, convenes by itself and takes the place red
    // leaves it. A square of side 1 around (2.0, 1.5), in the living room: red at its mark (2.5, 2.5) is nearest the north-east corner
    // (2.5, 2.0), so blue — far away in the north hall — gets the nearest corner left, the north-west one (1.5, 2.0); the other two stay
    // free (ajuste 86). By rank it would be the other way round (blue first of the names, the north-east corner).
    [TestMethod]
    public async Task AChoreographyByDistance_EachGolemTakesTheNearestPlace_TheWordSpreadsAndDecides()
    {
        await using var world = new MockWorld();
        await world.PlaceGolemAsync("red");
        await world.PlaceGolemAsync("blue");
        var red = new Commander(world.HostOf("red").Embodiment);
        var blue = new Commander(world.HostOf("blue").Embodiment);

        var call = await red.ExecuteAsync("choreograph square --center 2.0,1.5 --side 1.0 --by distance --fleet blue,red");
        Assert.IsTrue(call.Ok, call.Text);
        StringAssert.Contains(call.Text, "called", "red said where it stands; the routes open when blue speaks (ajuste 80) — " + call.Text);
        StringAssert.Contains(await Until(red, "living@2.5,2"), "living@2.5,2", "red, nearest to the north-east corner, sets out to it once the round is complete");
        string blueWay = await Until(blue, "living@1.5,2");
        StringAssert.Contains(blueWay, "living@1.5,2", "blue heard red, convened by itself and took the north-west corner, the nearest left — " + blueWay);
        StringAssert.Contains((await red.ExecuteAsync("route")).Text, "living@2.5,2", "blue's word took nothing from red");
        StringAssert.Contains((await red.ExecuteAsync("state")).Text, "of 1 route(s)", "red's route was never interrupted: still its one route");
    }

    // The newest route of a golem, read until it says what a tell should have made it do (a tell is delivered, not instant).
    private static async Task<string> Until(Commander golem, string expected)
    {
        string text = "";
        for (int i = 0; i < 100; i++)
        {
            text = (await golem.ExecuteAsync("route")).Text;
            if (text.Contains(expected)) return text;
            await Task.Delay(200);
        }
        return text;
    }
}
