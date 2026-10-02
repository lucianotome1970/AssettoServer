using AssettoServer.Network.Tcp;
using AssettoServer.Shared.Network.Packets.Shared;
using Serilog;

namespace TrackLimitsPlugin;

/// <summary>
/// Applies a penalty to a driver from the server.
///
/// <para>
/// WHY THIS IS POSSIBLE AT ALL: the jump-start drive-through is detected and
/// applied by the CLIENT -- the server only relays the news (see
/// <c>ACTcpClient.OnClientEvent</c>, <c>ClientEventType.JumpStartPenalty</c>).
/// So there is no server-side penalty to reuse. What there IS is
/// <see cref="CSPAdminPenalty"/>: a CSP message an admin's game sends to
/// penalise someone, which the server forwards to the target. It is an
/// <c>IOutgoingNetworkPacket</c>, so the server can originate one itself.
/// </para>
///
/// <para>
/// THE SIGNATURE IS NOT CHECKED -- measured 02/10/2026. Nobody in AssettoServer
/// builds this packet server-side, so this was unknown: relaying an admin's
/// penalty, the server forwards whatever signature that admin's CSP wrote, and
/// the CSP core is closed. Sent with <c>Signature = 0</c> and
/// <c>SessionId = 255</c>, the client applied the penalty. That is the finding
/// this whole plugin rests on.
/// </para>
///
/// <para>
/// WHAT EACH MODE DOES, measured and not read:
/// </para>
/// <list type="bullet">
/// <item><b>TeleportToPits (2)</b> -- teleports the car into its box and freezes
/// it for <c>PenaltyArgument</c> SECONDS. Recorded: argument 3 gave
/// <c>par=3,2,1</c> one per second and the type cleared on its own at the end.
/// The CSP SDK's name and units are exactly right here. It is NOT a
/// drive-through, and it is brutal -- the car leaves the track instantly.</item>
/// <item><b>MandatoryPits (1)</b> -- untested here. Upstream says this and
/// TeleportToPits are the only two known to work.</item>
/// <item><b>SlowDown (3)</b>, <b>BlackFlag (4)</b> -- untested, upstream says
/// unknown. SlowDown needs the gas-cut penalty NOT disabled, and this league
/// sets <c>RACE_GAS_PENALTY_DISABLED=1</c>.</item>
/// </list>
///
/// <para>
/// THE NATIVE DRIVE-THROUGH IS NOT IN THIS LIST. The type-2-with-3-laps that a
/// burned start produces behaves differently from mode 2 sent here -- it does
/// not teleport, the parameter does not count down per second, and it is served
/// by passing through the pit lane. Same number in
/// <c>currentPenaltyType</c>, different penalty. Whatever the client runs for a
/// jump start, this packet does not reach it.
/// </para>
///
/// <para>
/// WHY THE SERVER AND NOT A LUA SCRIPT IN THE HUD: a cut the client detects and
/// self-reports is forgeable by anyone who edits the Lua, and the Lua sits on
/// the driver's own machine. The cut is already judged here, from geometry the
/// server sees for itself. Sending the verdict from the same place that reached
/// it keeps the whole chain out of the client's reach.
/// </para>
/// </summary>
public static class PunicaoDoServidor
{
    /// <summary>
    /// Marks the message as coming from the SERVER rather than from a player.
    /// The relay path sets exactly this before forwarding, so it is the value
    /// the client is known to accept.
    /// </summary>
    private const byte DoServidor = 255;

    /// <summary>The 64-byte cap is the packet's, not ours.</summary>
    private const int LimiteDaMensagem = 64;

    public static void Aplicar(ACTcpClient client, CSPAdminPenaltyMode modo,
        int argumento, string motivo)
    {
        if (argumento < 1) argumento = 1;

        var mensagem = motivo.Length > LimiteDaMensagem
            ? motivo[..LimiteDaMensagem]
            : motivo;

        client.SendPacket(new CSPAdminPenalty
        {
            SessionId = DoServidor,
            Mode = modo,
            CarIndex = client.SessionId,
            PenaltyArgument = argumento,
            Message = mensagem,
            Signature = 0
        });

        // EM Information, E NAO Debug: o log do servidor e a unica evidencia de
        // que o pacote SAIU. Sem ele, "nao aconteceu nada" nao distingue pacote
        // recusado de pacote nao enviado -- e foi essa confusao que custou cinco
        // largadas queimadas.
        Log.Information("TrackLimitsPlugin: {Mode} ({Argument}) sent to {Name} ({SessionId}) - {Reason}",
            modo, argumento, client.Name, client.SessionId, motivo);
    }
}
