using System.Net.Http;
using System.Text.Json;
using WardenCli;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace WardenCliTests;

// THE CONSOLE USES THE GOLEM'S ENDPOINTS (16-oct-2026; Juan: "el CLI debería usar esos endpoints también"): every line the console writes by
// itself — a read, a lever of the environment, the map, the play, a tab's button — has its GET or POST; a tab's script stays a line of the language
[TestClass]
public class GolemClientTests
{
    [TestMethod]
    public void TheReads_GoByGet_AndTheLevers_ByTheirPosts_WithTheBodyTheEndpointExpects()
    {
        foreach (var read in new[] { "where", "state", "route", "scenarios", "obstacles", "formations", "disputes", "play", "plays" })
        {
            var call = GolemClient.Endpoint(read);
            Assert.IsNotNull(call, read);
            Assert.AreEqual(HttpMethod.Get, call!.Method, read);
            Assert.AreEqual(read, call.Path);
            Assert.IsNull(call.Body);
        }
        Assert.AreEqual("GET /where", GolemClient.Endpoint("where")!.Via);

        AssertPost("use map maze", "enter", "{\"scenario\":\"maze\"}");
        AssertPost("optimize on-the-way", "optimize", "{\"navigation\":\"on-the-way\"}");
        AssertPost("speed 1.5 --turn 1.5", "speed", "{\"speed\":1.5,\"turn\":1.5}");
        AssertPost("speed 3", "speed", "{\"speed\":3}");
        AssertPost("anchor", "anchor", null);
        AssertPost("reset", "reset", null);
        AssertPost("reset --all", "reset-everything", "{\"cascade\":true}");
        AssertPost("head north", "head", "{\"direction\":\"north\"}");
        AssertPost("steer manual", "steer", "{\"steer\":\"manual\"}");
        AssertPost("start game-1", "start", "{\"name\":\"game-1\"}");
        AssertPost("play over", "over", null);
        AssertPost("forget disputes", "forget-disputes", null);
        AssertPost("forget play game-1", "forget-play", "{\"name\":\"game-1\"}");
        AssertPost("forget 6.38,5.34", "forget", "{\"x\":6.38,\"y\":5.34}");
        AssertPost("place 14.25,8.25", "place", "{\"x\":14.25,\"y\":8.25}");
        AssertPost("dissolve tri-left", "dissolve", "{\"name\":\"tri-left\"}");

        // the language's lines are the tabs': POST /command
        Assert.IsNull(GolemClient.Endpoint("visit 2,9.5 5.5,9.5"));
        Assert.IsNull(GolemClient.Endpoint("form sq square --center 5.5,5.5 --side 2 --fleet blue,red"));
        Assert.IsNull(GolemClient.Endpoint("take sq"));
        Assert.IsNull(GolemClient.Endpoint("rotate sq clockwise"));
        Assert.IsNull(GolemClient.Endpoint("play pacman --pacman blue --ghosts red,green"), "staging a play is the language's: it spreads by tell");
        Assert.IsNull(GolemClient.Endpoint("speed fast"), "a pace that is no number: the line says what is wrong");
        Assert.IsNull(GolemClient.Endpoint("place here"));
        Assert.IsNull(GolemClient.Endpoint(""));
    }

    private static void AssertPost(string line, string path, string? body)
    {
        var call = GolemClient.Endpoint(line);
        Assert.IsNotNull(call, line);
        Assert.AreEqual(HttpMethod.Post, call!.Method, line);
        Assert.AreEqual(path, call.Path, line);
        if (body == null) Assert.IsNull(call.Body, line);
        else Assert.AreEqual(body, JsonSerializer.Serialize(call.Body), line);
    }
}
