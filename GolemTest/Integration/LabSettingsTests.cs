using GolemTest.World;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// WHICH WORLD THE SCENARIOS RUN AGAINST (propuesta 52, fase 3): the versioned appsettings.json says mock; a local overlay or
// the environment may say gazebo. These tests change the environment of THIS process only, and put it back.
[TestClass]
public class LabSettingsTests
{
    [TestMethod]
    public void TheVersionedSettings_AskForTheMockWorld_AndNameTheFleet()
    {
        using var _ = new Environment("GOLEM_LAB_WORLD", null);
        var lab = LabSettings.Load();
        if (File.Exists(Path.Combine(AppContext.BaseDirectory, "appsettings.local.json")))
            Assert.Inconclusive("a local overlay is wired here: the versioned default cannot be read alone");
        Assert.AreEqual("mock", lab.World, "with nothing said, the scenarios never need the simulator");
        Assert.IsFalse(lab.InGazebo);
        CollectionAssert.AreEquivalent(new[] { "blue", "red", "green" }, lab.Golems.Keys.ToArray());
        Assert.AreEqual(new Uri("http://localhost:8082/"), lab.Golems["red"]);
        Assert.AreEqual(new Uri("ws://localhost:9090"), lab.Rosbridge);
        Assert.AreEqual(TimeSpan.FromSeconds(60), lab.Patience);
    }

    [TestMethod]
    public void TheEnvironment_AsksForGazebo_WithoutTouchingAFile()
    {
        using var _ = new Environment("GOLEM_LAB_WORLD", "gazebo");
        var lab = LabSettings.Load();
        Assert.IsTrue(lab.InGazebo);
        Assert.AreEqual(TimeSpan.FromSeconds(180), lab.Patience, "Gazebo drives in real time: more patience");
    }

    [TestMethod]
    public void AWorldThatIsNeither_IsRefused()
    {
        using var _ = new Environment("GOLEM_LAB_WORLD", "turtlesim");
        var e = Assert.ThrowsException<InvalidOperationException>(() => LabSettings.Load());
        StringAssert.Contains(e.Message, "'mock' or 'gazebo'");
    }

    // A variable of this process set for one test and put back after it.
    private sealed class Environment : IDisposable
    {
        private readonly string name, before;
        public Environment(string name, string value)
        {
            this.name = name;
            before = System.Environment.GetEnvironmentVariable(name);
            System.Environment.SetEnvironmentVariable(name, value);
        }
        public void Dispose() => System.Environment.SetEnvironmentVariable(name, before);
    }
}
