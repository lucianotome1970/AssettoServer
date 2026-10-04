using System.Threading.Tasks;
using AssettoServer.Network.Tcp;
using AssettoServer.Server.Steam;
using AssettoServer.Shared.Network.Packets.Incoming;
using AssettoServer.Shared.Network.Packets.Outgoing.Handshake;

namespace AssettoServer.Server.OpenSlotFilters;

public class SteamSlotFilter : OpenSlotFilterBase
{
    private readonly SteamManager _steam;

    public SteamSlotFilter(SteamManager steam)
    {
        _steam = steam;
    }

    public override async Task<AuthFailedResponse?> ShouldAcceptConnectionAsync(ACTcpClient client, HandshakeRequest request)
    {
        // DIZ O QUE CHEGOU antes de recusar. "Missing session ticket" sozinho nao
        // distingue "o cliente nao mandou" de "mandou e o parser descartou por
        // tamanho" -- e as duas pedem investigacoes opostas.
        // DEBUG, E NAO INFORMATION: isto nasceu para caçar a falta do ticket do
        // Steam, e a caça acabou -- a causa era o `__FEATURES` do race.ini. Uma
        // linha por entrada num grid de 35 enche o log de quem procura outra
        // coisa. Continua aqui porque, quando o sintoma voltar, e a primeira
        // pergunta a fazer: subir o nivel do log e mais barato que reescrever.
        client.Logger.Debug(
            "Ticket do handshake: declarado={Declarado} disponivel={Disponivel} aceito={Aceito}",
            request.TicketDeclarado, request.TicketDisponivel, request.SessionTicket != null);

        if (!await _steam.ValidateSessionTicketAsync(request.SessionTicket, request.Guid, client))
        {
            return new AuthFailedResponse("Steam authentication failed.");
        }
        
        return await base.ShouldAcceptConnectionAsync(client, request);
    }
}
