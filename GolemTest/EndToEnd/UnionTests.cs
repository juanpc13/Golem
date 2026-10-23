using GolemDomain;
using GolemDomain.Formations;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Robots;
using GolemDomain.Scenarios;
using GolemDomain.Touches;
using GolemDomain.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// THE PROGRAMME (propuesta 123, 21-oct-2026; Juan: "sindicalizar parece ser lo de las formaciones, pero sin que uno elija a los golems como tal… se
// auto-selecciona de acuerdo a las distancias… dos coreografías en una sola cosa… si ya están llenos se va a la otra"): several OPEN formations in
// order, the golems shared among them by the law over ONE round of words — every copy concludes the same table. Tested as objects, through the golems.
// THE SCORE (propuesta 124, 22-oct-2026): every formation of the union is taken at once and its steps are the union's BEATS, danced together.
[TestClass]
public sealed class UnionTests
{
    private static readonly Body Body = new(new Meters(0.25), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(0.6));
    private static Golem Born(string name) { var g = new Golem(Body, name); g.Enter(new Scenario(Catalog.OpenFloor(), new Collisions())); return g; }
    private const string Six = "blue,cyan,green,orange,red,yellow";
    private static readonly Position Left = new(3.0, 5.5), Right = new(8.0, 5.5);

    // the two triangles told to a golem: the union first, then both formations opened — taken at once, their steps the union's beats (propuesta 124)
    private static (Union Show, Formation LeftOf, Formation RightOf) Told(Golem g, string candidates = Six, Policy by = Policy.Distance)
    {
        var fleet = new Fleet(candidates);
        var show = g.Choreography.Union("two-triangles", "tri-left,tri-right", fleet, by, "stamp-" + g.Name);
        var left = g.Choreography.Form("tri-left", g.Choreography.Figure("triangle", Left, new Meters(3.0)), fleet, by);
        g.Choreography.Open(left);
        var right = g.Choreography.Form("tri-right", g.Choreography.Figure("triangle", Right, new Meters(3.0)), fleet, by);
        g.Choreography.Open(right);
        return (show, left, right);
    }

    private static void WalkToTheEnd(GolemDomain.Routes.Route route)
    {
        for (int i = 0; i < 50 && route.IsPending(); i++)
        {
            if (route.Waiting) route.Go();   // the start given by hand where the words are not the point
            route.Arrive(route.NextLeg);
        }
    }

    // where the six stand: three near the left triangle, three near the right
    private static readonly Dictionary<string, Position> Homes = new()
    {
        ["blue"] = new Position(1.0, 4.6), ["red"] = new Position(3.0, 8.0), ["green"] = new Position(4.8, 4.5),
        ["yellow"] = new Position(9.8, 4.5), ["orange"] = new Position(8.0, 8.0), ["cyan"] = new Position(6.2, 4.8),
    };

    [TestMethod]
    public void AUnion_SharesTheCandidatesAmongItsFiguresByDistance_InOneRound_TheSameInEveryCopy()
    {
        var blue = Born("blue");
        var (show, left, right) = Told(blue);
        Assert.AreEqual("two-triangles", show.Name); CollectionAssert.AreEqual(new[] { "tri-left", "tri-right" }, show.Names.ToList());
        Assert.AreEqual(6, show.Candidates.Count); Assert.IsTrue(show.IsKnown, "both figures told and open"); Assert.IsFalse(show.IsComplete, "nobody spoke");
        Assert.IsTrue(left.IsOpen); Assert.AreEqual("two-triangles", left.UnionName); Assert.IsFalse(left.IsResolved); Assert.IsFalse(left.IsMember, "a candidate, no member yet");
        Assert.AreEqual(3, left.Capacity, "open: its vertices are its places"); Assert.AreEqual(3, left.PlaceCount); Assert.AreEqual(0, left.Holders().Count);
        Assert.AreEqual(6, left.Fleet.Count, "the candidates, until the union resolves");
        // an open formation is not taken by itself: its union convenes
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => left.Convene(new Pose(1.0, 4.6, 0.0), left.Fleet.Member(blue))).Message, "is open: its union two-triangles convenes");
        Assert.AreSame(show, blue.Choreography.FindUnion("Two-Triangles")); Assert.IsTrue(blue.Choreography.HasUnion("two-triangles"));
        Assert.AreEqual(1, blue.Choreography.Unions().Count);

        // blue's copy: blue speaks first, then the five words in any order — nothing opens before the sixth
        var six = show.Candidates;
        Assert.IsNull(show.Convene(new Pose(Homes["blue"].X, Homes["blue"].Y, 0.0), six.Member(blue)));
        Assert.IsTrue(show.Knows(six.Member(blue))); Assert.IsFalse(show.Awaits("blue")); Assert.IsTrue(show.Awaits("cyan"));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => show.Convene(new Pose(1.0, 4.6, 0.0), six.Member(blue))).Message, "already spoke");
        foreach (var who in new[] { "yellow", "cyan", "red", "orange" }) { Assert.IsNull(show.Stood(six.Member(who), Homes[who], "stamp-" + who)); Assert.IsFalse(show.IsResolved); }
        var route = show.Stood(six.Member("green"), Homes["green"], "stamp-green");
        Assert.IsTrue(show.IsResolved); Assert.IsFalse(show.CannotResolve);
        // the table: three nearest to each figure — a figure full sends the next to the other
        CollectionAssert.AreEqual(new[] { "blue", "green", "red" }, show.MembersOf("tri-left").ToList());
        CollectionAssert.AreEqual(new[] { "cyan", "orange", "yellow" }, show.MembersOf("tri-right").ToList());
        Assert.AreEqual(0, show.LeftOut().Count); Assert.IsTrue(show.IsMember); Assert.IsFalse(show.IsLeftOut); Assert.AreEqual("tri-left", show.MineName); Assert.AreSame(left, show.Mine);
        // the left, enrolled, dances at once: the union's words are its round, blue's route to its place opened — the right's three among the berths
        Assert.IsTrue(left.IsResolved); Assert.IsTrue(left.IsMember); Assert.AreEqual("blue,green,red", left.Fleet.Roster); Assert.AreEqual(3, left.Capacity);
        Assert.IsNotNull(route); Assert.AreSame(route, left.Route); Assert.IsTrue(route.IsPending()); Assert.IsTrue(left.IsComplete);
        Assert.AreEqual(3, left.Holders().Count, "the left's own law over its members' words");
        Assert.AreEqual(2 * 2 + 3, left.Berths().Count, "the two other members — where they stood and their places — and the three of the right where they stood");
        // the right, enrolled, taken at once too (propuesta 124): blue is no member of it, but its copy keeps its members' words, their places and its score
        Assert.IsTrue(right.IsResolved); Assert.IsFalse(right.IsMember, "blue dances on the left"); Assert.AreEqual("cyan,orange,yellow", right.Fleet.Roster);
        Assert.IsNull(right.Route, "nothing of blue's opens in the right"); Assert.IsTrue(right.IsComplete, "its members' words are kept here too"); Assert.AreEqual(3, right.Holders().Count);
        Assert.AreEqual(1, right.Queue(right.Figure.Rotate(Sense.Clockwise), "s1"), "every copy keeps the whole score: a step of the right queues here too");
        // a late word changes nothing
        Assert.AreSame(route, show.Stood(six.Member("green"), new Position(0.0, 0.0), "x"));

        // CYAN'S COPY hears the words in another order, its own last — and concludes the same table; it dances on the right, taken at once
        var cyan = Born("cyan");
        var (cyanShow, cyanLeft, cyanRight) = Told(cyan);
        foreach (var who in new[] { "green", "blue", "orange", "red", "yellow" }) cyanShow.Stood(six.Member(who), Homes[who], "stamp-" + who);
        Assert.IsFalse(cyanShow.IsResolved, "five of six");
        var cyanRoute = cyanShow.Convene(new Pose(Homes["cyan"].X, Homes["cyan"].Y, 0.0), six.Member(cyan));
        Assert.IsNotNull(cyanRoute, "the right is taken with its union: cyan's route to its place opened (propuesta 124)");
        Assert.IsTrue(cyanShow.IsResolved);
        CollectionAssert.AreEqual(show.MembersOf("tri-left").ToList(), cyanShow.MembersOf("tri-left").ToList());
        CollectionAssert.AreEqual(show.MembersOf("tri-right").ToList(), cyanShow.MembersOf("tri-right").ToList());
        Assert.AreEqual("tri-right", cyanShow.MineName); Assert.AreSame(cyanRight, cyanShow.Mine);
        Assert.IsFalse(cyanLeft.IsMember); Assert.IsTrue(cyanRight.IsMember); Assert.AreEqual("cyan,orange,yellow", cyanRight.Fleet.Roster);
        Assert.AreEqual(1, cyanRight.Queue(cyanRight.Figure.Rotate(Sense.Clockwise), "s1"), "a member queues its steps");
        Assert.AreSame(cyanRoute, cyanRight.Route); Assert.IsTrue(cyanRight.IsComplete); Assert.AreEqual(3, cyanRight.Holders().Count);
        Assert.IsFalse(cyan.Choreography.AwaitsCue("tri-right"), "an open formation waits for no cue: it is taken with its union (propuesta 124)");
        // a word about an open formation before its union resolved is nobody's (lab 21-oct-2026): orange's copy, unresolved
        var early = Born("orange");
        var (_, _, earlyRight) = Told(early);
        Assert.IsNull(earlyRight.Stood(six.Member("blue"), Homes["blue"]));
        Assert.AreEqual(0, earlyRight.StoodCount);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => cyanRight.Stood(cyanRight.Fleet.Member("blue"), Homes["blue"])).Message, "is not in the fleet");
        // the unions read: the table
        Assert.AreEqual(6, cyanShow.Shares().Count); Assert.AreEqual("tri-left,tri-right", cyanShow.Bill); Assert.AreEqual("", cyanShow.LeftOutNames);
    }

    [TestMethod]
    public void AFigureFull_SendsTheNextCandidateToTheOther_AndTheFarthestAreLeftOut_AndFewerCandidatesLeaveTheLastFigureShort()
    {
        // blue stands by the right apex: it takes it; orange, the fourth near the right, goes LEFT (the right is full)
        var blue = Born("blue");
        var (show, left, right) = Told(blue);
        var six = show.Candidates;
        show.Convene(new Pose(7.9, 7.0, 0.0), six.Member(blue));
        foreach (var who in new[] { "cyan", "yellow", "orange", "red", "green" }) show.Stood(six.Member(who), Homes[who], "n-" + who);
        Assert.IsTrue(show.IsResolved);
        CollectionAssert.AreEqual(new[] { "green", "orange", "red" }, show.MembersOf("tri-left").ToList());
        CollectionAssert.AreEqual(new[] { "blue", "cyan", "yellow" }, show.MembersOf("tri-right").ToList());
        Assert.AreEqual("tri-right", show.MineName); Assert.IsFalse(left.IsMember); Assert.IsTrue(right.IsMember);
        Assert.IsNotNull(show.Route, "taken at once with its union (propuesta 124)");

        // seven candidates for six places: the farthest is left out and free
        var pink = Born("pink");
        var (seven, _, _) = Told(pink, "blue,cyan,green,orange,pink,red,yellow");
        var all = seven.Candidates;
        seven.Convene(new Pose(5.5, 0.5, 0.0), all.Member(pink));
        foreach (var who in Homes.Keys) seven.Stood(all.Member(who), Homes[who], "n-" + who);
        Assert.IsTrue(seven.IsResolved);
        CollectionAssert.AreEqual(new[] { "pink" }, seven.LeftOut().ToList()); Assert.AreEqual("pink", seven.LeftOutNames);
        Assert.IsTrue(seven.IsLeftOut); Assert.IsFalse(seven.IsMember); Assert.AreEqual("", seven.MineName);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => seven.Mine).Message, "pink was left out");
        Assert.AreEqual(6, seven.Shares().Count);

        // five candidates (no red): the left gets two, the right three — a formation with fewer bodies than vertices leaves one free
        var green = Born("green");
        var (five, fiveLeft, fiveRight) = Told(green, "blue,cyan,green,orange,yellow");
        var crew = five.Candidates;
        foreach (var who in new[] { "blue", "cyan", "orange", "yellow" }) five.Stood(crew.Member(who), Homes[who], "n-" + who);
        Assert.IsNotNull(five.Convene(new Pose(Homes["green"].X, Homes["green"].Y, 0.0), crew.Member(green)), "green dances on the left, which opens at once");
        CollectionAssert.AreEqual(new[] { "blue", "green" }, five.MembersOf("tri-left").ToList());
        CollectionAssert.AreEqual(new[] { "cyan", "orange", "yellow" }, five.MembersOf("tri-right").ToList());
        Assert.AreEqual(2, fiveLeft.Fleet.Count); Assert.AreEqual(3, fiveLeft.PlaceCount, "a vertex stays free");

        // three candidates, all near the right: the left would get nobody — the union stays unresolved and says why
        var cyan = Born("cyan");
        var (three, _, _) = Told(cyan, "cyan,orange,yellow");
        var trio = three.Candidates;
        three.Stood(trio.Member("orange"), Homes["orange"], "n-orange"); three.Stood(trio.Member("yellow"), Homes["yellow"], "n-yellow");
        Assert.IsNull(three.Convene(new Pose(Homes["cyan"].X, Homes["cyan"].Y, 0.0), trio.Member(cyan)));
        Assert.IsFalse(three.IsResolved); Assert.IsTrue(three.CannotResolve);
        StringAssert.Contains(three.Why, "tri-left gets nobody: 3 candidate(s) for 2 formations");
    }

    [TestMethod]
    public void AFormationToldAfterTheWords_CompletesTheRound_AndTheRefusals()
    {
        // the words of the round heard before the union was told: kept by name, taken in by the union; a figure told last resolves it
        var red = Born("red");
        var six = new Fleet(Six);
        foreach (var who in new[] { "blue", "green", "yellow", "orange", "cyan" }) red.Choreography.Overheard("two-triangles", who, Homes[who], "n-" + who);
        Assert.AreEqual(5, red.Choreography.OverheardCount("two-triangles"));
        var show = red.Choreography.Union("two-triangles", "tri-left,tri-right", six, Policy.Distance, "stamp-red");
        Assert.AreEqual(5, show.StoodCount, "the words kept ahead are the union's"); Assert.AreEqual(0, red.Choreography.OverheardCount("two-triangles"));
        Assert.IsFalse(show.IsKnown);
        Assert.IsNull(show.Convene(new Pose(Homes["red"].X, Homes["red"].Y, 0.0), six.Member(red)), "six spoke, but no figure is told yet");
        Assert.IsTrue(show.Knows(six.Member(red))); Assert.IsFalse(show.IsComplete); Assert.IsFalse(show.IsResolved);
        var left = red.Choreography.Form("tri-left", red.Choreography.Figure("triangle", Left, new Meters(3.0)), six, Policy.Distance);
        red.Choreography.Open(left);
        Assert.IsFalse(show.IsResolved, "one of two figures told");
        var right = red.Choreography.Form("tri-right", red.Choreography.Figure("triangle", Right, new Meters(3.0)), six, Policy.Distance);
        Assert.AreSame(show, red.Choreography.Open(right));
        Assert.IsTrue(show.IsKnown); Assert.IsTrue(show.IsResolved, "the last figure told completes the round: the law ran");
        Assert.AreEqual("tri-left", show.MineName); Assert.IsNotNull(left.Route, "red's route to its place on the left opened inside the Open act");
        Assert.AreEqual(3, left.Holders().Count);

        // the refusals: a union by rank; one named like a formation; an open formation by rank, a circle, or without a union naming it
        var blue = Born("blue");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Union("p", "a,b", six, Policy.Rank, "s")).Message, "by rank nobody chooses");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Union("p", "a,a", six, Policy.Distance, "s")).Message, "each formation once");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Union("p", "p,a", six, Policy.Distance, "s")).Message, "cannot name itself");
        var byRank = blue.Choreography.Form("sq", blue.Choreography.Figure("square", Left, new Meters(2.0)), six, Policy.Rank);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Open(byRank)).Message, "no union names sq");
        blue.Choreography.Union("show", "sq,ring", six, Policy.Distance, "s");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Open(byRank)).Message, "cannot open by rank");
        var ring = blue.Choreography.Form("ring", blue.Choreography.Figure("circle", Left, new Meters(2.0)), six, Policy.Distance);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Open(ring)).Message, "a polygon");
        // FOR HOW MANY (22-oct-2026): told a count, a circle opens and a polygon takes more or fewer than its vertices
        blue.Choreography.Open(ring, 4);
        Assert.IsTrue(ring.IsOpen); Assert.AreEqual(4, ring.Capacity); Assert.AreEqual(4, ring.OpenPlaces); Assert.AreEqual(4, ring.PlaceCount);
        var two = blue.Choreography.Form("sq2", blue.Choreography.Figure("square", Right, new Meters(2.0)), six, Policy.Distance);
        blue.Choreography.Union("show3", "sq2", six, Policy.Distance, "s3");
        blue.Choreography.Open(two, 2);
        Assert.AreEqual(2, two.Capacity, "a square for two: two places shared, the other corners free");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Open(byRank, -1)).Message, "above zero, or its vertices");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Union("sq", "a,b", six, Policy.Distance, "s")).Message, "names a formation already");
        // dissolved: its formations with it, and told again under the name it is made again
        var square = blue.Choreography.Form("sq2", blue.Choreography.Figure("square", Left, new Meters(2.0)), six, Policy.Distance);
        var show2 = blue.Choreography.Union("show2", "sq2", six, Policy.Distance, "s2");
        blue.Choreography.Open(square);
        blue.Choreography.DissolveUnion(show2);
        Assert.IsTrue(show2.Dissolved); Assert.IsTrue(square.Dissolved); Assert.IsFalse(blue.Choreography.HasUnion("show2"));
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.FindUnion("show2")).Message, "no union named");
    }

    [TestMethod]
    public void ByRps_ThePlacesWantedBySeveral_ArePlayed_WithTheSameHandsInEveryCopy()
    {
        // two candidates standing alike by the left apex: the game decides, from the stamps the words carried — every copy the same
        var blue = Born("blue"); var red = Born("red");
        var (blueShow, _, _) = Told(blue, "blue,cyan,green,orange,red,yellow", Policy.RockPaperScissors);
        var (redShow, _, _) = Told(red, "blue,cyan,green,orange,red,yellow", Policy.RockPaperScissors);
        var six = blueShow.Candidates;
        var apex = new Position(3.0, 7.0);
        blueShow.Convene(new Pose(apex.X, apex.Y, 0.0), six.Member(blue));
        redShow.Stood(six.Member("blue"), apex, blueShow.Stamp);
        foreach (var who in new[] { "green", "yellow", "orange", "cyan" }) { blueShow.Stood(six.Member(who), Homes[who], "n-" + who); redShow.Stood(six.Member(who), Homes[who], "n-" + who); }
        blueShow.Stood(six.Member("red"), apex, redShow.Stamp);
        redShow.Convene(new Pose(apex.X, apex.Y, 0.0), six.Member(red));
        Assert.IsTrue(blueShow.IsResolved); Assert.IsTrue(redShow.IsResolved);
        CollectionAssert.AreEqual(blueShow.MembersOf("tri-left").ToList(), redShow.MembersOf("tri-left").ToList());
        CollectionAssert.AreEqual(blueShow.MembersOf("tri-right").ToList(), redShow.MembersOf("tri-right").ToList());
        Assert.AreEqual(blueShow.Played, redShow.Played, "the same game in both copies");
        StringAssert.Contains(blueShow.Played, "tri-left:0");
        Assert.IsTrue(blueShow.MembersOf("tri-left").Contains("blue") && blueShow.MembersOf("tri-left").Contains("red"), "both near the left: both of it — one the apex, the other its next free place");
    }

    // THE BEATS (propuesta 124, 22-oct-2026; Juan: "la unionize es la que tiene todo el plan, pero distribuido entre todos… la secuencia sí la llevaría
    // la unionize, pero la forma en como resuelven es con formaciones y con push y pulls"): both formations taken at once; the k-th step of each is the
    // k-th beat, opened when every formation has every member placed and started when every mover of the whole union is lined up; a formation with
    // no step left stays; the union is done when every member of every formation finished
    [TestMethod]
    public void TheBeats_StepEveryFormationTogether_StartWhenTheWholeUnionIsLinedUp_AndAFormationWithNoStepLeftStays()
    {
        var blue = Born("blue"); var cyan = Born("cyan");
        var (blueShow, blueLeft, blueRight) = Told(blue);
        var (cyanShow, cyanLeft, cyanRight) = Told(cyan);
        Assert.AreEqual(Link.Cue, blueShow.Link); Assert.AreEqual(0.0, blueShow.EverySeconds); Assert.AreEqual(0, blueShow.Beat); Assert.IsFalse(blueShow.IsDone);
        // THE SCORE on every tab, before anybody spoke: two steps of the left, one of the right — queued in every copy, member or not
        foreach (var (left, right) in new[] { (blueLeft, blueRight), (cyanLeft, cyanRight) })
        {
            Assert.AreEqual(1, left.Queue(left.Figure.Rotate(Sense.Clockwise), "l1")); Assert.AreEqual(2, left.Queue(left.Figure.Rotate(Sense.Clockwise), "l2"));
            Assert.AreEqual(1, right.Queue(right.Figure.Rotate(Sense.Clockwise), "r1"));
        }
        // the round of words, in both copies
        var six = blueShow.Candidates;
        blueShow.Convene(new Pose(Homes["blue"].X, Homes["blue"].Y, 0.0), six.Member(blue));
        cyanShow.Stood(six.Member("blue"), Homes["blue"], blueShow.Stamp);
        foreach (var who in new[] { "red", "green", "orange", "yellow" }) { blueShow.Stood(six.Member(who), Homes[who], "n-" + who); cyanShow.Stood(six.Member(who), Homes[who], "n-" + who); }
        var blueRoute = blueShow.Stood(six.Member("cyan"), Homes["cyan"], cyanShow.Stamp);
        var cyanRoute = cyanShow.Convene(new Pose(Homes["cyan"].X, Homes["cyan"].Y, 0.0), six.Member(cyan));
        Assert.IsTrue(blueShow.IsResolved && cyanShow.IsResolved);
        Assert.AreEqual("tri-left", blueShow.MineName); Assert.AreEqual("tri-right", cyanShow.MineName);
        Assert.IsNotNull(blueRoute); Assert.AreSame(blueRoute, blueLeft.Route); Assert.IsNotNull(cyanRoute); Assert.AreSame(cyanRoute, cyanRight.Route);
        Assert.IsTrue(blueLeft.IsComplete && blueRight.IsComplete, "both formations' words in blue's copy"); Assert.AreEqual(3, blueRight.Holders().Count);
        Assert.AreEqual(2, blueLeft.Queued); Assert.AreEqual(1, blueRight.Queued, "the right's score kept in blue's copy");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blueLeft.Step()).Message, "the beat waits");

        // THE TAKE STARTS TOGETHER ACROSS THE UNION: blue lined up, its two of the left too — the route still waits for the right's three
        blueRoute.Arrive(blueRoute.NextLeg);
        Assert.IsTrue(blueRoute.Waiting);
        blueLeft.Aligned(blueLeft.Me);
        blueLeft.HeardAligned(blueLeft.Fleet.Member("red")); blueLeft.HeardAligned(blueLeft.Fleet.Member("green"));
        Assert.IsTrue(blueLeft.MoversAligned); Assert.IsFalse(blueShow.MoversAligned); Assert.IsTrue(blueRoute.Waiting, "the right's movers are not lined up yet");
        blueRight.HeardAligned(blueRight.Fleet.Member("cyan")); blueRight.HeardAligned(blueRight.Fleet.Member("orange"));
        Assert.IsTrue(blueRoute.Waiting);
        blueRight.HeardAligned(blueRight.Fleet.Member("yellow"));
        Assert.IsTrue(blueShow.MoversAligned); Assert.IsFalse(blueRoute.Waiting, "the last mover of the whole union lined up: blue sets out");
        WalkToTheEnd(blueRoute);
        Assert.IsFalse(blueRoute.IsPending());

        // BEAT 1 opens when every formation has every member placed — the right's words count here too — and steps BOTH formations
        Assert.AreSame(blueRoute, blueLeft.Placed(blueLeft.Me), "placed; the beat waits for the others");
        blueLeft.Heard(blueLeft.Fleet.Member("red")); blueLeft.Heard(blueLeft.Fleet.Member("green"));
        Assert.IsTrue(blueLeft.AllPlaced); Assert.IsFalse(blueShow.CanStep, "the right is not placed yet"); Assert.AreEqual(0, blueShow.Beat);
        blueRight.Heard(blueRight.Fleet.Member("cyan")); blueRight.Heard(blueRight.Fleet.Member("orange"));
        string rightBefore = string.Join(" ", blueRight.Holders().Select(h => $"{h.Name}@{h.Index}"));
        var step = blueRight.Heard(blueRight.Fleet.Member("yellow"));
        Assert.AreEqual(1, blueShow.Beat); Assert.AreEqual(1, blueLeft.Round); Assert.AreEqual(1, blueRight.Round, "the right stepped in blue's copy too");
        Assert.IsNotNull(step); Assert.AreNotSame(blueRoute, step); Assert.AreSame(step, blueLeft.Route, "blue's route of the step, in its own formation");
        Assert.AreNotEqual(rightBefore, string.Join(" ", blueRight.Holders().Select(h => $"{h.Name}@{h.Index}")), "the right's holders shifted here");
        Assert.AreEqual(1, blueLeft.Queued); Assert.AreEqual(0, blueRight.Queued);
        Assert.IsFalse(blueLeft.AllPlaced); Assert.IsFalse(blueRight.AllPlaced, "everybody of both moves on the beat");
        // cyan's copy steps the same beat on its own words
        cyanRight.Placed(cyanRight.Me);
        foreach (var who in new[] { "orange", "yellow" }) cyanRight.Heard(cyanRight.Fleet.Member(who));
        foreach (var who in new[] { "blue", "red" }) cyanLeft.Heard(cyanLeft.Fleet.Member(who));
        Assert.AreEqual(0, cyanShow.Beat);
        var cyanStep = cyanLeft.Heard(cyanLeft.Fleet.Member("green"));
        Assert.AreEqual(1, cyanShow.Beat); Assert.AreSame(cyanStep, cyanRight.Route); Assert.AreEqual(1, cyanLeft.Round);
        CollectionAssert.AreEqual(blueRight.Holders().Select(h => $"{h.Name}@{h.Index}").ToList(), cyanRight.Holders().Select(h => $"{h.Name}@{h.Index}").ToList(), "the same holders in every copy");

        // the start of beat 1: the movers of the whole union — six, in both formations
        step.Arrive(step.NextLeg);
        Assert.IsTrue(step.Waiting);
        blueLeft.Aligned(blueLeft.Me);
        foreach (var who in new[] { "red", "green" }) blueLeft.HeardAligned(blueLeft.Fleet.Member(who), 1);
        foreach (var who in new[] { "cyan", "orange" }) blueRight.HeardAligned(blueRight.Fleet.Member(who), 1);
        Assert.IsTrue(step.Waiting);
        blueRight.HeardAligned(blueRight.Fleet.Member("yellow"), 1);
        Assert.IsFalse(step.Waiting);
        WalkToTheEnd(step);

        // BEAT 2: the left has a step left, the right has none — the left steps, the right STAYS placed, and the start waits for the left's movers alone
        blueLeft.Placed(blueLeft.Me); foreach (var who in new[] { "red", "green" }) blueLeft.Heard(blueLeft.Fleet.Member(who));
        Assert.IsFalse(blueShow.CanStep);
        foreach (var who in new[] { "cyan", "orange" }) blueRight.Heard(blueRight.Fleet.Member(who));
        var last = blueRight.Heard(blueRight.Fleet.Member("yellow"));
        Assert.AreEqual(2, blueShow.Beat); Assert.AreEqual(2, blueLeft.Round); Assert.AreEqual(1, blueRight.Round, "the right stayed");
        Assert.IsTrue(blueRight.AllPlaced); Assert.IsTrue(blueRight.MoversAligned, "placed implies lined up: the right holds nobody back");
        Assert.AreSame(last, blueLeft.Route); Assert.AreEqual(0, blueLeft.Queued);
        last.Arrive(last.NextLeg);
        Assert.IsTrue(last.Waiting);
        blueLeft.Aligned(blueLeft.Me); blueLeft.HeardAligned(blueLeft.Fleet.Member("red"), 2);
        Assert.IsTrue(last.Waiting);
        blueLeft.HeardAligned(blueLeft.Fleet.Member("green"), 2);
        Assert.IsFalse(last.Waiting, "the left's three lined up: it sets out, the right stands");
        WalkToTheEnd(last);

        // DONE when every member of every formation finished — the right's finishes count in blue's copy too
        blueLeft.Placed(blueLeft.Me); foreach (var who in new[] { "red", "green" }) blueLeft.Heard(blueLeft.Fleet.Member(who));
        Assert.IsFalse(blueShow.CanStep, "nothing queued anywhere: no beat"); Assert.AreEqual(2, blueShow.Beat);
        blueLeft.Finish(blueLeft.Me);
        Assert.IsTrue(blueLeft.JustFinished);
        foreach (var who in new[] { "red", "green" }) blueLeft.HeardFinished(blueLeft.Fleet.Member(who));
        Assert.IsTrue(blueLeft.IsFinished); Assert.IsFalse(blueShow.IsDone, "the right's three have not finished");
        foreach (var who in new[] { "cyan", "orange" }) blueRight.HeardFinished(blueRight.Fleet.Member(who));
        Assert.IsFalse(blueShow.IsDone);
        blueRight.HeardFinished(blueRight.Fleet.Member("yellow"));
        Assert.IsTrue(blueRight.IsFinished); Assert.IsTrue(blueShow.IsDone);
        // the words of the union's formations are heard here — the right's too — and a loose formation's only when this golem has a route in it
        Assert.IsTrue(blue.Choreography.Hears("tri-left")); Assert.IsTrue(blue.Choreography.Hears("tri-right")); Assert.IsFalse(blue.Choreography.Hears("nothing"));
    }

    // THE START FROM THE POSE THE GOLEM KNOWS (22-oct-2026, night; lab: cyan, facing 2.60, asked a turn as if it faced 0 — the union's take had opened
    // from the word, a position with no heading): the first turn of the take is measured from the heading the golem convened with; and A STRANDED
    // MEMBER TAKEN AGAIN on the warden's word (`take <union>`), from where its body stands, the stranding forgotten
    [TestMethod]
    public void TheUnionsTake_StartsFromTheHeadingTheGolemConvenedWith_AndAStrandedMember_IsTakenAgainOnTheWardensWord()
    {
        var blue = Born("blue");
        var (show, left, _) = Told(blue);
        var six = show.Candidates;
        // blue stands at the left apex facing EAST (0.0): its place by distance is the apex itself, so its route is the facing turn alone — toward the
        // centre, south: a right turn of π/2 from 0.0, not a left turn of π/2 from a heading of 0 measured the other way
        var apex = new Pose(3.0, 7.232, 0.0);
        blue.Wake(apex);
        show.Convene(apex, six.Member(blue));
        foreach (var who in new[] { "red", "green", "orange", "yellow" }) show.Stood(six.Member(who), Homes[who], "n-" + who);
        var route = show.Stood(six.Member("cyan"), Homes["cyan"], "n-cyan");
        Assert.IsNotNull(route); Assert.AreEqual(0, left.PlaceIndex, "the apex, where blue stands");
        Assert.AreEqual("turnRight", route.Order, "facing east, the centre is to the right: the turn is measured from the heading convened with, not from 0");
        Assert.AreEqual(Math.PI / 2, route.Amount, 1e-6);
        // a member stranded — the route failed and the retake found its way closed twice — taken again by the warden from where the body stands
        var red = Born("red");
        var (redShow, redLeft, _) = Told(red);
        red.Wake(new Pose(Homes["red"].X, Homes["red"].Y, 1.0));
        redShow.Convene(red.Destination, redShow.Candidates.Member(red));
        foreach (var who in new[] { "blue", "green", "orange", "yellow", "cyan" }) redShow.Stood(redShow.Candidates.Member(who), Homes[who], "n-" + who);
        Assert.IsNotNull(redLeft.Route); Assert.IsFalse(redLeft.Stranded);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => redLeft.TakeAgain(new Pose(1.0, 1.0, 0.0))).Message, "is not stranded");
        Assert.IsFalse(redShow.IsStranded); Assert.AreEqual("", redShow.StrandedWhy);
        redLeft.Route.Fail("a wall in the way");
        Assert.IsTrue(redLeft.NeedsRetake);
        var retaken = redLeft.Retake(new Pose(Homes["red"].X, Homes["red"].Y, 1.0));
        retaken.Fail("a wall in the way again");
        redLeft.Retake(new Pose(Homes["red"].X, Homes["red"].Y, 1.0));
        Assert.IsTrue(redLeft.Stranded); Assert.IsTrue(redShow.IsStranded); StringAssert.Contains(redShow.StrandedWhy, "twice");
        var again = redLeft.TakeAgain(new Pose(2.0, 5.0, 0.0));
        Assert.IsFalse(redLeft.Stranded); Assert.AreEqual("", redLeft.StrandedWhy); Assert.IsFalse(redShow.IsStranded);
        Assert.IsNotNull(again); Assert.IsTrue(again.IsPending()); Assert.AreSame(again, redLeft.Route);
        Assert.IsFalse(redLeft.NeedsRetake, "a fresh route: nothing ended short");
    }

    // THE REFUSALS OF THE SCORE (propuesta 124): an open formation has no cue, no on-cue and no pull of its own — the link is the union's; the beats
    // by asking are not built yet; a period of zero is no period
    [TestMethod]
    public void AnOpenFormation_HasNoCueOrPullOfItsOwn_AndTheLinkIsTheUnions()
    {
        var blue = Born("blue");
        var six = new Fleet(Six);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Union("u", "a,b", six, Policy.Distance, Link.Ask, new Seconds(5.0), "s")).Message, "not built yet");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Union("u", "a,b", six, Policy.Distance, Link.Cue, new Seconds(0.0), "s")).Message, "zero is no period");
        var show = blue.Choreography.Union("u", "a,b,c", six, Policy.Distance, Link.Cue, new Seconds(7.0), "s");
        Assert.AreEqual(Link.Cue, show.Link); Assert.AreEqual(7.0, show.EverySeconds, 1e-9);
        var a = blue.Choreography.Form("a", blue.Choreography.Figure("triangle", Left, new Meters(3.0)), six, Policy.Distance);
        blue.Choreography.Open(a);
        Assert.AreSame(show, a.Union);
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => a.Cues("b")).Message, "is open: it follows the beats of its union u");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => a.StartsOnCue()).Message, "is open");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => a.Follows("b")).Message, "is open");
        var b = blue.Choreography.Form("b", blue.Choreography.Figure("triangle", Right, new Meters(3.0)), six, Policy.Distance);
        b.Cues("a");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Open(b)).Message, "cannot open: an open formation follows the beats of its union");
        Assert.IsFalse(b.IsOpen); Assert.IsNull(b.Union);
        var c = blue.Choreography.Form("c", blue.Choreography.Figure("triangle", Right, new Meters(3.0)), six, Policy.Distance);
        c.Follows("a");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => blue.Choreography.Open(c)).Message, "cannot open");
        // a loose formation stays as it was: a cue and a pull of its own
        var loose = blue.Choreography.Form("loose", blue.Choreography.Figure("triangle", Right, new Meters(3.0)), six, Policy.Rank);
        loose.Cues("a"); Assert.AreEqual("a", loose.CueTo); Assert.IsNull(loose.Union);
    }

    // THE PROGRAM (propuesta 124, 22-oct-2026, night; Juan: "un solo comando con toda la definición… el dominio debería crear el objeto completo del
    // sindicato y el dominio entiende que debe terminar creando los dos objetos de las formaciones con todas sus instrucciones"): one definition, read by
    // the domain, makes the union, its formations opened into it, their steps and rests, and their finish implied at the end of their part
    [TestMethod]
    public void AProgram_MakesTheWholeUnionFromOneDefinition_WithItsRests_AndEveryFormationFinishesAtTheEndOfItsPart()
    {
        var blue = Born("blue");
        var six = new Fleet(Six);
        const string definition = "tri-left: triangle 3,5.5 side 3 angle 0 places 3 = cw,cw,cw; tri-right: triangle 8,5.5 side 3 places 3 = cw,-,cw";
        var show = blue.Choreography.Program("two-triangles", definition, six, Policy.Distance, Link.Cue, null, "stamp-blue");
        Assert.AreEqual("tri-left,tri-right", show.Bill); Assert.AreEqual(Link.Cue, show.Link); Assert.IsTrue(show.IsKnown, "both formations made and opened into it");
        var left = blue.Choreography.Find("tri-left"); var right = blue.Choreography.Find("tri-right");
        Assert.IsTrue(left.IsOpen && right.IsOpen); Assert.AreSame(show, left.Union); Assert.AreSame(show, right.Union);
        Assert.AreEqual("triangle", left.Figure.Name); Assert.AreEqual(3, left.Capacity); Assert.AreEqual(3, left.Queued); Assert.AreEqual(3, right.Queued, "a rest is a step of the score");
        Assert.AreEqual("stamp-blue", left.Stamp, "the union's stamp: one hand at the game in every copy");
        Assert.IsFalse(left.FinishDue, "the finish is implied, due only when the part is done");
        // the score read: its parts
        var score = Score.Read(definition);
        Assert.AreEqual(2, score.Parts.Count); Assert.AreEqual(3, score.Beats); Assert.AreEqual("cw,-,cw", score.Parts[1].StepsText); Assert.IsNull(score.Parts[1].Steps[1]);
        Assert.AreEqual(0.0, score.Parts[1].Turn.InDegrees, 1e-9, "angle left out: 0");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => Score.Read("a: hexagon 1,1 side 2")).Message, "a triangle, a square, a pentagon or a circle");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => Score.Read("a: triangle 1,1 radius 2")).Message, "is said by its side");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => Score.Read("a: circle 1,1 radius 2")).Message, "told for how many golems");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => Score.Read("a: triangle 1,1 side 2 = cw,left")).Message, "a step is cw, ccw or -");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => Score.Read("a: triangle 1,1 side 2; a: square 2,2 side 1")).Message, "names a twice");
        StringAssert.Contains(Assert.ThrowsException<GolemDomainException>(() => Score.Read("triangle 1,1 side 2")).Message, "names its formation first");

        // the round: the program's line is blue's word; the five others — and the take at once
        Assert.IsNull(show.Convene(new Pose(Homes["blue"].X, Homes["blue"].Y, 0.0), six.Member(blue)));
        foreach (var who in new[] { "red", "green", "orange", "yellow" }) show.Stood(six.Member(who), Homes[who], "n-" + who);
        var route = show.Stood(six.Member("cyan"), Homes["cyan"], "n-cyan");
        Assert.IsTrue(show.IsResolved); Assert.AreEqual("tri-left", show.MineName); Assert.IsNotNull(route);
        WalkToTheEnd(route);
        // beat 1: both step; beat 2: the left steps, the right RESTS — placed, round unchanged, its step consumed; beat 3: both again
        static void Placed(Formation own, Formation other, string[] mates, string[] others) { own.Placed(own.Me); foreach (var m in mates) own.Heard(own.Fleet.Member(m)); foreach (var o in others) other.Heard(other.Fleet.Member(o)); }
        Placed(left, right, new[] { "red", "green" }, new[] { "cyan", "orange", "yellow" });
        Assert.AreEqual(1, show.Beat); Assert.AreEqual(1, right.Round); Assert.IsFalse(right.AllPlaced);
        WalkToTheEnd(left.Route);
        Placed(left, right, new[] { "red", "green" }, new[] { "cyan", "orange", "yellow" });
        Assert.AreEqual(2, show.Beat); Assert.AreEqual(2, left.Round); Assert.AreEqual(1, right.Round, "the right rested: no round of its own"); Assert.IsTrue(right.AllPlaced, "resting, it stays placed");
        Assert.AreEqual(1, right.Queued, "the rest consumed, its last step left"); Assert.IsTrue(right.MoversAligned, "it holds nobody back");
        WalkToTheEnd(left.Route);
        Placed(left, right, new[] { "red", "green" }, Array.Empty<string>());   // the right is placed already: no word of it comes
        Assert.AreEqual(3, show.Beat); Assert.AreEqual(3, left.Round); Assert.AreEqual(2, right.Round, "the right's third step is its second round");
        WalkToTheEnd(left.Route);
        Placed(left, right, new[] { "red", "green" }, new[] { "cyan", "orange", "yellow" });
        Assert.AreEqual(3, show.Beat, "nothing queued: no beat"); Assert.AreEqual(0, left.Queued); Assert.AreEqual(0, right.Queued);
        // the finish implied: due now for blue's part, counted when written (the clock's act), never a non-member's
        Assert.IsTrue(left.FinishDue); Assert.IsFalse(right.FinishDue, "not blue's part");
        left.Finish(left.Me);
        Assert.IsTrue(left.JustFinished);
        foreach (var who in new[] { "red", "green" }) left.HeardFinished(left.Fleet.Member(who));
        foreach (var who in new[] { "cyan", "orange", "yellow" }) right.HeardFinished(right.Fleet.Member(who));
        Assert.IsTrue(show.IsDone);
        // told again under its name: made again
        var again = blue.Choreography.Program("two-triangles", "tri-left: triangle 3,5.5 side 3 places 3 = cw", six, Policy.Distance, Link.Cue, null, "stamp-2");
        Assert.IsTrue(show.Dissolved); Assert.IsTrue(left.Dissolved); Assert.AreEqual("tri-left", again.Bill); Assert.AreEqual(1, blue.Choreography.Find("tri-left").Queued);
    }
}
