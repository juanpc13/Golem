using GolemAPI.Membrane;

namespace GolemAPI.Navigation;

// Drives a differential-drive body to a point with a plain proportional controller over
// rosbridge telemetry — in a world that is now real. Failure is defined here, and the first
// definition is the simulator's own: its contact sensor says the body TOUCHED something (a
// wall, a crate, another body). The run stops, the body backs off a little so it is free
// again, and the verdict names what it hit. A stall (the distance stops improving while we
// push) and a timeout still count as failure; the map knows nothing of any of it.
public sealed class DiffDriveNavigator : INavigator
{
    private static readonly TimeSpan RunTimeout = TimeSpan.FromSeconds(90);
    private static readonly TimeSpan StallAfter = TimeSpan.FromSeconds(3);
    private static readonly TimeSpan Tick = TimeSpan.FromMilliseconds(50);
    private const double Progress = 0.05;       // an improvement smaller than this is noise (m)
    private const double BackOffSpeed = 0.4;    // m/s, in reverse, after a collision
    private static readonly TimeSpan FreeFor = TimeSpan.FromMilliseconds(400);   // no touch reported this long = free
    private static readonly TimeSpan BackOffAtMost = TimeSpan.FromSeconds(4);

    private readonly Rosbridge ros;
    private readonly Func<double> cruiseSpeed;   // the golem's declared body speed (a journaled property)
    private readonly Func<double> bodyRadius;    // the golem's declared body size (a journaled property)

    public DiffDriveNavigator(Rosbridge ros, Func<double> cruiseSpeed, Func<double> bodyRadius)
    {
        this.ros = ros;
        this.cruiseSpeed = cruiseSpeed;
        this.bodyRadius = bodyRadius;
    }

    public async Task<Outcome> GoToAsync(double targetX, double targetY, double within, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        double cruise = cruiseSpeed();
        double bestDistance = double.MaxValue;
        var lastImprovement = DateTime.UtcNow;

        while (DateTime.UtcNow - start < RunTimeout && !ct.IsCancellationRequested)
        {
            var touch = ros.LatestContact;
            if (touch != null && touch.At > start)
            {
                // Where the touch happened, as the golem reckons it: one body radius ahead of the
                // nose. A bumper knows no more than "I touched something while heading this way";
                // the run is taken as head-on. The world's name for what was hit rides along as words.
                var atTouch = ros.LatestPose;
                double r = bodyRadius();
                double hitX = atTouch == null ? targetX : atTouch.X + r * Math.Cos(atTouch.Theta);
                double hitY = atTouch == null ? targetY : atTouch.Y + r * Math.Sin(atTouch.Theta);
                double touchHeading = atTouch == null ? Math.Atan2(targetY - (ros.LatestPose?.Y ?? targetY), targetX - (ros.LatestPose?.X ?? targetX)) : atTouch.Theta;
                await BackOffAsync(touch, ct);
                return Outcome.Collided(touch.With, hitX, hitY, touchHeading);
            }

            var pose = ros.LatestPose;
            if (pose == null) { await Task.Delay(Tick, ct); continue; }

            double dx = targetX - pose.X, dy = targetY - pose.Y;
            double distance = Math.Sqrt(dx * dx + dy * dy);
            if (distance < within)
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
                return Outcome.Failed("stalled: no progress for 3 s");
            }

            // Turn toward the target first (in place: doors are narrow and runs through them must
            // be straight); move once aligned, slower the further off — and slower the closer to
            // the ARRIVAL circle, not to the point, so a standoff is met at a crawl, not at speed.
            double heading = Math.Atan2(dy, dx);
            double deviation = NormalizeAngle(heading - pose.Theta);
            double angular = Math.Clamp(3.0 * deviation, -3.0, 3.0);
            double linear = Math.Abs(deviation) < 0.35
                ? Math.Min(cruise, 1.5 * (distance - within) + 0.15) * Math.Cos(deviation)
                : 0.0;

            await ros.DriveAsync(linear, angular, ct);
            await Task.Delay(Tick, ct);
        }
        if (!ct.IsCancellationRequested)
            await ros.DriveAsync(0, 0, ct);
        return Outcome.Failed("timeout");
    }

    // After a hit the body is pressed against what it hit; it reverses until the world stops
    // reporting the touch (plus a moment), so the next mission does not begin in contact.
    // Closed loop on purpose: the drive ramps velocity within its acceleration limit, so after
    // a hit at speed the first half-second of "reverse" is still spent braking into the obstacle
    // — a fixed reverse time freed nothing. It gives up after a while, or if it backs into
    // something else.
    private async Task BackOffAsync(Contact touch, CancellationToken ct)
    {
        var began = DateTime.UtcNow;
        try
        {
            await ros.DriveAsync(-BackOffSpeed, 0, ct);
            while (DateTime.UtcNow - began < BackOffAtMost)
            {
                await Task.Delay(Tick, ct);
                var latest = ros.LatestContact;
                if (latest.With != touch.With && latest.At > began) break;   // backed into something else
                if (DateTime.UtcNow - latest.At > FreeFor) break;
            }
        }
        finally
        {
            await ros.DriveAsync(0, 0, CancellationToken.None);
        }
    }

    private static double NormalizeAngle(double a)
    {
        while (a > Math.PI) a -= 2 * Math.PI;
        while (a < -Math.PI) a += 2 * Math.PI;
        return a;
    }
}
