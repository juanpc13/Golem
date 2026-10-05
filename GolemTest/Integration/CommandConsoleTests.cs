using System.Globalization;
using System.Text.Json;
using GolemAPI.Commanding;
using GolemAPI.Coordination;
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

    // THE WARDEN CALLS THE FLEET (propuesta 88; Juan: "centraliza la ley, flota aprendida, berths por tells"): the operator commands the
    // WARDEN — a subject without a body — which convenes the fleet; every golem answers where it stands; when everybody spoke the warden
    // applies the law ONCE and shares the whole table as one word; each golem takes its own place from it and the others as berths. A
    // golem's console refuses a choreography of its own; the warden's console carries any golem's line with the golem in front.
    [TestMethod]
    public async Task TheWardenCallsTheFleet_EveryGolemAnswers_AndTakesThePlaceShared_ByRank()
    {
        await using var world = new MockWorld();
        await world.AddWardenAsync(new[] { "blue", "red" });   // the warden first: the golems' words find it as they wake
        await world.PlaceGolemAsync("red");
        await world.PlaceGolemAsync("blue");
        var warden = new WardenCommander(world.Warden.Mind);
        var red = new Commander(world.HostOf("red").Embodiment);
        var blue = new Commander(world.HostOf("blue").Embodiment);

        // the fleet is learned: both woke and said so
        for (int i = 0; i < 150 && !(await warden.ExecuteAsync("fleet")).Text.Contains("2 golem(s) heard from"); i++) await Task.Delay(200);   // the in-memory wire may hand a word over on its retry, seconds later
        var board = await warden.ExecuteAsync("fleet");
        StringAssert.Contains(board.Text, "2 golem(s) heard from", "both golems said they woke — " + board.Text);
        StringAssert.Contains(board.Text, "red · stands at (2.5, 2.5) in warehouse", "where red said it stood, in which scenario — " + board.Text);

        // a fleet with members not in this world: the round never completes, nobody sets out
        var call = await warden.ExecuteAsync("choreograph square --center 5.5,5.5 --side 2.0 --by rank --fleet blue,red,green,yellow");
        Assert.IsTrue(call.Ok, call.Text);
        StringAssert.Contains(call.Text, "called", "the warden convened: the places are shared when everybody said where it stands — " + call.Text);
        await Task.Delay(1500);
        StringAssert.Contains((await warden.ExecuteAsync("fleet")).Text, "2 of 4 stood, places not shared yet", "green and yellow are not in this world: the warden waits");
        StringAssert.Contains((await red.ExecuteAsync("route")).Text, "no route yet", "nobody sets out");

        // the fleet of two: the places shared by rank — blue north-east, red north-west (two of four corners, ajuste 86) — each takes its own
        call = await warden.ExecuteAsync("choreograph square --center 5.5,5.5 --side 2.0 --by rank --fleet blue,red");
        Assert.IsTrue(call.Ok, call.Text);
        StringAssert.Contains(await Until(red, "center@4.5,6.5"), "center@4.5,6.5", "red, second of the two names: the north-west corner");
        StringAssert.Contains(await Until(blue, "center@6.5,6.5"), "center@6.5,6.5", "blue, first: the north-east corner");
        for (int i = 0; i < 50 && !(await warden.ExecuteAsync("fleet")).Text.Contains("places shared"); i++) await Task.Delay(200);
        board = await warden.ExecuteAsync("fleet");
        StringAssert.Contains(board.Text, "square by rank", board.Text);
        StringAssert.Contains(board.Text, "places shared", board.Text);
        StringAssert.Contains(board.Text, "blue stands at 6.3,10.4 → goes to 6.5,6.5", "the table, as shared — " + board.Text);

        // the consoles: a golem refuses a choreography of its own; the warden carries a golem's line with the golem in front, never --with
        Assert.AreEqual("refused", (await red.ExecuteAsync("choreograph square --center 5.5,5.5 --side 2.0")).Kind, "a choreography is the warden's");
        Assert.AreEqual("refused", (await red.ExecuteAsync("rotate clockwise")).Kind);
        Assert.AreEqual("syntax", (await warden.ExecuteAsync("choreograph square --center 5.5,5.5 --side 2.0 --with blue")).Kind, "no --with on the warden's console");
        Assert.AreEqual("syntax", (await warden.ExecuteAsync("visit 2,9.5")).Kind, "a golem's command needs the golem in front");
        var carried = await warden.ExecuteAsync("golem red,blue where");
        Assert.IsTrue(carried.Ok, carried.Text);
        StringAssert.Contains(carried.Text, "red › standing at", carried.Text);
        StringAssert.Contains(carried.Text, "blue › standing at", carried.Text);
        Assert.AreEqual("refused", (await warden.ExecuteAsync("golem green where")).Kind, "no such golem on the wire");
        var all = await warden.ExecuteAsync("golem all state");
        StringAssert.Contains(all.Text, "red ›"); StringAssert.Contains(all.Text, "blue ›");
    }

    // THE STEP (ajustes 77, 79; propuesta 88: the warden counts who is placed and opens it): four golems — blue, green, purple, yellow — each
    // put in the centre hall a little OUTSIDE its corner of a square of side 2 around (5.5, 5.5), so the join crosses nobody and every body
    // arrives FACING THE CENTRE. Each says to the warden when it stands on its place; a step asked of the warden once all four stand opens
    // at once: the next table, shared — each to the PREVIOUS place in the order. A second step, queued while they walk, waits.
    [TestMethod]
    public async Task AStep_OpensWhenEveryBodyStands_TheNextTableShared_AndStepsQueue()
    {
        await using var world = new MockWorld();
        var names = new[] { "blue", "green", "purple", "yellow" };
        await world.AddWardenAsync(names);
        var homes = new Dictionary<string, (double X, double Y)> { ["blue"] = (6.65, 6.65), ["green"] = (4.35, 6.65), ["purple"] = (4.35, 4.35), ["yellow"] = (6.65, 4.35) };   // a hand off the hall's walls
        foreach (var name in names) await world.AddGolemAsync(name, homes[name], peers: names.Where(n => n != name).ToList());
        var fleet = names.ToDictionary(n => n, n => new Commander(world.HostOf(n).Embodiment));
        var warden = new WardenCommander(world.Warden.Mind);

        Assert.AreEqual("refused", (await warden.ExecuteAsync("rotate clockwise")).Kind, "no formation in place yet");
        Assert.IsTrue((await warden.ExecuteAsync("choreograph square --center 5.5,5.5 --side 2.0 --by rank --fleet blue,green,purple,yellow")).Ok);
        foreach (var g in fleet.Values) await Until(g, "route 1 completed");
        StringAssert.Contains((await fleet["blue"].ExecuteAsync("where")).Text, "(6.5, 6.5)", "blue, first of the names: the north-east corner");
        StringAssert.Contains((await fleet["green"].ExecuteAsync("where")).Text, "(4.5, 6.5)", "green, second: north-west");
        StringAssert.Contains((await fleet["purple"].ExecuteAsync("where")).Text, "(4.5, 4.5)", "purple, third: south-west");
        StringAssert.Contains((await fleet["yellow"].ExecuteAsync("where")).Text, "(6.5, 4.5)", "yellow, fourth: south-east");

        // the words reach the warden: all four placed (the in-memory wire may hand a word over on its retry, seconds later)
        for (int i = 0; i < 150 && !(await warden.ExecuteAsync("fleet")).Text.Contains("4 of 4 placed"); i++) await Task.Delay(200);
        StringAssert.Contains((await warden.ExecuteAsync("fleet")).Text, "4 of 4 placed", "every golem said it stands on its place");
        var step = await warden.ExecuteAsync("rotate clockwise");
        Assert.IsTrue(step.Ok, step.Text);
        StringAssert.Contains(await Until(fleet["blue"], "center@6.5,4.5"), "center@6.5,4.5", "one step clockwise: blue from the north-east corner to the south-east one");
        var again = await warden.ExecuteAsync("rotate clockwise");
        Assert.IsTrue(again.Ok, again.Text);
        for (int i = 0; i < 50 && !(await warden.ExecuteAsync("fleet")).Text.Contains("1 step(s) queued"); i++) await Task.Delay(200);
        StringAssert.Contains((await warden.ExecuteAsync("fleet")).Text, "round 1", "the first step opened a new round");
        StringAssert.Contains((await warden.ExecuteAsync("fleet")).Text, "1 step(s) queued", "the second step waits for everybody");
        StringAssert.Contains((await fleet["green"].ExecuteAsync("route")).Text, "center@6.5,6.5", "green's step: to the north-east corner");
        StringAssert.Contains((await fleet["purple"].ExecuteAsync("route")).Text, "center@4.5,6.5", "purple's step: to the north-west corner");
        StringAssert.Contains((await fleet["yellow"].ExecuteAsync("route")).Text, "center@4.5,4.5", "yellow's step: to the south-west corner");
    }

    // PLACE (ajuste 87, a lab lever; Juan: "el botón que los ponga en la posición"): the body carried onto a mark — the pending routes let go, the
    // body re-anchored, the golem awake there; with names, every golem of the fleet onto its own mark, each peer's line carried to it.
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

    // BY DISTANCE (ajuste 73; propuesta 88: the law applied once by the warden): a square of side 1 around (2.0, 1.5), in the living room —
    // red at its mark (2.5, 2.5) is nearest the north-east corner (2.5, 2.0); blue, far away in the north hall, gets the nearest corner left,
    // the north-west one (1.5, 2.0); the other two stay free (ajuste 86). By rank it would be the other way round.
    [TestMethod]
    public async Task AChoreographyByDistance_TheWardenGivesEachGolemTheNearestPlace()
    {
        await using var world = new MockWorld();
        await world.AddWardenAsync(new[] { "blue", "red" });
        await world.PlaceGolemAsync("red");
        await world.PlaceGolemAsync("blue");
        var warden = new WardenCommander(world.Warden.Mind);
        var red = new Commander(world.HostOf("red").Embodiment);
        var blue = new Commander(world.HostOf("blue").Embodiment);

        var call = await warden.ExecuteAsync("choreograph square --center 2.0,1.5 --side 1.0 --by distance --fleet blue,red");
        Assert.IsTrue(call.Ok, call.Text);
        StringAssert.Contains(await Until(red, "living@2.5,2"), "living@2.5,2", "red, nearest to the north-east corner, goes to it");
        string blueWay = await Until(blue, "living@1.5,2");
        StringAssert.Contains(blueWay, "living@1.5,2", "blue took the north-west corner, the nearest left — " + blueWay);
        StringAssert.Contains((await red.ExecuteAsync("state")).Text, "of 1 route(s)", "red's route was never interrupted: still its one route");
        StringAssert.Contains((await warden.ExecuteAsync("fleet")).Text, "square by distance", "the formation in place, on the board");
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
