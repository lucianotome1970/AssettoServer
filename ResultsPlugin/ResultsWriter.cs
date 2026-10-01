using System.Text.Json;
using AssettoServer.Network.Tcp;
using AssettoServer.Server;
using AssettoServer.Server.Configuration;
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
        SessionManager sessionManager)
    {
        _configuration = configuration;
        _serverConfiguration = serverConfiguration;
        _entryCarManager = entryCarManager;
        _sessionManager = sessionManager;
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
                // The server does not track which compound a car is on, so
                // this stays empty rather than guessing. Leaving the field out
                // entirely would break readers that expect acServer's shape.
                Tyre = "",
                Restrictor = car.Restrictor
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
            }).ToList();

            var results = session.Results
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
                Events = []
            };

            var now = DateTime.Now;
            // acServer's own naming, without zero padding: tools that sort or
            // match on these names in the wild were written against it.
            var name = $"{now.Year}_{now.Month}_{now.Day}_{now.Hour}_{now.Minute}_"
                + $"{TypeName(session.Configuration.Type)}.json";
            var path = Path.Combine(_configuration.Directory, name);

            File.WriteAllText(path,
                JsonSerializer.Serialize(root, new JsonSerializerOptions { WriteIndented = true }));

            Log.Information("ResultsPlugin: wrote {Path} with {Laps} laps and {Drivers} drivers",
                path, laps.Count, results.Count(r => r.DriverGuid != "0"));

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
