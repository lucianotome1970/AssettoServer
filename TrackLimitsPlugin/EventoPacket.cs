using AssettoServer.Network.ClientMessages;

namespace TrackLimitsPlugin;

/// <summary>
/// O nome do evento, dito pelo servidor.
///
/// <para>
/// POR QUE NÃO BASTA `ac.getServerName()` NO CLIENTE: aquela função devolve o
/// `SERVER_NAME` do `race.ini`, que o Content Manager escreve na hora de
/// lançar o jogo a partir da entrada dele na lista de servidores. Renomeando o
/// servidor, o valor fica para trás -- o HUD mostrava "TESTE" enquanto o
/// servidor já se chamava "4Fun Liga Veteranos - Guaporé", conectado na mesma
/// porta.
/// </para>
///
/// <para>
/// E POR QUE NÃO A MENSAGEM DE BOAS-VINDAS: ela chega pelo
/// <c>ac.onOnlineWelcome</c>, que é um CALLBACK. O app Lua é carregado na
/// subida da sessão, depois de a mensagem ter chegado, e aí o callback nunca
/// dispara. Testado na pista: o título continuou errado.
/// </para>
///
/// <para>
/// REPETIDO COM O LIMITE DE PIT, no mesmo laço de dez segundos e pelo mesmo
/// motivo medido lá: o handshake do CSP chega segundos depois da conexão e o
/// CSP recarrega o app quando o arquivo muda. Quem repete não depende de
/// quem carregou primeiro.
/// </para>
///
/// <para>
/// 64 CARACTERES. O nome da liga mais a etapa cabe com folga; o que passar
/// disso é cortado no servidor, onde dá para ver, em vez de chegar truncado
/// sem explicação.
/// </para>
/// </summary>
[OnlineEvent(Key = "evento")]
public class EventoPacket : OnlineEvent<EventoPacket>
{
    /// <summary>O nome do servidor, sem o sufixo de porta que o `/INFO` junta.</summary>
    [OnlineEventField(Name = "nome", Size = 64)]
    public string Nome = "";
}
