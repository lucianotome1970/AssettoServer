using AssettoServer.Commands;
using AssettoServer.Commands.Attributes;
using Qmmands;

namespace SondaFisicaPlugin;

/// <summary>
/// O unico comando da sonda.
///
/// <para>
/// ADMIN ONLY, e nao por formalidade: ele muda a potencia de um carro em
/// pista. Numa medicao isso e o ponto; na mao de qualquer um seria trapaca.
/// </para>
///
/// <para>
/// COMO MEDIR A ESCALA. O parametro de <c>physics.setCarRestrictor</c> esta
/// documentado so como "restrictor value", sem unidade. Entao se mede por
/// comparacao: numa reta, da mesma marca e na mesma marcha, acelere a fundo
/// com <c>/sonda 0</c> e depois com <c>/sonda 50</c>. O cliente manda rpm e
/// velocidade a cada 0,5 s, e os dois tracos ficam no log do servidor. Se 50
/// for "metade da potencia", a diferenca aparece; se for "50 por cento de
/// restricao" ou outra coisa, aparece igual -- o traco nao depende de eu ter
/// adivinhado a unidade.
/// </para>
/// </summary>
[RequireAdmin]
public class SondaFisicaCommandModule : ACModuleBase
{
    private readonly SondaFisicaService _sonda;

    public SondaFisicaCommandModule(SondaFisicaService sonda) => _sonda = sonda;

    /// <summary>
    /// Aplica um restritor no carro de quem digitou e inicia o traco.
    /// </summary>
    /// <remarks>
    /// SEM ALVO, DE PROPOSITO: quem esta medindo e quem esta dirigindo, e um
    /// nome digitado errado numa ferramenta de medicao vira "nao aconteceu
    /// nada", que e a resposta que uma medicao nunca pode dar de forma
    /// ambigua.
    /// </remarks>
    [Command("sonda"), RequireConnectedPlayer]
    public void Sonda(float valor)
    {
        _sonda.Pedir(Client!, valor);
        Reply($"Sonda: pedido restritor {valor}. Acelere a fundo; o traco vai para o log do servidor.");
    }
}
