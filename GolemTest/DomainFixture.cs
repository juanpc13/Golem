using GolemDomain;
using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Robots;
using GolemDomain.Routes;
using GolemDomain.Touches;
using GolemDomain.Units;
using Microsoft.VisualStudio.TestTools.UnitTesting;

namespace GolemTest;

// The domain, built as OBJECTS for every test (Juan, 21-sep-2026: "el testing es sobre el dominio de las clases, no sobre
// los scripts"): the body the fleet declares (body_v1), the warehouse the world builds, an empty collisions module over it,
// and the golem that receives the three — exactly what the releases build in a journal, without a journal. Plus the moves
// a test makes the body make: the turn or the advance the route asks, reported as the robot would report it.
//
//   kitchen (0,8 4x3) --door(4,9.5)-- north (4,8 3x3) --door(7,9.5)-- storage (7,8 4x3)
//     | door (0.75,8)                   ~ open ~                          | door (10.25,8)
//   west (0,3 1.5x5)    [solid]      center (4,3 3x5)     [solid]       east (9.5,3 1.5x5)
//     | door (0.75,3)                   ~ open ~                          | door (10.25,3)
//   living (0,0 4x3) --door(4,1.5)-- south (4,0 3x3) --door(7,1.5)-- garage (7,0 4x3)
internal static class DomainFixture
{
    internal const double East = 0.0, North = 1.5708, West = 3.1416, South = -1.5708;   // headings
    internal const double Radius = 0.25, Retreat = 0.6;

    internal static Body Body() => new(new Meters(Radius), new MetersPerSecond(2.0), new Seconds(6.0), new Meters(Retreat));

    /// <summary>A golem born over the warehouse, with its body and an empty collisions module — the three globals of a journal.</summary>
    internal static (Golem g, MapLayout map, Collisions collisions) Born()
    {
        var map = Catalog.Warehouse();
        var collisions = new Collisions(map);
        return (new Golem(Body(), map, collisions), map, collisions);
    }

    internal static Position P(double x, double y) => new(x, y);
    internal static Pose At(double x, double y, double heading) => new(x, y, heading);
    internal static Position Centre(MapLayout map, string area) => map.Find(area).Center;

    /// <summary>One thing the route asks, done: a turn made (the body stands where it stood, facing the target's heading) or a
    /// move made (the body stands at the target, facing that heading) — the act the robot's report writes.</summary>
    internal static void Step(Route route)
    {
        var target = route.Target;
        string order = route.Order;
        if (order == "turnLeft" || order == "turnRight") route.Turn(new Pose(route.Standing.X, route.Standing.Y, target.Heading));
        else if (order == "advance" || order == "back") route.Reach(new Pose(target.X, target.Y, target.Heading));
        else Assert.Fail($"route {route.Id} asks '{order}': nothing for the body to do");
    }

    /// <summary>The body walks the way until the leg at (x, y) — a stop, a door, a point — is behind it, one act per thing the
    /// route asks (a turn, an advance, a retreat).</summary>
    internal static void Reach(Route route, double x, double y)
    {
        Assert.IsTrue(route.IsRouted, $"route {route.Id} walks only once its way is decided");
        for (int step = 0; step < 80; step++)
        {
            if (!route.IsPending()) Assert.Fail($"route {route.Id} ended before reaching ({x}, {y})");
            bool atIt = Math.Abs(route.NextLeg.At.X - x) < 1e-6 && Math.Abs(route.NextLeg.At.Y - y) < 1e-6;
            Step(route);
            if (!atIt) continue;
            if (!route.IsPending()) return;
            if (Math.Abs(route.NextLeg.At.X - x) > 1e-6 || Math.Abs(route.NextLeg.At.Y - y) > 1e-6) return;
        }
        Assert.Fail($"route {route.Id}: too many steps without reaching ({x}, {y})");
    }

    /// <summary>The body walks the whole way to the end.</summary>
    internal static void WalkToTheEnd(Route route)
    {
        for (int step = 0; step < 200 && route.IsPending(); step++) Step(route);
        Assert.IsFalse(route.IsPending(), $"route {route.Id}: too many steps without ending — {route.AsPlan()}");
    }

    /// <summary>Where the body stood when it touched a point heading that way: one radius behind the touch, facing it.</summary>
    internal static Pose BehindTheTouch(double x, double y, double heading) =>
        new(x - Radius * Math.Cos(heading), y - Radius * Math.Sin(heading), heading);

    /// <summary>The body touched something uncharted at (x, y) heading that way: the route's own bump.</summary>
    internal static void Bump(Route route, double x, double y, double heading) =>
        route.Bump(new Pose(x, y, heading), BehindTheTouch(x, y, heading));

    /// <summary>The body touched SOMETHING at (x, y): the route concludes what it was.</summary>
    internal static void Touched(Route route, double x, double y, double heading) =>
        route.Touched(new Pose(x, y, heading), BehindTheTouch(x, y, heading));

    /// <summary>The body grazed a wall it knows at (x, y), standing a radius off it toward the east (the corridor's outer wall).</summary>
    internal static void Graze(Route route, double x, double y) =>
        route.Graze(new Position(x, y), new Pose(x + Radius, y, West));

    /// <summary>A mark a peer's bump left, or a lab planted: a touch at (x, y) heading that way.</summary>
    internal static void PlantMark(Collisions collisions, double x, double y, double heading) =>
        collisions.Mark(new Pose(x, y, heading));

    /// <summary>The domain refuses, in its own words.</summary>
    internal static void Refuses(Action act, string words)
    {
        var refusal = Assert.ThrowsException<GolemDomainException>(act);
        StringAssert.Contains(refusal.Message, words);
    }
}
