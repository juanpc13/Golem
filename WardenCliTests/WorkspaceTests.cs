using System.IO;
using WardenCli;
using WardenCli.Formations;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WardenCliTests;

[TestClass]
public class WorkspaceTests
{
    [TestMethod]
    public void AWorkspace_KeepsItsFormations_TheirFigureHoldersTurnsEyeAndDraft()
    {
        // a settings file of the test's own: saving a workspace remembers it among the recents, never in the operator's settings
        string settings = Path.Combine(Path.GetTempPath(), $"wardencli-test-{Guid.NewGuid():N}.json");
        string path = Path.Combine(Path.GetTempPath(), $"wardencli-test-{Guid.NewGuid():N}.golemws");
        Environment.SetEnvironmentVariable("WARDENCLI_SETTINGS", settings);
        try
        {
            var fleet = new Fleet(new[] { new Member("blue", null), new Member("red", null), new Member("green", null), new Member("yellow", null) });
            var formation = new Choreography(new Square(new Spot(5.5, 5.5), 2.0), fleet, new ByRank(), 0, clockwise: true).Outcome(3);
            formation.Rotate(Sense.Clockwise);
            formation.Orient(30);
            formation.Resize(2.5);
            formation.Shown = false;
            var golems = new[] { new Golem { Name = "blue", Host = "localhost", Port = 8081, Script = "visit 1,1" } };

            Workspace.Save(path, golems, new[] { formation });
            var (back, formations, problems) = Workspace.Load(path);

            Assert.AreEqual(0, problems.Count);
            Assert.AreEqual("visit 1,1", back.Single().Script, "the scripts travel as they did");
            var again = formations.Single();
            Assert.AreEqual("square #3", again.Name);
            Assert.AreEqual(formation.Holders, again.Holders, "everybody on the vertex it held");
            Assert.AreEqual(new Spot(5.5, 5.5), again.Figure.Center);
            Assert.AreEqual(2.5, again.Figure.Measure);
            Assert.AreEqual(30.0, again.Figure.Angle);
            Assert.AreEqual(-1, again.Turned, "the step it turned");
            Assert.IsFalse(again.Shown, "its eye closed");
            Assert.IsTrue(again.Unshot, "its draft still to shoot");
            CollectionAssert.AreEqual(formation.Places.ToList(), again.Places.ToList(), "the same vertices on the floor");
        }
        finally
        {
            File.Delete(path);
            File.Delete(settings);
            Environment.SetEnvironmentVariable("WARDENCLI_SETTINGS", null);
        }
    }

    [TestMethod]
    public void AWorkspaceFromBefore_OpensWithNoFormations()
    {
        string settings = Path.Combine(Path.GetTempPath(), $"wardencli-test-{Guid.NewGuid():N}.json");
        string path = Path.Combine(Path.GetTempPath(), $"wardencli-test-{Guid.NewGuid():N}.golemws");
        Environment.SetEnvironmentVariable("WARDENCLI_SETTINGS", settings);
        try
        {
            File.WriteAllText(path, "{ \"Kind\": \"golem workspace\", \"Version\": 1, \"Golems\": [ { \"Name\": \"red\", \"Host\": \"localhost\", \"Port\": 8082, \"Script\": \"\" } ] }");
            var (golems, formations, problems) = Workspace.Load(path);
            Assert.AreEqual("red", golems.Single().Name);
            Assert.AreEqual(0, formations.Count, "version 1 knew no formations");
            Assert.AreEqual(0, problems.Count);
        }
        finally
        {
            File.Delete(path);
            File.Delete(settings);
            Environment.SetEnvironmentVariable("WARDENCLI_SETTINGS", null);
        }
    }
}
