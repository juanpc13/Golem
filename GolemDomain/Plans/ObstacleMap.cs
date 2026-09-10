using GolemDomain.Geometry;

namespace GolemDomain.Plans;

/// <summary>
/// The obstacles module — what the bodies LEARNED by touching, as opposed to the <see cref="FloorPlan"/>, which
/// is what the golem was told. It keeps the facts (the marks where a touch met something the plan does not hold,
/// and the peers it met) and derives the hypotheses from them: the things the marks outline. It never stores a
/// hypothesis; every reading recomputes it (paper 08: the facts are testimony, the figure is inference).
/// <para>Two modules, not one: the plan does not change, the learning does, and the road is found by consulting
/// both — the plan says where the walls and the doors are, this one says what nobody charted. It leans on the
/// plan only to name the zone a mark stands in.</para>
/// </summary>
internal sealed class ObstacleMap
{
    /// <summary>A mark is a point where a body touched something: whatever stands there is taken to reach at
    /// least this far around the point, along the surface it touched.</summary>
    internal const double MarkReach = 0.25;
    /// <summary>The margin a body keeps from what it knows is there, beyond its own radius.</summary>
    internal const double MarkMargin = 0.1;
    /// <summary>Two marks this close were made on the same thing: joined, they are vertices of one obstacle.</summary>
    internal const double JoinWithin = 1.0;
    /// <summary>Two touches this close are the same mark.</summary>
    internal const double SameTouch = 0.1;

    private readonly FloorPlan plan;
    private readonly List<Mark> marks = new();        // facts: where bodies touched what the plan does not hold
    private readonly List<Peer> encounters = new();   // facts: where a body met another body — history, never planned around

    internal ObstacleMap(FloorPlan plan)
    {
        if (plan == null) throw new DomainException("the obstacles are learned over a floor plan");
        this.plan = plan;
    }

    // ---- the facts ----

    /// <summary>A point where a body touched something the plan does not hold, with the heading of the touch (the
    /// mark's normal). Two touches within SameTouch are one mark. Returns how many marks it holds.</summary>
    internal int Mark(Pose touch)
    {
        if (!marks.Any(m => m.DistanceTo(touch) < SameTouch)) marks.Add(new Mark(touch.X, touch.Y, touch.Heading));
        return marks.Count;
    }

    /// <summary>A body met another body at a point: kept as a peer among the obstacles, never planned around.
    /// Returns how many encounters it holds.</summary>
    internal int Meet(string who, Position at)
    {
        encounters.Add(new Peer(who, at, plan.ZoneOf(at)));
        return encounters.Count;
    }

    internal int MarkCount => marks.Count;
    internal int EncounterCount => encounters.Count;
    internal IReadOnlyList<Mark> Marks => marks;

    /// <summary>The marks standing in a place (a mark on a shared wall stands in both).</summary>
    internal IReadOnlyList<Mark> MarksIn(Place place) => marks.Where(place.Contains).ToList();

    // ---- the hypotheses, derived every time ----

    /// <summary>Every obstacle the golem hypothesizes: the things the marks outline — marks within JoinWithin of
    /// one another (directly or through others) are vertices of one thing, ordered around its centre so they can
    /// be joined into a figure — followed by the peers it met.</summary>
    internal IReadOnlyList<Obstacle> All()
    {
        int n = marks.Count;
        var parent = Enumerable.Range(0, n).ToArray();
        int Root(int i) { while (parent[i] != i) i = parent[i] = parent[parent[i]]; return i; }
        for (int i = 0; i < n; i++)
            for (int j = i + 1; j < n; j++)
                if (marks[i].DistanceTo(marks[j]) <= JoinWithin) parent[Root(i)] = Root(j);

        var obstacles = new List<Obstacle>();
        foreach (var group in Enumerable.Range(0, n).GroupBy(Root).OrderBy(g => g.Min()))
        {
            var members = group.Select(i => marks[i]).ToList();
            var centre = new Position(members.Average(m => m.X), members.Average(m => m.Y));
            var ordered = members.OrderBy(m => Math.Atan2(m.Y - centre.Y, m.X - centre.X)).ToList();
            obstacles.Add(new Thing(ordered, centre, plan.ZoneOf(centre)));
        }
        obstacles.AddRange(encounters);
        return obstacles;
    }

    /// <summary>The things alone: the obstacles the roads avoid. A peer is not one of them — bodies move on.</summary>
    internal IReadOnlyList<Thing> Things() => All().OfType<Thing>().ToList();

    /// <summary>The obstacles whose centre stands in a place.</summary>
    internal IReadOnlyList<Obstacle> In(Place place) => All().Where(o => place.Contains(o.Center)).ToList();

    // ---- forgetting: something that was there is not there any more ----

    /// <summary>Whether an obstacle stands at a point — its centre or any of its vertices within JoinWithin of
    /// it. What the operator consults before saying it is gone.</summary>
    internal bool KnowsAt(Position at) => Nearest(at) != null;

    /// <summary>Someone took it away: the golem forgets the obstacle standing there and, with it, EVERY fact that
    /// outlined it — all its marks at once, because they were vertices of one thing and the thing is gone. A body
    /// may pass there again, and if it touches something it will be a NEW obstacle, outlined by new marks.
    /// Returns how many facts were dropped; zero when nothing stands there.
    /// <para>The map forgets, the journal does not: the act of forgetting is journaled like any other, so the
    /// history still says what was believed and when it stopped being believed.</para></summary>
    internal int Forget(Position at)
    {
        var target = Nearest(at);
        if (target == null) return 0;
        if (target is Thing thing)
        {
            int had = marks.Count;
            foreach (var vertex in thing.Vertices()) marks.Remove(vertex);   // the very marks it holds: compared by identity
            return had - marks.Count;
        }
        int met = encounters.Count;
        encounters.RemoveAll(e => e.Center.DistanceTo(target.Center) < SameTouch);
        return met - encounters.Count;
    }

    // The obstacle a point names: the closest one whose centre or one of whose vertices lies within JoinWithin.
    private Obstacle Nearest(Position at)
    {
        Obstacle best = null;
        double closest = double.PositiveInfinity;
        foreach (var o in All())
        {
            double d = o.Center.DistanceTo(at);
            foreach (var v in o.Vertices()) d = Math.Min(d, v.DistanceTo(at));
            if (d > JoinWithin || d >= closest) continue;
            closest = d;
            best = o;
        }
        return best;
    }

    // ---- what it says to whoever looks for a road ----

    /// <summary>Whether what has been learned stands in the way of a body of this radius at a point: any mark
    /// blocks it, unless the point lies on the side the touching body came from (the mark's normal).</summary>
    internal bool Blocks(Position at, double radius) => marks.Any(m => m.Blocks(at, radius));

    /// <summary>Whether a straight run comes into what has been learned: judged at the run's closest point to
    /// each mark, on the mark's own terms.</summary>
    internal bool Blocks(Segment run, double radius) => marks.Any(m => m.Blocks(run.ClosestTo(m), radius));

    /// <summary>How far the closest mark lies from a run; positive infinity when nothing has been learned.</summary>
    internal double DistanceFrom(Segment run) => marks.Count == 0 ? double.PositiveInfinity : marks.Min(run.DistanceTo);

    /// <summary>How far the closest mark lies from a point; positive infinity when nothing has been learned.</summary>
    internal double DistanceFrom(Position at) => marks.Count == 0 ? double.PositiveInfinity : marks.Min(at.DistanceTo);
}
