using AssettoServer.Network.ClientMessages;

namespace BlueFlagPlugin;

/// <summary>
/// Sent to the car being lapped, and again with <see cref="Show"/> false when
/// it is clear.
///
/// A client side script draws it. The protocol has no blue flag packet of its
/// own -- only black, through CSPAdminPenalty -- so this is the only way for
/// the server to tell a driver that someone a lap up is arriving.
/// </summary>
[OnlineEvent(Key = "blueFlag")]
public class BlueFlagPacket : OnlineEvent<BlueFlagPacket>
{
    [OnlineEventField(Name = "show")]
    public bool Show;

    /// <summary>Session id of the car doing the lapping, for the HUD to name.</summary>
    [OnlineEventField(Name = "byCar")]
    public byte ByCar;

    /// <summary>Seconds until it arrives, rounded. Zero when clearing.</summary>
    [OnlineEventField(Name = "seconds")]
    public byte Seconds;
}
