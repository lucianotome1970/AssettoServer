using AssettoServer.Network.ClientMessages;

namespace SondaFisicaPlugin;

/// <summary>
/// O que o cliente responde. Vai do script que o SERVIDOR enviou para o
/// servidor, e e aqui que as tres perguntas se respondem.
/// </summary>
/// <remarks>
/// <para>
/// <c>Permitido</c> E A RESPOSTA QUE DECIDE TUDO. A documentacao do CSP diz,
/// sobre <c>physics.allowed()</c>: "Returns true if script can affect physics
/// engine. If not, only <c>physics.raycastTrack()</c> is available". Se vier
/// falso num script enviado pelo servidor, o push-to-pass pelo servidor nao
/// existe e o desenho inteiro cai -- melhor descobrir com um plugin de
/// medicao do que depois de escrever o sistema.
/// </para>
/// <para>
/// RPM E VELOCIDADE NAO SAO ENFEITE. Nao ha como LER a potencia do carro pelo
/// script, entao a escala do restritor so se mede pela aceleracao: duas
/// passadas iguais, restritores diferentes, e a diferenca da a escala. Por
/// isso o cliente manda um traco destes a cada 0,5 s depois de cada mudanca.
/// </para>
/// </remarks>
[OnlineEvent(Key = "sondaRelatorio")]
public class SondaRelatorioPacket : OnlineEvent<SondaRelatorioPacket>
{
    /// <summary>O que <c>physics.allowed()</c> devolveu no cliente.</summary>
    [OnlineEventField(Name = "permitido")]
    public bool Permitido;

    /// <summary>Versao do CSP do cliente, para saber a partir de qual funciona.</summary>
    [OnlineEventField(Name = "versaoCSP")]
    public int VersaoCsp;

    /// <summary>Ultimo restritor aplicado, ou -1 se nenhum nesta sessao.</summary>
    [OnlineEventField(Name = "restritor")]
    public float Restritor;

    [OnlineEventField(Name = "rpm")]
    public float Rpm;

    [OnlineEventField(Name = "kmh")]
    public float Kmh;

    /// <summary>Por que este relatorio foi enviado: chegada, aplicacao ou traco.</summary>
    [OnlineEventField(Name = "nota", Size = 48)]
    public string? Nota;
}

/// <summary>
/// A ordem que o servidor manda. So sai por comando de administrador: uma
/// sonda que mexe na fisica sozinha seria exatamente o que recusamos receber
/// de terceiros.
/// </summary>
[OnlineEvent(Key = "sondaComando")]
public class SondaComandoPacket : OnlineEvent<SondaComandoPacket>
{
    /// <summary>Valor passado a <c>physics.setCarRestrictor</c>.</summary>
    [OnlineEventField(Name = "valor")]
    public float Valor;

    /// <summary>Por quantos segundos o cliente manda o traco de rpm e velocidade.</summary>
    [OnlineEventField(Name = "segundos")]
    public float Segundos;
}
