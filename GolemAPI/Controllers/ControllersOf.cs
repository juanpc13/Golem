using Microsoft.AspNetCore.Mvc.ApplicationParts;
using Microsoft.AspNetCore.Mvc.Controllers;

namespace GolemAPI.Controllers;

// WHICH CONTROLLERS A PROCESS SERVES (ajuste 88): the one image hosts a golem OR the warden — a golem answers with GolemController and
// OperatorController, the warden with WardenController; both share TellController (the wire). The others are left out of the application's
// parts, or `/`, `/command`, `/commands`, `/query`, `/events` would match twice.
public sealed class ControllersOf : IApplicationFeatureProvider<ControllerFeature>
{
    private readonly HashSet<Type> kept;

    public ControllersOf(params Type[] kept) => this.kept = new HashSet<Type>(kept);

    public void PopulateFeature(IEnumerable<ApplicationPart> parts, ControllerFeature feature)
    {
        for (int i = feature.Controllers.Count - 1; i >= 0; i--)
            if (!kept.Contains(feature.Controllers[i].AsType())) feature.Controllers.RemoveAt(i);
    }

    /// <summary>A golem's process: the operator's and the golem's endpoints, and the wire's.</summary>
    public static ControllersOf AGolem() => new(typeof(GolemController), typeof(OperatorController), typeof(TellController));
    /// <summary>The warden's process: its endpoints, and the wire's.</summary>
    public static ControllersOf TheWarden() => new(typeof(WardenController), typeof(TellController));
}
