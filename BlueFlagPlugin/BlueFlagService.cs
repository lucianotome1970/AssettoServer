using AssettoServer.Server;
using AssettoServer.Shared.Model;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace BlueFlagPlugin;

/// <summary>
/// Blue flags that know who is actually a lap up.
///
/// The game shows a blue flag by proximity alone, which gets it wrong in both
/// directions on a full grid: it flags drivers who are racing each other for
/// position, and stays quiet for the driver about to be lapped. The server is
/// the only place that knows the lap counts, so this is the only place the
/// right answer can be worked out.
/// </summary>
public class BlueFlagService : BackgroundService
{
    private readonly BlueFlagConfiguration _configuration;
    private readonly EntryCarManager _entryCarManager;
    private readonly SessionManager _sessionManager;

    /// <summary>Last spline reading per car, to work out who is moving how fast.</summary>
    private readonly float[] _lastPosition;
    private readonly float[] _rate;
    private readonly bool[] _showing;
    private long _lastTick;

    public BlueFlagService(BlueFlagConfiguration configuration,
        EntryCarManager entryCarManager,
        SessionManager sessionManager)
    {
        _configuration = configuration;
        _entryCarManager = entryCarManager;
        _sessionManager = sessionManager;

        var slots = Math.Max(1, _entryCarManager.EntryCars.Length);
        _lastPosition = new float[slots];
        _rate = new float[slots];
        _showing = new bool[slots];
    }

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        Log.Information("BlueFlagPlugin: warning at {Warn}s, clearing past {Clear}s",
            _configuration.WarnSeconds, _configuration.ClearSeconds);

        using var timer = new PeriodicTimer(
            TimeSpan.FromMilliseconds(Math.Max(50, _configuration.IntervalMilliseconds)));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                // One bad tick must not end the loop: a flag system that dies
                // quietly in lap three is worse than one that never ran.
                Log.Error(ex, "BlueFlagPlugin: error while updating flags");
            }
        }
    }

    private void Tick()
    {
        var now = _sessionManager.ServerTimeMilliseconds;
        var seconds = (now - _lastTick) / 1000f;
        _lastTick = now;
        if (seconds is <= 0 or > 2) return;   // first tick, or the server stalled

        // RACE ONLY. In practice and qualifying nobody is a lap down in any
        // meaningful sense - everyone is running their own session - and
        // flagging there would be noise during a hot lap.
        if (_sessionManager.CurrentSession.Configuration.Type != SessionType.Race) return;

        var results = _sessionManager.CurrentSession.Results;
        if (results == null) return;

        var cars = _entryCarManager.EntryCars;
        for (var i = 0; i < cars.Length; i++)
        {
            var position = cars[i].Status.NormalizedPosition;
            _rate[i] = BlueFlagMath.Rate(_lastPosition[i], position, seconds);
            _lastPosition[i] = position;
        }

        for (var i = 0; i < cars.Length; i++)
        {
            var client = cars[i].Client;
            if (client is not { HasSentFirstUpdate: true })
            {
                _showing[i] = false;
                continue;
            }

            var lapper = ClosestLapper(i, results, out var secondsToCatch);
            var show = BlueFlagMath.ShouldShow(_showing[i], secondsToCatch,
                _configuration.WarnSeconds, _configuration.ClearSeconds);

            if (show == _showing[i]) continue;
            _showing[i] = show;

            client.SendPacket(new BlueFlagPacket
            {
                Show = show,
                ByCar = lapper,
                Seconds = show && secondsToCatch.HasValue
                    ? (byte)Math.Clamp(Math.Round(secondsToCatch.Value), 0, 255)
                    : (byte)0
            });

            if (_configuration.AnnounceInChat && show)
            {
                client.SendChatMessage($"Blue flag: car {lapper} is lapping you.");
            }
        }
    }

    /// <summary>
    /// The nearest car that is at least a full lap ahead and closing, with how
    /// long it will take to arrive.
    /// </summary>
    private byte ClosestLapper(int target, Dictionary<byte, EntryCarResult> results,
        out float? secondsToCatch)
    {
        secondsToCatch = null;
        byte nearest = 0;

        if (!results.TryGetValue((byte)target, out var targetResult)) return 0;

        var cars = _entryCarManager.EntryCars;
        for (var i = 0; i < cars.Length; i++)
        {
            if (i == target) continue;
            if (cars[i].Client is not { HasSentFirstUpdate: true }) continue;
            if (!results.TryGetValue((byte)i, out var result)) continue;

            // A LAP AHEAD IN THE CLASSIFICATION, not merely ahead on the road.
            // This is the whole point: the car half a straight in front may be
            // the one you are racing for position.
            if (result.NumLaps <= targetResult.NumLaps) continue;

            var gap = BlueFlagMath.ForwardGap(
                cars[i].Status.NormalizedPosition, cars[target].Status.NormalizedPosition);
            var time = BlueFlagMath.SecondsToCatch(gap, _rate[i], _rate[target]);
            if (time == null) continue;

            if (secondsToCatch == null || time < secondsToCatch)
            {
                secondsToCatch = time;
                nearest = (byte)i;
            }
        }

        return nearest;
    }
}
