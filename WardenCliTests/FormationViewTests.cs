using System.Text.Json;
using WardenCli;
using WardenCli.Formations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WardenCliTests;

// THE FORMATIONS AS THE GOLEM TELLS THEM (propuesta 104): the console parses the `formations` read, draws it, and composes the lines that
// administer it — never a place of its own
[TestClass]
public class FormationViewTests
{
    [TestMethod]
    public void TheRead_IsParsed_WithItsHoldersAndPlaces_AndTheConsoleComposesTheLines()
    {
        const string json = @"{""count"":2,""told"":[
          {""called"":""square-2"",""shape"":""square"",""atX"":5.5,""atY"":5.5,""length"":2.0,""degrees"":0.0,""policy"":""rank"",""crew"":""blue,green,red,yellow"",""places"":4,""placed"":1,""queued"":0,""round"":0,""mine"":2,
           ""holders"":[{""who"":""blue"",""place"":0,""x"":6.5,""y"":6.5,""ring"":0},{""who"":""green"",""place"":1,""x"":4.5,""y"":6.5,""ring"":0},{""who"":""red"",""place"":2,""x"":4.5,""y"":4.5,""ring"":0},{""who"":""yellow"",""place"":3,""x"":6.5,""y"":4.5,""ring"":0}],
           ""spots"":[{""x"":6.5,""y"":6.5},{""x"":4.5,""y"":6.5},{""x"":4.5,""y"":4.5},{""x"":6.5,""y"":4.5}]},
          {""called"":""ring-1"",""shape"":""circle"",""atX"":5.5,""atY"":5.5,""length"":3.0,""degrees"":0.0,""policy"":""distance"",""crew"":""blue,cyan,green"",""places"":3,""placed"":0,""queued"":1,""round"":0,""mine"":-1,
           ""holders"":[],
           ""spots"":[{""x"":8.5,""y"":5.5},{""x"":4.0,""y"":8.098},{""x"":4.0,""y"":2.902}]}]}";
        var views = FormationView.Parse(JsonDocument.Parse(json).RootElement);
        Assert.AreEqual(2, views.Count);

        var square = views[0];
        Assert.AreEqual("square-2", square.Name);
        Assert.AreEqual(4, square.CrewCount);
        Assert.AreEqual("red", square.HolderOf(2));
        Assert.AreEqual("blue NE · green NW · red SW · yellow SE", square.HoldersText, "the compass points are the console's words, the holders the golem's");
        Assert.AreEqual("  by rank · round 0 · 1/4 placed", square.RoundText);
        Assert.IsInstanceOfType(square.AsFigure(), typeof(Square));
        Assert.AreEqual("form square-2 square --center 3,3 --side 2 --angle 0 --fleet blue,green,red,yellow --by rank", square.FormLine(square.AsFigure()!.At(new Spot(3, 3))),
            "moved on the map: the same formation told again, elsewhere");

        var ring = views[1];
        var figure = ring.AsFigure() as Circle;
        Assert.IsNotNull(figure);
        Assert.AreEqual(3.0, figure!.Radius, 1e-9);
        StringAssert.Contains(ring.HoldersText, "nobody placed yet (by distance)");
        Assert.AreEqual("form ring-1 circle --center 5.5,5.5 --radius 3 --angle 45 --fleet blue,cyan,green --by distance",
                        ring.FormLine(figure.Oriented(45)), "turned on the map: told again");
    }

    // THE DRAFT (9-oct-2026; Juan: "hasta estar seguros de los nuevos ajustes se envían al golem seleccionado"): a figure reshaped on the map is
    // kept on the view and told only when shot — the line is the same `form` again, with the drafted figure
    [TestMethod]
    public void AFigureReshaped_IsADraft_UntilShot_AndTheShotIsTheFormLineWithIt()
    {
        var view = new FormationView("square-2", "square", 5.5, 5.5, 2.0, 0.0, Policy.Rank, "blue,green,red,yellow", 4, 0, 0, 0, -1, Array.Empty<HolderView>(), Array.Empty<(double, double)>());
        Assert.IsFalse(view.HasDraft);
        Assert.AreEqual("", view.DraftText);
        Assert.IsInstanceOfType(view.Projected, typeof(Square), "nothing drafted: the figure as the golem told it");
        var moved = view.AsFigure()!.At(new Spot(3, 3)).Sized(1.5);
        view.Draft = moved;
        Assert.IsTrue(view.HasDraft);
        StringAssert.Contains(view.DraftText, "draft — shot writes it");
        Assert.AreSame(moved, view.Projected, "the map projects the draft");
        Assert.AreEqual(new Spot(5.5, 5.5), view.AsFigure()!.Center, "the golem's own figure is untouched until the shot");
        Assert.AreEqual("form square-2 square --center 3,3 --side 1.5 --angle 0 --fleet blue,green,red,yellow --by rank", view.FormLine(view.Draft!), "the shot");
        view.Draft = null;
        Assert.IsFalse(view.HasDraft, "dropped");
    }

    // THE CUE BETWEEN DANCERS (propuesta 117): the read says whom the fleet cues, whether the formation starts on cue and was cued, how many finished;
    // the row says it and the form line told again keeps --cue and --on-cue
    [TestMethod]
    public void AFormationWithACue_IsReadWithIt_AndItsFormLineKeepsTheCueAndTheOnCue()
    {
        const string json = @"{""count"":2,""told"":[
          {""called"":""tri-left"",""shape"":""triangle"",""atX"":3.0,""atY"":5.5,""length"":3.0,""degrees"":0.0,""policy"":""Rank"",""crew"":""blue,green,red"",""places"":3,""placed"":3,""queued"":0,""round"":3,""mine"":0,
           ""cueTo"":""tri-right"",""onCue"":false,""cued"":true,""finishing"":false,""finished"":2,""done"":false,""cueGiven"":false,""holders"":[],""spots"":[]},
          {""called"":""tri-right"",""shape"":""triangle"",""atX"":8.0,""atY"":5.5,""length"":3.0,""degrees"":0.0,""policy"":""Rank"",""crew"":""cyan,orange,yellow"",""places"":3,""placed"":0,""queued"":3,""round"":0,""mine"":-1,
           ""cueTo"":"""",""onCue"":true,""cued"":false,""finishing"":false,""finished"":0,""done"":false,""cueGiven"":false,""holders"":[],""spots"":[]}]}";
        var views = FormationView.Parse(JsonDocument.Parse(json).RootElement);
        var left = views[0];
        Assert.AreEqual("tri-right", left.CueTo, "the cue names a formation (propuesta 123)");
        Assert.IsFalse(left.OnCue);
        StringAssert.Contains(left.RoundText, "cue → tri-right");
        StringAssert.Contains(left.RoundText, "2/3 finished");
        StringAssert.EndsWith(left.FormLine(left.AsFigure()!), "--by rank --cue tri-right");
        var right = views[1];
        Assert.IsTrue(right.OnCue); Assert.IsFalse(right.Cued);
        StringAssert.Contains(right.RoundText, "on cue — waiting");
        StringAssert.Contains(right.RoundText, "3 step(s) queued");
        StringAssert.EndsWith(right.FormLine(right.AsFigure()!), "--by rank --on-cue");
        var plain = new FormationView("sq", "square", 5.5, 5.5, 2, 0, Policy.Rank, "blue,red", 2, 0, 0, 0, -1, Array.Empty<HolderView>(), Array.Empty<(double, double)>());
        Assert.IsTrue(plain.Cued, "a formation that does not start on cue is as good as cued");
        Assert.AreEqual("", plain.SequenceText);
        Assert.IsFalse(plain.FormLine(plain.AsFigure()!).Contains("--cue"));
        // THE OPEN FORMATION (propuesta 123): the read says it is open, of which union, whether resolved and whether the golem that answered is in it
        const string openJson = @"{""count"":1,""told"":[
          {""called"":""tri-left"",""shape"":""triangle"",""atX"":3.0,""atY"":5.5,""length"":3.0,""degrees"":0.0,""policy"":""Distance"",""crew"":""blue,green,red"",""places"":3,""placed"":0,""queued"":0,""round"":0,""mine"":-1,
           ""open"":true,""union"":""two-triangles"",""resolved"":true,""member"":false,""holders"":[],""spots"":[]}]}";
        var opened = FormationView.Parse(JsonDocument.Parse(openJson).RootElement)[0];
        Assert.IsTrue(opened.Open); Assert.AreEqual("two-triangles", opened.UnionName); Assert.IsTrue(opened.Resolved); Assert.IsFalse(opened.Member);
        StringAssert.Contains(opened.RoundText, "open · union two-triangles · not mine");
        StringAssert.EndsWith(opened.FormLine(opened.AsFigure()!), "--by distance --open");
        Assert.IsFalse(opened.FormLine(opened.AsFigure()!).Contains("--fleet"));
        Assert.IsTrue(plain.Member, "a formation with its fleet told: the golem is of it");
    }

    [TestMethod]
    public void TheFiguresTheConsoleDraws_HaveTheGolemsPlaces()
    {
        var square = new Square(new Spot(5.5, 5.5), 2.0);
        var v = square.Vertices();
        Assert.AreEqual(new Spot(6.5, 6.5), v[0], "north-east first");
        Assert.AreEqual(new Spot(4.5, 6.5), v[1], "then north-west");
        Assert.AreEqual(new Spot(5.5, 6.5), square.Places(5)[1], "five: the corners and the middle of the north side (ajuste 101)");
        Assert.AreEqual("N", new Pentagon(new Spot(5.5, 5.5), 2.0).Label(0, 5));
        Assert.AreEqual("W", new Circle(new Spot(5.5, 5.5), 1.0).Label(2, 4));
        Assert.AreEqual(1, new Circle(new Spot(5.5, 5.5), 1.0).Orbits(4).Count, "a circle is one orbit: the double ring and its two went on 19-oct-2026");
    }
}
