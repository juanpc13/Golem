using GolemDomain.Geometry;
using GolemDomain.Layouts;
using GolemDomain.Maps;

namespace GolemDomain.Routes;

/// <summary>
/// The way a NEW errand would take, asked before the errand exists (Juan, 14-sep-2026: high level, no primitives —
/// the stops are told one by one as objects, not as two arrays of numbers): <c>preview = g.Preview(Position(@x, @y),
/// @cover); preview.Then(map.Find(@area1)); preview.Then(Position(@x2, @y2)); foreach (legs in preview.Legs()) { … }</c>.
/// A read: nothing of it reaches the journal; the errand's command writes the legs it answered.
/// </summary>
internal sealed class Preview
{
    private readonly MapLayout layout;
    private readonly RoutePlanner planner;
    private readonly Position from;
    private readonly bool choosesOrder;
    private readonly List<Position> stops = new();

    internal Preview(MapLayout layout, RoutePlanner planner, Position from, bool choosesOrder)
    {
        if (layout == null) throw new GolemDomainException("Preview.Preview: 'layout' was not given");
        if (planner == null) throw new GolemDomainException("Preview.Preview: 'planner' was not given");
        if (from == null) throw new GolemDomainException("Preview.Preview: 'from' was not given");
        this.layout = layout;
        this.planner = planner;
        this.from = from;
        this.choosesOrder = choosesOrder;
    }

    /// <summary>One more stop, in the order given; refused when it is nowhere on the map.</summary>
    internal Preview Then(Position stop)
    {
        if (stop == null) throw new GolemDomainException("Preview.Then: 'stop' was not given");
        if (!layout.IsOnMap(stop)) throw new GolemDomainException($"the point ({Fmt(stop.X)}, {Fmt(stop.Y)}) is nowhere on the map");
        stops.Add(stop);
        return this;
    }

    /// <summary>One more stop: an area's centre.</summary>
    internal Preview Then(Area area)
    {
        if (area == null) throw new GolemDomainException("Preview.Then: 'area' was not given");
        return Then(layout.Of(area).Center);
    }

    /// <summary>The legs of the way through the stops told so far — in the order given, or the one the golem chooses.</summary>
    internal IReadOnlyList<Leg> Legs()
    {
        if (stops.Count == 0) throw new GolemDomainException("a preview needs at least one stop");
        return planner.Road(from, choosesOrder ? planner.BestOrder(from, stops) : stops).Legs();
    }

    private static string Fmt(double d) => d.ToString("0.##", System.Globalization.CultureInfo.InvariantCulture);
}
