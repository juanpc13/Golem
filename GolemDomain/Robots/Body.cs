namespace GolemDomain.Robots;

/// <summary>
/// The robot's body as the golem knows it — a module of its own, released into the journal as one object:
/// <c>body = Body(0.25, 2.0, 6.0)</c> — the radius of the disk it occupies, its cruise speed, and how long it
/// lingers at a stop a peer told it about. The golem is the mind (what is journaled: entrusting, roads,
/// touches); the body is what it drives, handed to it at birth. The robot's name is the journal's identity
/// (the host's GOLEM), and its estimated position is telemetry that enters queries as parameters: neither is
/// state of the domain.
/// <para>Origins: the radius is all the planner needs because the body is reduced to a point and the world grown
/// by that radius (configuration space, Lozano-Pérez 1983). The estimated position stays out of the domain on
/// purpose: dead reckoning drifts (Borenstein &amp; Feng's UMBmark, IEEE T-RA 1996, separates systematic from
/// non-systematic odometry error), and the 7-sep wheel-odometry experiment showed 0.48 m of error in one bump.</para>
/// </summary>
internal sealed class Body
{
    private readonly double radius;   // the disk it occupies
    private readonly double speed;    // cruise speed, world units per second
    private readonly double linger;   // seconds it lingers at every stop a peer told it about (the follower's pacing)

    /// <summary>A body: its radius (world units), its cruise speed (units per second) and how long it lingers at
    /// a told stop (seconds).</summary>
    internal Body(double radius, double speed, double lingerSeconds)
    {
        if (radius <= 0) throw new DomainException("a body needs a radius above zero");
        if (speed <= 0) throw new DomainException("a body needs a cruise speed above zero");
        if (lingerSeconds < 0) throw new DomainException("a linger cannot be negative");
        this.radius = radius;
        this.speed = speed;
        linger = lingerSeconds;
    }

    internal double Radius => radius;
    internal double Speed() => speed;
    internal double LingerAfterTold => linger;
}
