using GolemHost.Membrane;

namespace GolemHost.Navigation;

// Drives a turtlesim sprite to a point with a plain proportional controller over
// rosbridge telemetry — and defines failure for a world that never fails on its own:
// a stall (the distance stops improving for StallAfter while we push: the body is
// against a wall) or a timeout. Collision avoidance between bodies belongs here too,
// when it comes.
public sealed class TurtlesimNavigator : INavigator
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan StallAfter = TimeSpan.FromSeconds(3);
    private const double ArriveWithin = 0.25;   // world units
    private const double Progress = 0.05;       // an improvement smaller than this is noise

    private readonly Rosbridge ros;

    public TurtlesimNavigator(Rosbridge ros)
    {
        this.ros = ros;
    }

    public async Task<Outcome> GoToAsync(double targetX, double targetY, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        double bestDistance = double.MaxValue;
        var lastImprovement = DateTime.UtcNow;

        while (DateTime.UtcNow - start < RunTimeout && !ct.IsCancellationRequested)
        {
            var pose = ros.LatestPose;
            if (pose == null) { await Task.Delay(100, ct); continue; }

            double dx = targetX - pose.X, dy = targetY - pose.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < ArriveWithin)
            {
                await ros.DriveAsync(0, 0, ct);
                return Outcome.Arrived;
            }

            if (distance < bestDistance - Progress)
            {
                bestDistance = distance;
                lastImprovement = DateTime.UtcNow;
            }
            else if (DateTime.UtcNow - lastImprovement > StallAfter)
            {
                await ros.DriveAsync(0, 0, ct);
                return Outcome.Failed("stuck against a wall");
            }

            double heading = Math.Atan2(dy, dx);
            double deviation = NormalizeAngle(heading - pose.Theta);
            double angular = Math.Clamp(4.0 * deviation, -4.0, 4.0);
            double linear = Math.Abs(deviation) < 0.4 ? Math.Min(2.0, 1.5 * distance) : 0.0;

            await ros.DriveAsync(linear, angular, ct);
            await Task.Delay(100, ct);
        }
        if (!ct.IsCancellationRequested)
            await ros.DriveAsync(0, 0, ct);
        return Outcome.Failed("timeout");
    }

    private static double NormalizeAngle(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }
}
