using System;
using System.Collections.Generic;
using AssettoServer.Server.Configuration;
using AssettoServer.Server.Configuration.Extra;
using Serilog;

namespace AssettoServer.Server;

/// <summary>
/// O BOP da liga: lastro e restritor POR CARRO, aplicados quando alguém pega a
/// vaga.
///
/// <para>
/// POR QUE NÃO NO <c>entry_list</c>. Lá o lastro e o restritor são da VAGA, e
/// a vaga só tem dono quando a liga sabe quem vai correr. Num 4fun de entrada
/// livre as vagas são abertas: não há piloto para mapear, e um BOP escrito na
/// vaga valeria apenas para quem foi previsto. O grid ficava equalizado na
/// etapa e desequilibrado no treino — que é justamente onde se aprende o
/// carro.
/// </para>
///
/// <para>
/// E ISSO É O QUE O ACC FAZ: o <c>bop.json</c> dele é por carro e por pista, o
/// servidor aplica, e quem dirige não entra na conta. Aqui o encanamento já
/// existia inteiro e ninguém usava — <see cref="EntryCar.Ballast"/> e
/// <see cref="EntryCar.Restrictor"/> são mutáveis em tempo de execução (é
/// assim que os comandos <c>/ballast</c> e <c>/restrictor</c> do admin
/// funcionam com o piloto na pista), e o cliente é avisado pelo pacote
/// <c>BallastUpdate</c>, que o servidor já manda de todos os carros a quem
/// conecta.
/// </para>
///
/// <para>
/// A TABELA VEM DO <c>extra_cfg.yml</c>, escrito pelo Pit Engineer a partir do
/// cadastro da liga. Assim o servidor continua funcionando sem rede, e trocar
/// o BOP é regerar o preset — não há consulta a fazer no meio de uma corrida.
/// </para>
/// </summary>
public class BopDaLiga
{
    private readonly Dictionary<string, BopDeCarro> _porCarro = new(StringComparer.OrdinalIgnoreCase);

    /// <param name="linhas">
    /// A tabela, direto -- e nao a configuracao inteira.
    ///
    /// RECEBER A LISTA E O QUE TORNA ISTO TESTAVEL: `ACServerConfiguration` so
    /// se constroi lendo arquivos de um preset em disco, entao um teste que
    /// dependesse dela testaria o carregador de configuracao, e nao a regra.
    /// </param>
    public BopDaLiga(IEnumerable<BopDeCarroConfiguration> linhas)
    {
        foreach (var linha in linhas)
        {
            var carro = (linha.Carro ?? "").Trim();
            if (carro.Length == 0) continue;

            // OS LIMITES SÃO DO AC, e não nossos: o validador do próprio
            // servidor exige lastro >= 0 e restritor entre 0 e 400. Um número
            // fora disso vindo do cadastro derrubaria o servidor na partida,
            // e a etapa não sobe — pior que correr sem BOP.
            _porCarro[carro] = new BopDeCarro(
                Math.Max(0f, linha.LastroKg),
                Math.Clamp(linha.Restritor, 0, 400));
        }

        if (_porCarro.Count > 0)
        {
            Log.Information("BOP da liga: {Count} carro(s) com lastro ou restritor", _porCarro.Count);
        }
    }

    /// <summary>Há BOP cadastrado para algum carro?</summary>
    public bool Existe => _porCarro.Count > 0;

    /// <summary>O BOP deste modelo, ou <c>null</c> quando ele não tem.</summary>
    public (float LastroKg, int Restritor)? Para(string modelo)
        => _porCarro.TryGetValue(modelo ?? "", out var b) ? (b.LastroKg, b.Restritor) : null;

    /// <summary>
    /// Aplica o BOP do modelo desta vaga. Chamado quando a vaga é tomada.
    /// </summary>
    /// <remarks>
    /// ANTES DE O CLIENTE TERMINAR DE CONECTAR, de propósito: o pacote que
    /// leva lastro e restritor sai no lote de conexão, montado depois. Aplicar
    /// aqui dispensa mandar um segundo pacote e dispensa o cliente lidar com
    /// um valor que muda logo após entrar.
    ///
    /// CARRO SEM BOP VOLTA A ZERO, e isso não é detalhe: a vaga é reusada por
    /// quem entrar depois, e <c>EntryCar.Reset()</c> não mexe em lastro. Sem
    /// zerar, o lastro de uma etapa ficaria grudado na vaga até o servidor
    /// reiniciar.
    /// </remarks>
    public void Aplicar(EntryCar vaga)
    {
        if (Para(vaga.Model) is { } bop)
        {
            vaga.Ballast = bop.LastroKg;
            vaga.Restrictor = bop.Restritor;
        }
        else if (Existe)
        {
            vaga.Ballast = 0;
            vaga.Restrictor = 0;
        }
    }

    private readonly record struct BopDeCarro(float LastroKg, int Restritor);
}
