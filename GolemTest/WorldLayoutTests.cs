using GolemAPI.Membrane;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// THE WORLD'S LAYOUT (propuesta 109): where each map lies in the simulator's world — the host's fact, translated at the edges
[TestClass]
public class WorldLayoutTests
{
    [TestMethod]
    public void TheMapsLieSideBySide_APoseIsReadInItsMapsFrame_AndAPointIsCarriedIntoTheWorld()
    {
        var layout = WorldLayout.Parse("open-floor=0,0,11,11;warehouse=13,0,11,11;maze=26,0,10.5,10.5");
        Assert.AreEqual(3, layout.Regions.Count);
        Assert.AreEqual("warehouse", layout.At(19.3, 10.4), "blue's mark lies in the warehouse's region");
        Assert.AreEqual("maze", layout.At(31.25, 0.75));
        Assert.AreEqual("open-floor", layout.At(5.5, 5.5));
        Assert.IsNull(layout.At(12.0, 20.0), "nowhere: between the maps and beyond");
        var pose = layout.ToMap("warehouse", new Pose(19.3, 10.4, 0.5));
        Assert.AreEqual(6.3, pose.X, 1e-9); Assert.AreEqual(10.4, pose.Y, 1e-9); Assert.AreEqual(0.5, pose.Theta, 1e-9);
        Assert.AreEqual((31.25, 0.75), layout.ToWorld("maze", 5.25, 0.75), "the maze's cradle, in the world");
        Assert.AreEqual((5.5, 5.5), layout.ToWorld("attic", 5.5, 5.5), "a map the layout knows nothing of keeps the world's frame");
        Assert.IsNull(layout.ToMap("maze", null));
        Assert.AreEqual(2.0, WorldLayout.OneFrame.ToMap("warehouse", new Pose(2.0, 3.0, 0.0)).X, 1e-9, "nothing told: one frame");
        StringAssert.Contains(Assert.ThrowsException<ArgumentException>(() => WorldLayout.Parse("maze=1,2")).Message, "needs x,y,w,h");
    }
}
