using System.Text.Json;
using WardenCli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WardenCliTests;

// THE UNIONS AS THE GOLEM TELLS THEM (22-oct-2026; Juan: "un apartado o endpoint de las unionize que existen o a las que pertenece el golem, así uno
// puede hacerle GET en el CLI para ver las formaciones que tiene encima esa unionize"): the console parses the `unions` read and shows the formations
// under each union with who went where — it decides nothing
[TestClass]
public class UnionViewTests
{
    [TestMethod]
    public void TheRead_IsParsed_WithItsBillAndShares_AndTheRowSaysWhoWentWhere()
    {
        const string json = @"{""count"":2,""shows"":[
          {""called"":""two-triangles"",""nonce"":""u-1"",""formations"":""tri-left,tri-right"",""candidates"":""blue,cyan,green,orange,red,yellow"",""policy"":""Distance"",
           ""stood"":6,""known"":true,""complete"":true,""resolved"":true,""stuck"":false,""why"":"""",""member"":true,""mine"":""tri-right"",""leftOut"":"""",""games"":"""",""link"":""Cue"",""beat"":2,""done"":false,
           ""shares"":[{""who"":""blue"",""in"":""tri-right"",""place"":0},{""who"":""cyan"",""in"":""tri-left"",""place"":1},{""who"":""green"",""in"":""tri-left"",""place"":0},
                      {""who"":""orange"",""in"":""tri-right"",""place"":2},{""who"":""red"",""in"":""tri-left"",""place"":2},{""who"":""yellow"",""in"":""tri-right"",""place"":1}]},
          {""called"":""show-2"",""nonce"":""u-2"",""formations"":""sq-1"",""candidates"":""blue,green,red"",""policy"":""RockPaperScissors"",
           ""stood"":1,""known"":false,""complete"":false,""resolved"":false,""stuck"":false,""why"":"""",""member"":false,""mine"":"""",""leftOut"":"""",""games"":"""",""shares"":[]}]}";
        var views = UnionView.Parse(JsonDocument.Parse(json).RootElement);
        Assert.AreEqual(2, views.Count);

        var two = views[0];
        Assert.AreEqual("two-triangles", two.Name);
        Assert.AreEqual("tri-left → tri-right", two.BillText);
        Assert.AreEqual("dancing — beat 2", two.StateText, "the beats (propuesta 124)");
        Assert.IsTrue(two.IsResolved); Assert.AreEqual("cue", two.LinkWord); StringAssert.Contains(two.CandidatesText, "the beats by the cue");
        Assert.AreEqual("done — 3 beats", (two with { Done = true, Beat = 3 }).StateText);
        Assert.AreEqual("resolved — taking", (two with { Beat = 0 }).StateText);
        Assert.AreEqual("mine: tri-right", two.MineText);
        Assert.AreEqual("tri-left: green (0), cyan (1), red (2)\ntri-right: blue (0), yellow (1), orange (2)", two.FormationsText, "the formations under the union, each with who the law sent there");
        StringAssert.Contains(two.CandidatesText, "by distance");

        var show = views[1];
        Assert.AreEqual("waiting for the words — 1/3 stood", show.StateText);
        Assert.AreEqual("rps", show.PolicyWord);
        Assert.AreEqual("sq-1: —", show.FormationsText, "nothing shared before the round resolved");
        Assert.AreEqual("", show.MineText);
    }

    [TestMethod]
    public void AStuckUnion_SaysWhy()
    {
        const string json = @"{""count"":1,""shows"":[{""called"":""u"",""nonce"":""n"",""formations"":""a,b"",""candidates"":""blue,red"",""policy"":""Distance"",
           ""stood"":2,""known"":true,""complete"":true,""resolved"":false,""stuck"":true,""why"":""b would get nobody"",""member"":false,""mine"":"""",""leftOut"":"""",""games"":"""",""shares"":[]}]}";
        var view = UnionView.Parse(JsonDocument.Parse(json).RootElement)[0];
        Assert.IsTrue(view.IsStuck);
        Assert.AreEqual("stuck — b would get nobody", view.StateText);
    }

    [TestMethod]
    public void AUnionWrittenOnTheTabs_AndNotSent_IsListedToSend()
    {
        var blue = new Golem { Name = "blue", Host = "localhost", Port = 8081, Script = "union two-triangles tri-left,tri-right --by rps --link ask --every 5s\r\nform tri-left triangle --center 3,5.5 --side 3 --by rps --open --places 3\r\ntake two-triangles" };
        var red = new Golem { Name = "red", Host = "localhost", Port = 8082, Script = "union two-triangles tri-left,tri-right --by rps\r\ntake two-triangles" };
        var views = PendingFormations.Unions(new[] { blue, red });
        Assert.AreEqual(1, views.Count);
        Assert.IsTrue(views[0].IsPending);
        Assert.AreEqual("to send · on blue, red", views[0].StateText);
        Assert.AreEqual("tri-left → tri-right", views[0].BillText);
        Assert.AreEqual("rps", views[0].PolicyWord);
        Assert.AreEqual("blue,red", views[0].Candidates, "no --fleet: every golem of the workspace is a candidate");
        Assert.AreEqual("ask", views[0].LinkWord, "the link as the line says it (propuesta 124)");
        // ONE LINE (propuesta 124): a program line is the union to send, its bill the --form clauses
        var green = new Golem { Name = "green", Host = "localhost", Port = 8083, Script = "program show-1 --by distance --link cue --form tri-left triangle 3,5.5 side 3 angle 0 places 3 --form tri-right triangle 8,5.5 side 3 places 3 --score tri-left=cw,cw,cw tri-right=cw,cw,-" };
        var programmed = PendingFormations.Unions(new[] { green });
        Assert.AreEqual(1, programmed.Count); Assert.AreEqual("show-1", programmed[0].Name); Assert.AreEqual("tri-left → tri-right", programmed[0].BillText);
        Assert.AreEqual("cue", programmed[0].LinkWord); Assert.AreEqual("to send · on green", programmed[0].StateText);
        Assert.AreEqual("show-1", PendingFormations.UnionsOf(green.Script)["tri-right"], "a formation of the program is the program's line");
    }
}
