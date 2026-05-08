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

    /// <summary>The retained draw commands.</summary>
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
    /// </summary>
    public void ApplyFull(RenderFrameBatch batch)
    {
        _commandBuffer.ApplyFull(batch.Commands);
        _resources = batch.Resources;
        _hitTargets = [.. batch.HitTargets];
        _dirtyCommandRanges = batch.DirtyCommandRanges;
    }

    /// <summary>
    /// Apply a partial update: replace only the dirty command ranges from the new batch.
    /// The new batch must be recorded with the same <see cref="IFrameResourceResolver"/>
    /// as the current retained frame (same frame scope). Falls back to full replacement
    /// if the buffer is empty, command count differs, no dirty ranges are provided,
    /// or the batch resources are not the same instance as the current resources.
    /// </summary>
    /// <returns>
    /// <c>true</c> if partial apply succeeded; <c>false</c> if fallback to full apply occurred.
    /// </returns>
    public bool TryApplyPartial(RenderFrameBatch batch)
    {
        if (_commandBuffer.Count == 0 || batch.DirtyCommandRanges.Count == 0)
        {
            ApplyFull(batch);
            return false;
        }

        // Resource identity guard: partial replace is only safe when both batches
        // were recorded with the same FrameDrawingResources instance. Different
        // instances mean TextSlice buffer IDs are incompatible — must fallback.
        if (!ReferenceEquals(batch.Resources, _resources))
        {
            ApplyFull(batch);
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
    /// The caller takes ownership of the returned batch's resources.
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
    /// Reset the retained frame. Must be called when the associated
    /// <see cref="FrameDrawingResources"/> is returned to the pool.
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
        _resources = FrameDrawingResources.Empty;
        _hitTargets = [];
        _dirtyCommandRanges = [];
    }
}
