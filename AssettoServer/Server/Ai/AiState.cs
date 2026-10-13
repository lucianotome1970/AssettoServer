using System;
using System.Collections.Generic;
using System.Drawing;
using System.Numerics;
using AssettoServer.Server.Ai.Splines;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Configuration.Extra;
using AssettoServer.Server.Weather;
using AssettoServer.Shared.Model;
using AssettoServer.Shared.Network.Packets.Outgoing;
using AssettoServer.Shared.Utils;
using AssettoServer.Utils;
using JPBotelho;
using Serilog;
using SunCalcNet.Model;

namespace AssettoServer.Server.Ai;

public class AiState : IDisposable
{
    public CarStatus Status { get; } = new();
    public bool Initialized { get; private set; }

    public int CurrentSplinePointId
    {
        get;
        private set
        {
            _spline.SlowestAiStates.Enter(value, this);
            _spline.SlowestAiStates.Leave(field, this);
            field = value;
        }
    }

    public long SpawnProtectionEnds { get; set; }
    public float SafetyDistanceSquared { get; set; } = 20 * 20;
    public float Acceleration { get; set; }
    public float CurrentSpeed { get; private set; }
    public float TargetSpeed { get; private set; }
    public float InitialMaxSpeed { get; private set; }
    public float MaxSpeed { get; private set; }
    public Color Color { get; private set; }
    public byte SpawnCounter { get; private set; }
    public float ClosestAiObstacleDistance { get; private set; }
    public EntryCar EntryCar { get; }

    private const float WalkingSpeed = 10 / 3.6f;

    private Vector3 _startTangent;
    private Vector3 _endTangent;

    private float _currentVecLength;

    /// <summary>
    /// Quando este carro cruzou a linha pela ultima vez, em tempo de servidor.
    /// </summary>
    /// <remarks>
    /// ZERO ATE A PRIMEIRA PASSAGEM, e a primeira nao conta volta: ela comeca
    /// no grid, no meio do tracado, e o tempo dela seria um pedaco de volta.
    /// Contar essa daria a todo mundo uma volta de brinde, com tempo absurdo
    /// como melhor volta da sessao.
    /// </remarks>
    private long _inicioDaVoltaMs;

    /// <summary>
    /// Quanto este carro anda fora da linha de corrida, em metros.
    /// </summary>
    /// <remarks>
    /// <para>
    /// POSITIVO E PARA A DIREITA. Zero e a propria linha, que e onde toda a IA
    /// de trafego anda -- e por isso eles andavam em fila indiana, sem
    /// ultrapassagem possivel: dois carros no mesmo ponto do tracado ocupavam o
    /// mesmo asfalto.
    /// </para>
    /// <para>
    /// O LIMITE E A PISTA DE VERDADE, ponto a ponto: o `fast_lane.ai` traz
    /// `SideLeft` e `SideRight`, a distancia da linha ate a borda de cada lado.
    /// Um numero fixo poria roda na grama onde a pista estreita -- em Guapore
    /// ha trechos em que 2 m ja e fora.
    /// </para>
    /// </remarks>
    private float _desvioLateral;
    private float _currentVecProgress;
    private long _lastTick;
    private bool _stoppedForObstacle;
    private long _stoppedForObstacleSince;
    private long _ignoreObstaclesUntil;
    private long _stoppedForCollisionUntil;
    private long _obstacleHonkStart;
    private long _obstacleHonkEnd;
    private CarStatusFlags _indicator = 0;
    private int _nextJunctionId;
    private bool _junctionPassed;
    private float _endIndicatorDistance;
    private float _minObstacleDistance;
    private double _randomTwilight;

    private readonly ACServerConfiguration _configuration;
    private readonly SessionManager _sessionManager;
    private readonly EntryCarManager _entryCarManager;
    private readonly WeatherManager _weatherManager;
    private readonly AiSpline _spline;
    private readonly JunctionEvaluator _junctionEvaluator;

    private static readonly List<Color> CarColors =
    [
        Color.FromArgb(13, 17, 22),
        Color.FromArgb(19, 24, 31),
        Color.FromArgb(28, 29, 33),
        Color.FromArgb(12, 13, 24),
        Color.FromArgb(11, 20, 33),
        Color.FromArgb(151, 154, 151),
        Color.FromArgb(153, 157, 160),
        Color.FromArgb(194, 196, 198),
        Color.FromArgb(234, 234, 234),
        Color.FromArgb(255, 255, 255),
        Color.FromArgb(182, 17, 27),
        Color.FromArgb(218, 25, 24),
        Color.FromArgb(73, 17, 29),
        Color.FromArgb(35, 49, 85),
        Color.FromArgb(28, 53, 81),
        Color.FromArgb(37, 58, 167),
        Color.FromArgb(21, 92, 45),
        Color.FromArgb(18, 46, 43)
    ];

    public AiState(EntryCar entryCar, SessionManager sessionManager, WeatherManager weatherManager, ACServerConfiguration configuration, EntryCarManager entryCarManager, AiSpline spline)
    {
        EntryCar = entryCar;
        _sessionManager = sessionManager;
        _weatherManager = weatherManager;
        _configuration = configuration;
        _entryCarManager = entryCarManager;
        _spline = spline;
        _junctionEvaluator = new JunctionEvaluator(spline);

        _lastTick = _sessionManager.ServerTimeMilliseconds;
    }

    ~AiState()
    {
        Despawn();
    }
    
    public void Dispose()
    {
        Despawn();
        GC.SuppressFinalize(this);
    }

    public void Despawn()
    {
        Initialized = false;
        _spline.SlowestAiStates.Leave(CurrentSplinePointId, this);
    }

    private void SetRandomSpeed()
    {
        float variation = _configuration.Extra.AiParams.MaxSpeedMs * _configuration.Extra.AiParams.MaxSpeedVariationPercent;

        float fastLaneOffset = 0;
        if (_spline.Points[CurrentSplinePointId].LeftId >= 0)
        {
            fastLaneOffset = _configuration.Extra.AiParams.RightLaneOffsetMs;
        }
        // EM CORRIDA O RITMO E DO PILOTO, E NAO DO SORTEIO.
        //
        // No trafego, sortear a cada nascimento e o certo: os carros passam uma
        // vez e nunca mais, e a variedade e toda a graca. Numa corrida o mesmo
        // carro reaparece volta apos volta, e um ritmo que muda a cada vez nao
        // e disputa -- e ruido. Quem narra perde a historia ("este vinha mais
        // rapido ha tres voltas"), que e justamente o que da enredo a prova.
        //
        // A CONTA E A VAGA: espalha o grid do mais lento ao mais rapido de
        // forma estavel, e o mesmo entry_list da sempre a mesma ordem de
        // forca. Sem o sorteio, duas subidas do servidor produzem a mesma
        // corrida -- o que e exatamente o que se quer para comparar um ajuste.
        if (_configuration.Extra.AiParams.ModoCorrida)
        {
            var vagas = Math.Max(1, _entryCarManager.EntryCars.Length - 1);
            var fracao = EntryCar.SessionId / (float)vagas;
            var espalhamento = _configuration.Extra.AiParams.CorridaVariacaoDeRitmo;
            InitialMaxSpeed = _configuration.Extra.AiParams.MaxSpeedMs
                              * (1f - espalhamento / 2f + fracao * espalhamento);
        }
        else
        {
            InitialMaxSpeed = _configuration.Extra.AiParams.MaxSpeedMs + fastLaneOffset - (variation / 2) + (float)Random.Shared.NextDouble() * variation;
        }
        CurrentSpeed = InitialMaxSpeed;
        TargetSpeed = InitialMaxSpeed;
        MaxSpeed = InitialMaxSpeed;
    }

    private void SetRandomColor()
    {
        Color = CarColors[Random.Shared.Next(CarColors.Count)];
    }

    public void Teleport(int pointId)
    {
        _junctionEvaluator.Clear();
        CurrentSplinePointId = pointId;
        if (!_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextPointId))
            throw new InvalidOperationException($"Cannot get next spline point for {CurrentSplinePointId}");
        _currentVecLength = (_spline.Points[nextPointId].Position - _spline.Points[CurrentSplinePointId].Position).Length();
        _currentVecProgress = 0;
            
        CalculateTangents();
        
        SetRandomSpeed();
        SetRandomColor();

        var minDist = _configuration.Extra.AiParams.MinAiSafetyDistanceSquared;
        var maxDist = _configuration.Extra.AiParams.MaxAiSafetyDistanceSquared;
        if (_configuration.Extra.AiParams.LaneCountSpecificOverrides.TryGetValue(_spline.GetLanes(CurrentSplinePointId).Length, out var overrides))
        {
            minDist = overrides.MinAiSafetyDistanceSquared;
            maxDist = overrides.MaxAiSafetyDistanceSquared;
        }
        
        if (EntryCar.MinAiSafetyDistanceMetersSquared.HasValue)
            minDist = EntryCar.MinAiSafetyDistanceMetersSquared.Value;
        if (EntryCar.MaxAiSafetyDistanceMetersSquared.HasValue)
            maxDist = EntryCar.MaxAiSafetyDistanceMetersSquared.Value;

        SpawnProtectionEnds = _sessionManager.ServerTimeMilliseconds + Random.Shared.Next(EntryCar.AiMinSpawnProtectionTimeMilliseconds, EntryCar.AiMaxSpawnProtectionTimeMilliseconds);
        SafetyDistanceSquared = Random.Shared.Next((int)Math.Round(minDist * (1.0f / _configuration.Extra.AiParams.TrafficDensity)),
            (int)Math.Round(maxDist * (1.0f / _configuration.Extra.AiParams.TrafficDensity)));
        _stoppedForCollisionUntil = 0;
        _ignoreObstaclesUntil = 0;
        _obstacleHonkEnd = 0;
        _obstacleHonkStart = 0;
        _indicator = 0;
        _randomTwilight = Random.Shared.NextSingle(0, 12) * Math.PI / 180.0;
        _nextJunctionId = -1;
        _junctionPassed = false;
        _endIndicatorDistance = 0;
        _lastTick = _sessionManager.ServerTimeMilliseconds;
        _minObstacleDistance = Random.Shared.Next(8, 13);
        SpawnCounter++;
        Initialized = true;
        Update();
    }

    private void CalculateTangents()
    {
        if (!_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextPointId))
            throw new InvalidOperationException("Cannot get next spline point");

        var points = _spline.Points;
        
        if (_junctionEvaluator.TryPrevious(CurrentSplinePointId, out var previousPointId))
        {
            _startTangent = (points[nextPointId].Position - points[previousPointId].Position) * 0.5f;
        }
        else
        {
            _startTangent = (points[nextPointId].Position - points[CurrentSplinePointId].Position) * 0.5f;
        }

        if (_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextNextPointId, 2))
        {
            _endTangent = (points[nextNextPointId].Position - points[CurrentSplinePointId].Position) * 0.5f;
        }
        else
        {
            _endTangent = (points[nextPointId].Position - points[CurrentSplinePointId].Position) * 0.5f;
        }
    }

    private bool Move(float progress)
    {
        var points = _spline.Points;
        var junctions = _spline.Junctions;
        
        bool recalculateTangents = false;
        while (progress > _currentVecLength)
        {
            progress -= _currentVecLength;
                
            if (!_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextPointId)
                || !_junctionEvaluator.TryNext(nextPointId, out var nextNextPointId))
            {
                return false;
            }

            // ───────────── CRUZOU A LINHA DE CHEGADA? ─────────────
            //
            // O indice do tracado cresce no sentido da pista e da a volta no
            // fim, entao o unico ponto em que ele DIMINUI e a passagem pela
            // linha. Nao ha marcador de chegada no `fast_lane.ai` -- o ponto 0
            // e onde o autor comecou a gravar, que e a propria linha.
            //
            // SO EM CORRIDA e so com um salto para tras PEQUENO: um teleporte
            // (largada, reposicionamento) tambem anda para tras no indice, e
            // sem este limite ele contaria uma volta que ninguem deu.
            if (_configuration.Extra.AiParams.ModoCorrida
                && nextPointId < CurrentSplinePointId
                && CurrentSplinePointId > points.Length - 50)
            {
                var agora = _sessionManager.ServerTimeMilliseconds;
                if (_inicioDaVoltaMs > 0)
                {
                    EntryCar.VoltaDaIa((uint)(agora - _inicioDaVoltaMs));
                }
                _inicioDaVoltaMs = agora;
            }

            CurrentSplinePointId = nextPointId;
            _currentVecLength = (points[nextNextPointId].Position - points[CurrentSplinePointId].Position).Length();
            recalculateTangents = true;

            if (_junctionPassed)
            {
                _endIndicatorDistance -= _currentVecLength;

                if (_endIndicatorDistance < 0)
                {
                    _indicator = 0;
                    _junctionPassed = false;
                    _endIndicatorDistance = 0;
                }
            }
                
            if (_nextJunctionId >= 0 && points[CurrentSplinePointId].JunctionEndId == _nextJunctionId)
            {
                _junctionPassed = true;
                _endIndicatorDistance = junctions[_nextJunctionId].IndicateDistancePost;
                _nextJunctionId = -1;
            }
        }

        if (recalculateTangents)
        {
            CalculateTangents();
        }

        _currentVecProgress = progress;

        return true;
    }

    public bool CanSpawn(int spawnPointId, AiState? previousAi, AiState? nextAi)
    {
        var ops = _spline.Operations;
        ref readonly var spawnPoint = ref ops.Points[spawnPointId];

        if (!IsAllowedLaneCount(spawnPointId))
            return false;
        if (!IsAllowedLane(in spawnPoint))
            return false;
        if (!IsKeepingSafetyDistances(in spawnPoint, previousAi, nextAi))
            return false;

        return EntryCar.CanSpawnAiState(spawnPoint.Position, this);
    }

    private bool IsKeepingSafetyDistances(in SplinePoint spawnPoint, AiState? previousAi, AiState? nextAi)
    {
        if (previousAi != null)
        {
            var distance = MathF.Max(0, Vector3.Distance(spawnPoint.Position, previousAi.Status.Position)
                           - previousAi.EntryCar.VehicleLengthPreMeters
                           - EntryCar.VehicleLengthPostMeters);

            var distanceSquared = distance * distance;
            if (distanceSquared < previousAi.SafetyDistanceSquared || distanceSquared < SafetyDistanceSquared)
                return false;
        }
        
        if (nextAi != null)
        {
            var distance = MathF.Max(0, Vector3.Distance(spawnPoint.Position, nextAi.Status.Position)
                                        - nextAi.EntryCar.VehicleLengthPostMeters
                                        - EntryCar.VehicleLengthPreMeters);

            var distanceSquared = distance * distance;
            if (distanceSquared < nextAi.SafetyDistanceSquared || distanceSquared < SafetyDistanceSquared)
                return false;
        }

        return true;
    }

    private bool IsAllowedLaneCount(int spawnPointId)
    {
        var laneCount = _spline.GetLanes(spawnPointId).Length;
        if (EntryCar.MinLaneCount.HasValue && laneCount < EntryCar.MinLaneCount.Value)
            return false;
        if (EntryCar.MaxLaneCount.HasValue && laneCount > EntryCar.MaxLaneCount.Value)
            return false;
        
        return true;
    }

    private bool IsAllowedLane(in SplinePoint spawnPoint)
    {
        var isAllowedLane = true;
        if (EntryCar.AiAllowedLanes != null)
        {
            isAllowedLane = (EntryCar.AiAllowedLanes.Contains(LaneSpawnBehavior.Middle) && spawnPoint.LeftId >= 0 && spawnPoint.RightId >= 0)
                            || (EntryCar.AiAllowedLanes.Contains(LaneSpawnBehavior.Left) && spawnPoint.LeftId < 0)
                            || (EntryCar.AiAllowedLanes.Contains(LaneSpawnBehavior.Right) && spawnPoint.RightId < 0);
        }

        return isAllowedLane;
    }

    /// <summary>
    /// Decide para que lado da pista este carro anda, e leva ele ate la.
    /// </summary>
    /// <remarks>
    /// <para>
    /// A REGRA E CURTA: atras de alguem mais lento, sai para o lado onde ha
    /// mais asfalto; livre, volta para a linha de corrida. E o bastante para
    /// uma ultrapassagem acontecer, porque quem vem mais rapido encontra o
    /// espaco ao lado em vez da traseira do outro.
    /// </para>
    /// <para>
    /// VOLTA PARA A LINHA QUANDO LIVRE, e isso importa tanto quanto sair dela:
    /// a linha de corrida e a rapida, e um carro que ficasse para sempre no
    /// lado seria eternamente lento -- a corrida congelaria na ordem da
    /// largada.
    /// </para>
    /// <para>
    /// O MOVIMENTO E GRADUAL, limitado em metros por segundo. Saltar de lado
    /// num quadro poria o carro dentro do outro, e para quem assiste seria um
    /// teleporte -- nao uma ultrapassagem.
    /// </para>
    /// </remarks>
    private void AtualizarDesvio(float dt)
    {
        if (!_configuration.Extra.AiParams.ModoCorrida) return;

        ref readonly var ponto = ref _spline.Points[CurrentSplinePointId];

        // SO QUEM ESTA ALCANCANDO SAI DA LINHA.
        //
        // Antes bastava ter alguem perto, e com o grid a 12 m TODOS estavam
        // perto de alguem o tempo inteiro: os quinze andavam permanentemente
        // fora da linha, e nunca voltavam. Quem nao tem o que fazer ali
        // atrapalha, perde tempo e termina na grama.
        //
        // A margem de velocidade evita o vaivem de quem anda no mesmo ritmo do
        // carro da frente: sem ela, meio quilometro por hora de diferenca
        // bastaria para o carro ficar trocando de linha a prova inteira.
        float alvo = 0;
        var alcancando = ClosestAiObstacleDistance >= 0
                         && ClosestAiObstacleDistance < DistanciaParaTentarPassar
                         && CurrentSpeed > _velocidadeDoDaFrente + MargemParaTentarMs;
        if (alcancando)
        {
            // O LADO COM MAIS ESPACO, limitado a UMA LARGURA DE CARRO.
            //
            // `SideLeft`/`SideRight` NAO sao a borda do asfalto -- medido em
            // Guapore, a mediana e 6,5 m e o maximo 45,4 m, porque eles contam
            // escape e patio. Aqui eles servem so para ESCOLHER O LADO e para
            // encolher o desvio onde a pista aperta; o tamanho vem de um
            // numero em metros.
            var espaco = Math.Max(ponto.SideLeft, ponto.SideRight);
            var desvio = Math.Min(_configuration.Extra.AiParams.CorridaDesvioMetros,
                                  Math.Max(0f, espaco - FolgaAteABordaMetros));
            alvo = ponto.SideLeft > ponto.SideRight ? -desvio : desvio;
        }

        var passo = DesvioMetrosPorSegundo * (dt / 1000.0f);
        var falta = alvo - _desvioLateral;
        _desvioLateral += Math.Clamp(falta, -passo, passo);
    }

    /// <summary>A que distancia do carro da frente se tenta a ultrapassagem.</summary>
    private const float DistanciaParaTentarPassar = 30f;

    /// <summary>Quao rapido o carro troca de linha, em metros por segundo.</summary>
    private const float DesvioMetrosPorSegundo = 2.5f;

    /// <summary>
    /// Quanto mais rapido e preciso ser para tentar passar, em m/s.
    /// </summary>
    /// <remarks>
    /// Cerca de 5 km/h. Abaixo disso nao ha ultrapassagem a fazer, e a
    /// tentativa so custaria tempo aos dois.
    /// </remarks>
    private const float MargemParaTentarMs = 1.5f;

    /// <summary>
    /// Folga mantida ate o limite que o tracado reporta, em metros.
    /// </summary>
    /// <remarks>
    /// Onde a pista aperta, o desvio encolhe junto. Nao e a borda do asfalto
    /// -- ver a nota em `AtualizarDesvio` --, mas serve de teto onde o valor e
    /// pequeno, que e justamente onde nao ha para onde ir.
    /// </remarks>
    private const float FolgaAteABordaMetros = 1.0f;

    /// <summary>
    /// A velocidade do carro a frente, guardada na busca por obstaculo.
    /// </summary>
    /// <remarks>
    /// `float.MaxValue` quando nao ha ninguem: assim a comparacao "sou mais
    /// rapido que ele" da falso sozinha, sem precisar de um teste a parte.
    /// </remarks>
    private float _velocidadeDoDaFrente = float.MaxValue;

    /// <summary>
    /// Carro ao lado nao e obstaculo.
    /// </summary>
    /// <remarks>
    /// SEM ISTO O DESVIO NAO SERVE DE NADA: a busca por obstaculo olha so para
    /// a FRENTE no tracado, sem saber de posicao lateral -- entao o
    /// ultrapassante, ja emparelhado, continuaria freando atras de quem esta ao
    /// seu lado, e a ultrapassagem nunca se completaria.
    ///
    /// A folga e de uma largura de carro mais uma margem: encostado demais, o
    /// toque seria o resultado.
    /// </remarks>
    private bool EstaAoLado(AiState outro) =>
        _configuration.Extra.AiParams.ModoCorrida
        && Math.Abs(_desvioLateral - outro._desvioLateral) > LarguraLivreMetros;

    private const float LarguraLivreMetros = 2.2f;

    /// <summary>
    /// A marcha e o conta-giros deste carro, a partir da velocidade.
    /// </summary>
    /// <remarks>
    /// <para>
    /// POR QUE E CALCULADO: a IA nao roda fisica -- nao ha motor nem caixa. O
    /// que o cliente ouve vem do numero que o servidor poe no pacote, e o
    /// upstream punha uma rampa linear entre 800 e 3000 rpm com a marcha presa
    /// na segunda. Resultado: um zumbido continuo ao lado de um carro de
    /// verdade que sobe e cai de giro a cada troca.
    /// </para>
    /// <para>
    /// A CONTA E A DE SEMPRE: a roda gira `velocidade / circunferencia` voltas
    /// por segundo, e o motor gira isso vezes a relacao da marcha vezes a
    /// final. Com as relacoes reais do carro, as trocas caem onde caem de
    /// verdade -- nao num ponto escolhido a dedo.
    /// </para>
    /// <para>
    /// SOBE DE MARCHA AO BATER NO LIMITADOR, que e o que um piloto faz. Anda-se
    /// da primeira para cima e fica-se na primeira que segura o giro abaixo do
    /// corte; nao achando nenhuma, usa-se a mais longa.
    /// </para>
    /// <para>
    /// SEM RELACOES CONFIGURADAS, nada muda: volta a rampa antiga. Inventar
    /// uma caixa daria trocas nos lugares errados, que soa pior do que nao
    /// trocar.
    /// </para>
    /// </remarks>
    private (byte Marcha, ushort Giro) MarchaEGiro()
    {
        var relacoes = EntryCar.AiGearRatios;
        if (relacoes == null || relacoes.Length == 0 || EntryCar.TyreDiameterMeters <= 0)
        {
            var rampa = MathUtils.Lerp(EntryCar.AiIdleEngineRpm, EntryCar.AiMaxEngineRpm,
                CurrentSpeed / _configuration.Extra.AiParams.MaxSpeedMs);
            return (GearNeutraMaisUm, (ushort)rampa);
        }

        var voltasDaRodaPorMinuto = CurrentSpeed / (MathF.PI * EntryCar.TyreDiameterMeters) * 60f;
        float GiroEm(int marcha) => voltasDaRodaPorMinuto * relacoes[marcha] * EntryCar.AiFinalRatio;

        _marchaAtual = Math.Clamp(_marchaAtual, 0, relacoes.Length - 1);

        var agora = _sessionManager.ServerTimeMilliseconds;
        if (agora - _ultimaTrocaMs >= TempoMinimoEntreTrocasMs)
        {
            var subir = EntryCar.AiMaxEngineRpm * FracaoParaSubir;
            var descer = EntryCar.AiMaxEngineRpm * FracaoParaDescer;
            var giroAgora = GiroEm(_marchaAtual);

            if (giroAgora > subir && _marchaAtual < relacoes.Length - 1)
            {
                _marchaAtual++;
                _ultimaTrocaMs = agora;
            }
            else if (giroAgora < descer && _marchaAtual > 0)
            {
                _marchaAtual--;
                _ultimaTrocaMs = agora;
            }
        }

        // PARADO NAO E MOTOR DESLIGADO: abaixo da marcha lenta o giro fica na
        // marcha lenta, senao o carro na largada apareceria com o motor morto.
        var rpm = Math.Max(GiroEm(_marchaAtual), EntryCar.AiIdleEngineRpm);

        // `Gear` do AC conta a partir da re: 0 e a re, 1 e o ponto morto, e a
        // primeira marcha e 2. O upstream mandava 2 fixo -- que e exatamente a
        // primeira, para sempre.
        return ((byte)(_marchaAtual + GearNeutraMaisUm), (ushort)Math.Min(rpm, EntryCar.AiMaxEngineRpm));
    }

    /// <summary>A marcha engatada agora, 0 para a primeira.</summary>
    /// <remarks>
    /// E ESTADO, E NAO CONTA. Antes a marcha saia direto da velocidade, a cada
    /// quadro: perto de um ponto de troca, a oscilacao normal de quem freia e
    /// acelera fazia o carro subir e descer sem parar -- dezenas de trocas numa
    /// curva so. Guardar a marcha e o que permite exigir uma condicao para
    /// SAIR dela.
    /// </remarks>
    private int _marchaAtual;

    private long _ultimaTrocaMs;

    /// <summary>Sobe de marcha a esta fracao do limitador.</summary>
    private const float FracaoParaSubir = 0.97f;

    /// <summary>
    /// Desce de marcha so abaixo desta fracao do limitador.
    /// </summary>
    /// <remarks>
    /// MUITO MAIS BAIXO QUE O DE SUBIR, e e isto que impede o vaivem: as
    /// relacoes do Clio sao ~19% de uma para a outra, entao reduzir a 55% do
    /// corte leva o giro para ~65% -- longe do ponto de subir. Com os dois
    /// limites proximos, o carro trocaria de volta no quadro seguinte.
    /// </remarks>
    private const float FracaoParaDescer = 0.55f;

    /// <summary>
    /// Tempo minimo entre duas trocas.
    /// </summary>
    /// <remarks>
    /// Mesmo com histerese, uma freada forte atravessa varias marchas de uma
    /// vez. Escalonar no tempo faz isso soar como um piloto reduzindo, e nao
    /// como a caixa inteira caindo num quadro.
    /// </remarks>
    private const long TempoMinimoEntreTrocasMs = 250;

    /// <summary>A primeira marcha no jogo. 0 e a re e 1 e o ponto morto.</summary>
    private const byte GearNeutraMaisUm = 2;

    private (AiState? ClosestAiState, float ClosestAiStateDistance, float MaxSpeed) SplineLookahead()
    {
        var points = _spline.Points;
        var junctions = _spline.Junctions;
        
        float maxBrakingDistance = PhysicsUtils.CalculateBrakingDistance(CurrentSpeed, EntryCar.AiDeceleration) * 2 + 20;
        AiState? closestAiState = null;
        float closestAiStateDistance = float.MaxValue;
        bool junctionFound = false;
        float distanceTravelled = 0;
        var pointId = CurrentSplinePointId;
        ref readonly var point = ref points[pointId]; 
        float maxSpeed = float.MaxValue;
        float currentSpeedSquared = CurrentSpeed * CurrentSpeed;
        while (distanceTravelled < maxBrakingDistance)
        {
            distanceTravelled += point.Length;
            pointId = _junctionEvaluator.Next(pointId);
            if (pointId < 0)
                break;

            point = ref points[pointId];

            if (!junctionFound && point.JunctionStartId >= 0 && distanceTravelled < junctions[point.JunctionStartId].IndicateDistancePre)
            {
                ref readonly var jct = ref junctions[point.JunctionStartId];
                
                var indicator = _junctionEvaluator.WillTakeJunction(point.JunctionStartId) ? jct.IndicateWhenTaken : jct.IndicateWhenNotTaken;
                if (indicator != 0)
                {
                    _indicator = indicator;
                    _nextJunctionId = point.JunctionStartId;
                    junctionFound = true;
                }
            }

            if (closestAiState == null)
            {
                var slowest = _spline.SlowestAiStates[pointId];

                if (slowest != null && EstaAoLado(slowest))
                {
                    // Emparelhado, nao atrapalha: segue procurando mais a frente.
                    slowest = null;
                }

                if (slowest != null)
                {
                    closestAiState = slowest;
                    closestAiStateDistance = MathF.Max(0, Vector3.Distance(Status.Position, closestAiState.Status.Position)
                                                          - EntryCar.VehicleLengthPreMeters
                                                          - closestAiState.EntryCar.VehicleLengthPostMeters);
                }
            }

            float maxCorneringSpeedSquared = PhysicsUtils.CalculateMaxCorneringSpeedSquared(point.Radius, EntryCar.AiCorneringSpeedFactor);
            if (maxCorneringSpeedSquared < currentSpeedSquared)
            {
                float maxCorneringSpeed = MathF.Sqrt(maxCorneringSpeedSquared);
                float brakingDistance = PhysicsUtils.CalculateBrakingDistance(CurrentSpeed - maxCorneringSpeed,
                                            EntryCar.AiDeceleration * EntryCar.AiCorneringBrakeForceFactor)
                                        * EntryCar.AiCorneringBrakeDistanceFactor;

                if (brakingDistance > distanceTravelled)
                {
                    maxSpeed = Math.Min(maxCorneringSpeed, maxSpeed);
                }
            }
        }

        return (closestAiState, closestAiStateDistance, maxSpeed);
    }

    private bool ShouldIgnorePlayerObstacles()
    {
        if (_configuration.Extra.AiParams.IgnorePlayerObstacleSpheres != null)
        {
            foreach (var sphere in _configuration.Extra.AiParams.IgnorePlayerObstacleSpheres)
            {
                if (Vector3.DistanceSquared(Status.Position, sphere.Center) < sphere.RadiusMeters * sphere.RadiusMeters)
                {
                    return true;
                }
            }
        }

        return false;
    }

    private (EntryCar? entryCar, float distance) FindClosestPlayerObstacle()
    {
        if (!ShouldIgnorePlayerObstacles())
        {
            EntryCar? closestCar = null;
            float minDistance = float.MaxValue;
            for (var i = 0; i < _entryCarManager.EntryCars.Length; i++)
            {
                var playerCar = _entryCarManager.EntryCars[i];
                if (playerCar.Client?.HasSentFirstUpdate == true)
                {
                    float distance = Vector3.DistanceSquared(playerCar.Status.Position, Status.Position);

                    if (distance < minDistance
                        && Math.Abs(playerCar.Status.Position.Y - Status.Position.Y) < 1.5
                        && GetAngleToCar(playerCar.Status) is > 166 and < 194)
                    {
                        minDistance = distance;
                        closestCar = playerCar;
                    }
                }
            }

            if (closestCar != null)
            {
                return (closestCar, MathF.Sqrt(minDistance));
            }
        }

        return (null, float.MaxValue);
    }

    public void DetectObstacles()
    {
        if (!Initialized) return;
            
        if (_sessionManager.ServerTimeMilliseconds < _ignoreObstaclesUntil)
        {
            SetTargetSpeed(MaxSpeed);
            return;
        }

        if (_sessionManager.ServerTimeMilliseconds < _stoppedForCollisionUntil)
        {
            SetTargetSpeed(0);
            return;
        }
            
        float targetSpeed = InitialMaxSpeed;
        float maxSpeed = InitialMaxSpeed;
        bool hasObstacle = false;

        var splineLookahead = SplineLookahead();
        var playerObstacle = FindClosestPlayerObstacle();

        ClosestAiObstacleDistance = splineLookahead.ClosestAiState != null ? splineLookahead.ClosestAiStateDistance : -1;
        _velocidadeDoDaFrente = splineLookahead.ClosestAiState?.CurrentSpeed ?? float.MaxValue;

        if (playerObstacle.distance < _minObstacleDistance || splineLookahead.ClosestAiStateDistance < _minObstacleDistance)
        {
            targetSpeed = 0;
            hasObstacle = true;
        }
        else if (playerObstacle.distance < splineLookahead.ClosestAiStateDistance && playerObstacle.entryCar != null)
        {
            float playerSpeed = playerObstacle.entryCar.Status.Velocity.Length();

            if (playerSpeed < 0.1f)
            {
                playerSpeed = 0;
            }

            if ((playerSpeed < CurrentSpeed || playerSpeed == 0)
                && playerObstacle.distance < PhysicsUtils.CalculateBrakingDistance(CurrentSpeed - playerSpeed, EntryCar.AiDeceleration) * 2 + 20)
            {
                targetSpeed = Math.Max(WalkingSpeed, playerSpeed);
                hasObstacle = true;
            }
        }
        else if (splineLookahead.ClosestAiState != null)
        {
            float closestTargetSpeed = Math.Min(splineLookahead.ClosestAiState.CurrentSpeed, splineLookahead.ClosestAiState.TargetSpeed);
            if ((closestTargetSpeed < CurrentSpeed || splineLookahead.ClosestAiState.CurrentSpeed == 0)
                && splineLookahead.ClosestAiStateDistance < PhysicsUtils.CalculateBrakingDistance(CurrentSpeed - closestTargetSpeed, EntryCar.AiDeceleration) * 2 + 20)
            {
                targetSpeed = Math.Max(WalkingSpeed, closestTargetSpeed);
                hasObstacle = true;
            }
        }

        targetSpeed = Math.Min(splineLookahead.MaxSpeed, targetSpeed);

        if (CurrentSpeed == 0 && !_stoppedForObstacle)
        {
            _stoppedForObstacle = true;
            _stoppedForObstacleSince = _sessionManager.ServerTimeMilliseconds;
            _obstacleHonkStart = _stoppedForObstacleSince + Random.Shared.Next(3000, 7000);
            _obstacleHonkEnd = _obstacleHonkStart + Random.Shared.Next(500, 1500);
            Log.Verbose("AI {SessionId} stopped for obstacle", EntryCar.SessionId);
        }
        else if (CurrentSpeed > 0 && _stoppedForObstacle)
        {
            _stoppedForObstacle = false;
            Log.Verbose("AI {SessionId} no longer stopped for obstacle", EntryCar.SessionId);
        }
        else if (_stoppedForObstacle && _sessionManager.ServerTimeMilliseconds - _stoppedForObstacleSince > _configuration.Extra.AiParams.IgnoreObstaclesAfterMilliseconds)
        {
            _ignoreObstaclesUntil = _sessionManager.ServerTimeMilliseconds + 10_000;
            Log.Verbose("AI {SessionId} ignoring obstacles until {IgnoreObstaclesUntil}", EntryCar.SessionId, _ignoreObstaclesUntil);
        }

        float deceleration = EntryCar.AiDeceleration;
        if (!hasObstacle)
        {
            deceleration *= EntryCar.AiCorneringBrakeForceFactor;
        }
        
        MaxSpeed = maxSpeed;
        SetTargetSpeed(targetSpeed, deceleration, EntryCar.AiAcceleration);
    }

    public void StopForCollision()
    {
        if (!ShouldIgnorePlayerObstacles())
        {
            _stoppedForCollisionUntil = _sessionManager.ServerTimeMilliseconds + Random.Shared.Next(EntryCar.AiMinCollisionStopTimeMilliseconds, EntryCar.AiMaxCollisionStopTimeMilliseconds);
        }
    }

    /// <returns>0 is the rear <br/> Angle is counterclockwise</returns>
    public float GetAngleToCar(CarStatus car)
    {
        float challengedAngle = (float) (Math.Atan2(Status.Position.X - car.Position.X, Status.Position.Z - car.Position.Z) * 180 / Math.PI);
        if (challengedAngle < 0)
            challengedAngle += 360;
        float challengedRot = Status.GetRotationAngle();

        challengedAngle += challengedRot;
        challengedAngle %= 360;

        return challengedAngle;
    }

    private void SetTargetSpeed(float speed, float deceleration, float acceleration)
    {
        TargetSpeed = speed;
        if (speed < CurrentSpeed)
        {
            Acceleration = -deceleration;
        }
        else if (speed > CurrentSpeed)
        {
            Acceleration = acceleration;
        }
        else
        {
            Acceleration = 0;
        }
    }

    private void SetTargetSpeed(float speed)
    {
        SetTargetSpeed(speed, EntryCar.AiDeceleration, EntryCar.AiAcceleration);
    }

    public void Update()
    {
        if (!Initialized)
            return;

        var ops = _spline.Operations;

        long currentTime = _sessionManager.ServerTimeMilliseconds;
        long dt = currentTime - _lastTick;
        _lastTick = currentTime;

        if (Acceleration != 0)
        {
            CurrentSpeed += Acceleration * (dt / 1000.0f);
                
            if ((Acceleration < 0 && CurrentSpeed < TargetSpeed) || (Acceleration > 0 && CurrentSpeed > TargetSpeed))
            {
                CurrentSpeed = TargetSpeed;
                Acceleration = 0;
            }
        }

        AtualizarDesvio(dt);

        float moveMeters = (dt / 1000.0f) * CurrentSpeed;
        if (!Move(_currentVecProgress + moveMeters) || !_junctionEvaluator.TryNext(CurrentSplinePointId, out var nextPoint))
        {
            Log.Debug("Car {SessionId} reached spline end, despawning", EntryCar.SessionId);
            Despawn();
            return;
        }

        CatmullRom.CatmullRomPoint smoothPos = CatmullRom.Evaluate(ops.Points[CurrentSplinePointId].Position, 
            ops.Points[nextPoint].Position, 
            _startTangent, 
            _endTangent, 
            _currentVecProgress / _currentVecLength);
            
        Vector3 rotation = new Vector3
        {
            X = MathF.Atan2(smoothPos.Tangent.Z, smoothPos.Tangent.X) - MathF.PI / 2,
            Y = (MathF.Atan2(new Vector2(smoothPos.Tangent.Z, smoothPos.Tangent.X).Length(), smoothPos.Tangent.Y) - MathF.PI / 2) * -1f,
            Z = ops.GetCamber(CurrentSplinePointId, _currentVecProgress / _currentVecLength)
        };

        float tyreAngularSpeed = GetTyreAngularSpeed(CurrentSpeed, EntryCar.TyreDiameterMeters);
        byte encodedTyreAngularSpeed =  (byte) (Math.Clamp(MathF.Round(MathF.Log10(tyreAngularSpeed + 1.0f) * 20.0f) * Math.Sign(tyreAngularSpeed), -100.0f, 154.0f) + 100.0f);

        // SAI DA LINHA DE CORRIDA, dentro do asfalto que existe naquele ponto.
        //
        // A perpendicular e no plano do chao: girar o vetor de avanco 90 graus
        // em XZ. Usar o produto vetorial com o vertical daria o mesmo, e esta
        // forma evita normalizar duas vezes a cada quadro, sessenta vezes por
        // segundo e por carro.
        var posicao = smoothPos.Position;
        if (_desvioLateral != 0)
        {
            var frente = smoothPos.Tangent;
            var comprimento = new Vector2(frente.X, frente.Z).Length();
            if (comprimento > 0.0001f)
            {
                posicao += new Vector3(frente.Z, 0, -frente.X) / comprimento * _desvioLateral;
            }
        }

        Status.Timestamp = _sessionManager.ServerTimeMilliseconds;
        Status.Position = posicao with { Y = posicao.Y + EntryCar.AiSplineHeightOffsetMeters };
        Status.Rotation = rotation;
        Status.Velocity = smoothPos.Tangent * CurrentSpeed;
        Status.SteerAngle = 127;
        Status.WheelAngle = 127;
        Status.TyreAngularSpeed[0] = encodedTyreAngularSpeed;
        Status.TyreAngularSpeed[1] = encodedTyreAngularSpeed;
        Status.TyreAngularSpeed[2] = encodedTyreAngularSpeed;
        Status.TyreAngularSpeed[3] = encodedTyreAngularSpeed;
        var (marcha, giro) = MarchaEGiro();
        Status.Gear = marcha;
        Status.EngineRpm = giro;
        Status.StatusFlag = GetLights(_configuration.Extra.AiParams.EnableDaytimeLights, _weatherManager.CurrentSunPosition, _randomTwilight)
                            | (_sessionManager.ServerTimeMilliseconds < _stoppedForCollisionUntil || CurrentSpeed < 20 / 3.6f ? CarStatusFlags.HazardsOn : 0)
                            | (CurrentSpeed == 0 || Acceleration < 0 ? CarStatusFlags.BrakeLightsOn : 0)
                            | (_stoppedForObstacle && _sessionManager.ServerTimeMilliseconds > _obstacleHonkStart && _sessionManager.ServerTimeMilliseconds < _obstacleHonkEnd ? CarStatusFlags.Horn : 0)
                            | GetWiperSpeed(_weatherManager.CurrentWeather.RainIntensity)
                            | _indicator;
        // A MARCHA JA FOI DECIDIDA acima, em `MarchaEGiro`. Aqui havia um
        // `Status.Gear = 2` fixo que apagava a conta -- e, por ser a ULTIMA
        // linha do metodo, apagava calado.
    }
        
    private static float GetTyreAngularSpeed(float speed, float wheelDiameter)
    {
        return speed / (MathF.PI * wheelDiameter) * 6;
    }

    private static CarStatusFlags GetWiperSpeed(float rainIntensity)
    {
        return rainIntensity switch
        {
            < 0.05f => 0,
            < 0.25f => CarStatusFlags.WiperLevel1,
            < 0.5f => CarStatusFlags.WiperLevel2,
            _ => CarStatusFlags.WiperLevel3
        };
    }
    
    private static CarStatusFlags GetLights(bool daytimeLights, SunPosition? sunPosition, double twilight)
    {
        const CarStatusFlags lightFlags = CarStatusFlags.LightsOn | CarStatusFlags.HighBeamsOff;
        if (daytimeLights || sunPosition == null) return lightFlags;

        return sunPosition.Value.Altitude < twilight ? lightFlags : 0;
    }
}
