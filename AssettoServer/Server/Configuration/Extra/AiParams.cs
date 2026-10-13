using System.Collections.Generic;
using CommunityToolkit.Mvvm.ComponentModel;
using JetBrains.Annotations;
using YamlDotNet.Serialization;

namespace AssettoServer.Server.Configuration.Extra;

[UsedImplicitly(ImplicitUseKindFlags.Assign, ImplicitUseTargetFlags.WithMembers)]
public partial class AiParams : ObservableObject
{
    [YamlMember(Description = "Automatically assign traffic cars based on the car folder name")]
    public bool AutoAssignTrafficCars { get; init; } = true;
    [YamlMember(Description = "Radius around a player in which AI cars won't despawn")]
    public float PlayerRadiusMeters { get; set; } = 200.0f;
    [YamlMember(Description = "Offset the player radius in direction of the velocity of the player so AI cars will despawn earlier behind a player")]
    public float PlayerPositionOffsetMeters { get; set; } = 100.0f;
    [YamlMember(Description = "AFK timeout for players. Players who are AFK longer than this won't spawn AI cars")]
    public long PlayerAfkTimeoutSeconds { get; set; } = 10;
    [YamlMember(Description = "Maximum distance to the AI spline for a player to spawn AI cars. This helps with parts of the map without traffic so AI cars won't spawn far away from players")]
    public float MaxPlayerDistanceToAiSplineMeters { get; set; } = 7;
    [YamlMember(Description = "Minimum amount of spline points in front of a player where AI cars will spawn")]
    public int MinSpawnDistancePoints { get; set; } = 100;
    [YamlMember(Description = "Maximum amount of spline points in front of a player where AI cars will spawn")]
    public int MaxSpawnDistancePoints { get; set; } = 400;
    [YamlMember(Description = "Minimum distance between AI cars")]
    public int MinAiSafetyDistanceMeters { get; set; } = 20;
    [YamlMember(Description = "Maximum distance between AI cars")]
    public int MaxAiSafetyDistanceMeters { get; set; } = 70;
    [YamlMember(Description = "Minimum spawn distance for AI states of the same car slot. If you set this too low you risk AI states despawning or AI states becoming invisible for some players when multiple states are close together")]
    public float StateSpawnDistanceMeters { get; set; } = 1000;
    [YamlMember(Description = "Minimum distance between AI states of the same car slot. If states get closer than this one of them will be forced to despawn")]
    public float MinStateDistanceMeters { get; set; } = 200;
    [YamlMember(Description = "Minimum spawn distance to players")]
    public float SpawnSafetyDistanceToPlayerMeters { get; set; } = 150;
    [YamlMember(Description = "Minimum time in which a newly spawned AI car cannot despawn")]
    public int MinSpawnProtectionTimeSeconds { get; set; } = 4;
    [YamlMember(Description = "Maximum time in which a newly spawned AI car cannot despawn")]
    public int MaxSpawnProtectionTimeSeconds { get; set; } = 8;
    [YamlMember(Description = "Minimum time an AI car will stop/slow down after a collision")]
    public int MinCollisionStopTimeSeconds { get; set; } = 1;
    [YamlMember(Description = "Maximum time an AI car will stop/slow down after a collision")]
    public int MaxCollisionStopTimeSeconds { get; set; } = 3;
    [YamlMember(Description = "Default maximum AI speed. This is not an absolute maximum and can be overridden with RightLaneOffsetKph and MaxSpeedVariationPercent")]
    public float MaxSpeedKph { get; set; } = 80;
    [YamlMember(Description = "Speed offset for right lanes. Will be added to MaxSpeedKph")]
    public float RightLaneOffsetKph { get; set; } = 10;
    [YamlMember(Description = "Maximum speed variation")]
    public float MaxSpeedVariationPercent { get; set; } = 0.15f;
    
    [ObservableProperty]
    [property: YamlMember(Description = "Default AI car deceleration for obstacle/collision detection (m/s^2)")]
    private float _defaultDeceleration = 8.5f;
    
    [ObservableProperty]
    [property: YamlMember(Description = "Default AI car acceleration for obstacle/collision detection (m/s^2)")]
    private float _defaultAcceleration = 2.5f;
    
    [ObservableProperty]
    [property: YamlMember(Description = "Maximum AI car target count for AI slot overbooking. This is not an absolute maximum and might be slightly higher (0 = auto/best)", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    private int _maxAiTargetCount;
    
    [ObservableProperty]
    [property: YamlMember(Description = "Number of AI cars per player the server will try to keep (0 = auto/best)", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    private int _aiPerPlayerTargetCount;
    
    [YamlMember(Description = "Soft player limit, the server will stop accepting new players when this many players are reached. Use this to ensure a minimum amount of AI cars. 0 to disable.")]
    public int MaxPlayerCount { get; set; } = 0;
    [YamlMember(Description = "Hide AI car nametags and make them invisible on the minimap. Broken on CSP versions < 0.1.78")]
    public bool HideAiCars { get; set; } = true;
    
    [ObservableProperty]
    [property: YamlMember(Description = "AI spline height offset. Use this if the AI spline is too close to the ground")]
    private float _splineHeightOffsetMeters = 0;
    
    [YamlMember(Description = "Lane width for adjacent lane detection")]
    public float LaneWidthMeters { get; init; } = 3.0f;
    [YamlMember(Description = "Enable two way traffic. This will allow AI cars to spawn in lanes with the opposite direction of travel to the player.")]
    public bool TwoWayTraffic { get; set; } = false;
    [YamlMember(Description = "Enable traffic spawning if the player is driving the wrong way. Only takes effect when TwoWayTraffic is set to false.")]
    public bool WrongWayTraffic { get; set; } = true;
    
    [ObservableProperty]
    [property: YamlMember(Description = "AI cornering speed factor. Lower = AI cars will drive slower around corners.")]
    private float _corneringSpeedFactor = 0.65f;
    
    [ObservableProperty]
    [property: YamlMember(Description = "AI cornering brake distance factor. Lower = AI cars will brake later for corners.")]
    private float _corneringBrakeDistanceFactor = 3;
    
    [ObservableProperty]
    [property: YamlMember(Description = "AI cornering brake force factor. This is multiplied with DefaultDeceleration. Lower = AI cars will brake less hard for corners.")]
    private float _corneringBrakeForceFactor = 0.5f;
    
    [YamlMember(Description = "Name prefix for AI cars. Names will be in the form of '<NamePrefix> <SessionId>'")]
    public string NamePrefix { get; init; } = "Traffic";
    [YamlMember(Description = "Ignore obstacles for some time if the AI car is stopped for longer than x seconds")]
    public int IgnoreObstaclesAfterSeconds { get; set; } = 10;
    
    [ObservableProperty]
    [property: YamlMember(Description = "Apply scale to some traffic density related settings. Increasing this DOES NOT magically increase your traffic density, it is dependent on your other settings. Values higher than 1 not recommended.", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    private float _trafficDensity = 1.0f;
    
    [YamlMember(Description = "Dynamic (hourly) traffic density. List must have exactly 24 entries in the format [0.2, 0.5, 1, 0.7, ...]")]
    public List<float>? HourlyTrafficDensity { get; set; }
    
    [ObservableProperty]
    [property: YamlMember(Description = "Tyre diameter of AI cars in meters, shouldn't have to be changed unless some cars are creating lots of smoke.")]
    private float _tyreDiameterMeters = 0.65f;
    
    [YamlMember(Description = "Apply some smoothing to AI spline camber")]
    public bool SmoothCamber { get; init; } = true;
    [YamlMember(Description = "Show debug overlay for AI cars", DefaultValuesHandling = DefaultValuesHandling.OmitDefaults)]
    public bool Debug { get; set; } = false;
    [YamlMember(Description = "Update interval for AI spawn point finder")]
    public int AiBehaviorUpdateIntervalHz { get; set; } = 2;
    [YamlMember(Description = "Enable AI car headlights during the day")]
    public bool EnableDaytimeLights { get; set; } = false;
    [YamlMember(Description = "AI cars inside these areas will ignore all player obstacles")]
    public List<Sphere>? IgnorePlayerObstacleSpheres { get; set; }
    [YamlMember(Description = "Override some settings for newly spawned cars based on the number of lanes")]
    public Dictionary<int, LaneCountSpecificOverrides> LaneCountSpecificOverrides { get; set; } = new();

    [YamlMember(Description = "Override some settings for specific car models")]
    public List<CarSpecificOverrides> CarSpecificOverrides { get; init; } = [];

    // ───────────────────────── MODO CORRIDA ─────────────────────────
    //
    // A IA daqui nasceu para TRAFEGO: ela e teleportada ao redor dos jogadores,
    // some quando ninguem esta perto e nao conta volta -- numa estrada aberta
    // isso e exatamente o certo, porque o que importa e haver carros no campo
    // de visao de quem dirige.
    //
    // Numa pista isso nao serve. Um carro que desaparece quando o lider abre
    // vantagem nao esta correndo; esta decorando a paisagem. O modo corrida
    // troca as tres regras: nasce uma vez, fica, e a volta dele conta.
    //
    // DESLIGADO POR PADRAO, e de proposito -- o upstream e um servidor de
    // trafego e tem que continuar se comportando como um.

    [YamlMember(Description = "Race mode: AI cars stay on track, start from the grid and count laps (instead of traffic behaviour)")]
    public bool ModoCorrida { get; set; } = false;

    [YamlMember(Description = "Race mode: spacing between AI cars on the starting grid, in metres")]
    public float CorridaEspacoNoGridMetros { get; set; } = 12.0f;

    // CADA PILOTO COM O SEU RITMO, e o MESMO ritmo a cada volta.
    //
    // Sem isto os quinze andam no mesmo passo e a corrida vira comboio: ninguem
    // alcanca ninguem, e nao ha o que narrar. Com variacao, abrem-se brigas
    // sozinhas ao longo da prova.
    //
    // POR VAGA, E NAO SORTEADO A CADA VOLTA: um piloto que oscila de ritmo nao
    // e disputa, e ruido -- e quem narra perde a historia ("este vinha mais
    // rapido a tres voltas") que e justamente o que faz a corrida ter enredo.
    [YamlMember(Description = "Race mode: pace spread between the fastest and slowest AI, as a fraction (0.04 = 4%)")]
    public float CorridaVariacaoDeRitmo { get; set; } = 0.04f;

    // O QUANTO ELES SAEM DA LINHA DE CORRIDA PARA PASSAR, EM METROS.
    //
    // EM METROS, E NAO EM FRACAO DA PISTA -- e isto foi medido, nao suposto.
    // A primeira versao usava `SideLeft`/`SideRight` do `fast_lane.ai` como
    // orcamento, por achar que fossem a distancia ate a borda do asfalto. Nao
    // sao: em Guapore a mediana e 6,5 m mas o MAXIMO e 45,4 m -- eles medem
    // ate um limite externo qualquer, incluindo escape, grama e patio. Com
    // 55% disso, o carro ia 25 m para fora na primeira curva e fazia a volta
    // inteira na grama.
    //
    // UMA LARGURA DE CARRO BASTA para ficar ao lado de alguem, e e tudo o que
    // uma ultrapassagem precisa. Mais do que isso nao ajuda a passar -- so
    // afasta da linha rapida.
    [YamlMember(Description = "Race mode: how far AI move off the racing line to overtake, in metres")]
    public float CorridaDesvioMetros { get; set; } = 1.8f;

    [YamlIgnore] public float PlayerRadiusSquared => PlayerRadiusMeters * PlayerRadiusMeters;
    [YamlIgnore] public float PlayerAfkTimeoutMilliseconds => PlayerAfkTimeoutSeconds * 1000;
    [YamlIgnore] public float MaxPlayerDistanceToAiSplineSquared => MaxPlayerDistanceToAiSplineMeters * MaxPlayerDistanceToAiSplineMeters;
    [YamlIgnore] public int MinAiSafetyDistanceSquared => MinAiSafetyDistanceMeters * MinAiSafetyDistanceMeters;
    [YamlIgnore] public int MaxAiSafetyDistanceSquared => MaxAiSafetyDistanceMeters * MaxAiSafetyDistanceMeters;
    [YamlIgnore] public float StateSpawnDistanceSquared => StateSpawnDistanceMeters * StateSpawnDistanceMeters;
    [YamlIgnore] public float MinStateDistanceSquared => MinStateDistanceMeters * MinStateDistanceMeters;
    [YamlIgnore] public float SpawnSafetyDistanceToPlayerSquared => SpawnSafetyDistanceToPlayerMeters * SpawnSafetyDistanceToPlayerMeters;
    [YamlIgnore] public int MinSpawnProtectionTimeMilliseconds => MinSpawnProtectionTimeSeconds * 1000;
    [YamlIgnore] public int MaxSpawnProtectionTimeMilliseconds => MaxSpawnProtectionTimeSeconds * 1000;
    [YamlIgnore] public int MinCollisionStopTimeMilliseconds => MinCollisionStopTimeSeconds * 1000;
    [YamlIgnore] public int MaxCollisionStopTimeMilliseconds => MaxCollisionStopTimeSeconds * 1000;
    [YamlIgnore] public float MaxSpeedMs => MaxSpeedKph / 3.6f;
    [YamlIgnore] public float RightLaneOffsetMs => RightLaneOffsetKph / 3.6f;
    [YamlIgnore] public int IgnoreObstaclesAfterMilliseconds => IgnoreObstaclesAfterSeconds * 1000;
    [YamlIgnore] public int AiBehaviorUpdateIntervalMilliseconds => 1000 / AiBehaviorUpdateIntervalHz;
}
