using GolemAPI.Commanding;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// THE LANGUAGE THE GOLEM IS COMMANDED IN (propuesta 58, 28-sep-2026), read: a line like a shell's — `golem visit 2,9.5 9,8` — is a
// command and its arguments, and a line that is no command says what was expected and where. The parser is the host's — it knows
// nothing of the golem — so it is tested alone.
[TestClass]
public class CommandLineTests
{
    [TestMethod]
    public void AnErrand_IsItsCommandAndItsPoints_InOrder_TheBinarysNameOptional()
    {
        var visit = CommandLine.Parse("visit 2,9.5 9,8");
        Assert.AreEqual("visit", visit.Verb);
        CollectionAssert.AreEqual(new[] { (2.0, 9.5), (9.0, 8.0) }, visit.Points.ToList());
        Assert.AreEqual("", visit.Golem, "no golem named: the console's own");
        Assert.AreEqual(0, visit.With.Count, "nobody to carry it to");
        var cover = CommandLine.Parse("  golem COVER 9,1.5   2,9.5 9,9.5  ");
        Assert.AreEqual("cover", cover.Verb, "the binary's name in front is welcome, the command in any case, the spaces anywhere");
        var named = CommandLine.Parse("golem Red visit 2,9.5 5.5,9.5 9,9.5");
        Assert.AreEqual("red", named.Golem, "the golem's name right after the binary's: the whole command, as a shell would take it");
        Assert.AreEqual("visit", named.Verb);
        Assert.AreEqual(3, named.Points.Count);
        Assert.AreEqual(3, cover.Points.Count);
        Assert.AreEqual((2.0, 9.5), cover.Points[1]);
        Assert.AreEqual((-1.25, 0.5), CommandLine.Parse("visit -1.25,0.5").Points[0], "a negative coordinate");
        Assert.AreEqual((2.0, 9.5), CommandLine.Parse("visit (2, 9.5)").Points[0], "a point in parentheses reads too");
    }

    [TestMethod]
    public void AnErrandWithoutPoints_OrWithSomethingElse_SaysWhatItExpected()
    {
        StringAssert.Contains(Refused("visit"), "visit: expected at least one point like 2,9.5");
        StringAssert.Contains(Refused("visit kitchen"), "visit: expected a point like 2,9.5 at 'kitchen'", "a place is not a point: the golem is sent to points only");
        StringAssert.Contains(Refused("cover 2,9.5"), "cover: expected at least 2 points like 2,9.5; found 1", "an order to choose needs two");
        StringAssert.Contains(Refused("visit 2;9.5"), "at '2;9.5'");
        StringAssert.Contains(Refused("visit @stops"), "visit: @stops is a name the console resolves — set it first: set stops 2,9.5 9,8");
    }

    [TestMethod]
    public void OneMoreStop_AndForgetting_TakeExactlyOnePoint()
    {
        Assert.AreEqual((5.5, 5.5), CommandLine.Parse("then 5.5,5.5").Points.Single());
        Assert.AreEqual((10.13, 5.5), CommandLine.Parse("forget 10.13,5.5").Points.Single());
        StringAssert.Contains(Refused("then 1,1 2,2"), "then: expected one point like 2,9.5; found 2");
        StringAssert.Contains(Refused("forget"), "forget: expected one point like 2,9.5; found 0");
    }

    [TestMethod]
    public void ACommandThatTakesNothing_RefusesMore()
    {
        foreach (var verb in new[] { "pause", "resume", "state", "route", "where", "obstacles" })
        {
            Assert.AreEqual(verb, CommandLine.Parse(verb).Verb);
            Assert.AreEqual(0, CommandLine.Parse(verb).Points.Count);
            StringAssert.Contains(Refused(verb + " now"), $"{verb} takes nothing more; found 'now'");
        }
    }

    [TestMethod]
    public void Reset_AloneOrWithTheOptionAll()
    {
        Assert.AreEqual(0, CommandLine.Parse("reset").Options.Count);
        CollectionAssert.AreEqual(new[] { "--all" }, CommandLine.Parse("reset --ALL").Options.ToList());
        StringAssert.Contains(Refused("reset all"), "reset takes no argument but the option --all; found 'all'");
        StringAssert.Contains(Refused("reset --everything"), "found '--everything'");
    }

    [TestMethod]
    public void SetAndShow_AreReadHere_ButAreTheConsolesOwn()
    {
        var set = CommandLine.Parse("set stops 2,9.5 9,8");
        Assert.AreEqual("stops", set.Text, "the name");
        Assert.AreEqual(2, set.Points.Count, "and its points, read like an errand's");
        StringAssert.Contains(Refused("set"), "set: expected a name and its points, like set stops 2,9.5 9,8");
        StringAssert.Contains(Refused("set 2,9.5"), "set: expected a name");
        Assert.AreEqual("", CommandLine.Parse("show").Text);
        Assert.AreEqual("stops", CommandLine.Parse("show stops").Text);
        StringAssert.Contains(Refused("show a b"), "show: expected nothing, or one name");
    }

    [TestMethod]
    public void Help_ListsEveryCommand_OrOne_AndAnUnknownCommandIsSaidSo()
    {
        Assert.AreEqual("", CommandLine.Parse("help").Text);
        Assert.AreEqual("visit", CommandLine.Parse("help VISIT").Text);
        CollectionAssert.AreEqual(new[] { "visit", "cover", "choreograph", "rotate", "then", "pause", "resume", "forget", "reset", "state", "route", "where", "obstacles", "adopt", "enter", "scenarios", "set", "show", "help", "--with" },
                                  CommandLine.Help.Select(h => h.Verb).ToList(), "every command the language has, in the help's order — no script of the actor's among them");
        Assert.IsTrue(CommandLine.Help.All(h => h.Usage != "" && h.What != "" && h.Example != ""), "each with how it is written, what it does and an example");
        StringAssert.Contains(Refused("help fly"), "help: 'fly' is no command; the commands are visit, cover, choreograph");
        Assert.AreEqual("on-the-way", CommandLine.Parse("adopt On-The-Way").Text, "the strategy adopted (ajuste 61)");
        StringAssert.Contains(Refused("adopt fast"), "adopt: expected the strategy, on-the-way or door-by-door");
        StringAssert.Contains(Refused("dash 9,1.5"), "'dash' is no command", "the dash is a strategy now, not a verb");
        StringAssert.Contains(Refused("fly 2,9.5"), "'fly' is no command; the commands are visit, cover, choreograph, rotate, then, pause, resume, forget, reset, state, route, where, obstacles, adopt, enter, scenarios, set, show, help, --with — help tells each");
        StringAssert.Contains(Refused("query { print g.Standing.X 'x'; }"), "'query' is no command", "the actor's scripts never travel on a line");
        StringAssert.Contains(Refused(""), "nothing to do: write a command, or help");
        StringAssert.Contains(Refused("golem"), "nothing to do");
        StringAssert.Contains(Refused("golem red"), "nothing to do", "a golem named and nothing asked of it");
        StringAssert.Contains(Refused("red visit 2,9.5"), "'red' is no command");
    }

    private static string Refused(string line) => Assert.ThrowsException<CommandSyntaxException>(() => CommandLine.Parse(line)).Message;

    [TestMethod]
    public void With_NamesThePeersTheSameCommandIsCarriedTo()
    {
        var with = CommandLine.Parse("golem visit 2,9.5 5.5,9.5 --with blue,green");
        Assert.AreEqual("", with.Golem, "the line is the console's own golem's");
        CollectionAssert.AreEqual(new[] { "blue", "green" }, with.With.ToList(), "and blue's and green's too");
        Assert.AreEqual(2, with.Points.Count, "the option is not a point");
        CollectionAssert.AreEqual(new[] { "blue" }, CommandLine.Parse("where --with Blue").With.ToList(), "a read travels too");
        CollectionAssert.AreEqual(new[] { "all" }, CommandLine.Parse("pause --with all").With.ToList(), "every peer on the wire");
        CollectionAssert.AreEqual(new[] { "blue", "green" }, CommandLine.Parse("visit --with blue 2,9.5 --with green,blue").With.ToList(), "anywhere after the command, each peer once");
        StringAssert.Contains(Refused("visit 2,9.5 --with"), "--with: expected the peers to carry the command to, like --with blue");
        StringAssert.Contains(Refused("visit 2,9.5 --with 3,4"), "--with: expected the peers");
        CollectionAssert.AreEqual(new[] { "--all" }, CommandLine.Parse("reset --with blue --all").Options.ToList());
        Assert.IsTrue(CommandLine.Help.Any(h => h.Verb == "--with"), "documented once, with the commands");
    }

    [TestMethod]
    public void Choreograph_TheSquare_SaidByItsSide_ItsCentre_ThePolicyByRank_AndTheFleetWhenTold()
    {
        var square = CommandLine.Parse("golem choreograph square --center 5.5,5.5 --side 2.0 --with all");
        Assert.AreEqual("choreograph", square.Verb);
        Assert.AreEqual("square", square.Text, "the figure");
        Assert.AreEqual((5.5, 5.5), square.Points[0], "the centre, a point like any other");
        Assert.AreEqual("2.0", square.Values["side"]);
        Assert.IsFalse(square.Values.ContainsKey("by"), "no policy said: rank, the default");
        CollectionAssert.AreEqual(new[] { "all" }, square.With.ToList(), "the fleet is this golem and those");
        Assert.IsFalse(square.Values.ContainsKey("fleet"));
        Assert.AreEqual("rank", CommandLine.Parse("choreograph square --center 5.5,5.5 --side 2 --by Rank").Values["by"], "the policy, said outright");
        Assert.AreEqual("blue,red", CommandLine.Parse("choreograph square --side 2 --fleet blue,red --center 5.5,5.5").Values["fleet"], "the options in any order");
        // the square alone for now, said by its side; the other policy not built yet
        StringAssert.Contains(Refused("choreograph circle --center 5.5,5.5 --radius 1"), "the square alone for now — the circle is set aside");
        StringAssert.Contains(Refused("choreograph triangle --center 5.5,5.5 --side 2"), "the triangle is set aside");
        StringAssert.Contains(Refused("choreograph hexagon --center 5.5,5.5 --side 1"), "expected the figure, square");
        StringAssert.Contains(Refused("choreograph square --center 5.5,5.5 --radius 2.0"), "a square is said by its side; --radius is the circle's");
        StringAssert.Contains(Refused("choreograph square --center 5.5,5.5"), "choreograph square: expected --center x,y and --side s");
        StringAssert.Contains(Refused("choreograph square --center 5.5,5.5 --side"), "--side needs a value");
        StringAssert.Contains(Refused("choreograph square --center 5.5,5.5 --side -2"), "--side expects metres greater than zero");
        Assert.AreEqual("distance", CommandLine.Parse("choreograph square --center 5.5,5.5 --side 2 --by Distance").Values["by"], "by distance: who stands nearest (ajuste 73)");
        StringAssert.Contains(Refused("choreograph square --center 5.5,5.5 --side 2 --by luck"), "--by is rank");
        StringAssert.Contains(Refused("choreograph square --center kitchen --side 1"), "expected a point like 2,9.5 at 'kitchen'");
        StringAssert.Contains(Refused("choreograph square --center 5.5,5.5 --side 1 --spin"), "'--spin' is no option");
        StringAssert.Contains(Refused("choreograph square --center 5.5,5.5 --side 1 --effect rotate-clockwise --for 10s"), "the timed turn is gone (ajuste 77)");
        // the step (ajuste 77): the sense is the whole of it
        Assert.AreEqual("clockwise", CommandLine.Parse("rotate Clockwise").Text);
        Assert.AreEqual("counterclockwise", CommandLine.Parse("golem red rotate counterclockwise").Text);
        StringAssert.Contains(Refused("rotate"), "expected the sense, clockwise or counterclockwise");
        StringAssert.Contains(Refused("rotate sideways"), "expected the sense");
        StringAssert.Contains(Refused("rotate clockwise 2"), "expected the sense");
    }
}
