using AssettoServer.Network.ClientMessages;

namespace TrackLimitsPlugin;

/// <summary>
/// Anuncia a TODOS que um piloto foi punido, e qual punição.
///
/// <para>
/// SEPARADO DO <see cref="PenaltyPacket"/> de propósito. Aquele vai só para o
/// piloto punido e responde "o que EU tenho que fazer" — por isso nem carrega
/// quem é, seria redundante. Este responde "quem foi punido, e de quê", que é
/// uma pergunta de quem assiste.
/// </para>
///
/// <para>
/// Juntar os dois num só seria mandar a instrução de cumprimento para o grid
/// inteiro: trinta e cinco HUDs mostrando "PARE NO BOX EM 3 VOLTAS" por causa de
/// uma punição alheia. O preço de um evento a mais é baixo perto disso.
/// </para>
///
/// <para>
/// O QUE ELE NÃO ALCANÇA: a punição que o JOGO aplica sozinho, como o
/// drive-through de queima de largada. Ela nasce no cliente e o servidor nunca
/// fica sabendo — medido e documentado em <c>docs/punicao-corte-ac1.md</c>.
/// A tarja da transmissão cobre o que a liga aplica, não o que o AC aplica.
/// </para>
/// </summary>
[OnlineEvent(Key = "penaltyBroadcast")]
public class PenaltyBroadcastPacket : OnlineEvent<PenaltyBroadcastPacket>
{
    /// <summary>A vaga do piloto punido, para o HUD achar o nome.</summary>
    [OnlineEventField(Name = "carro")]
    public byte Carro;

    /// <summary>O <c>CSPAdminPenaltyMode</c> aplicado.</summary>
    [OnlineEventField(Name = "mode")]
    public byte Mode;

    /// <summary>Voltas para MandatoryPits, segundos para TeleportToPits.</summary>
    [OnlineEventField(Name = "argument")]
    public int Argument;

    /// <summary>
    /// Por que, em texto curto, para a tarja mostrar o motivo.
    /// </summary>
    /// <remarks>
    /// CURTO PORQUE VAI AO AR: "3 cortes de pista" cabe numa tarja e explica; um
    /// motivo longo seria cortado no meio, que é pior que não mostrar.
    /// </remarks>
    [OnlineEventField(Name = "motivo", Size = 32)]
    public string Motivo = "";
}
