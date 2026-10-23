using GolemAPI.Choreography;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// What a script answered (Order.cs, `Answer.Of`): A REFUSAL IS RECOGNISED BY ITS SHAPE — the check's sidecar, the top-level
// property "EWI" of a JSON object — never by a substring and never by emptiness (the Puppeteer guides control-flow §9 and
// output-and-controller §10, 23-oct-2026). Only Error and Warning refuse, and their text is the reason the operator reads.
[TestClass]
public class AnswerTests
{
    [TestMethod]
    public void ARefusedCheck_IsRecognisedByItsSidecar_AndItsReasonIsTheErrorsText()
    {
        var refused = Answer.Of("{\"EWI\":[{\"Error\":\"a formation 'tri-left' starts on cue\"}]}");
        Assert.IsFalse(refused.Ok); Assert.IsNull(refused.Order);
        Assert.AreEqual("a formation 'tri-left' starts on cue", refused.Refused);
        var warned = Answer.Of("{\"EWI\":[{\"Warning\":\"nothing pending\"}]}");
        Assert.IsFalse(warned.Ok, "a Warning refuses too"); Assert.AreEqual("nothing pending", warned.Refused);
        var second = Answer.Of("{\"EWI\":[{\"Information\":\"advisory\"},{\"Error\":\"no road\"}]}");
        Assert.AreEqual("no road", second.Refused, "every reason of the sidecar is read, not the first alone");
    }

    [TestMethod]
    public void APrint_IsNeverTakenForARefusal_EvenWhenItsTextMentionsEWI_OrIsNotJson()
    {
        var print = Answer.Of("{\"route\":3,\"action\":\"advance\",\"amount\":2.5,\"pending\":true,\"kind\":\"stop\",\"name\":\"EWI\",\"x\":1.0,\"y\":2.0,\"heading\":0.0,\"following\":false,\"stopsLeft\":1}");
        Assert.IsTrue(print.Ok, "the text \"EWI\" as a value is no sidecar"); Assert.IsNotNull(print.Order);
        Assert.AreEqual("advance", print.Order.Action); Assert.AreEqual("EWI", print.Order.Name);
        var nested = Answer.Of("{\"note\":{\"EWI\":\"inside\"}}");
        Assert.IsTrue(nested.Ok, "EWI below the root is no sidecar"); Assert.IsNull(nested.Order);
        Assert.IsTrue(Answer.Of("").Ok); Assert.IsNull(Answer.Of("").Order);
        Assert.IsTrue(Answer.Of(null!).Ok); Assert.AreEqual("", Answer.Of(null!).Print);
        var plain = Answer.Of("not json at all");
        Assert.IsTrue(plain.Ok, "what is not JSON is the engine's print, never a refusal"); Assert.IsNull(plain.Order);
    }
}
