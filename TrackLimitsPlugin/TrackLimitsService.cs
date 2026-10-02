using System.Numerics;
using AssettoServer.Server;
using AssettoServer.Server.Ai.Splines;
using AssettoServer.Server.Configuration;
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

    /// <summary>
    /// The pit lane, so that using it is not mistaken for leaving the circuit.
    ///
    /// MEASURED, NOT GUESSED: a driver serving a drive-through produced the
    /// same pair on every lap -- a 23 second excursion 12 m from the racing
    /// line while crawling through the pits, forgiven for lifting, and then a
    /// 7 second one at 10 m with the throttle wide open on the way out, which
    /// counted. Five cuts in four laps, all of them the pit lane.
    ///
    /// Null when the track has no pit_lane.ai; then pit passes count as
    /// excursions and the log says so, which is better than pretending.
    /// </summary>
    private readonly SplinePoint[]? _pitLane;

    private sealed class Excursao
    {
        public bool Fora;
        public long DesdeMs;
        public float MaiorAcelerador;
        public float SegundosSemAcelerador;
        public float MaiorDistancia;
        public float DesvioNoPior;
        public int Cortes;
    }

    private readonly Dictionary<byte, Excursao> _estado = new();

    public TrackLimitsService(TrackLimitsConfiguration configuration,
        ACServerConfiguration serverConfiguration,
        EntryCarManager entryCarManager,
        SessionManager sessionManager,
        AiSpline? spline = null,
        FastLaneParser? parser = null)
    {
        _configuration = configuration;
        _entryCarManager = entryCarManager;
        _sessionManager = sessionManager;
        _spline = spline;

        if (parser == null) return;
        try
        {
            var pista = serverConfiguration.CSPTrackOptions.Track;
            var caminho = Path.Join("content", $"tracks/{pista}/ai/pit_lane.ai");
            _pitLane = parser.FromSingleFile(caminho)?.Points;
        }
        catch (Exception ex)
        {
            // Sem pit lane o plugin ainda funciona, so conta as passagens pelo
            // box como saida -- entao isto avisa e segue, em vez de derrubar o
            // servidor por causa de um arquivo de pista.
            Log.Warning(ex, "TrackLimitsPlugin: could not read the pit lane spline");
        }
    }

    /// <summary>
    /// Is the car using the pit lane rather than leaving the circuit?
    ///
    /// Compares which is nearer: the racing line or the pit lane. Nothing else
    /// separates the two reliably -- the server has no pit flag, and judging
    /// by speed alone would forgive anyone who spins slowly off the circuit.
    /// </summary>
    private bool NoPitLane(System.Numerics.Vector3 posicao, float distanciaDaPista)
    {
        if (_pitLane == null) return false;

        var maisPerto = float.MaxValue;
        foreach (var ponto in _pitLane)
        {
            var d = System.Numerics.Vector3.DistanceSquared(ponto.Position, posicao);
            if (d < maisPerto) maisPerto = d;
        }

        return maisPerto < distanciaDaPista * distanciaDaPista;
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

        // DIZ SE O PIT LANE CARREGOU, porque sem ele TODA passagem pelo box
        // vira corte -- e foi assim que cinco paradas viraram cinco punicoes
        // sem ninguem entender. Silencio aqui faria o proximo caso parecer
        // defeito de geometria em vez de arquivo faltando.
        if (_pitLane == null)
        {
            Log.Warning("TrackLimitsPlugin: no pit_lane.ai for this track, so pit lane passes "
                + "will be counted as going off track");
        }
        else
        {
            Log.Information("TrackLimitsPlugin: pit lane loaded, {Points} points - passes "
                + "through it do not count", _pitLane.Length);
        }

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

            // O PIT LANE NAO E FORA DE PISTA. So e consultado quando ja parece
            // saida: a busca e linear sobre centenas de pontos, e rodar isso
            // para 35 carros a cada quadro seria pagar caro por uma pergunta
            // que quase sempre tem a mesma resposta.
            if (fora && NoPitLane(car.Status.Position, Math.Abs(desvio))) fora = false;

            // ACCELERATOR AS A BYTE, 0 to 255 on the wire.
            var acelerador = car.Status.Gas / 255f;

            if (fora)
            {
                if (!estado.Fora)
                {
                    estado.Fora = true;
                    estado.DesdeMs = agora;
                    estado.MaiorAcelerador = 0;
                    estado.SegundosSemAcelerador = 0;
                    estado.MaiorDistancia = 0;
                    estado.DesvioNoPior = 0;
                }

                // HOW LONG THE PEDAL STAYED SHUT, which is what forgives.
                //
                // The peak throttle was here instead, and it is useless:
                // nobody leaves the circuit already lifting, so the peak is
                // 100% on every excursion including the ones where the driver
                // did give the time back.
                if (acelerador < _configuration.LiftThrottleThreshold)
                {
                    estado.SegundosSemAcelerador += _configuration.IntervalMilliseconds / 1000f;
                }

                estado.MaiorAcelerador = Math.Max(estado.MaiorAcelerador, acelerador);

                var distancia = TrackLimitsMath.HowFarOut(desvio, ponto.SideLeft, ponto.SideRight,
                    _configuration.MarginMetres);
                if (distancia > estado.MaiorDistancia)
                {
                    estado.MaiorDistancia = distancia;
                    // GUARDA O DESVIO COM SINAL do pior instante: e dele que
                    // sai o LADO no registro, e o lado e a unica pergunta que
                    // a leitura do codigo nao responde -- so a pista.
                    estado.DesvioNoPior = desvio;
                }
                continue;
            }

            if (!estado.Fora) continue;

            // Back on track: judge the excursion that just ended.
            estado.Fora = false;
            var segundos = (agora - estado.DesdeMs) / 1000f;

            var lado = TrackLimitsMath.SideName(estado.DesvioNoPior);

            if (!TrackLimitsMath.CountsAsCut(segundos, estado.SegundosSemAcelerador,
                    _configuration.MinimumSecondsOutside, _configuration.LiftSeconds,
                    _configuration.ForgiveLifting))
            {
                // EM Information, E NAO Debug. O que NAO contou e metade da
                // medicao: sem ver as saidas perdoadas, nao da para calibrar a
                // margem nem saber se o perdao esta funcionando.
                Log.Information("TrackLimitsPlugin: {Name} went {Metres:F1} m wide to the {Side} "
                    + "for {Seconds:F1} s, {Lifted:F1} s off throttle - not counted",
                    client.Name, estado.MaiorDistancia, lado, segundos,
                    estado.SegundosSemAcelerador);
                continue;
            }

            estado.Cortes++;
            Log.Information("TrackLimitsPlugin: cut by {Name} - {Metres:F1} m wide to the {Side} "
                + "for {Seconds:F1} s, peak {Throttle:P0} throttle, {Lifted:F1} s off it "
                + "(total {Total})",
                client.Name, estado.MaiorDistancia, lado, segundos, estado.MaiorAcelerador,
                estado.SegundosSemAcelerador, estado.Cortes);

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
