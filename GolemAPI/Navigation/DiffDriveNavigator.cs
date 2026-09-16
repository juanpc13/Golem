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
    private static readonly TimeSpan BackOffAtMost = TimeSpan.FromSeconds(6);

    private readonly Rosbridge ros;
    private readonly Func<double> cruiseSpeed;   // the golem's declared body speed (a journaled property)
    private readonly Func<double> bodyRadius;    // the golem's declared body size (a journaled property)
    private readonly Func<double> retreat;       // how far the body backs off after a touch (the body's own, journaled)

    public DiffDriveNavigator(Rosbridge ros, Func<double> cruiseSpeed, Func<double> bodyRadius, Func<double> retreat)
    {
        this.ros = ros;
        this.cruiseSpeed = cruiseSpeed;
        this.bodyRadius = bodyRadius;
        this.retreat = retreat;
    }

    private static readonly TimeSpan TurnTimeout = TimeSpan.FromSeconds(15);
    private const double FacingWithin = 0.05;   // rad: close enough to call the turn made (~3°)

    // Turn in place until the body faces the heading: the route asked this one thing (Juan, 16-sep-2026: "si tiene
    // que girar entonces gira, luego ros le dice ya giré"). A touch meanwhile ends it as a collision, like a run's.
    public async Task<Outcome> TurnToAsync(double heading, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        while (DateTime.UtcNow - start < TurnTimeout && !ct.IsCancellationRequested)
        {
            var pose = ros.LatestPose;
            var hit = await TouchedAsync(start, pose?.X ?? 0, pose?.Y ?? 0, ct);
            if (hit != null) return hit;
            if (pose == null) { await Task.Delay(Tick, ct); continue; }
            double deviation = NormalizeAngle(heading - pose.Theta);
            if (Math.Abs(deviation) < FacingWithin)
            {
                await ros.DriveAsync(0, 0, ct);
                return Outcome.Arrived;
            }
            double angular = Math.Clamp(3.0 * deviation, -1.5, 1.5);
            if (Math.Abs(angular) < 0.3) angular = Math.Sign(angular) * 0.3;   // enough to move against friction
            await ros.DriveAsync(0, angular, ct);
            await Task.Delay(Tick, ct);
        }
        if (!ct.IsCancellationRequested)
            await ros.DriveAsync(0, 0, ct);
        return Outcome.Failed("turn timeout");
    }

    // The simulator's own definition of failure: its contact sensor says the body TOUCHED something since the run
    // began. Where the touch happened, as the golem reckons it: one body radius from the centre of the body it
    // believes, in the direction the shell was pressed (the contact's bearing — a flank hit on a corner lands on the
    // flank, not on the nose). The heading points into what was touched. The world's name for it rides along as words.
    // The body backs off before the verdict, so it stands free again. Null when nothing was touched.
    private async Task<Outcome> TouchedAsync(DateTime since, double targetX, double targetY, CancellationToken ct)
    {
        var touch = ros.LatestContact;
        if (touch == null || touch.At <= since) return null;
        var atTouch = ros.LatestPose;
        double r = bodyRadius();
        double hitX, hitY, touchHeading;
        if (atTouch == null)
        {
            hitX = targetX; hitY = targetY;
            touchHeading = Math.Atan2(targetY - (ros.LatestPose?.Y ?? targetY), targetX - (ros.LatestPose?.X ?? targetX));
        }
        else (hitX, hitY, touchHeading) = touch.On(atTouch, r);
        Console.WriteLine($"[navigator] touched {touch.With} at bearing {touch.Bearing * 180 / Math.PI:0}° off the nose — the touch lands at ({hitX:0.00}, {hitY:0.00})");
        await BackOffAsync(touch, ct);
        return Outcome.Collided(touch.With, hitX, hitY, touchHeading);
    }

    public async Task<Outcome> GoToAsync(double targetX, double targetY, double within, CancellationToken ct)
    {
        var start = DateTime.UtcNow;
        double cruise = cruiseSpeed();
        double bestDistance = double.MaxValue;
        var lastImprovement = DateTime.UtcNow;

        while (DateTime.UtcNow - start < RunTimeout && !ct.IsCancellationRequested)
        {
            var hit = await TouchedAsync(start, targetX, targetY, ct);
            if (hit != null) return hit;

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
    // Backing off: until the touch is released, and then the body's own retreat further, so it stands clear of the
    // mark it just made before the golem decides its way again. It stops early if it backs into something else.
    private async Task BackOffAsync(Contact touch, CancellationToken ct)
    {
        var began = DateTime.UtcNow;
        var start = ros.LatestPose;
        double back = retreat();
        try
        {
            await ros.DriveAsync(-BackOffSpeed, 0, ct);
            bool free = false;
            while (DateTime.UtcNow - began < BackOffAtMost)
            {
                await Task.Delay(Tick, ct);
                var latest = ros.LatestContact;
                if (latest.With != touch.With && latest.At > began) break;   // backed into something else
                if (!free && DateTime.UtcNow - latest.At > FreeFor) free = true;
                var pose = ros.LatestPose;
                if (free && (start == null || pose == null || Math.Sqrt((pose.X - start.X) * (pose.X - start.X) + (pose.Y - start.Y) * (pose.Y - start.Y)) >= back)) break;
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
