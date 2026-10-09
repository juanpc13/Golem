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
          {""called"":""square-2"",""shape"":""square"",""atX"":5.5,""atY"":5.5,""length"":2.0,""degrees"":0.0,""policy"":""rank"",""crew"":""blue,green,red,yellow"",""division"":0,""places"":4,""placed"":1,""queued"":0,""round"":0,""mine"":2,
           ""holders"":[{""who"":""blue"",""place"":0,""x"":6.5,""y"":6.5,""ring"":0},{""who"":""green"",""place"":1,""x"":4.5,""y"":6.5,""ring"":0},{""who"":""red"",""place"":2,""x"":4.5,""y"":4.5,""ring"":0},{""who"":""yellow"",""place"":3,""x"":6.5,""y"":4.5,""ring"":0}],
           ""spots"":[{""x"":6.5,""y"":6.5},{""x"":4.5,""y"":6.5},{""x"":4.5,""y"":4.5},{""x"":6.5,""y"":4.5}]},
          {""called"":""double-ring-1"",""shape"":""double ring"",""atX"":5.5,""atY"":5.5,""length"":3.0,""degrees"":0.0,""policy"":""distance"",""crew"":""blue,cyan,green,orange,red,yellow"",""division"":3,""places"":6,""placed"":0,""queued"":1,""round"":0,""mine"":-1,
           ""holders"":[],
           ""spots"":[{""x"":8.5,""y"":5.5},{""x"":4.0,""y"":8.098},{""x"":4.0,""y"":2.902},{""x"":6.7,""y"":5.5},{""x"":4.9,""y"":6.539},{""x"":4.9,""y"":4.461}]}]}";
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

        var rings = views[1];
        Assert.IsTrue(rings.IsDoubleRing);
        Assert.AreEqual(3, rings.Division);
        var figure = rings.AsFigure() as DoubleRing;
        Assert.IsNotNull(figure);
        Assert.AreEqual(3.0, figure!.OuterRadius, 1e-9);
        Assert.AreEqual(1.2, figure.InnerRadius, 1e-3, "the inner radius read from the inner ring's first place");
        Assert.AreEqual(3, figure.OuterPlaces); Assert.AreEqual(3, figure.InnerPlaces);
        StringAssert.Contains(rings.HoldersText, "nobody placed yet (by distance)");
        Assert.AreEqual("form double-ring-1 double-ring --center 5.5,5.5 --radius 3 --inner-radius 1.2 --angle 45 --fleet blue,cyan,green --inner-fleet orange,red,yellow --by distance",
                        rings.FormLine(figure.Oriented(45)), "turned on the map: told again with its two rings of golems");
    }

    // THE DRAFT (9-oct-2026; Juan: "hasta estar seguros de los nuevos ajustes se envían al golem seleccionado"): a figure reshaped on the map is
    // kept on the view and told only when shot — the line is the same `form` again, with the drafted figure
    [TestMethod]
    public void AFigureReshaped_IsADraft_UntilShot_AndTheShotIsTheFormLineWithIt()
    {
        var view = new FormationView("square-2", "square", 5.5, 5.5, 2.0, 0.0, "rank", "blue,green,red,yellow", 0, 4, 0, 0, 0, -1, Array.Empty<HolderView>(), Array.Empty<(double, double)>());
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
        var rings = new DoubleRing(new Spot(5.5, 5.5), 2.5, 1.0, 2, 2);
        Assert.AreEqual(4, rings.Places(4).Count);
        Assert.AreEqual(new Spot(8, 5.5), rings.Places(4)[0], "the outer ring's first place due east");
        Assert.AreEqual(new Spot(4.5, 5.5), rings.Places(4)[3], "the inner ring's second place due west");
        Assert.IsTrue(rings.IsInner(3));
        Assert.AreEqual(2, rings.Orbits(4).Count);
    }
}
