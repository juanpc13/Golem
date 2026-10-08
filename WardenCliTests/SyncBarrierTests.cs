using WardenCli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WardenCliTests;

// THE BARRIER (8-oct-2026: yellow, the last to reach a @sync, waited forever on the next one and a peer walked into it)
[TestClass]
public class SyncBarrierTests
{
    [TestMethod]
    public void TheLastToArrive_GoesOnWithTheOthers_AndTheNextBarrierWaitsForAllAgain()
    {
        var barrier = new SyncBarrier(3);
        var first = barrier.ArriveAsync();
        var second = barrier.ArriveAsync();
        Assert.IsFalse(first.IsCompleted, "two of three: still waiting");
        var last = barrier.ArriveAsync();
        Assert.IsTrue(first.IsCompleted && second.IsCompleted, "the third arrived: everybody goes on");
        Assert.IsTrue(last.IsCompleted, "the last one too — it opened the barrier it reached, it does not wait on the next");

        var again = barrier.ArriveAsync();
        Assert.IsFalse(again.IsCompleted, "the next @sync waits for all three again");
    }

    [TestMethod]
    public void AQueueThatFinishes_IsNoLongerWaitedFor()
    {
        var barrier = new SyncBarrier(3);
        var a = barrier.ArriveAsync();
        var b = barrier.ArriveAsync();
        barrier.Leave();   // the third had nothing left
        Assert.IsTrue(a.IsCompleted && b.IsCompleted, "the two that wait go on");
    }
}
