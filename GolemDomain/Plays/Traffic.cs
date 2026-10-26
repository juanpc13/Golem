using GolemDomain.Geometry;
using GolemDomain.Routes;

namespace GolemDomain.Plays;

/// <summary>THE TRAFFIC PLAY (propuesta 125, 23-oct-2026; Juan: "3 robots como en una carretera y el semáforo les habla por tell para poder
/// pasar uno recto y otro girando a su izquierda o derecha"): a LIGHT and the CARS. Every car knows its journey (<see cref="Journeys"/>) and
/// drives, in ONE route decided whole, to the STOP LINE of its arm — where the route has a GATE and the body stands —, then through the
/// crossing to the EXIT POINT of the arm its turn leaves by, then to that arm's far end. The LAW is the light's: the crossing takes ONE
/// car at a time, and the first that stopped at its line passes first — the order is the one the light's own journal heard (the light
/// is the one part that observes it, which is why this play has a coordinator and the formations do not). The words between them are
/// facts, never orders (paper 04): a car STOPPED at its line, the light GRANTED the crossing to one, a car CLEARED it; every copy keeps
/// them, and the end — every car cleared — is concluded in every copy and announced by the clock like Pacman's.</summary>
internal sealed class Traffic : Play
{
    internal const string KindName = "traffic";

    private readonly Journeys journeys;
    private readonly List<string> queue = new();                       // the cars stopped at their line, in the order THIS copy heard them
    private readonly Dictionary<string, Direction> arrivals = new();   // which arm each one stopped on, as its word said
    private readonly Dictionary<string, int> stoodAt = new();           // the place of its queue each car said it stopped at (§12: the one behind sets out on it)
    private readonly HashSet<string> cleared = new();
    private string holder = "";                                        // the car the crossing was granted to, until it cleared
    private int permits;
    // the car's own copy
    private Direction? arm, exit;
    private Position stopLine;
    private int slot;                                                  // the car's place in its arm's queue (§12): 0 the line
    private Crossing crossing;                                         // the crossing it drives through, kept for the queue's advances

    internal Traffic(string name, Cast cast, Journeys journeys, string stamp) : base(name, KindName, cast, stamp)
    {
        if (journeys == null) throw new GolemDomainException("Traffic.Traffic: 'journeys' was not given");
        foreach (var car in cast.Ghosts) if (!journeys.Knows(car)) throw new GolemDomainException($"the car '{car}' has no journey: the journeys are {journeys.Text}");
        foreach (var car in journeys.Cars) if (!cast.Ghosts.Contains(car)) throw new GolemDomainException($"'{car}' has a journey but is no car of the cast: the cars are {cast.Roster}");
        this.journeys = journeys;
    }

    internal string Light => Cast.Pacman;
    internal IReadOnlyList<string> Cars => Cast.Ghosts;
    internal Journeys Journeys => journeys;
    internal override Part PartOf(Golem golem) => Cast.PartOf(golem) == Part.Pacman ? Part.Light : Part.Car;
    internal override Part PartOf(string name) => Cast.PartOf(name) == Part.Pacman ? Part.Light : Part.Car;
    internal bool Started { get; private set; }

    internal void Start()
    {
        if (IsOver) throw new GolemDomainException($"the play '{Name}' is over");
        State = PlayState.Running;
        Started = true;
    }

    // ---- the car's drive ----

    /// <summary>The car's journey through the crossing, as STOPS of one route: its place in its arm's queue (the gate: the stop line itself
    /// when it is first, a pitch behind per car ahead — §12), the exit point of the arm its turn leaves by, and its parking place at that arm's
    /// far end (the k-th to park there by name parks k pitches short of the farthest). Decided from where the car stands, which must be on
    /// its arm — the arm its journey says, or the one it stands on when the journeys say no arms.</summary>
    internal IReadOnlyList<Position> Plan(Crossing crossing, Position from, string car)
    {
        if (crossing == null) throw new GolemDomainException("Traffic.Plan: 'crossing' was not given");
        if (from == null) throw new GolemDomainException("Traffic.Plan: 'from' was not given");
        string me = Car(car, "Plan");
        var standsOn = crossing.ArmOf(from);
        var comingFrom = journeys.KnowArms ? journeys.ArmOf(me) : standsOn;
        if (comingFrom != standsOn) throw new GolemDomainException($"'{me}' comes from the {comingFrom.ToString().ToLowerInvariant()} arm by its journey but stands on the {standsOn.ToString().ToLowerInvariant()} one");
        var leavingBy = Crossing.Exit(comingFrom, journeys.Of(me));
        this.crossing = crossing;
        arm = comingFrom;
        exit = leavingBy;
        slot = SlotOf(me);
        if (slot >= crossing.SlotsOf(comingFrom)) throw new GolemDomainException($"the {comingFrom.ToString().ToLowerInvariant()} arm queues {crossing.SlotsOf(comingFrom)} cars at most: '{me}' would be the {slot + 1}th");
        stopLine = crossing.StopLine(comingFrom, slot);
        return new[] { stopLine }.Concat(WayThrough(me)).ToList();
    }

    /// <summary>The stops of the way through the crossing: the turn point when the car turns (square, along its lanes), the exit point of the
    /// arm it leaves by, and its parking place at that arm's far end.</summary>
    private IReadOnlyList<Position> WayThrough(string me)
    {
        var stops = new List<Position>();
        if (ExitArm != Crossing.Opposite(Arm)) stops.Add(crossing.TurnPoint(Arm, ExitArm));
        stops.Add(crossing.ExitPoint(ExitArm));
        stops.Add(crossing.FarEnd(ExitArm, ParkingSlotOf(me)));
        return stops;
    }

    /// <summary>THE QUEUE (§12): a car's place on its arm — the cars of that arm sorted by name, less those that already CLEARED the crossing:
    /// every copy computes the same, no word needed. Not at the green (lab 23-oct-2026: red advanced the instant blue got its green, and ran
    /// into blue still turning to leave): the car ahead is gone from the arm only once it cleared; the light serves the other arms meanwhile.</summary>
    internal int SlotOf(string car)
    {
        string me = Car(car, "SlotOf");
        if (!journeys.KnowArms) return 0;
        return journeys.OnArm(journeys.ArmOf(me)).TakeWhile(c => c != me).Count(c => !cleared.Contains(c));
    }

    /// <summary>Where a car parks at the far end of the arm it leaves by: the k-th GRANTED among those leaving by that arm parks k pitches short
    /// (lab 23-oct-2026: by name, yellow parked ahead of red and red, granted later, drove into it). Known the moment the green comes, in every
    /// copy alike (the greens are the light's words, in order); before any green, nobody ahead.</summary>
    internal int ParkingSlotOf(string car)
    {
        string me = Car(car, "ParkingSlotOf");
        if (!journeys.KnowArms) return 0;
        var exitArm = Crossing.Exit(journeys.ArmOf(me), journeys.Of(me));
        return granted.TakeWhile(c => c != me).Count(c => c != me && Crossing.Exit(journeys.ArmOf(c), journeys.Of(c)) == exitArm);
    }

    internal int Slot => slot;
    internal bool IsFirst => slot == 0;

    /// <summary>The queue moved (§12): a car ahead on my arm cleared the crossing — my place is nearer the line than my route's gate. Not while I
    /// hold the green, have cleared, or am not driving.</summary>
    internal bool MayAdvance => IsDriving && carName != null && !ClearedHere && !HasGreen(carName) && journeys.KnowArms && route != null && route.IsPending() && SlotNow < slot;

    private int SlotNow => arm == null ? 0 : (carName == null ? slot : SlotOf(carName));
    private string carName;
    private readonly List<string> granted = new();      // every car the light gave the green to, IN ORDER (§12: the parking places follow it)

    /// <summary>THE CAR ADVANCES (§12): its route to the old place abandoned, the new stops — the nearer place of the queue (the gate), the same
    /// exit and parking — handed back so the golem opens the route from where it stands.</summary>
    internal IReadOnlyList<Position> Advance(string car)
    {
        string me = Car(car, "Advance");
        if (!IsDriving) throw new GolemDomainException("the car is not driving yet");
        int now = SlotOf(me);
        if (now >= slot) throw new GolemDomainException($"'{me}' is at its place already: the {slot + 1}th of the {Arm.ToString().ToLowerInvariant()} queue");
        if (route != null && route.IsPending()) route.Abandon($"the queue moved: from the {slot + 1}th place to the {now + 1}th");
        slot = now;
        IsStopped = false;   // at the new place it stops again (its word only from the line)
        stopLine = crossing.StopLine(Arm, slot);
        return new[] { stopLine }.Concat(WayThrough(me)).ToList();
    }

    internal void Driving(Route drive, string car)
    {
        if (drive == null) throw new GolemDomainException("Traffic.Driving: 'drive' was not given");
        route = drive;
        carName = Car(car, "Driving");
    }

    internal bool IsDriving => arm != null;
    internal Direction Arm => arm ?? throw new GolemDomainException("the car is not driving yet");
    internal Direction ExitArm => exit ?? throw new GolemDomainException("the car is not driving yet");
    internal string ArmName => IsDriving ? Arm.ToString().ToLowerInvariant() : "";
    /// <summary>The arm as the enum's member name — what a tell carries, so the hearer's uptake coerces it back (ajuste 90).</summary>
    internal string ArmWord => IsDriving ? Arm.ToString() : "";
    internal Position StopLine => stopLine ?? throw new GolemDomainException("the car is not driving yet");

    /// <summary>The car stands at its place of the queue — the route waits at its gate — and has not said so yet (§12: every place has its
    /// word; the light counts the line's, the car behind sets out on the one ahead's).</summary>
    internal bool AtItsPlace => IsDriving && !IsStopped && route != null && route.IsPending() && route.WaitsAtGate;
    /// <summary>At the line itself (the first place) with its word to say.</summary>
    internal bool AtTheLine => AtItsPlace && IsFirst;
    internal bool IsStopped { get; private set; }
    /// <summary>Lined up at its start and waiting for the car ahead to be at its place (§12): the route waits to set out.</summary>
    internal bool WaitsToSetOut => IsDriving && route != null && route.IsPending() && route.WaitsToSetOut;

    /// <summary>THE CAR STOPPED AT ITS PLACE: its own word, said once per place, from where the body really stands.</summary>
    internal void Stop(Pose me)
    {
        if (me == null) throw new GolemDomainException("Traffic.Stop: 'me' was not given");
        if (!AtItsPlace) throw new GolemDomainException(IsStopped ? "the car said it stopped already" : WaitsToSetOut ? "the car waits to set out: the one ahead is not at its place yet" : "the car is not standing at its place");
        IsStopped = true;
    }

    /// <summary>The car ahead of this one on its arm (by name, among those not through), or "" when it is first.</summary>
    internal string Ahead(string car)
    {
        string me = Car(car, "Ahead");
        if (!journeys.KnowArms) return "";
        return journeys.OnArm(journeys.ArmOf(me)).TakeWhile(c => c != me).LastOrDefault(c => !cleared.Contains(c)) ?? "";
    }

    /// <summary>The car behind may SET OUT (§12): lined up and waiting at its start, and the one ahead said it stopped at its place (lab 23-oct-2026:
    /// red, already facing the junction after its place, drove into orange still turning at the place ahead).</summary>
    internal bool MaySetOut(string car)
    {
        string me = Car(car, "MaySetOut");
        if (!WaitsToSetOut) return false;
        string ahead = Ahead(me);
        return ahead != "" && stoodAt.ContainsKey(ahead) && stoodAt[ahead] == SlotOf(ahead);
    }

    /// <summary>THE WAY THROUGH, at the green (§12): the route that waited at the line is left and the stops through the crossing come back —
    /// the exit point and the PARKING PLACE as the greens' order gives it (the k-th car granted toward that arm parks k pitches short).</summary>
    internal IReadOnlyList<Position> Through(string car)
    {
        string me = Car(car, "Through");
        if (!IsDriving) throw new GolemDomainException("the car is not driving yet");
        if (!HasGreen(me)) throw new GolemDomainException($"'{me}' has no green: the crossing is {(IsFree ? "free" : holder + "'s")}");
        if (route != null && route.IsPending()) route.Abandon("the green: the way through the crossing");
        return WayThrough(me);
    }

    /// <summary>Whether the car holds the crossing: granted and not cleared yet.</summary>
    internal bool HasGreen(string me) => !string.IsNullOrWhiteSpace(me) && holder == me.Trim().ToLowerInvariant();
    internal bool ClearedHere { get; private set; }

    /// <summary>The car is past the crossing: it holds the green, its exit point is behind (one stop left, the far end, or none) and it has not said so.</summary>
    internal bool PastTheCrossing(string me) => HasGreen(me) && !ClearedHere && route != null && route.StopsLeft <= 1;

    /// <summary>THE CAR CLEARED THE CROSSING: its own word, once; the crossing is free in its copy too.</summary>
    internal void Clear(string me, Pose at)
    {
        if (at == null) throw new GolemDomainException("Traffic.Clear: 'at' was not given");
        if (!PastTheCrossing(me)) throw new GolemDomainException(ClearedHere ? "the car said it cleared already" : "the car is not past the crossing");
        ClearedHere = true;
        Freed(me);
    }

    // ---- the words every copy keeps, and the light's law ----

    /// <summary>A car stopped at its line (its word): it joins the queue, in the order heard here. A car already through is not queued again.</summary>
    internal void Queued(string who, Direction arm) => Queued(who, arm, 0);

    /// <summary>A car stopped at a place of its queue (its word, §12): at the line, it joins the light's queue in the order heard here; at any
    /// place, every copy keeps where it stands, so the car behind it may set out.</summary>
    internal void Queued(string who, Direction arm, int slot)
    {
        string car = Car(who, "Queued");
        if (slot < 0) throw new GolemDomainException("Traffic.Queued: a place of the queue is 0 or more");
        arrivals[car] = arm;
        stoodAt[car] = slot;
        if (slot > 0 || cleared.Contains(car) || holder == car) return;
        if (!queue.Contains(car)) queue.Add(car);
    }

    internal int QueueCount => queue.Count;
    internal string QueueText => string.Join(",", queue);
    internal bool IsFree => holder == "";
    internal string Holder => holder;
    internal int Permits => permits;

    /// <summary>The arm a car LEAVES by, from the arm its word said it stopped on and its journey.</summary>
    internal Direction ExitArmOf(string car) => Crossing.Exit(arrivals[Car(car, "ExitArmOf")], journeys.Of(car));

    /// <summary>A ROAD OF ONE LANE (lab 23-oct-2026: red, granted, drove straight into blue waiting at the line of the arm red left by): a car's
    /// way is BLOCKED while another car waits at the line of the arm it would leave by, or holds the crossing coming from it, or already
    /// cleared the crossing into that same arm (it stands at its far end). The light passes it over until the arm is free.</summary>
    internal bool IsBlocked(string car)
    {
        string who = Car(car, "IsBlocked");
        if (!arrivals.ContainsKey(who)) return false;
        if (crossing != null && crossing.Lanes(arrivals[who]) == 2) return false;   // two lanes (§12): the inbound and the outbound never meet; the far end has a place per car
        if (crossing == null && journeys.KnowArms) return false;                     // the light's copy knows no geometry: a road told with arms is the two-lane one
        var exit = ExitArmOf(who);
        foreach (var other in arrivals.Keys)
        {
            if (other == who) continue;
            bool waitingThere = (queue.Contains(other) || holder == other) && arrivals[other] == exit;   // standing at that arm's line
            bool parkedThere = cleared.Contains(other) && ExitArmOf(other) == exit;                       // gone into that arm, at its far end
            if (waitingThere || parkedThere) return true;
        }
        return false;
    }

    internal string BlockedText => string.Join(",", queue.Where(IsBlocked));

    /// <summary>The light may grant: the play runs, the crossing is free and a car waits at its line whose exit arm is free.</summary>
    internal bool MayGrant => IsRunning && IsFree && queue.Any(c => !IsBlocked(c));

    /// <summary>THE LIGHT GRANTS THE CROSSING to the first car that stopped whose exit arm is free: the law, on the light's own journal.
    /// One at a time; a car whose way is blocked is passed over until the arm it leaves by is free.</summary>
    internal Permit Grant()
    {
        if (!IsRunning) throw new GolemDomainException($"the play '{Name}' is not running");
        if (!IsFree) throw new GolemDomainException($"the crossing is {holder}'s until it clears");
        if (queue.Count == 0) throw new GolemDomainException("no car waits at its line");
        string next = queue.FirstOrDefault(c => !IsBlocked(c)) ?? throw new GolemDomainException($"every car waiting is blocked by another at the line of the arm it leaves by: {QueueText}");
        holder = next;
        if (!granted.Contains(next)) granted.Add(next);
        queue.Remove(next);
        permits++;
        return new Permit(Name, holder, permits);
    }

    /// <summary>The light granted the crossing to a car (its word, in every copy): that car holds it.</summary>
    internal void Granted(string to)
    {
        string car = Car(to, "Granted");
        if (cleared.Contains(car)) return;
        holder = car;
        if (!granted.Contains(car)) granted.Add(car);
        queue.Remove(car);
    }

    /// <summary>A car cleared the crossing (its word, in every copy): the crossing is free; the last one clears, the play is OVER here.</summary>
    internal void Freed(string who)
    {
        string car = Car(who, "Freed");
        cleared.Add(car);
        if (holder == car) holder = "";
        queue.Remove(car);
        if (cleared.Count == Cars.Count && IsRunning) Over("crossing");
    }

    internal int ClearedCount => cleared.Count;
    internal string ClearedText => string.Join(",", cleared.OrderBy(n => n, StringComparer.Ordinal));
    internal bool IsDone => cleared.Count == Cars.Count;

    /// <summary>The end of the traffic play abandons nothing: a car still driving to its far end finishes its journey.</summary>
    protected override void Closing(string why) { }

    private string Car(string who, string verb)
    {
        if (string.IsNullOrWhiteSpace(who)) throw new GolemDomainException($"Traffic.{verb}: 'who' was not given");
        string car = who.Trim().ToLowerInvariant();
        if (!Cars.Contains(car)) throw new GolemDomainException($"'{car}' is no car of the play: the cars are {Cast.Roster}");
        return car;
    }
}

/// <summary>THE PERMIT the light gives (propuesta 125): which car has the crossing, and the number of the grant — the once of its word.</summary>
internal sealed record Permit(string Play, string To, int Number);
