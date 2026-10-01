using System.Numerics;
using System.Runtime.InteropServices;

namespace AssettoServer.Server.Ai.Splines;

[StructLayout(LayoutKind.Sequential, Pack = 4)]
public struct SplinePoint
{
    public int Id;
    public Vector3 Position;
    public float Radius;
    public float Camber;
    public float Length;

    public int JunctionStartId;
    public int JunctionEndId;
    public int PreviousId;
    public int NextId;
    public int LeftId;
    public int RightId;
    public int LanesId;

    /// <summary>
    /// Distance from this point to the left edge of the track, in metres.
    ///
    /// Already present in every fast_lane.ai and read by the parser, which
    /// threw it away because traffic does not need it. Track limits do: it is
    /// the only description of where the circuit ends that a server has
    /// without loading the track mesh.
    ///
    /// APPENDED AT THE END on purpose. The struct is written to the spline
    /// cache raw, so adding a field anywhere else would shift everything after
    /// it in caches written by other versions.
    /// </summary>
    public float SideLeft;

    /// <summary>Distance to the right edge, in metres.</summary>
    public float SideRight;
}
