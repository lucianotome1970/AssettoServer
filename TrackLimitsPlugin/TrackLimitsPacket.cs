using AssettoServer.Network.ClientMessages;

namespace TrackLimitsPlugin;

/// <summary>
/// Tells a driver their cut count changed, so the HUD can show it.
///
/// The game does not report cuts during a race at all - measured across every
/// result file this league has: practice and qualifying count them, race never
/// does - so this is the only number a driver has in the session that matters.
/// </summary>
[OnlineEvent(Key = "trackLimits")]
public class TrackLimitsPacket : OnlineEvent<TrackLimitsPacket>
{
    [OnlineEventField(Name = "cuts")]
    public int Cuts;

    [OnlineEventField(Name = "metresOut")]
    public float MetresOut;
}
