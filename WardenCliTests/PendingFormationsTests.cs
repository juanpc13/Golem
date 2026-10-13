using WardenCli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WardenCliTests;

// THE FORMATIONS TO SEND (13-oct-2026; Juan: "poder ver algo que aún no ha sido creado en el golem… construir todo el script de todos para armar la
// coreografía completa"): a form line on a tab, not sent, is listed as the console reads it — its figure and places by the console's own geometry,
// the holders by rank as the golem would resolve them — and its lines are edited in place: a step after the take, the shot rewriting the form
[TestClass]
public class PendingFormationsTests
{
    [TestMethod]
    public void AFormLineOnATab_IsAFormationToSend_WithItsPlacesByRank_AndTheTabsItIsOn()
    {
        var blue = new Golem { Name = "blue", Host = "localhost", Port = 8081, Script = "form square-3 square --center 5.5,5.5 --side 2 --angle 0 --fleet blue,red,green,yellow --by rank\ntake square-3" };
        var red = new Golem { Name = "red", Host = "localhost", Port = 8082, Script = "form square-3 square --center 5.5,5.5 --side 2 --angle 0 --fleet blue,red,green,yellow --by rank\ntake square-3" };
        var green = new Golem { Name = "green", Host = "localhost", Port = 8083, Script = "visit 2,2" };
        var views = PendingFormations.Parse(new[] { blue, red, green });
        Assert.AreEqual(1, views.Count, "one formation, told on two tabs");
        var v = views[0];
        Assert.AreEqual("square-3", v.Name);
        Assert.IsTrue(v.IsPending);
        Assert.AreEqual("blue, red", v.PendingOn, "the tabs it is on");
        Assert.AreEqual("square", v.Shape);
        Assert.AreEqual(2.0, v.Measure);
        Assert.AreEqual("blue,green,red,yellow", v.Crew, "the names sorted: the rank");
        Assert.AreEqual(4, v.Spots.Count);
        Assert.AreEqual("blue NE · green NW · red SW · yellow SE", v.HoldersText, "by rank, resolved as the golem will");
        Assert.AreEqual("  to send · on blue, red", v.OwnerText);
        Assert.IsNotNull(v.AsFigure(), "the console draws it");

        // by distance nothing is anticipated; a circle by its radius; the fleet left out is every golem of the workspace
        var cyan = new Golem { Name = "cyan", Host = "localhost", Port = 8086, Script = "form ring-1 circle --center 5,5 --radius 3 --by distance" };
        var ring = PendingFormations.Parse(new[] { cyan, blue }).Single(x => x.Name == "ring-1");
        Assert.AreEqual("distance", ring.Policy);
        Assert.AreEqual(0, ring.Holders.Count);
        Assert.AreEqual("blue,cyan", ring.Crew);
        Assert.AreEqual(2, ring.Spots.Count);
        StringAssert.Contains(ring.HoldersText, "nobody placed yet (by distance)");

        // a line without a centre or a size is no formation
        var bad = new Golem { Name = "x", Host = "localhost", Port = 8090, Script = "form nope square --fleet a,b" };
        Assert.AreEqual(0, PendingFormations.Parse(new[] { bad }).Count);
    }

    [TestMethod]
    public void ATabsLines_AreEditedInPlace_AStepAfterTheTake_TheFormRewritten_TheLinesTakenOut()
    {
        var g = new Golem { Name = "blue", Host = "localhost", Port = 8081, Script = "visit 1,1\nform sq square --center 5.5,5.5 --side 2 --angle 0 --fleet blue,red --by rank\ntake sq\nvisit 9,9" };
        g.EnqueueAfter(l => l.StartsWith("take sq") || l.StartsWith("rotate sq "), "rotate sq clockwise");
        g.EnqueueAfter(l => l.StartsWith("take sq") || l.StartsWith("rotate sq "), "rotate sq counterclockwise");
        CollectionAssert.AreEqual(new[] { "visit 1,1", "form sq square --center 5.5,5.5 --side 2 --angle 0 --fleet blue,red --by rank", "take sq", "rotate sq clockwise", "rotate sq counterclockwise", "visit 9,9" }, g.Lines(),
                                  "each step right after the take, or after the last step — the visit after them stays last");
        Assert.IsTrue(g.Replace(l => l.StartsWith("form sq "), "form sq square --center 6,6 --side 3 --angle 45 --fleet blue,red --by rank"), "the form line rewritten where it is");
        Assert.AreEqual("form sq square --center 6,6 --side 3 --angle 45 --fleet blue,red --by rank", g.Lines()[1]);
        Assert.IsFalse(g.Replace(l => l.StartsWith("form other "), "form other circle --center 1,1 --radius 1"), "none to replace: added at the end");
        Assert.AreEqual(3, g.RemoveLines(l => l.StartsWith("form sq ") || l.StartsWith("take sq") || l.StartsWith("rotate sq ")) - 1, "the formation's lines taken out, the visits kept");
        CollectionAssert.AreEqual(new[] { "visit 1,1", "visit 9,9", "form other circle --center 1,1 --radius 1" }, g.Lines());
        g.EnqueueAfter(l => l.StartsWith("take none"), "rotate none clockwise");
        Assert.AreEqual("rotate none clockwise", g.Lines().Last(), "no take to follow: at the end");
    }
}
