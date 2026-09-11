using GolemDomain.Units;

namespace GolemDomain.Robots;

/// <summary>
/// The robot's body as the golem knows it — a module of its own, released into the journal as one object built
/// from magnitudes that say what they are (Juan, 11-sep-2026): <c>radius = Meters(0.25); speed = MetersPerSecond(2.0);
/// linger = Seconds(6.0); body = Body(radius, speed, linger);</c> — the radius of the disk it occupies, its cruise
/// speed, and how long it lingers at a stop a peer told it about. The golem is the mind (what is journaled: entrusting, roads,
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
    /// <summary>A body: the radius of the disk it occupies, its cruise speed, and how long it lingers at a told stop.
    /// Each a magnitude of its own kind — a duration where a length goes is refused by type, not accepted as a number.</summary>
    internal Body(Length radius, Speed speed, Duration linger)
    {
        if (radius == null || radius.IsZero) throw new GolemDomainException("a body needs a radius above zero");
        if (speed == null || speed.IsZero) throw new GolemDomainException("a body needs a cruise speed above zero");
        Radius = radius;
        Speed = speed;
        LingerAfterTold = linger ?? throw new GolemDomainException("a body needs to know how long it lingers at a told stop, even not at all");
    }

    /// <summary>The disk it occupies.</summary>
    internal Length Radius { get; }
    /// <summary>Its cruise speed.</summary>
    internal Speed Speed { get; }
    /// <summary>How long it lingers at every stop a peer told it about (the follower's pacing).</summary>
    internal Duration LingerAfterTold { get; }
}
