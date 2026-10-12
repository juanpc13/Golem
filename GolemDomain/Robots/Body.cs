using GolemDomain.Units;

namespace GolemDomain.Robots;

/// <summary>
/// The robot's body as the golem knows it — a module of its own, released into the journal as one object built
/// from magnitudes that say what they are (Juan, 11-sep-2026): <c>radius = Meters(0.25); speed = MetersPerSecond(2.0);
/// linger = Seconds(6.0); retreat = Meters(0.6); body = Body(radius, speed, linger, retreat);</c> — the radius of the
/// disk it occupies, its cruise speed, how long it lingers at a stop a peer told it about, and how far it backs off
/// after a touch. The golem is the mind (what is journaled: entrusting, roads,
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
    internal Body(Length radius, Speed speed, Duration linger, Length retreat)
    {
        if (linger == null) throw new GolemDomainException("a body needs to know how long it lingers at a told stop, even not at all");
        if (retreat == null) throw new GolemDomainException("a body needs to know how far it backs off after a touch, even not at all");
        if (radius == null || radius.IsZero) throw new GolemDomainException("a body needs a radius above zero");
        if (speed == null || speed.IsZero) throw new GolemDomainException("a body needs a cruise speed above zero");
        Radius = radius;
        Speed = speed;
        TurnRate = new RadiansPerSecond(1.5);   // the body node's own cap until told otherwise (sim/bridge/body.py, 1.5 rad/s)
        LingerAfterTold = linger;
        Retreat = retreat;
    }

    /// <summary>THE PACE TOLD AGAIN (propuesta 111, 12-oct-2026; Juan: "¿podríamos aumentar la velocidad del golem para giros y avanzar
    /// mucho más rápido?… en el panel del environment para subirle la velocidad"): the cruise the body runs at from here on — a journaled
    /// act, <c>body.CruiseAt(pace)</c>; every order carries it (<c>speed</c>), and the road ahead is timed by it (<c>RouteSeconds</c>).
    /// The body's own motors cap what it can really do: the domain tells, the body obeys as far as it can.</summary>
    internal void CruiseAt(Speed speed)
    {
        if (speed == null) throw new GolemDomainException("Body.CruiseAt: 'speed' was not given");
        if (speed.IsZero) throw new GolemDomainException("a body needs a cruise speed above zero");
        Speed = speed;
    }

    /// <summary>How fast it turns in place from here on — <c>body.TurnAt(rate)</c>; every turn order carries it (<c>rate</c>).</summary>
    internal void TurnAt(AngularSpeed rate)
    {
        if (rate == null) throw new GolemDomainException("Body.TurnAt: 'rate' was not given");
        if (rate.IsZero) throw new GolemDomainException("a body needs a turn rate above zero");
        TurnRate = rate;
    }

    /// <summary>How far it backs off after touching something, once the touch is released — so it stands clear of the
    /// mark it just made before deciding its way again (Juan, 14-sep-2026: "retroceder un poco más"). Two radii and a
    /// hand is the usual: outside the clearance the planner keeps around a mark.</summary>
    internal Length Retreat { get; }

    /// <summary>The disk it occupies.</summary>
    internal Length Radius { get; }
    /// <summary>Its cruise speed — as declared at birth, or as last told (<see cref="CruiseAt"/>).</summary>
    internal Speed Speed { get; private set; }
    /// <summary>How fast it turns in place — the body node's own cap at birth, or as last told (<see cref="TurnAt"/>).</summary>
    internal AngularSpeed TurnRate { get; private set; }
    /// <summary>How long it lingers at every stop a peer told it about (the follower's pacing).</summary>
    internal Duration LingerAfterTold { get; }
}
