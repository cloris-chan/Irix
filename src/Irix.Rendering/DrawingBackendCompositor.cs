using Irix.Drawing;

namespace Irix.Rendering;

/// <summary>
/// Compositor that delegates rendering to an <see cref="IDrawingBackend"/>.
/// Owns a <see cref="RetainedRenderFrame"/> for incremental update and zero-alloc backend reads.
/// Caches hit targets from the frame for input routing.
/// This is the bridge between the RenderFrameBatch world and the IDrawingBackend world.
/// </summary>
public sealed class DrawingBackendCompositor(IDrawingBackend backend) : ICompositor, IDisposable
{
    private readonly IDrawingBackend _backend = backend;
    private readonly Lock _hitTargetsLock = new();
    private readonly RetainedRenderFrame _retainedFrame = new();
    private HitTestTarget[] _hitTargets = [];

    /// <summary>
    /// The dirty command ranges from the last render, if any.
    /// Reflects the actual ranges that were applied to the retained frame
    /// (may differ from the batch's dirty ranges if partial apply was refused).
    /// </summary>
    public IReadOnlyList<(int Start, int Count)> LastDirtyCommandRanges { get; private set; } = [];

    /// <summary>
    /// Whether the last render used partial apply on the retained frame.
    /// </summary>
    public bool LastPartialApplySucceeded { get; private set; }

    /// <summary>
    /// The retained render frame owned by this compositor.
    /// Exposed for diagnostics and test assertions.
    /// </summary>
    internal RetainedRenderFrame RetainedFrame => _retainedFrame;

    public ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default)
    {
        if (renderFrameBatch.Commands.Count == 0)
        {
            lock (_hitTargetsLock)
            {
                _hitTargets = [];
            }

            _retainedFrame.Invalidate();
            LastDirtyCommandRanges = renderFrameBatch.DirtyCommandRanges;
            LastPartialApplySucceeded = false;
            return ValueTask.CompletedTask;
        }

        // Update retained frame: try partial apply first (same resources + dirty ranges),
        // fall back to full apply if resources differ or no dirty ranges.
        if (renderFrameBatch.DirtyCommandRanges.Count > 0)
        {
            LastPartialApplySucceeded = _retainedFrame.TryApplyPartial(renderFrameBatch);
        }
        else
        {
            LastPartialApplySucceeded = false;
        }

        if (!LastPartialApplySucceeded)
        {
            _retainedFrame.ApplyFull(renderFrameBatch);
        }

        LastDirtyCommandRanges = _retainedFrame.DirtyCommandRanges;

        // Execute backend from the retained frame (zero-alloc: no ToBatch copy).
        if (_retainedFrame.TryReadFrame(out var commands, out var resources))
        {
            var frameContext = new FrameContext(0, 0); // Viewport size not needed for PoC backend
            _backend.BeginFrame(frameContext);

            _backend.Execute(commands, resources);

            _backend.EndFrame();
        }

        lock (_hitTargetsLock)
        {
            _hitTargets = [.. _retainedFrame.HitTargets];
        }

        return ValueTask.CompletedTask;
    }

    public bool TryGetActionIdAt(int x, int y, out string actionId)
    {
        lock (_hitTargetsLock)
        {
            foreach (var hitTarget in _hitTargets)
            {
                if (x >= hitTarget.Bounds.X
                    && y >= hitTarget.Bounds.Y
                    && x < hitTarget.Bounds.X + hitTarget.Bounds.Width
                    && y < hitTarget.Bounds.Y + hitTarget.Bounds.Height)
                {
                    actionId = hitTarget.ActionId;
                    return true;
                }
            }
        }

        actionId = string.Empty;
        return false;
    }

    public void Dispose()
    {
        _backend.Dispose();
    }
}
