using JetBrains.Annotations;
using YamlDotNet.Serialization;

namespace TrackLimitsPlugin;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class TrackLimitsConfiguration
{
    [YamlMember(Description =
        "Metres past the track edge before a car counts as outside. The edge comes from "
        + "the AI spline, not from the surfaces the game itself checks, so the two disagree "
        + "slightly everywhere - this is where a league decides how strict to be")]
    public float MarginMetres { get; init; } = 1.0f;

    [YamlMember(Description =
        "Seconds a car must stay outside before it counts. Below this it is a wheel "
        + "brushing the edge, not a cut")]
    public float MinimumSecondsOutside { get; init; } = 0.4f;

    [YamlMember(Description =
        "Forgive an excursion where the driver lifted off. Running wide and lifting gives "
        + "the time back, and a rule that sometimes counts it anyway is one drivers cannot "
        + "trust")]
    public bool ForgiveLifting { get; init; } = true;

    [YamlMember(Description =
        "Throttle below this counts as lifting, 0 to 1. Not zero: a pedal that never quite "
        + "closes would turn every lift into a cut")]
    public float LiftThrottleThreshold { get; init; } = 0.15f;

    [YamlMember(Description = "Announce each cut in chat to the driver who made it")]
    public bool AnnounceInChat { get; init; } = true;

    [YamlMember(Description = "How often to check, in milliseconds")]
    public int IntervalMilliseconds { get; init; } = 100;
}
