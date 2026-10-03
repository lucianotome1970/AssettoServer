using System;
using System.Collections.Generic;
using System.Linq;
using AssettoServer.Shared.Model;

namespace AssettoServer.Server;

/// <summary>
/// Keeps the server's car enumerations in step with the client's.
///
/// <para>
/// A SPECTATOR ENTRY IS NOT JUST A FLAG. The client honours the bit in
/// <see cref="Shared.Network.Packets.Outgoing.CarListResponse"/> and drops that car
/// from its own list - so it holds 34 cars where the entry list has 35. Every packet
/// that then enumerates cars has to agree, or the client reads the next field from the
/// wrong offset.
/// </para>
///
/// <para>
/// MEASURED, 03/10/2026: with the bit set and nothing else changed, every client that
/// connected died within seconds, whichever slot it took - <c>UDPPAcket out of bounds</c>
/// inside <c>onRemoteLapCompleted</c>, after <c>Cannot find car with sessionID: 255</c>.
/// Four crashes; zero with the entry back at SPECTATOR_MODE=0, same binary.
/// </para>
///
/// <para>
/// TWO OF THE THREE LISTS CARRY NO COUNT. <c>CurrentSessionUpdate</c> writes one session
/// id per grid slot and then <c>StartTime</c>; <c>RaceOver</c> writes one record per car
/// and then a flag. The client sizes both from what is left of the packet, so one car too
/// many does not overflow a loop - it shifts the field after the list. Only
/// <c>LapCompletedOutgoing</c> has a length byte, and that is the one that finally crashed.
/// </para>
///
/// <para>
/// Results stay COMPLETE on purpose: <c>CurrentSession.Results</c> is indexed by session id
/// all over the server, and removing the spectator there would turn this into a null hunt.
/// The filtering happens only where a list reaches the wire.
/// </para>
/// </summary>
public static class VagaDeTransmissao
{
    /// <summary>
    /// Drops broadcast entries from a list of cars bound for the wire, keeping the one the
    /// packet is being built FOR.
    /// </summary>
    /// <param name="exceto">
    /// The recipient's own session id. A client that cannot see its own car cannot play:
    /// measured on 03/10/2026, a driver sitting IN the broadcast slot crashed on the first
    /// packet addressed to it, because the client had already dropped that car from its list.
    /// So each client gets a view in which only OTHER broadcast slots are missing.
    /// </param>
    public static IEnumerable<IEntryCar<IClient>> SemEspectadores(
        IEnumerable<IEntryCar<IClient>> carros, byte? exceto = null)
        => carros.Where(c => !c.IsSpectator || c.SessionId == exceto);

    /// <summary>
    /// Drops broadcast entries from a results table bound for the wire.
    /// </summary>
    /// <remarks>
    /// Takes the test as a delegate rather than reaching for the cars, so the rule can be
    /// exercised without standing up a server.
    /// </remarks>
    public static Dictionary<byte, EntryCarResult> SemEspectadores(
        Dictionary<byte, EntryCarResult> resultados,
        Func<byte, bool> ehEspectador,
        byte? exceto = null)
    {
        ArgumentNullException.ThrowIfNull(resultados);
        ArgumentNullException.ThrowIfNull(ehEspectador);

        var saida = new Dictionary<byte, EntryCarResult>(resultados.Count);
        foreach (var (sessionId, resultado) in resultados)
        {
            if (!ehEspectador(sessionId) || sessionId == exceto) saida.Add(sessionId, resultado);
        }
        return saida;
    }
}
