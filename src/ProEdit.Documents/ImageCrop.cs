namespace ProEdit.Documents;

public sealed class ImageCrop : IEquatable<ImageCrop>
{
    public float Left { get; set; }
    public float Top { get; set; }
    public float Right { get; set; }
    public float Bottom { get; set; }

    public bool HasValues => Left > 0f || Top > 0f || Right > 0f || Bottom > 0f;

    public ImageCrop()
    {
    }

    public ImageCrop(float left, float top, float right, float bottom)
    {
        Left = left;
        Top = top;
        Right = right;
        Bottom = bottom;
    }

    public ImageCrop Clone()
    {
        return new ImageCrop(Left, Top, Right, Bottom);
    }

    public bool Equals(ImageCrop? other)
    {
        if (other is null)
        {
            return false;
        }

        return Left.Equals(other.Left)
               && Top.Equals(other.Top)
               && Right.Equals(other.Right)
               && Bottom.Equals(other.Bottom);
    }

    public override bool Equals(object? obj) => obj is ImageCrop other && Equals(other);

    public override int GetHashCode()
    {
        return HashCode.Combine(Left, Top, Right, Bottom);
    }
}
