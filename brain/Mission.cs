namespace GolemHost;

// One journaled task: where to go and how it ended.
public class Mission
{
    public int Id { get; }
    public double X { get; }
    public double Y { get; }
    public string Status { get; set; } = "pending";
    public string Reason { get; set; } = "";

    public Mission(int id, double x, double y)
    {
        Id = id;
        X = x;
        Y = y;
    }
}
