using System.Collections.Generic;
using System.Linq;
using AssettoServer.Server;
using AssettoServer.Shared.Model;
using Microsoft.AspNetCore.Cors;
using Microsoft.AspNetCore.Mvc;

namespace AssettoServer.Network.Http;

/// <summary>
/// O PLACAR AO VIVO, pela porta que todo mundo alcança.
///
/// <para>
/// POR QUE ESTA ROTA EXISTE. O servidor sempre soube posição, voltas, melhor
/// volta e setores — mas só contava isso pelo canal do plugin, que é UDP e vai
/// para UMA máquina, a que hospeda. Quem está em casa fala com o servidor pelo
/// HTTP, e por ali só havia <c>/INFO</c> (contagem) e <c>/JSON|0</c> e
/// <c>/api/details</c> (quem está conectado). Nenhuma das três carrega um
/// tempo. O app do piloto conseguia listar nomes e nada mais.
/// </para>
///
/// <para>
/// É UMA ROTA NOSSA, e por isso vive em arquivo separado do
/// <see cref="HttpController"/>: o que está lá é protocolo do Assetto Corsa,
/// que o Content Manager e o jogo consomem, e misturar as duas coisas faria a
/// próxima atualização do upstream brigar com a liga.
/// </para>
///
/// <para>
/// SÓ LEITURA E SEM SEGREDO: nome, equipe, país, carro, pintura e tempos — o
/// mesmo que qualquer um vê na tela do jogo ao entrar. Não vai GUID nem
/// endereço, que é o que distingue um piloto de outro fora da pista.
/// </para>
/// </summary>
[ApiController]
public class LigaController : ControllerBase
{
    private readonly SessionManager _sessionManager;
    private readonly EntryCarManager _entryCarManager;

    public LigaController(SessionManager sessionManager, EntryCarManager entryCarManager)
    {
        _sessionManager = sessionManager;
        _entryCarManager = entryCarManager;
    }

    [EnableCors("ServerQueryPolicy")]
    [HttpGet("/api/liga/aovivo")]
    public AoVivoResponse GetAoVivo()
    {
        var sessao = _sessionManager.CurrentSession;
        var resultados = sessao.Results;

        var carros = new List<AoVivoCar>();
        foreach (var ec in _entryCarManager.EntryCars)
        {
            // SÓ QUEM ESTÁ DENTRO. A entry list tem as 35 vagas, e as vazias
            // só empurrariam para fora da tela as que importam.
            if (ec.Client is not { } cliente) continue;

            // O resultado pode nao existir: entre o carro conectar e a sessao
            // montar a tabela ha uma janela, e nela o indexador estouraria.
            var r2 = resultados is not null && resultados.TryGetValue(ec.SessionId, out var achado)
                ? achado
                : null;

            carros.Add(new AoVivoCar
            {
                CarId = ec.SessionId,
                Piloto = cliente.Name ?? "",
                Equipe = cliente.Team ?? "",
                Pais = cliente.NationCode ?? "",
                Carro = ec.Model,
                Pintura = ec.Skin,
                Voltas = r2?.NumLaps ?? 0,
                // SEM VOLTA FECHADA NÃO HÁ TEMPO, e o servidor guarda
                // 999999999 para dizer isso. Mandar o número cru faria a tela
                // escrever "277:46:39" como se fosse uma volta de verdade.
                MelhorVoltaMs = Tempo(r2?.BestLap),
                UltimaVoltaMs = Tempo(r2?.LastLap),
                // Em corrida o servidor numera a partir do zero.
                PosicaoDaCorrida = sessao.Configuration.Type == SessionType.Race && r2 is not null
                    ? (int)r2.RacePos + 1
                    : 0,
                SetoresMs = r2?.LastSplits ?? [],
            });
        }

        return new AoVivoResponse
        {
            Sessao = (int)sessao.Configuration.Type,
            RestamMs = sessao.TimeLeftMilliseconds,
            VoltasDoLider = (int)sessao.LeaderLapCount,
            Carros = carros,
        };
    }

    /// <summary>
    /// O tempo, ou <c>0</c> quando não existe.
    ///
    /// <para>
    /// O servidor marca "sem volta" com <c>999999999</c> — quase 278 horas.
    /// Repassado cru, ele atravessa a rede como um tempo plausível e só vira
    /// absurdo na hora de formatar, longe daqui. Zero é o combinado com o app:
    /// "não há tempo", e lá vira travessão.
    /// </para>
    /// </summary>
    public static uint Tempo(uint? bruto) =>
        bruto is null or 0 or 999999999 ? 0 : bruto.Value;
}

public class AoVivoResponse
{
    /// <summary>1 = treino, 2 = classificação, 3 = corrida — do acServer.</summary>
    public int Sessao { get; set; }
    public int RestamMs { get; set; }
    public int VoltasDoLider { get; set; }
    public List<AoVivoCar> Carros { get; set; } = [];
}

public class AoVivoCar
{
    public byte CarId { get; set; }
    public string Piloto { get; set; } = "";
    public string Equipe { get; set; } = "";
    public string Pais { get; set; } = "";
    public string Carro { get; set; } = "";
    public string Pintura { get; set; } = "";
    public uint Voltas { get; set; }
    /// <summary>Zero = ainda não fechou volta.</summary>
    public uint MelhorVoltaMs { get; set; }
    /// <summary>Zero = ainda não fechou volta.</summary>
    public uint UltimaVoltaMs { get; set; }
    /// <summary>
    /// A posição que o SERVIDOR calcula, e só em corrida — zero fora dela.
    ///
    /// <para>
    /// Em treino e classificação o acServer não ordena ninguém: quem ordena é
    /// a melhor volta, e isso quem faz é a tela. Mandar um número aqui seria
    /// inventar uma classificação que o servidor não tem.
    /// </para>
    /// </summary>
    public int PosicaoDaCorrida { get; set; }
    /// <summary>Setores da última volta fechada. Vazio = nenhuma ainda.</summary>
    public uint[] SetoresMs { get; set; } = [];
}
