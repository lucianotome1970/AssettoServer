using AssettoServer.Network.ClientMessages;

namespace TrackLimitsPlugin;

/// <summary>
/// Avisa um cliente de que ELE é admin, para o HUD mostrar a mesa de comandos.
///
/// <para>
/// O CLIENTE NÃO TEM COMO SABER SOZINHO. Nada no protocolo do AC conta ao Lua
/// se o jogador entrou como administrador; no chat isso só aparece quando se
/// digita <c>/admin</c> e a resposta chega. Sem este aviso, a única opção seria
/// mostrar os botões a todos e deixar o servidor recusar -- uma tela cheia de
/// botões que não funcionam para 34 dos 35.
/// </para>
///
/// <para>
/// A GARANTIA CONTINUA SENDO DO SERVIDOR. Isto é enfeite de tela: quem recusa
/// um comando é <see cref="ComandoAdminPacket"/> no servidor, olhando
/// <c>IsAdministrator</c>. Um cliente adulterado consegue, no máximo, desenhar
/// os botões para si mesmo e ver todos eles serem recusados.
/// </para>
///
/// <para>
/// SÓ VAI PARA QUEM É ADMIN, e por isso não existe campo "não é". A ausência da
/// mensagem é a resposta negativa, e o HUD começa fechado.
/// </para>
///
/// <para>
/// REPETIDO COM O LIMITE DE PIT, no mesmo laço de dez segundos e pelo mesmo
/// motivo medido lá: o handshake do CSP chegou OITO SEGUNDOS depois da conexão,
/// então uma mensagem única se perderia antes de haver Lua para ouvi-la -- e o
/// CSP recarrega o app quando o arquivo muda, o que faria o HUD esquecer.
/// </para>
/// </summary>
[OnlineEvent(Key = "souAdmin")]
public class SouAdminPacket : OnlineEvent<SouAdminPacket>
{
    /// <summary>Sempre 1. O campo existe porque o pacote precisa de um.</summary>
    [OnlineEventField(Name = "admin")]
    public byte Admin = 1;
}
