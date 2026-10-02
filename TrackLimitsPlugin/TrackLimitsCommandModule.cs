using AssettoServer.Commands;
using AssettoServer.Commands.Attributes;
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
    /// Penalises WHOEVER TYPED IT, and takes no target.
    ///
    /// <para>
    /// Not an oversight. Targeting by name needs quoting as soon as the name has
    /// a space in it -- most do -- and a mistyped name in a test tool turns into
    /// "nothing happened", which is the one answer a measurement must never give
    /// ambiguously. Whoever is mapping the modes is the one driving.
    /// </para>
    /// </summary>
    [Command("penalty"), RequireConnectedPlayer]
    public void Penalty(string modo, int argumento = 3)
    {
        if (!Enum.TryParse<CSPAdminPenaltyMode>(modo, true, out var m))
        {
            Reply("Unknown mode. Use one of: "
                + $"{string.Join(", ", Enum.GetNames<CSPAdminPenaltyMode>())}.");
            return;
        }

        PunicaoDoServidor.Aplicar(Client!, m, argumento, "manual test");
        Reply($"{m} ({argumento}) sent to you.");
    }
}
