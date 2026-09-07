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

// How a run ended: reached, or not — and why, in the navigator's words. A collision also says
// what was hit (as the world names it) and where the golem reckons the touch happened, so the
// golem can hold that point against its map and decide whether it hit something it knows.
public sealed class Outcome
{
    public static readonly Outcome Arrived = new(reached: true, "arrived", null);

    public static Outcome Failed(string reason) => new(reached: false, reason, null);

    public static Outcome Collided(string with, double x, double y) =>
        new(reached: false, $"collided with {with}", new Collision(with, x, y));

    public bool Reached { get; }
    public string Reason { get; }
    public Collision Hit { get; }   // null unless the run ended in a collision

    private Outcome(bool reached, string reason, Collision hit)
    {
        Reached = reached;
        Reason = reason;
        Hit = hit;
    }
}

public sealed record Collision(string With, double X, double Y);
