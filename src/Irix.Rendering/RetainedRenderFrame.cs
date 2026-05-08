using Irix.Drawing;

namespace Irix.Rendering;

/// <summary>
/// A retained render frame that bundles the command buffer, resource resolver,
/// dirty command ranges, and hit targets for backend consumption.
///
/// <para><b>Resource lifecycle:</b></para>
/// <para>
/// <see cref="DrawCommand"/>s may contain <see cref="TextSlice"/> references into
/// <see cref="FrameDrawingResources"/>. These references are only valid while the
/// resources are alive (not returned to the pool). Therefore, <see cref="RetainedRenderFrame"/>
/// is <b>frame-scoped</b>: it must be rebuilt each frame with fresh resources.
/// Partial replacement (<see cref="ApplyPartial"/>) is only valid within the same
/// frame/resource scope — the new batch must be recorded with the same resources.
/// </para>
///
/// <para><b>v0 status:</b> Memory-level validation. Not wired into D3D12 backend.</para>
/// </summary>
internal sealed class RetainedRenderFrame : IDisposable
{
    private readonly RetainedCommandBuffer _commandBuffer = new();
    private IFrameResourceResolver _resources = FrameDrawingResources.Empty;
    private HitTestTarget[] _hitTargets = [];
    private IReadOnlyList<(int Start, int Count)> _dirtyCommandRanges = [];

    /// <summary>The draw commands.</summary>
    public ReadOnlySpan<DrawCommand> Commands => _commandBuffer.Commands;

    /// <summary>Number of commands in the retained buffer.</summary>
    public int CommandCount => _commandBuffer.Count;

    /// <summary>The resource resolver associated with the current frame's commands.</summary>
    public IFrameResourceResolver Resources => _resources;

    /// <summary>Hit targets from the last full apply.</summary>
    public IReadOnlyList<HitTestTarget> HitTargets => _hitTargets;

    /// <summary>Dirty command ranges from the last apply, if any.</summary>
    public IReadOnlyList<(int Start, int Count)> DirtyCommandRanges => _dirtyCommandRanges;

    /// <summary>
    /// Apply a full render frame batch. Replaces all retained state.
    /// Does NOT take resource ownership — callers that need resources to survive
    /// batch disposal must call <see cref="RetainResources"/> after applying.
    /// </summary>
    public void ApplyFull(RenderFrameBatch batch)
    {
        _commandBuffer.ApplyFull(batch.Commands);
        _resources = batch.Resources;
        _hitTargets = [.. batch.HitTargets];
        _dirtyCommandRanges = batch.DirtyCommandRanges;
    }

    /// <summary>
    /// Retain the current resources, preventing <see cref="FrameDrawingResources.Return"/>
    /// from returning them to the pool while this frame still holds TextSlice references.
    /// Call this after <see cref="ApplyFull"/> when the retained frame must outlive the batch.
    /// </summary>
    public void RetainResources()
    {
        if (_resources is FrameDrawingResources fdr)
        {
            fdr.Retain();
        }
    }

    /// <summary>
    /// Release any previously retained resources back to the pool.
    /// Safe to call even if resources were not retained (no-op).
    /// </summary>
    public void ReleaseResources()
    {
        if (_resources is FrameDrawingResources fdr)
        {
            fdr.Release();
        }
    }

    /// <summary>
    /// Apply a partial update: replace only the dirty command ranges from the new batch.
    /// Returns <c>true</c> on success. Returns <c>false</c> without side effects when:
    /// the buffer is empty, no dirty ranges are provided, command count differs,
    /// or the batch resources are not the same instance and generation as the current resources.
    ///
    /// <para>
    /// On failure the caller is responsible for fallback (typically <see cref="ReleaseResources"/> +
    /// <see cref="ApplyFull"/> + <see cref="RetainResources"/>). This method never mutates
    /// retained state on the failure path.
    /// </para>
    /// </summary>
    public bool TryApplyPartial(RenderFrameBatch batch)
    {
        if (_commandBuffer.Count == 0 || batch.DirtyCommandRanges.Count == 0)
        {
            return false;
        }

        // Command count mismatch: partial replace is only valid when the new batch
        // has the same total command count as the retained buffer.
        if (_commandBuffer.Count != batch.Commands.Count)
        {
            return false;
        }

        // Resource identity + generation guard: partial replace is only safe when both
        // batches were recorded with the same FrameDrawingResources instance AND the
        // same rental cycle. A pooled instance re-rented for a different frame will
        // have a different FrameId even though it's the same object.
        if (_resources is not FrameDrawingResources currentFdr
            || batch.Resources is not FrameDrawingResources batchFdr
            || !ReferenceEquals(currentFdr, batchFdr)
            || currentFdr.FrameId != batchFdr.FrameId)
        {
            return false;
        }

        _commandBuffer.ApplyPartial(batch.Commands, batch.DirtyCommandRanges);
        _hitTargets = [.. batch.HitTargets];
        _dirtyCommandRanges = batch.DirtyCommandRanges;
        return true;
    }

    /// <summary>
    /// Try to read the retained frame data without copying (zero-alloc hot path).
    /// Returns true when the frame has commands, providing span access to the commands
    /// and the associated resource resolver.
    /// </summary>
    public bool TryReadFrame(out ReadOnlySpan<DrawCommand> commands, out IFrameResourceResolver resources)
    {
        if (_commandBuffer.Count == 0)
        {
            commands = default;
            resources = FrameDrawingResources.Empty;
            return false;
        }

        commands = _commandBuffer.Commands;
        resources = _resources;
        return true;
    }

    /// <summary>
    /// Create a <see cref="RenderFrameBatch"/> snapshot from the retained state.
    /// The returned batch shares the same resources reference; since the retained frame
    /// owns the resources (retained), the batch's <c>Dispose()</c> will be a no-op
    /// for resource return.
    /// </summary>
    public RenderFrameBatch ToBatch()
    {
        var owner = new ArrayMemoryOwner<DrawCommand>(_commandBuffer.Commands.ToArray());
        return new RenderFrameBatch(
            new DrawCommandBatch(owner, _commandBuffer.Count),
            _hitTargets,
            _resources,
            _dirtyCommandRanges);
    }

    /// <summary>
    /// Reset the retained frame. Callers that retained resources via <see cref="RetainResources"/>
    /// must call <see cref="ReleaseResources"/> before or after invalidation.
    /// </summary>
    public void Invalidate()
    {
        _commandBuffer.Reset();
        _resources = FrameDrawingResources.Empty;
        _hitTargets = [];
        _dirtyCommandRanges = [];
    }

    public void Dispose()
    {
        _commandBuffer.Dispose();
        ReleaseResources();
        _resources = FrameDrawingResources.Empty;
        _hitTargets = [];
        _dirtyCommandRanges = [];
    }
}
