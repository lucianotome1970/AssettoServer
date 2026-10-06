using System.Text.Json;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Weather;
using AssettoServer.Shared.Model;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace ResultsPlugin;

/// <summary>
/// Writes a session result file in the same JSON format acServer produces.
///
/// The format is kept byte-compatible on purpose: every league tool that
/// exists today reads acServer result files, so a server that scores races
/// but writes results nobody can parse is not usable as a drop-in.
/// </summary>
public class ResultsWriter : BackgroundService
{
    private readonly ResultsConfiguration _configuration;
    private readonly ACServerConfiguration _serverConfiguration;
    private readonly EntryCarManager _entryCarManager;
    private readonly SessionManager _sessionManager;

    /// <summary>
    /// Laps of the session being recorded.
    ///
    /// The server keeps aggregates per car (best lap, total time, lap count)
    /// but not the laps themselves, and the per-lap cut count is exactly what
    /// makes a result file worth importing: it is the only record of who went
    /// off track and when.
    /// </summary>
    private readonly List<LapEntry> _laps = [];

    /// <summary>
    /// O clima AO VIVO, para carimbar cada volta.
    ///
    /// NAO E O `server_cfg.ini`: aquele traz a temperatura INICIAL, e o que
    /// interessa e a do instante em que a volta foi feita. Ver as notas em
    /// `LapEntry`.
    /// </summary>
    private readonly WeatherManager _weatherManager;

    /// <summary>
    /// Sector times seen since each car's last completed lap.
    ///
    /// Sector splits arrive one by one while the lap is still running, and the
    /// final sector never arrives at all: it is the lap time minus the ones
    /// that did. Without this buffer the file would carry lap times with no
    /// sectors, which is what every timing tool uses to compare drivers.
    /// </summary>
    private readonly Dictionary<byte, List<uint>> _sectors = new();

    private readonly Lock _lock = new();

    public ResultsWriter(ResultsConfiguration configuration,
        ACServerConfiguration serverConfiguration,
        EntryCarManager entryCarManager,
        SessionManager sessionManager,
        WeatherManager weatherManager)
    {
        _configuration = configuration;
        _serverConfiguration = serverConfiguration;
        _entryCarManager = entryCarManager;
        _sessionManager = sessionManager;
        _weatherManager = weatherManager;
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        _entryCarManager.ClientConnected += OnClientConnected;
        _sessionManager.SessionChanged += OnSessionChanged;

        Directory.CreateDirectory(_configuration.Directory);
        Log.Information("ResultsPlugin: writing session results to {Directory}",
            Path.GetFullPath(_configuration.Directory));
        return Task.CompletedTask;
    }

    /// <summary>
    /// Writes the session that is ending, not the one starting.
    ///
    /// This runs on every session change and at shutdown, because a timed race
    /// that ends when the server is stopped would otherwise be lost entirely -
    /// and that is the session people care about most.
    /// </summary>
    public override async Task StopAsync(CancellationToken cancellationToken)
    {
        Write(_sessionManager.CurrentSession);
        await base.StopAsync(cancellationToken);
    }

    private void OnSessionChanged(SessionManager sender, SessionChangedEventArgs args)
    {
        if (args.PreviousSession != null) Write(args.PreviousSession);

        lock (_lock)
        {
            _laps.Clear();
            _sectors.Clear();
        }
    }

    private void OnClientConnected(ACTcpClient client, EventArgs args)
    {
        client.LapCompleted += OnLapCompleted;
        client.SectorSplit += OnSectorSplit;
    }

    private void OnSectorSplit(ACTcpClient sender, SectorSplitEventArgs args)
    {
        lock (_lock)
        {
            if (!_sectors.TryGetValue(sender.SessionId, out var splits))
            {
                splits = [];
                _sectors[sender.SessionId] = splits;
            }

            splits.Add(args.Packet.SplitTime);
        }
    }

    private void OnLapCompleted(ACTcpClient sender, LapCompletedEventArgs args)
    {
        var car = sender.EntryCar;

        lock (_lock)
        {
            var sectors = new List<uint>();
            if (_sectors.TryGetValue(sender.SessionId, out var splits))
            {
                sectors.AddRange(splits);
                splits.Clear();
            }

            // The last sector is never sent, it is what is left of the lap.
            // Clamped at zero because a lap can be cut short by the session
            // ending, and a negative sector would be worse than a missing one.
            // LIDO AGORA, no quadro em que a volta fechou -- e nao no fim da
            // sessao, que e quando o arquivo e escrito. `pcall` nao existe em
            // C#, mas o clima pode nao ter subido ainda num servidor recem
            // iniciado, e uma volta sem condicao e melhor que um erro.
            var clima = _weatherManager.CurrentWeather;
            var horaNaPista = "";
            try
            {
                var agora = _weatherManager.CurrentDateTime;
                horaNaPista = $"{agora.Hour:00}:{agora.Minute:00}";
            }
            catch (Exception ex)
            {
                Log.Debug(ex, "ResultsPlugin: hora na pista indisponivel nesta volta");
            }

            var measured = sectors.Aggregate(0u, (a, b) => a + b);
            sectors.Add(args.Packet.LapTime > measured ? args.Packet.LapTime - measured : 0);

            _laps.Add(new LapEntry
            {
                DriverName = sender.Name ?? "",
                DriverGuid = sender.Guid.ToString(),
                CarId = sender.SessionId,
                CarModel = car.Model,
                Timestamp = (int)_sessionManager.ServerTimeMilliseconds,
                LapTime = args.Packet.LapTime,
                Sectors = sectors,
                Cuts = args.Packet.Cuts,
                BallastKG = (int)car.Ballast,
                // The compound the car is on right now, which is what acServer
                // records for the lap. It can be null before the first position
                // update arrives, and an empty string is what readers already
                // handle for a lap whose tyre is unknown.
                Tyre = car.Status.CurrentTyreCompound ?? "",
                Restrictor = car.Restrictor,
                // AS CONDICOES DESTE INSTANTE. Ver as notas em `LapEntry`.
                TempAr = clima?.TemperatureAmbient ?? 0,
                TempPista = clima?.TemperatureRoad ?? 0,
                Grip = _serverConfiguration.Server.DynamicTrack.CurrentGrip,
                Chuva = clima?.RainIntensity ?? 0,
                Molhado = clima?.RainWetness ?? 0,
                VentoKmh = clima?.WindSpeed ?? 0,
                VentoGraus = clima?.WindDirection ?? 0,
                HoraNaPista = horaNaPista
            });
        }
    }

    private void Write(SessionState? session)
    {
        if (session?.Results == null) return;

        List<LapEntry> laps;
        lock (_lock)
        {
            laps = [.. _laps];
        }

        if (laps.Count == 0 && !_configuration.WriteEmptySessions)
        {
            Log.Debug("ResultsPlugin: {Session} had no completed laps, not writing a file",
                session.Configuration.Name);
            return;
        }

        try
        {
            // VAGA SEM PILOTO NAO ENTRA, nem aqui nem no `Result`.
            //
            // O acServer escreve uma linha por VAGA do entry_list, com guid em
            // branco nas vazias, e nos imitavamos isso. Num servidor de 35
            // vagas com um piloto, o arquivo saia com 35 carros e 35
            // resultados, 34 de cada sem nome e sem guid.
            //
            // QUEM ESCREVE O ARQUIVO SOMOS NOS, e nada le essas linhas -- o
            // importador da liga e justamente o que esta sendo escrito agora.
            // E a ausencia JA E a informacao: vaga que nao aparece no arquivo
            // e vaga que ninguem ocupou. Manter o lixo custa um filtro em todo
            // leitor futuro, e um leitor que esqueca o filtro reporta 35
            // pilotos numa sessao de um.
            //
            // O CRITERIO E TER GUID, e nao ter tempo: quem conectou e nao
            // completou volta ESTAVA na sessao, e sumir com ele apagaria um
            // abandono. Ele continua no arquivo, com `BestLap: 999999999` e
            // `TotalTime: 0` -- que e como o acServer diz "nao terminou".
            //
            // O `CarId` CONTINUA SENDO O INDICE DA VAGA, e nao a posicao na
            // lista: ele e a chave que liga `Cars`, `Result` e `Laps`, e
            // renumerar quebraria os tres de uma vez.
            var cars = _entryCarManager.EntryCars.Select((car, index) => new CarEntry
            {
                CarId = index,
                Driver = new DriverEntry
                {
                    Name = session.Results.TryGetValue((byte)index, out var r) ? r.Name : "",
                    Team = r2(session, index)?.Team ?? "",
                    Nation = r2(session, index)?.NationCode ?? "",
                    Guid = GuidOf(session, index),
                    GuidsList = [GuidOf(session, index)]
                },
                Model = car.Model,
                Skin = car.Skin,
                BallastKG = (int)car.Ballast,
                Restrictor = car.Restrictor
            }).Where(c => c.Driver.Guid.Length > 0).ToList();

            var results = session.Results
                .Where(kv => kv.Value.Guid != 0)
                .OrderBy(kv => kv.Value.RacePos == 0 ? int.MaxValue : kv.Value.RacePos)
                .Select(kv => new ResultEntry
                {
                    DriverName = kv.Value.Name,
                    // EMPTY, NOT "0", for a slot nobody took.
                    //
                    // An unoccupied entry still gets a result row, and acServer
                    // leaves its guid blank. Writing the numeric zero makes
                    // every importer that filters on "has a guid" accept the
                    // row, so a 35 slot server reports 35 drivers for a session
                    // one person drove.
                    DriverGuid = kv.Value.Guid == 0 ? "" : kv.Value.Guid.ToString(),
                    CarId = kv.Key,
                    CarModel = _entryCarManager.EntryCars[kv.Key].Model,
                    BestLap = kv.Value.BestLap,
                    TotalTime = kv.Value.TotalTime,
                    BallastKG = (int)_entryCarManager.EntryCars[kv.Key].Ballast,
                    Restrictor = _entryCarManager.EntryCars[kv.Key].Restrictor
                })
                .ToList();

            var root = new ResultFile
            {
                TrackName = PlainTrackName(_serverConfiguration.Server.Track),
                TrackConfig = _serverConfiguration.Server.TrackConfig ?? "",
                Type = TypeName(session.Configuration.Type),
                DurationSecs = session.Configuration.Laps > 0 ? 0 : session.Configuration.Time * 60,
                RaceLaps = session.Configuration.Laps,
                Cars = cars,
                Result = results,
                Laps = laps,
                Events = [],
                // DE ONDE VEM CADA UM: o nome, da configuracao do servidor; a
                // etapa, da configuracao deste plugin. Ver `ResultFile`.
                ServerName = _serverConfiguration.Server.Name ?? "",
                EventoId = _configuration.EventoId ?? ""
            };

            var now = DateTime.Now;
            // acServer's own naming, without zero padding: tools that sort or
            // match on these names in the wild were written against it.
            var name = $"{now.Year}_{now.Month}_{now.Day}_{now.Hour}_{now.Minute}_"
                + $"{TypeName(session.Configuration.Type)}.json";
            var path = Path.Combine(_configuration.Directory, name);

            File.WriteAllText(path,
                JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true }));

            // CONTA SO QUEM TEM GUID. A linha dizia "35 drivers" numa sessao
            // que uma pessoa correu, porque comparava com "0" -- e as vagas
            // vazias passaram a sair com guid VAZIO quando esse mesmo defeito
            // foi corrigido no arquivo. O arquivo estava certo e o log mentia,
            // que e a combinacao que faz alguem procurar o problema no lugar
            // errado.
            Log.Information("ResultsPlugin: wrote {Path} with {Laps} laps and {Drivers} drivers",
                path, laps.Count, results.Count(r => !string.IsNullOrEmpty(r.DriverGuid)));

            Prune();
        }
        catch (Exception ex)
        {
            // A failure here must not take the server down with it: the race is
            // already over, and losing the file is bad but losing the next
            // session is worse.
            Log.Error(ex, "ResultsPlugin: could not write the result file");
        }
    }

    private static EntryCarResult? r2(SessionState session, int index)
        => session.Results != null && session.Results.TryGetValue((byte)index, out var r) ? r : null;

    private static string GuidOf(SessionState session, int index)
    {
        var r = r2(session, index);
        return r == null || r.Guid == 0 ? "" : r.Guid.ToString();
    }

    /// <summary>
    /// The track name without CSP's extended form.
    ///
    /// With custom shaders patch options the TRACK key carries a prefix -
    /// "csp/2651/../sr_guapore" - that exists for the client handshake and is
    /// not a track anyone can look up. acServer writes the plain name in its
    /// result files, and readers match results to a track by that name, so
    /// writing the prefixed form would quietly orphan every imported session.
    /// </summary>
    internal static string PlainTrackName(string track)
    {
        var t = track.Trim();
        if (!t.StartsWith("csp/", StringComparison.Ordinal)) return t;
        var i = t.LastIndexOf('/');
        return i >= 0 ? t[(i + 1)..] : t;
    }

    /// <summary>acServer's session names, which readers match on.</summary>
    private static string TypeName(SessionType type) => type switch
    {
        SessionType.Booking => "BOOK",
        SessionType.Practice => "PRACTICE",
        SessionType.Qualifying => "QUALIFY",
        SessionType.Race => "RACE",
        _ => "PRACTICE"
    };

    private void Prune()
    {
        if (_configuration.KeepFiles <= 0) return;

        try
        {
            var files = new DirectoryInfo(_configuration.Directory)
                .GetFiles("*.json")
                .OrderByDescending(f => f.LastWriteTimeUtc)
                .Skip(_configuration.KeepFiles);

            foreach (var file in files) file.Delete();
        }
        catch (Exception ex)
        {
            Log.Warning(ex, "ResultsPlugin: could not prune old result files");
        }
    }
}
