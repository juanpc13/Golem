using GolemAPI;
using GolemDomain.Layouts;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// The host writes the maze's release verbatim (a fixed template, like every map's); the catalog renders it from the drawing. The two
// must say the same (propuesta 108).
[TestClass]
public class MazeReleaseTests
{
    [TestMethod]
    public void TheHostsMazeRelease_IsWhatTheCatalogRenders()
    {
        Assert.AreEqual(Catalog.Maze().AsRelease(staged: true), GolemPerformance.MazeRelease.Replace(((char)13).ToString(), ""));   // the host's file is CRLF; the engine reads either
    }

    [TestMethod]
    public void TheHostsCrossCorridorsRelease_IsWhatTheCatalogRenders()
    {
        Assert.AreEqual(Catalog.CrossCorridors().AsRelease(staged: true), GolemPerformance.CrossCorridorsRelease.Replace(((char)13).ToString(), ""));   // propuesta 125: the road
    }

    [TestMethod]
    public void TheHostsRoadRelease_IsWhatTheCatalogRenders()
    {
        Assert.AreEqual(Catalog.Road().AsRelease(staged: true), GolemPerformance.RoadRelease.Replace(((char)13).ToString(), ""));   // propuesta 125 §12: two lanes and queues
    }
}
