namespace Irix.Drawing;

public readonly record struct EffectiveScissor(DrawRect Bounds, bool IsEmpty)
{
    public static EffectiveScissor Empty => new(default, true);
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
}