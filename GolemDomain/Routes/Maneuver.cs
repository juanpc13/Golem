using GolemDomain.Geometry;

namespace GolemDomain.Routes;

/// <summary>
/// An evasion maneuver: a trajectory of a special kind — not the road to a stop but the moves a body makes
/// after touching something, to get clear and try again. Its legs are named for what they do (back, aside,
/// ahead) and never enter a mission's road. Every touch along it is another mark, so a maneuver is also how
/// the shape of an obstacle gets drawn.
/// </summary>
internal sealed class Maneuver : Trajectory
{
    /// <summary>The strategy that produced it: back-off, step-right, step-left.</summary>
    internal string Strategy { get; }

    internal Maneuver(string strategy, IEnumerable<Leg> legs) : base(legs)
    {
        if (string.IsNullOrWhiteSpace(strategy)) throw new GolemDomainException("a maneuver names the strategy that produced it");
        Strategy = strategy;
    }
}

/// <summary>A side of the body's lane: a closed set, compared by identity.</summary>
internal sealed class Side
{
    internal static readonly Side Right = new("right", -Math.PI / 2);
    internal static readonly Side Left  = new("left",   Math.PI / 2);

    internal string Name { get; }
    /// <summary>The turn from the heading toward this side, radians.</summary>
    internal double Turn { get; }

    private Side(string name, double turn) { Name = name; Turn = turn; }

    internal static Side Named(string name)
    {
        if (name == Right.Name) return Right;
        if (name == Left.Name) return Left;
        throw new GolemDomainException($"a side is 'right' or 'left', not '{name}'");
    }
}

/// <summary>
/// How a body gets clear of something it touched. Each strategy plans a maneuver from where the body stands
/// and the heading it had when it touched; the host executes it leg by leg and reports what it met. More
/// strategies will come (back off at an angle, a greedy or random road).
/// <para>Origins: a point robot with only a tactile sensor that reaches its target by alternating "head for the
/// goal" with "follow the obstacle's boundary" is the Bug family — Lumelsky &amp; Stepanov, Algorithmica 1987
/// (Bug1, Bug2); eleven variants compared by Ng &amp; Bräunl, J. Intell. Robot. Syst. 2007 (DistBug, TangentBug…).
/// Our step-aside-and-run-ahead is a bounded boundary-following of that family, with two differences the puppet
/// imposes: every touch is a journaled fact (Bump → Mark) that the whole fleet learns, and after a bounded probe
/// the golem re-decides the road over the map of marks (Route again) instead of following the boundary forever.</para>
/// </summary>
internal abstract class EvasionStrategy
{
    internal const string BackOffName   = "back-off";
    internal const string StepRightName = "step-right";
    internal const string StepLeftName  = "step-left";

    internal abstract string Name { get; }

    /// <summary>The maneuver from where the body stands, heading as it was when it touched something.</summary>
    internal abstract Maneuver From(Position here, double heading);

    internal static EvasionStrategy Named(string name)
    {
        switch (name)
        {
            case BackOffName:   return new BackOff();
            case StepRightName: return new StepAside(Side.Right);
            case StepLeftName:  return new StepAside(Side.Left);
        }
        throw new GolemDomainException($"no evasion strategy named '{name}': back-off, step-right or step-left");
    }

    internal static string[] Names() => new[] { BackOffName, StepRightName, StepLeftName };
}

/// <summary>Back straight off along the lane: one leg, 'back', a body's length and a half behind.</summary>
internal sealed class BackOff : EvasionStrategy
{
    internal const double Distance = 0.9;   // how far back: what the host backs a yielding body along its lane

    internal override string Name => BackOffName;

    internal override Maneuver From(Position here, double heading)
    {
        if (here == null) throw new GolemDomainException("a maneuver starts where the body stands");
        return new Maneuver(Name, new[] { new Leg(here.Along(heading + Math.PI, Distance), "back") });
    }
}

/// <summary>Step aside into the next lane — right first, by the golem's habit, then left — and run ahead in it
/// past whatever was touched: two legs, 'aside' and 'ahead'. If the run touches again, the thing is wider than a
/// step: mark it and step further out on the same side.</summary>
internal sealed class StepAside : EvasionStrategy
{
    internal const double Step = 0.5;   // the lateral step: two radii of the body
    internal const double Run  = 1.2;   // the run ahead in the new lane, past a crate's width

    internal Side Side { get; }

    internal StepAside(Side side)
    {
        if (side == null) throw new GolemDomainException("stepping aside needs a side");
        Side = side;
    }

    internal override string Name => Side == Side.Right ? StepRightName : StepLeftName;

    internal override Maneuver From(Position here, double heading)
    {
        if (here == null) throw new GolemDomainException("a maneuver starts where the body stands");
        var aside = here.Along(heading + Side.Turn, Step);
        var ahead = aside.Along(heading, Run);
        return new Maneuver(Name, new[] { new Leg(aside, "aside"), new Leg(ahead, "ahead") });
    }
}
