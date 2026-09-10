using System.Globalization;
using GolemDomain.Geometry;

namespace GolemDomain.Routes;

/// <summary>
/// A trajectory — Juan's RUTA / TRAYECTORIA: the ordered legs a body walks, one straight segment from each
/// leg to the next. A mission's road is a trajectory; an evasion <see cref="Maneuver"/> is a trajectory of a
/// special kind. It reads in one line — "kitchen/north@4,9.5 > north~center@5.5,8 > garage@9,1.5" — but the
/// journal writes it act by act (Route, Via, Around, Aside, Stop), never as text.
/// </summary>
internal class Trajectory
{
    private readonly List<Leg> legs;

    internal Trajectory(IEnumerable<Leg> legs)
    {
        if (legs == null) throw new DomainException("a trajectory needs its legs, even none");
        this.legs = legs.ToList();
    }

    internal IReadOnlyList<Leg> Legs() => legs;
    internal int Count => legs.Count;
    internal bool IsEmpty => legs.Count == 0;
    internal int StopCount => legs.Count(l => l.IsStop);

    internal Leg LegAt(int index)
    {
        if (index < 0 || index >= legs.Count) throw new DomainException($"the trajectory has {legs.Count} legs, not a leg {index}");
        return legs[index];
    }

    internal Leg Last
    {
        get
        {
            if (legs.Count == 0) throw new DomainException("an empty trajectory has no last leg");
            return legs[^1];
        }
    }

    /// <summary>The stops from a leg onward: the points of the stop legs, in order.</summary>
    internal IEnumerable<Position> StopsFrom(int index) => legs.Skip(index).Where(l => l.IsStop).Select(l => l.At);

    /// <summary>The segments a body walks, from where it starts: one straight run to each leg's point in turn.</summary>
    internal IReadOnlyList<Segment> Segments(Position from)
    {
        if (from == null) throw new DomainException("a trajectory is walked from somewhere");
        var segments = new List<Segment>();
        Position here = from;
        foreach (var leg in legs)
        {
            segments.Add(new Segment(here, leg.At));
            here = leg.At;
        }
        return segments;
    }

    /// <summary>How long the walk is, from where it starts, leg to leg.</summary>
    internal double Length(Position from) => Segments(from).Sum(s => s.Length);

    /// <summary>The trajectory as the journal writes it: "name@x,y > name@x,y".</summary>
    internal string AsPlan() => string.Join(" > ", legs.Select(l => $"{l.Name}@{Fmt(l.At.X)},{Fmt(l.At.Y)}"));

    private static string Fmt(double d) => d.ToString("0.##", CultureInfo.InvariantCulture);
}
