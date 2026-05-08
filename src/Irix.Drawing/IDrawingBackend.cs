namespace Irix.Drawing;

public interface IDrawingBackend : IDisposable
{
    void BeginFrame(in FrameContext frameContext);

    void Execute(ReadOnlySpan<DrawCommand> commands, IFrameResourceResolver resources);

    void EndFrame();
}

/// <summary>
/// Optional interface for backends that want read-only access to dirty command ranges
/// for diagnostics or selective rendering. The compositor sets the ranges before Execute.
/// </summary>
public interface IDirtyRangeAware
{
    /// <summary>
    /// Set the dirty command ranges for the current frame. Implementations should treat
    /// this as read-only diagnostic data. Full-frame rendering behavior must not change.
    /// </summary>
    void SetDirtyCommandRanges(IReadOnlyList<(int Start, int Count)> ranges);
}
