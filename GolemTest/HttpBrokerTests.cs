using Choreography.Transport.Brokered;
using GolemAPI.Membrane;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// THE WIRE ANSWERS ON RECEIPT (ajuste 76, 1-oct-2026): a record that arrives on /tell is retained, queued for its topic and the
// request returns; the handlers — the uptake and its ack — run off the request, in the order the records came. A handler that
// takes its time (an ack retried for seconds) no longer holds the teller's POST, which is what made words between golems
// convening at once take 12 to 18 s.
[TestClass]
public class HttpBrokerTests
{
    [TestMethod]
    public void Deliver_ReturnsBeforeTheHandlerIsDone_AndTheHandlerStillRuns()
    {
        var wire = new HttpBroker("red", new Dictionary<string, Uri>(), TimeSpan.FromSeconds(1));
        var handlerMayFinish = new ManualResetEventSlim(false);
        var handlerRan = new ManualResetEventSlim(false);
        wire.Subscribe("tell-red", _ => { handlerRan.Set(); handlerMayFinish.Wait(TimeSpan.FromSeconds(5)); });

        var watch = System.Diagnostics.Stopwatch.StartNew();
        bool taken = wire.Deliver("tell-red", "blue", new Dictionary<string, string>(), "a word");
        watch.Stop();

        Assert.IsTrue(taken, "this golem subscribes the topic: the record is taken");
        Assert.IsTrue(watch.ElapsedMilliseconds < 500, $"the request does not wait for the handler: {watch.ElapsedMilliseconds} ms");
        Assert.IsTrue(handlerRan.Wait(TimeSpan.FromSeconds(2)), "and the handler runs, off the request");
        handlerMayFinish.Set();
    }

    [TestMethod]
    public void RecordsOfOneTopic_AreHandedToTheHandler_InTheOrderTheyArrived_EvenWhenOneThrows()
    {
        var wire = new HttpBroker("red", new Dictionary<string, Uri>(), TimeSpan.FromSeconds(1));
        var seen = new List<string>();
        var third = new CountdownEvent(3);
        wire.Subscribe("tell-red", record =>
        {
            lock (seen) seen.Add(record.Value);
            third.Signal();
            if (record.Value == "second") throw new InvalidOperationException("the handler failed on this one");
        });

        wire.Deliver("tell-red", "blue", new Dictionary<string, string>(), "first");
        wire.Deliver("tell-red", "blue", new Dictionary<string, string>(), "second");
        wire.Deliver("tell-red", "blue", new Dictionary<string, string>(), "third");

        Assert.IsTrue(third.Wait(TimeSpan.FromSeconds(2)), "every record reaches the handler");
        lock (seen) CollectionAssert.AreEqual(new[] { "first", "second", "third" }, seen, "in arrival order; the one that threw did not stop the queue");
    }

    [TestMethod]
    public void ATopicNobodySubscribes_IsNotTaken_ButIsRetainedForALateSubscriber()
    {
        var wire = new HttpBroker("red", new Dictionary<string, Uri>(), TimeSpan.FromSeconds(1));
        Assert.IsFalse(wire.Deliver("tell-red", "blue", new Dictionary<string, string>(), "early"), "nobody here subscribes it: the origin keeps retrying");

        var heard = new List<string>();
        var once = new ManualResetEventSlim(false);
        wire.Subscribe("tell-red", record => { lock (heard) heard.Add(record.Value); once.Set(); });
        Assert.IsTrue(once.Wait(TimeSpan.FromSeconds(2)));
        lock (heard) CollectionAssert.AreEqual(new[] { "early" }, heard, "the retained record is replayed to the late subscriber");
    }
}
