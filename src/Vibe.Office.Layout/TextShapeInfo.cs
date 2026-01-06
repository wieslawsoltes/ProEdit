namespace Vibe.Office.Layout;

public sealed class TextShapeInfo
{
    public int TextLength { get; }
    public int[] ClusterOffsets { get; }
    public float[] ClusterAdvances { get; }

    public TextShapeInfo(int textLength, int[] clusterOffsets, float[] clusterAdvances)
    {
        TextLength = textLength;
        ClusterOffsets = clusterOffsets;
        ClusterAdvances = clusterAdvances;
    }
}
