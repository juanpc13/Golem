namespace GolemHost;

// Who this process is: the golem (its identity, names the journal) and the body it drives
// (the model in the world, named in its ROS topics).
public sealed record GolemIdentity(string Golem, string Body);
