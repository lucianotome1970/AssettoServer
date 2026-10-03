using AssettoServer.Network.ClientMessages;

namespace TrackLimitsPlugin;

/// <summary>
/// Diz ao piloto QUAL punicao o servidor aplicou, porque o jogo nao diz.
///
/// <para>
/// <c>currentPenaltyType = 2</c> e TRES punicoes diferentes, e das tres duas se
/// cumprem de formas opostas: a de queima de largada cumpre-se PASSANDO pelo pit
/// sem parar, <c>MandatoryPits</c> cumpre-se PARANDO no box, e
/// <c>TeleportToPits</c> cumpre-se esperando. Dos campos do cliente nao ha como
/// distinguir -- medido e documentado em <c>docs/punicao-corte-ac1.md</c>.
/// </para>
///
/// <para>
/// E O ERRO E CARO: entrar no pit sem parar com <c>MandatoryPits</c> pendente deu
/// BANDEIRA PRETA 5,6 s depois, medido. Um HUD que mande "passe" ali desqualifica
/// um piloto que estava fazendo o que a tela pediu.
/// </para>
///
/// <para>
/// QUEM SABE E O SERVIDOR, porque foi ele que escolheu o modo. AUSENCIA desta
/// mensagem tambem informa: significa que a punicao veio do JOGO -- queima de
/// largada --, que se cumpre passando. Por isso o HUD nao precisa de um valor
/// "nenhum" aqui; ele usa o comportamento nativo quando nada chegou.
/// </para>
/// </summary>
[OnlineEvent(Key = "penalty")]
public class PenaltyPacket : OnlineEvent<PenaltyPacket>
{
    /// <summary>O <c>CSPAdminPenaltyMode</c> aplicado.</summary>
    [OnlineEventField(Name = "mode")]
    public byte Mode;

    /// <summary>Voltas para MandatoryPits, segundos para TeleportToPits.</summary>
    [OnlineEventField(Name = "argument")]
    public int Argument;
}
