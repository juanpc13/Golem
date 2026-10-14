using System.Text.Json;
using WardenCli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WardenCliTests;

// THE DISPUTES TAB (14-oct-2026): the golem's `disputes` read, parsed as the console shows it — won or yielded, against whom, both distances
[TestClass]
public class DisputeViewTests
{
    [TestMethod]
    public void TheDisputesRead_IsShownAsWonOrYielded_WithTheRivalAndBothDistances()
    {
        var json = JsonDocument.Parse("{\"count\":2,\"disputes\":[{\"called\":\"wheel\",\"place\":4,\"x\":4,\"y\":2.9,\"rival\":\"red\",\"won\":false,\"mine\":1.7503,\"theirs\":1.2},"
                                    + "{\"called\":\"helix\",\"place\":0,\"x\":5.5,\"y\":5.5,\"rival\":\"cyan\",\"won\":true,\"mine\":0.4,\"theirs\":0.9,\"game\":\"rock beats scissors after 1 tie\"}]}").RootElement;
        var list = DisputeView.Parse(json);
        Assert.AreEqual(2, list.Count);
        Assert.AreEqual("↷ yielded", list[0].Title);
        Assert.AreEqual("place 4 of wheel to red", list[0].Text);
        Assert.AreEqual("1.75 m from it · red 1.2 m", list[0].Distances);
        Assert.AreEqual("✓ won", list[1].Title);
        Assert.AreEqual("place 0 of helix against cyan", list[1].Text);
        Assert.AreEqual("(5.5, 5.5)", list[1].Where);
        Assert.AreEqual("rock beats scissors after 1 tie · 0.4 m from it · cyan 0.9 m", list[1].Distances, "by the game, the throw that decided first (propuesta 115)");
        Assert.AreEqual(0, DisputeView.Parse(JsonDocument.Parse("{\"count\":0}").RootElement).Count, "no disputes: an empty list");
    }
}
