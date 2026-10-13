using System;
using System.Collections.Generic;
using System.Linq;
using System.Numerics;
using System.Reflection;
using System.Threading;
using System.Threading.Tasks;
using AssettoServer.Network.Http;
using AssettoServer.Network.Tcp;
using AssettoServer.Server.Ai.Splines;
using AssettoServer.Server.Configuration;
using AssettoServer.Shared.Network.Packets.Outgoing;
using AssettoServer.Utils;
using Microsoft.Extensions.Hosting;
using Prometheus;
using Serilog;

namespace AssettoServer.Server.Ai;

public class AiBehavior : BackgroundService
{
    private readonly ACServerConfiguration _configuration;
    private readonly SessionManager _sessionManager;
    private readonly EntryCarManager _entryCarManager;
    private readonly AiSpline _spline;
    private readonly HttpInfoCache _httpInfoCache;

    private readonly JunctionEvaluator _junctionEvaluator;

    private readonly Gauge _aiStateCountMetric = Metrics.CreateGauge("assettoserver_aistatecount", "Number of AI states");
    private readonly Summary _updateDurationTimer = Metrics.CreateSummary("assettoserver_aibehavior_update",
        "AiBehavior.Update Duration",
        MetricDefaults.DefaultQuantiles);
    private readonly Summary _obstacleDetectionDurationTimer = Metrics.CreateSummary("assettoserver_aibehavior_obstacledetection", 
        "AiBehavior.ObstacleDetection Duration", 
        MetricDefaults.DefaultQuantiles);

    public AiBehavior(SessionManager sessionManager,
        ACServerConfiguration configuration,
        EntryCarManager entryCarManager,
        CSPServerScriptProvider serverScriptProvider, 
        AiSpline spline,
        HttpInfoCache httpInfoCache)
    {
        _sessionManager = sessionManager;
        _configuration = configuration;
        _entryCarManager = entryCarManager;
        _spline = spline;
        _httpInfoCache = httpInfoCache;
        _junctionEvaluator = new JunctionEvaluator(spline, false);

        if (_configuration.Extra.AiParams.Debug)
        {
            serverScriptProvider.AddScript(Assembly.GetExecutingAssembly().GetManifestResourceStream("AssettoServer.Server.Ai.ai_debug.lua")!, "ai_debug.lua");
        }

        _entryCarManager.ClientConnected += (client, _) =>
        {
            client.ChecksumPassed += OnClientChecksumPassed;
            client.Collision += OnCollision;
        };
        _entryCarManager.ClientDisconnected += OnClientDisconnected;
        _configuration.Extra.AiParams.PropertyChanged += (_, _) => AdjustOverbooking();
        _sessionManager.SessionChanged += OnSessionChanged;
    }

    private static void OnCollision(ACTcpClient sender, CollisionEventArgs args)
    {
        if (args.TargetCar?.AiControlled == true)
        {
            var targetAiState = args.TargetCar.GetClosestAiState(sender.EntryCar.Status.Position);
            if (targetAiState.AiState != null && targetAiState.DistanceSquared < 25 * 25)
            {
                Task.Delay(Random.Shared.Next(100, 500)).ContinueWith(_ => targetAiState.AiState.StopForCollision());
            }
        }
    }

    private void OnClientChecksumPassed(ACTcpClient sender, EventArgs args)
    {
        sender.EntryCar.SetAiControl(false);
        AdjustOverbooking();
    }

    private async Task ObstacleDetectionAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(100));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                using var context = _obstacleDetectionDurationTimer.NewTimer();

                for (int i = 0; i < _entryCarManager.EntryCars.Length; i++)
                {
                    var entryCar = _entryCarManager.EntryCars[i];
                    if (entryCar.AiControlled)
                    {
                        entryCar.AiObstacleDetection();
                    }
                }

                if (_configuration.Extra.AiParams.Debug)
                {
                    SendDebugPackets();
                }
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in AI obstacle detection");
            }
        }
    }

    private void SendDebugPackets()
    {
        var sessionIds = new byte[_entryCarManager.EntryCars.Length];
        var currentSpeeds = new byte[_entryCarManager.EntryCars.Length];
        var targetSpeeds = new byte[_entryCarManager.EntryCars.Length];
        var maxSpeeds = new byte[_entryCarManager.EntryCars.Length];
        var closestAiObstacles = new short[_entryCarManager.EntryCars.Length];
        
        foreach (var player in _entryCarManager.ConnectedCars.Values)
        {
            if (player.Client?.HasSentFirstUpdate == false) continue;

            var count = 0;
            foreach (var car in _entryCarManager.EntryCars)
            {
                if (!car.AiControlled) continue;

                var (aiState, _) = car.GetClosestAiState(player.Status.Position);
                if (aiState == null) continue;

                sessionIds[count] = car.SessionId;
                currentSpeeds[count] = (byte)(aiState.CurrentSpeed * 3.6f);
                targetSpeeds[count] = (byte)(aiState.TargetSpeed * 3.6f);
                maxSpeeds[count] = (byte)(aiState.MaxSpeed * 3.6f);
                closestAiObstacles[count] = (short)aiState.ClosestAiObstacleDistance;
                count++;
            }

            for (int i = 0; i < count; i += AiDebugPacket.Length)
            {
                var packet = new AiDebugPacket();
                Array.Fill(packet.SessionIds, (byte)255);

                new ArraySegment<byte>(sessionIds, i, Math.Min(AiDebugPacket.Length, count - i)).CopyTo(packet.SessionIds);
                new ArraySegment<short>(closestAiObstacles, i, Math.Min(AiDebugPacket.Length, count - i)).CopyTo(packet.ClosestAiObstacles);
                new ArraySegment<byte>(currentSpeeds, i, Math.Min(AiDebugPacket.Length, count - i)).CopyTo(packet.CurrentSpeeds);
                new ArraySegment<byte>(maxSpeeds, i, Math.Min(AiDebugPacket.Length, count - i)).CopyTo(packet.MaxSpeeds);
                new ArraySegment<byte>(targetSpeeds, i, Math.Min(AiDebugPacket.Length, count - i)).CopyTo(packet.TargetSpeeds);

                player.Client?.SendPacket(packet);
            }
        }
    }
    
    private readonly List<EntryCar> _playerCars = new();
    private readonly List<AiState> _initializedAiStates = new();
    private readonly List<AiState> _uninitializedAiStates = new();
    private readonly List<Vector3> _playerOffsetPositions = new();
    private readonly List<KeyValuePair<AiState, float>> _aiMinDistanceToPlayer = new();
    private readonly List<KeyValuePair<EntryCar, float>> _playerMinDistanceToAi = new();
    private void Update()
    {
        using var context = _updateDurationTimer.NewTimer();

        _playerCars.Clear();
        _initializedAiStates.Clear();
        _uninitializedAiStates.Clear();
        _playerOffsetPositions.Clear();
        _aiMinDistanceToPlayer.Clear();
        _playerMinDistanceToAi.Clear();

        foreach (var entryCar in _entryCarManager.EntryCars)
        {
            var (currentSplinePointId, _) = _spline.WorldToSpline(entryCar.Status.Position);
            var drivingTheRightWay = Vector3.Dot(_spline.Operations.GetForwardVector(currentSplinePointId), entryCar.Status.Velocity) > 0;

            if (!entryCar.AiControlled
                && entryCar.Client?.HasSentFirstUpdate == true
                && _sessionManager.ServerTimeMilliseconds - entryCar.LastActiveTime < _configuration.Extra.AiParams.PlayerAfkTimeoutMilliseconds
                && (_configuration.Extra.AiParams.TwoWayTraffic || _configuration.Extra.AiParams.WrongWayTraffic || drivingTheRightWay))
            {
                _playerCars.Add(entryCar);
            }
            else if (entryCar.AiControlled)
            {
                entryCar.RemoveUnsafeStates();
                entryCar.GetInitializedStates(_initializedAiStates, _uninitializedAiStates);
            }
            
        }

        _aiStateCountMetric.Set(_initializedAiStates.Count);

        if (_sessionManager.CurrentSession.StartTimeMilliseconds > _sessionManager.ServerTimeMilliseconds)
            return;

        // ─────────────────── MODO CORRIDA PARA AQUI ───────────────────
        //
        // Tudo daqui para baixo e a mecanica de TRAFEGO: medir a distancia de
        // cada IA ao jogador mais proximo, aposentar quem ficou longe e
        // teleportar de volta a frente de alguem. Numa estrada isso e o que
        // mantem carros no campo de visao de quem dirige.
        //
        // NUMA CORRIDA ISSO DESTROI A PROVA. O carro que abre vantagem some, o
        // retardatario reaparece colado no lider, e a tabela de voltas vira
        // ficcao. Em corrida eles nascem uma vez, na largada, e ficam ate o
        // fim -- `LargarNoGrid` cuida disso.
        if (_configuration.Extra.AiParams.ModoCorrida)
        {
            GarantirGrid();
            return;
        }

        for (int i = 0; i < _initializedAiStates.Count; i++)
        {
            _aiMinDistanceToPlayer.Add(new KeyValuePair<AiState, float>(_initializedAiStates[i], float.MaxValue));
        }

        for (int i = 0; i < _playerCars.Count; i++)
        {
            _playerMinDistanceToAi.Add(new KeyValuePair<EntryCar, float>(_playerCars[i], float.MaxValue));
        }

        // Get minimum distance to a player for each AI
        // Get minimum distance to AI for each player
        for (int i = 0; i < _initializedAiStates.Count; i++)
        {
            for (int j = 0; j < _playerCars.Count; j++)
            {
                if (_playerOffsetPositions.Count <= j)
                {
                    var offsetPosition = _playerCars[j].Status.Position;
                    if (_playerCars[j].Status.Velocity != Vector3.Zero)
                    {
                        offsetPosition += Vector3.Normalize(_playerCars[j].Status.Velocity) * _configuration.Extra.AiParams.PlayerPositionOffsetMeters;
                    }

                    _playerOffsetPositions.Add(offsetPosition);
                }

                var distanceSquared = Vector3.DistanceSquared(_initializedAiStates[i].Status.Position, _playerOffsetPositions[j]);

                if (_aiMinDistanceToPlayer[i].Value > distanceSquared)
                {
                    _aiMinDistanceToPlayer[i] = new KeyValuePair<AiState, float>(_initializedAiStates[i], distanceSquared);
                }

                if (_playerMinDistanceToAi[j].Value > distanceSquared)
                {
                    _playerMinDistanceToAi[j] = new KeyValuePair<EntryCar, float>(_playerCars[j], distanceSquared);
                }
            }
        }
        
        // Order AI cars by their minimum distance to a player. Higher distance = higher chance for respawn
        _aiMinDistanceToPlayer.Sort((a, b) => b.Value.CompareTo(a.Value));

        foreach (var dist in _aiMinDistanceToPlayer)
        {
            if (dist.Value > _configuration.Extra.AiParams.PlayerRadiusSquared
                && _sessionManager.ServerTimeMilliseconds > dist.Key.SpawnProtectionEnds)
            {
                _uninitializedAiStates.Add(dist.Key);
            }
        }
        
        if (_initializedAiStates.Count > 0 && _playerCars.Count > 0)
        {
            _playerCars.Clear();
            // Order player cars by their minimum distance to an AI. Higher distance = higher chance for next AI spawn
            _playerMinDistanceToAi.Sort((a, b) => b.Value.CompareTo(a.Value));
            for (int i = 0; i < _playerMinDistanceToAi.Count; i++)
            {
                _playerCars.Add(_playerMinDistanceToAi[i].Key);
            }
        }

        while (_playerCars.Count > 0 && _uninitializedAiStates.Count > 0)
        {
            int spawnPointId = -1;
            while (spawnPointId < 0 && _playerCars.Count > 0)
            {
                var targetPlayerCar = _playerCars.ElementAt(GetRandomWeighted(_playerCars.Count));
                _playerCars.Remove(targetPlayerCar);

                spawnPointId = GetSpawnPoint(targetPlayerCar);
            }

            if (spawnPointId < 0 || !_junctionEvaluator.TryNext(spawnPointId, out _))
                continue;

            var previousAi = FindClosestAiState(spawnPointId, false);
            var nextAi = FindClosestAiState(spawnPointId, true);

            foreach (var targetAiState in _uninitializedAiStates)
            {
                if (!targetAiState.CanSpawn(spawnPointId, previousAi, nextAi))
                    continue;

                targetAiState.Teleport(spawnPointId);

                _uninitializedAiStates.Remove(targetAiState);
                break;
            }
        }
    }

    private AiState? FindClosestAiState(int pointId, bool forward)
    {
        var points = _spline.Points;
        float distanceTravelled = 0;
        float searchDistance = 50;
        ref readonly var point = ref points[pointId];
        
        AiState? closestAiState = null;
        while (distanceTravelled < searchDistance && closestAiState == null)
        {
            distanceTravelled += point.Length;
            // TODO reuse this junction evaluator for the newly spawned car
            pointId = forward ? _junctionEvaluator.Next(pointId) : _junctionEvaluator.Previous(pointId);
            if (pointId < 0)
                break;

            point = ref points[pointId];
            
            var slowest = _spline.SlowestAiStates[pointId];
            if (slowest != null)
            {
                closestAiState = slowest;
            }
        }

        return closestAiState;
    }

    private async Task UpdateAsync(CancellationToken stoppingToken)
    {
        using var timer = new PeriodicTimer(TimeSpan.FromMilliseconds(_configuration.Extra.AiParams.AiBehaviorUpdateIntervalMilliseconds));

        while (await timer.WaitForNextTickAsync(stoppingToken))
        {
            try
            {
                Update();
            }
            catch (Exception ex)
            {
                Log.Error(ex, "Error in AI update");
            }
        }
    }

    private void OnClientDisconnected(ACTcpClient sender, EventArgs args)
    {
        if (sender.EntryCar.AiMode != AiMode.None)
        {
            sender.EntryCar.SetAiControl(true);
            AdjustOverbooking();
        }
    }

    private void OnSessionChanged(SessionManager sender, SessionChangedEventArgs args)
    {
        for (var i = 0; i < _initializedAiStates.Count; i++)
        {
            _initializedAiStates[i].Despawn();
        }

        // Sessao nova, grid novo. O `GarantirGrid` do proximo tique recoloca
        // todo mundo; aqui so se apaga a marca de que ja houve largada.
        _largou = false;
    }

    private bool _largou;

    /// <summary>
    /// Poe cada IA na sua vaga de largada, uma unica vez por sessao.
    /// </summary>
    /// <remarks>
    /// <para>
    /// ESPACADAS PARA TRAS A PARTIR DA LINHA, e nao nas vagas de verdade: as
    /// posicoes do grid moram no arquivo da PISTA (os `AC_START_n` do modelo),
    /// que o servidor nao le -- ele so conhece o tracado da IA. Espacar pelo
    /// tracado da a mesma coisa que importa aqui: carros em fila, separados, no
    /// sentido certo, prontos para largar.
    /// </para>
    /// <para>
    /// A DIFERENCA que isso deixa e a largada parada em duas colunas, que nao
    /// existe. Para ensaio de transmissao e disputa em pista isso nao pesa; se
    /// um dia pesar, o caminho e o servidor ler os `AC_START_n`.
    /// </para>
    /// <para>
    /// UMA VEZ SO: chamado a cada tique do comportamento, ele precisa saber que
    /// ja largou -- senao teleportaria todo mundo de volta a largada duas vezes
    /// por segundo, e a corrida nunca sairia do lugar.
    /// </para>
    /// </remarks>
    private void GarantirGrid()
    {
        if (_largou) return;

        var espaco = _configuration.Extra.AiParams.CorridaEspacoNoGridMetros;
        var pontos = _spline.Points;
        if (pontos.Length == 0) return;

        // ANDA O TRACADO PARA TRAS SOMANDO DISTANCIA, em vez de dividir o
        // comprimento pelo numero de pontos: o passo do `fast_lane.ai` e
        // irregular -- ele e denso na curva e esparso na reta --, e a media
        // colocaria o grid comprimido justamente onde ele e desenhado, na
        // reta de largada.
        //
        // `PreviousId` quando existe; senao, o anterior pelo indice com a
        // volta no fim. Um tracado de circuito e fechado, entao a volta
        // sempre chega em asfalto.
        var ponto = 0;
        var vaga = 0;
        foreach (var entryCar in _entryCarManager.EntryCars)
        {
            if (!entryCar.AiControlled) continue;

            var andado = 0f;
            while (andado < espaco)
            {
                var anterior = pontos[ponto].PreviousId;
                ponto = anterior >= 0 ? anterior : (ponto - 1 + pontos.Length) % pontos.Length;
                andado += pontos[ponto].Length;

                // GUARDA CONTRA TRACADO QUEBRADO: comprimento zero em todos os
                // pontos faria este laco girar para sempre e levar o servidor
                // junto. Uma vaga mal posicionada e um defeito menor.
                if (andado <= 0f && ponto == 0) break;
            }

            entryCar.LargarEm(ponto);
            vaga++;
        }

        if (vaga > 0)
        {
            _largou = true;
            Log.Information("Modo corrida: {Quantas} IA largaram, a cada {Espaco} m", vaga, espaco);
        }
    }

    private int GetRandomWeighted(int max)
    {
        // Probabilities for max = 4
        // 0    4/10
        // 1    3/10
        // 2    2/10
        // 3    1/10
            
        int maxRand = max * (max + 1) / 2;
        int rand = Random.Shared.Next(maxRand);
        int target = 0;
        for (int i = max; i < maxRand; i += (i - 1))
        {
            if (rand < i) break;
            target++;
        }

        return target;
    }

    private bool IsPositionSafe(int pointId)
    {
        var ops = _spline.Operations;

        for (var i = 0; i < _entryCarManager.EntryCars.Length; i++)
        {
            var entryCar = _entryCarManager.EntryCars[i];
            if (entryCar.AiControlled && !entryCar.IsPositionSafe(pointId))
            {
                return false;
            }

            if (entryCar.Client?.HasSentFirstUpdate == true
                && Vector3.DistanceSquared(entryCar.Status.Position, ops.Points[pointId].Position) < _configuration.Extra.AiParams.SpawnSafetyDistanceToPlayerSquared)
            {
                return false;
            }
        }

        return true;
    }

    private int GetSpawnPoint(EntryCar playerCar)
    {
        var result = _spline.WorldToSpline(playerCar.Status.Position);
        var ops = _spline.Operations;


        if (result.PointId < 0 || ops.Points[result.PointId].NextId < 0) return -1;
        
        int direction = Vector3.Dot(ops.GetForwardVector(result.PointId), playerCar.Status.Velocity) > 0 ? 1 : -1;
        
        // Do not not spawn if a player is too far away from the AI spline, e.g. in pits or in a part of the map without traffic
        if (result.DistanceSquared > _configuration.Extra.AiParams.MaxPlayerDistanceToAiSplineSquared)
        {
            return -1;
        }
        
        int spawnDistance = Random.Shared.Next(_configuration.Extra.AiParams.MinSpawnDistancePoints, _configuration.Extra.AiParams.MaxSpawnDistancePoints);
        var spawnPointId = _junctionEvaluator.Traverse(result.PointId, spawnDistance * direction);

        if (spawnPointId >= 0)
        {
            spawnPointId = _spline.RandomLane(spawnPointId);
        }
        
        if (spawnPointId >= 0 && ops.Points[spawnPointId].NextId >= 0)
        {
            direction = Vector3.Dot(ops.GetForwardVector(spawnPointId), playerCar.Status.Velocity) > 0 ? 1 : -1;
        }

        while (spawnPointId >= 0 && !IsPositionSafe(spawnPointId))
        {
            spawnPointId = _junctionEvaluator.Traverse(spawnPointId, direction * 5);
        }
        
        if (spawnPointId >= 0)
        {
            spawnPointId = _spline.RandomLane(spawnPointId);
        }

        return spawnPointId;
    }

    private void AdjustOverbooking()
    {
        int playerCount = _entryCarManager.EntryCars.Count(car => car.Client != null && car.Client.IsConnected);
        var aiSlots = _entryCarManager.EntryCars.Where(car => car.Client == null && car.AiControlled).ToList(); // client null check is necessary here so that slots where someone is connecting don't count

        if (aiSlots.Count == 0)
        {
            Log.Debug("AI Slot overbooking update - no AI slots available");
            return;
        }

        // ─────────── EM CORRIDA, UM CARRO POR VAGA. SEMPRE. ───────────
        //
        // O calculo abaixo e de trafego: ele multiplica carros por jogador e
        // por densidade, e a mesma vaga do entry_list vira varios veiculos ao
        // mesmo tempo em pontos diferentes da estrada. Numa corrida isso seria
        // o mesmo piloto em dois lugares da pista, com uma volta so contando.
        //
        // E ELE RODA A CADA CONEXAO: sem este desvio, o primeiro piloto que
        // entrasse recalcularia a quantidade e desfaria o grid.
        if (_configuration.Extra.AiParams.ModoCorrida)
        {
            foreach (var slot in aiSlots)
            {
                slot.SetAiOverbooking(1);
            }
            return;
        }
            
        int targetAiCount = Math.Min(playerCount * Math.Min((int)Math.Round(_configuration.Extra.AiParams.AiPerPlayerTargetCount * _configuration.Extra.AiParams.TrafficDensity), aiSlots.Count), _configuration.Extra.AiParams.MaxAiTargetCount);

        int overbooking = targetAiCount / aiSlots.Count;
        int rest = targetAiCount % aiSlots.Count;
            
        Log.Debug("AI Slot overbooking update - No. players: {NumPlayers} - No. AI Slots: {NumAiSlots} - Target AI count: {TargetAiCount} - Overbooking: {Overbooking} - Rest: {Rest}", 
            playerCount, aiSlots.Count, targetAiCount, overbooking, rest);

        for (int i = 0; i < aiSlots.Count; i++)
        {
            aiSlots[i].SetAiOverbooking(i < rest ? overbooking + 1 : overbooking);
        }
    }

    private void SetHttpDetailsExtensions()
    {
        _httpInfoCache.Extensions.Add("aiTraffic", new Dictionary<string, List<byte>>
        {
            { "auto", _entryCarManager.EntryCars.Where(c => c.AiMode == AiMode.Auto).Select(c => c.SessionId).ToList() },
            { "fixed", _entryCarManager.EntryCars.Where(c => c.AiMode == AiMode.Fixed).Select(c => c.SessionId).ToList() }
        });
    }

    protected override Task ExecuteAsync(CancellationToken stoppingToken)
    {
        SetHttpDetailsExtensions();
        return Task.WhenAll(UpdateAsync(stoppingToken), ObstacleDetectionAsync(stoppingToken));
    }
}
