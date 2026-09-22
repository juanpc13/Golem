using Puppeteer;

namespace GolemAPI.Choreography.Roles;

// THE COLLISION CAPTOR — the role of the BUMPER: the switch on the chassis that says the body touched something (Juan,
// 17-sep-2026: "CollisionCaptor"; 18-sep: "el botón del chasis que nos dice si tocamos algo"). A ROLE is the encapsulation
// unit of the acts ONE capability of the body writes in the golem's journal; this one owns the touch — the bump the body
// reports, and the operator's word that what was learned by touching is gone. It RECEIVES the robot (the golem with its body:
// the actor, the golem's NAME that rides beside a touch so the peers know who bumped) and builds nothing. The embodiment plays
// it only when the body declared it (Capabilities.CollisionCaptor, `ROLES` in the compose); a body without a bumper has no
// `/robot/bump` to answer, and the controller says so.
//
// WHAT THE CAPTOR DOES NOT DO: decide. What a touch WAS — a wall the map knows (a graze), a thing (marked, the way corrected
// around it) — is the ROUTE's conclusion inside `g.Bump(me, bearing)`; FOR NOW EVERY TOUCH IS A BUMP (Juan, 18-sep-2026), the
// meeting of two bodies comes back later. A touch while the body stands is refused by the domain, not filtered here.
public sealed class CollisionCaptor
{
    private readonly GolemEmbodiment robot;   // the golem with its body: every act is written to its actor

    public CollisionCaptor(GolemEmbodiment robot)
    {
        if (robot == null) throw new ArgumentNullException(nameof(robot), "a collision captor needs the robot that does the touching");
        this.robot = robot;
    }

    /// <summary>The body bumped and its motors stopped at once — or it stood and something touched it. It says what a bumper can
    /// say: where it stood, facing which way, and where on its shell it was pressed (the bearing). ONE script: `g.Bump(me,
    /// @bearing)` — the golem reckons the touch on the plane from the body it declared and concludes inside: a route underway
    /// takes it (a wall grazed, a thing marked and skirted by the right) or, standing, a body touched it (no mark; ajuste 48). The
    /// print is what the route underway asks now, if any — written in full after the act (Juan, 22-sep-2026). The peers are told by the reaction that captures the pose from the
    /// `Pose(@…)` beside the act and the bearing from the act itself; only the golem's NAME is exposed (ajuste 42).</summary>
    public Answer Bumped(double bodyX, double bodyY, double bodyHeading, double bearing)
    {
        Answer answer;
        try
        {
            answer = Answer.Of(robot.Actor.Using(
                @"
                    {
                        me = Pose(@bodyX, @bodyY, @bodyHeading);
                        g.Bump(me, @bearing);
                    }
                    expose @name who;
                    {
                        print g.HasPendingMission() 'pending', g.Held 'held';
                        if (g.HasPendingMission()) {
                            route = g.Underway();
                            print route.Id 'route', route.Order 'action', route.Amount 'amount';
                            if (route.IsWalkable) {
                                print route.NextLeg.Kind 'kind', route.NextLeg.Name 'name',
                                      route.Target.X 'x', route.Target.Y 'y', route.Target.Heading 'heading',
                                      route.Following 'following', route.StopsLeft 'stopsLeft';
                            }
                        }
                    }
                ")
                .WithParameters(p => {
                    p["bodyX", typeof(double)] = bodyX;
                    p["bodyY", typeof(double)] = bodyY;
                    p["bodyHeading", typeof(double)] = bodyHeading;
                    p["bearing", typeof(double)] = bearing;
                    p["name", typeof(string)] = robot.Name;
                })
                .PerformCommand());
        }
        catch (Exception ex) { return Answer.Refusal(GolemEmbodiment.Reason(ex)); }
        robot.Report(answer, "the touch is written; the golem concluded inside — a route corrected its way, or a standing body was touched");
        return answer;
    }

    /// <summary>Somebody took it away: the golem forgets the obstacle standing there, with every mark that outlined it. The
    /// reaction tells the peers — it captures the point from the `Position(@x, @y)` built beside the act, no expose — who
    /// forget it too. No order changes: the way stands.</summary>
    public Answer Forget(double x, double y) => Answer.Of(robot.Actor.Using(
        @"
            Check(collisions.KnowsAt(Position(@x, @y))) Error 'the golem holds no obstacle there';
        ",
        @"
            {
                at = Position(@x, @y);
                g.Forget(at);
            }
        ")
        .WithParameters(p => {
            p["x", typeof(double)] = x;
            p["y", typeof(double)] = y;
        })
        .PerformCheckThenCommand());
}
