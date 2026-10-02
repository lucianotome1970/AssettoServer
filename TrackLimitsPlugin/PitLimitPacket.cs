using AssettoServer.Network.ClientMessages;

namespace TrackLimitsPlugin;

/// <summary>
/// Tells a driver the pit speed limit, so the HUD can warn with the real number.
///
/// <para>
/// The client has no way to know it: the limit is not a field CSP exposes, it
/// arrives inside the compressed welcome blob that only CSP itself decodes. The
/// HUD showing a hardcoded number would be a confident lie the first time a
/// league event changed it.
/// </para>
/// </summary>
[OnlineEvent(Key = "pitLimit")]
public class PitLimitPacket : OnlineEvent<PitLimitPacket>
{
    [OnlineEventField(Name = "speedKmh")]
    public int SpeedKmh;
}
