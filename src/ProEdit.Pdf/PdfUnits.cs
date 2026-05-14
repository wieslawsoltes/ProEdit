namespace ProEdit.Pdf;

public static class PdfUnits
{
    private const float PointsToDipScale = 96f / 72f;

    public static float PointsToDip(double points) => (float)(points * PointsToDipScale);

    public static double DipToPoints(float dip) => dip / PointsToDipScale;
}
