namespace Irix.Drawing;

public readonly struct EffectiveScissor(DrawRect Bounds, bool IsEmpty) : IEquatable<EffectiveScissor>
{

    public DrawRect Bounds { get; } = Bounds;
    public bool IsEmpty { get; } = IsEmpty;

    public static EffectiveScissor Empty => new(default, true);

    public bool Equals(EffectiveScissor other) => Bounds == other.Bounds && IsEmpty == other.IsEmpty;

    public override bool Equals(object? obj) => obj is EffectiveScissor other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Bounds, IsEmpty);

    public static bool operator ==(EffectiveScissor left, EffectiveScissor right) => left.Equals(right);

    public static bool operator !=(EffectiveScissor left, EffectiveScissor right) => !left.Equals(right);
}

public readonly struct IntegerScissorRect(int Left, int Top, int Right, int Bottom) : IEquatable<IntegerScissorRect>
{

    public int Left { get; } = Left;
    public int Top { get; } = Top;
    public int Right { get; } = Right;
    public int Bottom { get; } = Bottom;

    public static IntegerScissorRect Empty => default;

    public bool IsEmpty => Right <= Left || Bottom <= Top;

    public bool Equals(IntegerScissorRect other)
    {
        return Left == other.Left
            && Top == other.Top
            && Right == other.Right
            && Bottom == other.Bottom;
    }

    public override bool Equals(object? obj) => obj is IntegerScissorRect other && Equals(other);

    public override int GetHashCode() => HashCode.Combine(Left, Top, Right, Bottom);

    public static bool operator ==(IntegerScissorRect left, IntegerScissorRect right) => left.Equals(right);

    public static bool operator !=(IntegerScissorRect left, IntegerScissorRect right) => !left.Equals(right);
}

public static class DrawingScissor
{
    public static EffectiveScissor ResolveEffectiveScissor(in FrameContext frameContext, in DrawRect clipBounds)
    {
        var viewportBounds = new DrawRect(0, 0, frameContext.Width, frameContext.Height);
        return ResolveEffectiveScissor(viewportBounds, clipBounds);
    }

    public static EffectiveScissor ResolveEffectiveScissor(in DrawRect viewportBounds, in DrawRect clipBounds)
    {
        if (viewportBounds.Width <= 0 || viewportBounds.Height <= 0)
        {
            return EffectiveScissor.Empty;
        }

        var source = clipBounds == default ? viewportBounds : clipBounds;
        if (source.Width <= 0 || source.Height <= 0)
        {
            return EffectiveScissor.Empty;
        }

        var left = MathF.Max(viewportBounds.X, source.X);
        var top = MathF.Max(viewportBounds.Y, source.Y);
        var right = MathF.Min(viewportBounds.X + viewportBounds.Width, source.X + source.Width);
        var bottom = MathF.Min(viewportBounds.Y + viewportBounds.Height, source.Y + source.Height);
        var width = right - left;
        var height = bottom - top;

        return width <= 0 || height <= 0
            ? EffectiveScissor.Empty
            : new EffectiveScissor(new DrawRect(left, top, width, height), false);
    }

    public static IntegerScissorRect ToIntegerScissorRect(in EffectiveScissor scissor, int viewportWidth, int viewportHeight)
    {
        if (scissor.IsEmpty || viewportWidth <= 0 || viewportHeight <= 0)
        {
            return IntegerScissorRect.Empty;
        }

        var bounds = scissor.Bounds;
        var left = Math.Clamp((int)MathF.Floor(bounds.X), 0, viewportWidth);
        var top = Math.Clamp((int)MathF.Floor(bounds.Y), 0, viewportHeight);
        var right = Math.Clamp((int)MathF.Ceiling(bounds.X + bounds.Width), 0, viewportWidth);
        var bottom = Math.Clamp((int)MathF.Ceiling(bounds.Y + bounds.Height), 0, viewportHeight);

        return right <= left || bottom <= top
            ? IntegerScissorRect.Empty
            : new IntegerScissorRect(left, top, right, bottom);
    }
}
