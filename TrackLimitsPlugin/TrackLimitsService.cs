using System.Numerics;
using AssettoServer.Server;
using AssettoServer.Server.Ai.Splines;
using Microsoft.Extensions.Hosting;
using Serilog;

namespace TrackLimitsPlugin;

/// <summary>
/// Counts cuts on the server, because the game does not count them in a race.
///
/// Measured across every result file this league has: practice counts cuts,
/// qualifying counts cuts, and race - thirteen files, forty eight laps - never
/// counts a single one. It makes sense from the engine's side, since a cut
/// invalidates a lap in practice and invalidates nothing in a race, but it
/// leaves a league with no cut data in the only session that awards points.
///
/// So the judging happens here, from geometry the server can see for itself:
/// where the car is, how wide the circuit is there, and how much throttle was
/// applied. None of it can be forged by a modified client.
/// </summary>
public class TrackLimitsService : BackgroundService
{
    private readonly TrackLimitsConfiguration _configuration;
    private readonly EntryCarManager _entryCarManager;
    private readonly SessionManager _sessionManager;
    private readonly AiSpline? _spline;

    private sealed class Excursao
    {
        public bool Fora;
        public long DesdeMs;
        public float MaiorAcelerador;
        public float MaiorDistancia;
        public int Cortes;
    }

    private readonly Dictionary<byte, Excursao> _estado = new();

    public TrackLimitsService(TrackLimitsConfiguration configuration,
        EntryCarManager entryCarManager,
        SessionManager sessionManager,
        AiSpline? spline = null)
    {
        _configuration = configuration;
        _entryCarManager = entryCarManager;
        _sessionManager = sessionManager;
        _spline = spline;
    }

    /// <summary>Cuts counted for a car in this session.</summary>
    public int CutsOf(byte sessionId) => _estado.TryGetValue(sessionId, out var e) ? e.Cortes : 0;

    protected override async Task ExecuteAsync(CancellationToken stoppingToken)
    {
        if (_spline == null)
        {
            // SAYING WHICH SWITCH. "Plugin did nothing" sends an admin reading
            // three config files on a Sunday morning.
            Log.Error("TrackLimitsPlugin: no AI spline loaded, so there is no track geometry "
                + "to judge against. Set LoadAiSplineWithoutAi: true in extra_cfg.yml, and "
                + "make sure the track has an ai/fast_lane.ai");
            return;
        }

        Log.Information("TrackLimitsPlugin: margin {Margin} m, minimum {Seconds} s outside, "
            + "lifting {Forgives}", _configuration.MarginMetres,
            _configuration.MinimumSecondsOutside,
            _configuration.ForgiveLifting ? "forgives" : "does not forgive");

        using var timer = new PeriodicTimer(
            TimeSpan.FromMilliseconds(Math.Max(50, _configuration.IntervalMilliseconds)));

        _sessionManager.SessionChanged += (_, _) => _estado.Clear();

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Tick();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "TrackLimitsPlugin: error while checking track limits");
            }
        }
    }

    private void Tick()
    {
        var agora = _sessionManager.ServerTimeMilliseconds;

        foreach (var car in _entryCarManager.EntryCars)
        {
            var client = car.Client;
            if (client is not { HasSentFirstUpdate: true }) continue;

            if (!_estado.TryGetValue(car.SessionId, out var estado))
            {
                estado = new Excursao();
                _estado[car.SessionId] = estado;
            }

            var (pointId, _) = _spline!.WorldToSpline(car.Status.Position);
            if (pointId < 0) continue;

            var ponto = _spline.Points[pointId];
            var desvio = TrackLimitsMath.SignedOffset(
                car.Status.Position, ponto.Position, _spline.Operations.GetForwardVector(pointId));

            var fora = TrackLimitsMath.IsOutside(
                desvio, ponto.SideLeft, ponto.SideRight, _configuration.MarginMetres);

            // ACCELERATOR AS A BYTE, 0 to 255 on the wire.
            var acelerador = car.Status.Gas / 255f;

            if (fora)
            {
                if (!estado.Fora)
                {
                    estado.Fora = true;
                    estado.DesdeMs = agora;
                    estado.MaiorAcelerador = 0;
                    estado.MaiorDistancia = 0;
                }

                // THE MOST throttle used out there, not the throttle right now.
                // A driver who lifts only as the car rejoins did not give the
                // time back, and sampling the instant would let that pass.
                estado.MaiorAcelerador = Math.Max(estado.MaiorAcelerador, acelerador);
                estado.MaiorDistancia = Math.Max(estado.MaiorDistancia,
                    TrackLimitsMath.HowFarOut(desvio, ponto.SideLeft, ponto.SideRight,
                        _configuration.MarginMetres));
                continue;
            }

            if (!estado.Fora) continue;

            // Back on track: judge the excursion that just ended.
            estado.Fora = false;
            var segundos = (agora - estado.DesdeMs) / 1000f;

            if (!TrackLimitsMath.CountsAsCut(segundos, estado.MaiorAcelerador,
                    _configuration.MinimumSecondsOutside, _configuration.LiftThrottleThreshold,
                    _configuration.ForgiveLifting))
            {
                Log.Debug("TrackLimitsPlugin: {Name} went {Metres:F1} m wide for {Seconds:F1} s "
                    + "with {Throttle:P0} throttle - not counted",
                    client.Name, estado.MaiorDistancia, segundos, estado.MaiorAcelerador);
                continue;
            }

            estado.Cortes++;
            Log.Information("TrackLimitsPlugin: cut by {Name} - {Metres:F1} m wide for "
                + "{Seconds:F1} s with {Throttle:P0} throttle (total {Total})",
                client.Name, estado.MaiorDistancia, segundos, estado.MaiorAcelerador, estado.Cortes);

            client.SendPacket(new TrackLimitsPacket
            {
                Cuts = estado.Cortes,
                MetresOut = estado.MaiorDistancia
            });

            if (_configuration.AnnounceInChat)
            {
                client.SendChatMessage(
                    $"Track limits: cut {estado.Cortes} ({estado.MaiorDistancia:F1} m wide).");
            }
        }
    }
}
