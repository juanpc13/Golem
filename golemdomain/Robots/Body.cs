namespace GolemHost.Domain.Robots;

/// <summary>
/// The robot's body as the golem knows it: the properties released into the journal — the radius of the disk
/// it occupies, its cruise speed, and how long it lingers at a stop a peer told it about. The golem is the
/// mind (what is journaled: entrusting, roads, touches); the body is what it drives. The robot's name is
/// the journal's identity (the host's GOLEM), and its estimated position is telemetry that enters queries as
/// parameters: neither is state of the domain.
/// <para>Origins: the radius is all the planner needs because the body is reduced to a point and the world grown
/// by that radius (configuration space, Lozano-Pérez 1983). The estimated position stays out of the domain on
/// purpose: dead reckoning drifts (Borenstein &amp; Feng's UMBmark, IEEE T-RA 1996, separates systematic from
/// non-systematic odometry error), and the 7-sep wheel-odometry experiment showed 0.48 m of error in one bump.</para>
/// </summary>
internal sealed class Body
{
    private double radius;   // the disk it occupies — zero until embodied: a body that has no size yet is a point
    private double speed;    // cruise speed, world units per second
    private double linger;   // seconds it lingers at every stop a peer told it about (the follower's pacing)

    /// <summary>Gives the body its size: the radius of the disk it occupies, in world units. Returns it.</summary>
    internal double Embody(double bodyRadius)
    {
        if (bodyRadius <= 0) throw new DomainException("a body needs a radius above zero");
        radius = bodyRadius;
        return radius;
    }

    /// <summary>Sets the cruise speed, in world units per second. Returns it.</summary>
    internal double Cruise(double unitsPerSecond)
    {
        if (unitsPerSecond <= 0) throw new DomainException("a body needs a cruise speed above zero");
        speed = unitsPerSecond;
        return speed;
    }

    /// <summary>Sets how long the body lingers at every stop a peer told it about. Returns it.</summary>
    internal double Linger(double seconds)
    {
        if (seconds < 0) throw new DomainException("a linger cannot be negative");
        linger = seconds;
        return linger;
    }

    /// <summary>The radius; zero until the init release runs.</summary>
    internal double Radius => radius;

    internal double Speed()
    {
        if (speed <= 0) throw new DomainException("the golem has no cruise speed yet: the init release must run first");
        return speed;
    }

    internal double LingerAfterTold => linger;
}
