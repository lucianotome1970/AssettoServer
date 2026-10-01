using JetBrains.Annotations;
using YamlDotNet.Serialization;

namespace BlueFlagPlugin;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class BlueFlagConfiguration
{
    [YamlMember(Description = "Show the flag when the lapping car is this many seconds away")]
    public float WarnSeconds { get; init; } = 4.0f;

    [YamlMember(Description =
        "Clear it again past this many seconds. Must be larger than WarnSeconds: "
        + "with a single threshold the flag blinks while the gap hovers around it")]
    public float ClearSeconds { get; init; } = 7.0f;

    [YamlMember(Description =
        "Also announce in chat. Reaches drivers without the client side script, "
        + "at the cost of a chat line they cannot dismiss")]
    public bool AnnounceInChat { get; init; } = false;

    [YamlMember(Description = "How often to recalculate, in milliseconds")]
    public int IntervalMilliseconds { get; init; } = 250;
}
