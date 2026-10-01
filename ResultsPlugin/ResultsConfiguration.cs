using AssettoServer.Server.Configuration;
using JetBrains.Annotations;
using YamlDotNet.Serialization;

namespace ResultsPlugin;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public class ResultsConfiguration
{
    [YamlMember(Description = "Directory for result files, relative to the server executable")]
    public string Directory { get; init; } = "results";

    [YamlMember(Description =
        "Write a file even when nobody completed a lap. acServer skips those sessions, "
        + "and tools that watch the results directory generally expect the same")]
    public bool WriteEmptySessions { get; init; } = false;

    [YamlMember(Description =
        "Keep at most this many result files, deleting the oldest. 0 keeps everything")]
    public int KeepFiles { get; init; } = 0;
}
