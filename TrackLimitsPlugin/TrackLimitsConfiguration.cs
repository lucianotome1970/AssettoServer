using JetBrains.Annotations;
using YamlDotNet.Serialization;

namespace TrackLimitsPlugin;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class TrackLimitsConfiguration
{
    [YamlMember(Description =
        "Metres past the track edge before a car counts as outside. This measures the car's "
        + "CENTRE, so it also encodes the wheel rule: the centre sits on the edge with two "
        + "wheels out, and roughly half a track width beyond it with three. For a car with "
        + "1.5 m between the wheels, 0.75 means 'three wheels out', which is what "
        + "ALLOWED_TYRES_OUT=2 means in the game. Add to it to be lenient about the spline's "
        + "edge differing from the surfaces the game checks")]
    public float MarginMetres { get; init; } = 0.75f;

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

    [YamlMember(Description =
        "Seconds off throttle, while off track, that forgive the excursion. Nobody leaves "
        + "the circuit already lifting - they lift a moment later - so what matters is how "
        + "long the pedal stayed shut, not whether it was ever down")]
    public float LiftSeconds { get; init; } = 0.3f;

    [YamlMember(Description = "Announce each cut in chat to the driver who made it")]
    public bool AnnounceInChat { get; init; } = true;

    [YamlMember(Description =
        "Cuts in one session before the server penalises the driver, 0 to only count. "
        + "Keep it equal to the limit the HUD shows (corte.limite), or the driver watches a "
        + "counter that disagrees with the penalty. The league sets this per event")]
    public int PenaltyAtCuts { get; init; } = 3;

    [YamlMember(Description =
        "Which penalty to apply. ALL FOUR MEASURED on track, 02/10/2026: "
        + "MandatoryPits is served by STOPPING in the box within PenaltyArgument laps, and "
        + "black-flags the driver who crosses the line without stopping. "
        + "TeleportToPits teleports the car into its box and freezes it for PenaltyArgument "
        + "SECONDS. SlowDown does nothing at all while RACE_GAS_PENALTY_DISABLED=1, which "
        + "this league sets. BlackFlag disqualifies. "
        + "THE NATIVE DRIVE-THROUGH IS NOT IN THIS LIST - the penalty a burned start gives, "
        + "served by PASSING through the pit lane, cannot be reached from the server")]
    public string PenaltyMode { get; init; } = "MandatoryPits";

    [YamlMember(Description =
        "The penalty's argument. UNITS DEPEND ON THE MODE, measured: LAPS to make the stop "
        + "for MandatoryPits, SECONDS frozen for TeleportToPits. Reading it as laps "
        + "everywhere is what the CSP SDK's naming invites, and it is wrong. "
        + "Three laps for MandatoryPits matches what the game gives for a burned start")]
    public int PenaltyArgument { get; init; } = 3;

    [YamlMember(Description = "How often to check, in milliseconds")]
    public int IntervalMilliseconds { get; init; } = 100;
}
