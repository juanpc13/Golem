using GolemAPI.Controllers;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// What the operator sends arrives as JSON, typed and validated before any script runs (Juan, 16-sep-2026): each
// request says what is wrong with it, in plain words, and a sound one says nothing.
[TestClass]
public class RequestValidationTests
{
    [TestMethod]
    public void AnErrand_NeedsAtLeastOnePoint_EachWithBothCoordinates_Finite()
    {
        Assert.AreEqual("give at least one stop: " + ErrandRequest.Shape, new ErrandRequest(null).Problems().Single());
        Assert.AreEqual("give at least one stop: " + ErrandRequest.Shape, new ErrandRequest(new()).Problems().Single());
        Assert.AreEqual(0, new ErrandRequest(new() { new StopRequest(2.0, 9.5), new StopRequest(9.0, 8.0) }).Problems().Count(), "two points: sound");
        StringAssert.Contains(new ErrandRequest(new() { new StopRequest(1.0, null) }).Problems().Single(), "stop 1: a point needs both x and y");
        StringAssert.Contains(new ErrandRequest(new() { new StopRequest(null, null) }).Problems().Single(), "stop 1: a point needs both x and y");
        StringAssert.Contains(new ErrandRequest(new() { new StopRequest(double.NaN, 2.0) }).Problems().Single(), "stop 1: x and y must be finite numbers");
        StringAssert.Contains(new ErrandRequest(new() { new StopRequest(2.0, 9.5), null }).Problems().Single(), "stop 2: is empty");
        var tooMany = new ErrandRequest(Enumerable.Repeat(new StopRequest(2.0, 9.5), ErrandRequest.MostStops + 1).ToList());
        StringAssert.Contains(tooMany.Problems().Single(), $"at most {ErrandRequest.MostStops} stops");
    }

    [TestMethod]
    public void APoint_NeedsBothCoordinates_Finite()
    {
        Assert.AreEqual(0, new PointRequest(5.2, 5.8).Problems().Count());
        StringAssert.Contains(new PointRequest(5.2, null).Problems().Single(), "give both x and y");
        StringAssert.Contains(new PointRequest(double.PositiveInfinity, 1.0).Problems().Single(), "finite");
    }

    [TestMethod]
    public void AQuery_NeedsAScript_NotTooLong()
    {
        Assert.AreEqual(0, new QueryRequest("g.PendingRoutes().Count").Problems().Count());
        StringAssert.Contains(new QueryRequest("  ").Problems().Single(), "give a script");
        StringAssert.Contains(new QueryRequest(new string('x', QueryRequest.LongestScript + 1)).Problems().Single(), "at most");
    }

    [TestMethod]
    public void AReset_SaysWhetherThePeersResetToo()
    {
        Assert.AreEqual(0, new ResetRequest(false).Problems().Count());
        StringAssert.Contains(new ResetRequest(null).Problems().Single(), "say whether the peers reset too");
    }
}
