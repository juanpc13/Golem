namespace GolemHost.Navigation;

// The seam between the golem and its body's locomotion. The golem asks for a point — and
// says how close counts: a leg's waypoint is met tightly, a leader's spot at a standoff —
// and gets a verdict; HOW the body gets there, and what counts as failure on the way, is the
// navigator's business. DiffDriveNavigator drives a body in Gazebo over rosbridge; a
// Nav2Navigator would hand the same request to Nav2 on a real robot, and the golem, its
// journal, its reactions and its tells would not change.
// Nothing here touches the journal: the golem journals the verdict, not the drive.
public interface INavigator
{
    Task<Outcome> GoToAsync(double x, double y, double within, CancellationToken ct);
}

// How a run ended: reached, or not — and why, in the navigator's words.
public sealed class Outcome
{
    public static readonly Outcome Arrived = new(reached: true, "arrived");

    public static Outcome Failed(string reason) => new(reached: false, reason);

    public bool Reached { get; }
    public string Reason { get; }

    private Outcome(bool reached, string reason)
    {
        Reached = reached;
        Reason = reason;
    }
}
