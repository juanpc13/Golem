using GolemDomain.Geometry;
using GolemDomain.Layouts;

namespace GolemDomain.Touches;

/// <summary>
/// The collisions module — what the bodies LEARNED by touching, as opposed to the map (what the golem was told)
/// and the layout (where that stands). It keeps the facts — the marks where a touch met something the map does
/// not hold, the peers met, the bumps peers told about — and INTERPRETS them: the things the marks outline, who
/// was bumped near a point, what a fresh touch most likely was. It never stores a hypothesis; every reading
/// recomputes it (paper 08: the facts are testimony, the figure is inference). It leans on the layout only to
/// measure and to name the zone a mark stands in.
/// </summary>
internal sealed class Collisions
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
    /// <summary>How close two touches must be, in space, for them to be one meeting of two bodies: two radii of a
    /// body plus the error of estimating the point at the nose.</summary>
    internal const double MeetingReach = 1.2;

    private readonly List<Mark> marks = new();          // facts: where bodies touched what the map does not hold
    private readonly List<Peer> encounters = new();     // facts: where a body met another body — history, never planned around
    private readonly List<HeardBump> heard = new();     // facts: the bumps peers told about — who, where, and where they stood

    internal MapLayout Layout { get; }

    internal Collisions(MapLayout layout)
    {
        if (layout == null) throw new GolemDomainException("collisions are measured over a layout");
        Layout = layout;
    }

    // ---- the facts ----

    /// <summary>A point where a body touched something the map does not hold, with the heading of the touch (the
    /// mark's normal). Two touches within SameTouch are one mark. Returns how many marks it holds.</summary>
    internal int Mark(Pose touch)
    {
        if (touch == null) throw new GolemDomainException("a mark needs the pose of the touch");
        if (!marks.Any(m => m.DistanceTo(touch) < SameTouch)) marks.Add(new Mark(new Position(touch.X, touch.Y), touch.Heading));
        return marks.Count;
    }

    /// <summary>A body met another body standing at a point: kept as a peer among the obstacles — IN THE WAY, planned
    /// around like a thing, until the route it was met on ends (PeersMovedOn). Returns how many encounters it holds.</summary>
    internal int Meet(string who, Position at)
    {
        if (at == null) throw new GolemDomainException("Collisions.Meet: 'at' was not given");
        encounters.Add(new Peer(who, at, Layout.ZoneOf(at)?.Name ?? ""));
        return encounters.Count;
    }

    /// <summary>The peers still standing in the way: met on a route that has not ended.</summary>
    internal IReadOnlyList<Peer> PeersInTheWay() => encounters.Where(p => p.InTheWay).ToList();

    /// <summary>The route ended: every peer met on it has moved on — history from now on, nothing planned around. Returns how many.</summary>
    internal int PeersMovedOn()
    {
        int moved = 0;
        foreach (var p in encounters) if (p.InTheWay) { p.MovedOn(); moved++; }
        return moved;
    }

    /// <summary>A peer said it bumped at a point while it stood somewhere: heard and kept, so a touch of my own
    /// there and then is known to be that peer, and so I know where it is when I step out of its way.</summary>
    internal int Hear(string who, Position at, Position peerAt)
    {
        if (at == null) throw new GolemDomainException("Collisions.Hear: 'at' was not given");
        if (peerAt == null) throw new GolemDomainException("Collisions.Hear: 'peerAt' was not given");
        if (ReferenceEquals(at, peerAt)) throw new GolemDomainException("Collisions.Hear: 'at' and 'peerAt' are the same point");
        heard.Add(new HeardBump(who, at, peerAt));
        return heard.Count;
    }

    internal int MarkCount => marks.Count;
    internal int EncounterCount => encounters.Count;
    internal int HeardCount => heard.Count;
    internal IReadOnlyList<Mark> Marks => marks;
    internal IReadOnlyList<HeardBump> Heard => heard;

    /// <summary>The marks standing in a zone (a mark on a shared wall stands in both).</summary>
    internal IReadOnlyList<Mark> MarksIn(Zone zone)
    {
        if (zone == null) throw new GolemDomainException("Collisions.MarksIn: 'zone' was not given");
        return marks.Where(m => zone.Contains(m.At)).ToList();
    }

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
                if (marks[i].DistanceTo(marks[j].At) <= JoinWithin) parent[Root(i)] = Root(j);

        var obstacles = new List<Obstacle>();
        foreach (var group in Enumerable.Range(0, n).GroupBy(Root).OrderBy(g => g.Min()))
        {
            var members = group.Select(i => marks[i]).ToList();
            var centre = new Position(members.Average(m => m.At.X), members.Average(m => m.At.Y));
            var ordered = members.OrderBy(m => Math.Atan2(m.At.Y - centre.Y, m.At.X - centre.X)).ToList();
            obstacles.Add(new Thing(ordered, centre, Layout.ZoneOf(centre)?.Name ?? ""));
        }
        obstacles.AddRange(encounters);
        return obstacles;
    }

    /// <summary>The things alone: the obstacles the roads avoid. A peer is not one of them — bodies move on.</summary>
    internal IReadOnlyList<Thing> Things() => All().OfType<Thing>().ToList();

    /// <summary>The obstacles whose centre stands in a zone.</summary>
    internal IReadOnlyList<Obstacle> In(Zone zone)
    {
        if (zone == null) throw new GolemDomainException("Collisions.In: 'zone' was not given");
        return All().Where(o => zone.Contains(o.Center)).ToList();
    }

    /// <summary>Who, among the bumps heard after the given count, bumped near a point — within a meeting's reach;
    /// "" for nobody. (Robotics resolves two bodies meeting with reciprocal velocity obstacles — van den Berg, Lin
    /// &amp; Manocha, ICRA 2008; ORCA 2011 — each taking half the avoidance from what it senses of the other. Our
    /// bodies sense nothing but a touch, so they resolve it by speech: both tell the fact, and each concludes.)</summary>
    internal string HeardNear(Position at, int sinceCount)
    {
        if (at == null) throw new GolemDomainException("Collisions.HeardNear: 'at' was not given");
        for (int i = heard.Count - 1; i >= sinceCount && i >= 0; i--)
            if (heard[i].At.DistanceTo(at) <= MeetingReach) return heard[i].Who;
        return "";
    }

    /// <summary>Where a peer stood the last time it told about a bump; null if it never did.</summary>
    internal Position LastKnownPositionOf(string who)
    {
        for (int i = heard.Count - 1; i >= 0; i--)
            if (heard[i].Who == who) return heard[i].PeerAt;
        return null;
    }

    /// <summary>Whether this very word was heard already — the same peer, the same touch, standing at the same place: the wire
    /// may deliver a tell twice (17-sep-2026 lab, both journals heard the doorway bump twice); a fact is heard once.</summary>
    internal bool HeardAlready(string who, Position at, Position peerAt)
    {
        if (at == null) throw new GolemDomainException("Collisions.HeardAlready: 'at' was not given");
        if (peerAt == null) throw new GolemDomainException("Collisions.HeardAlready: 'peerAt' was not given");
        return heard.Any(h => h.Who == who && h.At.DistanceTo(at) < SameWord && h.PeerAt.DistanceTo(peerAt) < SameWord);
    }

    /// <summary>The wire's repeat of a word is the SAME word — the same pose, the same bearing, to the last digit. A peer that
    /// bumped again at the same spot, standing where it stood, says a NEW word (22-sep-2026 live: blue, parked, was bumped twice
    /// on two errands and its second word was dropped as a repeat within SameTouch; green kept its mark).</summary>
    internal const double SameWord = 1e-6;

    /// <summary>A mark taken back: the touch that made it turned out to be a body, not a thing. Only the mark at that
    /// point (within SameTouch) goes; the figure it was a vertex of is recomputed from what remains. Returns how many.</summary>
    internal int Unmark(Position at)
    {
        if (at == null) throw new GolemDomainException("taking a mark back needs where");
        return marks.RemoveAll(m => m.DistanceTo(at) < SameTouch);
    }

    /// <summary>The marks learned from a peer's bumps near a point are taken back too: the encounter was mutual, so what
    /// that peer presumed there was my body. Returns how many went.</summary>
    internal int UnmarkHeardFrom(string who, Position at)
    {
        if (at == null) throw new GolemDomainException("Collisions.UnmarkHeardFrom: 'at' was not given");
        int gone = 0;
        foreach (var h in heard)
            if (h.Who == who && h.At.DistanceTo(at) <= MeetingReach) gone += Unmark(h.At);
        return gone;
    }

    // ---- forgetting: something that was there is not there any more ----

    /// <summary>Whether an obstacle stands at a point — its centre or any of its vertices within JoinWithin of
    /// it. What the operator consults before saying it is gone.</summary>
    internal bool KnowsAt(Position at)
    {
        if (at == null) throw new GolemDomainException("Collisions.KnowsAt: 'at' was not given");
        return Nearest(at) != null;
    }

    /// <summary>Someone took it away: the golem forgets the obstacle standing there and, with it, EVERY fact that
    /// outlined it — all its marks at once, because they were vertices of one thing and the thing is gone. A body
    /// may pass there again, and if it touches something it will be a NEW obstacle, outlined by new marks.
    /// Returns how many facts were dropped; zero when nothing stands there.
    /// <para>The module forgets, the journal does not: the act of forgetting is journaled like any other, so the
    /// history still says what was believed and when it stopped being believed.</para></summary>
    internal int Forget(Position at)
    {
        if (at == null) throw new GolemDomainException("Collisions.Forget: 'at' was not given");
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
    internal bool Blocks(Position at, double radius)
    {
        if (at == null) throw new GolemDomainException("Collisions.Blocks: 'at' was not given");
        return Figures(radius).Any(f => f.Contains(at));
    }

    /// <summary>Where a body's CENTRE may not go: the figure of every thing (its extent with the mark margin) grown by
    /// the body's radius — and, while the route they were met on lasts, the figure of every peer in the way: a box around
    /// where it stands, its radius and mine and the margin (the fleet shares one body). Derived every time, like the things
    /// themselves; a planner keeps the list for one road.</summary>
    internal IReadOnlyList<Rectangle> Figures(double radius) =>
        Things().Select(t => t.Extent(MarkMargin).Inflated(radius)).Concat(PeersInTheWay().Select(p => PeerFigure(p, radius))).ToList();

    /// <summary>The berth a body keeps around a peer standing in the way: two radii and a mark's margin around its centre.</summary>
    internal static Rectangle PeerFigure(Peer peer, double radius)
    {
        if (peer == null) throw new GolemDomainException("Collisions.PeerFigure: 'peer' was not given");
        double berth = 2 * radius + MarkMargin;
        return new Rectangle(peer.Center.X - berth, peer.Center.Y - berth, 2 * berth, 2 * berth);
    }



    /// <summary>Whether a straight run comes into what has been learned: judged at the run's closest point to
    /// each mark, on the mark's own terms.</summary>
    internal bool Blocks(Segment run, double radius)
    {
        if (run == null) throw new GolemDomainException("Collisions.Blocks: 'run' was not given");
        return Figures(radius).Any(f => f.IsCrossedBy(run));
    }

    /// <summary>How far the closest mark lies from a run; positive infinity when nothing has been learned.</summary>
    internal double DistanceFrom(Segment run)
    {
        if (run == null) throw new GolemDomainException("Collisions.DistanceFrom: 'run' was not given");
        return marks.Count == 0 ? double.PositiveInfinity : marks.Min(m => run.DistanceTo(m.At));
    }

    /// <summary>How far the closest mark lies from a point; positive infinity when nothing has been learned.</summary>
    internal double DistanceFrom(Position at)
    {
        if (at == null) throw new GolemDomainException("Collisions.DistanceFrom: 'at' was not given");
        return marks.Count == 0 ? double.PositiveInfinity : marks.Min(m => m.DistanceTo(at));
    }
}
