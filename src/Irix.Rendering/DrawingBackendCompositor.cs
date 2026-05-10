using System.Threading;
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
    private ulong _lastAppliedFrameId;
    private long _renderCount;
    private long _partialApplyCount;
    private long _fullApplyCount;
    private long _emptyFrameCount;

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

    /// <summary>Total non-empty frames rendered.</summary>
    public long RenderCount => _renderCount;

    /// <summary>Number of renders that used partial apply.</summary>
    public long PartialApplyCount => _partialApplyCount;

    /// <summary>Number of renders that fell back to full apply.</summary>
    public long FullApplyCount => _fullApplyCount;

    /// <summary>Number of empty frames received (commands.Count == 0).</summary>
    public long EmptyFrameCount => _emptyFrameCount;

    public DrawingBackendClipMode BackendClipMode => _backend is IClipScissorCapability capability
        ? capability.ClipMode
        : DrawingBackendClipMode.None;

    public ValueTask RenderAsync(RenderFrameBatch renderFrameBatch, CancellationToken cancellationToken = default)
    {
        if (renderFrameBatch.Commands.Count == 0)
        {
            lock (_hitTargetsLock)
            {
                _hitTargets = [];
            }

            Interlocked.Increment(ref _emptyFrameCount);
            _retainedFrame.ReleaseResources();
            _retainedFrame.Invalidate();
            _lastAppliedFrameId = 0;
            LastDirtyCommandRanges = renderFrameBatch.DirtyCommandRanges;
            LastPartialApplySucceeded = false;
            return ValueTask.CompletedTask;
        }

        // Cross-frame partial apply guard: only allow partial apply when the batch's
        // resources are the same FrameDrawingResources instance AND same rental cycle
        // (FrameId) as the last apply. This prevents a recycled pooled instance from
        // being misidentified as "same frame scope".
        var batchFrameId = renderFrameBatch.Resources is FrameDrawingResources fdr ? fdr.FrameId : 0ul;
        var isSameFrameScope = batchFrameId != 0 && batchFrameId == _lastAppliedFrameId;

        if (isSameFrameScope && renderFrameBatch.DirtyCommandRanges.Count > 0)
        {
            LastPartialApplySucceeded = _retainedFrame.TryApplyPartial(renderFrameBatch);
        }
        else
        {
            LastPartialApplySucceeded = false;
        }

        if (!LastPartialApplySucceeded)
        {
            // Release old retained resources before taking new ones
            _retainedFrame.ReleaseResources();
            _retainedFrame.ApplyFull(renderFrameBatch);
            // Take ownership: prevent batch.Dispose() from returning resources to pool
            _retainedFrame.RetainResources();
            Interlocked.Increment(ref _fullApplyCount);
        }
        else
        {
            Interlocked.Increment(ref _partialApplyCount);
        }

        Interlocked.Increment(ref _renderCount);
        _lastAppliedFrameId = batchFrameId;
        LastDirtyCommandRanges = _retainedFrame.DirtyCommandRanges;

        // Execute backend from the retained frame (zero-alloc: no ToBatch copy).
        if (_retainedFrame.TryReadFrame(out var commands, out var resources))
        {
            // Propagate dirty ranges to backend for diagnostics (read-only).
            if (_backend is IDirtyRangeAware dirtyAware)
            {
                dirtyAware.SetDirtyCommandRanges(LastDirtyCommandRanges);
            }

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
                // Check bounds
                if (x < hitTarget.Bounds.X
                    || y < hitTarget.Bounds.Y
                    || x >= hitTarget.Bounds.X + hitTarget.Bounds.Width
                    || y >= hitTarget.Bounds.Y + hitTarget.Bounds.Height)
                {
                    continue;
                }

                // Check clip bounds (if set): reject hits outside the clip region
                if (hitTarget.ClipBounds.Width > 0 && hitTarget.ClipBounds.Height > 0)
                {
                    if (x < hitTarget.ClipBounds.X
                        || y < hitTarget.ClipBounds.Y
                        || x >= hitTarget.ClipBounds.X + hitTarget.ClipBounds.Width
                        || y >= hitTarget.ClipBounds.Y + hitTarget.ClipBounds.Height)
                    {
                        continue;
                    }
                }

                actionId = hitTarget.ActionId;
                return true;
            }
        }

        actionId = string.Empty;
        return false;
    }

    public void Dispose()
    {
        _retainedFrame.ReleaseResources();
        _retainedFrame.Dispose();
        _backend.Dispose();
    }
}
