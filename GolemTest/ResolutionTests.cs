using GolemAPI.Choreography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// A MEASURE AT ITS RESOLUTION (ajuste 53, 23-sep-2026): what the host writes into the journal, and sends the body, is rounded to the
// millimetre and the milliradian — the noise of the sensor stays out of the journal, nothing it could measure is lost.
[TestClass]
public class ResolutionTests
{
    [TestMethod]
    public void ALength_IsKeptToTheMillimetre_AndAnAngle_ToTheMilliradian()
    {
        Assert.AreEqual(5.855, Resolution.Metres(5.855150933786965));
        Assert.AreEqual(4.780, Resolution.Metres(4.779588202378993));
        Assert.AreEqual(1.456, Resolution.Radians(1.4555633009344278));
        Assert.AreEqual(-1.571, Resolution.Radians(-1.5707963267949), "a half-milliradian rounds away from zero, either side");
        Assert.AreEqual(0.003, Resolution.Metres(0.0025));
        Assert.AreEqual(5.5, Resolution.Metres(5.5), "a value already round stays as it is");
        Assert.AreEqual("5.855", Resolution.Written(Resolution.Metres(5.855150933786965)));
    }
}
