namespace ZeroNative.Primitives;

public readonly record struct PointF(float X, float Y)
{
    public static readonly PointF Zero = new(0, 0);
}

public readonly record struct SizeF(float Width, float Height)
{
    public static readonly SizeF Zero = new(0, 0);

    public static SizeF Init(float width, float height) => new(width, height);

    public bool IsEmpty => Width <= 0 || Height <= 0;
}

public readonly record struct RectF(float X, float Y, float Width, float Height)
{
    public static readonly RectF Empty = new(0, 0, 0, 0);

    public static RectF Init(float x, float y, float width, float height) => new(x, y, width, height);

    public static RectF FromSize(SizeF size) => new(0, 0, size.Width, size.Height);

    public PointF Origin => new(X, Y);
    public SizeF Size => new(Width, Height);

    public float Right => X + Width;
    public float Bottom => Y + Height;

    public bool Contains(PointF p) => p.X >= X && p.X <= Right && p.Y >= Y && p.Y <= Bottom;

    public RectF Inset(float dx, float dy) => new(X + dx, Y + dy, Width - 2 * dx, Height - 2 * dy);

    public RectF Union(RectF other)
    {
        var nx = Math.Min(X, other.X);
        var ny = Math.Min(Y, other.Y);
        var r = Math.Max(Right, other.Right);
        var b = Math.Max(Bottom, other.Bottom);
        return new RectF(nx, ny, r - nx, b - ny);
    }
}

public readonly record struct EdgeInsets(float Top, float Leading, float Bottom, float Trailing)
{
    public static readonly EdgeInsets Zero = new(0, 0, 0, 0);

    public static EdgeInsets All(float value) => new(value, value, value, value);

    public static EdgeInsets Symmetric(float vertical, float horizontal) =>
        new(vertical, horizontal, vertical, horizontal);
}
