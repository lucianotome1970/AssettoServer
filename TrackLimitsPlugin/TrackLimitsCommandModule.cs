using AssettoServer.Commands;
using AssettoServer.Commands.Attributes;
using AssettoServer.Network.Tcp;
using AssettoServer.Shared.Network.Packets.Shared;
using Qmmands;

namespace TrackLimitsPlugin;

/// <summary>
/// Admin commands for track limits.
///
/// <para>
/// <c>/penalty</c> EXISTS TO MAP THE MODES CHEAPLY. Which
/// <see cref="CSPAdminPenaltyMode"/> does what is not documented anywhere --
/// upstream says only two of the six are "known to work", and measuring
/// <c>TeleportToPits</c> showed the CSP SDK's units are right there and the
/// name we had assumed was wrong. The alternative way to find out is to edit
/// YAML, restart the server, rejoin and drive off track, per mode. This does it
/// in one chat line, in the session already running.
/// </para>
///
/// <para>
/// ADMIN ONLY, and that is not a formality: a command that hands out penalties
/// is the one command a driver must never reach.
/// </para>
/// </summary>
[RequireAdmin]
public class TrackLimitsCommandModule : ACModuleBase
{
    /// <summary>
    /// Penalises the given driver, or WHOEVER TYPED IT when no target is given.
    ///
    /// <para>
    /// IT TOOK NO TARGET AT ALL AT FIRST, and that was not an oversight: this
    /// began as the cheap way to map the six undocumented
    /// <see cref="CSPAdminPenaltyMode"/> values, and whoever is mapping them is
    /// the one driving. Targeting by NAME needs quoting as soon as the name has
    /// a space in it -- most do -- and a mistyped name in a measuring tool
    /// turns into "nothing happened", the one answer a measurement must never
    /// give ambiguously.
    /// </para>
    ///
    /// <para>
    /// WHAT CHANGED IS THE CALLER. The HUD's race control panel fires this by
    /// SESSION ID, which <see cref="AssettoServer.Commands.TypeParsers.ACClientTypeParser"/>
    /// accepts as a plain number and which the timing tower already knows by
    /// heart -- no name, no quoting, no ambiguity. Handing out a drive-through
    /// is the league's actual penalty, and race control cannot be the one
    /// person who can only penalise themselves.
    /// </para>
    ///
    /// <para>
    /// THE TARGET COMES LAST so that the measuring form still reads the same.
    /// Reordering it would break every note written while the modes were being
    /// mapped.
    /// </para>
    /// </summary>
    [Command("penalty"), RequireConnectedPlayer]
    public void Penalty(string modo, int argumento = 3, ACTcpClient? alvo = null)
    {
        if (!Enum.TryParse<CSPAdminPenaltyMode>(modo, true, out var m))
        {
            Reply("Unknown mode. Use one of: "
                + $"{string.Join(", ", Enum.GetNames<CSPAdminPenaltyMode>())}.");
            return;
        }

        var quem = alvo ?? Client!;
        PunicaoDoServidor.Aplicar(quem, m, argumento,
            alvo == null ? "manual test" : $"race control ({Client!.Name})");
        Reply(alvo == null
            ? $"{m} ({argumento}) sent to you."
            : $"{m} ({argumento}) sent to {quem.Name} ({quem.SessionId}).");
    }
}
