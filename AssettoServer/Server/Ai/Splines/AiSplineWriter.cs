using System.IO;
using AssettoServer.Utils;
using Serilog;

namespace AssettoServer.Server.Ai.Splines;

public class AiSplineWriter
{
    public void ToFile(MutableAiSpline map, string path)
    {
        Log.Debug("Writing cached AI spline to file");
        using var file = File.Create(path);

        var treePoints = map.KdTree.InternalPointArray;
        var treeNodes = map.KdTree.InternalNodeArray;

        file.Write(new AiSplineHeader
        {
            // FROM THE READER'S CONSTANT, never a literal. The writer stamping a
            // number of its own means every version bump has two places to change,
            // and forgetting the second one writes a cache the reader refuses -- in
            // a loop, since it regenerates it and refuses it again.
            Version = AiSpline.SupportedVersion,
            NumPoints = map.Points.Length,
            NumJunctions = map.Junctions.Count,
            NumKdTreePoints = treePoints.Length
        });
        
        file.Write(map.Points);
        file.Write(map.Junctions);
        file.Write(treePoints);
        file.Write(treeNodes);

        foreach (var lanes in map.Lanes)
        {
            file.Write(lanes.Length);
            file.Write(lanes);
        }
    }
}
