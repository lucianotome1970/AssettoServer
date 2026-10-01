using JetBrains.Annotations;

namespace ResultsPlugin;

/// <summary>
/// The shape of an acServer result file.
///
/// Property names are the JSON keys, so they are the names acServer uses and
/// not the ones this codebase would pick. Renaming any of them silently breaks
/// every existing reader.
/// </summary>
[UsedImplicitly(ImplicitUseKindFlags.Access, ImplicitUseTargetFlags.WithMembers)]
public class ResultFile
{
    public string TrackName { get; init; } = "";
    public string TrackConfig { get; init; } = "";
    public string Type { get; init; } = "";
    public int DurationSecs { get; init; }
    public int RaceLaps { get; init; }
    public List<CarEntry> Cars { get; init; } = [];
    public List<ResultEntry> Result { get; init; } = [];
    public List<LapEntry> Laps { get; init; } = [];
    public List<object> Events { get; init; } = [];
}

[UsedImplicitly(ImplicitUseKindFlags.Access, ImplicitUseTargetFlags.WithMembers)]
public class CarEntry
{
    public int CarId { get; init; }
    public DriverEntry Driver { get; init; } = new();
    public string Model { get; init; } = "";
    public string Skin { get; init; } = "";
    public int BallastKG { get; init; }
    public int Restrictor { get; init; }
}

[UsedImplicitly(ImplicitUseKindFlags.Access, ImplicitUseTargetFlags.WithMembers)]
public class DriverEntry
{
    public string Name { get; init; } = "";
    public string Team { get; init; } = "";
    public string Nation { get; init; } = "";
    public string Guid { get; init; } = "";
    public List<string> GuidsList { get; init; } = [];
}

[UsedImplicitly(ImplicitUseKindFlags.Access, ImplicitUseTargetFlags.WithMembers)]
public class ResultEntry
{
    public string DriverName { get; init; } = "";
    public string DriverGuid { get; init; } = "";
    public int CarId { get; init; }
    public string CarModel { get; init; } = "";
    public uint BestLap { get; init; }
    public uint TotalTime { get; init; }
    public int BallastKG { get; init; }
    public int Restrictor { get; init; }
}

[UsedImplicitly(ImplicitUseKindFlags.Access, ImplicitUseTargetFlags.WithMembers)]
public class LapEntry
{
    public string DriverName { get; init; } = "";
    public string DriverGuid { get; init; } = "";
    public int CarId { get; init; }
    public string CarModel { get; init; } = "";
    public int Timestamp { get; init; }
    public uint LapTime { get; init; }
    public List<uint> Sectors { get; init; } = [];
    public int Cuts { get; init; }
    public int BallastKG { get; init; }
    public string Tyre { get; init; } = "";
    public int Restrictor { get; init; }
}
