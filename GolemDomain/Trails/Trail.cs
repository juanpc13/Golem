using GolemDomain.Geometry;

namespace GolemDomain.Trails;

/// <summary>THE TRAIL of one scenario (propuesta 120, 20-oct-2026): where the body went, stride by stride, in that scenario's frame.
/// Fed by the golem from what the acts bring it (`Golem.Stood`, ajuste 78) — nothing of it is journaled: it is rebuilt on replay
/// from the acts that already are, like the list of routes. A body CARRIED (woken after a teleport) is anchored, not walked: the
/// trail goes on from there and no stride is drawn across the jump.</summary>
internal sealed class Trail
{
    /// <summary>A move shorter than this (metres) is no stride: a turn in place, the noise of a report.</summary>
    internal const double Hair = 0.01;

    private readonly List<Stride> strides = new();
    private Pose last;

    internal string Scenario { get; }
    internal IReadOnlyList<Stride> Strides => strides;
    internal int Count => strides.Count;
    internal double Length => strides.Sum(s => s.Length);
    /// <summary>Where the trail ends now — the last pose the acts brought; null before any.</summary>
    internal Pose Last => last;

    internal Trail(string scenario)
    {
        if (string.IsNullOrWhiteSpace(scenario)) throw new GolemDomainException("Trail: 'scenario' was not given");
        Scenario = scenario;
    }

    /// <summary>The body stands here now, having WALKED from where the trail ended: a stride, unless it did not move.</summary>
    internal void Walked(Pose me)
    {
        if (me == null) throw new GolemDomainException("Trail.Walked: 'me' was not given");
        if (last != null && last.DistanceTo(me) > Hair) strides.Add(new Stride(last, me));
        last = me;
    }

    /// <summary>The body stands here now WITHOUT having walked (carried, reborn): the trail goes on from here.</summary>
    internal void Anchored(Pose me)
    {
        if (me == null) throw new GolemDomainException("Trail.Anchored: 'me' was not given");
        last = me;
    }
}
