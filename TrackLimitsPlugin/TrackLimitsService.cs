using System.Numerics;
using AssettoServer.Server;
using AssettoServer.Server.Ai.Splines;
using AssettoServer.Server.Configuration;
using AssettoServer.Shared.Network.Packets.Shared;
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
        /// <summary>
        /// Já recebeu o drive-through nesta sessão.
        ///
        /// <para>
        /// SEM ISTO, TODO CORTE DEPOIS DO LIMITE MANDA OUTRO. O piloto no quarto
        /// corte estaria cumprindo o DT do terceiro, e o quarto renovaria o prazo
        /// -- uma punição que não acaba, que é exatamente o sintoma que passamos
        /// cinco noites perseguindo. Zera com a sessão, junto com a contagem.
        /// </para>
        /// </summary>
        public bool DtAplicado;
        /// <summary>
        /// Quando o limite de pit foi mandado, ou nulo se ainda nao foi.
        ///
        /// <para>
        /// NULO E NAO <c>long.MinValue</c>. Era o sentinela, e
        /// <c>agora - long.MinValue</c> ESTOURA: em aritmetica de 64 bits o
        /// resultado da negativo, a condicao de reenvio nunca e verdadeira e o
        /// pacote nao sai uma vez sequer. Custou quatro versoes do HUD atras de
        /// um canal de rede que estava intacto -- o servidor simplesmente nunca
        /// falou. Um tipo que nao permite a conta errada vale mais que o cuidado
        /// de lembrar dela.
        /// </para>
        /// </summary>
        public long? LimiteEnviadoEm;
    }

    private readonly Dictionary<byte, Excursao> _estado = new();

    /// <summary>
    /// O modo configurado, convertido na partida e nao a cada corte.
    ///
    /// <para>
    /// NOME ERRADO NO YAML VIRA ERRO NA PARTIDA, e nao punicao silenciosamente
    /// trocada no meio de uma prova. Um <c>TryParse</c> que cai no padrao
    /// deixaria o gestor da liga achando que configurou uma coisa enquanto o
    /// servidor aplica outra -- e ele so descobriria pelo piloto reclamando.
    /// </para>
    /// </summary>
    private readonly CSPAdminPenaltyMode _modo;

    /// <summary>
    /// O limite de pit, lido do welcome na partida, ou nulo se nao der para ler.
    /// Ver <see cref="LimiteDePit"/> sobre por que ele nao tem chave propria.
    /// </summary>
    private readonly int? _limiteDePit;

    /// <summary>De quanto em quanto tempo o limite de pit e repetido.</summary>
    public const long IntervaloDoLimiteMs = 10_000;

    /// <summary>
    /// Ja passou tempo de repetir o limite?
    ///
    /// <para>
    /// SEPARADO E PUBLICO PARA TER TESTE. A versao anterior vivia numa
    /// linha dentro do laco e nao mandava nunca, por estouro de 64 bits -- e o
    /// sintoma, do lado do piloto, era identico ao de um canal de rede quebrado.
    /// Uma conta que falha em silencio e exatamente o que merece teste.
    /// </para>
    /// </summary>
    public static bool DeveReenviar(long? ultimoEnvio, long agora, long intervalo)
    {
        if (ultimoEnvio is not long quando) return true;
        return agora - quando >= intervalo;
    }

    public TrackLimitsService(TrackLimitsConfiguration configuration,
        ACServerConfiguration serverConfiguration,
        EntryCarManager entryCarManager,
        SessionManager sessionManager,
        AiSpline? spline = null,
        FastLaneParser? parser = null)
    {
        _configuration = configuration;

        // ERRO NA PARTIDA, e nao no meio da prova: ver `_modo`.
        if (!Enum.TryParse<CSPAdminPenaltyMode>(configuration.PenaltyMode, true, out _modo)
            || _modo == CSPAdminPenaltyMode.None)
        {
            throw new ArgumentException(
                $"TrackLimitsPlugin: PenaltyMode '{configuration.PenaltyMode}' is not a penalty. "
                + $"Use one of: {string.Join(", ", Enum.GetNames<CSPAdminPenaltyMode>())}.");
        }
        _entryCarManager = entryCarManager;
        _sessionManager = sessionManager;
        _spline = spline;

        _limiteDePit = LimiteDePit.Ler(serverConfiguration.WelcomeMessage);
        Log.Information("TrackLimitsPlugin: pit speed limit {Limit}",
            _limiteDePit is int kmh ? $"{kmh} km/h, from the welcome message"
                                    : "unknown - the HUD will warn without a number");

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

            // O LIMITE DE PIT, REPETIDO DE DEZ EM DEZ SEGUNDOS.
            //
            // UMA VEZ SO NAO FUNCIONA, e isso foi medido: o piloto conectou as
            // 07:16:13 e o handshake do CSP so veio as 07:16:21. Mandar na
            // primeira volta do laco entrega a mensagem OITO SEGUNDOS antes de
            // existir Lua do outro lado para ouvi-la, e ela se perde em silencio
            // -- o HUD mostrava o aviso sem numero, que e o unico modo de falha
            // que este desenho tinha.
            //
            // REPETIR TAMBEM CURA O RECARREGAMENTO: o CSP recarrega um app Lua
            // quando o arquivo muda, e o HUD recomeca sem saber o limite. Um
            // pacote de quatro bytes a cada dez segundos por piloto nao se mede.
            if (_limiteDePit is int kmhDoPit
                && DeveReenviar(estado.LimiteEnviadoEm, agora, IntervaloDoLimiteMs))
            {
                var primeira = estado.LimiteEnviadoEm == null;
                estado.LimiteEnviadoEm = agora;
                client.SendPacket(new PitLimitPacket { SpeedKmh = kmhDoPit });

                // A PRIMEIRA SO, e nao as repeticoes: isto existe para separar
                // "o servidor nao mandou" de "a mensagem nao chegou", duas
                // hipoteses que do lado do piloto sao identicas -- nada na tela.
                // O HUD loga o recebimento no log do CSP; os dois logs juntos
                // respondem, e sem eles cada tentativa custa um ciclo inteiro.
                // REPETE A PUNICAO ENQUANTO ELA ESTIVER DE PE no nosso lado.
                //
                // O servidor nao sabe quando o piloto cumpriu -- quem limpa e o
                // cliente --, mas repetir e inofensivo: o HUD so usa o modo
                // enquanto HA punicao ativa nos campos dele. E cura a mensagem
                // perdida, que aqui nao custa um aviso errado e sim uma
                // desqualificacao.
                if (estado.DtAplicado)
                {
                    client.SendPacket(new PenaltyPacket
                    {
                        Mode = (byte)_modo,
                        Argument = _configuration.PenaltyArgument
                    });
                }

                if (primeira)
                {
                    Log.Information("TrackLimitsPlugin: pit limit {Kmh} km/h sent to {Name} "
                        + "({SessionId})", kmhDoPit, client.Name, client.SessionId);
                }
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

            // A PUNICAO, DA MESMA CABECA QUE JULGOU O CORTE.
            //
            // O jogo nao tem punicao de corte em corrida -- nao conta o corte e
            // portanto nao pune. A unica punicao nativa que existe e a de queima
            // de largada, e quem a aplica e o CLIENTE. Entao a corrente inteira
            // -- medir, julgar, punir -- fica aqui, onde nenhum cliente alcanca.
            //
            // UMA VEZ POR SESSAO, por `DtAplicado`: ver o porque lá.
            if (_configuration.PenaltyAtCuts > 0
                && estado.Cortes >= _configuration.PenaltyAtCuts
                && !estado.DtAplicado)
            {
                estado.DtAplicado = true;
                PunicaoDoServidor.Aplicar(client, _modo, _configuration.PenaltyArgument,
                    $"{estado.Cortes} track limit cuts");

                // E O HUD PRECISA SABER QUAL, senao ele instrui errado -- ver
                // `PenaltyPacket`. Mandado aqui E repetido no tique do limite,
                // porque uma mensagem perdida vira instrucao errada na tela.
                client.SendPacket(new PenaltyPacket
                {
                    Mode = (byte)_modo,
                    Argument = _configuration.PenaltyArgument
                });

                // E A TRANSMISSAO PRECISA SABER DE QUEM. Este vai para TODOS,
                // com a vaga e o motivo, para a tarja de punicao entrar no ar.
                // Separado do de cima de proposito: aquele carrega a INSTRUCAO
                // de cumprimento, e mandar isso ao grid inteiro poria "PARE NO
                // BOX EM 3 VOLTAS" em trinta e cinco telas por punicao alheia.
                _entryCarManager.BroadcastPacket(new PenaltyBroadcastPacket
                {
                    Carro = client.SessionId,
                    Mode = (byte)_modo,
                    Argument = _configuration.PenaltyArgument,
                    Motivo = $"{estado.Cortes} cortes de pista",
                });

                if (_configuration.AnnounceInChat)
                {
                    // DIZ O QUE VAI ACONTECER, em vez do nome do modo. Um piloto
                    // lendo "TeleportToPits" nao sabe se deve ir ao box; e quem
                    // le "drive-through" vai PARAR no box, e parar nao cumpre
                    // drive-through -- medido, o piloto parou e tomou DSQ.
                    client.SendChatMessage(_modo switch
                    {
                        CSPAdminPenaltyMode.TeleportToPits =>
                            $"PENALTY: {_configuration.PenaltyArgument} s stopped in your box.",
                        // "PARE", e nao "pit stop extra": medido -- entrar no pit
                        // sem parar e cruzar a linha deu BANDEIRA PRETA 5,6 s
                        // depois. A instrucao errada aqui desqualifica o piloto.
                        CSPAdminPenaltyMode.MandatoryPits =>
                            $"PENALTY: STOP in your box within {_configuration.PenaltyArgument} "
                            + "laps. Passing through does NOT serve it - you will be black-flagged.",
                        CSPAdminPenaltyMode.BlackFlag => "BLACK FLAG: you are out.",
                        _ => $"PENALTY: {_modo} ({_configuration.PenaltyArgument})."
                    });
                }
            }
        }
    }
}
