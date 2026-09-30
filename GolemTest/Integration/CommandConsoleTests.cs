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
        var adopt = await console.ExecuteAsync("adopt on-the-way");
        Assert.IsTrue(adopt.Ok, adopt.Text);
        StringAssert.Contains((await console.ExecuteAsync("where")).Text, "navigating on the way", "the strategy adopted, read where the golem stands");
        var dash = await console.ExecuteAsync("visit 9,1.5");
        Assert.IsTrue(dash.Ok, dash.Text);
        var way = (await console.ExecuteAsync("route")).Text;
        StringAssert.Contains(way, "living/south@3.4,1.5", "from the mark, on the way: the door where it bends, then the garage in one run through its door — the script's if improved the route born door by door (ajuste 62): " + way);
        Assert.AreEqual("syntax", (await console.ExecuteAsync("adopt sideways")).Kind, "a strategy that is none is no command");
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

}
