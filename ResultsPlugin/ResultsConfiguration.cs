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

    [YamlMember(Description =
        "League event this server is running, written into every result file as "
        + "EventoId. Leave empty for free practice servers: an empty value means "
        + "the result only feeds the server ranking, a filled one makes it the "
        + "official result of that event")]
    public string EventoId { get; init; } = "";
}
