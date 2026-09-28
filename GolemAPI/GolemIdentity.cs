namespace GolemAPI;

// Who this process is: the golem (its identity, names the journal), the body it drives (the model in the world, named in its
// ROS topics) and the PEERS it can reach by name — every `tell-<name>` route of TELL_ROUTES — so the panel offers them as the
// golems a line may name (`golem red,blue …`; Juan, 28-sep-2026: "un selector en el panel, botones de los otros golems").
public sealed record GolemIdentity(string Golem, string Body, IReadOnlyList<string> Peers);
