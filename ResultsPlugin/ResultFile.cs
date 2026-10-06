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

    // ---------------------------------------------------------------------
    // ACRESCENTADOS PELA LIGA. O acServer nao escreve nenhum dos dois.
    //
    // ACRESCENTAR E SEGURO, RENOMEAR NAO E -- ver o aviso no topo desta
    // classe. Leitor que nao conhece estes campos simplesmente os ignora, e
    // os arquivos antigos continuam validos (chegam com string vazia).

    /// <summary>
    /// O nome do servidor, que o formato do acServer nao carrega.
    ///
    /// SEM ELE NAO HA COMO SABER DE ONDE VEIO o resultado: o arquivo so tem
    /// pista, tipo e tempos. Quem importa varios servidores precisa separar.
    /// </summary>
    public string ServerName { get; init; } = "";

    /// <summary>
    /// A etapa a que este resultado pertence, ou vazio em treino livre.
    ///
    /// ESTE CAMPO E A DISTINCAO ENTRE OS DOIS TIPOS DE IMPORTACAO. Vazio, o
    /// resultado alimenta so o ranking do servidor; preenchido, ele e o
    /// resultado OFICIAL daquela etapa.
    ///
    /// POR QUE NAO IMITAMOS O ACC: la o codigo do evento vai escondido num
    /// sufixo "#XXXX" do nome do servidor, e o casamento ainda depende de
    /// bater o nome da pista. Renomear o servidor quebra em silencio. Aqui e
    /// um campo proprio, escrito por quem gera a etapa e lido por quem
    /// importa -- e continua valendo se todo o resto mudar.
    ///
    /// VEM DA CONFIGURACAO DO PLUGIN (`plugin_results_cfg.yml`), que o
    /// gerador de pacote da liga escreve junto com o resto da etapa.
    /// </summary>
    public string EventoId { get; init; } = "";
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

    // ----------------- AS CONDICOES NO INSTANTE DESTA VOLTA -----------------
    //
    // ACRESCENTADO PELA LIGA. O acServer nao grava condicao nenhuma.
    //
    // POR VOLTA, E NAO POR SESSAO. A temperatura do `server_cfg.ini` e a
    // INICIAL: numa corrida de 30 minutos o asfalto sobe vários graus e o grip
    // evolui com a borracha. Guardar um bloco unico no topo registraria as
    // condicoes do FIM -- e a volta rapida costuma vir quando elas mudaram.
    // Assim um recorde de pista carrega as condicoes DELE, e duas voltas so se
    // comparam sabendo em que asfalto cada uma foi feita.
    //
    // LIDO QUANDO A VOLTA FECHA, em `OnLapCompleted`, e nao na hora de
    // escrever o arquivo -- que acontece no fim da sessao.

    /// <summary>Temperatura do ar, em graus.</summary>
    public float TempAr { get; init; }

    /// <summary>Temperatura do asfalto, em graus.</summary>
    public float TempPista { get; init; }

    /// <summary>Grip da pista, de 0 a 1. Evolui com a borracha e com a chuva.</summary>
    public float Grip { get; init; }

    /// <summary>Intensidade da chuva, 0 em pista seca.</summary>
    public float Chuva { get; init; }

    /// <summary>Quanto a pista esta molhada, 0 a 1. Seca depois de parar de chover.</summary>
    public float Molhado { get; init; }

    /// <summary>Vento em km/h e a direcao em graus.</summary>
    public float VentoKmh { get; init; }

    public int VentoGraus { get; init; }

    /// <summary>Hora NA PISTA, no formato "HH:MM" -- nao e a hora do servidor.</summary>
    public string HoraNaPista { get; init; } = "";
}
