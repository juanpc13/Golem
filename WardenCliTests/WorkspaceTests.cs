using System.IO;
using WardenCli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WardenCliTests;

// THE WORKSPACE (propuesta 93b): one file with the golems and the script each was left with. Since propuesta 104 (8-oct-2026) it keeps no
// formation: the formations are the golems' own, read from each.
[TestClass]
public class WorkspaceTests
{
    [TestMethod]
    public void AWorkspace_KeepsItsGolems_AndTheirScripts()
    {
        // a settings file of the test's own: saving a workspace remembers it among the recents, never in the operator's settings
        string settings = Path.Combine(Path.GetTempPath(), $"wardencli-test-{Guid.NewGuid():N}.json");
        string path = Path.Combine(Path.GetTempPath(), $"wardencli-test-{Guid.NewGuid():N}.golemws");
        Environment.SetEnvironmentVariable("WARDENCLI_SETTINGS", settings);
        try
        {
            var golems = new[] { new Golem { Name = "blue", Host = "localhost", Port = 8081, Script = "visit 1,1" }, new Golem { Name = "red", Host = "localhost", Port = 8082, Script = "" } };
            Workspace.Save(path, golems);
            var back = Workspace.Load(path);
            Assert.AreEqual(2, back.Count);
            Assert.AreEqual("visit 1,1", back[0].Script, "the scripts travel as they were");
            Assert.AreEqual(8082, back[1].Port);
        }
        finally
        {
            File.Delete(path);
            File.Delete(settings);
            Environment.SetEnvironmentVariable("WARDENCLI_SETTINGS", null);
        }
    }

    [TestMethod]
    public void AWorkspaceFromBefore_OpensWithItsGolems_AndItsFormationsAreLeftBehind()
    {
        string settings = Path.Combine(Path.GetTempPath(), $"wardencli-test-{Guid.NewGuid():N}.json");
        string path = Path.Combine(Path.GetTempPath(), $"wardencli-test-{Guid.NewGuid():N}.golemws");
        Environment.SetEnvironmentVariable("WARDENCLI_SETTINGS", settings);
        try
        {
            File.WriteAllText(path, "{ \"Kind\": \"golem workspace\", \"Version\": 2, \"Golems\": [ { \"Name\": \"red\", \"Host\": \"localhost\", \"Port\": 8082, \"Script\": \"\" } ], \"Formations\": [ { \"Number\": 1, \"Figure\": \"square\" } ] }");
            var golems = Workspace.Load(path);
            Assert.AreEqual("red", golems.Single().Name, "a version 2 file opens; its formations were the console's and are no more (propuesta 104)");
        }
        finally
        {
            File.Delete(path);
            File.Delete(settings);
            Environment.SetEnvironmentVariable("WARDENCLI_SETTINGS", null);
        }
    }
}
