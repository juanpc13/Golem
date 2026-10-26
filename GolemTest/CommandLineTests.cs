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
        CollectionAssert.AreEqual(new[] { "visit", "cover", "form", "union", "program", "unions", "take", "rotate", "dissolve", "finish", "disputes", "formations", "play", "plays", "start", "then", "pause", "resume", "forget", "anchor", "reset", "state", "route", "where", "obstacles", "place", "optimize", "speed", "use", "scenarios", "explored", "explore", "export", "set", "show", "help", "--with" },
                                  CommandLine.Help.Select(h => h.Verb).ToList(), "every command the language has, in the help's order — no script of the actor's among them");
        Assert.IsTrue(CommandLine.Help.All(h => h.Usage != "" && h.What != "" && h.Example != ""), "each with how it is written, what it does and an example");
        StringAssert.Contains(Refused("help fly"), "help: 'fly' is no command; the commands are visit, cover, form");
        Assert.AreEqual("on-the-way", CommandLine.Parse("optimize On-The-Way").Text, "the strategy the routes are optimized by (ajustes 61, 82)");
        StringAssert.Contains(Refused("optimize fast"), "optimize: expected the strategy, on-the-way or door-by-door");
        // the play of pacman (propuesta 108): its title, pacman and the ghosts; alone, the read; the start — no keyboard, no steering (21-oct-2026)
        var staged = CommandLine.Parse("play game-1 pacman --pacman Blue --ghosts red,Green");
        Assert.AreEqual("game-1", staged.Text, "the play's name, its identity across the fleet (16-oct-2026)");
        Assert.AreEqual("pacman", staged.Values["kind"]);
        Assert.AreEqual("ghost", CommandLine.Parse("play game-1 pacman --pacman blue --ghosts red --as ghost").Values["as"], "the part on the line (16-oct-2026)");
        StringAssert.Contains(Refused("play game-1 pacman --pacman blue --as king"), "play: --as needs pacman or ghost");
        Assert.AreEqual("blue", staged.Values["pacman"]);
        Assert.AreEqual("red,green", staged.Values["ghosts"]);
        // the traffic play (propuesta 125): the light and the cars with their turns, in a driver's words; --as light|car
        var road = CommandLine.Parse("play road-1 traffic --light Orange --cars Red:straight,green:LEFT,yellow:right --as car");
        Assert.AreEqual("road-1", road.Text); Assert.AreEqual("traffic", road.Values["kind"]); Assert.AreEqual("orange", road.Values["light"]);
        Assert.AreEqual("red:straight,green:left,yellow:right", road.Values["cars"]); Assert.AreEqual("car", road.Values["as"]);
        StringAssert.Contains(Refused("play road-1 traffic --light orange"), "needs --light <golem> and --cars");
        StringAssert.Contains(Refused("play road-1 traffic --light orange --cars red:back"), "--cars needs every car and its turn");
        StringAssert.Contains(Refused("play road-1 traffic --light orange --cars red:left --as ghost"), "in a traffic play --as is light or car");
        StringAssert.Contains(Refused("play game-1 pacman --pacman blue --cars red:left"), "--light and --cars are the traffic play's");
        Assert.AreEqual("", CommandLine.Parse("play").Text, "alone: the read");
        StringAssert.Contains(Refused("play chess"), "play: expected the play's name and its kind");
        Assert.AreEqual("game-1", CommandLine.Parse("forget play game-1").Values["play"], "a play forgotten (16-oct-2026)");
        StringAssert.Contains(Refused("forget play"), "forget play: expected the play's name");
        StringAssert.Contains(Refused("play game-1 chess"), "play: expected the play's name and its kind");
        StringAssert.Contains(Refused("play pacman --ghosts"), "play: --ghosts needs names");
        StringAssert.Contains(Refused("head north"), "'head' is gone (21-oct-2026): pacman is autonomous");
        StringAssert.Contains(Refused("steer manual"), "'steer' is gone (21-oct-2026): pacman is autonomous");
        StringAssert.Contains(Refused("play game-1 pacman --pacman blue --ghosts red --steer manual"), "play: --steer is gone (21-oct-2026)");
        Assert.AreEqual("game-1", CommandLine.Parse("start game-1").Text, "the start of a play, this golem's own (16-oct-2026)");
        StringAssert.Contains(Refused("start"), "start: expected the play's name");
        // the body's pace (propuesta 111): the cruise, and the turn rate after --turn
        var pace = CommandLine.Parse("speed 3 --turn 2.5");
        Assert.AreEqual("3", pace.Text, "the cruise in m/s");
        Assert.AreEqual("2.5", pace.Values["turn"], "the turn rate in rad/s");
        Assert.IsFalse(CommandLine.Parse("speed 1.5").Values.ContainsKey("turn"), "the rate may be left as it is");
        StringAssert.Contains(Refused("speed fast"), "speed: expected the cruise in metres per second above zero");
        StringAssert.Contains(Refused("speed 0"), "speed: expected the cruise in metres per second above zero");
        StringAssert.Contains(Refused("speed 3 --rate 2"), "speed: after the cruise comes --turn");
        // a formation of the golem's (propuesta 104): its name, figure, centre, size, orientation, FLEET and POLICY — never a place
        var form = CommandLine.Parse("form Square-2 Square --side 2 --center 5.5,5.5 --angle 45 --fleet Blue,red --by Distance");
        Assert.AreEqual("form", form.Verb); Assert.AreEqual("square-2", form.Text); Assert.AreEqual("square", form.Values["figure"]);
        Assert.AreEqual((5.5, 5.5), form.Points[0]); Assert.AreEqual("2", form.Values["side"]); Assert.AreEqual("45", form.Values["angle"]);
        Assert.AreEqual("Blue,red", form.Values["fleet"]); Assert.AreEqual("distance", form.Values["by"]);
        Assert.IsFalse(CommandLine.Parse("form p pentagon --center 5,5 --side 1.5").Values.ContainsKey("fleet"), "no fleet said: this golem and every peer");
        Assert.AreEqual("rps", CommandLine.Parse("form game-1 square --center 5.5,5.5 --side 2 --by rps").Values["by"], "rock, paper, scissors (propuesta 115)");
        var forgetDisputes = CommandLine.Parse("forget disputes");
        Assert.AreEqual("forget", forgetDisputes.Verb);
        Assert.AreEqual("disputes", forgetDisputes.Text, "the disputes forgotten, no point");
        Assert.AreEqual(1, CommandLine.Parse("forget 10.13,5.5").Points.Count, "forget x,y still forgets what stood there");
        StringAssert.Contains(Refused("form game-1 square --center 5.5,5.5 --side 2 --by dice"), "--by is rank, distance or rps");
        StringAssert.Contains(Refused("form square-1 hexagon --center 5,5 --side 1"), "form: the figure is square, pentagon, triangle, circle or propeller");
        // the propeller (propuesta 114): its tips by --radius, its hub and its blades told or left to their defaults
        var propeller = CommandLine.Parse("form prop-1 propeller --center 5.5,5.5 --radius 3 --inner-radius 1 --blades 3 --fleet blue,red,green,yellow,orange,cyan");
        Assert.AreEqual("propeller", propeller.Values["figure"]); Assert.AreEqual("1", propeller.Values["inner-radius"]); Assert.AreEqual("3", propeller.Values["blades"]);
        Assert.AreEqual("propeller", CommandLine.Parse("form prop-2 propeller --center 5.5,5.5 --radius 3").Values["figure"], "hub and blades left to their defaults");
        StringAssert.Contains(Refused("form prop-1 propeller --center 5.5,5.5 --radius 3 --blades 1"), "--blades expects a whole number from 2 to 12");
        StringAssert.Contains(Refused("form prop-1 propeller --center 5.5,5.5 --radius 3 --inner-radius 4"), "hub");
        StringAssert.Contains(Refused("form prop-1 propeller --center 5.5,5.5 --side 3"), "is said by its --radius");
        StringAssert.Contains(Refused("form sq square --center 5.5,5.5 --side 3 --blades 3"), "only a propeller has --blades");
        StringAssert.Contains(Refused("form square-1 square --side 2"), "expected --center x,y and --side s");
        StringAssert.Contains(Refused("form square-1 square --center 5,5 --side 2 --angle north"), "--angle expects degrees");
        StringAssert.Contains(Refused("form t triangle --center 5,5 --side 2 --places 6"), "--places goes with --open");
        StringAssert.Contains(Refused("form s square --center 5,5 --side 2 --by luck"), "--by is rank, distance or rps");
        var ring = CommandLine.Parse("form ring-1 circle --center 5.5,5.5 --radius 1 --fleet red,yellow");
        Assert.AreEqual("circle", ring.Values["figure"]); Assert.AreEqual("1", ring.Values["radius"], "a ring said by its radius");
        StringAssert.Contains(Refused("form r circle --center 5,5 --side 1"), "a circle is said by its --radius");
        StringAssert.Contains(Refused("form s square --center 5,5 --radius 1"), "a square is said by its --side");
        StringAssert.Contains(Refused("form rings-1 double-ring --center 5.5,5.5 --radius 3 --inner-radius 1.2 --fleet blue,cyan,green"), "the figure is square, pentagon, triangle, circle or propeller");   // the double ring went (19-oct-2026)
        StringAssert.Contains(Refused("form s square --center 5,5 --side 2 --inner-radius 1"), "only a propeller has an inner radius");
        // the formation taken: its name alone — who goes where is the golem's (Juan: "lo que teníamos malo era --place 0")
        var take = CommandLine.Parse("take Square-2");
        Assert.AreEqual("take", take.Verb); Assert.AreEqual("square-2", take.Text);
        StringAssert.Contains(Refused("take square-2 --place 1"), "--place is gone (propuesta 104)");
        StringAssert.Contains(Refused("take"), "take: expected the formation's name");
        // a step of a formation, whole or of one ring
        var step = CommandLine.Parse("rotate square-2 Clockwise");
        Assert.AreEqual("rotate", step.Verb); Assert.AreEqual("square-2", step.Text); Assert.AreEqual("clockwise", step.Values["sense"]); Assert.IsFalse(step.Values.ContainsKey("ring"));
        StringAssert.Contains(Refused("rotate clockwise"), "rotate: expected the formation's name and the sense");
        StringAssert.Contains(Refused("rotate rings-1 clockwise --ring inner"), "--ring is gone with the double ring");
        Assert.AreEqual("square-2", CommandLine.Parse("dissolve square-2").Text);
        StringAssert.Contains(Refused("dissolve"), "dissolve: expected the formation's name");
        // the cue between dancers (propuesta 117; the FORMATION cued by its name since propuesta 123): a formation that starts on cue, the word that a member finished
        var cued = CommandLine.Parse("form tri-left triangle --center 3,5.5 --side 3 --fleet blue,red,green --cue Tri-Right");
        Assert.AreEqual("tri-right", cued.Values["cue"]);
        Assert.IsFalse(cued.Values.ContainsKey("on-cue"));
        var waits = CommandLine.Parse("form tri-right triangle --center 8,5.5 --side 3 --fleet cyan,orange,yellow --on-cue");
        Assert.IsTrue(waits.Values.ContainsKey("on-cue"));
        Assert.IsFalse(waits.Values.ContainsKey("cue"));
        StringAssert.Contains(Refused("form tri-left triangle --center 3,5.5 --side 3 --cue tri-right,tri-other"), "--cue expects one formation's name");
        StringAssert.Contains(Refused("form tri-left triangle --center 3,5.5 --side 3 --cue tri-left"), "cannot cue itself");
        StringAssert.Contains(Refused("form tri-left triangle --center 3,5.5 --side 3 --cue"), "--cue needs a value");
        // the pull (propuesta 118): the formation waited for — the one asked; no golem named (propuesta 123: --ask is gone)
        var pull = CommandLine.Parse("form tri-right triangle --center 8,5.5 --side 3 --fleet cyan,orange,yellow --after Tri-Left");
        Assert.AreEqual("tri-left", pull.Values["after"]); Assert.IsFalse(pull.Values.ContainsKey("ask"));
        StringAssert.Contains(Refused("form tri-right triangle --center 8,5.5 --side 3 --after tri-left --ask red"), "--ask is gone");
        StringAssert.Contains(Refused("form tri-right triangle --center 8,5.5 --side 3 --after tri-right"), "cannot start after itself");
        StringAssert.Contains(Refused("form tri-right triangle --center 8,5.5 --side 3 --after tri-left,tri-other"), "--after expects a formation's name");
        // how often it asks (propuesta 121): --every in seconds, with or without the s; only with the pull
        Assert.AreEqual("5", CommandLine.Parse("form tri-right triangle --center 8,5.5 --side 3 --after tri-left --every 5s").Values["every"]);
        Assert.AreEqual("2.5", CommandLine.Parse("form tri-right triangle --center 8,5.5 --side 3 --after tri-left --every 2.5").Values["every"]);
        Assert.IsFalse(pull.Values.ContainsKey("every"), "left out: the golem's own period");
        StringAssert.Contains(Refused("form tri-right triangle --center 8,5.5 --side 3 --after tri-left --every 0"), "--every expects seconds above zero");
        StringAssert.Contains(Refused("form tri-right triangle --center 8,5.5 --side 3 --every 5s"), "--every goes with --after");
        Assert.AreEqual("tri-left", CommandLine.Parse("finish Tri-Left").Text);
        // THE PROGRAMME (propuesta 123): its name, its formations in order, the candidates and the policy; an open formation: no fleet, by distance or rps, a polygon
        var show = CommandLine.Parse("union Two-Triangles Tri-Left,tri-right --by rps");
        Assert.AreEqual("two-triangles", show.Text); Assert.AreEqual("tri-left,tri-right", show.Values["formations"]); Assert.AreEqual("rps", show.Values["by"]);
        Assert.IsFalse(CommandLine.Parse("union two-triangles tri-left,tri-right").Values.ContainsKey("by"), "left out: by distance");
        Assert.AreEqual("blue,red", CommandLine.Parse("union two-triangles tri-left,tri-right --fleet blue,red").Values["fleet"]);
        StringAssert.Contains(Refused("union two-triangles"), "expected its name and its formations");
        StringAssert.Contains(Refused("union two-triangles tri-left,tri-left"), "each formation once");
        StringAssert.Contains(Refused("union two-triangles two-triangles,tri-left"), "cannot name itself");
        StringAssert.Contains(Refused("union two-triangles tri-left,tri-right --by rank"), "by rank nobody chooses");
        CollectionAssert.AreEqual(new[] { "all" }, CommandLine.Parse("union two-triangles tri-left --with all").With.ToList(), "parsed; the Commander refuses --with on it, like form");
        var open = CommandLine.Parse("form tri-left triangle --center 3,5.5 --side 3 --by distance --open");
        Assert.AreEqual("true", open.Values["open"]); Assert.IsFalse(open.Values.ContainsKey("fleet"));
        // the link is the union's (propuesta 124): an open formation has no cue, on-cue or pull of its own
        StringAssert.Contains(Refused("form tri-left triangle --center 3,5.5 --side 3 --by distance --open --cue tri-right"), "--open takes no --cue");
        StringAssert.Contains(Refused("form tri-right triangle --center 8,5.5 --side 3 --by distance --open --on-cue"), "--open takes no --cue");
        StringAssert.Contains(Refused("form tri-right triangle --center 8,5.5 --side 3 --by distance --open --after tri-left"), "--open takes no --cue");
        var linked = CommandLine.Parse("union two-triangles tri-left,tri-right --by distance --link Ask --every 5s");
        Assert.AreEqual("ask", linked.Values["link"]); Assert.AreEqual("5", linked.Values["every"]);
        Assert.AreEqual("cue", CommandLine.Parse("union two-triangles tri-left,tri-right --link cue").Values["link"]);
        Assert.IsFalse(CommandLine.Parse("union two-triangles tri-left,tri-right").Values.ContainsKey("link"), "left out: by the cue");
        StringAssert.Contains(Refused("union two-triangles tri-left,tri-right --link push"), "--link is cue or ask");
        StringAssert.Contains(Refused("union two-triangles tri-left,tri-right --every 5s"), "--every goes with --link ask");
        StringAssert.Contains(Refused("union two-triangles tri-left,tri-right --link ask --every 0s"), "seconds above zero");
        // THE PROGRAM (propuesta 124): the whole union in one line — the forms and the score composed into the definition the domain reads
        var program = CommandLine.Parse("program Two-Triangles --by distance --link cue --form tri-left triangle 3,5.5 side 3 angle 0 places 3 --form Tri-Right triangle 8,5.5 side 3 places 3 --score tri-left=cw,cw,cw tri-right=cw,cw,-");
        Assert.AreEqual("program", program.Verb); Assert.AreEqual("two-triangles", program.Text);
        Assert.AreEqual("tri-left: triangle 3,5.5 side 3 angle 0 places 3 = cw,cw,cw; tri-right: triangle 8,5.5 side 3 places 3 = cw,cw,-", program.Values["definition"]);
        Assert.AreEqual("tri-left,tri-right", program.Values["formations"]); Assert.AreEqual("distance", program.Values["by"]); Assert.AreEqual("cue", program.Values["link"]);
        Assert.AreEqual("ring: circle 5.5,5.5 radius 2 places 4", CommandLine.Parse("program solo --form ring circle 5.5,5.5 radius 2 places 4").Values["definition"], "no score: a formation that only takes its places");
        StringAssert.Contains(Refused("program solo"), "at least one --form");
        StringAssert.Contains(Refused("program solo --form tri triangle 3,5.5"), "--form expects");
        StringAssert.Contains(Refused("program solo --form tri hexagon 3,5.5 side 3"), "a triangle, a square, a pentagon or a circle");
        StringAssert.Contains(Refused("program solo --form tri triangle 3,5.5 side 3 --score other=cw"), "which no --form told");
        StringAssert.Contains(Refused("program solo --form tri triangle 3,5.5 side 3 --score tri=cw,left"), "a step is cw, ccw or -");
        StringAssert.Contains(Refused("program solo --form solo triangle 3,5.5 side 3"), "cannot name itself");
        StringAssert.Contains(Refused("program solo --form tri triangle 3,5.5 side 3 --by rank"), "by rank nobody chooses");
        StringAssert.Contains(Refused("form tri-left triangle --center 3,5.5 --side 3 --by distance --open --fleet blue,red"), "--open takes no --fleet");
        StringAssert.Contains(Refused("form tri-left triangle --center 3,5.5 --side 3 --open"), "by rank nobody chooses");
        StringAssert.Contains(Refused("form tri-left triangle --center 3,5.5 --side 3 --by rank --open"), "by rank nobody chooses");
        StringAssert.Contains(Refused("form ring circle --center 3,5.5 --radius 2 --by distance --open"), "opens for a number of golems");
        // for how many (22-oct-2026): --places n with --open — a circle opens with it
        Assert.AreEqual("2", CommandLine.Parse("form tri-left triangle --center 3,5.5 --side 3 --by distance --open --places 2").Values["places"]);
        Assert.AreEqual("4", CommandLine.Parse("form ring circle --center 3,5.5 --radius 2 --by rps --open --places 4").Values["places"]);
        StringAssert.Contains(Refused("form tri-left triangle --center 3,5.5 --side 3 --by distance --open --places 0"), "whole number above zero");
        StringAssert.Contains(Refused("form sq square --center 3,5.5 --side 3 --fleet blue,red --places 2"), "--places goes with --open");
        Assert.AreEqual("unions", CommandLine.Parse("unions").Verb);
        StringAssert.Contains(Refused("unions now"), "takes nothing more");
        StringAssert.Contains(Refused("finish"), "finish: expected the formation's name");
        Assert.AreEqual("formations", CommandLine.Parse("formations").Verb);
        // the map the golem uses (7-oct-2026, was enter): use map <scenario>
        var use = CommandLine.Parse("use Map Open-Floor");
        Assert.AreEqual("use", use.Verb); Assert.AreEqual("open-floor", use.Text, "the scenario named after the map, lower case");
        StringAssert.Contains(Refused("use open-floor"), "use: expected map and the scenario's name, like use map open-floor");
        StringAssert.Contains(Refused("use map"), "use: expected map and the scenario's name");
        StringAssert.Contains(Refused("enter open-floor"), "enter is now use map <scenario>", "the old word says what replaced it");
        // place (ajuste 87, a lab lever): this golem's mark bare, every other golem's by its name
        var place = CommandLine.Parse("place blue@6,6.2 Red@7,6.3 3,3");
        Assert.AreEqual("place", place.Verb);
        Assert.AreEqual("6,6.2", place.Values["blue"]);
        Assert.AreEqual("7,6.3", place.Values["red"], "the name, lower case");
        Assert.AreEqual("3,3", place.Values[""], "the bare mark is this golem's");
        Assert.AreEqual(0, place.Points.Count, "the marks are values, one per name, not the line's points");
        StringAssert.Contains(Refused("place"), "expected a mark like 3,3 for this golem, or one per golem");
        StringAssert.Contains(Refused("place blue@six"), "expected a mark like 3,3 at 'blue@six'");
        StringAssert.Contains(Refused("place blue@1,1 blue@2,2"), "blue's mark was given twice");
        StringAssert.Contains(Refused("place @spots"), "a name the console resolves");
        StringAssert.Contains(Refused("dash 9,1.5"), "'dash' is no command", "the dash is a strategy now, not a verb");
        StringAssert.Contains(Refused("fly 2,9.5"), "'fly' is no command; the commands are visit, cover, form, union, program, unions, take, rotate, dissolve, finish, disputes, formations, play, plays, start, then, pause, resume, forget, anchor, reset, state, route, where, obstacles, place, optimize, speed, use, scenarios, explored, explore, export, set, show, help, --with — help tells each");
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

}
